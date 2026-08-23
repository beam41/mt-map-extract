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
 * Every frame, selectLeafTiles() walks the quadtree from z0 and decides, per node,
 * whether to render it as a leaf (a real GPU tile) or subdivide into its 4 children,
 * purely from distance-to-camera vs. the tile's own world size - the same idea Cesium/
 * OpenLayers-3D/etc. use, simplified to a single distance ratio since this viewer has no
 * true screen-space-error budget or camera-FOV-aware projection. Closer tiles end up
 * smaller/higher-zoom; distant tiles stay large/coarse.
 */

// Real elevation differences (tens of meters) are imperceptible against a 22km-wide map
// at true 1:1 scale - this is a deliberate, visible artistic exaggeration, not a
// measurement. Adjustable live via the on-screen slider.
const DEFAULT_EXAGGERATION = 6;

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

// A tile subdivides into its 4 children once the camera gets closer than its own world
// size times this factor; otherwise it renders as a single leaf tile. Larger = coarser
// terrain closer to the camera (fewer, bigger tiles); smaller = finer detail nearer the
// camera (more, smaller tiles). Tuned by eye, not derived from a real screen-space-error
// budget - this viewer has no per-frame projected-size calculation.
const REFINE_DISTANCE_FACTOR = 1.5;

// Post-exaggeration world-unit drop for the skirt (a thin vertical wall of extra
// geometry around every tile's border, dropped straight down). Neighboring tiles at
// different zoom levels sample the native heightmap at different resolutions, so their
// shared edge rarely lines up vertex-for-vertex - a classic quadtree-terrain crack. The
// skirt doesn't fix the crack geometrically; it hides it behind a wall deep enough that
// the gap reads as a shadowed seam instead of a hole punched through the terrain.
const SKIRT_DROP = 400;

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

/** Walks the quadtree from z0, returning the [z, x, y] leaves the camera's current
 * position calls for - see REFINE_DISTANCE_FACTOR. */
