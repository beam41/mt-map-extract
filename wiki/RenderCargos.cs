using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WikiGenerator;

internal static class RenderCargos
{
    /// <summary>Just the infobox block — transcluded first
    /// ({{page>cargos:{slug}:auto_infobox}}). Regenerated every run.</summary>
    public static string CargoPageInfobox(CargoInfo cargo, Data data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{{infobox>");
        sb.AppendLine($"name = {cargo.Name}");
        sb.AppendLine($"Cargo Type = {CargoTypeText(data, cargo.Type)}");
        // a handful of cargos (Raven, Formula SCM) ARE a vehicle - the delivery point that
        // produces this cargo is that vehicle's actual factory (Data.CargoProducers).
        if (data.VehicleByKey(cargo.Key) is { } vehicle)
            sb.AppendLine($"Vehicle = [[vehicles:{RenderVehicles.VehicleSlug(vehicle)}|{vehicle.En}]]");
        sb.AppendLine($"Volume = {Format.Num(cargo.Volume)}");
        if (cargo.WeightText() is { } weight) sb.AppendLine($"Weight = {weight}");
        sb.AppendLine($"Payment = ${cargo.PaymentPerKm}/km");
        sb.AppendLine("}}");
        return sb.ToString();
    }

    /// <summary>Heading + intro sentence — generated once, straight into the live shell
    /// page's bootstrap suggestion.</summary>
    public static string CargoPageHeading(CargoInfo cargo, Data data) =>
        $"""
        ====== {cargo.Name} ======

        {IntroSentence(cargo, data)}
        """;

    /// <summary>Specifications through Production — transcluded second
    /// ({{page>cargos:{slug}:auto_details}}).</summary>
    public static string CargoPageDetails(CargoInfo cargo, Data data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== Specifications =====");
        sb.AppendLine("^ Stat ^ Value ^");
        sb.AppendLine($"| Type | {CargoTypeText(data, cargo.Type)} |");
        if (cargo.WeightText() is { } w2) sb.AppendLine($"| Weight | {w2} |");
        sb.AppendLine($"| Payment per km | ${cargo.PaymentPerKm} |");
        if (cargo.PaymentMultiplier != 1) sb.AppendLine($"| Payment multiplier | {cargo.PaymentMultiplier:F1} |");
        if (cargo.BasePayment > 0) sb.AppendLine($"| Base payment | ${cargo.BasePayment} |");
        if (cargo.MinDist > 0) sb.AppendLine($"| Min delivery distance | {Format.Num(cargo.MinDist)}m |");
        if (cargo.MaxDist > 0) sb.AppendLine($"| Max delivery distance | {Format.Num(cargo.MaxDist)}m |");
        sb.AppendLine($"| Stackable | {(cargo.Stackable ? "Yes" : "No")} |");
        sb.AppendLine($"| Can be pickup | {(PickupableTypes.Contains(cargo.Type) ? "Yes" : "No")} |");
        sb.AppendLine($"| Fragile | {(cargo.Fragile > 0 ? $"Level {cargo.Fragile:0.0}" : "No")} |");

        if (cargo.SpaceTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("===== Compatible Cargo Space Types =====");
            foreach (var t in cargo.SpaceTypes.Distinct(StringComparer.Ordinal))
                sb.AppendLine($"  * [[cargo_space:{t.ToLowerInvariant()}|{t}]]");
        }

        var producers = ConfigsFor(data, cargo, isOutput: true);
        var consumers = ConfigsFor(data, cargo, isOutput: false);
        if (producers.Count > 0 || consumers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("===== Production =====");
            if (producers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("==== Produced At ====");
                sb.AppendLine("^ Location ^ Inputs ^ Time ^");
                foreach (var (point, inputs, time) in producers)
                    sb.AppendLine($"| {point} | {inputs} | {time} |");
            }
            if (consumers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("==== Consumed At ====");
                sb.AppendLine("^ Location ^ Inputs ^ Time ^");
                foreach (var (point, inputs, time) in consumers)
                    sb.AppendLine($"| {point} | {inputs} | {time} |");
            }
        }
        return sb.ToString();
    }

    /// <summary>"**{name}** is {a|an} {type} cargo in Motor Town" — same a/an-article idiom
    /// as the part page's intro; "None"-type cargos (bulk/generic, no locres group) drop
    /// the type clause entirely rather than reading "is a none cargo".</summary>
    private static string IntroSentence(CargoInfo cargo, Data data)
    {
        if (cargo.Type == "None") return $"**{cargo.Name}** is a cargo in [[:motor_town|Motor Town]].";
        var typeEnglish = data.CargoTypeEnglish(cargo.Type).ToLowerInvariant();
        var article = "aeiou".Contains(typeEnglish[0]) ? "an" : "a";
        return $"**{cargo.Name}** is {article} {typeEnglish} cargo in [[:motor_town|Motor Town]].";
    }

    /// <summary>Only these cargo types can be hand-picked up (user-confirmed game rule).</summary>
    private static readonly HashSet<string> PickupableTypes = new(StringComparer.Ordinal)
    {
        "SmallPackage", "Food", "MilitarySupply",
    };

    private static List<(string Point, string Inputs, string Time)> ConfigsFor(Data data, CargoInfo cargo, bool isOutput)
    {
        var result = new List<(string SortKey, string Point, string Inputs, string Time)>();
        foreach (var point in data.Points)
        {
            var pointText = point.HasPage ? $"[[delivery_points:{point.Slug}|{point.En}]]" : point.En;
            var matchedRecipe = false;
            foreach (var c in point.Configs)
            {
                var matches = isOutput
                    ? RefMatch(c.Outputs, cargo.Key) || TypeRefMatch(c.OutputTypes, cargo.Type, c.OutputTags, cargo.Tags)
                    : RefMatch(c.Inputs, cargo.Key) || TypeRefMatch(c.InputTypes, cargo.Type, c.InputTags, cargo.Tags);
                if (!matches) continue;
                matchedRecipe = true;
                var inputs = InputText(c, data);
                result.Add((point.En, pointText, inputs, Format.Duration(c.TimeSeconds)));
            }
            if (matchedRecipe) continue;

            if (isOutput)
            {
                foreach (var s in point.PassiveSupplies)
                {
                    if (!RefEntryMatch(s, cargo)) continue;
                    result.Add((point.En, pointText, "(passive)", "—"));
                    break;
                }
            }
            else
            {
                foreach (var d in point.Demands)
                {
                    if (!RefEntryMatch(d, cargo)) continue;
                    result.Add((point.En, pointText, "—", "—"));
                    break;
                }
            }
        }
        // the wiki orders rows by point name, keeping each point's config order
        return result.OrderBy(r => r.SortKey, StringComparer.Ordinal)
            .Select(r => (r.Point, r.Inputs, r.Time)).ToList();
    }

    private static bool RefMatch(List<CargoRef> refs, string key) =>
        refs.Any(r => r.Key == key);

    private static bool TypeRefMatch(List<CargoRef> refs, string type, List<string> queryTags, List<string> cargoTags)
    {
        var hit = refs.Any(r => r.Key == "EDeliveryCargoType::" + type);
        if (!hit) return false;
        if (queryTags.Count == 0) return true;
        return queryTags.Any(cargoTags.Contains);
    }

    private static bool RefEntryMatch(CargoRef r, CargoInfo cargo)
    {
        if (r.Key == cargo.Key) return true;
        if (r.Type == cargo.Type)
            return r.Tags.Count == 0 || r.Tags.Any(cargo.Tags.Contains);
        return false;
    }

    internal static string InputText(ProductionConfig c, Data data)
    {
        var parts = new List<string>();
        foreach (var r in c.Inputs)
            if (r.Key is { } k) parts.Add($"{Format.Num(r.Count)}× {CargoLink(data, k)}");
        foreach (var r in c.InputTypes)
            if (r.Key is { } k) parts.Add($"{Format.Num(r.Count)}× {CargoTypeText(data, Format.Tail(k))}");
        return parts.Count == 0 ? "(passive)" : string.Join(", ", parts);
    }

    /// <summary>A cargo key as a link to its cargo page, plain text when the key doesn't
    /// resolve to a known cargo (shared with RenderDelivery for Inputs/Output/Import/Export
    /// cells).</summary>
    internal static string CargoLink(Data data, string key)
    {
        var cargo = data.CargoByKey(key);
        return cargo is not null ? $"[[cargos:{cargo.Key.ToLowerInvariant()}|{cargo.Name}]]" : key;
    }

    /// <summary>An EDeliveryCargoType tail as a link to its aggregate cargo_type page,
    /// labeled with the locres display name ("SmallPackage" -> "Box"); "None" (not a real
    /// group) and empty stay plain text.</summary>
    internal static string CargoTypeText(Data data, string type) =>
        type is "None" or "" ? type : $"[[cargo_type:{type.ToLowerInvariant()}|{data.CargoTypeEnglish(type)}]]";

    // ------------------------------------------------------------------ cargo type pages

    public static string CargoTypePage(CargoTypeInfo type, Data data)
    {
        var english = data.CargoTypeEnglish(type.Type);
        var sb = new StringBuilder();
        sb.AppendLine($"====== {english} Cargo Type ======");
        sb.AppendLine();
        sb.AppendLine($"There are {type.Cargos.Count} {english} cargos in [[:motor_town|Motor Town]].");
        sb.AppendLine();
        sb.AppendLine("^ Name ^ Weight ^ Payment/km ^");
        foreach (var cargo in type.Cargos.OrderBy(c => c.Name, Format.NaturalComparer.Instance))
        {
            var weight = cargo.WeightText() ?? "";
            sb.AppendLine($"| [[cargos:{cargo.Key.ToLowerInvariant()}|{cargo.Name}]] | {weight} | ${cargo.PaymentPerKm} |");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ cargo space pages

    public static string CargoSpacePage(SpaceInfo space)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"====== {space.Type} Cargo Space ======");
        sb.AppendLine();
        sb.AppendLine($"Everything that uses or accepts the **{space.Type}** cargo space.");
        Bullets(sb, "Cargos", space.Cargos.Count,
            space.Cargos.OrderBy(c => c.Key, Format.NaturalComparer.Instance).Select(c => ($"cargos:{c.Key.ToLowerInvariant()}", c.Name)));
        Bullets(sb, "Vehicles", space.Vehicles.Count,
            space.Vehicles.Select(e => ($"vehicles:{RenderVehicles.VehicleSlug(e.Vehicle)}", e.Vehicle.En + (e.Installable ? " (installable)" : ""))));
        Bullets(sb, "Parts", space.Parts.Count,
            space.Parts.Select(p => ($"parts:{p.Slug}", p.En)));
        return sb.ToString();
    }

    /// <summary>An aggregate bullet section; omitted entirely (no heading, no "(0)") when
    /// the group is empty rather than showing a bare count.</summary>
    private static void Bullets(StringBuilder sb, string heading, int count, IEnumerable<(string Slug, string Name)> items)
    {
        if (count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"===== {heading} ({count}) =====");
        foreach (var (slug, name) in items)
            sb.AppendLine($"  * [[{slug}|{name}]]");
    }

    // ------------------------------------------------------------------ list page

    public static string ListOfCargos(List<CargoInfo> cargos, List<CargoTypeInfo> types, Data data)
    {
        var active = cargos.Where(c => !c.Deprecated).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("====== List of Cargos ======");
        sb.AppendLine();
        sb.AppendLine($"There are {active.Count} active cargos in [[:motor_town|Motor Town]].");
        sb.AppendLine();
        sb.AppendLine("^ Name ^ Type ^ Weight ^ Payment/km ^");
        foreach (var cargo in active
                     .GroupBy(c => c.Type)
                     .OrderBy(g => g.Key, Format.NaturalComparer.Instance)
                     .SelectMany(g => g.OrderBy(c => c.Name, Format.NaturalComparer.Instance)))
        {
            var weight = cargo.WeightText() ?? "";
            sb.AppendLine($"| [[cargos:{cargo.Key.ToLowerInvariant()}|{cargo.Name}]] | {CargoTypeText(data, cargo.Type)} | {weight} | ${cargo.PaymentPerKm} |");
        }

        Bullets(sb, "Cargo Types", types.Count,
            types.OrderBy(t => t.Type, Format.NaturalComparer.Instance).Select(t => ($"cargo_type:{t.Type.ToLowerInvariant()}", data.CargoTypeEnglish(t.Type))));
        return sb.ToString();
    }
}
