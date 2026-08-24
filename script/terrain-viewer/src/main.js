import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";

/**
 * Leaflet/OpenLayers-style tiled 3D terrain: drapes amc-web's color tile pyramid
 * (out/amc-web/map/tiles/) over a quadtree of terrain patches built from the
 * `heightmap` project's matching height tile pyramid (out/heightmap/tiles/). Both
 * pyramids use the same {z}_{x}_{y} naming and the same zoom-to-resolution scheme (z0 =
 * 1x1 grid, zN = 2^N x 2^N, `2^N * tileSize` total resolution) by construction - see
 * heightmap/ImageWriter.cs's WriteHeightTiles. scripts/prepare-assets.js copies both
 * pyramids as-is into public/assets/tiles/{height,color}/ plus a small tiles.json
 * metadata passthrough - no decoding/resampling in JS at all.
 *
 * Every ~150ms, selectLeafTiles() walks the quadtree from z0 and decides, per node,
 * whether to render it as a leaf (a real GPU tile) or subdivide into its 4 children,
 * from a real screen-space-error budget (the same idea Cesium/OpenLayers-3D/etc. use):
 * a tile's own world size and its 3D distance to the camera are projected through the
 * camera's actual vertical FOV and the renderer's current viewport height into an
 * on-screen pixel size, and it only subdivides once that projected size crosses an
 * effective threshold. That threshold starts at MAX_TILE_SCREEN_PX dead center and
 * grows more lenient toward the edges of the viewport (see CENTER_BIAS_STRENGTH,
 * centerBiasMultiplier()) - a deliberate foveated bias, not a flat FOV-aware ratio
 * alone: reported as tiles "unhelpfully show[ing] smaller zoom off to the side" -
 * i.e. periphery content refining inconsistently relative to what's actually near the
 * camera's gaze, since a flat, position-agnostic threshold gives a screen corner (where
 * detail matters least, and where a tile's true footprint is easiest to underestimate
 * at a shallow/grazing viewing angle) exactly as much refinement priority as dead
 * center. This is deliberately FOV/viewport-aware, not a flat distance-to-tile-size
 * ratio either - the same real-world camera distance covers very different screen area
 * depending on FOV and window size, so a fixed-ratio distance threshold is inherently
 * wrong (too eager to refine on a typical FOV/window), not just mistunable. Reported as
 * "zoom level 5 [renders] from quite far" even after an earlier fix (3D box distance
 * instead of flat horizontal distance) - the residual bug was the ratio-based threshold
 * itself having no real relationship to what's actually on screen. Nodes entirely
 * outside the camera's view frustum are culled outright (not recursed into, not
 * loaded, not rendered) via a THREE.Frustum test against each node's own world-space
 * bounding box.
 */

// Orbiting the same angle sweeps terrain past the camera much faster, visually, when
// zoomed in close than when zoomed far out - scaled by current camera distance every
// frame (see animate()) so close-up orbiting feels controlled instead of dizzying.
const ROTATE_SPEED_NEAR = 0.15; // at controls.minDistance
const ROTATE_SPEED_FAR = 1.0;   // at controls.maxDistance

// World units of camera distance per unit of wheel deltaY - constant regardless of
// current zoom level (unlike OrbitControls' default multiplicative dolly).
const ZOOM_UNITS_PER_WHEEL_DELTA = 5;

// Vertices per tile edge for the actual mesh - independent of the underlying raw height
// data resolution (tileSize, 256 by default): every tile is built at this fixed density
// regardless of zoom, so total scene triangle count is bounded by how many leaf tiles
// the quadtree currently selects, not by the raw data's own resolution.
const MESH_RESOLUTION = 65;

// Maximum on-screen vertical size (px) a tile may reach before subdividing into its 4
// children - see projectedScreenSizePx(). Deliberately more conservative than the old
// flat REFINE_DISTANCE_FACTOR=1.5 ratio (which worked out to roughly 600px at a
// typical ~900px-tall viewport and 55deg FOV) specifically because that ratio was
// reported as still refining to the finest zoom from too far away - this requires a
// tile to visually dominate more of the screen before its children load in.
const MAX_TILE_SCREEN_PX = 900;

// How much more lenient the refine threshold (see MAX_TILE_SCREEN_PX,
// centerBiasMultiplier()) gets for a tile projected at the very edge/corner of the
// viewport versus one dead center - CENTER_BIAS_STRENGTH=1.0 means an edge tile may
// grow to 2x MAX_TILE_SCREEN_PX before subdividing; CENTER_BIAS_POWER=4 keeps that
// leniency concentrated near the true edge instead of a linear ramp starting at the
// center (a linear ramp was tried first and reported as a regression - "z1-z4 zoom
// only show at extreme zoom": even a moderate 20-30% offset from dead center already
// got ~1.5-1.75x leniency, which starves nearly the entire frame of refinement since
// almost no on-screen content sits exactly at NDC (0,0); with power=4 that same
// 20-30% offset is still ~1.0x, unbiased, and the bias only becomes material past
// ~70% of the way to the corner - see centerBiasMultiplier()). Deliberately not
// 0/disabled: this is foveated, not cosmetic - a viewport corner is both where a
// human viewer's attention least needs sharp detail and where the plain
// distance-based screen-size estimate above is easiest to get wrong (a ground tile
// viewed at a shallow/grazing angle near the horizon can occupy far more screen
// pixels than its Euclidean camera distance alone suggests).
const CENTER_BIAS_STRENGTH = 1.0;
const CENTER_BIAS_POWER = 4;

