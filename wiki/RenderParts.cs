using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace WikiGenerator;

internal static class RenderParts
{
    public static string PartPage(PartInfo part)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{{infobox>");
        sb.AppendLine($"name = {part.En}");
        sb.AppendLine($"Part Type = {part.TypeEnglish}");
        sb.AppendLine($"Cost = {Format.N0(part.Cost)}");
        if (part.MassKg is { } mass) sb.AppendLine($"Mass = {Format.N0(mass)} kg");
        sb.AppendLine("}}");
        sb.AppendLine();
        sb.AppendLine($"====== {part.En} ======");
        sb.AppendLine();
        var article = "aeiou".Contains(char.ToLowerInvariant(part.TypeEnglish[0])) ? "an" : "a";
        sb.AppendLine($"**{part.En}** is {article} {part.TypeEnglish.ToLowerInvariant()} part for vehicles in [[:motor_town|Motor Town]].");
        sb.AppendLine();
        sb.AppendLine("===== Specifications =====");
        sb.AppendLine("^ Stat ^ Value ^");
        sb.AppendLine($"| Type | {part.TypeEnglish} |");
        sb.AppendLine($"| Cost | {Format.N0(part.Cost)} |");
        if (part.MassKg is { } m2) sb.AppendLine($"| Mass | {Format.N0(m2)} kg |");
        sb.AppendLine();

        var rows = StatsRows(part);
        if (rows.Count > 0)
        {
            sb.AppendLine("===== Stats =====");
            sb.AppendLine();
            var isTire = part.Stats["tire"] is JObject || part.Stats["Tire"] is JObject;
            if (isTire)
            {
                // the wiki splits tire stats into "Tire" (dual-rear flag) and "Tire Physics"
                var dual = rows.FirstOrDefault(r => r.Item1 == "Dual Rear");
                sb.AppendLine("==== Tire ====");
                sb.AppendLine("^ Stat ^ Value ^");
                if (dual.Item1 is not null) sb.AppendLine($"| {dual.Item1} | {dual.Item2} |");
                var physics = rows.Where(r => r.Item1 != "Dual Rear").ToList();
                if (physics.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("==== Tire Physics ====");
                    sb.AppendLine("^ Stat ^ Value ^");
                    foreach (var (label, value) in physics) sb.AppendLine($"| {label} | {value} |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"==== {StatsHeading(part)} ====");
                sb.AppendLine("^ Stat ^ Value ^");
                foreach (var (label, value) in rows) sb.AppendLine($"| {label} | {value} |");
                sb.AppendLine();
            }
        }
        else
        {
            // one blank line before the next heading (stat-less pages have no Stats block)
        }

        sb.AppendLine("===== Installable Vehicles =====");
        sb.AppendLine($"See [[parts:{part.Slug}:installable_vehicles|Vehicles that can install {part.En}]].");
        sb.AppendLine();
        sb.AppendLine("===== In other languages =====");
        sb.AppendLine("^ Language ^ Name ^");
        foreach (var (code, display) in Format.Languages)
            sb.AppendLine($"| {display} | {part.Names.GetValueOrDefault(code) ?? part.En} |");
        return sb.ToString();
    }

    private static string StatsHeading(PartInfo part)
    {
        var stats = part.Stats;
        if (stats["engine"] is JObject) return "Engine Physics";
        if (stats["transmission"] is JObject) return "Transmission Physics";
        if (stats["tire"] is JObject || stats["Tire"] is JObject) return "Tire";
        if (stats["lsd"] is JObject) return "LSD";
        if (part.HasAero) return "Aero";
        if (stats["FinalDriveRatio"] is JValue) return "Final Drive Ratio";
        foreach (var (structName, heading) in StructHeadings)
            if (stats[structName] is JObject) return heading;
        return part.StatsHeading;
    }

    private static readonly (string Struct, string Heading)[] StructHeadings =
    [
        ("AngleKit", "Angle Kit"),
        ("AntiRollBar", "Anti-Roll Bar"),
        ("BrakeBalance", "Brake Balance"),
        ("BrakePad", "Brake Pad"),
        ("BrakePower", "Brake Power"),
        ("SuspensionDamper", "Suspension Damper"),
        ("SuspensionSpring", "Suspension Spring"),
        ("SuspensionRideHeight", "Suspension Ride Height"),
        ("CoolantRadiator", "Coolant Radiator"),
        ("Turbocharger", "Turbocharger"),
        ("Intake", "Intake"),
        ("WheelSpacer", "Wheel Spacer"),
        ("Winch", "Winch"),
        ("TrailerHitch", "Trailer Hitch"),
        ("Taxi", "Taxi"),
        ("CargoBed", "Cargo Bed"),
        ("RoofRack", "Roof Rack"),
        ("ItemInventory", "Inventory"),
        ("FuelTank", "Fuel Tank"),
    ];

    private static List<(string Label, string Value)> StatsRows(PartInfo part)
    {
        var rows = new List<(string, string)>();
        var stats = part.Stats;

        if (stats["engine"] is JObject e) EngineRows(e, part.Electric, rows);
        if (stats["transmission"] is JObject tr) TransmissionRows(tr, rows);
        if (stats["tire"] is JObject tireStat || stats["Tire"] is JObject)
        {
            // the wiki splits tire stats into "Tire" (dual-rear flag) and "Tire Physics" (asset)
            if (stats["Tire"] is JObject tireStruct && tireStruct["bIsDualRearWheel"] is JValue dual)
                rows.Add(("Dual Rear", (bool?)dual.Value == true ? "Yes" : "No"));
            if (stats["tire"] is JObject t) TirePhysicsRows(t, rows);
        }
        if (stats["lsd"] is JObject lsd) LsdRows(lsd, rows);

        foreach (var (structName, _) in StructHeadings)
            if (stats[structName] is JObject s) StructRows(structName, s, rows);

        if (part.HasAero) AeroRows(part, rows);
        if (stats["FinalDriveRatio"] is JValue fdr) rows.Add(("Final Drive Ratio", Format.Num(Convert.ToDouble(fdr.Value))));

        return rows;
    }


    private static void EngineRows(JObject e, bool electric, List<(string, string)> rows)
    {
        if (e["Inertia"] is JValue inertia) rows.Add(("Rotational Inertia", $"{Format.Num(Convert.ToDouble(inertia.Value))} kg·m²"));
        if (e["StarterTorque"] is JValue stv && Convert.ToDouble(stv.Value) != 0) rows.Add(("Starter Torque", $"{Format.N0(Convert.ToDouble(stv.Value))} N·m"));
        else if (electric) rows.Add(("Starter Torque", "0 N·m"));
        if (e["MaxTorque"] is JValue maxTq && Convert.ToDouble(maxTq.Value) != 0) rows.Add(("Max Torque", $"{Format.N0(Convert.ToDouble(maxTq.Value))} N·m"));
        if (e["MaxRPM"] is JValue maxRpm) rows.Add(("Max RPM", $"{Format.Num(Convert.ToDouble(maxRpm.Value))} rpm"));
        if (e["FrictionViscosityCoeff"] is JValue fv) rows.Add(("Friction Viscosity", Format.Num(Convert.ToDouble(fv.Value))));
        if (e["IdleThrottle"] is JValue idv && Convert.ToDouble(idv.Value) != 0) rows.Add(("Idle Throttle", $"{Convert.ToDouble(idv.Value) * 100:0.##}%"));
        else if (electric) rows.Add(("Idle Throttle", "0%"));
        if (e["FuelConsumption"] is JValue fc) rows.Add(("Fuel Consumption", Format.Num(Convert.ToDouble(fc.Value))));
        if (e["BlipThrottle"] is JValue btv && Convert.ToDouble(btv.Value) != 0) rows.Add(("Blip Throttle", Format.Num(Convert.ToDouble(btv.Value))));
        else if (electric) rows.Add(("Blip Throttle", "0"));
        if (e["TorqueCurve"] is JArray curve)
            rows.Add(("Torque Curve", string.Join(", ", curve.OfType<JObject>()
                .Select(k => $"{Format.Curve(Convert.ToDouble(k["Value"]))} @ {Format.Curve(Convert.ToDouble(k["Time"]))}"))));
        if (e["StarterRPM"] is JValue srv && Convert.ToDouble(srv.Value) != 0) rows.Add(("Starter RPM", $"{Format.Num(Convert.ToDouble(srv.Value))} rpm"));
        else if (electric) rows.Add(("Starter RPM", "0 rpm"));
        if (e["FrictionCoulombCoeff"] is JValue cc) rows.Add(("Friction Coulomb Coefficient", Format.Num(Convert.ToDouble(cc.Value))));
        if (e["FuelType"] is JValue ft) rows.Add(("Fuel Type", Format.Tail((string?)ft.Value)));
        if (e["EngineType"] is JValue et) rows.Add(("Engine Type", Format.Tail((string?)et.Value)));
        if (e["IntakeSpeedEfficency"] is JValue ise) rows.Add(("Intake Speed Efficency", Format.Num(Convert.ToDouble(ise.Value))));
        if (e["BlipDurationSeconds"] is JValue bd) rows.Add(("Blip Duration", $"{Format.Num(Convert.ToDouble(bd.Value))} s"));
        if (e["MaxJakeBrakeStep"] is JValue jb) rows.Add(("Max Jake Brake Step", Format.Num(Convert.ToDouble(jb.Value))));
        if (e["AfterFireProbability"] is JValue af) rows.Add(("After-Fire Probability", $"{Convert.ToDouble(af.Value) * 100:0.##}%"));
        if (e["HeatingPower"] is JValue hp) rows.Add(("Heating Power", Format.Pct(Convert.ToDouble(hp.Value) - 1)));
        if (e["CoolingEfficiency"] is JValue ce) rows.Add(("Cooling Efficiency", Format.Pct(Convert.ToDouble(ce.Value) - 1)));
        if (e["MaxRegenTorqueRatio"] is JValue mr) rows.Add(("Max Regen Torque Ratio", $"{Convert.ToDouble(mr.Value) * 100:0}%"));
        if (e["MotorMaxPower"] is JValue mp) rows.Add(("Motor Max Power", $"{Format.N0(Convert.ToDouble(mp.Value))} W"));
        if (e["MotorMaxVoltage"] is JValue mv) rows.Add(("Motor Max Voltage", $"{Format.N0(Convert.ToDouble(mv.Value))} V"));
    }

    private static void TransmissionRows(JObject tr, List<(string, string)> rows)
    {
        if (tr["TorqueConvertorStallRPM"] is JValue stall) rows.Add(("Torque Converter Stall RPM", $"{Format.Num(Convert.ToDouble(stall.Value))} rpm"));
        if (tr["TorqueConvertorStallRatioPower"] is JValue srp) rows.Add(("Torque Converter Stall Ratio Power", Format.Num(Convert.ToDouble(srp.Value))));
        if (tr["DefaultGearIndex"] is JValue dgi) rows.Add(("Default Gear", ((long?)dgi.Value ?? 0).ToString()));
        if (tr["Gears"] is JArray gearArray)
            rows.Add(("Gears", string.Join(", ", gearArray.OfType<JObject>()
                .Select(g => $"{g["Name"]}:{Format.GearRatio(Convert.ToDouble(g["GearRatio"]))}"))));
        if (tr["DevComment"] is JValue dev) rows.Add(("Inspiration", (string?)dev.Value ?? ""));
        if (tr["ShiftTimeSeconds"] is JValue shift) rows.Add(("Shift Time", $"{Format.Num(Convert.ToDouble(shift.Value))} s"));
        if (tr["TorqueConvertorTorqueRate"] is JValue trr) rows.Add(("Torque Converter Torque Rate", Format.Num(Convert.ToDouble(trr.Value))));
        if (tr["ClutchType"] is JValue ct) rows.Add(("Clutch Type", ClutchTypeName(Format.Tail((string?)ct.Value))));
        if (tr["AutoShiftComportRPM"] is JValue asr) rows.Add(("Comfort Autoshift RPM", $"{Format.Num(Convert.ToDouble(asr.Value))} rpm"));
        if (tr["Type"] is JValue trt) rows.Add(("Type (transmission)", Format.Tail((string?)trt.Value)));
    }

    /// <summary>The pak's "TorqueConvertorV2" is a dev misspelling; the wiki renders the
    /// corrected English.</summary>
    private static string ClutchTypeName(string tail) =>
        tail == "TorqueConvertorV2" ? "Torque Converter V2" : Format.Humanize(tail);

    private static void TirePhysicsRows(JObject t, List<(string, string)> rows)
    {
        if (t["PatchLengthCoefficient"] is JValue plc) rows.Add(("Patch Length Coefficient", Format.Num(Convert.ToDouble(plc.Value))));
        if (t["StaticMu"] is JValue sm) rows.Add(("Static Grip", Format.Num(Convert.ToDouble(sm.Value)) + " G"));
        if (t["SlidingMu"] is JValue sl) rows.Add(("Sliding Grip", Format.Num(Convert.ToDouble(sl.Value)) + " G"));
        if (t["SpringX"] is JValue sx) rows.Add(("Spring Rate X", $"{Format.Num(Convert.ToDouble(sx.Value))} N/m"));
        if (t["SpringY"] is JValue sy) rows.Add(("Spring Rate Y", $"{Format.Num(Convert.ToDouble(sy.Value))} N/m"));
        if (t["DampingX"] is JValue dx) rows.Add(("Damping X", $"{Format.Num(Convert.ToDouble(dx.Value))} N·s/m"));
        if (t["DampingY"] is JValue dy) rows.Add(("Damping Y", $"{Format.Num(Convert.ToDouble(dy.Value))} N·s/m"));
        if (t["MaxWeightKg"] is JValue mw) rows.Add(("Max Load", $"{Format.Num(Convert.ToDouble(mw.Value))} kg"));
    }

    private static void LsdRows(JObject lsd, List<(string, string)> rows)
    {
        if (lsd["LSDType"] is JValue lt) rows.Add(("LSD Type", Format.Humanize(Format.Tail((string?)lt.Value))));
        if (lsd["ClutchPackAccel"] is JValue ca) rows.Add(("Clutch Pack Acceleration", Format.Num(Convert.ToDouble(ca.Value))));
        if (lsd["ClutchPackBrake"] is JValue cb) rows.Add(("Clutch Pack Brake", Format.Num(Convert.ToDouble(cb.Value))));
    }

    private static void StructRows(string structName, JObject s, List<(string, string)> rows)
    {
        switch (structName)
        {
            case "AngleKit":
            {
                if (s["AngleIncreaseInDegree"] is JValue v) rows.Add(("Angle Increase", $"{Format.Num(Convert.ToDouble(v.Value))} deg"));
                break;
            }
            case "AntiRollBar":
            {
                if (s["AntiRollBarRateMultiplier"] is JValue v) rows.Add(("Anti-Roll Bar Rate", Format.Pct(Convert.ToDouble(v.Value) - 1)));
                break;
            }
            case "BrakeBalance":
            {
                if (s["FrontMultiplier"] is JValue f) rows.Add(("Front Brake Bias", Format.Pct(Convert.ToDouble(f.Value) - 1)));
                if (s["RearMultiplier"] is JValue r) rows.Add(("Rear Brake Bias", Format.Pct(Convert.ToDouble(r.Value) - 1)));
                break;
            }
            case "BrakePad":
            {
                if (s["HeatingMultiplier"] is JValue h) rows.Add(("Heating", Format.Pct(Convert.ToDouble(h.Value) - 1)));
                if (s["CoolingMultiplier"] is JValue c) rows.Add(("Brake Cooling", Format.Pct(Convert.ToDouble(c.Value) - 1)));
                if (s["FadeTemperature"] is JValue ft) rows.Add(("Fade Temperature", $"{Format.Num(Convert.ToDouble(ft.Value))} °C"));
                if (s["WearMultiplier"] is JValue w) rows.Add(("Wear Rate", Format.Pct(Convert.ToDouble(w.Value) - 1)));
                break;
            }
            case "BrakePower":
            {
                if (s["BrakePowerMultiplier"] is JValue v) rows.Add(("Brake Power", Format.Pct(Convert.ToDouble(v.Value) - 1)));
                break;
            }
            case "SuspensionDamper":
            {
                if (s["BoundDampingRateMultiplier"] is JValue b) rows.Add(("Bound Damping Rate", Format.Pct(Convert.ToDouble(b.Value) - 1)));
                if (s["ReboundDampingRateMultiplier"] is JValue r) rows.Add(("Rebound Damping Rate", Format.Pct(Convert.ToDouble(r.Value) - 1)));
                break;
            }
            case "SuspensionSpring":
            {
                if (s["SpringRateMultiplier"] is JValue v) rows.Add(("Spring Rate", Format.Pct(Convert.ToDouble(v.Value) - 1)));
                break;
            }
            case "SuspensionRideHeight":
            {
                if (s["RideHeightChange"] is JValue v) rows.Add(("Ride Height Change", $"{Format.Num(Convert.ToDouble(v.Value))} cm"));
                break;
            }
            case "CoolantRadiator":
            {
                if (s["CoolingPower"] is JValue c) rows.Add(("Cooling Power", Format.Pct(Convert.ToDouble(c.Value) - 1)));
                if (s["CoolantWaterInLiter"] is JValue w) rows.Add(("Coolant Capacity", $"{Format.Num(Convert.ToDouble(w.Value))} L"));
                break;
            }
            case "Turbocharger":
            {
                if (s["BaseTorqueMultiplier"] is JValue b) rows.Add(("Base Torque", Format.Pct(Convert.ToDouble(b.Value) - 1)));
                if (s["TorqueMultiplier"] is JValue t) rows.Add(("Torque", Format.Pct(Convert.ToDouble(t.Value) - 1)));
                if (s["TurbineAspectRatio"] is JValue a) rows.Add(("Turbine Aspect Ratio", Format.Num(Convert.ToDouble(a.Value))));
                if (s["IntakePressureMultiplier"] is JValue i) rows.Add(("Intake Pressure", Format.Pct(Convert.ToDouble(i.Value) - 1)));
                if (s["HeatingMultiplier"] is JValue h) rows.Add(("Heating", Format.Pct(Convert.ToDouble(h.Value) - 1)));
                if (s["FuelConsumptionMultiplier"] is JValue f) rows.Add(("Fuel Consumption", Format.Pct(Convert.ToDouble(f.Value) - 1)));
                if (s["TurbineWeight"] is JValue w) rows.Add(("Turbine Weight", $"{Format.Num(Convert.ToDouble(w.Value))} kg"));
                break;
            }
            case "Intake":
            {
                if (s["Slope"] is JValue sl) rows.Add(("Intake Torque Slope", Format.Num(Convert.ToDouble(sl.Value))));
                if (s["BaseRPMRatio"] is JValue b) rows.Add(("Base RPM Ratio", Format.Num(Convert.ToDouble(b.Value))));
                if (s["IntakeSpeedEfficencyMultiplier"] is JValue e) rows.Add(("Intake Speed Efficiency", Format.Pct(Convert.ToDouble(e.Value) - 1)));
                break;
            }
            case "WheelSpacer":
            {
                if (s["Space"] is JValue sp) rows.Add(("Width", $"{Format.Num(Convert.ToDouble(sp.Value) * 10)} mm"));
                break;
            }
            case "Winch":
            {
                if (s["MaxForceKg"] is JValue f) rows.Add(("Max Force", $"{Format.Num(Convert.ToDouble(f.Value))} kg"));
                if (s["MaxLength"] is JValue l) rows.Add(("Cable Length", $"{Format.Num(Convert.ToDouble(l.Value) / 100)} m"));
                break;
            }
            case "TrailerHitch":
            {
                if (s["ConnectionType"] is JValue c) rows.Add(("Connection", Format.Tail((string?)c.Value)));
                break;
            }
            case "Taxi":
            {
                if (s["TaxiType"] is JValue t) rows.Add(("Type", Format.Tail((string?)t.Value)));
                break;
            }
            case "CargoBed":
            {
                if (s["CargoSpaceLocation"] is JObject loc) rows.Add(("Cargo Space Location", Format.Vec(
                    (double?)loc["X"] ?? 0, (double?)loc["Y"] ?? 0, (double?)loc["Z"] ?? 0)));
                if (s["CargoSpaceSize"] is JObject size) rows.Add(("Cargo Space Size", Format.Vec(
                    (double?)size["X"] ?? 0, (double?)size["Y"] ?? 0, (double?)size["Z"] ?? 0)));
                if (s["CargoSpaceType"] is JValue st) rows.Add(("Cargo Space Type", Format.Tail((string?)st.Value)));
                if (s["DumpVolume"] is JValue dv) rows.Add(("Dump Volume", $"{Format.Num(Convert.ToDouble(dv.Value))} kL"));
                break;
            }
            case "RoofRack":
            {
                if (s["CargoSpaceLocation"] is JObject loc) rows.Add(("Cargo Space Location", Format.Vec(
                    (double?)loc["X"] ?? 0, (double?)loc["Y"] ?? 0, (double?)loc["Z"] ?? 0)));
                if (s["CargoSpaceSize"] is JObject size) rows.Add(("Cargo Space Size", Format.Vec(
                    (double?)size["X"] ?? 0, (double?)size["Y"] ?? 0, (double?)size["Z"] ?? 0)));
                break;
            }
            case "ItemInventory":
            {
                if (s["NumSlots"] is JValue v) rows.Add(("Slots", Format.Num(Convert.ToDouble(v.Value))));
                break;
            }
            case "FuelTank":
            {
                if (s["FuelLiter"] is JValue v) rows.Add(("Fuel Capacity", $"{Format.Num(Convert.ToDouble(v.Value))} L"));
                break;
            }
        }
    }

    private static void AeroRows(PartInfo part, List<(string, string)> rows)
    {
        var stats = part.Stats;
        var hasLift = new[] { "AeroLift", "FrontAeroLift", "RearAeroLift" }
            .Any(f => stats[f] is JValue v && Convert.ToDouble(v.Value) != 0);
        var liftMult = hasLift ? 1.5 : 1.0;

        foreach (var (field, isDefault) in part.AeroRows())
        {
            var value = Convert.ToDouble(part.Row[field]);
            switch (field)
            {
                case "AirDragMultiplier":
                    rows.Add(("Air Drag", isDefault ? "-" : Format.Pct((value - 1) * liftMult)));
                    break;
                case "TrailerAirDragMultiplier":
                    rows.Add(("Trailer Air Drag", isDefault ? "-" : Format.Pct(value - 1)));
                    break;
                case "FrontDamageMultiplier":
                    rows.Add(("Front Damage", isDefault ? "-" : Format.Pct(value - 1)));
                    break;
                case "AeroLift":
                    rows.Add(("Aero Lift", isDefault ? "-" : Format.AeroLift(value, withKind: true)));
                    break;
                case "FrontAeroLift":
                    rows.Add(("Front Aero Lift", isDefault ? "-" : Format.AeroLift(value, withKind: false)));
                    break;
                case "RearAeroLift":
                    rows.Add(("Rear Aero Lift", isDefault ? "-" : Format.AeroLift(value, withKind: false)));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ list page

    /// <summary>The per-vehicle installable parts page (vehicles:{slug}:installable_parts):
    /// a subset of list_of_parts filtered by the part→vehicle fit rule, with per-type counts
    /// and a "Return to" link.</summary>
    public static string InstallablePartsPage(VehicleInfo v, List<PartInfo> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"====== Installable Parts for {v.En} ======");
        sb.AppendLine();
        var groups = parts.GroupBy(p => p.TypeEnglish)
            .OrderBy(g => g.Key, Format.NaturalComparer.Instance)
            .Select(g => (Type: g.Key, Parts: g.OrderBy(p => p.En, Format.NaturalComparer.Instance).ToList()))
            .ToList();
        var total = groups.Sum(g => g.Parts.Count);
        sb.AppendLine($"All vehicle parts that can be installed on the **{v.En}** ({groups.Count} part type{(groups.Count == 1 ? "" : "s")}, {total} part{(total == 1 ? "" : "s")} in total).");
        sb.AppendLine();
        sb.AppendLine($"Return to [[vehicles:{RenderVehicles.VehicleSlug(v)}|{v.En}]].");
        sb.AppendLine();
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var (type, list) = groups[gi];
            sb.AppendLine($"===== {type} ({list.Count}) =====");
            sb.AppendLine();
            sb.AppendLine("^ Part ^ Cost ^ Mass ^");
            foreach (var part in list)
            {
                var mass = part.MassKg is { } m ? $"{Format.N0(m)} kg" : "—";
                sb.AppendLine($"| [[parts:{part.Slug}|{part.En}]] | {Format.N0(part.Cost)} | {mass} |");
            }
            if (gi < groups.Count - 1) sb.AppendLine();   // no trailing blank after the last section
        }
        return sb.ToString();
    }

    public static string ListOfParts(List<PartInfo> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("====== List of Vehicle Parts ======");
        sb.AppendLine();
        sb.AppendLine($"There are {parts.Count} vehicle parts in [[:motor_town|Motor Town]].");
        sb.AppendLine();
        foreach (var group in parts
                     .GroupBy(p => p.TypeEnglish)
                     .OrderBy(g => g.Key, Format.NaturalComparer.Instance))
        {
            sb.AppendLine($"===== {group.Key} =====");
            sb.AppendLine("^ Part ^ Cost ^ Mass ^");
            foreach (var part in group.OrderBy(p => p.En, Format.NaturalComparer.Instance))
            {
                var mass = part.MassKg is { } m ? $"{Format.N0(m)} kg" : "—";
                sb.AppendLine($"| [[parts:{part.Slug}|{part.En}]] | {Format.N0(part.Cost)} | {mass} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
