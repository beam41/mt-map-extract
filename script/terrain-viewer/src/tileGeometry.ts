import * as THREE from "three";
import { SKIRT_DROP, COLOR_MAX_ZOOM } from "./constants";
import { rawHeightToWorldZMeters } from "./heightmap";

/** Fetches one height tile's raw uint16 samples (tileSampleCount x tileSampleCount,
 * row-major - see meta.tileSampleCount/tileSampleInnerCount in tiles.json). */
export async function fetchHeightTile(z: number, x: number, y: number): Promise<Uint16Array> {
  const r = await fetch(`/assets/tiles/height/${z}_${x}_${y}.bin`);
  if (!r.ok) throw new Error(`height tile ${z}_${x}_${y}: HTTP ${r.status}`);
  return new Uint16Array(await r.arrayBuffer());
}

/** Loads one color tile's texture, resolving only once the image has actually
 * finished decoding - not the moment `TextureLoader.load()` returns (which happens
 * immediately, well before the network fetch + image decode complete, since
 * `THREE.TextureLoader` loads asynchronously in the background). `loadTile()` (see
 * tileManager.ts) awaits this before ever adding the tile's mesh to the scene, so a
 * tile is either fully textured or not present at all - never rendered with the
 * still-blank/default texture partway through loading, which read as a "weird dark
 * patch" flash on every newly-loaded tile (most visible right after an LOD
 * transition, when several sibling tiles all start loading their own textures at
 * once) even after the load/unload-ordering fix above solved the geometry gap.
 *
 * Same flipY fix as the old single-mesh viewer: this mesh's UVs are assigned by hand
 * to directly match the tile's own un-flipped row->worldY mapping, but TextureLoader
 * defaults flipY=true (assumes a plane's default UVs, v=0 at bottom) - disable it so
 * texture and geometry read the tile in the same top-down row order. */
export function loadColorTexture(
  z: number,
  x: number,
  y: number,
  renderer: THREE.WebGLRenderer
): Promise<THREE.Texture> {
  // COLOR-TILE FALLBACK: the height pyramid goes deeper than the color pyramid
  // (COLOR_MAX_ZOOM - amc-web's color tiles stop there; deeper color is pure
  // upscaled blur and isn't generated). For a height tile at zoom > COLOR_MAX_ZOOM
  // this fetches the color tile of its COLOR_MAX_ZOOM ANCESTOR instead (same world
  // rect, coarser) and sets repeat/offset so the geometry's 0..1 UVs sample exactly
  // the ancestor's sub-rect this tile covers - the GPU upscales the slowly-varying
  // color for free, while the geometry (real height detail) carries the resolution.
  const { promise, resolve, reject } = Promise.withResolvers<THREE.Texture>();
  const depth = Math.max(0, z - COLOR_MAX_ZOOM);
  const colorZ = z - depth;
  const colorX = x >> depth;
  const colorY = y >> depth;
  const frac = 1 / (1 << depth);
  const offX = (x - (colorX << depth)) * frac;
  const offY = (y - (colorY << depth)) * frac;
  new THREE.TextureLoader().load(
    `/assets/tiles/color/${colorZ}_${colorX}_${colorY}.avif`,
    (texture) => {
      texture.flipY = false;
      texture.colorSpace = THREE.SRGBColorSpace;
      texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
      if (depth > 0) {
        texture.repeat.set(frac, frac);
        texture.offset.set(offX, offY);
      }
      resolve(texture);
    },
    undefined,
    (err) => reject(new Error(`color tile ${colorZ}_${colorX}_${colorY}.avif failed to load: ${(err as ErrorEvent)?.message ?? err}`))
  );
  return promise;
}

export interface TileGeometryResult {
  geometry: THREE.BufferGeometry;
  /** Mesh resolution (vertices per edge) - the number of main-grid vertices along
   * one edge of the tile. Forced to 2 (a single quad) for a perfectly flat tile -
   * see `flat` below. */
  N: number;
  /** True if every sample this tile stores (inner + border-overlap + normal-halo,
   * the whole fetched array) is exactly the same raw height - a real, common case
   * (open ocean floor, large flat plains) where a full mesh grid is pure waste: the
   * surface has zero curvature, so a single quad (this function's N=2 case) renders
   * pixel-identical to the full-resolution grid at a fraction of the triangle
   * count. */
  flat: boolean;
}

/**
  * Builds one tile's mesh geometry: a `resolution` x `resolution` grid sampled from
 * the tile's raw height data, plus a skirt border (see SKIRT_DROP) to hide LOD-boundary
 * cracks. Heights render at true 1:1 world scale - there is no exaggeration factor
 * anywhere in this pipeline.
 *
 * The mesh resolution is derived from the bin tile's own size, so the two can never
 * drift apart: the tile stores `haloSize x haloSize` samples (the `TileInnerResolution
 * + 2` scheme from heightmap/ImageWriter.cs's WriteHeightTiles), of which the outer
 * 1-ring is the normal halo, so `innerSize = haloSize - 2` inner samples remain per
 * edge and `resolution = innerSize` - the mesh uses every single stored inner sample
 * as exactly one vertex, nothing unused. (heightmap/ImageWriter.cs's
 * TileInnerResolution is uniform (32) across every zoom - deriving the actual
 * resolution from the fetched array here instead of trusting a JS-side copy of that
 * constant means a stale/mismatched .bin fails loud rather than building a
 * wrong-stride mesh.)
 *
 * Perfectly flat tiles (every stored sample the same raw height - common for open
 * ocean floor and large flat plains) collapse the mesh resolution to N=2, a single
 * quad: with zero curvature the full-resolution grid and the single quad are the
 * exact same flat plane, so the extra vertices/triangles are pure waste. This reuses
 * the general N-vertex-per-edge code below unchanged - N=2 is simply the smallest
 * valid grid it already knows how to build (one quad, one skirt segment per edge).
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
export function buildTileGeometry(
  rawHeights: Uint16Array,
  worldX0: number,
  worldZ0: number,
  tileWorldSize: number
): TileGeometryResult {
  // Derive the tile's sample layout from the fetched array itself - see the doc
  // comment above. A bin tile is always square.
  const haloSize = Math.round(Math.sqrt(rawHeights.length));
  if (haloSize * haloSize !== rawHeights.length) {
    throw new Error(`height tile is not square: ${rawHeights.length} samples`);
  }
  const innerSize = haloSize - 2; // outer 1-ring is the normal halo
  const flat = rawHeights.every((h) => h === rawHeights[0]);
  const N = flat ? 2 : innerSize; // collapse to a single quad if the tile is flat - see doc comment
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
  function heightAtHalo(hx: number, hy: number): number {
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

  const indices: number[] = [];
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

  return { geometry, N, flat };
}

