using NetVips;
using Newtonsoft.Json.Linq;
using MtExtract;

namespace HeightmapExtractor;

/// <summary>Writes the combined map at native resolution, a raw binary for point queries,
/// a Leaflet/OpenLayers-style height tile pyramid matching `amc-web`'s color tile zoom
/// scheme, and a downscaled debug preview.</summary>
internal static class ImageWriter
{
    private const string Label = "Jeju_World";

    public static void Write(LandscapeExtractor.Map map, double? oceanLevelCm, int tileSize, int maxZoom, int debugSize, string outDir)
    {
        Directory.CreateDirectory(outDir);

        using var native = Image.NewFromMemory(map.Heights, map.Width, map.Height, 1,
            Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Grey16);

        var heightmapPath = Path.Combine(outDir, $"{Label}_heightmap16.png");
        native.Pngsave(heightmapPath, compression: 6);
        Console.WriteLine($"  wrote {heightmapPath} ({map.Width}x{map.Height}, 16-bit, native resolution)");

        Output.Write(Path.Combine(outDir, $"{Label}.json"),
            BuildMetadata(map, oceanLevelCm, tileSize, maxZoom).ToString(Newtonsoft.Json.Formatting.Indented));

        WriteHeightsBin(map, Path.Combine(outDir, "heights.bin"));
        WriteHeightTiles(map, tileSize, maxZoom, Path.Combine(outDir, "tiles"));
        WriteDebugPreview(native, map, debugSize, Path.Combine(outDir, "debug"));
    }

