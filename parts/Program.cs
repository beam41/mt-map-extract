using MtExtract;

namespace Parts;

/// <summary>
/// Standalone vehicle-parts extractor for Motor Town: reads the VehicleParts and Vehicles
/// data tables and writes out_vehicle_part.json, out_vehicle_part_type_name.json,
/// out_vehicle.json and the per-part wiki pages under out/wiki/parts/. Mirrors the main
/// extractor's mounting (same resource/, options and output conventions) but touches no
/// world or map data. Run it from the repo root:
///
///     dotnet run -c Release --project parts
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
            Console.Error.WriteLine();
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
            return 2;
        }

        using var assets = new AssetSource(opts);
        Console.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files ({opts.Game})");
        if (assets.FileCount == 0)
        {
            Console.Error.WriteLine("error: nothing mounted - wrong AES key?");
            return 1;
        }

        var localization = assets.LoadLocalization();
        Console.WriteLine($"  {localization.Languages.Count} languages: {string.Join(", ", localization.Languages)}");

        var parts = new PartExtractor(assets, localization);

        Output.WriteJson(opts.Out("out_vehicle_part.json"), parts.VehicleParts(), "vehicle parts");
        Output.WriteJson(opts.Out("out_vehicle_part_type_name.json"), parts.PartTypeNames(), "part type names");
        Output.WriteJson(opts.Out("out_vehicle.json"), parts.Vehicles(), "vehicles");

        var wiki = parts.WikiParts(opts.Out("wiki/parts"));
        Console.WriteLine($"  out/wiki/parts/ {wiki,4} part pages");

        return 0;
    }
}
