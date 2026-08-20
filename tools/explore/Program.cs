using System.Text.RegularExpressions;
using MtExtract;
using Newtonsoft.Json.Linq;

namespace Explore;

/// <summary>
/// Throwaway exploration harness for vehicle parts data. Commands:
///   find [pattern]      - list pak files whose path matches (regex, case-insensitive)
///   table <path>        - print the DataTable rows of <path> (names + property names, no values)
///   rows <path> <name>  - print one row's full json
///   props <path>        - print property names of every export in a package
///   grep <dir> <pattern> - dump packages under <dir> and grep the JSON for <pattern>
///   types <path>        - per PartType: row count + populated columns + sample
///   stats <path> <type> - per PartType: columns whose values vary across rows (real stats)
///   names <path>        - dump row names, one per line
///   veh <path> <name>   - print a vehicle row's restrictions only
///   nearbox <path> <x1> <y1> <x2> <y2> - every actor in a world package whose
///                         RootComponent/DefaultSceneRoot/Scene RelativeLocation falls
///                         inside the coordinate box, with type + label + coord
///   wpscan [pattern]    - scan every Jeju_World World Partition cell (_Generated_/*.umap,
///                         invisible to grep/find's uasset-only filters) for an export
///                         type-name regex match (default "Kart"); Jeju_World.umap alone is
///                         only the always-loaded persistent level
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pakPath = Path.Combine("resource", "MotorTown-Windows.pak");
        if (!File.Exists(pakPath))
            pakPath = Path.Combine(root, "resource", "MotorTown-Windows.pak");
        var opts = new PakOptions(pakPath,
            File.ReadAllText(Path.Combine(root, "resource", "aes")).Trim(),
            Path.Combine(root, "resource", "Mappings.usmap"),
            CUE4Parse.UE4.Versions.EGame.GAME_UE5_5);

        using var assets = new AssetSource(opts);
        Console.Error.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files");

        var command = args.Length == 0 ? "help" : args[0];
        switch (command)
        {
            case "find":
                return Find(assets, args.Length > 1 ? args[1] : ".");
            case "table":
                return Table(assets, args[1]);
            case "rows":
                return Rows(assets, args[1], args[2]);
            case "props":
                return Props(assets, args[1]);
            case "grep":
                return Grep(assets, args[1], args[2]);
            case "types":
                return Types(assets, args[1]);
            case "nearbox":
            {
                // nearbox <path> <x1> <y1> <x2> <y2> - every actor whose RootComponent (or
                // DefaultSceneRoot/Scene/SceneComponent fallback) RelativeLocation falls
                // inside the box, name + type + coord.
                var package = assets.RequirePackage(args[1]);
                var x1 = double.Parse(args[2]); var y1 = double.Parse(args[3]);
                var x2 = double.Parse(args[4]); var y2 = double.Parse(args[5]);
                var (xlo, xhi) = (Math.Min(x1, x2), Math.Max(x1, x2));
                var (ylo, yhi) = (Math.Min(y1, y2), Math.Max(y1, y2));
                for (var i = 0; i < package.Exports.Count; i++)
                {
                    var obj = package.Json(i);
                    var props = obj["Properties"] as JObject;
                    var sceneRef = props?["RootComponent"] as JObject ?? props?["DefaultSceneRoot"] as JObject
                        ?? props?["Scene"] as JObject ?? props?["SceneComponent"] as JObject;
                    var path = (string?)sceneRef?["ObjectPath"];
                    var dot = path?.LastIndexOf('.') ?? -1;
                    if (dot < 0 || !int.TryParse(path![(dot + 1)..], out var sceneIndex)) continue;
                    var loc = package.Json(sceneIndex)["Properties"]?["RelativeLocation"];
                    var x = (double?)loc?["X"]; var y = (double?)loc?["Y"];
                    if (x is null || y is null || x < xlo || x > xhi || y < ylo || y > yhi) continue;
                    var label = (string?)obj["ActorLabel"] ?? "";
                    Console.WriteLine($"export {i} ({package.Exports[i].ExportType}) \"{label}\": x={x} y={y}");
                }
                return 0;
            }
            case "wpscan":
            {
                var pattern = args.Length > 1 ? args[1] : "Kart";
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var files = assets.Files("MotorTown/Content/Maps/Jeju/Jeju_World/_Generated_/")
                    .Where(f => f.Extension == "umap").ToList();
                Console.WriteLine($"scanning {files.Count} world partition cells for /{pattern}/i...");
                var hits = 0;
                foreach (var file in files)
                {
                    var package = assets.Package(file.PathWithoutExtension);
                    if (package is null) continue;
                    for (var i = 0; i < package.Exports.Count; i++)
                    {
                        if (!regex.IsMatch(package.Exports[i].ExportType)) continue;
                        Console.WriteLine($"{file.Path} export {i}: {package.Exports[i].ExportType}");
                        hits++;
                    }
                }
                Console.WriteLine($"--- {hits} type-name hits ---");
                return 0;
            }
            case "stats":
                return Stats(assets, args[1], args.Length > 2 ? args[2] : null);
            case "names":
            {
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var rows = package.First()["Rows"] as JObject ?? [];
                foreach (var (name, _) in rows) Console.WriteLine(name);
                return 0;
            }
            case "nonekeys":
            {
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var rows = package.First()["Rows"] as JObject ?? [];
                foreach (var (name, row) in rows)
                {
                    if (row is not JObject obj) continue;
                    var keys = (obj["VehicleKeys"] as JArray ?? []).OfType<JValue>()
                        .Select(v => (string?)v.Value).Where(v => v is not null).ToList();
                    if (keys.Count == 0 || !keys.Contains("None")) continue;
                    var slots = (obj["Slots"] as JArray ?? []).OfType<JValue>()
                        .Select(v => ((string?)v.Value ?? "").Replace("EMTVehiclePartSlot::", "")).ToList();
                    Console.WriteLine($"{name}\t{(string?)obj["PartType"]}\tkeys=[{string.Join(",", keys)}]\tslots=[{string.Join(",", slots)}]\tcost={(long?)obj["Cost"]}\thidden={(bool?)obj["bIsHidden"]}");
                }
                return 0;
            }
            case "dump":
            {
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var index = args.Length > 2 ? int.Parse(args[2]) : 0;
                Console.WriteLine(package.Json(index).ToString(Newtonsoft.Json.Formatting.Indented));
                return 0;
            }
            case "loc":
            {
                var localization = assets.LoadLocalization();
                Console.WriteLine($"{localization.Languages.Count} languages: {string.Join(", ", localization.Languages)}");
                var wanted = args.Length > 1 ? new[] { args[1] } : new[] { "VehicleParts", "VehiclePartsBrand", "Vehicle", "Item", "Garage" };
                foreach (var ns in wanted)
                {
                    var entries = localization.Table("en").GetValueOrDefault(ns);
                    Console.WriteLine($"[{ns}] {(entries?.Count ?? 0)} entries");
                    if (entries is null) continue;
                    foreach (var (key, value) in entries)
                        Console.WriteLine($"  {key} = {value}");
                }
                return 0;
            }
            case "locfind":
            {
                var localization = assets.LoadLocalization();
                var needle = args.Length > 1 ? args[1] : "";
                foreach (var (ns, entries) in localization.Table("en"))
                {
                    foreach (var (key, value) in entries)
                    {
                        if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                            Console.WriteLine($"[{ns}] {key} = {value}");
                    }
                }
                return 0;
            }
            case "veh":
            {
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var rows = package.First()["Rows"] as JObject ?? [];
                JToken? row = null;
                if (args.Length > 2)
                {
                    row = rows[args[2]];
                    if (row is null) { Console.Error.WriteLine($"row not found: {args[2]}"); return 1; }
                    DumpVehicle(row);
                }
                else
                {
                    foreach (var (name, r) in rows)
                    {
                        Console.WriteLine($"\n===== {name}");
                        DumpVehicle(r);
                    }
                }
                return 0;
            }
            case "vehiclesale":
            {
                // vehiclesale <path> - dump every MTDealerVehicleSpawnPoint (label, coord,
                // VehicleClass/EditorVisualVehicleClass) and VehicleSpawner_C (label, coord,
                // VehicleParams/VechileKeys keys) export in a world package, unfiltered.
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                for (var i = 0; i < package.Exports.Count; i++)
                {
                    var type = package.Exports[i].ExportType;
                    if (type != "MTDealerVehicleSpawnPoint" && type != "VehicleSpawner_C") continue;
                    var obj = package.Json(i);
                    var props = obj["Properties"] as JObject;
                    var label = (string?)obj["ActorLabel"] ?? "";
                    if (type == "MTDealerVehicleSpawnPoint")
                    {
                        var vc = (string?)props?["VehicleClass"]?["ObjectPath"];
                        var evc = (string?)props?["EditorVisualVehicleClass"]?["ObjectPath"];
                        Console.WriteLine($"export {i} DEALER \"{label}\": VehicleClass={vc} EditorVisual={evc}");
                    }
                    else
                    {
                        var listRef = props?["MTSpawnVehicleList"] as JObject;
                        var listPath = (string?)listRef?["ObjectPath"];
                        var dot = listPath?.LastIndexOf('.') ?? -1;
                        JToken? list = null;
                        if (dot >= 0 && int.TryParse(listPath![(dot + 1)..], out var listIndex))
                            list = package.Json(listIndex)["Properties"];
                        var keys = (list?["VehicleParams"] as JArray ?? []).Select(p => (string?)p["VehicleKey"])
                            .Concat((list?["VechileKeys"] as JArray ?? []).OfType<JValue>().Select(v => (string?)v.Value))
                            .Where(k => k is { Length: > 0 });
                        Console.WriteLine($"export {i} SPAWNER \"{label}\": keys=[{string.Join(",", keys)}]");
                    }
                }
                return 0;
            }
            case "alltypes":
            {
                // alltypes <path> [regex] - distinct export type names + counts, optionally
                // filtered by a case-insensitive regex.
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var filter = args.Length > 2 ? new Regex(args[2], RegexOptions.IgnoreCase) : null;
                var counts = new Dictionary<string, int>();
                foreach (var e in package.Exports) counts[e.ExportType] = counts.GetValueOrDefault(e.ExportType) + 1;
                foreach (var (type, count) in counts.OrderByDescending(kv => kv.Value))
                {
                    if (filter is not null && !filter.IsMatch(type)) continue;
                    Console.WriteLine($"{count,6}  {type}");
                }
                return 0;
            }
            case "listowners":
            {
                // listowners <path> - for every MTSpawnVehicleListComponent export, print its
                // owning actor's export type + label + vehicle keys, to find every kiosk
                // regardless of blueprint subclass name.
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                for (var i = 0; i < package.Exports.Count; i++)
                {
                    if (package.Exports[i].ExportType != "MTSpawnVehicleListComponent") continue;
                    var comp = package.Json(i);
                    var outerPath = (string?)comp["Outer"]?["ObjectPath"];
                    var dot = outerPath?.LastIndexOf('.') ?? -1;
                    if (dot < 0 || !int.TryParse(outerPath![(dot + 1)..], out var ownerIndex)) continue;
                    var owner = package.Json(ownerIndex);
                    var ownerType = package.Exports[ownerIndex].ExportType;
                    var label = (string?)owner["ActorLabel"] ?? "";
                    var props = comp["Properties"] as JObject;
                    var keys = (props?["VechileKeys"] as JArray ?? []).OfType<JValue>().Select(v => (string?)v.Value)
                        .Concat((props?["VehicleParams"] as JArray ?? []).Select(p => (string?)p["VehicleKey"]))
                        .Where(k => k is { Length: > 0 });
                    var hasInteractable = (owner["Properties"] as JObject)?.Properties()
                        .Any(p => p.Name == "MTInteractable" || p.Name.Contains("Interact")) ?? false;
                    Console.WriteLine($"export {i} owner={ownerType} \"{label}\" interactable={hasInteractable} keys=[{string.Join(",", keys)}]");
                }
                return 0;
            }
            case "wgrep":
            {
                // wgrep <path> <pattern> - dump every export's JSON and grep the serialized
                // text for <pattern> (case-insensitive), unlike `grep` this covers .umap world
                // packages export-by-export, not just .uasset files.
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var regex = new Regex(args[2], RegexOptions.IgnoreCase);
                for (var i = 0; i < package.Exports.Count; i++)
                {
                    var text = package.Json(i).ToString(Newtonsoft.Json.Formatting.None);
                    if (!regex.IsMatch(text)) continue;
                    var label = (string?)package.Json(i)["ActorLabel"];
                    Console.WriteLine($"export {i} ({package.Exports[i].ExportType}) label={label}");
                }
                return 0;
            }
            case "areas":
            {
                // areas - AreaVolume flag distribution + every name per flag (WorldExtractor.AreaVolumes()).
                var localization = assets.LoadLocalization();
                var extractor = new WorldExtractor(assets, localization);
                var groups = extractor.AreaVolumes().OfType<JObject>()
                    .GroupBy(a => (string?)a["flag"] ?? "")
                    .OrderBy(g => g.Key);
                foreach (var g in groups)
                {
                    Console.WriteLine($"--- flag=\"{g.Key}\" ({g.Count()}) ---");
                    foreach (var a in g)
                        Console.WriteLine($"  {(string?)(a["name"] as JObject)?["en"]}");
                }
                return 0;
            }
            case "dealeraudit":
            {
                // dealeraudit <path> - every MTDealerVehicleSpawnPoint: which of
                // VehicleClass / EditorVisualVehicleClass / VehicleClasses[] it carries, and
                // whether the existing VehicleClass/EditorVisualVehicleClass-only match would
                // miss it (blank=true).
                var package = assets.Package(args[1]);
                if (package is null) { Console.Error.WriteLine($"package not found: {args[1]}"); return 1; }
                var blankCount = 0;
                var classesOnlyCount = 0;
                for (var i = 0; i < package.Exports.Count; i++)
                {
                    if (package.Exports[i].ExportType != "MTDealerVehicleSpawnPoint") continue;
                    var obj = package.Json(i);
                    var props = obj["Properties"] as JObject;
                    var vc = (string?)props?["VehicleClass"]?["ObjectPath"];
                    var evc = (string?)props?["EditorVisualVehicleClass"]?["ObjectPath"];
                    var classes = (props?["VehicleClasses"] as JArray ?? [])
                        .OfType<JObject>().Select(c => (string?)c["ObjectPath"]).Where(c => c is not null).ToList();
                    var label = (string?)obj["ActorLabel"] ?? "";
                    var blank = vc is null && evc is null;
                    if (blank) blankCount++;
                    if (blank && classes.Count > 0) classesOnlyCount++;
                    if (blank || classes.Count > 0)
                        Console.WriteLine($"export {i} \"{label}\": VehicleClass={vc} EditorVisual={evc} VehicleClasses=[{string.Join(",", classes)}]");
                }
                Console.WriteLine($"--- blank VehicleClass+EditorVisual: {blankCount}, of which VehicleClasses[] non-empty: {classesOnlyCount} ---");
                return 0;
            }
            default:
                Console.WriteLine("usage: explore <find|table|rows|props|grep> ...");
                return 1;
        }
    }

    private static int Find(AssetSource assets, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var hits = assets.Files("MotorTown/").Where(f => regex.IsMatch(f.Path)).ToList();
        foreach (var file in hits.OrderBy(f => f.Path, StringComparer.Ordinal))
            Console.WriteLine($"{file.Path}  ({file.Size})");
        Console.WriteLine($"--- {hits.Count} files");
        return 0;
    }

    private static int Table(AssetSource assets, string path)
    {
        var package = assets.Package(path);
        if (package is null) { Console.Error.WriteLine($"package not found: {path}"); return 1; }

        var json = package.First();
        var rows = json["Rows"] as JObject;
        if (rows is null) { Console.WriteLine($"no Rows in {path}; exports: {package.Exports.Count}"); return 0; }

        Console.WriteLine($"{path}: {rows.Count} rows");
        var structs = new SortedSet<string>(StringComparer.Ordinal);
        JObject? first = null;
        foreach (var (name, row) in rows)
        {
            var properties = row as JObject;
            if (properties is null) continue;
            first ??= properties;
            structs.UnionWith(properties.Properties().Select(p => p.Name));
        }
        Console.WriteLine("columns: " + string.Join(", ", structs));
        return 0;
    }

    private static int Rows(AssetSource assets, string path, string name)
    {
        var package = assets.Package(path);
        if (package is null) { Console.Error.WriteLine($"package not found: {path}"); return 1; }

        var rows = package.First()["Rows"] as JObject ?? [];
        JToken? row = rows[name];
        if (row is null)
        {
            var match = rows.Properties().FirstOrDefault(p =>
                p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is null) { Console.Error.WriteLine($"row not found: {name}"); return 1; }
            row = match.Value;
            Console.WriteLine($"matched {match.Name}:");
        }
        Console.WriteLine(row.ToString(Newtonsoft.Json.Formatting.Indented));
        return 0;
    }

    private static int Props(AssetSource assets, string path)
    {
        var package = assets.Package(path);
        if (package is null) { Console.Error.WriteLine($"package not found: {path}"); return 1; }

        Console.WriteLine($"{path}: {package.Exports.Count} exports");
        for (var i = 0; i < package.Exports.Count; i++)
        {
            var json = package.Json(i);
            var props = json["Properties"] as JObject;
            if (props is null) continue;
            Console.WriteLine($"export {i} ({json["Type"]}): " +
                string.Join(", ", props.Properties().Select(p => p.Name)));
        }
        return 0;
    }

    /// <summary>Prints the restriction-relevant fields of a vehicle row.</summary>
    private static void DumpVehicle(JToken row)
    {
        var obj = row as JObject ?? [];
        var wanted = new[] { "VehicleType", "TruckClass", "VehicleTypeFlags", "GameplayTags", "Parts", "PartValues", "NotOptionalPartTypes", "OptionalPartTypes", "NotOptionalPartSlots", "NotSupportedPartTypes", "SlotSupportedPartsQueries" };
        foreach (var name in wanted)
        {
            if (obj[name] is not { } value) continue;
            var text = value.ToString(Newtonsoft.Json.Formatting.None);
            if (text == "[]" || text == "null" || text == "\"\"") continue;
            Console.WriteLine($"  {name}: {text}");
        }
    }

    private static int Stats(AssetSource assets, string path, string? typeFilter)
    {
        var package = assets.Package(path);
        if (package is null) { Console.Error.WriteLine($"package not found: {path}"); return 1; }

        var rows = package.First()["Rows"] as JObject ?? [];
        var byType = new SortedDictionary<string, List<KeyValuePair<string, JObject>>>(StringComparer.Ordinal);
        foreach (var (name, row) in rows)
        {
            if (row is not JObject obj) continue;
            var type = (string?)obj["PartType"] ?? "";
            if (typeFilter is not null && !type.EndsWith(typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (!byType.TryGetValue(type, out var list))
            {
                list = [];
                byType[type] = list;
            }
            list.Add(new(name, obj));
        }

        foreach (var (type, list) in byType)
        {
            Console.WriteLine($"\n{type}  ({list.Count} rows)");
            if (list.Count < 2) { Console.WriteLine("  only one row - nothing to compare"); continue; }

            var columns = list[0].Value.Properties().Select(p => p.Name)
                .Where(name => name is not ("Name" or "Name2" or "Desciption" or "GameplayTags" or "BodyMaterialNames"
                    or "ColorSlots" or "DecalableMaterialSlotNames" or "VehicleRowGameplayTagQuery"))
                .ToList();
            foreach (var column in columns)
            {
                var distinct = list.Select(pair => pair.Value[column]?.ToString(Newtonsoft.Json.Formatting.None))
                    .Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count > 1)
                {
                    var values = distinct.Take(4);
                    Console.WriteLine($"  {column}: {string.Join(" | ", values)}");
                }
            }
        }
        return 0;
    }

    private static int Types(AssetSource assets, string path)
    {
        var package = assets.Package(path);
        if (package is null) { Console.Error.WriteLine($"package not found: {path}"); return 1; }

        var rows = package.First()["Rows"] as JObject ?? [];
        var byType = new SortedDictionary<string, TypeInfo>(StringComparer.Ordinal);

        foreach (var (name, row) in rows)
        {
            if (row is not JObject obj) continue;
            var type = (string?)obj["PartType"] ?? "";
            if (!byType.TryGetValue(type, out var entry))
            {
                entry = new TypeInfo();
                byType[type] = entry;
            }

            if (entry.Sample.Length == 0) entry.Sample = name;
            entry.Count++;
            foreach (var property in obj.Properties())
            {
                var value = property.Value;
                if (IsDefault(property.Name, value)) continue;
                entry.Columns.Add(property.Name);
            }
        }

        foreach (var (type, entry) in byType)
        {
            Console.WriteLine($"\n{type}  ({entry.Count} rows, sample: {entry.Sample})");
            Console.WriteLine("  " + string.Join(", ", entry.Columns));
        }
        return 0;

        static bool IsDefault(string name, JToken value)
        {
            switch (value)
            {
                case JValue { Type: JTokenType.Float } f when name is "MassKg" or "AirDragMultiplier"
                    or "TrailerAirDragMultiplier" or "AeroLift" or "FrontAeroLift" or "RearAeroLift"
                    or "FrontDamageMultiplier":
                    return Convert.ToDouble(f.Value!) == 1.0;
                case JValue { Type: JTokenType.Float } f:
                    return Convert.ToDouble(f.Value!) == 0.0;
                case JValue { Type: JTokenType.Integer } i:
                    return Convert.ToInt64(i.Value!) == 0;
                case JValue { Type: JTokenType.Boolean } b:
                    return !(bool)b.Value!;
                case JValue { Type: JTokenType.String } s:
                    return string.IsNullOrEmpty((string)s.Value!);
                case JArray a:
                    return a.Count == 0;
                case JObject o:
                    return IsEmptyObject(o);
                default:
                    return true;
            }
        }

        static bool IsEmptyObject(JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (IsDefault(property.Name, property.Value)) continue;
                return false;
            }
            return true;
        }
    }

    private sealed class TypeInfo
    {
        public int Count;
        public readonly SortedSet<string> Columns = new(StringComparer.Ordinal);
        public string Sample = "";
    }

    private static int Grep(AssetSource assets, string dir, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var files = assets.Files(dir).Where(f => f.Extension == "uasset").ToList();
        Console.WriteLine($"scanning {files.Count} packages under {dir}...");

        var matches = new List<(string Path, string Prop, string Value)>();
        foreach (var file in files)
        {
            var package = assets.Package(file.PathWithoutExtension);
            if (package is null) continue;
            foreach (var (path, value) in Walk(package.First()))
            {
                if (value is JValue { Type: JTokenType.String, Value: string text } && regex.IsMatch(text))
                    matches.Add((file.Path, path, text));
            }
        }

        foreach (var (path, prop, value) in matches.Take(200))
            Console.WriteLine($"{path}  {prop} = {value}");
        Console.WriteLine($"--- {matches.Count} matches");
        return 0;
    }

    private static IEnumerable<(string Path, JToken Value)> Walk(JToken token, string path = "")
    {
        switch (token)
        {
            case JObject obj:
                foreach (var property in obj.Properties())
                {
                    var p = path.Length == 0 ? property.Name : path + "." + property.Name;
                    foreach (var hit in Walk(property.Value, p)) yield return hit;
                }
                break;
            case JArray array:
                for (var i = 0; i < array.Count; i++)
                    foreach (var hit in Walk(array[i], $"{path}[{i}]")) yield return hit;
                break;
            default:
                yield return (path, token);
                break;
        }
    }
}