    /// <summary>Raw uint16, little-endian, row-major, one value per quad at native
    /// resolution - a flat array a consumer can seek into directly at
    /// `(row * width + col) * 2` bytes, no PNG/zlib decode or tile-boundary math needed
    /// for a single point query (unlike the PNG, which needs a full-image or per-tile
    /// decode first). Same raw height values as the PNG (0-65535, formulas in the JSON
    /// sidecar apply identically); this is a lossless alternate encoding, not a
    /// downsample.</summary>
    private static void WriteHeightsBin(LandscapeExtractor.Map map, string path)
    {
        var bytes = new byte[map.Heights.Length * 2];
        Buffer.BlockCopy(map.Heights, 0, bytes, 0, bytes.Length);
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < bytes.Length; i += 2) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  wrote {path} ({map.Width}x{map.Height}, uint16 little-endian, {bytes.Length:N0} bytes)");
    }

    /// <summary>Leaflet/OpenLayers-style tile pyramid, `{z}_{x}_{y}.bin` - deliberately
    /// the same `{z}_{x}_{y}` naming and the same zoom-to-grid scheme as
    /// `amc-web/TileGenerator.cs`'s color tiles (z0 = 1x1 grid, zN = 2^N x 2^N), so
    /// `script/terrain-viewer` can fetch a height tile and a color tile for the same
    /// (z, x, y) and know they cover the exact same world-space rectangle - `tileSize`
    /// (raw sample resolution per tile) is independent of amc-web's own `--tile-size`,
    /// so the two pyramids can carry different detail levels per tile while sharing the
    /// same grid/zoom layout. `maxZoom` is tied to amc-web's own *native* zoom (4, for
    /// its 4096px map at its default tile size) as a manually-kept-in-sync constant,
    /// not auto-derived from amc-web's on-disk output - this project has no runtime
    /// dependency on amc-web's, matching the existing "manually kept in sync with the
    /// game's own map" pattern used by --origin-x/--origin-y/--map-size. Deliberately
    /// never generates amc-web's extra upscaled level (z5 by its default) - upscaling
    /// raw elevation data invents no real detail, only interpolates, which would be
    /// actively misleading for a heightmap. Every level here is a genuine area-average
    /// downsample of the native data.
    ///
    /// Each tile file actually stores `(tileSize+3) x (tileSize+3)` samples, not just
    /// `tileSize x tileSize`: a 1px border overlap (as above) *plus* one more "halo"
    /// sample beyond each edge, purely so `script/terrain-viewer` can compute vertex
    /// normals via central finite differences that are bit-identical between two
    /// adjacent tiles at their shared edge. A tile's own triangles alone can only see
    /// one side of a boundary vertex, so per-tile `computeVertexNormals()` (face-normal
    /// averaging restricted to that tile's own mesh) gives every boundary vertex a
    /// normal skewed toward its own tile's interior - systematically different from its
    /// neighbour's skewed-the-other-way normal for the textually same world point, which
    /// reads as a lighting-discontinuity seam even when the *position* data matches
    /// exactly (a real, separate bug from the plain 1px-overlap position fix above -
    /// reported as "seam is still a problem" after that fix alone). The extra halo
    /// sample gives each tile's own data everything needed for a symmetric central
    /// difference at its own edge, and because both tiles derive that halo sample from
    /// the exact same deterministic area-average of the same canvas region, the two
    /// independently-computed edge normals come out numerically identical. The
    /// outermost edge of the whole pyramid (no real neighbour) clamps the halo to the
    /// last valid canvas pixel, same as the border-overlap sample.</summary>
    private static void WriteHeightTiles(LandscapeExtractor.Map map, int tileSize, int maxZoom, string tilesDir)
    {
        Directory.CreateDirectory(tilesDir);
        var sampleCount = tileSize + 3; // tileSize+1 border overlap + 1px normal halo per edge

        for (var zoom = 0; zoom <= maxZoom; zoom++)
        {
            var grid = 1 << zoom;
            var canvasSize = grid * tileSize;
            var canvas = DownsampleAreaAverage(map, canvasSize);
            var written = 0;

            for (var ty = 0; ty < grid; ty++)
            {
                for (var tx = 0; tx < grid; tx++)
                {
                    var tile = new ushort[sampleCount * sampleCount];
                    for (var y = 0; y < sampleCount; y++)
                    {
                        var srcY = Math.Clamp(ty * tileSize - 1 + y, 0, canvasSize - 1);
                        var srcRowBase = srcY * canvasSize;
                        var dstRowBase = y * sampleCount;
                        for (var x = 0; x < sampleCount; x++)
                        {
                            var srcX = Math.Clamp(tx * tileSize - 1 + x, 0, canvasSize - 1);
                            tile[dstRowBase + x] = canvas[srcRowBase + srcX];
                        }
                    }
                    WriteUshortsLE(tile, Path.Combine(tilesDir, $"{zoom}_{tx}_{ty}.bin"));
                    written++;
                }
            }

            Console.WriteLine($"  z={zoom} {grid}x{grid} = {written} height tiles ({sampleCount}x{sampleCount} " +
                              $"each incl. 1px border overlap + 1px normal halo, canvas {canvasSize}x{canvasSize})");
        }
    }

    /// <summary>Area-average-downsamples the native height array to n x n. Recomputed
    /// independently from the native array at every zoom (not chained from the previous,
    /// coarser level) to match amc-web's TileGenerator, which resizes from the original
    /// source image at every zoom rather than progressively halving.</summary>
    private static ushort[] DownsampleAreaAverage(LandscapeExtractor.Map map, int n)
    {
        var heights = new ushort[n * n];
        var cellW = (double)map.Width / n;
        var cellH = (double)map.Height / n;

        for (var gy = 0; gy < n; gy++)
        {
            var y0 = (int)Math.Floor(gy * cellH);
            var y1 = Math.Max(y0 + 1, (int)Math.Floor((gy + 1) * cellH));
            for (var gx = 0; gx < n; gx++)
            {
                var x0 = (int)Math.Floor(gx * cellW);
                var x1 = Math.Max(x0 + 1, (int)Math.Floor((gx + 1) * cellW));

                long sum = 0;
                var count = 0;
                for (var y = y0; y < y1 && y < map.Height; y++)
                {
                    var rowBase = (long)y * map.Width;
                    for (var x = x0; x < x1 && x < map.Width; x++)
                    {
                        sum += map.Heights[rowBase + x];
                        count++;
                    }
                }
                heights[gy * n + gx] = count > 0 ? (ushort)Math.Round((double)sum / count) : (ushort)0;
            }
        }

        return heights;
    }

    private static void WriteUshortsLE(ushort[] values, string path)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < bytes.Length; i += 2) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>8-bit min/max-normalized preview, resampled so the longer edge is
    /// debugSize px (aspect ratio preserved - the map isn't square) - a raw 16-bit
    /// heightmap looks almost solid black in ordinary viewers otherwise.</summary>
    private static void WriteDebugPreview(Image native, LandscapeExtractor.Map map, int debugSize, string debugDir)
    {
        Directory.CreateDirectory(debugDir);
        var scale = (double)debugSize / Math.Max(native.Width, native.Height);
        using var resized = Math.Abs(scale - 1.0) < 1e-9 ? native.Copy() : native.Resize(scale, kernel: Enums.Kernel.Cubic);

        var range = Math.Max(1, map.RawMax - map.RawMin);
        var normScale = 255.0 / range;
        using var preview = resized.Linear([normScale], [-map.RawMin * normScale], uchar: true)
            .Copy(interpretation: Enums.Interpretation.Bw);

        var previewPath = Path.Combine(debugDir, $"{Label}_preview.png");
        preview.Pngsave(previewPath, compression: 6);
        Console.WriteLine($"  wrote {previewPath} ({resized.Width}x{resized.Height}, 8-bit normalized preview)");
    }

    private static JObject BuildMetadata(LandscapeExtractor.Map map, double? oceanLevelCm, int tileSize, int maxZoom)
    {
        // worldZFormulaCm is linear and monotonically increasing in rawHeight, so the
        // native rawHeightRange's min/max map directly to the world-Z min/max - a safe
        // (if slightly conservative) bound for every tile too, since area-averaging can
        // only narrow the range further, never widen it.
        double ZMeters(ushort rawHeight) => (rawHeight - 32768) / 128.0;

        return new JObject
        {
            ["componentCount"] = map.ComponentCount,
            ["landscapeCount"] = map.LandscapeCount,
            ["nativeResolution"] = new JObject { ["width"] = map.Width, ["height"] = map.Height },
            ["heightsBin"] = new JObject
            {
                ["fileName"] = "heights.bin",
                ["dtype"] = "uint16",
                ["byteOrder"] = "little",
                ["layout"] = "row-major, one value per quad, native resolution - offset (row * width + col) * 2 bytes",
            },
            ["tiles"] = new JObject
            {
                ["directory"] = "tiles/",
                ["fileNamePattern"] = "{z}_{x}_{y}.bin",
                ["dtype"] = "uint16",
                ["byteOrder"] = "little",
                ["tileSize"] = tileSize,
                ["tileSampleCount"] = tileSize + 3,
                ["tileSampleInnerCount"] = tileSize + 1,
                ["maxZoom"] = maxZoom,
                ["layout"] = "Leaflet/OpenLayers XYZ scheme matching amc-web's color tiles: z0 is a " +
                    "1x1 grid, zN is 2^N x 2^N (tileSize defines this grid/world-size math), each tile " +
                    "file is tileSampleCount x tileSampleCount samples (tileSize+3: tileSampleInnerCount " +
                    "= tileSize+1 real position samples with a 1px border overlap so adjacent tiles " +
                    "share their boundary pixel exactly, plus 1 more halo sample beyond each edge purely " +
                    "for computing vertex normals that agree exactly with the neighbouring tile at the " +
                    "shared edge), row-major, raw height units (not meters - apply worldZFormulaCm " +
                    "client-side). x/col and y/row both increase together with world X/Y, same as " +
                    "heights.bin - no flip in either axis. Every zoom is a genuine area-average " +
                    "downsample of the native heights.bin, recomputed independently per zoom (not " +
                    "chained from a coarser level).",
                ["widthMeters"] = map.Width * map.QuadScaleCm / 100.0,
                ["heightMeters"] = map.Height * map.QuadScaleCm / 100.0,
                ["originMetersX"] = map.OriginXCm / 100.0,
                ["originMetersY"] = map.OriginYCm / 100.0,
                ["minZMeters"] = ZMeters(map.RawMin),
                ["maxZMeters"] = ZMeters(map.RawMax),
            },
            ["originCm"] = new JObject { ["X"] = map.OriginXCm, ["Y"] = map.OriginYCm },
            ["quadScaleCm"] = map.QuadScaleCm,
            ["rawHeightRange"] = new JObject { ["min"] = map.RawMin, ["max"] = map.RawMax },
            ["worldXFormulaCm"] = "originCm.X + col * quadScaleCm",
            ["worldYFormulaCm"] = "originCm.Y + row * quadScaleCm",
            ["worldZFormulaCm"] = "((rawHeight - 32768) / 128.0) * 100.0 - assumes the source " +
                "landscape's own actor Z origin is 0, true for every landscape in this map except " +
                "one small ~12-component islet (Z origin -21900cm, excluded by default anyway) not " +
                "distinguishable per-pixel in this combined canvas",
            ["heightmap16Format"] = "16-bit grayscale PNG, native resolution, one pixel per quad; pixel value is rawHeight",
            ["ocean"] = oceanLevelCm is null ? null : new JObject
            {
                ["levelCm"] = oceanLevelCm,
                ["levelMeters"] = oceanLevelCm / 100.0,
                ["source"] = "MTOceanConfig.OceanConfig.OceanLevel in the persistent Jeju_World level - " +
                    "cross-verified against WaterBodyOcean's own WaterBodyOceanComponent.RelativeLocation.Z, " +
                    "which matches exactly",
            },
        };
    }
}
