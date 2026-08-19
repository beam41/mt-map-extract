using System.Collections.Concurrent;
using System.Net.Http;
using MtExtract;

namespace WikiGenerator;

/// <summary>
/// The wiki generator: reads the pak directly and writes the DokuWiki source of every
/// generated page as plain .txt files — no intermediate JSON.
///
/// The four "detail" page types (vehicles, parts, cargos, delivery points) split into two
/// bot-owned subpages each — {slug}:auto_infobox (just the infobox) and {slug}:auto_details
/// (specs onward), both fully regenerated every run — plus a heading+intro that's
/// generated once into a live shell page a human curator owns:
///
///   {{page>{ns}:{slug}:auto_infobox}}
///
///   ====== Name ======
///   **Name** is a ... in Motor Town.
///
///   (hand-written prose goes here — anything at all)
///
///   {{page>{ns}:{slug}:auto_details}}
///
/// via the DokuWiki `include` plugin's {{page>}} transclusion, so a curator's prose (and
/// field is hand-curated too (no pak source) — every run fetches the live wiki's current
/// page for every detail-page entity (the new auto_infobox subpage if it exists, else the
/// legacy flat page, since most pages haven't been migrated to this structure yet) and
/// merges whatever image line it finds back into the freshly rendered infobox before
/// writing it. `--bootstrap` (off by default — a one-time deployment step, not a normal
/// run) additionally writes the shell page itself, at its real live-wiki path
/// (`{ns}/{slug}.txt`, sibling of the `{ns}/{slug}/` subpage directory — exactly how
/// DokuWiki's own file-per-page layout works): an infobox transclusion, the heading+intro
/// generated once as literal text, and a details transclusion. Deploy it by dropping the
/// whole `out/wiki/` tree onto the live wiki's page directory once; after that the
/// generator's normal (non-bootstrap) runs never touch the shell path again, only the two
/// `:auto_*` subpages, so any hand-written prose added to a shell after deployment is
/// permanent. List/aggregate pages (list_of_*, vehicle_comparison, cargo_space,
/// cargo_type, installable_parts/installable_vehicles) have no curatable seam and stay
/// single-page, fully generator-owned, with or without --bootstrap.
///
///   wiki/generate/                this program, LiveWiki.cs (image fetch/merge)
///   wiki/out/vehicles/{slug}/     auto_infobox.txt, auto_details.txt, installable_parts.txt
///   wiki/out/vehicles/{slug}.txt  (--bootstrap only) the shell page, real path
///   wiki/out/parts/{slug}/        auto_infobox.txt, auto_details.txt, installable_vehicles.txt
///   wiki/out/parts/{slug}.txt     (--bootstrap only)
///   wiki/out/cargos/{key}/        auto_infobox.txt, auto_details.txt
///   wiki/out/cargos/{key}.txt     (--bootstrap only)
///   wiki/out/delivery_points/{slug}/  auto_infobox.txt, auto_details.txt
///   wiki/out/delivery_points/{slug}.txt  (--bootstrap only)
///   wiki/out/cargo_space/, cargo_type/, list_of_*.txt, vehicle_comparison.txt
///
/// Run: dotnet run -c Release --project wiki/generate [--bootstrap]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
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
            Console.WriteLine("  --bootstrap   also (re)generate the shell page itself at its real path (one-time deploy step)");
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

        // every detail-page entity, keyed by (namespace, slug) — the unit both the live
        // image fetch and the page writer below iterate over
        var vehicleSlugs = data.Vehicles.Select(v => ("vehicles", RenderVehicles.VehicleSlug(v))).ToList();
        var partSlugs = data.Parts.Where(p => p.HasPage).Select(p => ("parts", p.Slug)).ToList();
        var cargoSlugs = data.Cargos.Where(c => !c.Deprecated).Select(c => ("cargos", c.Key.ToLowerInvariant())).ToList();
        var deliverySlugs = data.Points.Where(p => p.HasPage).Select(p => ("delivery_points", p.Slug)).ToList();
        var detailTargets = vehicleSlugs.Concat(partSlugs).Concat(cargoSlugs).Concat(deliverySlugs).ToList();

        Console.WriteLine($"  fetching live image fields for {detailTargets.Count} pages...");
        var imageLines = new ConcurrentDictionary<(string Ns, string Slug), string?>();
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
        {
            var gate = new SemaphoreSlim(8);
            await Task.WhenAll(detailTargets.Select(async target =>
            {
                await gate.WaitAsync();
                try
                {
                    imageLines[target] = await LiveWiki.FetchImageLine(http, target.Item1, target.Item2);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        Console.WriteLine($"  found {imageLines.Values.Count(v => v is not null)} hand-curated image fields to preserve");

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
        var typeDir = Path.Combine(outDir, "cargo_type");
        var deliveryDir = Path.Combine(outDir, "delivery_points");
        foreach (var dir in new[] { vehicleDir, partDir, cargoDir, spaceDir, typeDir, deliveryDir })
            Directory.CreateDirectory(dir);

        // A "detail" page (vehicle/part/cargo/delivery point) writes two bot-owned
        // subpages ({slug}:auto_infobox with the live image field merged back in,
        // {slug}:auto_details) plus, with --bootstrap, the shell page itself at its real
        // live-wiki path ({ns}/{slug}.txt, sibling of the {ns}/{slug}/ subpage directory):
        // an infobox transclusion, the heading+intro generated once as literal text, and
        // a details transclusion — ready to drop straight onto the live wiki once.
        void WriteDetailPage(string pagesDir, string ns, string slug, string infobox, string heading, string info)
        {
            var mergedInfobox = LiveWiki.MergeImage(infobox, imageLines.GetValueOrDefault((ns, slug)));
            var subDir = Path.Combine(pagesDir, slug);
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "auto_infobox.txt"), mergedInfobox);
            File.WriteAllText(Path.Combine(subDir, "auto_details.txt"), info);

            if (!opts.Bootstrap) return;
            var shell = "{{page>" + ns + ":" + slug + ":auto_infobox&nodate&nomdate}}\n\n"
                        + heading + "\n\n"
                        + "{{page>" + ns + ":" + slug + ":auto_details&nodate&nomdate}}\n";
            File.WriteAllText(Path.Combine(pagesDir, slug + ".txt"), shell);
        }

        // vehicle pages (every vehicle, incl. the broken assets and trailers)
        foreach (var v in data.Vehicles)
        {
            var slug = RenderVehicles.VehicleSlug(v);
            WriteDetailPage(vehicleDir, "vehicles", slug,
                RenderVehicles.VehiclePageInfobox(v, data), RenderVehicles.VehiclePageHeading(v), RenderVehicles.VehiclePageDetails(v, data));
            File.WriteAllText(Path.Combine(vehicleDir, slug, "installable_parts.txt"),
                RenderParts.InstallablePartsPage(v, data.InstallableParts(v)));
        }

        // part pages (RideHeight_-N have no page, matching the wiki)
        foreach (var p in data.Parts)
        {
            if (!p.HasPage) continue;
            WriteDetailPage(partDir, "parts", p.Slug,
                RenderParts.PartPageInfobox(p), RenderParts.PartPageHeading(p), RenderParts.PartPageDetails(p));
            File.WriteAllText(Path.Combine(partDir, p.Slug, "installable_vehicles.txt"),
                RenderParts.InstallableVehiclesPage(p, data.InstallableVehicles(p)));
        }

        // cargo pages (active only)
        foreach (var c in data.Cargos.Where(c => !c.Deprecated))
            WriteDetailPage(cargoDir, "cargos", c.Key.ToLowerInvariant(),
                RenderCargos.CargoPageInfobox(c, data), RenderCargos.CargoPageHeading(c, data), RenderCargos.CargoPageDetails(c, data));

        // cargo space pages (aggregate — no curatable seam, single page)
        foreach (var s in data.Spaces)
            File.WriteAllText(Path.Combine(spaceDir, s.Type.ToLowerInvariant() + ".txt"), RenderCargos.CargoSpacePage(s));

        // cargo type pages (aggregate — no curatable seam, single page)
        foreach (var t in data.CargoTypes)
            File.WriteAllText(Path.Combine(typeDir, t.Type.ToLowerInvariant() + ".txt"), RenderCargos.CargoTypePage(t, data));

        // delivery point pages (one per real-world placement with a resolvable name)
        foreach (var p in data.Points.Where(p => p.HasPage))
            WriteDetailPage(deliveryDir, "delivery_points", p.Slug,
                RenderDelivery.DeliveryPointPageInfobox(p, data), RenderDelivery.DeliveryPointPageHeading(p), RenderDelivery.DeliveryPointPageDetails(p, data));

        // list pages (aggregate — always fully generated, no curatable seam)
        File.WriteAllText(Path.Combine(outDir, "list_of_parts.txt"), RenderParts.ListOfParts(data.Parts));
        File.WriteAllText(Path.Combine(outDir, "list_of_vehicles.txt"), RenderVehicles.ListOfVehicles(data.Vehicles));
        File.WriteAllText(Path.Combine(outDir, "list_of_cargos.txt"), RenderCargos.ListOfCargos(data.Cargos, data.CargoTypes, data));
        File.WriteAllText(Path.Combine(outDir, "vehicle_comparison.txt"), RenderVehicles.Comparison(data.Vehicles, data));
        File.WriteAllText(Path.Combine(outDir, "list_of_delivery_points.txt"), RenderDelivery.ListOfDeliveryPoints(data.Points));

        var bootstrapFiles = opts.Bootstrap ? detailTargets.Count : 0;
        var txtFiles = Directory.EnumerateFiles(outDir, "*.txt", SearchOption.AllDirectories).Count() - bootstrapFiles;
        Console.WriteLine(opts.Bootstrap
            ? $"  wrote {txtFiles} DokuWiki pages + {bootstrapFiles} shell pages (real paths, one-time deploy) to {outDir}/"
            : $"  wrote {txtFiles} DokuWiki pages to {outDir}/ (pass --bootstrap to also write the shell pages once)");
        return 0;
    }
}
