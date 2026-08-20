using System.Text;

namespace WikiGenerator;

internal static class RenderDelivery
{
    /// <summary>Just the infobox block — transcluded first
    /// ({{page>delivery_points:{slug}:auto_infobox}}). Regenerated every run.</summary>
    public static string DeliveryPointPageInfobox(DeliveryPointInfo point, Data data)
    {
        var import = JoinDistinct(data,
            point.Configs.SelectMany(c => c.Inputs).Select(r => (r.Key, (string?)null)),
            point.Configs.SelectMany(c => c.InputTypes).Select(r => ((string?)null, Format.Tail(r.Key))),
            point.Demands.Select(d => (d.Key, d.Type)));
        var export = JoinDistinct(data,
            point.Configs.SelectMany(c => c.Outputs).Select(r => (r.Key, (string?)null)),
            point.Configs.SelectMany(c => c.OutputTypes).Select(r => ((string?)null, Format.Tail(r.Key))),
            point.PassiveSupplies.Select(s => (s.Key, s.Type)));
        var requiredSpaceType = string.Join(", ", RequiredSpaceTypes(point, data));

        var sb = new StringBuilder();
        sb.AppendLine("{{infobox>");
        sb.AppendLine($"name = {point.En}");
        if (import.Length > 0) sb.AppendLine($"Import = {import}");
        if (export.Length > 0) sb.AppendLine($"Export = {export}");
        if (requiredSpaceType.Length > 0) sb.AppendLine($"Required Space Type = {requiredSpaceType}");
        sb.AppendLine($"Location = {point.Zone}");
        sb.AppendLine($"External Link = [[https://www.aseanmotorclub.com/map?menu=deliveries/{point.Guid}&delivery={point.Guid}|View on map]]");
        sb.AppendLine("}}");
        return sb.ToString();
    }

    /// <summary>Heading + intro sentence — generated once, straight into the live shell
    /// page's bootstrap suggestion.</summary>
    public static string DeliveryPointPageHeading(DeliveryPointInfo point) =>
        $"====== {point.En} ======\n\n**{point.En}** is a delivery point in [[:motor_town|Motor Town]].";

    /// <summary>Production through In other languages — transcluded second
    /// ({{page>delivery_points:{slug}:auto_details}}).</summary>
    public static string DeliveryPointPageDetails(DeliveryPointInfo point, Data data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== Production =====");

        var recipeRows = new List<(string Inputs, string Output, string Time)>();
        foreach (var c in point.Configs)
            recipeRows.Add((RenderCargos.InputText(c, data), OutputText(c, data), Format.Duration(c.TimeSeconds)));
        foreach (var s in point.PassiveSupplies)
            recipeRows.Add(("(passive)", CargoRefText(s, data), "—"));

        if (recipeRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("==== Recipes ====");
            sb.AppendLine("^ Inputs ^ Output ^ Time ^");
            foreach (var (inputs, output, time) in recipeRows)
                sb.AppendLine($"| {inputs} | {output} | {time} |");
        }

        if (point.Demands.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("==== Demand ====");
            sb.AppendLine("^ Cargo ^ Payment Multiplier ^");
            foreach (var d in point.Demands)
                sb.AppendLine($"| {CargoRefText(d, data)} | {d.PaymentMultiplier:F1}x |");

            sb.AppendLine();
            sb.AppendLine("==== Storage ====");
            sb.AppendLine("^ Cargo ^ Max Storage ^");
            foreach (var d in point.Demands)
                sb.AppendLine($"| {CargoRefText(d, data)} | {(d.MaxStorage is { } ms ? Format.N0(ms) : "—")} |");
        }

        sb.AppendLine();
        sb.AppendLine("===== In other languages =====");
        sb.AppendLine("^ Language ^ Name ^");
        foreach (var (code, display) in Format.Languages)
            sb.AppendLine($"| {display} | {point.Names.GetValueOrDefault(code) ?? point.En} |");
        return sb.ToString();
    }

    private static string OutputText(ProductionConfig c, Data data)
    {
        var parts = new List<string>();
        foreach (var r in c.Outputs)
            if (r.Key is { } k) parts.Add(RenderCargos.CargoLink(data, k));
        foreach (var r in c.OutputTypes)
            if (r.Key is { } k) parts.Add(RenderCargos.CargoTypeText(data, Format.Tail(k)));
        if (parts.Count > 0) return string.Join(", ", parts);
        // no output cargo: the input instead boosts the point's other recipes, matching the
        // in-game production panel's "Production Speed: +100.0%" row
        return c.SpeedMultiplier != 1 ? $"Production Speed: {Format.SpeedPct(c.SpeedMultiplier)}" : "—";
    }

    /// <summary>The cargo space type(s) needed to carry this point's produced output away —
    /// the in-game production panel's own requirement, derived from every resolved output
    /// cargo's own Compatible Cargo Space Types (recipes/passive supplies have no separate
    /// space-type field of their own). Type-ref outputs (no specific cargo key) can't
    /// resolve to a cargo's space types and are skipped.</summary>
    private static List<string> RequiredSpaceTypes(DeliveryPointInfo point, Data data)
    {
        var keys = point.Configs.SelectMany(c => c.Outputs).Select(r => r.Key)
            .Concat(point.PassiveSupplies.Select(s => s.Key));
        var types = new List<string>();
        foreach (var key in keys)
        {
            if (key is not { } k) continue;
            var cargo = data.CargoByKey(k);
            if (cargo is null) continue;
            foreach (var t in cargo.SpaceTypes)
                if (!types.Contains(t, StringComparer.Ordinal)) types.Add(t);
        }
        return types.Select(t => $"[[cargo_space:{t.ToLowerInvariant()}|{t}]]").ToList();
    }

    private static string CargoRefText(CargoRef r, Data data) =>
        r.Key is { } k ? RenderCargos.CargoLink(data, k) : RenderCargos.CargoTypeText(data, r.Type ?? "?");

    /// <summary>Joins cargo refs from several (key, typeTail) groups into one distinct,
    /// linked-when-possible list for the infobox Import/Export rows.</summary>
    private static string JoinDistinct(Data data, params IEnumerable<(string? Key, string? Type)>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var (key, type) in groups.SelectMany(g => g))
        {
            if (key is { Length: > 0 })
            {
                if (!seen.Add(key)) continue;
                parts.Add(RenderCargos.CargoLink(data, key));
            }
            else if (type is { Length: > 0 })
            {
                if (!seen.Add(type)) continue;
                parts.Add(RenderCargos.CargoTypeText(data, type));
            }
        }
        return string.Join(", ", parts);
    }

    // ------------------------------------------------------------------ list page

    public static string ListOfDeliveryPoints(List<DeliveryPointInfo> points)
    {
        var listed = points.Where(p => p.HasPage).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("====== List of Delivery Points ======");
        sb.AppendLine();
        sb.AppendLine($"There are {listed.Count} delivery points in [[:motor_town|Motor Town]].");
        sb.AppendLine();
        foreach (var group in listed
                     .GroupBy(p => p.ZoneGroup)
                     .OrderBy(g => g.Key, Format.NaturalComparer.Instance))
        {
            sb.AppendLine($"===== {group.Key} =====");
            foreach (var p in group.OrderBy(p => p.En, StringComparer.Ordinal))
                sb.AppendLine($"  * [[delivery_points:{p.Slug}|{p.En}]]");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
