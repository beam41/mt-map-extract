#!/usr/bin/env node

/**
 * Build step: copies the assets the Three.js viewer loads into
 * script/terrain-viewer/public/assets/. Does no decoding or resampling of its own - the
 * `heightmap` project's `--web-size` output (out/heightmap/heights_<n>px.bin, default
 * 512x512) is already area-average-downsampled and ready to use, so this is a direct
 * file copy plus a small metadata passthrough (see out/heightmap/Jeju_World.json's "web"
 * section, written by heightmap/ImageWriter.cs):
 *
 *   heights.bin   direct copy of out/heightmap/heights_<n>px.bin - raw uint16,
 *                 little-endian, row-major, GRID x GRID, still in raw height units (not
 *                 meters). src/main.js applies worldZFormulaCm client-side; see its
 *                 rawHeightToWorldZMeters().
 *   heights.json  grid resolution, real-world width/height/origin in meters, min/max Z
 *                 in meters (all copied from Jeju_World.json's "web" section) - what the
 *                 viewer needs to build a correctly-scaled BufferGeometry, plus dtype/
 *                 byteOrder so main.js knows how to interpret heights.bin.
 *   map.png       copied as-is (the color texture; no per-pixel correspondence with the
 *                 heightmap grid is required, it's just draped over the mesh via UVs).
 *
 * Usage: node script/terrain-viewer/scripts/prepare-assets.js
 * (resolution is fixed by the heightmap project's --web-size at generation time, not an
 * argument here - re-run `dotnet run -c Release --project heightmap -- --web-size <n>`
 * to change it)
 */

import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.join(__dirname, "..", "..", "..");
const HEIGHTMAP_DIR = path.join(REPO_ROOT, "out", "heightmap");
const MAP_PNG = path.join(REPO_ROOT, "out", "amc-web", "map", "map.png");
const ASSETS_DIR = path.join(__dirname, "..", "public", "assets");

function loadMetadata() {
  const metaPath = path.join(HEIGHTMAP_DIR, "Jeju_World.json");
  if (!fs.existsSync(metaPath)) {
    throw new Error(`${metaPath} not found - run 'dotnet run -c Release --project heightmap' first.`);
  }
  const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
  if (!meta.web) {
    throw new Error(`${metaPath} has no "web" section - regenerate with a heightmap build that writes heights_<n>px.bin.`);
  }
  return meta.web;
}

function main() {
  const web = loadMetadata();

  const webBinPath = path.join(HEIGHTMAP_DIR, web.fileName);
  if (!fs.existsSync(webBinPath)) {
    throw new Error(`${webBinPath} not found - run 'dotnet run -c Release --project heightmap' first.`);
  }

  fs.mkdirSync(ASSETS_DIR, { recursive: true });

  const heightsOutPath = path.join(ASSETS_DIR, "heights.bin");
  fs.copyFileSync(webBinPath, heightsOutPath);
  console.log(`copied ${path.relative(REPO_ROOT, webBinPath)} -> ${path.relative(REPO_ROOT, heightsOutPath)}`);

  const metaOut = {
    grid: web.grid,
    dtype: web.dtype,
    byteOrder: web.byteOrder,
    widthMeters: web.widthMeters,
    heightMeters: web.heightMeters,
    originMetersX: web.originMetersX,
    originMetersY: web.originMetersY,
    minZ: web.minZMeters,
    maxZ: web.maxZMeters,
  };
  const metaOutPath = path.join(ASSETS_DIR, "heights.json");
  fs.writeFileSync(metaOutPath, JSON.stringify(metaOut, null, 2));
  console.log(`wrote ${path.relative(REPO_ROOT, metaOutPath)}`);

  if (!fs.existsSync(MAP_PNG)) {
    throw new Error(`${MAP_PNG} not found - run 'dotnet run -c Release --project amc-web -- --skip-tiles' first.`);
  }
  const mapOutPath = path.join(ASSETS_DIR, "map.png");
  fs.copyFileSync(MAP_PNG, mapOutPath);
  console.log(`copied ${path.relative(REPO_ROOT, MAP_PNG)} -> ${path.relative(REPO_ROOT, mapOutPath)}`);

  console.log("Done.");
}

main();