// World-unit drop for the skirt (a thin vertical wall of extra geometry around every
// tile's border, dropped straight down). Neighboring tiles at different zoom levels
// sample the native heightmap at different resolutions, so their shared edge rarely
// lines up vertex-for-vertex - a classic quadtree-terrain crack. The skirt doesn't fix
// the crack geometrically; it hides it behind a wall deep enough that the gap reads as
// a shadowed seam instead of a hole punched through the terrain.
const SKIRT_DROP = 400;

// World-unit side length of the flat ocean quad - deliberately much larger than the
// ~22km map itself so it reads as an endless sea to the horizon rather than a visibly
// bounded tile, out to and past the camera's own far clip plane (100000).
const OCEAN_QUAD_SIZE = 200000;

// Debug view: color each active tile by its own zoom level instead of its real
// texture - lets you see directly whether a given seam sits between two *different*-
// zoom tiles or two *same*-zoom ones, instead of assuming it's an LOD-stitching
// artifact. One distinct, maximally-separated color per zoom (z0..z4) - shaded
// (`MeshStandardMaterial`, same roughness/metalness as the real tile material) rather
// than flat/unlit, so terrain relief and any *normal*-continuity seam (a lighting
// discontinuity, not a position crack) stay visible in this view too, not just the
// zoom-level boundaries themselves. Combine with the wireframe toggle below to also
// see the actual triangle mesh at a boundary - a real geometric gap shows as a break
// in the wireframe grid; a seam with no such break is a shading-only (normal/lighting)
// artifact, not a position crack.
const ZOOM_DEBUG_COLORS = [0xff3b30, 0xff9500, 0xffdd00, 0x34c759, 0x0a84ff];

/**
 * Raw height unit (0-65535, as stored in the height tiles) -> world Z, in meters.
 * Matches `worldZFormulaCm` in Jeju_World.json exactly (that formula in cm, divided by
 * 100 here): `((rawHeight - 32768) / 128.0) * 100.0 / 100.0` simplifies to the form
 * below. Kept as its own small function here (not imported from script/lib/png16.js,
 * which is CommonJS for Node/get-height.js) rather than fighting Vite's bundler over a
 * CJS/ESM interop that isn't needed for one pure, one-line formula.
 */
function rawHeightToWorldZMeters(rawHeight) {
  return (rawHeight - 32768) / 128.0;
}

async function loadTilesMeta() {
  const r = await fetch("/assets/tiles.json");
  if (!r.ok) throw new Error(`tiles.json: HTTP ${r.status}`);
  const meta = await r.json();
  if (meta.dtype !== "uint16" || meta.byteOrder !== "little") {
    throw new Error(`unsupported height tile format: dtype=${meta.dtype} byteOrder=${meta.byteOrder}`);
  }
  return meta;
}

/** World-space rectangle a (z, x, y) tile covers, centered on the map's own center
 * (matching the tile mesh's coordinate convention below). */
function tileWorldRect(meta, z, x, y) {
  const grid = 1 << z;
  const tileSize = meta.widthMeters / grid; // widthMeters === heightMeters (square map)
  const worldX0 = -meta.widthMeters / 2 + x * tileSize;
  const worldZ0 = -meta.heightMeters / 2 + y * tileSize;
  return { worldX0, worldZ0, tileSize };
}

/** Approximate on-screen vertical size, in pixels, that a `tileWorldSize`-tall object
 * at `distance` from the camera would occupy - projected through the camera's actual
 * vertical FOV and the renderer's current viewport height, so refinement decisions
 * scale correctly with both instead of assuming a fixed window size/FOV (see
 * MAX_TILE_SCREEN_PX). */
function projectedScreenSizePx(distance, tileWorldSize, camera, viewportHeightPx) {
  const angularSizeRad = 2 * Math.atan(tileWorldSize / (2 * Math.max(distance, 1e-6)));
  const verticalFovRad = THREE.MathUtils.degToRad(camera.fov);
  return (angularSizeRad / verticalFovRad) * viewportHeightPx;
}

// Scratch objects reused by centerBiasMultiplier() every call - a fresh allocation
// per call (once per visited quadtree node every ~150ms) would just be needless GC
// churn.
const _centerBiasSphere = new THREE.Sphere();
const _centerBiasToCenter = new THREE.Vector3();
const _centerBiasForward = new THREE.Vector3();

