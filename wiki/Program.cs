using MtExtract;

namespace WikiGenerator;

/// <summary>
/// The wiki generator: reads the pak directly and writes the DokuWiki source of every
/// generated page (vehicles, parts, cargos, cargo spaces, and the four list pages) as plain
/// .txt files — no intermediate JSON.
///
///   wiki/generate/        this program
///   wiki/out/vehicles/    one page per vehicle
///   wiki/out/parts/       one page per part (minus the unlisted RideHeight_-N rows)
///   wiki/out/cargos/      one page per active cargo
///   wiki/out/cargo_space/ one aggregate page per space type
///   wiki/out/list_of_*.txt, vehicle_comparison.txt   the list pages
///
/// Run: dotnet run -c Release --project wiki/generate
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        WikiOptions opts;
        try
        {
            opts = WikiOptions.Parse(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }

        if (opts.ShowHelp)
        {
            Console.WriteLine("wiki generator: read the pak and write every DokuWiki page as .txt");
            return 0;
        }

        if (!File.Exists(opts.PakPath))
        {
            Console.Error.WriteLine($"error: pak not found: {opts.PakPath}");
            return 2;
        }

        using var assets = new AssetSource(opts.Pak);
        Console.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files ({opts.Game})");
        if (assets.FileCount == 0)
        {
            Console.Error.WriteLine("error: nothing mounted - wrong AES key?");
            return 1;
        }

        var localization = assets.LoadLocalization();
        Console.WriteLine($"  {localization.Languages.Count} languages: {string.Join(", ", localization.Languages)}");

        var data = new Data(assets, localization);
        data.Gather();
        Console.WriteLine($"  {data.Vehicles.Count,3} vehicles, {data.Parts.Count,3} parts, {data.Cargos.Count,3} cargos, {data.Spaces.Count,2} spaces, {data.Points.Count,3} delivery points");

        // wipe previous output — this generator writes only DokuWiki txt
        var outDir = Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            "out", "wiki");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        var vehicleDir = Path.Combine(outDir, "vehicles");
        var partDir = Path.Combine(outDir, "parts");
        var cargoDir = Path.Combine(outDir, "cargos");
        var spaceDir = Path.Combine(outDir, "cargo_space");
        foreach (var dir in new[] { vehicleDir, partDir, cargoDir, spaceDir })
            Directory.CreateDirectory(dir);

        // vehicle pages (every vehicle, incl. the broken assets and trailers)
        foreach (var v in data.Vehicles)
        {
            var slug = RenderVehicles.VehicleSlug(v);
            File.WriteAllText(Path.Combine(vehicleDir, slug + ".txt"), RenderVehicles.VehiclePage(v, data));
        }

        // part pages (RideHeight_-N have no page, matching the wiki)
        foreach (var p in data.Parts)
        {
            if (!p.HasPage) continue;
            File.WriteAllText(Path.Combine(partDir, p.Slug + ".txt"), RenderParts.PartPage(p));
        }

        // cargo pages (active only)
        foreach (var c in data.Cargos.Where(c => !c.Deprecated))
            File.WriteAllText(Path.Combine(cargoDir, c.Key.ToLowerInvariant() + ".txt"), RenderCargos.CargoPage(c, data));

        // cargo space pages
        foreach (var s in data.Spaces)
            File.WriteAllText(Path.Combine(spaceDir, s.Type.ToLowerInvariant() + ".txt"), RenderCargos.CargoSpacePage(s));

        // list pages
        File.WriteAllText(Path.Combine(outDir, "list_of_parts.txt"), RenderParts.ListOfParts(data.Parts));
        File.WriteAllText(Path.Combine(outDir, "list_of_vehicles.txt"), RenderVehicles.ListOfVehicles(data.Vehicles));
        File.WriteAllText(Path.Combine(outDir, "list_of_cargos.txt"), RenderCargos.ListOfCargos(data.Cargos));
        File.WriteAllText(Path.Combine(outDir, "vehicle_comparison.txt"), RenderVehicles.Comparison(data.Vehicles, data));

        var txtFiles = Directory.EnumerateFiles(outDir, "*.txt", SearchOption.AllDirectories).Count();
        Console.WriteLine($"  wrote {txtFiles} DokuWiki pages to {outDir}/");
        return 0;
    }
}
