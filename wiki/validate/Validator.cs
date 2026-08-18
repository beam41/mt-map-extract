using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using MtExtract;

namespace WikiValidate;

/// <summary>
/// Fetches the wiki pages and validates every section against the gathered pak data:
/// vehicle comparison table (cost / chassis weight / total weight / drag / drivetrain),
/// vehicle page infobox + Specifications + Capabilities + Default Parts + Installable Parts,
/// and the part list (name / cost / mass).
///
/// Writes wiki/out/validation.json (one object per incorrect claim, machine-readable) and
/// wiki/out/review.md (human-readable summary).
/// </summary>
internal sealed class Validator
{
    private const string WikiBase = "https://wiki.aseanmotorclub.com";

    private readonly string _outDir;
    private readonly HttpClient _http = new();
    private readonly List<JObject> _claims = new();

    public Validator(string outDir) => _outDir = outDir;

    public void Run(JObject vehicles, JObject parts, JObject vehicleData,
        Localization localization, Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var pagesDir = Path.Combine(_outDir, "pages");
        Directory.CreateDirectory(pagesDir);

        // ---- part list ----
        var partList = Fetch("list_of_parts?do=export_raw", "list_of_parts.txt");
        ValidatePartList(partList, parts);

        // ---- vehicle list + comparison ----
        var vehicleList = Fetch("list_of_vehicles?do=export_raw", "list_of_vehicles.txt");
        var comparison = Fetch("vehicle_comparison?do=export_raw", "vehicle_comparison.txt");
        ValidateVehicleList(vehicleList, vehicleData);
        ValidateComparison(comparison, vehicleData);

        // ---- per-vehicle pages: infobox, Specifications, Capabilities, Default Parts ----
        foreach (var (slug, _) in VehicleEntries(vehicleList))
        {
            var raw = Fetch($"vehicles:{slug}?do=export_raw", $"vehicles_{slug}.txt");
            ValidateVehiclePage(slug, raw, vehicles, parts, vehicleData, localization, englishIndex);

            var install = Fetch($"vehicles:{slug}:installable_parts?do=export_raw", $"installable_{slug}.txt");
            ValidateInstallableParts(slug, install, vehicles, parts, vehicleData);
        }

        WriteClaims();
    }

    // ------------------------------------------------------------------ fetching

    private string Fetch(string path, string cacheName)
    {
        var cache = Path.Combine(_outDir, "pages", cacheName);
        if (File.Exists(cache)) return File.ReadAllText(cache);

        var url = $"{WikiBase}/{path}";
        var text = _http.GetStringAsync(url).Result;
        if (text.Contains("No such revision") || text.Contains("<!DOCTYPE html>"))
        {
            // raw export failure; retry the normal page
            text = _http.GetStringAsync($"{WikiBase}/{path.Split('?')[0]}").Result;
        }
        File.WriteAllText(cache, text);
        return text;
    }

    // ------------------------------------------------------------------ part list

    private void ValidatePartList(string raw, JObject parts)
    {
        var lc = parts.Properties().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Name);
        var rows = Regex.Matches(raw, @"^\| \[\[parts:([^|]+)\|([^\]]+)\]\] \| ([\d,]+) \| (.*?) \|$", RegexOptions.Multiline);