/** How much more lenient MAX_TILE_SCREEN_PX should be for `box`, given `camera`'s
 * current pose - see CENTER_BIAS_STRENGTH/CENTER_BIAS_POWER. Measured as an *angle*
 * off the camera's forward view direction to the box's bounding sphere, not an NDC
 * projection of any single representative point (centroid, corner, or closest
 * point) - every one of those was tried first and broke down for large/enclosing
 * boxes in a way this angular measure doesn't:
 * - The centroid gave a real, reproduced bug: looking straight down at the map's own
 *   center, all 4 z1 quadrant tiles have centroids pushed into their own quadrant
 *   (away from center) even though each shares the exact center point as a corner,
 *   so every candidate for refinement got penalized simultaneously and nothing past
 *   z1 could ever refine except at extreme, artificial closeness - reported as
 *   "z1-z4 zoom only show at extreme zoom".
 * - The 8 corners broke down differently: once a box is large enough for the camera
 *   to sit *inside* it (true of the z0 root tile at any reasonable viewing distance),
 *   most or all of its corners project behind the camera (`w <= 0`, unusable), so the
 *   measure degenerated to "no valid corner - assume fully peripheral", applying
 *   *maximum* leniency to exactly the tile that most obviously needs to refine (the
 *   camera standing inside it) - permanently stuck at a single `z0` leaf at every
 *   distance.
 * - The closest point (`Box3.clampPoint`) fixed the inside-the-box case (closest
 *   point coincides with the camera itself there) but broke a different, common case:
 *   for a box that's mostly *below* the camera (the typical relationship between a
 *   near-ground-level camera and the terrain it's flying over), the nearest Euclidean
 *   point is straight down - which can be far off the camera's actual, forward-and-
 *   down *view* direction, so a tile the camera is clearly looking at got penalized
 *   as if it were off to the side, just because "straight down" isn't "straight
 *   ahead".
 * The angular measure sidesteps all three failure modes at once: it asks "how far off
 * my view axis does this tile's silhouette start", using the box's bounding sphere
 * (`Box3.getBoundingSphere`) so the whole tile's angular footprint - not one
 * arbitrarily chosen point on it - is what's being measured against. If the camera is
 * inside the sphere, or the view axis passes through the sphere at all (the angle to
 * the sphere's center is smaller than the sphere's own angular radius as seen from the
 * camera), the bias is exactly 1 - correctly recognizing that the tile's silhouette
 * covers dead center regardless of where its centroid, corners, or nearest surface
 * point happen to sit. Only once the *entire* sphere sits off to one side of the view
 * axis does the leniency ramp up, based on how far past the sphere's own edge that
 * axis has to travel (`edgeAngle`) relative to the camera's diagonal half-FOV (so
 * "reached the corner" lines up with the same semantics used elsewhere: NDC radial
 * distance sqrt(2) at the exact corner, here expressed as the diagonal half-angle
 * instead of a screen-space distance). */
function centerBiasMultiplier(box, camera) {
  box.getBoundingSphere(_centerBiasSphere);
  _centerBiasToCenter.copy(_centerBiasSphere.center).sub(camera.position);
  const dist = _centerBiasToCenter.length();
  if (dist <= _centerBiasSphere.radius) return 1; // camera inside/touching the tile's sphere

  _centerBiasToCenter.divideScalar(dist); // normalize
  camera.getWorldDirection(_centerBiasForward);
  const angleRad = Math.acos(THREE.MathUtils.clamp(_centerBiasToCenter.dot(_centerBiasForward), -1, 1));
  const angularRadius = Math.asin(Math.min(_centerBiasSphere.radius / dist, 1));
  const edgeAngle = Math.max(angleRad - angularRadius, 0); // 0 if the sphere overlaps the view axis at all

  const halfV = THREE.MathUtils.degToRad(camera.fov / 2);
  const halfH = Math.atan(Math.tan(halfV) * camera.aspect);
  const halfDiagonal = Math.atan(Math.hypot(Math.tan(halfV), Math.tan(halfH)));
  const radial = Math.min(edgeAngle / halfDiagonal, 1);
  return 1 + CENTER_BIAS_STRENGTH * Math.pow(radial, CENTER_BIAS_POWER);
}

/** Walks the quadtree from z0, returning the [z, x, y] leaves the camera's current
 * position, view frustum, and viewport call for - see MAX_TILE_SCREEN_PX. A node
 * entirely outside the camera's frustum is skipped outright (not recursed into, not
 * selected as a leaf) - it and everything below it is offscreen, so there's no reason
 * to load or render any of it. The refine distance is the camera's actual 3D distance
 * to the closest point of the tile's bounding box (`THREE.Box3.distanceToPoint`), not a
 * flat horizontal-plane (XZ-only) distance to the tile's center - a horizontal-only
 * metric ignores camera altitude entirely, so looking straight down from very high up
 * (XZ position ~= the tile directly underneath) reads as "distance ~= 0" and
 * force-refines to max zoom regardless of how far out the camera has actually zoomed
 * (confirmed bug, not theoretical: reported as "still selects the deepest zoom fully
 * zoomed out").
 *
 * The bounding box's vertical range is the map's *real* height range (`meta.minZ` /
 * `meta.maxZ`, true 1:1 scale - there is no exaggeration slider to account for
 * anymore) for both the frustum-culling test and the refine-distance calculation. A
 * second, real bug lived here even after the fix above: this box's Y-range used to be
 * padded by a large, fixed "vertical exaggeration safety margin" (`CULL_MAX_EXAGGERATION
 * = 40`) so culling would stay correct if a since-removed exaggeration slider was
 * cranked up later - but that same oversized box was also reused for the
 * refine-distance check, and it was tall enough (thousands of world units) to contain
 * the camera's own altitude in almost any normal viewing position. That silently
 * collapsed `box.distanceToPoint`'s vertical component back down to 0, reproducing the
 * exact "ignores camera altitude" bug the 3D-box-distance fix above was meant to solve
 * - just through a new code path. Reported as "zoom level 5 still render from pretty
 * far away" even after that fix, and confirmed with the color-by-zoom debug view
 * showing the finest zoom selected directly under a camera that was clearly still high
 * above the terrain. Removing the exaggeration slider entirely also removes the reason
 * that safety margin existed - the box can now just be the map's real height range,
 * which is naturally tight enough to make the vertical distance term meaningful again. */
