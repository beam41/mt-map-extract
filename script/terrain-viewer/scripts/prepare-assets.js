#!/usr/bin/env node

/**
 * Build step: copies the Leaflet/OpenLayers-style tile pyramids the 3D LOD renderer
 * loads into script/terrain-viewer/public/assets/tiles/. No decoding or resampling of
 * its own - both pyramids are already generated at the right zoom/resolution:
 *
 *   tiles/height/<z>_<x>_<y>.bin   copied from out/heightmap/tiles/ (heightmap/
 *                                  ImageWriter.cs's WriteHeightTiles) - raw uint16,
 *                                  little-endian, tileSize x tileSize, still raw height
 *                                  units (not meters - src/heightmap.ts applies
 *                                  worldZFormulaCm client-side, see
 *                                  rawHeightToWorldZMeters()).
 *   tiles/color/<z>_<x>_<y>.avif   copied from out/amc-web/map/tiles/ (amc-web/
 *                                  TileGenerator.cs) - z0..maxZoom (the heightmap
 *                                  pyramid's own depth); at the default maxZoom (5)
 *                                  this includes amc-web's own upscaled top level
 *                                  (its native zoom is 4 for its default map/tile
 *                                  size - z5 is color pixels upscaled, unlike the
 *                                  matching height z5 tile, which is a genuine
 *                                  downsample of the native heightmap, not upscaled).
 *   tiles.json                     tileSize, maxZoom, real-world width/height/origin in
 *                                  meters, min/max Z in meters, oceanLevelMeters (null if
 *                                  the pak build has no MTOceanConfig) - copied from
 *                                  Jeju_World.json's "tiles"/"ocean" sections (see
 *                                  heightmap/ImageWriter.cs's BuildMetadata).
 *
 * Both pyramids use the same {z}_{x}_{y} naming and the same zoom-to-resolution scheme
 * (z0 = 1x1 grid, zN = 2^N x 2^N, 2^N * tileSize total resolution) by construction - see
 * heightmap/ImageWriter.cs's WriteHeightTiles doc comment for why.
 *
 * Usage: node script/terrain-viewer/scripts/prepare-assets.js
 */

import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.join(__dirname, "..", "..", "..");
const HEIGHTMAP_DIR = path.join(REPO_ROOT, "out", "heightmap");
const COLOR_TILES_DIR = path.join(REPO_ROOT, "out", "amc-web", "map", "tiles");
const ASSETS_DIR = path.join(__dirname, "..", "public", "assets");
// Deepest zoom of the COLOR pyramid (amc-web's tiles). Must match the viewer constant.
const COLOR_MAX_ZOOM = 5;

function loadMetadata() {
  const metaPath = path.join(HEIGHTMAP_DIR, "Jeju_World.json");
  if (!fs.existsSync(metaPath)) {
    throw new Error(`${metaPath} not found - run 'dotnet run -c Release --project heightmap' first.`);
  }
  const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
  if (!meta.tiles) {
    throw new Error(`${metaPath} has no "tiles" section - regenerate with a heightmap build that writes tiles/<z>_<x>_<y>.bin.`);
  }
  return meta;
}

function copyPyramid(srcDir, destDir, maxZoom, extension, label, { minZoom = 0 } = {}) {
  // Clear the dest so stale tiles from a previous run (different minZoom, old
  // per-zoom resolution, old format) can't linger and get served.
  if (fs.existsSync(destDir)) fs.rmSync(destDir, { recursive: true, force: true });
  fs.mkdirSync(destDir, { recursive: true });
  let copied = 0;
  for (let z = minZoom; z <= maxZoom; z++) {
    const grid = 1 << z;
    for (let y = 0; y < grid; y++) {
      for (let x = 0; x < grid; x++) {
        const fileName = `${z}_${x}_${y}.${extension}`;
        const src = path.join(srcDir, fileName);
        if (!fs.existsSync(src)) {
          throw new Error(`${label} tile missing: ${src}`);
        }
        fs.copyFileSync(src, path.join(destDir, fileName));
        copied++;
      }
    }
  }
  console.log(`copied ${copied} ${label} tiles (z${minZoom}-z${maxZoom}) -> ${path.relative(REPO_ROOT, destDir)}`);
}

function main() {
  const meta = loadMetadata();
  const tiles = meta.tiles;

  const heightTilesSrc = path.join(HEIGHTMAP_DIR, tiles.directory);
  copyPyramid(heightTilesSrc, path.join(ASSETS_DIR, "tiles", "height"), tiles.maxZoom, "bin", "height", { minZoom: 1 });

  if (!fs.existsSync(COLOR_TILES_DIR)) {
    throw new Error(`${COLOR_TILES_DIR} not found - run 'dotnet run -c Release --project amc-web' first (tiles are not skippable with --skip-tiles).`);
  }
  // Color only exists up to COLOR_MAX_ZOOM (amc-web's pyramid stops there; deeper
  // color is pure upscaled blur - the viewer's loadColorTexture falls back to a
  // coarser color tile for deeper height tiles). Copy what's actually generated.
  copyPyramid(COLOR_TILES_DIR, path.join(ASSETS_DIR, "tiles", "color"), Math.min(tiles.maxZoom, COLOR_MAX_ZOOM), "avif", "color");

  const metaOut = {
    maxZoom: tiles.maxZoom,
    // height-tile sample layout (uniform at every zoom): tileInnerResolution = mesh
    // vertices per edge (buildTileGeometry derives sizes from the fetched .bin
    // directly, so these are informational), tileSampleCount = inner + 2.
    tileInnerResolution: tiles.tileInnerResolution,
    tileSampleCount: tiles.tileSampleCount,
    dtype: tiles.dtype,
    byteOrder: tiles.byteOrder,
    widthMeters: tiles.widthMeters,
    heightMeters: tiles.heightMeters,
    originMetersX: tiles.originMetersX,
    originMetersY: tiles.originMetersY,
    minZ: tiles.minZMeters,
    maxZ: tiles.maxZMeters,
    colorExtension: "avif",
    // null when the pak build this ran against has no MTOceanConfig (shouldn't happen for
    // Jeju_World, but a consumer must not assume every map has an ocean).
    oceanLevelMeters: meta.ocean ? meta.ocean.levelMeters : null,
  };
  const metaOutPath = path.join(ASSETS_DIR, "tiles.json");
  fs.writeFileSync(metaOutPath, JSON.stringify(metaOut, null, 2));
  console.log(`wrote ${path.relative(REPO_ROOT, metaOutPath)}`);

  console.log("Done.");
}

main();
