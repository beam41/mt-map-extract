using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// Game.locres for every shipped culture, as namespace -> key -> string.
/// </summary>
internal sealed class Localization(SortedDictionary<string, Dictionary<string, Dictionary<string, string>>> tables)
{
    public const string English = "en";

    /// <summary>Cultures in name order, which is the order the name maps are written in.</summary>
    public IReadOnlyList<string> Languages { get; } = tables.Keys.ToList();

    public IReadOnlyDictionary<string, Dictionary<string, string>> Table(string language) =>
        tables.TryGetValue(language, out var table) ? table : new Dictionary<string, Dictionary<string, string>>();

    public string? Lookup(string language, string? ns, string? key)
    {
        if (ns is null || key is null) return null;
        return tables.TryGetValue(language, out var table)
               && table.TryGetValue(ns, out var entries)
               && entries.TryGetValue(key, out var value)
            ? value
            : null;
    }

    /// <summary>Lookup in <paramref name="language"/>, falling back to English like the JS helpers did.</summary>
    public string? LookupOrEnglish(string language, string? ns, string? key) =>
        Lookup(language, ns, key) ?? Lookup(English, ns, key);

    /// <summary>
    /// English string -> the namespace/key that produced it, so a text whose own key has no
    /// locres entry can still be translated. Namespaces earlier in
    /// <paramref name="preferredNamespaces"/> win.
    /// </summary>
    public Dictionary<string, (string Namespace, string Key)> IndexByEnglish(params string[] preferredNamespaces)
    {
        var index = new Dictionary<string, (string Namespace, string Key)>(StringComparer.Ordinal);
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (ns, entries) in Table(English))
        {
            var position = Array.IndexOf(preferredNamespaces, ns);
            var priority = position < 0 ? preferredNamespaces.Length : position;
            foreach (var (key, value) in entries)
            {
                if (index.ContainsKey(value) && rank[value] <= priority) continue;
                index[value] = (ns, key);
                rank[value] = priority;
            }
        }

        return index;
    }
}

/// <summary>Helpers for the FText shape CUE4Parse emits.</summary>
internal static class Text
{
    /// <summary>Fields the Rust/JS pipeline kept when it re-serialized a text.</summary>
    private static readonly string[] TextFields =
        ["TableId", "Key", "SourceString", "LocalizedString", "CultureInvariantString"];

    private static readonly string[] MapIconNameFields =
        ["TableId", "Key", "SourceString", "LocalizedString"];

    /// <summary>
    /// Texts authored in the editor carry no namespace at all and land in the locres under the
    /// empty one, so that - not null - is the fallback.
    /// </summary>
    public static string Namespace(JObject text) =>
        (string?)text["Namespace"] ?? TableNamespace((string?)text["TableId"]) ?? "";

    /// <summary>"/Game/.../PlaceName.PlaceName" -> "PlaceName", matching the JS `split(".")[1]`.</summary>
    private static string? TableNamespace(string? tableId)
    {
        if (tableId is null) return null;
        var parts = tableId.Split('.');
        return parts.Length > 1 ? parts[1] : null;
    }

    public static string? Key(JObject text) => (string?)text["Key"];

    public static string? Source(JObject text) =>
        (string?)text["SourceString"] ?? (string?)text["CultureInvariantString"];

    public static string? Localized(JObject text) =>
        (string?)text["LocalizedString"] ?? (string?)text["CultureInvariantString"];

    public static JObject Project(JObject text) => Project(text, TextFields);

    public static JObject ProjectMapIconName(JObject text) => Project(text, MapIconNameFields);

    private static JObject Project(JObject text, string[] fields)
    {
        var projected = new JObject();
        foreach (var field in fields)
        {
            var value = text[field];
            if (value is not null && value.Type != JTokenType.Null) projected[field] = value.DeepClone();
        }
        return projected;
    }

    /// <summary>A text carrying nothing but a culture-invariant string, e.g. a point number.</summary>
    public static JObject Invariant(string value) => new() { ["CultureInvariantString"] = value };
}

internal static class Output
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
    }

    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes 2-space indented JSON with no trailing newline, like serde and JSON.stringify did.</summary>
    public static void WriteJson(string path, JToken json, string label)
    {
        Write(path, json.ToString(Formatting.Indented));
        var count = json is JArray array ? array.Count : ((JObject)json).Count;
        Console.WriteLine($"  {Path.GetFileName(path),-32} {count} {label}");
    }

    /// <summary>
    /// Rewrites whole floats as integers. The files that used to come out of the Node scripts
    /// were written by JSON.stringify, which prints 1306382.0 as 1306382; the ones that came out
    /// of the Rust keep the trailing .0. Both shapes are preserved as they were.
    /// </summary>
    public static JToken JsNumbers(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                foreach (var property in obj.Properties()) property.Value = JsNumbers(property.Value);
                return obj;
            case JArray array:
                for (var i = 0; i < array.Count; i++) array[i] = JsNumbers(array[i]);
                return array;
            case JValue { Type: JTokenType.Float } value:
                var number = Convert.ToDouble(value.Value);
                return double.IsFinite(number) && number == Math.Floor(number) && Math.Abs(number) < 1e18
                    ? new JValue((long)number)
                    : value;
            default:
                return token;
        }
    }

    /// <summary>
    /// The enum spellings the AMC map expects, applied to every key and every string on the way
    /// out: no EMTAreaVolumeFlags:: prefix, EDeliveryCargoType:: shortened to _T, and the
    /// SmallPackage2 display name folded onto _TSmallPackage.
    /// </summary>
    public static JToken AmcNames(JToken token)
    {
        switch (token)
        {
            case JObject obj:
            {
                var renamed = new JObject();
                foreach (var property in obj.Properties()) renamed[AmcName(property.Name)] = AmcNames(property.Value);
                return renamed;
            }
            case JArray array:
            {
                var mapped = new JArray();
                foreach (var item in array) mapped.Add(AmcNames(item));
                return mapped;
            }
            case JValue { Type: JTokenType.String, Value: string text }:
                return new JValue(AmcName(text));
            default:
                return token;
        }
    }

    private static string AmcName(string name)
    {
        const string areaFlags = "EMTAreaVolumeFlags::";
        const string cargoType = "EDeliveryCargoType::";

        if (name.StartsWith(areaFlags, StringComparison.Ordinal)) name = name[areaFlags.Length..];
        else if (name.StartsWith(cargoType, StringComparison.Ordinal)) name = "_T" + name[cargoType.Length..];

        return name == "_TSmallPackage2" ? "_TSmallPackage" : name;
    }

    /// <summary>
    /// Drops languages whose value matches English and moves "en" to the front - the old
    /// remove_duplicates.js pass, applied as the name maps are built.
    /// </summary>
    public static JObject Dedupe(JObject names)
    {
        if (names["en"] is not { Type: JTokenType.String } english) return names;

        var result = new JObject { ["en"] = english };
        foreach (var (language, value) in names)
        {
            if (language == "en") continue;
            if (value?.Type == JTokenType.String && (string)value! == (string)english!) continue;
            result[language] = value?.DeepClone();
        }
        return result;
    }
}
