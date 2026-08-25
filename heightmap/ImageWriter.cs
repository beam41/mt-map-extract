using System.Linq;
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

    /// <summary>Inner sample resolution (samples per tile edge, before the +1
    /// border-overlap / +1 normal-halo padding) - uniform across every zoom level,
    /// the standard Leaflet/OpenLayers/Slippy-map tile-pyramid convention: each
    /// tile covers a shrinking world-space rect as zoom increases (the grid
    /// quadruples per level), so a FIXED per-tile sample count already yields
    /// exponentially finer absolute (world-units-per-sample) detail at higher
    /// zoom - no need to also grow the per-tile sample count itself. Every stored
    /// inner sample becomes exactly one mesh vertex in the viewer (nothing unused,
    /// nothing resampled - see script/terrain-viewer/src/tileGeometry.ts's
    /// buildTileGeometry, which derives its mesh resolution from the fetched .bin
    /// tile directly, not from a hardcoded table, so the two can never drift).
    /// z0 is unused/never rendered (the viewer force-refines below z1).</summary>
    /// `tileSize` is the mesh-vertex resolution per tile edge (the old fixed 32),
    /// set by --tile-size; resolution(z) = tileSize * 2^z.
    public static void Write(LandscapeExtractor.Map map, double? oceanLevelCm, int maxZoom, int tileSize, int debugSize, string outDir)
    {
        Directory.CreateDirectory(outDir);

        using var native = Image.NewFromMemory(map.Heights, map.Width, map.Height, 1,
            Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Grey16);

        var heightmapPath = Path.Combine(outDir, $"{Label}_heightmap16.png");
        native.Pngsave(heightmapPath, compression: 6);
        Console.WriteLine($"  wrote {heightmapPath} ({map.Width}x{map.Height}, 16-bit, native resolution)");

        Output.Write(Path.Combine(outDir, $"{Label}.json"),
            BuildMetadata(map, oceanLevelCm, maxZoom, tileSize).ToString(Newtonsoft.Json.Formatting.Indented));

        // tiles.json - the CONSUMER-READY metadata (the viewer's public/assets/tiles.json
        // is a straight copy of this file by scripts/prepare-assets.js, which does no
        // reshaping): a flat subset of the fields above that the terrain viewer reads.
        // dtype/byteOrder/colorExtension are format invariants, not data - the viewer
        // hardcodes them.
        Output.Write(Path.Combine(outDir, "tiles.json"),
            BuildViewerTilesJson(map, oceanLevelCm, maxZoom, tileSize).ToString(Newtonsoft.Json.Formatting.Indented));

        WriteHeightsBin(map, Path.Combine(outDir, "heights.bin"));
        WriteHeightTiles(map, maxZoom, tileSize, Path.Combine(outDir, "tiles"));
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
    /// (z, x, y) and know they cover the exact same world-space rectangle - the world
    /// rect per tile is governed purely by the grid (zN = 2^N x 2^N tiles over
    /// widthMeters x heightMeters), matching amc-web's color tiles exactly, and the
    /// *sample density within* that rect is TileInnerResolution (see its own doc
    /// comment) at every zoom, so every stored sample becomes one mesh vertex in the
    /// viewer - nothing unused. `maxZoom` defaults to 5, matching amc-web's own
    /// default `--zoom` (also 5 - one level past its native zoom of 4 for its 4096px
    /// map at its default tile size): amc-web's z5 color tiles are its own upscaled
    /// level (upscaling raw *color* pixels is a reasonable one-zoom overscroll
    /// tradeoff amc-web already makes by default), while this project's z5 height
    /// tiles are a genuine area-average downsample of the native ~11000x11000
    /// heightmap, not an upscale - real elevation detail, not interpolated. Every
    /// level here is a genuine downsample of the native data, never chained from a
    /// coarser level.
    ///
    /// Each tile file actually stores `(inner+2) x (inner+2)` samples, not just
    /// `inner x inner`: a 1px border overlap (as above) *plus* one more "halo" sample
    /// beyond each edge, purely so `script/terrain-viewer` can compute vertex normals
    /// via central finite differences that are bit-identical between two adjacent
    /// tiles at their shared edge. A tile's own triangles alone can only see one side
    /// of a boundary vertex, so per-tile `computeVertexNormals()` (face-normal
    /// averaging restricted to that tile's own mesh) gives every boundary vertex a
    /// normal skewed toward its own tile's interior - systematically different from
    /// its neighbour's skewed-the-other-way normal for the textually same world point,
    /// which reads as a lighting-discontinuity seam even when the *position* data
    /// matches exactly (a real, separate bug from the plain 1px-overlap position fix
    /// above - reported as "seam is still a problem" after that fix alone). The extra
    /// halo sample gives each tile's own data everything needed for a symmetric
    /// central difference at its own edge, and because both tiles derive that halo
    /// sample from the exact same deterministic area-average of the same canvas
    /// region, the two independently-computed edge normals come out numerically
    /// identical. The outermost edge of the whole pyramid (no real neighbour) clamps
    /// the halo to the last valid canvas pixel, same as the border-overlap
    /// sample.</summary>
    private static void WriteHeightTiles(LandscapeExtractor.Map map, int maxZoom, int tileSize, string tilesDir)
    {
        // The pyramid shape changed (per-zoom resolutions, no z0) - clear any stale
        // tiles from a previous run rather than leaving old-format files behind.
        if (Directory.Exists(tilesDir)) Directory.Delete(tilesDir, recursive: true);
        Directory.CreateDirectory(tilesDir);

        // z0 is never rendered (see TileInnerResolution) - start at z1. Every zoom's
        // bin tile stores the same (TileInnerResolution + 2) samples per edge: `inner`
        // position samples (one per viewer mesh vertex, so nothing stored is ever
        // unused - see TileInnerResolution's doc comment) plus a 1px border overlap +
        // 1px normal halo per edge (same scheme the old uniform tileSize+3 used).
        //
        // The canvas step between adjacent tiles is `inner - 1`, NOT `inner`: with
        // `inner` samples per tile covering the tile's world rect, tile tx's last
        // sample and tile tx+1's first sample must be the *same* canvas sample so the
        // two tiles' shared edge reads the identical height (this is the whole point
        // of the 1px border overlap - losing it made every same-zoom tile boundary
        // read two different neighbouring canvas pixels and rendered as visible gaps
        // between tiles). The full canvas is therefore grid*(inner-1)+1 samples.
        var inner = tileSize;
        var step = inner - 1;
        var sampleCount = inner + 2; // inner position samples + 1px border overlap + 1px normal halo per edge
        for (var zoom = 1; zoom <= maxZoom; zoom++)
        {
            var grid = 1 << zoom;
            var canvasSize = grid * step + 1;
            var canvas = DownsampleAreaAverage(map, canvasSize);
            var written = 0;

            for (var ty = 0; ty < grid; ty++)
            {
                for (var tx = 0; tx < grid; tx++)
                {
                    var tile = new ushort[sampleCount * sampleCount];
                    for (var y = 0; y < sampleCount; y++)
                    {
                        var srcY = Math.Clamp(ty * step - 1 + y, 0, canvasSize - 1);
                        var srcRowBase = srcY * canvasSize;
                        var dstRowBase = y * sampleCount;
                        for (var x = 0; x < sampleCount; x++)
                        {
                            var srcX = Math.Clamp(tx * step - 1 + x, 0, canvasSize - 1);
                            tile[dstRowBase + x] = canvas[srcRowBase + srcX];
                        }
                    }
                    WriteUshortsLE(tile, Path.Combine(tilesDir, $"{zoom}_{tx}_{ty}.bin"));
                    written++;
                }
            }

            Console.WriteLine($"  z={zoom} {grid}x{grid} = {written} height tiles ({sampleCount}x{sampleCount} " +
                              $"each = {inner} inner + 1px border overlap + 1px normal halo, canvas {canvasSize}x{canvasSize})");
        }
    }

    /// <summary>Area-average-downsamples the native height array to n x n. Recomputed
    /// independently from the native array at every zoom (not chained from the previous,
    /// coarser level) to match amc-web's TileGenerator, which resizes from the original
    /// source image at every zoom rather than progressively halving.
    ///
    /// Deliberately a plain mean, not a max-preserving downsample: max-filtering was
    /// tried (to keep low-zoom peaks from flattening away) but produced more high-
    /// frequency jitter in the coarse tiles than the mean does, and the user preferred
    /// the smoother average look - "I think I prefer old average algorithm, less map
    /// jitter overall". Flat/ocean regions are unaffected either way (a uniform block
    /// averages to that value); the tradeoff is purely that isolated summits read
    /// somewhat lower on the coarsest zooms.</summary>
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

    private static JObject BuildMetadata(LandscapeExtractor.Map map, double? oceanLevelCm, int maxZoom, int tileSize)
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
                ["layout"] = "row-major, one value per quad, native resolution - offset (row * width + col) * 2 bytes",
            },
            ["tiles"] = new JObject
            {
                ["directory"] = "tiles/",
                ["fileNamePattern"] = "{z}_{x}_{y}.bin",
                ["maxZoom"] = maxZoom,
                ["tileInnerResolution"] = tileSize,
                ["tileSampleCount"] = tileSize + 2,
                ["layout"] = "Leaflet/OpenLayers XYZ scheme matching amc-web's color tiles: z0 is never " +
                    "generated (the viewer force-refines below z1), zN is 2^N x 2^N (grid/world-size math " +
                    "is by grid, matching amc-web's color tiles), each tile file is tileSampleCount x " +
                    "tileSampleCount samples = tileInnerResolution + 2 (inner position samples - " +
                    "one per viewer mesh vertex, so nothing is unused - plus a 1px border overlap so adjacent " +
                    "tiles share their boundary pixel exactly, plus 1 more halo sample beyond each edge purely " +
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
                ["minZ"] = ZMeters(map.RawMin),
                ["maxZ"] = ZMeters(map.RawMax),
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

    /// <summary>The terrain viewer's metadata file (tiles.json): exactly the fields
    /// src/heightmap.ts's loadTilesMeta() reads, in its final shape - no renaming or
    /// reshaping by the copy step. minZ/maxZ are world-space meters (ZMeters), the
    /// same values as Jeju_World.json's tiles.minZ/maxZ.</summary>
    private static JObject BuildViewerTilesJson(LandscapeExtractor.Map map, double? oceanLevelCm, int maxZoom, int tileSize)
    {
        double ZMeters(ushort rawHeight) => (rawHeight - 32768) / 128.0;

        return new JObject
        {
            ["maxZoom"] = maxZoom,
            ["tileInnerResolution"] = tileSize,
            ["tileSampleCount"] = tileSize + 2,
            ["widthMeters"] = map.Width * map.QuadScaleCm / 100.0,
            ["heightMeters"] = map.Height * map.QuadScaleCm / 100.0,
            ["originMetersX"] = map.OriginXCm / 100.0,
            ["originMetersY"] = map.OriginYCm / 100.0,
            ["minZ"] = ZMeters(map.RawMin),
            ["maxZ"] = ZMeters(map.RawMax),
            ["oceanLevelMeters"] = oceanLevelCm is null ? null : oceanLevelCm / 100.0,
        };
    }
}
