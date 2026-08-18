using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using MtExtract;

namespace VehicleData;

/// <summary>
/// Vehicle data gatherer for wiki validation: reads the Vehicles data table, resolves each
/// vehicle's class blueprint, and extracts the stats the wiki shows — chassis weight
/// (sum of BodyInstance.MassInKgOverride over the blueprint's exports), comfort (Comport),
/// seats (MTSeatComponent count), axles (MHWheelComponent pairs + CDO LiftAxles), drag
/// coefficient and fuel tank (class default object), fuel type (default engine part).
///
/// Writes out_vehicle_data.json keyed by vehicle row name. Run from the repo root:
///
///     dotnet run -c Release --project tools/vehicledata
/// </summary>
internal static class Program
{
    private const string VehiclesPath = "MotorTown/Content/DataAsset/Vehicles/Vehicles";
    private const string VehiclePartsPath = "MotorTown/Content/DataAsset/VehicleParts/VehicleParts";
    private const string SlotPrefix = "EMTVehiclePartSlot::";

    private static AssetSource? _assets;

    private static int Main(string[] args)
    {
        Options opts;
        try
        {
            opts = Options.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }

        if (opts.ShowHelp)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        if (!File.Exists(opts.PakPath))
        {
            Console.Error.WriteLine($"error: pak not found: {opts.PakPath}");
            return 2;
        }

        using var assets = new AssetSource(opts);
        _assets = assets;
        Console.WriteLine($"Mounted {Path.GetFileName(opts.PakPath)}: {assets.FileCount} files ({opts.Game})");
        if (assets.FileCount == 0)
        {
            Console.Error.WriteLine("error: nothing mounted - wrong AES key?");
            return 1;
        }

        var localization = assets.LoadLocalization();
        Console.WriteLine($"  {localization.Languages.Count} languages: {string.Join(", ", localization.Languages)}");

        var vehicles = assets.RequirePackage(VehiclesPath).First()["Rows"] as JObject ?? [];
        var parts = assets.RequirePackage(VehiclePartsPath).First()["Rows"] as JObject ?? [];
        var englishIndex = localization.IndexByEnglish("VehicleName", "Vehicle", "Brand", "Common", "");

        var output = new JObject();
        foreach (var (key, row) in vehicles)
        {
            if (row is not JObject obj) continue;
            try
            {
                output[key] = Vehicle(obj, key, parts, localization, englishIndex);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"  {key}: {e.Message}");
            }
        }