function selectLeafTiles(camera, meta, viewportHeightPx) {
  camera.updateMatrixWorld(); // ensures matrixWorldInverse below reflects this frame's pose
  const viewProjMatrix = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
  const frustum = new THREE.Frustum().setFromProjectionMatrix(viewProjMatrix);
  const minY = Math.min(meta.minZ, meta.maxZ, 0);
  const maxY = Math.max(meta.minZ, meta.maxZ, 0);

  const leaves = [];
  const box = new THREE.Box3();
  function visit(z, x, y) {
    const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
    box.min.set(worldX0, minY, worldZ0);
    box.max.set(worldX0 + tileSize, maxY, worldZ0 + tileSize);
    if (!frustum.intersectsBox(box)) return; // fully offscreen - cull, don't recurse or load

    const distance = box.distanceToPoint(camera.position); // full 3D, accounts for camera height
    const screenPx = projectedScreenSizePx(distance, tileSize, camera, viewportHeightPx);
    const maxPx = MAX_TILE_SCREEN_PX * centerBiasMultiplier(box, camera);

    if (z < meta.maxZoom && screenPx > maxPx) {
      visit(z + 1, x * 2, y * 2);
      visit(z + 1, x * 2 + 1, y * 2);
      visit(z + 1, x * 2, y * 2 + 1);
      visit(z + 1, x * 2 + 1, y * 2 + 1);
    } else {
      leaves.push([z, x, y]);
    }
  }
  visit(0, 0, 0);
  return leaves;
}

/** Fetches one height tile's raw uint16 samples (tileSampleCount x tileSampleCount,
 * row-major - see meta.tileSampleCount/tileSampleInnerCount in tiles.json). */
async function fetchHeightTile(z, x, y) {
  const r = await fetch(`/assets/tiles/height/${z}_${x}_${y}.bin`);
  if (!r.ok) throw new Error(`height tile ${z}_${x}_${y}: HTTP ${r.status}`);
  return new Uint16Array(await r.arrayBuffer());
}

/** Composites the four z1 color tiles into one canvas texture, used in place of z0's
 * own texture whenever z0 is actually selected as a leaf by `selectLeafTiles()`
 * (see `loadColorTexture()`). z0's own color tile is the *entire* ~22km map resized
 * down to one `tileSize x tileSize` image (256x256 by default, ~86m/pixel) - visibly
 * blurry well before the height *geometry* would need refining, since z0 can
 * legitimately stay a leaf from far away or directly overhead. Reported directly:
 * "z0 tile texture is too blurry, do stitched z1" - then refined further: "I want it
 * to do z0 height map to reduce triangle at min zoom, just don't use z0 texture" -
 * i.e. keep z0's single, cheap mesh (one `MESH_RESOLUTION x MESH_RESOLUTION` tile,
 * not four) rather than forcing a geometry split into 4 z1 tiles, but texture that
 * one mesh with the four real z1 images instead of z0's own. This reconstructs
 * exactly the full-map image `amc-web/TileGenerator.cs` cropped those four tiles from
 * in the first place (mod AVIF's own lossy encoding) - 2x the linear resolution (4x
 * the pixels) of z0's own texture, for the exact same ground footprint, at no
 * geometry/triangle cost at all.
 *
 * Pure canvas 2D compositing, not a WebGL render-to-texture pass: four decoded `Image`
 * elements drawn into the four quadrants of one canvas, matching the mesh's own
 * coordinate convention - quadrant `(x, y)` (the same z1 tile indices used everywhere
 * else) goes at canvas pixel offset `(x * tileW, y * tileH)`: `x` increasing
 * left-to-right matches `u`/`worldX` increasing, `y` increasing top-to-bottom matches
 * `v`/`worldZ` increasing - the same top-down row convention `flipY = false` relies on
 * for every other tile's texture. Canvas size is derived from the loaded images'
 * own natural dimensions, not assumed from `meta.tileSize` (that field describes the
 * *height* tile pyramid - independent of amc-web's own color tile size by design, even
 * though both happen to default to 256). */
function loadZ0StitchedTexture(renderer) {
  const quadrants = [
    ["/assets/tiles/color/1_0_0.avif", 0, 0],
    ["/assets/tiles/color/1_1_0.avif", 1, 0],
    ["/assets/tiles/color/1_0_1.avif", 0, 1],
    ["/assets/tiles/color/1_1_1.avif", 1, 1],
  ];
  const loads = quadrants.map(([url, qx, qy]) => new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve({ img, qx, qy });
    img.onerror = (err) => reject(new Error(`stitched z0 texture: ${url} failed to load: ${err?.message ?? err}`));
    img.src = url;
  }));
  return Promise.all(loads).then((loaded) => {
    const tileW = loaded[0].img.naturalWidth;
    const tileH = loaded[0].img.naturalHeight;
    const canvas = document.createElement("canvas");
    canvas.width = tileW * 2;
    canvas.height = tileH * 2;
    const ctx = canvas.getContext("2d");
    for (const { img, qx, qy } of loaded) {
      ctx.drawImage(img, qx * tileW, qy * tileH, tileW, tileH);
    }
    const texture = new THREE.CanvasTexture(canvas);
    texture.flipY = false;
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
    return texture;
  });
}

