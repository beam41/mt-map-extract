using NetVips;
using Newtonsoft.Json.Linq;
using MtExtract;

namespace HeightmapExtractor;

/// <summary>Writes the combined map at native resolution, a raw binary for point queries,
/// a downsampled raw binary for the terrain-viewer web project, and a downscaled debug
/// preview.</summary>
internal static class ImageWriter
{
    private const string Label = "Jeju_World";

    public static void Write(LandscapeExtractor.Map map, int webSize, int debugSize, string outDir)
    {
        Directory.CreateDirectory(outDir);

        using var native = Image.NewFromMemory(map.Heights, map.Width, map.Height, 1,
            Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Grey16);

        var heightmapPath = Path.Combine(outDir, $"{Label}_heightmap16.png");
        native.Pngsave(heightmapPath, compression: 6);
        Console.WriteLine($"  wrote {heightmapPath} ({map.Width}x{map.Height}, 16-bit, native resolution)");

        var webFileName = $"heights_{webSize}px.bin";
        Output.Write(Path.Combine(outDir, $"{Label}.json"),
            BuildMetadata(map, webSize, webFileName).ToString(Newtonsoft.Json.Formatting.Indented));

        WriteHeightsBin(map, Path.Combine(outDir, "heights.bin"));
        WriteWebHeightsBin(map, webSize, Path.Combine(outDir, webFileName));
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

    /// <summary>Same raw uint16/little-endian/row-major encoding as heights.bin, but
    /// area-average-downsampled to n x n - small enough for `script/terrain-viewer` to
    /// copy straight into its BufferGeometry with no PNG decode or resampling of its own
    /// (11000x11000 is far more detail than any GPU mesh needs). Still raw height units,
    /// not pre-converted to world-Z meters - the consumer applies worldZFormulaCm itself,
    /// same as every other output here.</summary>
    private static void WriteWebHeightsBin(LandscapeExtractor.Map map, int n, string path)
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

        var bytes = new byte[heights.Length * 2];
        Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < bytes.Length; i += 2) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  wrote {path} ({n}x{n}, uint16 little-endian, area-averaged downsample, {bytes.Length:N0} bytes)");
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

    private static JObject BuildMetadata(LandscapeExtractor.Map map, int webSize, string webFileName)
    {
        // worldZFormulaCm is linear and monotonically increasing in rawHeight, so the
        // native rawHeightRange's min/max map directly to the world-Z min/max - a safe
        // (if slightly conservative) bound for the web bin too, since area-averaging can
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
            ["web"] = new JObject
            {
                ["fileName"] = webFileName,
                ["dtype"] = "uint16",
                ["byteOrder"] = "little",
                ["grid"] = webSize,
                ["layout"] = "row-major, area-averaged downsample of heights.bin to grid x grid - " +
                    "raw height units, not meters; apply worldZFormulaCm client-side",
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
        };
    }
}
