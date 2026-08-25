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
  /** Re-runs selectLeafTiles() from the current camera/orbit state, fills the tile
   * cache in the background, and reconciles the scene against whatever is now fully
   * ready in that cache. */
  updateVisibleTiles(): void;
  setZoomDebug(enabled: boolean): void;
  setWireframe(enabled: boolean): void;
}

/** Owns the tile load/replace lifecycle. The two structures no longer move in
 * lockstep the way the old all-in-one `loadTile -> activeTiles` flow did:
 *
 *  - `cache`  - every tile EVER built lives here permanently. The quadtree can only
 *    reach 1364 distinct (z, x, y) positions (README), so keeping all of them is a
 *    small, bounded set of meshes/textures, and it is what makes "keep state" work:
 *    background loads populate the cache without touching the scene, a scene tile
 *    falls back to a previously-loaded coarser ancestor the instant a finer
 *    replacement isn't ready yet, and returning the camera to a visited area
 *    re-mounts its cached mesh with zero refetch.
 *  - `activeTiles` - the subset of the cache currently mounted in the scene. It is
 *    the reconciled output of updateVisibleTiles() (see its own doc comment) - NEVER
 *    written directly by the background loader.
 *
 * See updateVisibleTiles() for the exact swap rule: a tile leaves the scene only
 * once its replacement is fully loaded (both height fetched and color decoded), and
 * the cache is only ever cheapened by a dead-simultaneous-zoom-in, not flashed out. */
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

  // Persistent cache + mount bookkeeping. loading dedupes concurrent background
  // loads of the same tile; heightCache dedupes re-fetching the same height bytes.
  const cache = new Map<string, ActiveTile>();
  const loading = new Set<string>();
  const heightCache = new Map<string, Promise<Uint16Array>>();
  const activeTiles = new Map<string, ActiveTile>();

  function keyOf(z: number, x: number, y: number): string {
    return `${z}_${x}_${y}`;
  }

  function fetchHeightCached(z: number, x: number, y: number): Promise<Uint16Array> {
    const key = keyOf(z, x, y);
    if (!heightCache.has(key)) heightCache.set(key, fetchHeightTile(z, x, y));
    return heightCache.get(key)!;
  }

  /** Background cache load: fetch height bytes + decode the color texture, build the
   * tile's mesh, and store it in `cache`. NEVER touches the scene - mounting is
   * decided separately each 150ms tick (updateVisibleTiles), so a tile is swapped
   * into the scene only once it is fully ready (height fetched AND texture decoded),
   * and a still-loading tile never leaves a visible gap while it loads. Deduped:
   * calling it twice for the same tile is a no-op the second time. */
  async function loadTile(z: number, x: number, y: number): Promise<void> {
    const key = keyOf(z, x, y);
    if (cache.has(key) || loading.has(key)) return;
    loading.add(key);
    try {
      // Fetched/decoded in parallel - the mesh is only ever stored once BOTH resolve
      // (see loadColorTexture()'s doc comment for why awaiting the actual decode
      // matters), so a cached tile is always fully textured, never a blank/dark patch.
      const [rawHeights, texture] = await Promise.all([
        fetchHeightCached(z, x, y),
        loadColorTexture(z, x, y, renderer),
      ]);
      const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
      const { geometry, N } = buildTileGeometry(rawHeights, worldX0, worldZ0, tileSize);
      const material = new THREE.MeshStandardMaterial({
        map: texture, roughness: 0.95, metalness: 0, side: THREE.DoubleSide, wireframe: wireframeEnabled,
      });
      const mesh = new THREE.Mesh(geometry, showZoomDebug ? debugMaterials[z] : material);
      const border = new THREE.LineLoop(buildTileBorder(geometry, N), borderMaterial);
      border.visible = showZoomDebug;
      cache.set(key, { mesh, geometry, material, texture, border, z, x, y });
    } finally {
      loading.delete(key);
    }
  }

  /** Ensure this tile AND every coarser ancestor down to MIN_RENDER_ZOOM is cached.
   * The coarser ancestors are what the scene falls back to while this tile (and its
   * siblings) load - so they MUST be in the cache for the fallback to have anything
   * to mount ("fallback to bigger tile, it should already be contained in cache").
   * Cheap: loadTile dedupes, and the descent is at most 5 levels. */
  function ensureAncestorsCached(z: number, x: number, y: number): void {
    for (;;) {
      loadTile(z, x, y);
      if (z <= MIN_RENDER_ZOOM) break;
      z--;
      x >>= 1;
      y >>= 1;
    }
  }

  function mountTile(tile: ActiveTile): void {
    const key = keyOf(tile.z, tile.x, tile.y);
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
    // Resources intentionally NOT disposed - the tile stays in `cache` for reuse
    // (see the createTileManager doc comment). Cache is bounded (1364 tiles max).
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

  /** Computes the scene cover to mount this tick: the finest set of already-cached
   * tiles that exactly covers the desired leaf set with NO overlap and NO holes,
   * preferring exact desired leaves and falling back to the coarsest available
   * cached ancestor of anything not yet ready.
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
  function chooseCover(
    desiredRects: Array<{ key: string; z: number; x: number; y: number; worldX0: number; worldZ0: number; tileSize: number }>,
    desiredKeys: Set<string>
  ): Set<string> {
    const cover = new Set<string>();

    function containsDesiredDescendant(childZ: number, childX: number, childY: number): boolean {
      const cr = tileWorldRect(meta, childZ, childX, childY);
      return desiredRects.some((d) => d.z >= childZ && contains(cr, d));
    }

    function choose(z: number, x: number, y: number): Set<string> {
      const key = keyOf(z, x, y);
      if (desiredKeys.has(key)) {
        return cache.has(key) ? new Set([key]) : new Set();
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
      if (cache.has(key)) return new Set([key]); // fall back to this coarser cached tile
      return new Set();
    }

    // The quadtree root covers the whole map; z0 itself is never a desired leaf, so
    // this just springs the descent into the z1.. children that actually contain
    // desired leaves. (z0 tiles are never built or mounted - MIN_RENDER_ZOOM.)
    const root = choose(0, 0, 0);
    for (const k of root) cover.add(k);
    return cover;
  }

  /** The 150ms scene-reconciliation tick:
   *  1. Re-run selectLeafTiles() from the current camera/orbit state (the tiler).
   *  2. Send every desired tile to the background loader - it loads into the CACHE
   *     only, never the scene, and won't block on anything. Coarser ancestors are
   *     loaded too so a not-yet-ready finer region always has a coarse fallback.
   *  3. Choose the cover to render from what's NOW ready in the cache (exact desired
   *     leaves where available, coarser cached ancestors elsewhere) and diff-mount it
   *     against the current scene. A tile whose replacement is still loading simply
   *     stays mounted - it keeps its state and is swapped the exact tick its
   *     replacement is fully ready, so there is never a flashed-empty gap. */
  function updateVisibleTiles(): void {
    const desired: LeafTile[] = selectLeafTiles(camera, meta, controls.target);
    const desiredKeys = new Set<string>();
    const desiredRects = desired.map(([z, x, y]) => {
      const key = keyOf(z, x, y);
      desiredKeys.add(key);
      return { key, z, x, y, ...tileWorldRect(meta, z, x, y) };
    });

    for (const [z, x, y] of desired) ensureAncestorsCached(z, x, y);

    const cover = chooseCover(desiredRects, desiredKeys);
    for (const [key, tile] of [...activeTiles]) {
      if (!cover.has(key)) unmountTile(tile);
    }
    for (const key of cover) {
      if (!activeTiles.has(key)) mountTile(cache.get(key)!);
    }
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
      for (const tile of cache.values()) {
        tile.mesh.material = showZoomDebug ? debugMaterials[tile.z] : tile.material;
        tile.border.visible = showZoomDebug;
      }
    },
    setWireframe(enabled: boolean) {
      wireframeEnabled = enabled;
      for (const material of debugMaterials) material.wireframe = wireframeEnabled;
      for (const tile of cache.values()) tile.material.wireframe = wireframeEnabled;
    },
  };
}