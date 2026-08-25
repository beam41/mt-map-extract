import type * as THREE from "three";

/** tiles.json's shape - written by amc-web/heightmap's tile pyramid exporters and
 * passed straight through by scripts/prepare-assets.js with no decoding/resampling.
 * Only the fields this viewer actually reads are modeled; unknown passthrough fields
 * are tolerated via the index signature rather than rejected. */
export interface TilesMeta {
  widthMeters: number;
  heightMeters: number; // === widthMeters (the map is square) - see tileWorldRect()
  maxZoom: number;
  oceanLevelMeters: number | null;
  minZ: number;
  maxZ: number;
  dtype: string;
  byteOrder: string;
  [key: string]: unknown;
}

/** One quadtree leaf as selectLeafTiles() returns it: zoom, then tile-grid x/y at
 * that zoom (a `1 << z` x `1 << z` grid). */
export type LeafTile = readonly [z: number, x: number, y: number];

/** Rule 1 + rule 2's shared setup - see computeRingExtents' doc comment. */
export interface RingExtents {
  maxZoomByAltitude: number;
  cellSize: number;
  orbitCX: number;
  orbitCY: number;
  /** Vision radius per zoom, in continuous finest-grid-cell units; index 0 and
   * indices above maxZoomByAltitude are unset. */
  ringExtent: number[];
}

/** One tile's live GPU resources - see tileManager.ts's doc comment for the
 * load/unload lifecycle that owns these. */
export interface ActiveTile {
  mesh: THREE.Mesh;
  geometry: THREE.BufferGeometry;
  material: THREE.MeshStandardMaterial;
  texture: THREE.Texture;
  z: number;
  x: number;
  y: number;
}
