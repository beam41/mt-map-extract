"use strict";

/**
 * Minimal, dependency-free decoder for the exact PNG format this project writes:
 * grayscale, 16-bit, non-interlaced. Shared by script/get-height.js and
 * script/terrain-viewer/scripts/prepare-assets.js so the decode logic (and the world-Z
 * formula) exists in exactly one place.
 */

const zlib = require("zlib");

/** Returns { width, height, rowBytes, pixels } where pixels is the unfiltered,
 * row-major, big-endian 16-bit-per-sample raw buffer. */
function decodeGray16Png(buffer) {
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (!buffer.subarray(0, 8).equals(signature)) {
    throw new Error("not a PNG file (bad signature)");
  }

  let offset = 8;
  let width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
  const idatChunks = [];

  while (offset < buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("ascii", offset + 4, offset + 8);
    const dataStart = offset + 8;
    const data = buffer.subarray(dataStart, dataStart + length);

    if (type === "IHDR") {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      bitDepth = data.readUInt8(8);
      colorType = data.readUInt8(9);
      interlace = data.readUInt8(12);
    } else if (type === "IDAT") {
      idatChunks.push(data);
    } else if (type === "IEND") {
      break;
    }

    offset = dataStart + length + 4; // skip CRC
  }

  if (colorType !== 0 || bitDepth !== 16) {
    throw new Error(`expected grayscale 16-bit PNG (colorType=0, bitDepth=16), got colorType=${colorType} bitDepth=${bitDepth}`);
  }
  if (interlace !== 0) {
    throw new Error("interlaced PNGs are not supported by this decoder");
  }

  const inflated = zlib.inflateSync(Buffer.concat(idatChunks));
  const bytesPerPixel = 2; // grayscale, 16-bit
  const rowBytes = width * bytesPerPixel;
  const pixels = Buffer.alloc(rowBytes * height);

  let prevRow = Buffer.alloc(rowBytes);
  let srcPos = 0;
  for (let y = 0; y < height; y++) {
    const filterType = inflated[srcPos];
    srcPos += 1;
    const row = pixels.subarray(y * rowBytes, (y + 1) * rowBytes);

    for (let i = 0; i < rowBytes; i++) {
      const x = inflated[srcPos + i];
      const a = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
      const b = prevRow[i];
      const c = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : 0;
      let value;
      switch (filterType) {
        case 0: value = x; break;
        case 1: value = x + a; break;
        case 2: value = x + b; break;
        case 3: value = x + Math.floor((a + b) / 2); break;
        case 4: value = x + paethPredictor(a, b, c); break;
        default: throw new Error(`unknown PNG filter type ${filterType} at row ${y}`);
      }
      row[i] = value & 0xff;
    }

    srcPos += rowBytes;
    prevRow = row;
  }

  return { width, height, rowBytes, pixels };
}

function paethPredictor(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  if (pb <= pc) return b;
  return c;
}

/** rawHeight (0-65535) -> world Z, in cm. See .agents/knowledge/landscape-heightmap.md
 * "Combined-canvas compositing" / worldZFormulaCm in Jeju_World.json: assumes the source
 * landscape's own actor Z origin is 0, true for every landscape in this map except one
 * excluded islet. The /128 is UE's fixed LANDSCAPE_ZSCALE engine constant, verified
 * against a real component's engine-computed CachedLocalBox.Z; the *100 is this map's
 * own RelativeScale3D.Z (uniform across every included landscape here). */
function rawHeightToWorldZCm(rawHeight) {
  return ((rawHeight - 32768) / 128.0) * 100.0;
}

module.exports = { decodeGray16Png, rawHeightToWorldZCm };
