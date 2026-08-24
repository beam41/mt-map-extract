using MtExtract;

namespace HeightmapExtractor;

/// <summary>
/// Stitches Jeju_World's Landscape elevation data into a high-resolution heightmap PNG.
/// See .agents/knowledge/landscape-heightmap.md for the pak layout, decode formula, and
/// world-transform background this project relies on.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Options opts;
        try
        {
            opts = Options.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        if (opts.ShowHelp)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        if (!File.Exists(opts.PakPath))
        {
            Console.Error.WriteLine($"error: pak not found: {opts.PakPath}");
            return 1;
        }

        using var assets = new AssetSource(new PakOptions(opts.PakPath, opts.AesKey, opts.UsmapPath, opts.Game));
        Console.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files ({opts.Game})");
        if (assets.FileCount == 0)
        {
            Console.Error.WriteLine("error: nothing mounted - wrong AES key?");
            return 1;
        }

        if (opts.DebugTiles)
        {
            var tilesDir = Path.Combine(opts.OutDir, "debug", "tiles");
            Console.WriteLine($"dumping unstitched tiles to {tilesDir}...");
            TileDumper.DumpAll(assets, opts.DebugGuidFilter, tilesDir);
            Console.WriteLine("Done.");
            return 0;
        }

        double? originX = opts.DebugAutoFit ? null : opts.OriginXCm;
        double? originY = opts.DebugAutoFit ? null : opts.OriginYCm;
        double? mapSize = opts.DebugAutoFit ? null : opts.MapSizeCm;
        var map = LandscapeExtractor.Extract(assets, opts.DebugGuidFilter, opts.ExcludeGuids, originX, originY, mapSize);

        if (map.ComponentCount == 0)
        {
            Console.Error.WriteLine("error: no matching landscape component found");
            return 1;
        }

        Console.WriteLine($"{map.ComponentCount} components across {map.LandscapeCount} landscape(s), " +
                          $"native {map.Width}x{map.Height}, raw height {map.RawMin}..{map.RawMax}");

        var oceanLevelCm = OceanExtractor.FindOceanLevelCm(assets);
        Console.WriteLine(oceanLevelCm is not null
            ? $"ocean level: {oceanLevelCm}cm ({oceanLevelCm / 100.0}m)"
            : "warning: no MTOceanConfig found - ocean level omitted from metadata");

        Console.WriteLine($"writing to {opts.OutDir}...");
        ImageWriter.Write(map, oceanLevelCm, opts.MaxZoom, opts.DebugSize, opts.OutDir);

        Console.WriteLine("Done.");
        return 0;
    }
}