function selectLeafTiles(camera, meta) {
  const leaves = [];
  function visit(z, x, y) {
    const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
    const cx = worldX0 + tileSize / 2;
    const cz = worldZ0 + tileSize / 2;
    const dx = camera.position.x - cx;
    const dz = camera.position.z - cz;
    const distance = Math.sqrt(dx * dx + dz * dz);

    if (z < meta.maxZoom && distance < tileSize * REFINE_DISTANCE_FACTOR) {
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

/** Fetches one height tile's raw uint16 samples (tileSize x tileSize, row-major). */
async function fetchHeightTile(z, x, y) {
  const r = await fetch(`/assets/tiles/height/${z}_${x}_${y}.bin`);
  if (!r.ok) throw new Error(`height tile ${z}_${x}_${y}: HTTP ${r.status}`);
  return new Uint16Array(await r.arrayBuffer());
}

/** Loads one color tile's texture. Same flipY fix as the old single-mesh viewer: this
 * mesh's UVs are assigned by hand to directly match the tile's own un-flipped
 * row->worldY mapping, but TextureLoader defaults flipY=true (assumes a plane's default
 * UVs, v=0 at bottom) - disable it so texture and geometry read the tile in the same
 * top-down row order. */
function loadColorTexture(z, x, y, renderer) {
  const texture = new THREE.TextureLoader().load(`/assets/tiles/color/${z}_${x}_${y}.avif`);
  texture.flipY = false;
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
  return texture;
}

/**
 * Builds one tile's mesh geometry: a MESH_RESOLUTION x MESH_RESOLUTION grid sampled from
 * the tile's raw height data, plus a skirt border (see SKIRT_DROP) to hide LOD-boundary
 * cracks. Returns per-vertex bookkeeping (baseHeights in meters, skirtDrop) so the
 * exaggeration slider can recompute every active tile's Y coordinates live without
 * re-fetching or re-sampling anything.
 */
function buildTileGeometry(rawHeights, tileDataSize, worldX0, worldZ0, tileWorldSize, exaggeration) {
  const N = MESH_RESOLUTION;
  const mainCount = N * N;
  const skirtCount = 4 * N;
  const total = mainCount + skirtCount;

  const positions = new Float32Array(total * 3);
  const uvs = new Float32Array(total * 2);
  const baseHeights = new Float32Array(total); // meters, pre-exaggeration
  const skirtDrop = new Float32Array(total);   // 0 for main vertices, SKIRT_DROP for skirt

  function sampleRaw(u, v) {
    const rx = Math.min(tileDataSize - 1, Math.round(u * (tileDataSize - 1)));
    const ry = Math.min(tileDataSize - 1, Math.round(v * (tileDataSize - 1)));
    return rawHeights[ry * tileDataSize + rx];
  }

  for (let row = 0; row < N; row++) {
    const v = row / (N - 1);
    for (let col = 0; col < N; col++) {
      const u = col / (N - 1);
      const idx = row * N + col;
      const y = rawHeightToWorldZMeters(sampleRaw(u, v));
      positions[idx * 3 + 0] = worldX0 + u * tileWorldSize;
      positions[idx * 3 + 1] = y * exaggeration;
      positions[idx * 3 + 2] = worldZ0 + v * tileWorldSize;
      baseHeights[idx] = y;
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
      baseHeights[sIdx] = baseHeights[mainIdx];
      skirtDrop[sIdx] = SKIRT_DROP;
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

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();

  return { geometry, baseHeights, skirtDrop };
}

/** Applies the current exaggeration to a tile's already-built geometry in place (no
 * re-fetch, no re-sampling) - called for every active tile whenever the slider moves. */
function applyExaggeration(geometry, baseHeights, skirtDrop, exaggeration) {
  const positionAttr = geometry.getAttribute("position");
  for (let i = 0; i < baseHeights.length; i++) {
    positionAttr.setY(i, baseHeights[i] * exaggeration - skirtDrop[i]);
  }
  positionAttr.needsUpdate = true;
  geometry.computeVertexNormals();
}

async function main() {
  const container = document.getElementById("app");

  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const skyColor = 0x8fb8e0;
  renderer.setClearColor(skyColor);
  container.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(skyColor);
  scene.fog = new THREE.Fog(skyColor, 25000, 100000);

  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 1, 100000);
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
  const sunDirection = new THREE.Vector3(-8000, 12000, 6000).normalize();
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  // Large terrain, coarse texel size at that map resolution - offset along the surface
  // normal (world units, meters) rather than a flat depth bias, which is far more robust
  // against self-shadowing acne on sloped terrain than a small constant bias.
  sun.shadow.normalBias = 15;
  scene.add(sun);
  scene.add(sun.target); // stays at the default (0,0,0), which is where the map is centered

  const meta = await loadTilesMeta();

  // Size the sun's orthographic shadow frustum to comfortably contain the terrain at
  // any exaggeration up to the slider's max (40x) - a single static frustum, not
  // recomputed per-frame or per-exaggeration-change, so shadow resolution is a fixed
  // (coarse, ~dozens of meters/texel across a 22km map) tradeoff of this single-cascade
  // setup, not a bug.
  const SHADOW_MAX_EXAGGERATION = 40;
  const horizontalRadius = 0.5 * Math.sqrt(meta.widthMeters ** 2 + meta.heightMeters ** 2);
  const verticalHalfRange = Math.max(Math.abs(meta.minZ), Math.abs(meta.maxZ)) * SHADOW_MAX_EXAGGERATION;
  const shadowRadius = horizontalRadius + verticalHalfRange;
  // Place the sun far enough past shadowRadius that its shadow camera's near/far span
  // (centered on the light-to-target distance) stays comfortably positive - too close
  // and the bounding sphere below would swallow the light position itself.
  sun.position.copy(sunDirection).multiplyScalar(shadowRadius * 2);
  const shadowCam = sun.shadow.camera;
  shadowCam.left = -shadowRadius;
  shadowCam.right = shadowRadius;
  shadowCam.top = shadowRadius;
  shadowCam.bottom = -shadowRadius;
  shadowCam.near = sun.position.length() - shadowRadius - 1000;
  shadowCam.far = sun.position.length() + shadowRadius + 1000;
  shadowCam.updateProjectionMatrix();

  // All active tile meshes live under this group - both the shadow-casting/receiving
  // terrain surface and the raycast target for ground-anchored panning below.
  const tileGroup = new THREE.Group();
  scene.add(tileGroup);

  let currentExaggeration = DEFAULT_EXAGGERATION;
  const activeTiles = new Map();  // "z_x_y" -> { mesh, geometry, baseHeights, skirtDrop, texture }
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
      const rawHeights = await fetchHeightCached(z, x, y);
      const texture = loadColorTexture(z, x, y, renderer);
      const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
      const { geometry, baseHeights, skirtDrop } = buildTileGeometry(
        rawHeights, meta.tileSize, worldX0, worldZ0, tileSize, currentExaggeration
      );
      const material = new THREE.MeshStandardMaterial({
        map: texture, roughness: 0.95, metalness: 0, side: THREE.DoubleSide,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      tileGroup.add(mesh);
      activeTiles.set(key, { mesh, geometry, material, baseHeights, skirtDrop, texture });
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
    const desired = selectLeafTiles(camera, meta);
    const desiredKeys = new Set(desired.map(([z, x, y]) => `${z}_${x}_${y}`));

    for (const [z, x, y] of desired) {
      const key = `${z}_${x}_${y}`;
      if (!activeTiles.has(key) && !pending.has(key)) {
        loadTile(z, x, y).catch((err) => console.error(`tile ${key} failed to load:`, err));
      }
    }
    for (const key of [...activeTiles.keys()]) {
      if (!desiredKeys.has(key)) unloadTile(key);
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

  // Live exaggeration slider - real terrain relief is a few tens of meters over a 22km
  // map, invisible at 1:1; this lets you dial it back to (near) true scale to see that.
  const slider = document.createElement("input");
  slider.type = "range";
  slider.min = "1";
  slider.max = "40";
  slider.step = "1";
  slider.value = String(DEFAULT_EXAGGERATION);
  Object.assign(slider.style, { position: "fixed", bottom: "12px", left: "8px", zIndex: 10, width: "220px" });
  document.body.appendChild(slider);
  const label = document.createElement("div");
  Object.assign(label.style, {
    position: "fixed", bottom: "34px", left: "8px", zIndex: 10,
    font: "12px monospace", color: "#cfe3ff",
  });
  label.textContent = `vertical exaggeration: ${DEFAULT_EXAGGERATION}x`;
  document.body.appendChild(label);

  slider.addEventListener("input", () => {
    currentExaggeration = Number(slider.value);
    label.textContent = `vertical exaggeration: ${currentExaggeration}x`;
    for (const tile of activeTiles.values()) {
      applyExaggeration(tile.geometry, tile.baseHeights, tile.skirtDrop, currentExaggeration);
    }
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
      tileInfo.textContent = `${activeTiles.size} tiles`;
    }

    renderer.render(scene, camera);
  }
  animate(0);
}

main().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="color:#f88;padding:16px;font:14px monospace">${err.stack || err.message}</pre>`;
});
