import * as THREE from "three";
import type { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { ZOOM_DEBUG_COLORS } from "./constants";
import { tileWorldRect } from "./heightmap";
import { selectLeafTiles } from "./lod";
import { fetchHeightTile, loadColorTexture, buildTileGeometry, buildTileBorder } from "./tileGeometry";
import type { TilesMeta, ActiveTile } from "./types";

export interface TileManager {
  /** All active tile meshes live under this group - the raycast target for
   * ground-anchored panning (see groundPan.ts). */
  tileGroup: THREE.Group;
  /** "z_x_y" -> live GPU resources for that tile - read-only from outside. */
  activeTiles: ReadonlyMap<string, ActiveTile>;
  /** Re-runs selectLeafTiles() from the current camera/orbit state and loads/unloads
   * tiles to match. */
  updateVisibleTiles(): void;
  setZoomDebug(enabled: boolean): void;
  setWireframe(enabled: boolean): void;
}

/** Owns the tile load/unload/replace lifecycle: fetches height + color data for
 * every quadtree leaf selectLeafTiles() currently wants, builds its mesh once both
 * resolve, and swaps out a given stale tile only once whatever's replacing *it
 * specifically* is actually ready - see updateVisibleTiles()'s own doc comment for
 * why that's per-region, not an all-or-nothing gate on the whole desired set. */
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

  const activeTiles = new Map<string, ActiveTile>();
  const heightCache = new Map<string, Promise<Uint16Array>>();
  const pending = new Set<string>();
  // The most recent selectLeafTiles() result's key set - updated at the START of
  // every updateVisibleTiles() call, read at the END of every in-flight loadTile()
  // (see its own doc comment for why: without this, a tile whose fetch/decode is
  // still running when the camera moves on gets built and added to the scene anyway,
  // even though it's no longer wanted - wasted work that also bloats `activeTiles`
  // with already-stale tiles, which in turn delays the "every desired tile is active"
  // gate below that unloads the REAL stale tiles, stretching out exactly the kind of
  // old-tile-lingering-behind-the-ring-extent lag reported as "the tile touched by
  // the ring doesn't get that LOD" - confirmed by live instrumentation: right after
  // rapid panning, activeTiles briefly ballooned to 3x its settled size with several
  // leaves visibly stale relative to the (always-correct, instantly-updated)
  // selectLeafTiles() output; all mismatches were gone within one further update.
  let currentDesiredKeys = new Set<string>();

  function fetchHeightCached(z: number, x: number, y: number): Promise<Uint16Array> {
    const key = `${z}_${x}_${y}`;
    if (!heightCache.has(key)) heightCache.set(key, fetchHeightTile(z, x, y));
    return heightCache.get(key)!;
  }

  async function loadTile(z: number, x: number, y: number): Promise<void> {
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
      // The camera may well have moved on by the time this fetch/decode finishes -
      // see currentDesiredKeys' doc comment. Building and adding a mesh nobody wants
      // anymore is pure waste, and worse, it bloats activeTiles with an
      // already-stale tile that then has to sit through a full extra unload cycle -
      // stretching out exactly the old-tile-lingering lag that made this worth
      // guarding against. Bail out before doing any of that work.
      if (!currentDesiredKeys.has(key)) {
        texture.dispose();
        return;
      }
      const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
      const { geometry, N } = buildTileGeometry(rawHeights, worldX0, worldZ0, tileSize);
      const material = new THREE.MeshStandardMaterial({
        map: texture, roughness: 0.95, metalness: 0, side: THREE.DoubleSide, wireframe: wireframeEnabled,
      });
      const mesh = new THREE.Mesh(geometry, showZoomDebug ? debugMaterials[z] : material);
      tileGroup.add(mesh);
      const border = new THREE.LineLoop(buildTileBorder(geometry, N), borderMaterial);
      border.visible = showZoomDebug;
      tileGroup.add(border);
      activeTiles.set(key, { mesh, geometry, material, texture, border, z, x, y });
    } finally {
      pending.delete(key);
    }
  }

  function unloadTile(key: string): void {
    const tile = activeTiles.get(key);
    if (!tile) return;
    tileGroup.remove(tile.mesh);
    tileGroup.remove(tile.border);
    tile.geometry.dispose();
    tile.border.geometry.dispose();
    tile.material.dispose();
    tile.texture.dispose();
    activeTiles.delete(key);
  }

  /** True if two tile world rects (as tileWorldRect() returns) share more than a
   * sliver of area - a small epsilon excludes exactly-touching adjacent tiles
   * (which share a zero-width edge, not real overlap) from counting as replacements
   * for each other. */
  function rectsOverlap(
    a: { worldX0: number; worldZ0: number; tileSize: number },
    b: { worldX0: number; worldZ0: number; tileSize: number }
  ): boolean {
    const EPS = 1e-6;
    return (
      a.worldX0 < b.worldX0 + b.tileSize - EPS && b.worldX0 < a.worldX0 + a.tileSize - EPS &&
      a.worldZ0 < b.worldZ0 + b.tileSize - EPS && b.worldZ0 < a.worldZ0 + a.tileSize - EPS
    );
  }

  function updateVisibleTiles(): void {
    const desired = selectLeafTiles(camera, meta, controls.target);
    const desiredKeys = new Set(desired.map(([z, x, y]) => `${z}_${x}_${y}`));
    currentDesiredKeys = desiredKeys; // see its own doc comment

    for (const [z, x, y] of desired) {
      const key = `${z}_${x}_${y}`;
      if (!activeTiles.has(key) && !pending.has(key)) {
        loadTile(z, x, y).catch((err) => console.error(`tile ${key} failed to load:`, err));
      }
    }

    // Per-old-tile unload: a no-longer-desired tile is safe to remove as soon as
    // EVERY desired tile whose world rect overlaps its own is actually active -
    // decoupled per screen region, not gated on the whole desired set being ready.
    // The earlier all-or-nothing gate (unload nothing until every desired tile is
    // active) caused a sustained triangle-count spike while moving: camera movement
    // typically transitions LOD across many unrelated regions of the screen at
    // once, and the single slowest straggler anywhere blocked every other,
    // already-ready region's old tile from unloading too - so old and new tiles
    // piled up together for as long as the slowest fetch/decode took, not just the
    // fast ones. Each region now swaps the instant *its own* replacement is ready,
    // independent of how long any other region's fetch/decode takes. A genuinely
    // brand-new tile (never fetched before) still can't render in the same tick
    // it's first requested - fetch + texture decode is inherently asynchronous,
    // there's no way around at least one round trip - but this removes the
    // artificial extra delay of waiting on unrelated tiles too.
    const desiredRects = desired.map(([z, x, y]) => ({ key: `${z}_${x}_${y}`, ...tileWorldRect(meta, z, x, y) }));
    for (const [key, tile] of [...activeTiles]) {
      if (desiredKeys.has(key)) continue; // still desired - not stale
      const oldRect = tileWorldRect(meta, tile.z, tile.x, tile.y);
      const replacementReady = desiredRects.every((d) => !rectsOverlap(oldRect, d) || activeTiles.has(d.key));
      if (replacementReady) unloadTile(key);
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
    },
    setWireframe(enabled: boolean) {
      wireframeEnabled = enabled;
      for (const material of debugMaterials) material.wireframe = wireframeEnabled;
      for (const tile of activeTiles.values()) tile.material.wireframe = wireframeEnabled;
    },
  };
}