/** Loads one color tile's texture, resolving only once the image has actually
 * finished decoding - not the moment `TextureLoader.load()` returns (which happens
 * immediately, well before the network fetch + image decode complete, since
 * `THREE.TextureLoader` loads asynchronously in the background). `loadTile()` awaits
 * this before ever adding the tile's mesh to the scene, so a tile is either fully
 * textured or not present at all - never rendered with the still-blank/default
 * texture partway through loading, which read as a "weird dark patch" flash on every
 * newly-loaded tile (most visible right after an LOD transition, when several
 * sibling tiles all start loading their own textures at once) even after the
 * load/unload-ordering fix above solved the geometry gap.
 *
 * z0 is special-cased to `loadZ0StitchedTexture()` instead of its own `0_0_0.avif` -
 * see that function's doc comment.
 *
 * Same flipY fix as the old single-mesh viewer: this mesh's UVs are assigned by hand
 * to directly match the tile's own un-flipped row->worldY mapping, but TextureLoader
 * defaults flipY=true (assumes a plane's default UVs, v=0 at bottom) - disable it so
 * texture and geometry read the tile in the same top-down row order. */
function loadColorTexture(z, x, y, renderer) {
  if (z === 0) return loadZ0StitchedTexture(renderer);
  return new Promise((resolve, reject) => {
    new THREE.TextureLoader().load(
      `/assets/tiles/color/${z}_${x}_${y}.avif`,
      (texture) => {
        texture.flipY = false;
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
        resolve(texture);
      },
      undefined,
      (err) => reject(new Error(`color tile ${z}_${x}_${y}.avif failed to load: ${err?.message ?? err}`))
    );
  });
}

/**
 * Builds one tile's mesh geometry: a MESH_RESOLUTION x MESH_RESOLUTION grid sampled from
 * the tile's raw height data, plus a skirt border (see SKIRT_DROP) to hide LOD-boundary
 * cracks. Heights render at true 1:1 world scale - there is no exaggeration factor
 * anywhere in this pipeline.
 *
 * Normals are computed analytically from the height field via central finite
 * differences (`-dh/dx, 1, -dh/dz`, matching the mesh's own winding - verified against
 * the actual triangle order below, not assumed), not `BufferGeometry.
 * computeVertexNormals()`. That distinction matters at tile boundaries specifically: a
 * boundary vertex's face-normal average only ever sees *this tile's own* triangles, so
 * it's systematically skewed toward this tile's interior - the neighbouring tile's copy
 * of that same world vertex gets its normal skewed the *other* way, and two different
 * normals at a shared point reads as a lighting-discontinuity seam even when the
 * position data matches exactly (a real, separate bug from the plain 1px position
 * overlap - reported as "seam is still a problem" after that fix alone). The analytic
 * gradient instead uses `rawHeights`' 1px "normal halo" (`tileDataSize` = `tileSize+3`,
 * see heightmap/ImageWriter.cs's WriteHeightTiles) - one real sample beyond each tile
 * edge - so a central difference at a boundary vertex is always well-defined from data
 * this tile alone stores, and because both tiles derive that halo sample from the exact
 * same deterministic area-average of the same canvas pixel, the two independently
 * computed edge normals come out bit-identical.
 */
