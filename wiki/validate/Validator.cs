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

        // ---- part detail pages: infobox + Specifications + Stats for every part ----
        var lcParts = parts.Properties().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Name);
        foreach (var (slug, _) in PartEntries(partList))
        {
            var raw = Fetch($"parts:{slug}?do=export_raw", $"parts_{slug}.txt");
            ValidatePartPage(slug, raw, parts, lcParts);
        }

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

    // ------------------------------------------------------------------ part detail pages

    private static List<(string Slug, string Name)> PartEntries(string raw) =>
        Regex.Matches(raw, @"\[\[parts:([^|]+)\|([^\]]+)\]\]")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    /// <summary>
    /// Validates one part detail page: infobox (name, type, cost, mass), Specifications and the
    /// Stats blocks. Stat values are compared against the pak using the review2 display rules:
    /// multipliers render as ±% from 100, probabilities as %, grip as G, and unit labels are
    /// applied (deg, N/m, N·s/m, kg, °C, ...). A missing Stats block on a part that has stats is
    /// also flagged.
    /// </summary>
    private void ValidatePartPage(string slug, string raw, JObject parts, Dictionary<string, string> lc)
    {
        var key = ResolvePart(slug, lc, parts);
        if (key is null) { Claim("parts:" + slug, slug, "part", slug, "not found in pak"); return; }
        var part = (JObject)parts[key]!;
        var en = (string?)(part["name"] as JObject)?["en"] ?? "";

        // infobox
        var infobox = Regex.Match(raw, @"\{\{infobox>(.*?)\}\}", RegexOptions.Singleline);
        if (infobox.Success)
        {
            var fields = new Dictionary<string, string>();
            foreach (Match fm in Regex.Matches(infobox.Groups[1].Value, @"^(\S[^=]*?) = (.*)$", RegexOptions.Multiline))
                fields[fm.Groups[1].Value.Trim()] = fm.Groups[2].Value.Trim();

            if (fields.TryGetValue("name", out var wikiName) && wikiName != en)
                Claim($"parts:{slug} infobox", slug, "name", wikiName, en);
            if (fields.TryGetValue("Cost", out var wikiCost)
                && long.TryParse(wikiCost.Replace(",", "").Replace("$", ""), out var wc)
                && wc != (long?)part["cost"])
                Claim($"parts:{slug} infobox", slug, "cost", wikiCost, part["cost"]!.ToString());
            if (fields.TryGetValue("Mass", out var wikiMass) && TryKg(wikiMass, out var wm)
                && Math.Abs(wm - (double?)part["massKg"] ?? 0) > 1e-9)
                Claim($"parts:{slug} infobox", slug, "mass", wikiMass, (part["massKg"]?.ToString() ?? "0"));
        }

        // Specifications
        var specs = Section(raw, "Specifications");
        foreach (Match row in Regex.Matches(specs, @"^\| ([^|]+) \| (.+?) \|$", RegexOptions.Multiline))
        {
            var stat = row.Groups[1].Value.Trim();
            var value = row.Groups[2].Value.Trim();
            switch (stat)
            {
                case "Cost" when long.TryParse(value.Replace(",", ""), out var wc) && wc != (long?)part["cost"]:
                    Claim($"parts:{slug} Specifications", slug, "Cost", value, part["cost"]!.ToString());
                    break;
                case "Mass" when TryKg(value, out var wm) && Math.Abs(wm - (double?)part["massKg"] ?? 0) > 1e-9:
                    Claim($"parts:{slug} Specifications", slug, "Mass", value, (part["massKg"]?.ToString() ?? "0"));
                    break;
            }
        }

        // Stats: build the expected rows from the pak and compare values.
        var expectedStats = ExpectedStats(part);
        var statsSection = Section(raw, "Stats");
        foreach (var (label, expected) in expectedStats)
        {
            var m = Regex.Match(statsSection, @"^\| " + Regex.Escape(label) + @" \| (.+?) \|$", RegexOptions.Multiline);
            if (!m.Success)
                Claim($"parts:{slug} Stats", slug, label, "(missing row)", expected);
            else if (m.Groups[1].Value.Trim() != "-" && NormalizeNumber(m.Groups[1].Value.Trim()) != NormalizeNumber(expected))
                Claim($"parts:{slug} Stats", slug, label, m.Groups[1].Value.Trim(), expected);
        }
        // Stats block present on the wiki but nothing in the pak -> data the wiki invented.
        // A "-" value means "no data" (the wiki renders the full schema), so it is not a claim.
        var wikiLabels = Regex.Matches(statsSection, @"^\| ([^|]+) \| (.+?) \|$", RegexOptions.Multiline)
            .Where(m => m.Groups[2].Value.Trim() != "-")
            .Select(m => m.Groups[1].Value.Trim()).ToList();
        foreach (var label in wikiLabels)
        {
            if (!expectedStats.ContainsKey(label) && label is not ("Type" or "Cost" or "Mass"))
                Claim($"parts:{slug} Stats", slug, label, "(wiki only)", "(no such stat in pak)");
        }
    }

    /// <summary>Strip separators/units so "1,500 kg" == "1500" == "1500kg" for comparison.</summary>
    private static string NormalizeNumber(string s) =>
        Regex.Replace(s, @"[\s,°%N·m²/×]", "").ToLowerInvariant();

    /// <summary>
    /// Maps the part's pak stats to the wiki's displayed rows: label -> formatted value.
    /// Covers every stat the wiki renders, applying the review2 display rules.
    /// </summary>
    private static Dictionary<string, string> ExpectedStats(JObject part)
    {
        var result = new Dictionary<string, string>();
        var stats = part["stats"] as JObject;
        if (stats is null) return result;

        // engine physics (asset-resolved); zero-valued rows are omitted like the wiki does
        if (stats["engine"] is JObject e)
        {
            if (e["MaxRPM"] is JValue maxRpm) result["Max RPM"] = $"{Num(Convert.ToDouble(maxRpm.Value))} rpm";
            if (e["MaxTorque"] is JValue maxTq && Convert.ToDouble(maxTq.Value) != 0) result["Max Torque"] = $"{Num(Convert.ToDouble(maxTq.Value))} N·m";
            if (e["StarterTorque"] is JValue st && Convert.ToDouble(st.Value) != 0) result["Starter Torque"] = $"{Num(Convert.ToDouble(st.Value))} N·m";
            if (e["Inertia"] is JValue inertia) result["Rotational Inertia"] = $"{Num(Convert.ToDouble(inertia.Value))} kg·m²";
            if (e["FrictionViscosityCoeff"] is JValue fv) result["Friction Viscosity"] = Num(Convert.ToDouble(fv.Value));
            if (e["IdleThrottle"] is JValue idle && Convert.ToDouble(idle.Value) != 0) result["Idle Throttle"] = $"{Convert.ToDouble(idle.Value) * 100:0.##}%";
            if (e["FuelConsumption"] is JValue fc) result["Fuel Consumption"] = Num(Convert.ToDouble(fc.Value));
            if (e["BlipThrottle"] is JValue blip && Convert.ToDouble(blip.Value) != 0) result["Blip Throttle"] = Num(Convert.ToDouble(blip.Value));
            if (e["AfterFireProbability"] is JValue af) result["After-Fire Probability"] = $"{Convert.ToDouble(af.Value) * 100:0.##}%";
            if (e["CoolingEfficiency"] is JValue ce) result["Cooling Efficiency"] = Pct(Convert.ToDouble(ce.Value) - 1);
            if (e["HeatingPower"] is JValue hp) result["Heating Power"] = Pct(Convert.ToDouble(hp.Value) - 1);
            if (e["StarterRPM"] is JValue srpm && Convert.ToDouble(srpm.Value) != 0) result["Starter RPM"] = $"{Num(Convert.ToDouble(srpm.Value))} rpm";
            if (e["FrictionCoulombCoeff"] is JValue cc) result["Friction Coulomb Coefficient"] = Num(Convert.ToDouble(cc.Value));
            if (e["BlipDurationSeconds"] is JValue bd) result["Blip Duration"] = $"{Num(Convert.ToDouble(bd.Value))} s";
            if (e["IntakeSpeedEfficency"] is JValue ise) result["Intake Speed Efficency"] = Num(Convert.ToDouble(ise.Value));
            if (e["FuelType"] is JValue ft) result["Fuel Type"] = Tail((string?)ft.Value);
            if (e["EngineType"] is JValue et) result["Engine Type"] = Tail((string?)et.Value);
            if (e["MaxJakeBrakeStep"] is JValue jb) result["Max Jake Brake Step"] = Num(Convert.ToDouble(jb.Value));
            if (e["MaxRegenTorqueRatio"] is JValue mr) result["Max Regen Torque Ratio"] = $"{Convert.ToDouble(mr.Value) * 100:0}%";
            if (e["MotorMaxPower"] is JValue mp) result["Motor Max Power"] = $"{Num(Convert.ToDouble(mp.Value))} W";
            if (e["MotorMaxVoltage"] is JValue mv) result["Motor Max Voltage"] = $"{Num(Convert.ToDouble(mv.Value))} V";
            if (e["TorqueCurve"] is JArray curve)
            {
                result["Torque Curve"] = string.Join(", ", curve.OfType<JObject>()
                    .Select(k => $"{k["Value"]:0.##} @ {k["Time"]:0.##}"));
            }
        }

        // struct stats
        void Struct(string section, Dictionary<string, (string Label, Func<double, string> Fmt)> fields)
        {
            if (stats[section] is not JObject obj) return;
            foreach (var (field, (label, fmt)) in fields)
            {
                if (obj[field] is JValue v) result[label] = fmt(Convert.ToDouble(v.Value));
            }
        }

        Struct("AngleKit", new() { ["AngleIncreaseInDegree"] = ("Angle Increase", x => $"{Num(x)} deg") });
        Struct("AntiRollBar", new() { ["AntiRollBarRateMultiplier"] = ("Anti-Roll Bar Rate", x => Pct(x - 1)) });
        Struct("BrakeBalance", new()
        {
            ["FrontMultiplier"] = ("Front Brake Bias", x => Pct(x - 1)),
            ["RearMultiplier"] = ("Rear Brake Bias", x => Pct(x - 1)),
        });
        Struct("BrakePad", new()
        {
            ["HeatingMultiplier"] = ("Heating", x => Pct(x - 1)),
            ["CoolingMultiplier"] = ("Brake Cooling", x => Pct(x - 1)),
            ["WearMultiplier"] = ("Wear Rate", x => Pct(x - 1)),
            ["FadeTemperature"] = ("Fade Temperature", x => $"{Num(x)} °C"),
        });
        Struct("BrakePower", new() { ["BrakePowerMultiplier"] = ("Brake Power", x => Pct(x - 1)) });
        Struct("SuspensionDamper", new()
        {
            ["BoundDampingRateMultiplier"] = ("Bound Damping Rate", x => Pct(x - 1)),
            ["ReboundDampingRateMultiplier"] = ("Rebound Damping Rate", x => Pct(x - 1)),
        });
        Struct("SuspensionSpring", new() { ["SpringRateMultiplier"] = ("Spring Rate", x => Pct(x - 1)) });
        Struct("SuspensionRideHeight", new() { ["RideHeightChange"] = ("Ride Height Change", x => $"{Num(x)} cm") });
        Struct("CoolantRadiator", new()
        {
            ["CoolingPower"] = ("Cooling Power", x => Pct(x - 1)),
            ["CoolantWaterInLiter"] = ("Coolant Capacity", x => $"{Num(x)} L"),
        });
        Struct("Turbocharger", new()
        {
            ["BaseTorqueMultiplier"] = ("Base Torque", x => Pct(x - 1)),
            ["TorqueMultiplier"] = ("Torque", x => Pct(x - 1)),
            ["IntakePressureMultiplier"] = ("Intake Pressure", x => Pct(x - 1)),
            ["HeatingMultiplier"] = ("Heating", x => Pct(x - 1)),
            ["FuelConsumptionMultiplier"] = ("Fuel Consumption", x => Pct(x - 1)),
            ["TurbineWeight"] = ("Turbine Weight", x => $"{Num(x)} kg"),
            ["TurbineAspectRatio"] = ("Turbine Aspect Ratio", Num),
        });
        Struct("Intake", new()
        {
            ["Slope"] = ("Intake Torque Slope", Num),
            ["BaseRPMRatio"] = ("Base RPM Ratio", Num),
            ["IntakeSpeedEfficencyMultiplier"] = ("Intake Speed Efficiency", x => Pct(x - 1)),
        });
        Struct("WheelSpacer", new() { ["Space"] = ("Width", x => $"{Num(x * 10)} mm") });
        if (stats["WheelSpacer"] is JObject ws && ws["Space"] is JValue spaceVal)
            result["Width"] = $"{Num(Convert.ToDouble(spaceVal.Value) * 10)} mm";

        // tire physics (asset-resolved); the wiki renders a fixed field set and omits
        // rolling resistance / wear / offroad / smoke rows entirely
        if (stats["tire"] is JObject t)
        {
            if (t["PatchLengthCoefficient"] is JValue plc) result["Patch Length Coefficient"] = Num(Convert.ToDouble(plc.Value));
            if (t["StaticMu"] is JValue sm) result["Static Grip"] = Num(Convert.ToDouble(sm.Value)) + " G";
            if (t["SlidingMu"] is JValue sl) result["Sliding Grip"] = Num(Convert.ToDouble(sl.Value)) + " G";
            if (t["SpringX"] is JValue sx) result["Spring Rate X"] = $"{Num(Convert.ToDouble(sx.Value))} N/m";
            if (t["SpringY"] is JValue sy) result["Spring Rate Y"] = $"{Num(Convert.ToDouble(sy.Value))} N/m";
            if (t["DampingX"] is JValue dx) result["Damping X"] = $"{Num(Convert.ToDouble(dx.Value))} N·s/m";
            if (t["DampingY"] is JValue dy) result["Damping Y"] = $"{Num(Convert.ToDouble(dy.Value))} N·s/m";
            if (t["MaxWeightKg"] is JValue mw) result["Max Load"] = $"{Num(Convert.ToDouble(mw.Value))} kg";
        }
        if (stats["Tire"] is JObject tireStruct && tireStruct["bIsDualRearWheel"] is JValue dual)
            result["Dual Rear"] = (bool?)dual.Value == true ? "Yes" : "No";

        // aero
        if (new[] { "AirDragMultiplier", "FrontDamageMultiplier", "AeroLift", "FrontAeroLift", "RearAeroLift", "TrailerAirDragMultiplier" }
                .Any(f => stats[f] is JValue) || HasLift(stats))
        {
            var mult = (double?)stats["AirDragMultiplier"] ?? 1;
            var liftMult = HasLift(stats) ? 1.5 : 1.0;
            if (mult != 1 || liftMult != 1)
                result["Air Drag"] = Pct((mult - 1) * liftMult);
            if (stats["TrailerAirDragMultiplier"] is JValue td && Convert.ToDouble(td.Value) != 1)
                result["Trailer Air Drag"] = Pct(Convert.ToDouble(td.Value) - 1);
            if (stats["FrontDamageMultiplier"] is JValue fdm && Convert.ToDouble(fdm.Value) != 1)
                result["Front Damage"] = Pct(Convert.ToDouble(fdm.Value) - 1);
            foreach (var (field, label) in new[] { ("AeroLift", "Aero Lift"), ("FrontAeroLift", "Front Aero Lift"), ("RearAeroLift", "Rear Aero Lift") })
            {
                if (stats[field] is JValue lift && Convert.ToDouble(lift.Value) != 0)
                    result[label] = field == "AeroLift"
                        ? AeroLift(Convert.ToDouble(lift.Value), withKind: true)
                        : AeroLift(Convert.ToDouble(lift.Value), withKind: false);
            }
        }

        // final drive ratio (scalar stat)
        if (stats["FinalDriveRatio"] is JValue fdr && Convert.ToDouble(fdr.Value) != -1)
            result["Final Drive Ratio"] = Num(Convert.ToDouble(fdr.Value));

        // transmission (asset-resolved)
        if (stats["transmission"] is JObject tr)
        {
            if (tr["ShiftTimeSeconds"] is JValue shift) result["Shift Time"] = $"{Num(Convert.ToDouble(shift.Value))} s";
            if (tr["TorqueConvertorStallRPM"] is JValue stall) result["Torque Converter Stall RPM"] = $"{Num(Convert.ToDouble(stall.Value))} rpm";
            if (tr["TorqueConvertorStallRatioPower"] is JValue srp) result["Torque Converter Stall Ratio Power"] = Num(Convert.ToDouble(srp.Value));
            if (tr["TorqueConvertorTorqueRate"] is JValue trr) result["Torque Converter Torque Rate"] = Num(Convert.ToDouble(trr.Value));
            if (tr["AutoShiftComportRPM"] is JValue asr) result["Comfort Autoshift RPM"] = $"{Num(Convert.ToDouble(asr.Value))} rpm";
            if (tr["ClutchType"] is JValue ct) result["Clutch Type"] = Tail((string?)ct.Value);
            if (tr["Type"] is JValue trt) result["Type (transmission)"] = Tail((string?)trt.Value);
            if (tr["DevComment"] is JValue dev) result["Inspiration"] = (string?)dev.Value ?? "";
            if (tr["DefaultGearIndex"] is JValue dgi)
                result["Default Gear"] = ((long?)dgi.Value ?? 0).ToString();
            if (tr["Gears"] is JArray gearArray)
            {
                result["Gears"] = string.Join(", ", gearArray.OfType<JObject>()
                    .Select(g => $"{g["Name"]}:{GearRatio(Convert.ToDouble(g["GearRatio"]))}"));
            }
        }

        // LSD (asset-resolved)
        if (stats["lsd"] is JObject lsd)
        {
            if (lsd["LSDType"] is JValue lt) result["LSD Type"] = Humanize(Tail((string?)lt.Value));
            if (lsd["ClutchPackAccel"] is JValue ca) result["Clutch Pack Acceleration"] = Num(Convert.ToDouble(ca.Value));
            if (lsd["ClutchPackBrake"] is JValue cb) result["Clutch Pack Brake"] = Num(Convert.ToDouble(cb.Value));
        }

        // winch / trailer hitch / taxi / cargo bed / inventory / fuel tank
        Struct("Winch", new()
        {
            ["MaxForceKg"] = ("Max Force", x => $"{Num(x)} kg"),
            ["MaxLength"] = ("Cable Length", x => $"{Num(x / 100)} m"),
        });
        if (stats["TrailerHitch"] is JObject hitch && hitch["ConnectionType"] is JValue conn)
            result["Connection"] = Tail((string?)conn.Value);
        if (stats["Taxi"] is JObject taxi && taxi["TaxiType"] is JValue taxiType)
            result["Type"] = Tail((string?)taxiType.Value);
        if (stats["CargoBed"] is JObject cargo
            && !part["type"]?.ToString().Contains("CargoBedAttachment", StringComparison.Ordinal) == true)
        {
            if (cargo["CargoSpaceType"] is JValue cst) result["Cargo Space Type"] = Tail((string?)cst.Value);
            if (cargo["DumpVolume"] is JValue dv) result["Dump Volume"] = $"{Num(Convert.ToDouble(dv.Value))} kL";
            if (cargo["CargoSpaceLocation"] is JObject loc)
                result["Cargo Space Location"] = Vec(loc);
            if (cargo["CargoSpaceSize"] is JObject size)
                result["Cargo Space Size"] = Vec(size);
        }
        if (stats["RoofRack"] is JObject rack)
        {
            if (rack["CargoSpaceLocation"] is JObject rl) result["Cargo Space Location"] = Vec(rl);
            if (rack["CargoSpaceSize"] is JObject rs) result["Cargo Space Size"] = Vec(rs);
        }
        if (stats["ItemInventory"] is JObject inv && inv["NumSlots"] is JValue slots)
            result["Slots"] = Num(Convert.ToDouble(slots.Value));
        if (stats["FuelTank"] is JObject tank && tank["FuelLiter"] is JValue fuel)
            result["Fuel Capacity"] = $"{Num(Convert.ToDouble(fuel.Value))} L";

        return result;
    }

    /// <summary>A 3D vector struct -> "X cm × Y cm × Z cm" with each axis labeled per review2.</summary>
    private static string Vec(JObject v)
    {
        double X = (double?)v["X"] ?? 0, Y = (double?)v["Y"] ?? 0, Z = (double?)v["Z"] ?? 0;
        return $"{Num(Eps(X))} cm × {Num(Eps(Y))} cm × {Num(Eps(Z))} cm";
    }

    /// <summary>Axis values like -0.000122 are editor noise; the wiki renders them as 0.</summary>
    private static double Eps(double x) => Math.Abs(x) < 0.01 ? 0 : x;

    private static bool HasLift(JObject stats) =>
        new[] { "AeroLift", "FrontAeroLift", "RearAeroLift" }.Any(f => stats[f] is JValue v && Convert.ToDouble(v.Value) != 0);

    /// <summary>Downforce coefficient -> "coef (X kg @ 200 km/h)" using force = 7.098e-7 * v² * coef.
    /// The wiki labels the whole-vehicle Aero Lift with the kind (downforce/lift) but not the
    /// per-axle Front/Rear lifts.</summary>
    private static string AeroLift(double coef, bool withKind)
    {
        var force = 7.098e-7 * 40000 * coef;
        var kind = coef < 0 ? " downforce" : " lift";
        return withKind
            ? $"{coef:0} ({Math.Abs(force):0.0} kg{kind} @ 200 km/h)"
            : $"{coef:0} ({Math.Abs(force):0.0} kg @ 200 km/h)";
    }

    private static string Tail(string? value)
    {
        if (value is null) return "";
        var idx = value.LastIndexOf("::", StringComparison.Ordinal);
        return idx < 0 ? value : value[(idx + 2)..];
    }

    /// <summary>"ClutchPackLSD" -> "Clutch Pack LSD": split camelCase for display.</summary>
    private static string Humanize(string value) =>
        Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");

    /// <summary>Multiplier delta as ±% from 100: 1.15 -> "+15%", 0.98 -> "-2%", 1.0 -> "±0%".</summary>
    private static string Pct(double x) => x switch
    {
        0 => "±0%",
        > 0 => $"+{x * 100:0.##}%",
        _ => $"{x * 100:0.##}%",
    };

    /// <summary>Whole numbers without a trailing .0, decimals to 2 places.</summary>
    private static string Num(double x) => x == Math.Floor(x) ? ((long)x).ToString() : x.ToString("0.##");

    /// <summary>Gear ratios: the wiki renders ToString("F2") with trailing zeros stripped —
    /// 1.785 -> "1.78", 1.315 -> "1.31", 2.105 -> "2.1". (Math.Round/0.## differ on exact
    /// halves because the custom format rounds the binary value; F2 rounds the decimal
    /// representation.)</summary>
    private static string GearRatio(double x)
    {
        var f2 = x.ToString("F2");
        return f2.TrimEnd('0').TrimEnd('.');
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
        Console.WriteLine($"  {Path.Combine(_outDir, "validation.json")} {_claims.Count} claims");
        Console.WriteLine("  review.md is hand-written; run `--validate` then author findings from validation.json");
    }
}