        Output.WriteJson(opts.Out("out_vehicle_data.json"), output, "vehicle data");
        return 0;
    }

    private static JObject Vehicle(JObject row, string key, JObject parts,
        Localization localization, Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var vehicle = new JObject
        {
            ["name"] = VehicleName(row, localization, englishIndex),
            ["type"] = (string?)row["VehicleType"] ?? "",
            ["truckClass"] = (string?)row["TruckClass"] ?? "",
            ["cost"] = (long?)row["Cost"] ?? 0,
            ["comfort"] = (double?)row["Comport"] ?? 0,
        };

        var classPath = (string?)row["VehicleClass"]?["AssetPathName"]
                        ?? (string?)row["VehicleClass"]?["ObjectPath"];
        if (classPath is not null && Blueprint(PathToPackage(classPath)) is { } bp)
        {
            // Weight is the sum of BodyInstance.MassInKgOverride across the vehicle's own
            // blueprint exports; some vehicles (Zero, Bongo Bus, ...) genuinely have none.
            vehicle["weightKg"] = bp.WeightKg;
            if (bp.Seats != 0) vehicle["seats"] = bp.Seats;
            if (bp.Axles.Count > 0) vehicle["axles"] = new JArray(bp.Axles);
            if (bp.AirDragCoeff is { } drag) vehicle["dragCoeff"] = drag;
            if (bp.FuelTankCapacityInLiter is { } tank) vehicle["fuelTankL"] = tank;
        }

        var engine = DefaultPart(row, "Engine");
        if (engine is not null && parts[engine] is JObject engineRow)
        {
            var fuelType = EngineFuelType(engineRow);
            if (fuelType is not null) vehicle["fuelType"] = fuelType;
        }

        return vehicle;
    }

    /// <summary>Fuel type from the default engine part's MHEngineDataAsset, defaulting to
    /// Gasoline when the asset carries no FuelType (the reference reader does the same).</summary>
    private static string? EngineFuelType(JObject engineRow)
    {
        var assetPath = (string?)engineRow["EngineAsset"]?["AssetPathName"]
                        ?? (string?)engineRow["EngineAsset"]?["ObjectPath"];
        if (assetPath is null || _assets is null) return "Gasoline";

        var packagePath = PathToPackage(assetPath);
        var props = _assets.Package(packagePath)?.First()["Properties"]?["EngineProperty"] as JObject;
        var fuelType = (string?)props?["FuelType"];
        if (fuelType is null) return "Gasoline";
        var separator = fuelType.IndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? fuelType : fuelType[(separator + 2)..];
    }

    /// <summary>"/Game/Cars/Models/Hana/Hana.Hana_C" -> pak path without extension.</summary>
    private static string PathToPackage(string assetPath)
    {
        var dot = assetPath.LastIndexOf('.');
        var package = dot < 0 ? assetPath : assetPath[..dot];
        return package.StartsWith("/Game", StringComparison.Ordinal)
            ? "MotorTown/Content" + package["/Game".Length..]
            : package;
    }

    private static readonly Dictionary<string, BlueprintStats> _blueprints = new(StringComparer.OrdinalIgnoreCase);

    private static BlueprintStats? Blueprint(string packagePath)
    {
        if (_blueprints.TryGetValue(packagePath, out var cached)) return cached;
        var stats = BuildBlueprintStats(packagePath);
        _blueprints[packagePath] = stats;
        return stats;
    }

    private static BlueprintStats? BuildBlueprintStats(string packagePath)
    {
        if (_assets is null) return null;
        var package = _assets.Package(packagePath);
        if (package is null) return null;

        double weight = 0;
        int seats = 0;
        double? drag = null, tank = null;
        var axles = new List<JObject>();
        JObject? cdo = null;

        // Walk every export: weight = sum of BodyInstance.MassInKgOverride; seats count
        // MTSeatComponent; wheels define axle pairs; the BlueprintGeneratedClass export points
        // at the class default object that carries AirDragCoeff / FuelTankCapacityInLiter /
        // LiftAxles.
        for (var i = 0; i < package.Exports.Count; i++)
        {
            JObject export;
            try { export = package.Json(i); }
            catch { continue; }

            var props = export["Properties"] as JObject;
            if (props is null) continue;

            if (props["BodyInstance"]?["MassInKgOverride"] is JValue mass && Convert.ToDouble(mass.Value) != 0)
                weight += Convert.ToDouble(mass.Value);

            var type = (string?)export["Type"] ?? "";
            if (type == "MTSeatComponent") seats++;

            if (type == "MHWheelComponent")
            {
                var name = (string?)export["Name"] ?? "";
                var m = Regex.Match(name, @"\d+");
                var index = m.Success ? int.Parse(m.Value) : 0;
                var axleIndex = index / 2;
                while (axles.Count <= axleIndex) axles.Add(NewAxle());
                var axle = axles[axleIndex];
                var flags = props["WheelFlags"] as JArray ?? [];
                if (flags.ToString(Newtonsoft.Json.Formatting.None).Contains("DualRearWheel")) axle["dual"] = true;
                if (props["DifferentialComponentName"] is not null) axle["driven"] = true;
                axle["brakeRatio"] = (double?)axle["brakeRatio"] + (double?)props["BrakeRatio"] ?? 0;
            }

            if (type == "BlueprintGeneratedClass")
            {
                var cdoPath = (string?)export["ClassDefaultObject"]?["ObjectPath"];
                if (cdoPath is not null && int.TryParse(cdoPath[(cdoPath.LastIndexOf('.') + 1)..], out var cdoIndex)
                    && cdoIndex < package.Exports.Count)
                {
                    try { cdo = package.Json(cdoIndex)["Properties"] as JObject; }
                    catch { }
                }
            }
        }

        if (cdo is not null)
        {
            if (cdo["AirDragCoeff"] is JValue d) drag = Convert.ToDouble(d.Value);
            if (cdo["FuelTankCapacityInLiter"] is JValue t) tank = Convert.ToDouble(t.Value);
            if (cdo["LiftAxles"] is JArray lifts)
            {
                foreach (var lift in lifts.OfType<JObject>())
                {
                    foreach (var wheel in (lift["WheelIndexToHeight"] as JArray ?? []).OfType<JObject>())
                    {
                        if ((double?)wheel["Value"] > 0 && (int?)wheel["Key"] is { } wi)
                        {
                            var axleIndex = wi / 2;
                            if (axleIndex >= 0 && axleIndex < axles.Count) axles[axleIndex]["lift"] = true;
                        }
                    }
                }
            }
        }

        return new BlueprintStats(weight, seats, drag, tank, axles);
    }

    private static JObject NewAxle() => new()
    {
        ["brakeRatio"] = 0.0,
        ["driven"] = false,
        ["dual"] = false,
        ["lift"] = false,
    };

    private static string? DefaultPart(JObject row, string slot)
    {
        if (row["Parts"] is not JArray parts) return null;
        foreach (var entry in parts.OfType<JObject>())
        {
            var key = (string?)entry["Key"] ?? "";
            if (key.StartsWith(SlotPrefix, StringComparison.Ordinal)) key = key[SlotPrefix.Length..];
            if (key == slot) return (string?)entry["Value"];
        }
        return null;
    }

    private static JToken VehicleName(JObject row, Localization localization,
        Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var texts = (row["VehicleName2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["VehicleName"] as JObject;

        var names = new JObject();
        foreach (var language in localization.Languages)
        {
            var localized = texts.Count > 0
                ? string.Join(" ", texts
                    .Select(text => Text.LocalizeVehicleText(text, language, localization, englishIndex))
                    .Where(part => !string.IsNullOrEmpty(part)))
                : name is null ? null : Text.LocalizeVehicleText(name, language, localization, englishIndex);

            if (!string.IsNullOrEmpty(localized)) names[language] = localized;
        }

        if (string.IsNullOrEmpty((string?)names["en"]))
            names["en"] = name is null ? "" : Text.Source(name) ?? "";
        return Output.Dedupe(names);
    }

    private sealed record BlueprintStats(double WeightKg, int Seats, double? AirDragCoeff,
        double? FuelTankCapacityInLiter, List<JObject> Axles);
}