        foreach (Match m in rows)
        {
            var slug = m.Groups[1].Value;
            var disp = m.Groups[2].Value;
            var cost = m.Groups[3].Value.Replace(",", "");
            var mass = m.Groups[4].Value.Trim();
            var key = ResolvePart(slug, lc, parts);
            if (key is null) { Claim("list_of_parts", slug, "part", disp, "not found in pak"); continue; }

            var part = (JObject)parts[key]!;
            var en = (string?)(part["name"] as JObject)?["en"] ?? "";
            if (en != disp) Claim("list_of_parts", slug, "name", disp, en);

            var pakCost = (long?)part["cost"] ?? 0;
            if (long.TryParse(cost, out var wikiCost) && wikiCost != pakCost)
                Claim("list_of_parts", slug, "cost", cost, pakCost.ToString());

            var pakMass = (double?)part["massKg"] ?? 0;
            double wikiMass = mass == "—" ? 0 : (double.TryParse(mass.Replace("kg", "").Trim(), out var wm) ? wm : double.NaN);
            if (!double.IsNaN(wikiMass) && Math.Abs(wikiMass - pakMass) > 1e-9)
                Claim("list_of_parts", slug, "mass", mass, pakMass.ToString());
        }
    }

    // ------------------------------------------------------------------ vehicle list + comparison

    private static List<(string Slug, string Name)> VehicleEntries(string raw) =>
        Regex.Matches(raw, @"\[\[vehicles:([^|]+)\|([^\]]+)\]\]")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    private void ValidateVehicleList(string raw, JObject vehicleData)
    {
        var nameKeys = NameToKeys(vehicleData);
        foreach (var (slug, disp) in VehicleEntries(raw))
        {
            var key = ResolveVehicle(slug, nameKeys, vehicleData);
            if (key is null)
            {
                Claim("list_of_vehicles", slug, "vehicle", disp, "not found in pak");
                continue;
            }
            var en = (string?)(vehicleData[key]!["name"] as JObject)?["en"] ?? "";
            if (en != disp && !(slug == "kuda_" && disp == "Kuda Flatbed"))
                Claim("list_of_vehicles", slug, "name", disp, en);
        }
    }

    private void ValidateComparison(string raw, JObject vehicleData)
    {
        var nameKeys = NameToKeys(vehicleData);
        foreach (var line in raw.Split('\n'))
        {
            var m = Regex.Match(line, @"^\| \[\[vehicles:([^|]+)\|([^\]]+)\]\] \|(.+)$");
            if (!m.Success) continue;
            var slug = m.Groups[1].Value;
            var disp = m.Groups[2].Value;
            var cells = m.Groups[3].Value.Split('|').Select(c => c.Trim()).ToArray();
            // cells: Type, Cost, Drivetrain, Chassis Weight, Total Weight, Drag
            var key = ResolveVehicle(slug, nameKeys, vehicleData);
            if (key is null) continue;
            var v = (JObject)vehicleData[key]!;

            // cost
            if (cells.Length > 1 && long.TryParse(cells[1].Replace(",", "").Replace("$", ""), out var wikiCost)
                && wikiCost != (long?)v["cost"])
                Claim("vehicle_comparison", slug, "cost", cells[1], v["cost"]!.ToString());

            // drivetrain
            if (cells.Length > 2)
            {
                var wikiDrive = cells[2];
                var pakDrive = PakDrive(v);
                var wikiNorm = wikiDrive switch { "Rear-wheel drive" => "RWD", "Front-wheel drive" => "FWD", "All-wheel drive" => "AWD", _ => wikiDrive };
                if (wikiDrive != "" && wikiNorm != pakDrive)
                    Claim("vehicle_comparison", slug, "drivetrain", wikiDrive, pakDrive == "" ? "(none)" : pakDrive);
                else if (wikiDrive == "" && pakDrive != "")
                    Claim("vehicle_comparison", slug, "drivetrain", "(blank)", pakDrive);
            }

            // chassis weight
            if (cells.Length > 3 && TryKg(cells[3], out var wikiW) && Math.Abs(wikiW - (double?)v["weightKg"] ?? 0) > 1e-9)
                Claim("vehicle_comparison", slug, "chassisWeight", cells[3], v["weightKg"]!.ToString());

            // drag
            if (cells.Length > 5 && double.TryParse(cells[5], out var wikiDrag)
                && v["dragCoeff"] is JValue d && Math.Abs(wikiDrag - Convert.ToDouble(d.Value)) > 1e-6)
                Claim("vehicle_comparison", slug, "drag", cells[5], d.Value!.ToString());
        }
    }

    // ------------------------------------------------------------------ vehicle page

    private void ValidateVehiclePage(string slug, string raw, JObject vehicles, JObject parts,
        JObject vehicleData, Localization localization,
        Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var key = ResolveVehicle(slug, NameToKeys(vehicleData), vehicleData);
        if (key is null) return;
        var v = (JObject)vehicleData[key]!;

        // infobox
        var infobox = Regex.Match(raw, @"\{\{infobox>(.*?)\}\}", RegexOptions.Singleline);
        if (infobox.Success)
        {
            var fields = new Dictionary<string, string>();
            foreach (Match fm in Regex.Matches(infobox.Groups[1].Value, @"^(\S[^=]*?) = (.*)$", RegexOptions.Multiline))
                fields[fm.Groups[1].Value.Trim()] = fm.Groups[2].Value.Trim();
            if (fields.TryGetValue("Weight", out var wikiW) && TryKg(wikiW, out var ww)
                && Math.Abs(ww - (double?)v["weightKg"] ?? 0) > 1e-9)
                Claim($"vehicles:{slug} infobox", slug, "Weight", wikiW, v["weightKg"]!.ToString());
            if (fields.TryGetValue("Drag coefficient", out var wikiD) && double.TryParse(wikiD, out var wd)
                && v["dragCoeff"] is JValue d && Math.Abs(wd - Convert.ToDouble(d.Value)) > 1e-6)
                Claim($"vehicles:{slug} infobox", slug, "Drag coefficient", wikiD, d.Value!.ToString());
        }

        // Specifications
        var specs = Section(raw, "Specifications");
        foreach (var row in Regex.Matches(specs, @"^\| ([^|]+) \| (.+?) \|$", RegexOptions.Multiline).Cast<Match>())
        {
            var stat = row.Groups[1].Value.Trim();
            var value = row.Groups[2].Value.Trim();
            switch (stat)
            {
                case "Chassis Weight" when TryKg(value, out var cw) && Math.Abs(cw - (double?)v["weightKg"] ?? 0) > 1e-9:
                    Claim($"vehicles:{slug} Specifications", slug, "Chassis Weight", value, v["weightKg"]!.ToString());
                    break;
                case "Drivetrain":
                {
                    var pak = PakDrive(v);
                    var norm = value switch { "Rear-wheel drive" => "RWD", "Front-wheel drive" => "FWD", "All-wheel drive" => "AWD", _ => value };
                    if (norm != pak)
                        Claim($"vehicles:{slug} Specifications", slug, "Drivetrain", value, pak == "" ? "(none)" : pak);
                    break;
                }
                case "Engine":
                {
                    var partSlug = Regex.Match(value, @"\[\[parts:([^|]+)\|").Groups[1].Value;
                    var pakEngine = (string?)v["defaultParts"]?["Engine"];
                    if (pakEngine is not null && !SlugMatches(partSlug, pakEngine))
                        Claim($"vehicles:{slug} Specifications", slug, "Engine", value, pakEngine);
                    break;
                }
                case "Transmission":
                {
                    var partSlug = Regex.Match(value, @"\[\[parts:([^|]+)\|").Groups[1].Value;
                    var pakTrans = (string?)v["defaultParts"]?["Transmission"];
                    if (pakTrans is not null && !SlugMatches(partSlug, pakTrans))
                        Claim($"vehicles:{slug} Specifications", slug, "Transmission", value, pakTrans);
                    break;
                }
            }
        }

        // Capabilities
        var caps = Section(raw, "Capabilities");
        var wikiCaps = Regex.Matches(caps, @"^\s*\* (.+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim()).ToList();
        var pakCaps = new List<string>();
        var flags = v["flags"] as JObject;
        if (flags?["taxiable"] != null) pakCaps.Add("Taxi");
        if (flags?["busable"] != null) pakCaps.Add("Bus");
        if (flags?["limoable"] != null) pakCaps.Add("Limo");
        if (flags?["raceCar"] != null) pakCaps.Add("Race Car");
        if (wikiCaps.Count != pakCaps.Count || wikiCaps.Except(pakCaps, StringComparer.OrdinalIgnoreCase).Any())
            Claim($"vehicles:{slug} Capabilities", slug, "capabilities", string.Join(", ", wikiCaps), string.Join(", ", pakCaps));

        // Default Parts
        var dp = Section(raw, "Default Parts");
        var wikiParts = Regex.Matches(dp, @"^\| ([^|]+) \| \[\[parts:([^|]+)\|([^\]]+)\]\] ?(?:\(×(\d+)\))? \| (.*?) \|$", RegexOptions.Multiline)
            .Select(m => (Slot: m.Groups[1].Value.Trim(), Slug: m.Groups[2].Value, Mass: m.Groups[5].Value.Trim())).ToList();
        var pakParts = (v["defaultParts"] as JObject)?.Properties()
            .ToDictionary(p => p.Name, p => (string?)p.Value ?? "") ?? new();
        // Group by base slot name: the wiki renders Tire0..Tire3 as one "Tire (×4)" row.
        static string BaseSlot(string s) => Regex.Replace(s, @"\d+$", "");
        var lcParts = parts.Properties().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Name);
        var wikiSlots = wikiParts.GroupBy(p => BaseSlot(p.Slot))
            .ToDictionary(g => g.Key, g => g.Select(x => ResolvePart(x.Slug, lcParts, parts) ?? x.Slug).Distinct().OrderBy(x => x).ToList());
        var pakSlots = pakParts.GroupBy(p => BaseSlot(p.Key), p => p.Value)
            .ToDictionary(g => g.Key, g => g.Distinct().OrderBy(x => x).ToList());
        var allSlots = wikiSlots.Keys.Union(pakSlots.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var slot in allSlots)
        {
            var w = wikiSlots.TryGetValue(slot, out var wl) ? string.Join(",", wl) : "(none)";
            var p = pakSlots.TryGetValue(slot, out var pl) ? string.Join(",", pl) : "(none)";
            if (w != p)
                Claim($"vehicles:{slug} Default Parts", slug, $"slot {slot}", w, p);
        }
    }

    // ------------------------------------------------------------------ installable parts

    private void ValidateInstallableParts(string slug, string raw, JObject vehicles, JObject parts, JObject vehicleData)
    {
        if (raw.Contains("No such revision")) return;

        var key = ResolveVehicle(slug, NameToKeys(vehicleData), vehicleData);
        if (key is null) return;
        var v = (JObject)vehicles[key]!;

        // expected: parts whose restriction allows this vehicle
        var expected = new List<string>();
        foreach (var pp in parts.Properties())
        {
            var partKey = pp.Name;
            var part = pp.Value;
            if (part is not JObject p || !FitsVehicle(p, v, key)) continue;
            expected.Add(partKey);
        }
        var expectedSet = expected.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // wiki: part slugs listed on the page
        var wikiSlugs = Regex.Matches(raw, @"\[\[parts:([^|]+)\|")
            .Select(m => ResolvePart(m.Groups[1].Value, parts.Properties().ToDictionary(x => x.Name.ToLowerInvariant(), x => x.Name), parts) ?? m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expectedSet.Except(wikiSlugs).ToList();
        var extra = wikiSlugs.Except(expectedSet).ToList();
        if (missing.Count > 0)
            Claim($"vehicles:{slug} Installable Parts", slug, "missing parts", string.Join(",", missing.Take(10)), "(expected to be listed)");
        if (extra.Count > 0)
            Claim($"vehicles:{slug} Installable Parts", slug, "extra parts", "(expected only fitting parts)", string.Join(",", extra.Take(10)));
    }

    /// <summary>Does part restriction allow this vehicle? Mirrors the fit rule in docs/vehicle-parts.md.</summary>
    private static bool FitsVehicle(JObject part, JObject vehicle, string vehicleKey)
    {
        var restrict = part["restrict"] as JObject;
        if (restrict is null) return true;

        var overrides = (restrict["overrideKeys"] as JArray ?? []).Select(x => (string?)x).Where(x => x != null).ToList();
        if (overrides.Contains(vehicleKey)) return true;

        var types = (restrict["types"] as JArray ?? []).Select(x => (string?)x).ToList();
        if (types.Count > 0 && !types.Contains((string?)vehicle["type"])) return false;

        var truckClasses = (restrict["truckClasses"] as JArray ?? []).Select(x => (string?)x).ToList();
        if (truckClasses.Count > 0)
        {
            var truckClass = (string?)vehicle["truckClass"];
            var includeNone = (bool?)restrict["truckClassIncludeNone"] == true;
            // bTruckClassIncludeNone: when the vehicle's class is None it is still allowed.
            if (!truckClasses.Contains(truckClass) && !(includeNone && truckClass == "EMTTruckClass::None"))
                return false;
        }

        var keys = (restrict["keys"] as JArray ?? []).Select(x => (string?)x).Where(x => x != null && x != "None").ToList();
        if (keys.Count > 0 && !keys.Contains(vehicleKey)) return false;

        var tagQuery = (string?)restrict["tagQuery"];
        if (tagQuery is not null && !string.IsNullOrEmpty(tagQuery))
        {
            var tags = (vehicle["tags"] as JArray ?? []).Select(x => (string?)x).ToList();
            if (!EvaluateTagQuery(tagQuery, tags)) return false;
        }

        return true;
    }

    /// <summary>GameplayTagQuery evaluator for the emitted AutoDescription shape: "ALL( A B )",
    /// "ANY( A B )", "NONE( A )", recursively nestable, with bare tag tokens and operator
    /// prefixes. E.g. "ALL( NONE( Vehicle.EV ), ANY( Vehicle.Bike.SportBike, Vehicle.Bike.Standard ) )".</summary>
    private static bool EvaluateTagQuery(string query, List<string?> tags)
    {
        return EvaluateTagNode(query.Trim(), tags);
    }

    private static bool EvaluateTagNode(string expr, List<string?> tags)
    {
        // strip one layer of parens
        expr = expr.Trim();
        if (expr.StartsWith('(') && expr.EndsWith(')'))
            expr = expr[1..^1].Trim();

        var m = Regex.Match(expr, @"^(ALL|ANY|NONE)\s*\((.*)\)\s*$", RegexOptions.Singleline);
        if (!m.Success)
        {
            // bare tag token
            var tag = expr.Trim();
            return tag.Length == 0 || tags.Contains(tag);
        }

        var op = m.Groups[1].Value;
        var inner = m.Groups[2].Value.Trim();
        var parts = SplitTopLevel(inner);
        var results = parts.Select(p => EvaluateTagNode(p, tags)).ToList();
        return op switch
        {
            "ALL" => results.All(r => r),
            "ANY" => results.Any(r => r),
            "NONE" => !results.Any(r => r),
            _ => true,
        };
    }

    /// <summary>Splits a query body on commas (and whitespace) at the top nesting level.</summary>
    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')') depth--;
            else if (depth == 0 && (body[i] == ',' || char.IsWhiteSpace(body[i])))
            {
                if (i > start) parts.Add(body[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < body.Length) parts.Add(body[start..].Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private static string PakDrive(JObject v)
    {
        var axles = v["axles"] as JArray ?? [];
        var driven = axles.Select((a, i) => (a as JObject)?["driven"]?.Value<bool>() == true ? i : -1)
            .Where(i => i >= 0).ToList();
        return driven.Count switch
        {
            0 => "",
            1 => driven[0] == 0 ? "FWD" : "RWD",
            _ => "AWD",
        };
    }

    private static bool TryKg(string s, out double kg)
    {
        kg = 0;
        var m = Regex.Match(s, @"([\d,.]+)\s*kg");
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(",", ""), out kg);
    }

    private static string Section(string raw, string name)
    {
        // stop at the NEXT "===== X =====" header (newline-anchored so we don't eat its first '=')
        var m = Regex.Match(raw, $@"===== {Regex.Escape(name)} =====(.*?)(?=\r?\n===== )", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static Dictionary<string, List<string>> NameToKeys(JObject data)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var p in data.Properties())
        {
            var k = p.Name;
            var v = p.Value;
            var en = (string?)(v as JObject)?["name"]?["en"] ?? "";
            var n = Norm(en);
            if (!map.TryGetValue(n, out var list)) { list = new(); map[n] = list; }
            list.Add(k);
            var kn = Norm(k);
            if (!map.TryGetValue(kn, out list)) { list = new(); map[kn] = list; }
            list.Add(k);
        }
        return map;
    }

    private static string? ResolveVehicle(string slug, Dictionary<string, List<string>> nameKeys, JObject data)
    {
        var n = Norm(slug);
        if (data.ContainsKey(slug)) return slug;
        if (nameKeys.TryGetValue(n, out var keys)) return keys.Count > 0 ? keys[0] : null;
        return null;
    }

    private static string? ResolvePart(string slug, Dictionary<string, string>? lc, JObject parts)
    {
        if (parts.ContainsKey(slug)) return slug;
        if (lc is not null && lc.TryGetValue(slug.ToLowerInvariant(), out var key)) return key;
        // rideheight_p1 -> RideHeight_+1, fd_1_33 -> FD_1.33
        var m = Regex.Match(slug, @"^rideheight_(p\d+)$");
        if (m.Success) return $"RideHeight_+{m.Groups[1].Value[1..]}";
        m = Regex.Match(slug, @"^rideheight_(-\d+)$");
        if (m.Success) return $"RideHeight_{m.Groups[1].Value}";
        m = Regex.Match(slug, @"^fd_(\d+)_(\d+)$");
        if (m.Success) return $"FD_{m.Groups[1].Value}.{m.Groups[2].Value}";
        m = Regex.Match(slug, @"^fd_(\d+)$");
        if (m.Success) return $"FD_{m.Groups[1].Value}";
        m = Regex.Match(slug, @"^fd_(\d+)_(\w+)$");
        if (m.Success)
        {
            // FD_15_hm -> FD_15_HM: match the real key through the lowercase map.
            var candidate = $"FD_{m.Groups[1].Value}_{m.Groups[2].Value}";
            if (lc is not null && lc.TryGetValue(candidate.ToLowerInvariant(), out var exact)) return exact;
            return candidate;
        }
        foreach (var prop in parts.Properties())
        {
            if (Norm(prop.Name) == Norm(slug)) return prop.Name;
        }
        return null;
    }

    private static bool SlugMatches(string wikiSlug, string pakKey)
    {
        var lc = pakKey.ToLowerInvariant();
        return wikiSlug.ToLowerInvariant() == lc || Norm(wikiSlug) == Norm(pakKey);
    }

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");

    private void Claim(string source, string slug, string field, string wiki, string pak)
    {
        _claims.Add(new JObject
        {
            ["source"] = source,
            ["vehicle"] = slug,
            ["field"] = field,
            ["wiki"] = wiki,
            ["pak"] = pak,
        });
    }

    private void WriteClaims()
    {
        var json = new JArray(_claims);
        Output.WriteJson(Path.Combine(_outDir, "validation.json"), json, "validation claims");

        var md = new System.Text.StringBuilder();
        md.AppendLine("# Wiki validation report");
        md.AppendLine();
        md.AppendLine($"Generated by wiki/validate on {DateTime.UtcNow:yyyy-MM-dd}.");
        md.AppendLine();
        md.AppendLine($"**{_claims.Count} incorrect claims found.** Machine-readable copy: `validation.json`.");
        md.AppendLine();
        md.AppendLine("| Source | Vehicle | Field | Wiki says | Pak says |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var c in _claims)
        {
            md.AppendLine($"| {c["source"]} | {c["vehicle"]} | {c["field"]} | `{c["wiki"]}` | `{c["pak"]}` |");
        }
        File.WriteAllText(Path.Combine(_outDir, "review.md"), md.ToString());
        Console.WriteLine($"  {Path.Combine(_outDir, "validation.json")} {_claims.Count} claims");
        Console.WriteLine($"  {Path.Combine(_outDir, "review.md")}");
    }
}
