import type { TilesMeta } from "./types";

/**
 * Raw height unit (0-65535, as stored in the height tiles) -> world Z, in meters.
 * Matches `worldZFormulaCm` in Jeju_World.json exactly (that formula in cm, divided by
 * 100 here): `((rawHeight - 32768) / 128.0) * 100.0 / 100.0` simplifies to the form
 * below. Kept as its own small function here (not imported from script/lib/png16.js,
 * which is CommonJS for Node/get-height.js) rather than fighting Vite's bundler over a
 * CJS/ESM interop that isn't needed for one pure, one-line formula.
 */
export function rawHeightToWorldZMeters(rawHeight: number): number {
  return (rawHeight - 32768) / 128.0;
}

export async function loadTilesMeta(): Promise<TilesMeta> {
  const r = await fetch("/assets/tiles.json");
  if (!r.ok) throw new Error(`tiles.json: HTTP ${r.status}`);
  const meta = (await r.json()) as TilesMeta;
  if (meta.dtype !== "uint16" || meta.byteOrder !== "little") {
    throw new Error(`unsupported height tile format: dtype=${meta.dtype} byteOrder=${meta.byteOrder}`);
  }
  return meta;
}

export interface TileWorldRect {
  worldX0: number;
  worldZ0: number;
  tileSize: number;
}

/** World-space rectangle a (z, x, y) tile covers, centered on the map's own center
 * (matching the tile mesh's coordinate convention below). */
export function tileWorldRect(meta: TilesMeta, z: number, x: number, y: number): TileWorldRect {
  const grid = 1 << z;
  const tileSize = meta.widthMeters / grid; // widthMeters === heightMeters (square map)
  const worldX0 = -meta.widthMeters / 2 + x * tileSize;
  const worldZ0 = -meta.heightMeters / 2 + y * tileSize;
  return { worldX0, worldZ0, tileSize };
}
