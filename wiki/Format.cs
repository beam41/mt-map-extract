using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WikiGenerator;

/// <summary>
/// The wiki's display conventions (reverse-engineered from the live pages):
/// whole numbers plain or N0 per row, multipliers as ±% from 100, probabilities as %,
/// gear ratios as F2 with trailing zeros stripped, drag with one significant decimal.
/// </summary>
internal static class Format
{
    /// <summary>Whole numbers without a trailing .0, decimals to 2 places.</summary>
    public static string Num(double x) =>
        x == Math.Floor(x) && Math.Abs(x) < 1e15 ? ((long)x).ToString() : x.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Thousands separators when whole, else plain decimals.</summary>
    public static string N0(double x) =>
        x == Math.Floor(x) && Math.Abs(x) < 1e15
            ? ((long)x).ToString("N0", CultureInfo.InvariantCulture)
            : x.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Production/delivery time: under a minute stays plain seconds ("45s"); a
    /// minute or more splits into minutes + remainder seconds ("90s" -> "1m 30s", "120s" ->
    /// "2m").</summary>
    public static string Duration(double seconds)
    {
        if (seconds < 60) return $"{Num(seconds)}s";
        var minutes = (long)(seconds / 60);
        var remainder = seconds - minutes * 60;
        return remainder == 0 ? $"{minutes}m" : $"{minutes}m {Num(remainder)}s";
    }

    /// <summary>Production speed multiplier as the in-game HUD shows it (always signed, one
    /// decimal): 2.0 -> "+100.0%", 1.5 -> "+50.0%".</summary>
    public static string SpeedPct(double multiplier)
    {
        var delta = (multiplier - 1) * 100;
        return delta >= 0 ? $"+{delta:0.0}%" : $"{delta:0.0}%";
    }

    /// <summary>Multiplier delta as ±% from 100: 1.15 -> "+15%", 0.98 -> "-2%", 1.0 -> "±0%".</summary>
    public static string Pct(double x) => x switch
    {
        0 => "±0%",
        > 0 => $"+{x * 100:0.##}%",
        _ => $"{x * 100:0.##}%",
    };

    /// <summary>Drag coefficient: 0.800000011920929 -> "0.8", 0.232 -> "0.232", 1.0 -> "1.0".</summary>
    public static string Drag(double x) => x.ToString("0.0##", CultureInfo.InvariantCulture);

    /// <summary>Gear ratios: ToString("F2") with trailing zeros stripped — 1.785 -> "1.78",
    /// 2.105 -> "2.1", 1.0 -> "1".</summary>
    public static string GearRatio(double x)
    {
        var f2 = x.ToString("F2", CultureInfo.InvariantCulture);
        return f2.TrimEnd('0').TrimEnd('.');
    }

    /// <summary>The pak serializes floats as JValues holding float32; the JSON text carries the
    /// round-trip decimal ("1.315" not 1.315000057220459). The wiki renders from the text, so
    /// read the value the same way.</summary>
    public static double JvDouble(Newtonsoft.Json.Linq.JValue v) =>
        double.Parse(v.ToString(Newtonsoft.Json.Formatting.None), CultureInfo.InvariantCulture);

    /// <summary>Break ratio: 0 -> "0%", 0.6 -> "60.0%".</summary>
    public static string BrakeRatio(double x) => x == 0 ? "0%" : $"{x * 100:0.0}%";

    /// <summary>Comfort stars: 7 -> "⭐⭐⭐⭐⭐⭐⭐".</summary>
    public static string Stars(double comfort) => new('⭐', (int)Math.Round(comfort));

