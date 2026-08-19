using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WikiGenerator;

internal static class RenderVehicles
{
    public static string VehiclePage(VehicleInfo v, Data data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{{infobox>");
        sb.AppendLine($"name = {v.En}");
        sb.AppendLine($"Internal key = {v.Key}");
        sb.AppendLine($"Type = {Format.InfoboxType(v.Type, v.TruckClass)}");
        sb.AppendLine($"Cost = {Format.N0(v.Cost)}");
        sb.AppendLine($"Weight = {Format.N0(v.WeightKg ?? 0)} kg");
        var enginePart = EnginePart(v, data);
        if (enginePart is not null) sb.AppendLine($"Engine = {EngineHp(enginePart)} HP");
        var drive = v.Drivetrain(spelledOut: true);
        if (drive.Length > 0) sb.AppendLine($"Drivetrain = {drive}");
        if (v.CargoSpace is { } space)
            sb.AppendLine($"Cargo space = [[cargo_space:{space.Type.ToLowerInvariant()}|{space.Type}]]");
        else if (v.InstallableSpaces.Count > 0)
            sb.AppendLine($"Cargo space = {string.Join(", ", v.InstallableSpaces.Select(s => $"[[cargo_space:{s.Type.ToLowerInvariant()}|{s.Type}]]"))} (installable)");
        // the wiki renders the default 1.0 when the CDO carries no AirDragCoeff
        sb.AppendLine($"Drag coefficient = {Format.Drag(v.DragCoeff ?? 1.0)}");
        if (v.Comfort > 0) sb.AppendLine($"Comfort = {Format.Stars(v.Comfort)}");
        if (v.FuelTankL is { } tank && tank > 0) sb.AppendLine($"Fuel = {Format.Num(tank)}L ({v.FuelType ?? "Gasoline"})");
        if (v.Seats is { } seats) sb.AppendLine($"Seats = {seats}");
        if (v.Levels.Count > 0) sb.AppendLine($"Level requirement = {LevelText(v)}");
        sb.AppendLine("}}");
        sb.AppendLine();
        sb.AppendLine($"====== {v.En} ======");
        sb.AppendLine($"**{v.En}** is a {Format.IntroType(v.Type, v.TruckClass)} vehicle in [[:motor_town|Motor Town]]");
        sb.AppendLine();

        // Specifications
        sb.AppendLine("===== Specifications =====");
        sb.AppendLine("^ Stat ^ Value ^");
        if (enginePart is not null)
            sb.AppendLine($"| Engine | [[parts:{enginePart.Slug}|{enginePart.En}]] ({EngineHp(enginePart)} HP) |");
        var transmission = DefaultPart(v, "Transmission", data);
        if (transmission is not null)
            sb.AppendLine($"| Transmission | [[parts:{transmission.Slug}|{transmission.En}]] |");
        if (drive.Length > 0) sb.AppendLine($"| Drivetrain | {drive} |");
        var fdr = DefaultPart(v, "FinalDriveRatio", data);
        if (fdr is not null) sb.AppendLine($"| Final Drive Ratio | {Format.Drag(fdr.FdrValue ?? 0)} |");
        sb.AppendLine($"| Chassis Weight | {Format.N0(v.WeightKg ?? 0)} kg |");
        sb.AppendLine($"| Total Weight (stock) | {Format.N0(TotalWeight(v, data))} kg |");
        if (v.DragCoeff is { } drag2 && drag2 > 0 && drag2 != 1) sb.AppendLine($"| Drag Coefficient | {Format.Drag(drag2)} |");
        sb.AppendLine();

        // Cargo Space
        if (v.CargoSpace is { } space2)
        {
            sb.AppendLine("===== Cargo Space =====");
            sb.AppendLine("^ Stat ^ Value ^");
            sb.AppendLine($"| Type | {space2.Type} |");
            if (space2.LengthM is { } len) sb.AppendLine($"| Length | {len:0.0} m |");
            if (space2.WidthM is { } wid) sb.AppendLine($"| Width | {wid:0.0} m |");
            if (space2.HeightM is { } hei) sb.AppendLine($"| Height | {hei:0.0} m |");
            if (space2.VolumeM3 is { } vol) sb.AppendLine($"| Volume | {vol:0.0} m³ |");
            if (space2.DumpKl is { } dump) sb.AppendLine($"| Dump Volume | {dump:0.0} kL |");
            if (space2.UnlimitedHeight) sb.AppendLine("| Unlimited Height | Yes |");
            if (space2.FixCargo) sb.AppendLine("| Fixed Cargo | Yes |");
            sb.AppendLine();
        }

        // Capabilities (after Cargo Space, before Delivery)
        var caps = Capabilities(v);
        if (caps.Count > 0)
        {
            sb.AppendLine("===== Capabilities =====");
            foreach (var c in caps) sb.AppendLine($"  * {c}");
            sb.AppendLine();
        }

        // Delivery
        if (v.BasePayment is { } basePayment || v.PaymentMultiplier is { } multiplier)
        {
            sb.AppendLine("===== Delivery =====");
            sb.AppendLine("^ Stat ^ Value ^");
            if (v.BasePayment is { } bp) sb.AppendLine($"| Base Payment | ${bp} |");
            if (v.PaymentMultiplier is { } pm) sb.AppendLine($"| Payment Multiplier | {pm:0.0}x |");
            sb.AppendLine();
        }

        // Default Parts
        sb.AppendLine("===== Default Parts =====");
        sb.AppendLine("^ Slot ^ Part ^ Total Mass ^");
        foreach (var row in DefaultPartRows(v, data))
            sb.AppendLine(row);
        sb.AppendLine();

        // Installable Parts
        sb.AppendLine("===== Installable Parts =====");
        sb.AppendLine();
        sb.AppendLine($"See [[vehicles:{VehicleSlug(v)}:installable_parts|Installable parts for {v.En}]].");
        sb.AppendLine();

        // Axle info
        if (v.Axles.Count > 0)
        {
            sb.AppendLine("===== Axle info =====");
            sb.AppendLine("^ Axle ^ Break Ratio ^ Driven ^ Dual Wheels ^ Liftable ^");
            var labels = AxleLabels(v.Axles.Count);
            for (var i = 0; i < v.Axles.Count; i++)
            {
                var axle = v.Axles[i];
                sb.AppendLine($"| {labels[i]} | {Format.BrakeRatio(axle.BrakeRatio)} | {YesNo(axle.Driven)} | {YesNo(axle.Dual)} | {YesNo(axle.Lift)} |");
            }
            sb.AppendLine();
        }

        // In other languages
        sb.AppendLine("===== In other languages =====");
        sb.AppendLine("^ Language ^ Name ^");
        foreach (var (code, display) in Format.Languages)
            sb.AppendLine($"| {display} | {v.Names.GetValueOrDefault(code) ?? v.En} |");
        return sb.ToString();
    }

    /// <summary>The wiki's vehicle page slug comes from the display name: "Elisa Taxi" ->
    /// "elisa_taxi", "Goliath-4" -> "goliath_4", "Air City" -> "air_city".</summary>
    public static string VehicleSlug(VehicleInfo v) =>
        Regex.Replace(v.En.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    private static string YesNo(bool b) => b ? "**Yes**" : "No";

    private static string LevelText(VehicleInfo v) =>
        string.Join(", ", v.Levels.Select(l => $"{l.Name.Replace("CL_", "")}: {l.Value}"));

    private static List<string> Capabilities(VehicleInfo v)
    {
        var caps = new List<string>();
        if (v.Flags.Contains("taxiable")) caps.Add("Taxi");
        if (v.Flags.Contains("busable")) caps.Add("Bus");
        if (v.Flags.Contains("limoable")) caps.Add("Limousine");
        if (v.Flags.Contains("raceCar")) caps.Add("Race car");
        if (v.Flags.Contains("trailerHauling")) caps.Add("Can haul trailer");
        if (v.Flags.Contains("hasFuelPump")) caps.Add("Has fuel pump");
        return caps;
    }

    private static PartInfo? EnginePart(VehicleInfo v, Data data)
    {
        var engine = DefaultPart(v, "Engine", data);
        return engine;
    }

    private static int EngineHp(PartInfo engine)
    {
        var m = Regex.Match(engine.En, @"(\d+)\s*HP");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static PartInfo? DefaultPart(VehicleInfo v, string slot, Data data)
    {
        foreach (var (s, partKey) in v.DefaultParts)
        {
            if (Regex.IsMatch(s, @"^" + Regex.Escape(slot) + @"\d*$"))
                return data.PartByKey(partKey);
        }
        return null;
    }

    private static double TotalWeight(VehicleInfo v, Data data)
    {
        var total = v.WeightKg ?? 0;
        foreach (var (_, partKey) in v.DefaultParts)
        {
            if (data.PartByKey(partKey)?.MassKg is { } mass) total += mass;
        }
        return total;
    }

    /// <summary>Default Parts rows: group by base slot (Tire0..3 -> Tire), one row per distinct
    /// part in first-occurrence order, count = occurrences, total mass = part mass × count.</summary>
    private static IEnumerable<string> DefaultPartRows(VehicleInfo v, Data data)
    {
        var seen = new HashSet<(string Slot, string Part)>();
        foreach (var (slot, partKey) in v.DefaultParts)
        {
            var baseSlot = Regex.Replace(slot, @"\d+$", "");
            if (!seen.Add((baseSlot, partKey))) continue;
            var count = v.DefaultParts.Count(dp => Regex.Replace(dp.Slot, @"\d+$", "") == baseSlot && dp.Part == partKey);
            var part = data.PartByKey(partKey);
            var display = part?.En ?? partKey;
            var link = $"[[parts:{part?.Slug ?? Format.PartSlug(partKey)}|{display}]]";
            var suffix = count > 1 ? $" (×{count})" : "";
            var mass = part?.MassKg is { } m ? $"{Format.N0(m * count)} kg" : "—";
            yield return $"| {baseSlot} | {link}{suffix} | {mass} |";
        }
    }

    private static string[] AxleLabels(int count) => count switch
    {
        1 => ["Front"],
        2 => ["Front", "Rear"],
        3 => ["Front", "Middle", "Rear"],
        4 => ["Front", "Front Middle", "Rear Middle", "Rear"],
        _ => Enumerable.Range(0, count).Select((_, i) => i == 0 ? "Front" : i == count - 1 ? "Rear" : "Middle").ToArray(),
    };

    // ------------------------------------------------------------------ list page

    public static string ListOfVehicles(List<VehicleInfo> vehicles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("====== List of Vehicles ======");
        sb.AppendLine();
        sb.AppendLine($"There are {vehicles.Count} vehicles in [[:motor_town|Motor Town]]. For the **extended comparison** see [[:vehicle_comparison|vehicle comparison table]].");
        sb.AppendLine();
        foreach (var group in vehicles
                     .GroupBy(v => Format.HumanizeType(v.Type))
                     .OrderBy(g => g.Key, Format.NaturalComparer.Instance))
        {
            sb.AppendLine($"===== {group.Key} =====");
            foreach (var v in group.OrderBy(v => v.En, StringComparer.Ordinal))
                sb.AppendLine($"  * [[vehicles:{VehicleSlug(v)}|{v.En}]]");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ comparison table

    public static string Comparison(List<VehicleInfo> vehicles, Data data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("====== Vehicle Comparison Table ======");
        sb.AppendLine();
        sb.AppendLine("^ Name ^ Type ^ Cost ^ Drivetrain ^ Chassis Weight ^ Total Weight ^ Drag ^");
        foreach (var v in vehicles
                     .GroupBy(v => Format.HumanizeType(v.Type))
                     .OrderBy(g => g.Key, Format.NaturalComparer.Instance)
                     .SelectMany(g => g.OrderBy(v => v.En, StringComparer.Ordinal)))
        {
            var drive = v.Drivetrain(spelledOut: true);
            var drag = Format.Drag(v.DragCoeff ?? 1.0);
            sb.AppendLine($"| [[vehicles:{VehicleSlug(v)}|{v.En}]] | {Format.HumanizeType(v.Type)} | {Format.N0(v.Cost)} | {drive} | {Format.N0(v.WeightKg ?? 0)} kg | {Format.N0(TotalWeight(v, data))} kg | {drag} |");
        }
        return sb.ToString();
    }
}
