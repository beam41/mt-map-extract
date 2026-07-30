using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// The data-table half of the pipeline: cargo keys/metadata and the localized name maps for
/// cargo, cargo types and vehicles.
/// </summary>
internal sealed class TableExtractor(AssetSource assets, Localization localization)
{
    private const string CargosPath = "MotorTown/Content/DataAsset/Cargos";
    private const string CargosScheduleIPath = "MotorTown/Content/DataAsset/Cargos_ScheduleI";
    private const string VehiclesPath = "MotorTown/Content/DataAsset/Vehicles/Vehicles";
    private const string CargoTypeNamespace = "CargoType";

    /// <summary>Cargo type keys stay as the game spells them, e.g. EDeliveryCargoType::Log.</summary>
    private const string CargoTypePrefix = "EDeliveryCargoType::";

    /// <summary>out_cargo_key.json (type -> keys) and out_cargo_metadata.json (key -> type/distances).</summary>
    public (JObject Keys, JObject Metadata) CargoMaps()
    {
        var keys = new JObject();
        var metadata = new JObject();

        foreach (var (key, row) in CargoRows())
        {
            var type = (string?)row?["CargoType"] ?? "";
            if (keys[type] is not JArray members) keys[type] = members = [];
            members.Add(key);

            var meta = new JObject { ["type"] = type };
            if (Distance(row?["MinDeliveryDistance"]) is { } min) meta["minDist"] = min;
            if (Distance(row?["MaxDeliveryDistance"]) is { } max) meta["maxDist"] = max;
            metadata[key] = meta;
        }

        return (keys, metadata);

        static JToken? Distance(JToken? token)
        {
            if (token is not JValue { Value: not null } value) return null;
            return Convert.ToDouble(value.Value) == 0 ? null : Output.JsNumbers(value.DeepClone());
        }
    }

    /// <summary>out_cargo_name.json.</summary>
    public JObject CargoNames()
    {
        var output = new JObject();

        foreach (var (key, row) in CargoRows())
        {
            var texts = (row?["Name2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
            var name = row?["Name"] as JObject;
            var names = new JObject();

            foreach (var language in localization.Languages)
            {
                var joined = string.Join(" ", texts.Select(text =>
                    localization.LookupOrEnglish(language, Text.Namespace(text), Text.Key(text))
                    ?? Text.Localized(text)
                    ?? ""));

                var localized = Blank(joined)
                    ? name is null
                        ? null
                        : localization.LookupOrEnglish(language, Text.Namespace(name), Text.Key(name))
                          ?? (string?)name["LocalizedString"]
                          ?? (string?)name["SourceString"]
                          ?? (string?)name["CultureInvariantString"]
                    : joined;

                if (!Blank(localized)) names[language] = localized;
            }

            // Rows pointing at a string table entry with no locres translation (Raven) fall
            // through to the source string above; anything left has only its key.
            if (Blank((string?)names[Localization.English])) names[Localization.English] = key;

            output[key] = Output.Dedupe(names);
        }

        return output;
    }

    /// <summary>out_cargo_type_name.json, keyed by the raw enum value.</summary>
    public JObject CargoTypeNames()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var language in localization.Languages)
        {
            if (localization.Table(language).TryGetValue(CargoTypeNamespace, out var entries))
                keys.UnionWith(entries.Keys);
        }

        var output = new JObject();
        foreach (var key in keys)
        {
            var names = new JObject();
            foreach (var language in localization.Languages)
            {
                names[language] = localization.LookupOrEnglish(language, CargoTypeNamespace, key) ?? key;
            }
            output[CargoTypePrefix + key] = Output.Dedupe(names);
        }

        return output;
    }

    /// <summary>out_vehicles_name.json.</summary>
    public JObject VehicleNames()
    {
        // Vehicle names used to be string table references (TableId ".../VehicleName.VehicleName",
        // key = row name). The DataTable now stores most of them inline - namespace "Vehicles_1" /
        // "Vehicles_Truck", key "<Row>_VehicleName" - and Game.locres has no entries under those
        // namespaces. The translations are still there under VehicleName, just reachable only via
        // the English source string, so that is the last resort before giving up.
        var englishIndex = localization.IndexByEnglish("VehicleName", "Vehicle", "Brand", "Common", "");
        var rows = assets.RequirePackage(VehiclesPath).First()["Rows"] as JObject ?? [];
        var output = new JObject();

        foreach (var (key, row) in rows)
        {
            var texts = (row?["VehicleName2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
            var name = row?["VehicleName"] as JObject;
            var names = new JObject();

            foreach (var language in localization.Languages)
            {
                var localized = texts.Count > 0
                    ? string.Join(" ", texts
                        .Select(text => LocalizeVehicleText(text, language, englishIndex))
                        .Where(part => !Blank(part)))
                    : name is null ? null : LocalizeVehicleText(name, language, englishIndex);

                if (!Blank(localized)) names[language] = localized;
            }

            // A row whose text is empty in every language still deserves a name.
            if (Blank((string?)names[Localization.English])) names[Localization.English] = key;

            output[key] = Output.Dedupe(names);
        }

        return output;
    }

    private string? LocalizeVehicleText(
        JObject text, string language, Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var key = Text.Key(text);

        if (localization.Lookup(language, Text.Namespace(text), key) is { } direct) return direct;
        if (localization.Lookup(language, "VehicleName", key) is { } byName) return byName;
        if (localization.Lookup(language, "Vehicle", key) is { } byVehicle) return byVehicle;

        var source = Text.Source(text);
        if (source is not null && englishIndex.TryGetValue(source, out var match)
            && localization.Lookup(language, match.Namespace, match.Key) is { } bySource)
            return bySource;

        return (string?)text["LocalizedString"] ?? source;
    }

    /// <summary>
    /// Replaces each raw name (an array of texts) with a language map, the old
    /// convert_raw_to_localized.js pass.
    /// </summary>
    public JArray LocalizeNames(JArray raw)
    {
        var output = new JArray();

        foreach (var item in raw.OfType<JObject>())
        {
            var localized = (JObject)item.DeepClone();
            if (item["name"] is JArray texts && texts.Count > 0)
            {
                var names = new JObject();
                foreach (var language in localization.Languages)
                {
                    names[language] = string.Join(" ", texts.OfType<JObject>().Select(text =>
                        localization.LookupOrEnglish(language, Text.Namespace(text), Text.Key(text))
                        ?? Text.Localized(text)
                        ?? ""));
                }
                localized["name"] = Output.Dedupe(names);
            }
            output.Add(localized);
        }

        return (JArray)Output.JsNumbers(output);
    }

    /// <summary>Cargos plus the Schedule I table, which the game may or may not still ship.</summary>
    private IEnumerable<KeyValuePair<string, JToken?>> CargoRows()
    {
        var rows = assets.RequirePackage(CargosPath).First()["Rows"] as JObject ?? [];
        var scheduleI = assets.Package(CargosScheduleIPath)?.First()["Rows"] as JObject ?? [];

        var merged = new JObject();
        foreach (var (key, row) in rows) merged[key] = row;
        foreach (var (key, row) in scheduleI) merged[key] = row;
        return merged;
    }

    private static bool Blank(string? value) => string.IsNullOrEmpty(value);
}
