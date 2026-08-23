#!/usr/bin/env node
"use strict";

/**
 * Sample program: given a world X/Y coordinate (cm, same convention as the game/pak),
 * returns the terrain elevation (Z) at that point.
 *
 * Reads out/heightmap/Jeju_World.json (origin, quad scale, heights.bin layout) and seeks
 * directly into out/heightmap/heights.bin (raw uint16, little-endian, row-major, native
 * resolution) at the one point requested - never loads the full 11000x11000 array, and
 * needs no PNG/zlib decode at all (unlike the .png outputs). Needs
 * `dotnet run -c Release --project heightmap` to have been run first.
 *
 * No npm dependencies: reads the 2 bytes at the computed offset with a plain
 * `fs.readSync` against an open file descriptor.
 *
 * Usage:
 *   node script/get-height.js <worldX_cm> <worldY_cm>
 *   node script/get-height.js -1024000 204800
 *
 * See .agents/knowledge/landscape-heightmap.md for the world-space formulas and gotchas
 * (quad scale, actor-Z-origin assumption, etc.) this script implements.
 */

const fs = require("fs");
const path = require("path");
const { rawHeightToWorldZCm } = require("./lib/png16");

const HEIGHTMAP_DIR = path.join(__dirname, "..", "out", "heightmap");

function loadMetadata() {
  const metaPath = path.join(HEIGHTMAP_DIR, "Jeju_World.json");
  if (!fs.existsSync(metaPath)) {
    throw new Error(
      `${metaPath} not found - run 'dotnet run -c Release --project heightmap' first ` +
      "to generate the heightmap and heights.bin."
    );
  }
  return JSON.parse(fs.readFileSync(metaPath, "utf8"));
}

/** worldX/Y (cm) -> terrain elevation Z (cm). Throws if the point falls outside the
 * generated map. */
function getHeightAt(worldXCm, worldYCm, meta) {
  const { originCm, quadScaleCm, nativeResolution, heightsBin } = meta;
  if (heightsBin.dtype !== "uint16" || heightsBin.byteOrder !== "little") {
    throw new Error(`unsupported heights.bin format: dtype=${heightsBin.dtype} byteOrder=${heightsBin.byteOrder}`);
  }

  const col = Math.round((worldXCm - originCm.X) / quadScaleCm);
  const row = Math.round((worldYCm - originCm.Y) / quadScaleCm);
  if (col < 0 || col >= nativeResolution.width || row < 0 || row >= nativeResolution.height) {
    throw new Error(
      `(${worldXCm}, ${worldYCm}) cm is outside the generated map ` +
      `(covers X:[${originCm.X}, ${originCm.X + nativeResolution.width * quadScaleCm}), ` +
      `Y:[${originCm.Y}, ${originCm.Y + nativeResolution.height * quadScaleCm}))`
    );
  }

  const binPath = path.join(HEIGHTMAP_DIR, heightsBin.fileName);
  if (!fs.existsSync(binPath)) {
    throw new Error(`heights.bin not found: ${binPath}`);
  }
  const offset = (row * nativeResolution.width + col) * 2;
  const fd = fs.openSync(binPath, "r");
  let rawHeight;
  try {
    const buf = Buffer.alloc(2);
    fs.readSync(fd, buf, 0, 2, offset);
    rawHeight = buf.readUInt16LE(0);
  } finally {
    fs.closeSync(fd);
  }
  const worldZCm = rawHeightToWorldZCm(rawHeight);

  return { col, row, offset, rawHeight, worldZCm };
}

function main() {
  const [xArg, yArg] = process.argv.slice(2);
  if (xArg === undefined || yArg === undefined || xArg === "-h" || xArg === "--help") {
    console.error("Usage: node script/get-height.js <worldX_cm> <worldY_cm>");
    console.error("Example (a point on the main landmass): node script/get-height.js -500000 800000");
    process.exit(xArg === "-h" || xArg === "--help" ? 0 : 2);
  }

  const worldX = Number(xArg);
  const worldY = Number(yArg);
  if (!Number.isFinite(worldX) || !Number.isFinite(worldY)) {
    console.error(`error: coordinates must be numbers, got '${xArg}', '${yArg}'`);
    process.exit(2);
  }

  try {
    const meta = loadMetadata();
    const result = getHeightAt(worldX, worldY, meta);
    console.log(`world (${worldX}, ${worldY}) cm`);
    console.log(`  -> pixel (col=${result.col}, row=${result.row}), heights.bin offset ${result.offset}`);
    console.log(`  -> raw height: ${result.rawHeight}`);
    console.log(`  -> Z: ${result.worldZCm.toFixed(2)} cm (${(result.worldZCm / 100).toFixed(2)} m)`);
  } catch (err) {
    console.error(`error: ${err.message}`);
    process.exit(1);
  }
}

main();
