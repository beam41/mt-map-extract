using System.Collections.Concurrent;
using System.Diagnostics;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// One pass over MotorTown-Windows.pak that produces every out_*.json the site needs, plus the
/// world map png. Replaces the old dump-to-MotorTown/ + Rust + Node pipeline.
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

        var total = Stopwatch.StartNew();
        using var assets = new AssetSource(opts);
        Console.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files ({opts.Game})");
        if (assets.FileCount == 0)
        {
            Console.Error.WriteLine("error: nothing mounted - wrong AES key?");
            return 1;
        }

        if (opts.SkipJson)
        {
            Console.WriteLine("Skipping json extraction");
        }
        else
        {
            var localization = Step("Loading localization", assets.LoadLocalization);
            Console.WriteLine($"  {localization.Languages.Count} languages: {string.Join(", ", localization.Languages)}");

            var world = new WorldExtractor(assets);
            var tables = new TableExtractor(assets, localization);

            void Write(string fileName, JToken json, string label) =>
                Output.WriteJson(opts.Out(fileName), opts.Amc ? Output.AmcNames(json) : json, label);

            Step("Reading world", () =>
            {
                Write("out_area_volume.json", tables.LocalizeNames(world.AreaVolumes()), "areas");
                Write("out_delivery_point.json", tables.LocalizeNames(world.DeliveryPoints()), "delivery points");

                Write("out_bus_stop.json", world.BusStops(), "bus stops");
                Write("out_house.json", world.Houses(), "houses");
                return true;
            });

            Step("Reading data tables", () =>
            {
                var (cargoKeys, cargoMetadata) = tables.CargoMaps();
                Write("out_cargo_key.json", cargoKeys, "cargo types");
                Write("out_cargo_metadata.json", cargoMetadata, "cargos");
                Write("out_cargo_name.json", tables.CargoNames(), "cargo names");
                Write("out_cargo_type_name.json", tables.CargoTypeNames(), "cargo type names");
                Write("out_vehicles_name.json", tables.VehicleNames(), "vehicle names");
                return true;
            });
        }

        var failed = 0;
        if (!opts.SkipMap || !opts.SkipTiles)
        {
            var png = DecodeMapTexture(assets, opts);
            if (png is null) failed++;
            else
            {
                if (!opts.SkipMap)
                {
                    Output.Write(opts.MapOut, png);
                    Console.WriteLine($"Wrote {opts.MapOut}");
                }
                if (!opts.SkipTiles) TileGenerator.Generate(png, opts);
            }
        }

        if (opts.DumpDir is not null) DumpPackages(assets, opts);

        Console.WriteLine($"Done in {total.Elapsed:mm\\:ss}");
        return failed == 0 ? 0 : 1;
    }

    private static T Step<T>(string label, Func<T> body)
    {
        Console.WriteLine(label + "...");
        var sw = Stopwatch.StartNew();
        var result = body();
        Console.WriteLine($"  took {sw.Elapsed:mm\\:ss\\.ff}");
        return result;
    }

    /// <summary>Decodes the world map Texture2D to png bytes, the source for map.png and the tiles.</summary>
    private static byte[]? DecodeMapTexture(AssetSource assets, Options opts)
    {
        try
        {
            var path = opts.MapTexture.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? opts.MapTexture[..^".uasset".Length]
                : opts.MapTexture;

            var texture = assets.Provider.LoadPackageObject<UTexture2D>(path);
            var decoded = texture.Decode(assets.Provider.Versions.Platform)
                          ?? throw new InvalidOperationException("texture decode returned null");

            Console.WriteLine($"Map texture {decoded.Width}x{decoded.Height} ({texture.Format})");
            return decoded.Encode(ETextureFormat.Png, false, out _);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: failed to export {opts.MapTexture}: {e.Message}");
            return null;
        }
    }

    /// <summary>Optional FModel-style dump of every package under the game root, for digging around.</summary>
    private static void DumpPackages(AssetSource assets, Options opts)
    {
        var files = assets.Files(opts.Root)
            .Where(f => f.Extension is "uasset" or "umap" or "locres" or "locmeta")
            .ToArray();

        Console.WriteLine($"Dumping {files.Length} assets to {opts.DumpDir}/");
        var sw = Stopwatch.StartNew();
        var done = 0;
        var errors = new ConcurrentQueue<string>();

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = opts.Threads }, file =>
        {
            try
            {
                var relative = file.Path[opts.Root.Length..].TrimStart('/');
                var outPath = Path.Combine(opts.DumpDir!,
                    Path.ChangeExtension(relative, ".json").Replace('/', Path.DirectorySeparatorChar));
                assets.WritePackageJson(file, outPath);
            }
            catch (Exception e)
            {
                errors.Enqueue($"{file.Path}: {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}");
            }

            var n = Interlocked.Increment(ref done);
            if (n % 500 == 0 || n == files.Length)
                Console.Write($"\r  {n}/{files.Length} ({sw.Elapsed:mm\\:ss}, {errors.Count} failed)   ");
        });
        Console.WriteLine();

        if (!errors.IsEmpty)
        {
            var log = Path.Combine(opts.DumpDir!, "extract-errors.log");
            Output.Write(log, string.Join(Environment.NewLine, errors.OrderBy(e => e, StringComparer.Ordinal)));
            Console.WriteLine($"  {errors.Count} assets failed, see {log}");
        }
    }
}
