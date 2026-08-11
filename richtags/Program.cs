using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace Richtags;

/// <summary>
/// Standalone rich-text tag finder for Motor Town. Mounts the pak, scans every
/// .uasset for DataTables whose row names include the rich-text tags shown in
/// tag.png (or whose row struct is a rich-text style), and reports every tag it
/// finds with the asset path(s) that define it. Does not touch the main extractor.
/// </summary>
internal static class Program
{
    // The 21 tags captured in tag.png (game's own rich text reference image).
    private static readonly string[] Baseline =
    {
        "Default", "Money", "InputKey", "Warning", "Highlight", "Focus",
        "Focus_Outline", "Bold", "Title", "Large", "Small", "Secondary",
        "Disabled", "Announce", "Company", "Event", "Whisper", "EffectGood",
        "EffectBad", "Chat",
    };

    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var resource = Path.Combine(root, "resource");
        var pakPath = Path.Combine(resource, "MotorTown-Windows.pak");
        var aesPath = Path.Combine(resource, "aes");
        var usmapPath = Path.Combine(resource, "Mappings.usmap");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pak" when i + 1 < args.Length: pakPath = args[++i]; break;
                case "--aes" when i + 1 < args.Length: aesPath = args[++i]; break;
                case "--usmap" when i + 1 < args.Length: usmapPath = args[++i]; break;
                default:
                    Console.Error.WriteLine($"unknown option: {args[i]}");
                    return 2;
            }
        }

        if (!File.Exists(pakPath)) { Console.Error.WriteLine($"pak not found: {pakPath}"); return 2; }
        if (!File.Exists(aesPath)) { Console.Error.WriteLine($"aes not found: {aesPath}"); return 2; }
        if (!File.Exists(usmapPath)) { Console.Error.WriteLine($"usmap not found: {usmapPath}"); return 2; }

        var aes = File.ReadAllText(aesPath).Trim().TrimStart('0', 'x');
        if (aes.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) aes = aes[2..];

        Console.WriteLine($"mounting {Path.GetFileName(pakPath)} ...");
        using var provider = new DefaultFileProvider(
            Path.GetDirectoryName(Path.GetFullPath(pakPath))!,
            SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_UE5_5));

        provider.RegisterVfs(new FileInfo(pakPath));
        provider.SubmitKey(new FGuid(), new FAesKey(aes));
        provider.Mount();
        provider.PostMount();
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath);

        Console.WriteLine($"mounted: {provider.Files.Count} files");
        if (provider.Files.Count == 0) { Console.Error.WriteLine("nothing mounted - wrong AES key?"); return 1; }

        var baseline = new HashSet<string>(Baseline, StringComparer.Ordinal);

        // tag -> asset paths that define it (style tags: RichTextStyleRow)
        var tags = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // tag -> asset paths that define it (image tags: RichImageRow, the <img id="..."> decorators)
        var imageTags = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // asset path -> (row struct, row count)
        var tables = new SortedDictionary<string, (string RowStruct, int Rows)>(StringComparer.Ordinal);
        var skipped = 0;

        var files = provider.Files.Values
            .Where(f => f.Path.StartsWith("MotorTown/", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Extension.Equals("uasset", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(f => f.Path)
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"scanning {files.Count} uassets ...");

        foreach (var file in files)
        {
            if (!provider.TryLoadPackage(file.Path, out var package)) { skipped++; continue; }

            foreach (var export in package.GetExports())
            {
                if (export is not UDataTable dt) continue;

                string rowStruct;
                IReadOnlyDictionary<FName, FStructFallback> rows;
                try
                {
                    rowStruct = dt.RowStructName;
                    rows = dt.RowMap;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"  row map failed for {file.Path}: {e.Message}");
                    continue;
                }

                var rowNames = rows.Keys.Select(k => k.ToString() ?? "").ToList();
                tables[file.Path] = (rowStruct, rowNames.Count);

                // Rich-text style tags (e.g. <Money>) live in tables whose rows are
                // RichTextStyleRow; image decorator tags (<img id="...">) are RichImageRow.
                // Anything else is game data, not rich text.
                var isStyle = rowStruct.Contains("RichText", StringComparison.OrdinalIgnoreCase)
                    || rowStruct.Contains("TextStyle", StringComparison.OrdinalIgnoreCase);
                var isImage = rowStruct.Contains("RichImage", StringComparison.OrdinalIgnoreCase);
                if (!isStyle && !isImage) continue;

                var into = isStyle ? tags : imageTags;
                foreach (var name in rowNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!into.TryGetValue(name, out var paths)) into[name] = paths = new HashSet<string>(StringComparer.Ordinal);
                    paths.Add(file.Path);
                }
            }
        }

        Console.WriteLine($"scanned {files.Count} uassets ({skipped} load failures), {tables.Count} data tables, {tags.Count} style tags, {imageTags.Count} image tags");
        Console.WriteLine();
        Console.WriteLine("rich text tables:");
        foreach (var (path, (rowStruct, rows)) in tables.Where(kv =>
                     kv.Value.RowStruct.Contains("RichText", StringComparison.OrdinalIgnoreCase)
                     || kv.Value.RowStruct.Contains("TextStyle", StringComparison.OrdinalIgnoreCase)
                     || kv.Value.RowStruct.Contains("RichImage", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  {path}  struct={rowStruct} rows={rows}");
        }
        Console.WriteLine();

        var all = tags.Keys.ToList();
        var newer = all.Where(t => !baseline.Contains(t)).ToList();
        var missing = baseline.Where(t => !tags.ContainsKey(t)).ToList();

        Console.WriteLine($"style tags: {all.Count}");
        Console.WriteLine($"new style tags (not in tag.png): {newer.Count}");
        Console.WriteLine($"image tags: {imageTags.Count}");
        Console.WriteLine($"baseline tags absent from pak: {missing.Count}");
        if (missing.Count > 0) Console.WriteLine("  " + string.Join(", ", missing));

        // Markdown summary
        var md = Path.Combine(root, "out", "rich_text_tags.md");
        Directory.CreateDirectory(Path.GetDirectoryName(md)!);
        using (var w = new StreamWriter(md))
        {
            w.WriteLine("# Motor Town rich text tags");
            w.WriteLine();
            w.WriteLine($"Found in `{Path.GetFileName(pakPath)}` on {DateTime.Now:yyyy-MM-dd}.");
            w.WriteLine();
            w.WriteLine($"- style tags (e.g. `<Money>`, from `RichTextStyleRow` tables): **{all.Count}**");
            w.WriteLine($"- new style tags (not in `tag.png`): **{newer.Count}**");
            w.WriteLine($"- image tags (e.g. `<img id=\"...\">`, from `RichImageRow` tables): **{imageTags.Count}**");
            w.WriteLine($"- baseline tags absent from the pak: **{missing.Count}**");
            if (missing.Count > 0) w.WriteLine($"  ({string.Join(", ", missing)})");
            w.WriteLine();

            w.WriteLine("## New style tags");
            w.WriteLine();
            if (newer.Count == 0)
            {
                w.WriteLine("_none_");
            }
            else
            {
                foreach (var t in newer)
                {
                    w.WriteLine($"- `{t}` — {string.Join(", ", tags[t].OrderBy(p => p))}");
                }
            }
            w.WriteLine();

            w.WriteLine("## All style tags");
            w.WriteLine();
            foreach (var t in all)
            {
                w.WriteLine($"- `{t}`{(baseline.Contains(t) ? " *(baseline)*" : " ***(new)***")} — {string.Join(", ", tags[t].OrderBy(p => p))}");
            }
            w.WriteLine();

            if (imageTags.Count > 0)
            {
                w.WriteLine("## Image tags (`<img id=\"...\">`)");
                w.WriteLine();
                foreach (var t in imageTags.Keys)
                {
                    w.WriteLine($"- `{t}` — {string.Join(", ", imageTags[t].OrderBy(p => p))}");
                }
                w.WriteLine();
            }

            w.WriteLine("## Rich text tables");
            w.WriteLine();
            w.WriteLine("| Asset | Row struct | Rows |");
            w.WriteLine("| --- | --- | --- |");
            foreach (var (path, (rowStruct, rows)) in tables.Where(kv =>
                         kv.Value.RowStruct.Contains("RichText", StringComparison.OrdinalIgnoreCase)
                         || kv.Value.RowStruct.Contains("TextStyle", StringComparison.OrdinalIgnoreCase)
                         || kv.Value.RowStruct.Contains("RichImage", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                w.WriteLine($"| `{path}` | {rowStruct} | {rows} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"wrote {md}");
        return 0;
    }
}