function buildTileGeometry(rawHeights, innerSize, haloSize, worldX0, worldZ0, tileWorldSize) {
  const N = MESH_RESOLUTION;
  const mainCount = N * N;
  const skirtCount = 4 * N;
  const total = mainCount + skirtCount;

  const positions = new Float32Array(total * 3);
  const normals = new Float32Array(total * 3);
  const uvs = new Float32Array(total * 2);
  const baseGradX = new Float32Array(total); // d(height meters)/d(world x)
  const baseGradZ = new Float32Array(total); // d(height meters)/d(world z)

  // World-unit spacing between adjacent halo-array samples (main-grid samples are a
  // subset of this same spacing, so this is also the right step for the gradient).
  const sampleSpacing = tileWorldSize / (innerSize - 1);

  // Height in meters at a halo-array index pair. hx/hy in [0, haloSize-1]; every call
  // below stays in range because the loop only ever asks for hx +/- 1 where hx itself
  // ranges over [1, innerSize] (the halo's inner region).
  function heightAtHalo(hx, hy) {
    return rawHeightToWorldZMeters(rawHeights[hy * haloSize + hx]);
  }

  for (let row = 0; row < N; row++) {
    const v = row / (N - 1);
    const hy = Math.round(v * (innerSize - 1)) + 1; // +1 shifts into the halo-padded index space
    for (let col = 0; col < N; col++) {
      const u = col / (N - 1);
      const hx = Math.round(u * (innerSize - 1)) + 1;
      const idx = row * N + col;

      const y = heightAtHalo(hx, hy);
      positions[idx * 3 + 0] = worldX0 + u * tileWorldSize;
      positions[idx * 3 + 1] = y;
      positions[idx * 3 + 2] = worldZ0 + v * tileWorldSize;
      baseGradX[idx] = (heightAtHalo(hx + 1, hy) - heightAtHalo(hx - 1, hy)) / (2 * sampleSpacing);
      baseGradZ[idx] = (heightAtHalo(hx, hy + 1) - heightAtHalo(hx, hy - 1)) / (2 * sampleSpacing);
      uvs[idx * 2 + 0] = u;
      uvs[idx * 2 + 1] = v;
    }
  }

  // Skirt vertices: one per edge vertex, same X/Z, dropped Y - order: top, bottom, left,
  // right, each walked in increasing col/row so consecutive skirt indices are adjacent
  // along the edge (needed for the wall triangles below).
  const edges = [
    { base: mainCount + 0 * N, mainIndices: Array.from({ length: N }, (_, col) => col) },              // top (row 0)
    { base: mainCount + 1 * N, mainIndices: Array.from({ length: N }, (_, col) => (N - 1) * N + col) }, // bottom
    { base: mainCount + 2 * N, mainIndices: Array.from({ length: N }, (_, row) => row * N) },           // left (col 0)
    { base: mainCount + 3 * N, mainIndices: Array.from({ length: N }, (_, row) => row * N + (N - 1)) }, // right
  ];
  for (const edge of edges) {
    edge.mainIndices.forEach((mainIdx, i) => {
      const sIdx = edge.base + i;
      positions[sIdx * 3 + 0] = positions[mainIdx * 3 + 0];
      positions[sIdx * 3 + 1] = positions[mainIdx * 3 + 1] - SKIRT_DROP;
      positions[sIdx * 3 + 2] = positions[mainIdx * 3 + 2];
      baseGradX[sIdx] = baseGradX[mainIdx];
      baseGradZ[sIdx] = baseGradZ[mainIdx];
      uvs[sIdx * 2 + 0] = uvs[mainIdx * 2 + 0];
      uvs[sIdx * 2 + 1] = uvs[mainIdx * 2 + 1];
    });
  }

  const indices = [];
  for (let row = 0; row < N - 1; row++) {
    for (let col = 0; col < N - 1; col++) {
      const a = row * N + col, b = a + 1, c = a + N, d = c + 1;
      indices.push(a, c, b, b, c, d);
    }
  }
  // Wall triangles connecting each edge's main vertices down to their skirt mirrors.
  // Winding isn't rigorously derived per edge (four different orientations) - the tile
  // material below is double-sided specifically so an inverted wall triangle still
  // renders instead of being backface-culled into a visible gap.
  for (const edge of edges) {
    for (let i = 0; i < edge.mainIndices.length - 1; i++) {
      const m0 = edge.mainIndices[i], m1 = edge.mainIndices[i + 1];
      const s0 = edge.base + i, s1 = edge.base + i + 1;
      indices.push(m0, s0, m1, m1, s0, s1);
    }
  }

  // -dh/dx, 1, -dh/dz, matching the mesh's own winding (see this function's doc
  // comment) - computed once here since the geometry never changes after this.
  for (let i = 0; i < baseGradX.length; i++) {
    const nx = -baseGradX[i], nz = -baseGradZ[i];
    const len = Math.hypot(nx, 1, nz);
    normals[i * 3 + 0] = nx / len;
    normals[i * 3 + 1] = 1 / len;
    normals[i * 3 + 2] = nz / len;
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
  geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
  geometry.setIndex(indices);

  return { geometry };
}

async function main() {
  const container = document.getElementById("app");

  // logarithmicDepthBuffer: true - this scene mixes tiny (skirt/seam-scale) and huge
  // (map/far-clip-scale) depths in one standard depth buffer; without it, two nearly
  // coplanar surfaces far from the camera (most visibly the flat ocean quad against
  // gently-sloping shoreline terrain near the water's edge) z-fight heavily because a
  // linear depth buffer has almost no precision left out there. Reported as "heavy
  // ocean z-fighting" - this is the standard, well-documented Three.js fix for exactly
  // that failure mode, not a targeted hack for this one surface pair.
  const renderer = new THREE.WebGLRenderer({ antialias: true, logarithmicDepthBuffer: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  const skyColor = 0x8fb8e0;
  renderer.setClearColor(skyColor);
  container.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(skyColor);
  scene.fog = new THREE.Fog(skyColor, 25000, 100000);

  // near=5, not 1: also reduces the near/far ratio (5:100000 instead of 1:100000),
  // compounding with logarithmicDepthBuffer above rather than fighting it - nothing in
  // this scene needs to render closer than a few meters from the camera anyway.
  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 5, 100000);
  camera.position.set(0, 6000, 9000);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.minDistance = 30;
  controls.maxDistance = 55000;
  // Traditional-game-style bindings: left-drag pans (custom, ground-anchored - see below),
  // right-drag orbits, wheel zooms. OrbitControls' own pan is screen-space only (not
  // ground-anchored), so it's disabled in favor of the raycast-based pan further down.
  controls.mouseButtons = { LEFT: null, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
  controls.enablePan = false;
  // OrbitControls' own wheel zoom is multiplicative (percentage-of-distance per tick) -
  // fast when far out, barely perceptible when close in. Replaced below with a constant
  // linear step per wheel tick, independent of current distance.
  controls.enableZoom = false;

  scene.add(new THREE.HemisphereLight(0xbfe0ff, 0x4a3d2a, 1.4));
  scene.add(new THREE.AmbientLight(0xffffff, 0.6));
  const sun = new THREE.DirectionalLight(0xfff3df, 2.4);
  sun.position.set(-8000, 12000, 6000);
  scene.add(sun);
  scene.add(sun.target); // stays at the default (0,0,0), which is where the map is centered

  const meta = await loadTilesMeta();

  // Flat ocean quad at the pak's own ocean level (see OceanExtractor.cs /
  // Jeju_World.json's "ocean" section) - a huge horizontal plane at the map's true,
  // unexaggerated ocean level. Not added to tileGroup: it must never be a
  // ground-anchored-pan raycast target, or panning would grab the ocean surface
  // instead of the real terrain underneath it.
  let oceanMesh = null;
  if (meta.oceanLevelMeters != null) {
    const oceanGeometry = new THREE.PlaneGeometry(OCEAN_QUAD_SIZE, OCEAN_QUAD_SIZE);
    oceanGeometry.rotateX(-Math.PI / 2); // PlaneGeometry is XY-facing by default; lay it flat (XZ)
    // Lower opacity + lighter, less saturated color than the first version (was
    // color 0x1c5f8a, opacity 0.85, which read as opaque/murky) - reported as "make
    // water clearer". Known limitation, not fixed by this: any road that's a real
    // elevated bridge in-game has no separate bridge-deck geometry here (only the
    // landscape heightmap + this flat sea-level quad), so it's draped on the
    // underlying (submerged) terrain and will still show through the water surface
    // regardless of clarity - see script/terrain-viewer/README.md's "Ocean quad"
    // section.
    const oceanMaterial = new THREE.MeshStandardMaterial({
      color: 0x2f7fa8, roughness: 0.2, metalness: 0.05,
      transparent: true, opacity: 0.15, side: THREE.DoubleSide,
    });
    oceanMesh = new THREE.Mesh(oceanGeometry, oceanMaterial);
    oceanMesh.position.y = meta.oceanLevelMeters;
    scene.add(oceanMesh);
  } else {
    console.warn("tiles.json has no oceanLevelMeters - skipping the ocean quad");
  }

  // All active tile meshes live under this group - the raycast target for
  // ground-anchored panning below.
  const tileGroup = new THREE.Group();
  scene.add(tileGroup);

  // Debug view state - see ZOOM_DEBUG_COLORS' doc comment. debugMaterials is built
  // once (5 entries, one per zoom, shared by every tile at that zoom - no need for a
  // per-tile copy since they carry no texture) and reused by every tile that has ever
  // shown the debug view; wireframeEnabled applies independently, to whichever
  // material (real or debug) is currently in use.
  const debugMaterials = ZOOM_DEBUG_COLORS.map(
    (color) => new THREE.MeshStandardMaterial({ color, roughness: 0.95, metalness: 0, wireframe: false })
  );
  let showZoomDebug = false;
  let wireframeEnabled = false;

  const activeTiles = new Map();  // "z_x_y" -> { mesh, geometry, material, texture, z }
  const heightCache = new Map();  // "z_x_y" -> Promise<Uint16Array>
  const pending = new Set();      // "z_x_y" currently being loaded

  function fetchHeightCached(z, x, y) {
    const key = `${z}_${x}_${y}`;
    if (!heightCache.has(key)) heightCache.set(key, fetchHeightTile(z, x, y));
    return heightCache.get(key);
  }

  async function loadTile(z, x, y) {
    const key = `${z}_${x}_${y}`;
    pending.add(key);
    try {
      // Fetched/decoded in parallel, but the mesh below isn't built or added to the
      // scene until *both* resolve - see loadColorTexture()'s doc comment for why:
      // a mesh added with a still-decoding texture renders as a dark/blank patch
      // until the image finishes, which is a large chunk of the observable "flash".
      const [rawHeights, texture] = await Promise.all([
        fetchHeightCached(z, x, y),
        loadColorTexture(z, x, y, renderer),
      ]);
      const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
      const { geometry } = buildTileGeometry(
        rawHeights, meta.tileSampleInnerCount, meta.tileSampleCount, worldX0, worldZ0, tileSize
      );
      const material = new THREE.MeshStandardMaterial({
        map: texture, roughness: 0.95, metalness: 0, side: THREE.DoubleSide, wireframe: wireframeEnabled,
      });
      const mesh = new THREE.Mesh(geometry, showZoomDebug ? debugMaterials[z] : material);
      tileGroup.add(mesh);
      activeTiles.set(key, { mesh, geometry, material, texture, z });
    } finally {
      pending.delete(key);
    }
  }

  function unloadTile(key) {
    const tile = activeTiles.get(key);
    if (!tile) return;
    tileGroup.remove(tile.mesh);
    tile.geometry.dispose();
    tile.material.dispose();
    tile.texture.dispose();
    activeTiles.delete(key);
  }

  function updateVisibleTiles() {
    const desired = selectLeafTiles(camera, meta, renderer.domElement.clientHeight);
    const desiredKeys = new Set(desired.map(([z, x, y]) => `${z}_${x}_${y}`));

    for (const [z, x, y] of desired) {
      const key = `${z}_${x}_${y}`;
      if (!activeTiles.has(key) && !pending.has(key)) {
        loadTile(z, x, y).catch((err) => console.error(`tile ${key} failed to load:`, err));
      }
    }

    // Only unload stale tiles once every desired tile is actually active (loaded, not
    // merely pending) - otherwise a tile gets removed the instant it's no longer
    // desired, but its replacement (a fetch + texture decode + geometry build) hasn't
    // finished yet, leaving a visible gap/flash for however long that takes. Briefly
    // rendering both the old and new tiles overlapped is a strictly better tradeoff
    // than a hole where terrain used to be.
    const replacementReady = desired.every(([z, x, y]) => activeTiles.has(`${z}_${x}_${y}`));
    if (replacementReady) {
      for (const key of [...activeTiles.keys()]) {
        if (!desiredKeys.has(key)) unloadTile(key);
      }
    }
  }

  controls.target.set(0, 0, 0);
  controls.update();
  updateVisibleTiles();

  // Ground-anchored pan (left-drag): the terrain point grabbed on mousedown stays under
  // the cursor for the whole drag, like Google Earth / most CAD tools - not the same as
  // OrbitControls' built-in pan, which just translates the camera in its own screen plane
  // regardless of what's actually under the cursor.
  const raycaster = new THREE.Raycaster();
  const pointerNDC = new THREE.Vector2();
  const panPlane = new THREE.Plane();
  const grabbedPoint = new THREE.Vector3();
  const currentPoint = new THREE.Vector3();
  let panning = false;

  function setPointerNDC(event) {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  renderer.domElement.addEventListener("contextmenu", (event) => event.preventDefault());

  renderer.domElement.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return; // left button grabs; right button orbits via OrbitControls
    setPointerNDC(event);
    raycaster.setFromCamera(pointerNDC, camera);
    const hits = raycaster.intersectObjects(tileGroup.children, false);
    if (hits.length === 0) return; // clicked off the terrain (open sky, or no tile loaded there yet)
    grabbedPoint.copy(hits[0].point);
    panPlane.setFromNormalAndCoplanarPoint(new THREE.Vector3(0, 1, 0), grabbedPoint);
    panning = true;
    renderer.domElement.setPointerCapture(event.pointerId);
  });

  renderer.domElement.addEventListener("pointermove", (event) => {
    if (!panning) return;
    setPointerNDC(event);
    raycaster.setFromCamera(pointerNDC, camera);
    if (!raycaster.ray.intersectPlane(panPlane, currentPoint)) return;
    // Shift camera + target by exactly what's needed to bring the grabbed point back
    // under the (moved) cursor - re-derived every move from the live camera pose, so it
    // stays exact for the whole drag rather than drifting.
    const delta = grabbedPoint.clone().sub(currentPoint);
    camera.position.add(delta);
    controls.target.add(delta);
  });

  function endPan(event) {
    if (!panning) return;
    panning = false;
    if (renderer.domElement.hasPointerCapture(event.pointerId)) {
      renderer.domElement.releasePointerCapture(event.pointerId);
    }
  }
  renderer.domElement.addEventListener("pointerup", endPan);
  renderer.domElement.addEventListener("pointercancel", endPan);

  // Linear-velocity zoom: each wheel tick moves the camera a fixed distance along the
  // camera-target line, clamped to [minDistance, maxDistance] - replaces OrbitControls'
  // disabled built-in dolly (see controls.enableZoom = false above).
  renderer.domElement.addEventListener(
    "wheel",
    (event) => {
      event.preventDefault();
      const offset = camera.position.clone().sub(controls.target);
      const distance = offset.length();
      const newDistance = THREE.MathUtils.clamp(
        distance + event.deltaY * ZOOM_UNITS_PER_WHEEL_DELTA,
        controls.minDistance,
        controls.maxDistance
      );
      offset.setLength(newDistance);
      camera.position.copy(controls.target).add(offset);
    },
    { passive: false }
  );

  // Debug controls - see ZOOM_DEBUG_COLORS' doc comment. Independent toggles: zoom
  // color replaces the real texture entirely; wireframe applies on top of whichever
  // material (real or debug-colored) is currently showing.
  const debugPanel = document.createElement("div");
  Object.assign(debugPanel.style, {
    position: "fixed", top: "8px", right: "8px", zIndex: 10,
    font: "12px monospace", color: "#cfe3ff", background: "rgba(0,0,0,0.35)",
    padding: "6px 8px", borderRadius: "4px",
  });
  document.body.appendChild(debugPanel);

  function addDebugToggle(labelText, onChange) {
    const row = document.createElement("label");
    Object.assign(row.style, { display: "block", cursor: "pointer" });
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.addEventListener("change", () => onChange(checkbox.checked));
    row.appendChild(checkbox);
    row.appendChild(document.createTextNode(" " + labelText));
    debugPanel.appendChild(row);
  }

  addDebugToggle("debug: color by zoom", (checked) => {
    showZoomDebug = checked;
    for (const tile of activeTiles.values()) {
      tile.mesh.material = showZoomDebug ? debugMaterials[tile.z] : tile.material;
    }
  });
  addDebugToggle("wireframe", (checked) => {
    wireframeEnabled = checked;
    for (const material of debugMaterials) material.wireframe = wireframeEnabled;
    for (const tile of activeTiles.values()) tile.material.wireframe = wireframeEnabled;
  });

  const tileInfo = document.createElement("div");
  Object.assign(tileInfo.style, {
    position: "fixed", bottom: "12px", right: "8px", zIndex: 10,
    font: "12px monospace", color: "#cfe3ff", textAlign: "right",
  });
  document.body.appendChild(tileInfo);

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  let lastTileUpdate = 0;
  const TILE_UPDATE_INTERVAL_MS = 150; // throttled - the quadtree walk is cheap, but tile
                                        // load/unload churn on every single frame is not

  function animate(now) {
    requestAnimationFrame(animate);
    const distance = camera.position.distanceTo(controls.target);
    const t = THREE.MathUtils.clamp(
      (distance - controls.minDistance) / (controls.maxDistance - controls.minDistance), 0, 1
    );
    controls.rotateSpeed = THREE.MathUtils.lerp(ROTATE_SPEED_NEAR, ROTATE_SPEED_FAR, t);
    controls.update();

    if (now - lastTileUpdate > TILE_UPDATE_INTERVAL_MS) {
      lastTileUpdate = now;
      updateVisibleTiles();
    }

    renderer.render(scene, camera);
    // renderer.info.render.triangles is reset and recomputed by render() every frame
    // (not throttled alongside updateVisibleTiles() above) - actual GPU-submitted
    // triangle count for everything drawn this frame (tiles + skirts + the ocean
    // quad), not just a per-tile estimate multiplied out by hand.
    tileInfo.textContent = `${activeTiles.size} tiles, ${renderer.info.render.triangles.toLocaleString()} tris`;
  }
  animate(0);
}

main().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="color:#f88;padding:16px;font:14px monospace">${err.stack || err.message}</pre>`;
});