    /// <summary>Torque-curve point: 1.0 -> "1", 0.17500001 -> "0.18".</summary>
    public static string Curve(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A 3D vector struct -> "X cm × Y cm × Z cm" with each axis labeled.</summary>
    public static string Vec(double x, double y, double z) =>
        $"{Num(Eps(x))} cm × {Num(Eps(y))} cm × {Num(Eps(z))} cm";

    /// <summary>Axis values like -0.000122 are editor noise; the wiki renders them as 0.</summary>
    public static double Eps(double x) => Math.Abs(x) < 0.01 ? 0 : x;

    /// <summary>Downforce coefficient -> "coef (X kg @ 200 km/h)" using force = 7.098e-7 * v² * coef.
    /// The whole-vehicle Aero Lift carries the kind (downforce/lift), per-axle lifts do not.</summary>
    public static string AeroLift(double coef, bool withKind)
    {
        var force = 7.098e-7 * 40000 * coef;
        var kind = coef < 0 ? " downforce" : " lift";
        return withKind
            ? $"{coef:0} ({Math.Abs(force):0.0} kg{kind} @ 200 km/h)"
            : $"{coef:0} ({Math.Abs(force):0.0} kg @ 200 km/h)";
    }

    /// <summary>Enum tail after the last "::".</summary>
    public static string Tail(string? value)
    {
        if (value is null) return "";
        var idx = value.LastIndexOf("::", StringComparison.Ordinal);
        return idx < 0 ? value : value[(idx + 2)..];
    }

    /// <summary>"ClutchPackLSD" -> "Clutch Pack LSD": split camelCase for display.</summary>
    public static string Humanize(string value) =>
        Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");

    /// <summary>The wiki's slug for a pak part key: lowercase, '.' -> '_', "RideHeight_+N" ->
    /// "rideheight_pN", leading underscore stripped. "AngleKit_5" -> "anglekit_5".</summary>
    public static string PartSlug(string key)
    {
        var slug = key.ToLowerInvariant().Replace('.', '_');
        var m = Regex.Match(slug, @"^rideheight_\+(.+)$");
        if (m.Success) slug = "rideheight_p" + m.Groups[1].Value;
        return slug.TrimStart('_');
    }

    /// <summary>Generic display-name slug: lowercase, non-alphanumeric runs -> "_", trimmed —
    /// the same rule VehicleSlug applies ("Elisa Taxi" -> "elisa_taxi").</summary>
    public static string Slug(string name) =>
        Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    /// <summary>Sort key that moves a "#N (Family)" owner suffix ahead of the number, so parts
    /// group by family — "#1 (Dabo)", "#2 (Dabo)" adjacent — instead of all "#1"s first.
    /// Names without a family suffix ("#1") sort unchanged.</summary>
    public static string PartSortKey(string name) =>
        Regex.Replace(name, @"^#(\d+)\s*\((.+)\)$", "$2 #$1");

    /// <summary>Case-insensitive natural sort: digit runs compare as integers; names that are
    /// pure numbers (with an optional unit) sort numerically before everything else.</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            if (a == b) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            var ka = Key(a);
            var kb = Key(b);
            if (ka.Numeric != kb.Numeric) return ka.Numeric ? -1 : 1;
            if (ka.Numeric) return ka.Number.CompareTo(kb.Number);
            var ca = ka.Tokens;
            var cb = kb.Tokens;
            for (var i = 0; i < Math.Min(ca.Count, cb.Count); i++)
            {
                var r = CompareToken(ca[i], cb[i]);
                if (r != 0) return r;
            }
            return ca.Count.CompareTo(cb.Count);

        static int CompareToken(IComparable a, IComparable b) =>
            a is long la && b is long lb
                ? la.CompareTo(lb)
                : string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        private static (bool Numeric, double Number, List<IComparable> Tokens) Key(string s)
        {
            var n = s.ToLowerInvariant();
            var m = Regex.Match(n, @"^[+-]?\d+(\.\d+)?(\s*(%|cm|mm|kg|deg|rpm|l))?$");
            if (m.Success)
            {
                var num = Regex.Match(n, @"^[+-]?\d+(\.\d+)?").Value;
                return (true, double.Parse(num, CultureInfo.InvariantCulture), []);
            }
            var tokens = new List<IComparable>();
            foreach (var tok in Regex.Split(n, @"(\d+)"))
            {
                if (tok.Length == 0) continue;
                tokens.Add(long.TryParse(tok, out var num) ? (IComparable)num : tok);
            }
            return (false, 0, tokens);
        }
    }

    /// <summary>The 22 non-English languages the wiki lists (locres order) with English display names.</summary>
    public static readonly (string Code, string Display)[] Languages =
    [
        ("cs", "Czech"),
        ("de", "German"),
        ("es-419", "Spanish (Latin America)"),
        ("es-ES", "Spanish (Spain)"),
        ("fi", "Finnish"),
        ("fr", "French"),
        ("hu", "Hungarian"),
        ("it", "Italian"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("lt", "Lithuanian"),
        ("nl", "Dutch"),
        ("no", "Norwegian"),
        ("pl", "Polish"),
        ("pt-BR", "Portuguese (Brazil)"),
        ("ru", "Russian"),
        ("sv", "Swedish"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("vi", "Vietnamese"),
        ("zh-Hans", "Chinese (Simplified)"),
        ("zh-Hant", "Chinese (Traditional)"),
    ];

    /// <summary>"EMTVehicleType::HeavyMachinery" -> "Heavy Machinery", "SemiTractor" ->
    /// "Semi Tractor", "Racecar" -> "Racecar" (the wiki's comparison/list spelling).</summary>
    public static string HumanizeType(string pakType)
    {
        var tail = Tail(pakType);
        var spaced = Regex.Replace(tail, @"([a-z0-9])([A-Z])", "$1 $2");
        return spaced;
    }

    /// <summary>The infobox renders type + truck class in sentence case — "Semi trailer,
    /// Heavy duty", "Pickup, Light duty", "Kart". Truck class omitted when None.</summary>
    public static string InfoboxType(string pakType, string truckClass)
    {
        var tail = Tail(pakType);
        var type = SentenceCase(tail);
        var tc = Tail(truckClass);
        if (tc is "None" or "") return type;
        return $"{type}, {SentenceCase(tc)}";
    }

    private static string SentenceCase(string camel)
    {
        var words = Regex.Replace(camel, @"([a-z0-9])([A-Z])", "$1 $2").Split(' ');
        return string.Join(" ", words.Select((w, i) => i == 0 ? w : w.ToLowerInvariant()));
    }

    /// <summary>The intro sentence description: truck class first, all lowercase —
    /// "Semi trailer, Heavy duty" -> "heavy duty semi trailer".</summary>
    public static string IntroType(string pakType, string truckClass)
    {
        var tail = Tail(pakType);
        var type = Regex.Replace(tail, @"([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant();
        var tc = Tail(truckClass);
        if (tc is "None" or "") return type;
        return $"{Regex.Replace(tc, @"([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant()} {type}";
    }
}
