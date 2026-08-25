import * as THREE from "three";
import type { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { ZOOM_DEBUG_COLORS, MIN_RENDER_ZOOM } from "./constants";
import { tileWorldRect } from "./heightmap";
import { selectLeafTiles } from "./lod";
import { fetchHeightTile, loadColorTexture, buildTileGeometry, buildTileBorder } from "./tileGeometry";
import type { TilesMeta, ActiveTile, LeafTile } from "./types";

export interface TileManager {
  /** All active tile meshes live under this group - the raycast target for
   * ground-anchored panning (see groundPan.ts). */
  tileGroup: THREE.Group;
  /** "z_x_y" -> live GPU resources for that tile, currently mounted in the scene -
   * read-only from outside. */
  activeTiles: ReadonlyMap<string, ActiveTile>;
  /** Runs one full 150ms pipeline tick: LOD -> load/cache (with fallback cover) ->
   * generation (build mesh, mount/unmount). See the stage doc comments below. */
  updateVisibleTiles(): void;
  setZoomDebug(enabled: boolean): void;
  setWireframe(enabled: boolean): void;
}

/** A tile's (z, x, y) plus its world rect - the shape both the LOD stage's leaf
 * output and the load/cache stage's rect checks work on. */
interface DesiredTile {
  key: string;
  z: number;
  x: number;
  y: number;
  worldX0: number;
  worldZ0: number;
  tileSize: number;
}

/** What the load/cache stage stores per tile: the fetched height samples and the
 * decoded color texture - the RAW DATA only. Mesh/geometry/material generation is
 * the generation stage's job, done lazily on mount from this. */
interface CachedTile {
  z: number;
  x: number;
  y: number;
  rawHeights: Uint16Array;
  texture: THREE.Texture;
}

/** Shared pipeline state across all three stages of a tick. Each stage reads what
 * the previous stage produced and writes what the next one consumes; the cache and
 * the mounted set persist across ticks (that persistence IS the state preservation). */
interface TickContext {
  /** Stage 1 (LOD) output: the quadtree leaves selectLeafTiles() wants, with their
   * world rects precomputed so stages 2+ don't re-derive them. */
  desired: DesiredTile[];
  desiredKeys: Set<string>;
  /** Stage 2 (load/cache) output: the finest set of already-cached tiles that
   * exactly covers the desired set with no holes (exact leaves where ready, coarsest
   * cached ancestor otherwise). Stage 3 consumes this. */
  cover: Set<string>;
}

/** Owns the tile load/replace lifecycle as a three-stage pipeline run once per
 * TILE_UPDATE_INTERVAL_MS (see main.ts's animate loop). The stages are deliberately
 * separate so each has one job and a well-defined hand-off:
 *
 *   1. LOD stage          - selectLeafTiles() (ring cascade + frustum cull, lod.ts)
 *                           -> the desired leaf set.
 *   2. Load & cache stage - accept the desired set; fire background loads into the
 *                           persistent DATA cache (desired + coarser ancestors, so
 *                           the fallback always has something). The cache stores
 *                           ONLY the fetched data (rawHeights + texture) - no mesh,
 *                           no GPU geometry. Then compute the fallback cover: the
 *                           finest set of ALREADY-CACHED tiles that covers the
 *                           desired set, so a not-yet-ready finer tile falls back to
 *                           its cached coarser ancestor and the pipeline continues.
 *   3. Generation stage   - for each cover tile, build the mesh/geometry/material
 *                           from the cached data (lazily, memoized per tile), then
 *                           diff against the current scene and mount / unmount to
 *                           match. Resources stay in the generation memo when
 *                           unmounted, so a revisit re-mounts without rebuilding.
 *
 * `dataCache` (stage 2) and `builtTiles`/`activeTiles` (stage 3) persist across
 * ticks - that is what makes "keep state" work (see README "Persistent tile cache +
 * scene cover"). */
export function createTileManager(
  scene: THREE.Scene,
  meta: TilesMeta,
  renderer: THREE.WebGLRenderer,
  camera: THREE.Camera,
  controls: OrbitControls
): TileManager {
  const tileGroup = new THREE.Group();
  scene.add(tileGroup);

  // Debug view state - see ZOOM_DEBUG_COLORS' doc comment. debugMaterials is built
  // once (6 entries, one per zoom, shared by every tile at that zoom - no need for a
  // per-tile copy since they carry no texture) and reused by every tile that has ever
  // shown the debug view; wireframeEnabled applies independently, to whichever
  // material (real or debug) is currently in use. borderMaterial is likewise a single
  // shared instance - every tile's border LineLoop (see tileGeometry.ts's
  // buildTileBorder()) just reuses it, toggled alongside the zoom-color debug view
  // itself.
  const debugMaterials = ZOOM_DEBUG_COLORS.map(
    (color) => new THREE.MeshStandardMaterial({ color, roughness: 0.95, metalness: 0, wireframe: false })
  );
  const borderMaterial = new THREE.LineBasicMaterial({ color: 0x000000 });
  let showZoomDebug = false;
  let wireframeEnabled = false;

  // Stage 2 (load & cache) state: the persistent DATA cache. loading dedupes
  // concurrent background loads of the same tile.
  const dataCache = new Map<string, CachedTile>();
  const loading = new Set<string>();

  // Stage 3 (generation) state: builtTiles memoizes generated meshes per tile (so a
  // revisit re-mounts without rebuilding geometry/material); activeTiles is the
  // subset of builtTiles currently in the scene.
  const builtTiles = new Map<string, ActiveTile>();
  const activeTiles = new Map<string, ActiveTile>();

  function keyOf(z: number, x: number, y: number): string {
    return `${z}_${x}_${y}`;
  }

  // ---------------------------------------------------------------------------
  // Stage 1: LOD - the ring-based cascade + frustum cull (lod.ts). Purely reads
  // camera/orbit state, writes the tick's desired leaf set. No cache/GPU access.
  // ---------------------------------------------------------------------------
  function runLodStage(tick: TickContext): void {
    const leaves: LeafTile[] = selectLeafTiles(camera, meta, controls.target);
    for (const [z, x, y] of leaves) {
      const key = keyOf(z, x, y);
      tick.desiredKeys.add(key);
      tick.desired.push({ key, z, x, y, ...tileWorldRect(meta, z, x, y) });
    }
  }

  // ---------------------------------------------------------------------------
  // Stage 2: Load & cache - accept the desired set, load its RAW DATA (height
  // samples + color texture) into the persistent data cache in the background, and
  // compute the FALLBACK COVER: the finest set of already-cached tiles that exactly
  // covers the desired set, so the pipeline can continue (stage 3) while finer
  // tiles are still loading. NO mesh/geometry is built here - that is stage 3's job.
  // ---------------------------------------------------------------------------

  /** Background data load: fetch height bytes + decode the color texture, store
   * them in `dataCache`. No mesh, no geometry, no scene access - generation happens
   * later in stage 3, lazily on mount. Deduped: calling it twice for the same tile
   * is a no-op the second time. */
  async function loadTileData(z: number, x: number, y: number): Promise<void> {
    const key = keyOf(z, x, y);
    if (dataCache.has(key) || loading.has(key)) return;
    loading.add(key);
    try {
      // Fetched/decoded in parallel - a cached tile's data is only stored once BOTH
      // resolve (see loadColorTexture()'s doc comment for why awaiting the actual
      // decode matters), so a tile is never built from half-loaded data.
      const [rawHeights, texture] = await Promise.all([
        fetchHeightTile(z, x, y),
        loadColorTexture(z, x, y, renderer),
      ]);
      dataCache.set(key, { z, x, y, rawHeights, texture });
    } finally {
      loading.delete(key);
    }
  }

  /** Ensure this tile AND every coarser ancestor down to MIN_RENDER_ZOOM has its
   * data cached. The coarser ancestors are what the scene falls back to while this
   * tile (and its siblings) load - so they MUST be in the cache for the fallback to
   * have anything to mount ("fallback to bigger tile, it should already be contained
   * in cache"). Cheap: loadTileData dedupes, and the descent is at most 5 levels. */
  function ensureAncestorsCached(z: number, x: number, y: number): void {
    for (;;) {
      loadTileData(z, x, y);
      if (z <= MIN_RENDER_ZOOM) break;
      z--;
      x >>= 1;
      y >>= 1;
    }
  }

  /** True if `outer`'s world rect fully contains `inner`'s, with a tiny epsilon so
   * exactly-touching tiles don't wobble the check. */
  function contains(
    outer: { worldX0: number; worldZ0: number; tileSize: number },
    inner: { worldX0: number; worldZ0: number; tileSize: number }
  ): boolean {
    const e = 1e-9;
    return (
      outer.worldX0 - e <= inner.worldX0 && inner.worldX0 + inner.tileSize - e <= outer.worldX0 + outer.tileSize &&
      outer.worldZ0 - e <= inner.worldZ0 && inner.worldZ0 + inner.tileSize - e <= outer.worldZ0 + outer.tileSize
    );
  }

  /** Computes the fallback cover from the desired set + whatever the data cache
   * already holds: the finest set of CACHED tiles that exactly covers the desired
   * leaves with NO overlap and NO holes, preferring exact desired leaves and falling
   * back to the coarsest available cached ancestor of anything not yet ready.
   *
   * Recursive, and it returns a LOCAL tile set rather than mutating a shared one - a
   * fallback down to a coarser ancestor must push aside any finer tiles already
   * chosen inside it (otherwise a coarse fallback tile would overlap the finer
   * siblings it was meant to cover). The rule per node:
   *   - the node is itself a desired leaf -> mount it iff it's cached;
   *   - otherwise refine into the children that contain any desired leaf, mounting
   *     the FULL child cover only if every such child came back covered;
   *   - if any child couldn't be covered (its own subtree has nothing ready yet),
   *     fall back to mounting THIS node's single tile - once it's cached, the
   *     previously-mounted coarse tile stays in view until ALL of its finer
   *     replacements are ready, which is exactly the "2x2 swap only when fully
   *     loaded" behavior. If neither is ready, this region stays uncovered and
   *     whatever still coarser ancestor covers it (recursively) falls through. */
  function computeFallbackCover(desired: DesiredTile[], desiredKeys: Set<string>): Set<string> {
    const cover = new Set<string>();

    function containsDesiredDescendant(childZ: number, childX: number, childY: number): boolean {
      const cr = tileWorldRect(meta, childZ, childX, childY);
      return desired.some((d) => d.z >= childZ && contains(cr, d));
    }

    function choose(z: number, x: number, y: number): Set<string> {
      const key = keyOf(z, x, y);
      if (desiredKeys.has(key)) {
        return dataCache.has(key) ? new Set([key]) : new Set();
      }
      const children = new Set<string>();
      let allCovered = true;
      for (let dy = 0; dy < 2; dy++) {
        for (let dx = 0; dx < 2; dx++) {
          const cz = z + 1, cx = x * 2 + dx, cy = y * 2 + dy;
          if (!containsDesiredDescendant(cz, cx, cy)) continue; // no desired leaf there - nothing wanted, already covered
          const childCover = choose(cz, cx, cy);
          if (childCover.size === 0) allCovered = false;
          for (const k of childCover) children.add(k);
        }
      }
      if (allCovered) return children;
      if (dataCache.has(key)) return new Set([key]); // fall back to this coarser cached tile
      return new Set();
    }

    // The quadtree root covers the whole map; z0 itself is never a desired leaf, so
    // this just springs the descent into the z1.. children that actually contain
    // desired leaves. (z0 tiles are never built or mounted - MIN_RENDER_ZOOM.)
    const root = choose(0, 0, 0);
    for (const k of root) cover.add(k);
    return cover;
  }

  function runLoadCacheStage(tick: TickContext): void {
    // Kick off background data loads for every desired leaf + its coarser ancestors
    // so the fallback always has something cached to mount. Fire-and-forget: the
    // loads populate `dataCache` asynchronously; nothing here blocks on them.
    for (const { z, x, y } of tick.desired) ensureAncestorsCached(z, x, y);
    // The fallback cover - finest cached set covering the desired set - is what the
    // generation stage mounts, so a still-loading finer tile never stalls the
    // pipeline (the cache's coarser ancestor stands in until it's ready).
    tick.cover = computeFallbackCover(tick.desired, tick.desiredKeys);
  }

  // ---------------------------------------------------------------------------
  // Stage 3: Generation - for each cover tile, BUILD the mesh/geometry/material
  // from the cached data (lazily, memoized in `builtTiles`), then diff the cover
  // against the current scene and mount / unmount to match. Unmounted tiles keep
  // their built resources so a revisit re-mounts without rebuilding.
  // ---------------------------------------------------------------------------

  /** Builds one tile's mesh/geometry/material/border from its cached data - the
   * actual GENERATION step. Called lazily by mountTile() the first time a cached
   * tile enters the cover, then memoized in `builtTiles` for reuse. */
  function buildTile(cached: CachedTile): ActiveTile {
    const { z, x, y, rawHeights, texture } = cached;
    const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
    const { geometry, N } = buildTileGeometry(rawHeights, worldX0, worldZ0, tileSize);
    const material = new THREE.MeshStandardMaterial({
      map: texture, roughness: 0.95, metalness: 0, side: THREE.DoubleSide, wireframe: wireframeEnabled,
    });
    const mesh = new THREE.Mesh(geometry, showZoomDebug ? debugMaterials[z] : material);
    const border = new THREE.LineLoop(buildTileBorder(geometry, N), borderMaterial);
    border.visible = showZoomDebug;
    return { mesh, geometry, material, texture, border, z, x, y };
  }

  /** Ensure the tile is built (from cached data, memoized) and add it to the scene. */
  function mountTile(key: string): void {
    let tile = builtTiles.get(key);
    if (!tile) {
      const cached = dataCache.get(key);
      if (!cached) return; // cover is only ever computed from cached data, so this is unreachable
      tile = buildTile(cached);
      builtTiles.set(key, tile);
    }
    // A tile built while the debug/wireframe view was OFF and offscreen can sit in
    // the cache with stale view state; re-apply the CURRENT view state on mount so a
    // cached tile never re-enters the scene looking wrong.
    tile.mesh.material = showZoomDebug ? debugMaterials[tile.z] : tile.material;
    tile.material.wireframe = wireframeEnabled;
    tile.border.visible = showZoomDebug;
    tileGroup.add(tile.mesh);
    tileGroup.add(tile.border);
    activeTiles.set(key, tile);
  }

  function unmountTile(tile: ActiveTile): void {
    tileGroup.remove(tile.mesh);
    tileGroup.remove(tile.border);
    activeTiles.delete(keyOf(tile.z, tile.x, tile.y));
    // Resources intentionally NOT disposed - the built tile stays in `builtTiles`
    // for reuse (see the createTileManager doc comment). Both caches are bounded
    // (1364 tiles max).
  }

  function runGenerationStage(tick: TickContext): void {
    // Unmount tiles the cover no longer wants - they stay built in `builtTiles`.
    for (const [key, tile] of [...activeTiles]) {
      if (!tick.cover.has(key)) unmountTile(tile);
    }
    // Mount tiles the cover now wants that aren't mounted yet - every cover tile is
    // guaranteed data-cached by construction (computeFallbackCover only picks cached
    // tiles), so this builds (once) and mounts without waiting.
    for (const key of tick.cover) {
      if (!activeTiles.has(key)) mountTile(key);
    }
  }

  // ---------------------------------------------------------------------------
  // Tick orchestration: run the three stages in order, handing each stage's output
  // to the next via the shared TickContext.
  // ---------------------------------------------------------------------------
  function updateVisibleTiles(): void {
    const tick: TickContext = { desired: [], desiredKeys: new Set(), cover: new Set() };
    // Stage 1: LOD (ring cascade + frustum cull) -> desired set.
    runLodStage(tick);
    // Stage 2: Load & cache -> background data loads + fallback cover.
    runLoadCacheStage(tick);
    // Stage 3: Generation -> build meshes, mount/unmount to match the cover.
    runGenerationStage(tick);
  }

  return {
    tileGroup,
    activeTiles,
    updateVisibleTiles,
    setZoomDebug(enabled: boolean) {
      showZoomDebug = enabled;
      for (const tile of activeTiles.values()) {
        tile.mesh.material = showZoomDebug ? debugMaterials[tile.z] : tile.material;
        tile.border.visible = showZoomDebug;
      }
      for (const tile of builtTiles.values()) {
        tile.mesh.material = showZoomDebug ? debugMaterials[tile.z] : tile.material;
        tile.border.visible = showZoomDebug;
      }
    },
    setWireframe(enabled: boolean) {
      wireframeEnabled = enabled;
      for (const material of debugMaterials) material.wireframe = wireframeEnabled;
      for (const tile of builtTiles.values()) tile.material.wireframe = wireframeEnabled;
    },
  };
}