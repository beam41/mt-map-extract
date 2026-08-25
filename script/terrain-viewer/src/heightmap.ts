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

/** tiles.json's raw shape from the extractor (heightmap/ImageWriter.cs's
 * BuildViewerTilesJson) - all spatial values are in CENTIMETERS (Unreal's native
 * unit). Kept separate from TilesMeta so the raw file stays untouched and we convert
 * once here. */
interface RawTilesJson {
  maxZoom: number;
  tileInnerResolution: number;
  tileSampleCount: number;
  widthCm: number;
  heightCm: number;
  originXCm: number;
  originYCm: number;
  minZ: number;   // cm
  maxZ: number;   // cm
  oceanLevelCm: number | null;
}

export async function loadTilesMeta(): Promise<TilesMeta> {
  const r = await fetch("/assets/tiles.json");
  if (!r.ok) throw new Error(`tiles.json: HTTP ${r.status}`);
  // The height tile format is an invariant of the extractor (heightmap/ImageWriter.cs):
  // raw uint16, little-endian, tileSampleCount x tileSampleCount samples per tile. The
  // dtype/byteOrder were dropped from the metadata as constants; buildTileGeometry
  // derives the sample count from the fetched .bin directly anyway.
  const raw = (await r.json()) as RawTilesJson;
  // The file is in Unreal's cm; the rest of the viewer works in meters - convert once.
  return {
    widthMeters: raw.widthCm / 100,
    heightMeters: raw.heightCm / 100,
    maxZoom: raw.maxZoom,
    oceanLevelMeters: raw.oceanLevelCm === null ? null : raw.oceanLevelCm / 100,
    minZ: raw.minZ / 100,
    maxZ: raw.maxZ / 100,
    tileInnerResolution: raw.tileInnerResolution,
    tileSampleCount: raw.tileSampleCount,
  };
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
