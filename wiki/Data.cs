using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using MtExtract;

namespace WikiGenerator;

internal sealed class AxleInfo(double brakeRatio, bool driven, bool dual, bool lift)
{
    public double BrakeRatio { get; } = brakeRatio;
    public bool Driven { get; } = driven;
    public bool Dual { get; } = dual;
    public bool Lift { get; } = lift;
}

internal sealed class CargoSpaceInfo
{
    public required string Type { get; init; }
    public double? LengthM { get; init; }
    public double? WidthM { get; init; }
    public double? HeightM { get; init; }
    public double? VolumeM3 { get; init; }
    public double? DumpKl { get; init; }
    public bool FixCargo { get; init; }
    public bool UnlimitedHeight { get; init; }
}

internal sealed class VehicleInfo
{
    public required string Key { get; init; }
    public required Dictionary<string, string> Names { get; init; }
    public string En => Names.GetValueOrDefault("en") ?? Key;
    public required string Type { get; init; }
    public required string TruckClass { get; init; }
    public long Cost { get; init; }
    public double Comfort { get; init; }
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public double? WeightKg { get; set; }
    public int? Seats { get; set; }
    public double? FuelTankL { get; set; }
    public string? FuelType { get; set; }
    public double? DragCoeff { get; set; }
    public List<AxleInfo> Axles { get; } = [];
    public List<(string Slot, string Part)> DefaultParts { get; } = [];
    public List<(string Name, long Value)> Levels { get; } = [];
    public CargoSpaceInfo? CargoSpace { get; set; }

    /// <summary>Cargo spaces the vehicle can acquire only by installing a CargoBed part
    /// (it ships with no cargo space) — rendered as "(installable)". One entry per distinct
    /// space type, from the first fitting part.</summary>
    public List<CargoSpaceInfo> InstallableSpaces { get; } = [];
    public long? BasePayment { get; set; }
    public double? PaymentMultiplier { get; set; }
    public List<string> Tags { get; } = [];

    /// <summary>Part types this vehicle cannot take at all (raw `EMTVehiclePartType::` values,
    /// e.g. Formula SCM: [LSD, WheelSpacer]) — the vehicle-side fit exclusion.</summary>
    public List<string> NotSupportedPartTypes { get; } = [];

    /// <summary>Broken/unused assets with no usable drivetrain; the wiki's "Rear-wheel drive"
    /// display for them is the established convention.</summary>
    public static readonly HashSet<string> BrokenAssets = new(StringComparer.Ordinal)
    {
        "Bongo_Bus", "Nimo_Taxi", "Nuke_Taxi", "Townie_Bus", "Elisa2_Police",
    };

    public bool Broken => BrokenAssets.Contains(Key);

    public string Drivetrain(bool spelledOut) => Broken
        ? "Rear-wheel drive"
        : PakDrive() switch
        {
            "FWD" => spelledOut ? "Front-wheel drive" : "FWD",
            "RWD" => spelledOut ? "Rear-wheel drive" : "RWD",
            "AWD" => spelledOut ? "All-wheel drive" : "AWD",
            _ => "",
        };

    private string PakDrive()
    {
        var driven = Axles.Select((a, i) => a.Driven ? i : -1).Where(i => i >= 0).ToList();
        return driven.Count switch
        {
            0 => "",
            1 => driven[0] == 0 ? "FWD" : "RWD",
            _ => "AWD",
        };
    }
}

internal sealed class PartInfo
{
    public required string Key { get; init; }
    public required Dictionary<string, string> Names { get; init; }
    public string En => Names.GetValueOrDefault("en") ?? Key;
    public required string Type { get; init; }
    public required string TypeEnglish { get; init; }
    public required string StatsHeading { get; init; }
    public long Cost { get; init; }
    public double? MassKg { get; init; }
    public bool Hidden { get; init; }
    public required string Slug { get; init; }
    public bool HasPage { get; init; }
    public required JObject Row { get; init; }
    public required JObject Stats { get; init; }
    public bool Electric { get; init; }
    public double? FdrValue { get; init; }

    /// <summary>Aero parts (the wiki renders a fixed per-type aero schema): detected by the
    /// part's type, not by field presence — every table row carries all aero scalars.</summary>
    public static readonly HashSet<string> AeroTypes = new(StringComparer.Ordinal)
    {
        "FrontBumper", "RearBumper", "SideSkirt", "RearSpoiler", "RearWing",
        "Roof", "Fender", "FrontSpoiler", "Bullbar",
    };

    public bool HasAero => AeroTypes.Contains(Regex.Replace(Type, @"^EMTVehiclePartType::", ""));

    /// <summary>The aero row schema per part type, in display order.</summary>
    public static readonly Dictionary<string, string[]> AeroSchemas = new(StringComparer.Ordinal)
    {
        ["FrontBumper"] = ["AirDragMultiplier", "FrontDamageMultiplier", "AeroLift", "FrontAeroLift", "RearAeroLift"],
        ["RearBumper"] = ["AirDragMultiplier", "AeroLift", "RearAeroLift"],
        ["SideSkirt"] = ["AirDragMultiplier", "AeroLift"],
        ["RearSpoiler"] = ["AirDragMultiplier", "TrailerAirDragMultiplier", "RearAeroLift"],
        ["RearWing"] = ["AirDragMultiplier", "RearAeroLift"],
        ["Roof"] = ["AirDragMultiplier", "TrailerAirDragMultiplier"],
        ["Fender"] = ["AirDragMultiplier", "AeroLift", "FrontAeroLift"],
        ["FrontSpoiler"] = ["AirDragMultiplier", "AeroLift", "FrontAeroLift"],
        ["Bullbar"] = ["FrontDamageMultiplier"],
    };

    public List<(string Field, bool Default)> AeroRows()
    {
        var tail = Regex.Replace(Type, @"^EMTVehiclePartType::", "");
        var result = new List<(string, bool)>();
        if (!AeroSchemas.TryGetValue(tail, out var fields)) return result;
        foreach (var f in fields)
        {
            if (Row[f] is not JValue v) continue;
            var x = Format.JvDouble(v);
            result.Add((f, x == (f is "AeroLift" or "FrontAeroLift" or "RearAeroLift" ? 0 : 1)));
        }
        return result;
    }
}

internal sealed class CargoInfo
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public double Volume { get; init; }
    public double? WeightKg { get; set; }
    public double WeightMin { get; init; }
    public double WeightMax { get; init; }
    public List<string> SpaceTypes { get; } = [];
    public bool Stackable { get; init; }
    public double Fragile { get; init; }
    public long PaymentPerKm { get; init; }
    public double PaymentMultiplier { get; init; }
    public long BasePayment { get; init; }
    public double MinDist { get; init; }
    public double MaxDist { get; init; }
    public bool Deprecated { get; init; }
    public List<string> Tags { get; } = [];

    /// <summary>Weight display rule: a cargo with a WeightRange shows the range (single value
    /// when X=Y, "X–Y kg" when variable); cargos without a range show the actor mesh mass,
    /// defaulting to "0 kg" when the actor carries none.</summary>
    public string WeightText()
    {
        if (WeightMin != 0 || WeightMax != 0)
            return WeightMin == WeightMax ? $"{Format.N0(WeightMin)} kg" : $"{Format.N0(WeightMin)}–{Format.N0(WeightMax)} kg";
        return $"{Format.N0(WeightKg ?? 0)} kg";
    }
}

internal sealed class DeliveryPointInfo
{
    public required string Key { get; init; }
    public List<ProductionConfig> Configs { get; } = [];
    public List<CargoRef> Demands { get; } = [];
    public List<CargoRef> PassiveSupplies { get; } = [];
}

internal sealed class ProductionConfig
{
    public List<CargoRef> Inputs { get; } = [];
    public List<CargoRef> InputTypes { get; } = [];
    public List<string> InputTags { get; } = [];
    public List<CargoRef> Outputs { get; } = [];
    public List<CargoRef> OutputTypes { get; } = [];
    public List<string> OutputTags { get; } = [];
    public double TimeSeconds { get; set; }
}

internal sealed class CargoRef
{
    public string? Key { get; init; }
    public string? Type { get; init; }
    public List<string> Tags { get; } = [];
    public double Count { get; init; } = 1;
}

internal sealed class SpaceInfo
{
    public required string Type { get; init; }
    public List<CargoInfo> Cargos { get; } = [];
    public List<(VehicleInfo Vehicle, bool Installable)> Vehicles { get; } = [];
    public List<PartInfo> Parts { get; } = [];
}

/// <summary>
/// Reads every wiki-relevant fact directly from the pak: the VehicleParts / Vehicles / Cargos
/// data tables, per-vehicle blueprint stats (weight, seats, drag, fuel, axles, cargo space),
/// the resolved engine/transmission/tire/LSD data assets, and the DeliveryPoint production
/// configs. No intermediate JSON.
/// </summary>
internal sealed class Data(AssetSource assets, Localization localization)
{
    private const string VehiclesPath = "MotorTown/Content/DataAsset/Vehicles/Vehicles";
    private const string VehiclePartsPath = "MotorTown/Content/DataAsset/VehicleParts/VehicleParts";
    private const string CargosPath = "MotorTown/Content/DataAsset/Cargos";
    private const string CargosScheduleIPath = "MotorTown/Content/DataAsset/Cargos_ScheduleI";
    private const string DeliveryPointDir = "MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint/";
    private const string PartTypePrefix = "EMTVehiclePartType::";
    private const string SlotPrefix = "EMTVehiclePartSlot::";
    private const string CargoTypePrefix = "EDeliveryCargoType::";
    private const string SpaceTypePrefix = "EMTCargoSpaceType::";

    public List<PartInfo> Parts { get; } = [];
    public List<VehicleInfo> Vehicles { get; } = [];
    public List<CargoInfo> Cargos { get; } = [];
    public List<SpaceInfo> Spaces { get; } = [];
    public Dictionary<string, DeliveryPointInfo> Points { get; } = new(StringComparer.Ordinal);

    /// <summary>Blueprint CDO type -> localized English name of the location, from the world
    /// actors via WorldExtractor.DeliveryPoints() (the map site's reference resolution).</summary>
    private readonly Dictionary<string, string> _pointNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pointTypes = new(StringComparer.Ordinal);

    private readonly Dictionary<string, JObject?> _dataAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JArray?> _curves = new(StringComparer.OrdinalIgnoreCase);
    private readonly CargoKeys _cargoKeys = new(assets);
    private readonly Dictionary<string, Dictionary<string, JToken?>> _structDefaults = new(StringComparer.Ordinal);

    public void Gather()
    {
        var englishIndex = localization.IndexByEnglish("VehicleName", "Vehicle", "Brand", "Common", "");
        // raw part rows first: vehicles resolve the engine fuel type through them
        var rows = assets.RequirePackage(VehiclePartsPath).First()["Rows"] as JObject ?? [];
        foreach (var (key, rowToken) in rows)
            if (rowToken is JObject obj) _partRowsByKey[key] = obj;
        GatherVehicles(englishIndex);
        GatherParts();
        GatherCargos();
        GatherSpaces();
        GatherDeliveryPoints();
        GatherPointNames();
    }

    // ------------------------------------------------------------------ vehicles

    private void GatherVehicles(Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var rows = assets.RequirePackage(VehiclesPath).First()["Rows"] as JObject ?? [];
        foreach (var (key, rowToken) in rows)
        {
            if (rowToken is not JObject row) continue;

            var names = VehicleName(row, englishIndex);
            var vehicle = new VehicleInfo
            {
                Key = key,
                Names = names,
                Type = (string?)row["VehicleType"] ?? "",
                TruckClass = (string?)row["TruckClass"] ?? "",
                Cost = (long?)row["Cost"] ?? 0,
                Comfort = (double?)row["Comport"] ?? 0,
            };

            foreach (var (field, name) in new[]
                     {
                         ("bIsTaxiable", "taxiable"), ("bIsLimoable", "limoable"), ("bIsBusable", "busable"),
                         ("bIsRaceCar", "raceCar"), ("bTrailerHauling", "trailerHauling"),
                         ("bHasFuelPump", "hasFuelPump"),
                     })
            {
                if ((bool?)row[field] == true) vehicle.Flags.Add(name);
            }

            if (row["GameplayTags"] is JArray tags)
                foreach (var t in tags.OfType<JValue>())
                    if (t.Value is string s) vehicle.Tags.Add(s);
            foreach (var t in (row["NotSupportedPartTypes"] as JArray ?? []).OfType<JValue>())
                if (t.Value is string s) vehicle.NotSupportedPartTypes.Add(s);

            // ordered default parts (the wiki renders the pak array order)
            foreach (var entry in (row["Parts"] as JArray ?? []).OfType<JObject>())
            {
                var slot = (string?)entry["Key"] ?? "";
                if (slot.StartsWith(SlotPrefix, StringComparison.Ordinal)) slot = slot[SlotPrefix.Length..];
                var part = (string?)entry["Value"];
                if (slot.Length > 0 && part is not null) vehicle.DefaultParts.Add((slot, part));
            }

            foreach (var (levelName, levelValue) in Levels(row["LevelRequirementToDrive"]))
                vehicle.Levels.Add((levelName, levelValue));

            vehicle.BasePayment = (long?)row["DeliveryBasePayment"] is { } bpay && bpay != 0 ? bpay : null;
            vehicle.PaymentMultiplier = (double?)row["DeliveryPaymentMultiplier"] is { } pm && Math.Abs(pm - 1.0) > 1e-9 ? pm : null;

            // blueprint-derived stats
            var classPath = (string?)row["VehicleClass"]?["AssetPathName"]
                            ?? (string?)row["VehicleClass"]?["ObjectPath"];
            if (classPath is not null && Blueprint(PathToPackage(classPath)) is { } bp)
            {
                vehicle.WeightKg = bp.WeightKg;
                if (bp.Seats != 0) vehicle.Seats = bp.Seats;
                if (bp.Axles.Count > 0) vehicle.Axles.AddRange(bp.Axles);
                if (bp.AirDragCoeff is { } drag) vehicle.DragCoeff = drag;
                if (bp.FuelTank is { } tank) vehicle.FuelTankL = tank;
                vehicle.CargoSpace = bp.CargoSpace;
            }

            var engine = DefaultPart(row, "Engine");
            if (engine is not null) vehicle.FuelType = EngineFuelType(engine);

            Vehicles.Add(vehicle);
        }
    }

    private readonly Dictionary<string, JObject> _partRowsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PartInfo> _partsByKey = new(StringComparer.Ordinal);

    /// <summary>Fuel type from the default engine part's MHEngineDataAsset, defaulting to
    /// Gasoline when the asset carries no FuelType (the reference reader does the same).</summary>
    private string EngineFuelType(string enginePartKey)
    {
        if (_partRowsByKey.TryGetValue(enginePartKey, out var engineRow))
        {
            var assetPath = (string?)engineRow["EngineAsset"]?["AssetPathName"]
                            ?? (string?)engineRow["EngineAsset"]?["ObjectPath"];
            if (assetPath is not null)
            {
                var packagePath = PathToPackage(assetPath);
                var props = assets.Package(packagePath)?.First()["Properties"]?["EngineProperty"] as JObject;
                var fuelType = (string?)props?["FuelType"];
                if (fuelType is not null)
                {
                    var separator = fuelType.IndexOf("::", StringComparison.Ordinal);
                    return separator < 0 ? fuelType : fuelType[(separator + 2)..];
                }
            }
        }
        return "Gasoline";
    }

    private static string? DefaultPart(JObject row, string slot)
    {
        foreach (var entry in (row["Parts"] as JArray ?? []).OfType<JObject>())
        {
            var key = (string?)entry["Key"] ?? "";
            if (key.StartsWith(SlotPrefix, StringComparison.Ordinal)) key = key[SlotPrefix.Length..];
            if (key == slot) return (string?)entry["Value"];
        }
        return null;
    }

    /// <summary>Full per-language name map (English fallback per language), same resolution
    /// order as out_vehicles_name.json but without the identical-to-English dedupe.</summary>
    private Dictionary<string, string> VehicleName(JObject row,
        Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var texts = (row["VehicleName2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["VehicleName"] as JObject;

        var names = new Dictionary<string, string>();
        foreach (var language in localization.Languages)
        {
            var localized = texts.Count > 0
                ? string.Join(" ", texts
                    .Select(text => Text.LocalizeVehicleText(text, language, localization, englishIndex))
                    .Where(part => !string.IsNullOrEmpty(part)))
                : name is null ? null : Text.LocalizeVehicleText(name, language, localization, englishIndex);
            names[language] = !string.IsNullOrEmpty(localized)
                ? localized!
                : (names.TryGetValue("en", out var en) ? en : name is null ? "" : Text.Source(name) ?? "");
        }
        if (!names.ContainsKey("en"))
            names["en"] = name is null ? "" : Text.Source(name) ?? "";
        return names;
    }

    private readonly Dictionary<string, (double WeightKg, int Seats, double? AirDragCoeff, double? FuelTank, List<AxleInfo> Axles, CargoSpaceInfo? CargoSpace)?> _blueprints = new(StringComparer.OrdinalIgnoreCase);

    private (double WeightKg, int Seats, double? AirDragCoeff, double? FuelTank, List<AxleInfo> Axles, CargoSpaceInfo? CargoSpace)? Blueprint(string packagePath)
    {
        if (_blueprints.TryGetValue(packagePath, out var cached)) return cached;
        var stats = BuildBlueprintStats(packagePath);
        _blueprints[packagePath] = stats;
        return stats;
    }

    private (double WeightKg, int Seats, double? AirDragCoeff, double? FuelTank, List<AxleInfo> Axles, CargoSpaceInfo? CargoSpace)? BuildBlueprintStats(string packagePath)
    {
        var package = assets.Package(packagePath);
        if (package is null) return null;

        double weight = 0;
        int seats = 0;
        double? drag = null, tank = null;
        var axles = new List<(double Brake, bool Driven, bool Dual, bool Lift)>();
        JObject? cdo = null;
        CargoSpaceInfo? cargoSpace = null;

        for (var i = 0; i < package.Exports.Count; i++)
        {
            JObject export;
            try { export = package.Json(i); }
            catch { continue; }

            var props = export["Properties"] as JObject;
            if (props is null) continue;

            if (props["BodyInstance"]?["MassInKgOverride"] is JValue mass)
                weight += Convert.ToDouble(mass.Value);

            var type = (string?)export["Type"] ?? "";
            if (type == "MTSeatComponent") seats++;

            if (type == "MHWheelComponent")
            {
                var name = (string?)export["Name"] ?? "";
                var m = Regex.Match(name, @"\d+");
                var index = m.Success ? int.Parse(m.Value) : 0;
                var axleIndex = index / 2;
                while (axles.Count <= axleIndex) axles.Add((0, false, false, false));
                var axle = axles[axleIndex];
                var flags = props["WheelFlags"] as JArray ?? [];
                var dual = flags.ToString(Newtonsoft.Json.Formatting.None).Contains("DualRearWheel");
                var driven = props["DifferentialComponentName"] is not null;
                var brake = (double?)axle.Brake + (double?)props["BrakeRatio"] ?? 0;
                axles[axleIndex] = (brake, driven, dual || axle.Dual, axle.Lift);
            }

            if (type == "MTVehicleCargoSpaceComponent")
            {
                var spaceType = Suffix((string?)props["CargoSpaceType"], SpaceTypePrefix);
                if (spaceType is not null && cargoSpace is null)
                {
                    // actual dimensions = 2 * BoxExtent(cm) * RelativeScale3D; the pak stores
                    // float32 and UE5 rounds the float directly (no round-trip text)
                    var extent = props["BoxExtent"] as JObject;
                    var scale = props["RelativeScale3D"] as JObject;
                    var len = 2 * ((double?)extent?["X"] ?? 50) * ((double?)scale?["X"] ?? 1) / 100;
                    var wid = 2 * ((double?)extent?["Y"] ?? 50) * ((double?)scale?["Y"] ?? 1) / 100;
                    var hei = 2 * ((double?)extent?["Z"] ?? 50) * ((double?)scale?["Z"] ?? 1) / 100;
                    cargoSpace = new CargoSpaceInfo
                    {
                        Type = spaceType,
                        LengthM = len,
                        WidthM = wid,
                        HeightM = hei,
                        VolumeM3 = len * wid * hei,
                        DumpKl = (double?)props["DumpVolume"] is { } dv && dv != 0 ? dv : null,
                        FixCargo = (bool?)props["bFixCargo"] == true,
                        UnlimitedHeight = (bool?)props["bUnlimitedHeight"] == true,
                    };
                }
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
                            if (axleIndex >= 0 && axleIndex < axles.Count)
                            {
                                var a = axles[axleIndex];
                                axles[axleIndex] = (a.Brake, a.Driven, a.Dual, true);
                            }
                        }
                    }
                }
            }
        }

        return (weight, seats, drag, tank,
            axles.Select(a => new AxleInfo(a.Brake, a.Driven, a.Dual, a.Lift)).ToList(), cargoSpace);
    }

    // ------------------------------------------------------------------ parts

    private void GatherParts()
    {
        ComputeStructDefaults();

        var vehicleNames = Vehicles.ToDictionary(v => v.Key, v => v.Names, StringComparer.OrdinalIgnoreCase);
        var lcParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in _partRowsByKey.Keys) lcParts[k.ToLowerInvariant()] = k;

        foreach (var (key, row) in _partRowsByKey)
        {
            var names = PartName(row, key, vehicleNames);
            var type = (string?)row["PartType"] ?? "";
            var typeEnglish = PartTypeEnglish(type);
            var stats = Stats(row);

            // The FinalDriveRatio field (used for the vehicle page's ratio row) may differ
            // from the pak's name text (fd_10.65 is named "10.65" in-game while its field is
            // 9.4 after a retune) — the name and slug follow the game UI's name text.
            double? fdrValue = row["FinalDriveRatio"] is JValue fv && Convert.ToDouble(fv.Value) != -1
                ? Convert.ToDouble(fv.Value)
                : null;

            var part = new PartInfo
            {
                Key = key,
                Names = names,
                Type = type,
                TypeEnglish = typeEnglish,
                StatsHeading = typeEnglish switch
                {
                    "Engine" => "Engine Physics",
                    "Transmission" => "Transmission Physics",
                    _ => typeEnglish,
                },
                Cost = (long?)row["Cost"] ?? 0,
                MassKg = (double?)row["MassKg"] is { } m && m != 0 ? m : null,
                Hidden = (bool?)row["bIsHidden"] == true,
                Slug = Format.PartSlug(key),
                HasPage = !Regex.IsMatch(key, @"^RideHeight_-\d+$"),
                Row = row,
                Stats = stats,
                Electric = stats["engine"] is JObject e
                    && e["FuelType"]?.ToString().EndsWith("Electric", StringComparison.Ordinal) == true,
                FdrValue = fdrValue,
            };
            Parts.Add(part);
            _partsByKey[key] = part;
        }
    }

    /// <summary>The wiki's part-type display name: the locres "Parts" value when it differs from
    /// the raw enum tail (AntiRollBar -> "Anti-roll Bar"); otherwise the humanized tail
    /// (SideSkirt -> "Side Skirt", since the locres value "SideSkirt" is unhelpful); "LSD" is
    /// spelled out ("Limited Slip Differential") per the wiki.</summary>
    private string PartTypeEnglish(string type)
    {
        if (!type.StartsWith(PartTypePrefix, StringComparison.Ordinal)) return type;
        var tail = type[PartTypePrefix.Length..];
        var locres = localization.Lookup(Localization.English, "Parts", tail);
        if (locres is not null && locres != tail) return locres;
        if (tail == "LSD") return "Limited Slip Differential";
        return Humanize(tail);
    }

    /// <summary>The part's display name per language: Name2 texts joined, else Name, localized.
    /// A generic "#N" name gets the owning vehicles appended — "#1" + VehicleKeys [Dory,
    /// Dory_Wrecker] -> "#1 (Dory / Dory Wrecker)" — the same augmentation the wiki applies.</summary>
    private Dictionary<string, string> PartName(JObject row, string key,
        Dictionary<string, Dictionary<string, string>> vehicleNames)
    {
        var texts = (row["Name2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["Name"] as JObject;

        var names = new Dictionary<string, string>();
        foreach (var language in localization.Languages)
        {
            var joined = string.Join(" ", texts.Select(text =>
                localization.LookupOrEnglish(language, Text.Namespace(text), Text.Key(text))
                ?? Text.Localized(text)
                ?? ""));
            var localized = Blank(joined)
                ? name is null
                    ? ""
                    : localization.LookupOrEnglish(language, Text.Namespace(name), Text.Key(name))
                      ?? Text.Localized(name)
                      ?? Text.Source(name)
                      ?? ""
                : joined;
            names[language] = localized;
        }

        if (Blank(names.GetValueOrDefault("en")))
            names["en"] = name is null ? key : Text.Source(name) ?? key;

        // Generic "#N" names are ambiguous on their own; append the vehicles that use the
        // part. When all owners share a brand (leading words), collapse to the brand —
        // "Brutus Wrecker / Brutus Tanker / Brutus Ambulance / Brutus Fire Engine" ->
        // "Brutus" (user directive); unrelated owners keep the full " / " join.
        if (Regex.IsMatch(names.GetValueOrDefault("en") ?? "", @"^#\d+$")
            && VehicleKeys(row) is { Count: > 0 } keys)
        {
            foreach (var language in localization.Languages)
            {
                if (!names.TryGetValue(language, out var value) || Blank(value)) continue;
                var owners = keys
                    .Where(vk => vk != "None")
                    .Select(vk => vehicleNames.GetValueOrDefault(vk)?.GetValueOrDefault(language) ?? vk)
                    .Where(v => !Blank(v))
                    .ToList();
                var suffix = Brand(owners) ?? string.Join(" / ", owners);
                if (!Blank(suffix)) names[language] = $"{value} ({suffix})";
            }
        }
        return names;
    }

    /// <summary>The brand shared by all owner names — the longest common leading words
    /// ("Brutus Wrecker", "Brutus Tanker" -> "Brutus"); null when they share none.</summary>
    private static string? Brand(List<string> owners)
    {
        if (owners.Count == 0) return null;
        var words = owners.Select(n => n.Split(' ')).ToList();
        var common = new List<string>();
        for (var i = 0; ; i++)
        {
            var w = words[0].Length > i ? words[0][i] : null;
            if (w is null || words.Any(ws => ws.Length <= i || ws[i] != w)) break;
            common.Add(w);
        }
        return common.Count == 0 ? null : string.Join(" ", common);
    }

    private static List<string>? VehicleKeys(JObject row)
    {
        if (row["VehicleKeys"] is not JArray array || array.Count == 0) return null;
        return array.OfType<JValue>().Select(v => (string?)v.Value ?? "").Where(k => k.Length > 0).ToList();
    }

    private static List<string>? OverrideKeys(JToken? token)
    {
        if (token is not JArray array || array.Count == 0) return null;
        return array.OfType<JValue>().Select(v => (string?)v.Value ?? "").Where(k => k.Length > 0).ToList();
    }

    /// <summary>The part→vehicle fit rule (vehicle-parts.md): the override key wins; otherwise
    /// ALL of VehicleTypes / TruckClasses / VehicleKeys / tag query / NotSupportedPartTypes.
    /// Final Drive Ratio parts fit every vehicle (user directive — the bandaid renamed some).</summary>
    private static bool PartFitsVehicle(JObject partRow, VehicleInfo vehicle)
    {
        if ((string?)partRow["PartType"] == "EMTVehiclePartType::FinalDriveRatio") return true;
        if (OverrideKeys(partRow["OverrideAllowedVehicleKeys"])?.Contains(vehicle.Key) == true) return true;
        var keys = VehicleKeys(partRow);
        // a literal "None" key is a key no vehicle row has — the part is UNUSED (the generic
        // RearWing_A/B/C/D, which the wiki wrongly treats as a catch-all and lists on all 171
        // vehicles). Real keys alongside it (Muhan_FrontBumper_02: ["Muhan", "None"]) filter
        // as usual with "None" inert.
        if (keys is { Count: > 0 })
        {
            var real = keys.Where(k => k != "None").ToList();
            if (real.Count == 0) return false;
            if (!real.Contains(vehicle.Key)) return false;
        }
        var types = (partRow["VehicleTypes"] as JArray ?? []).OfType<JValue>().Select(v => (string?)v.Value).Where(v => v is not null).ToList();
        if (types.Count > 0 && !types.Contains(vehicle.Type)) return false;
        var classes = (partRow["TruckClasses"] as JArray ?? []).OfType<JValue>().Select(v => (string?)v.Value).Where(v => v is not null).ToList();
        if (classes.Count > 0 && !classes.Contains(vehicle.TruckClass)
            && !((bool?)partRow["bTruckClassIncludeNone"] == true && vehicle.TruckClass == "EMTTruckClass::None"))
            return false;
        if (!TagQueryMatches(partRow["VehicleRowGameplayTagQuery"], vehicle.Tags)) return false;
        if (vehicle.NotSupportedPartTypes.Contains((string?)partRow["PartType"] ?? "")) return false;
        return true;
    }

    /// <summary>Evaluates a part's VehicleRowGameplayTagQuery against the vehicle's
    /// GameplayTags. Token stream ([version][hasRoot=1][expr…]) mirrors CUE4Parse's
    /// FQueryEvaluator: op 1/2/3 = Any/All/No TagsMatch with [count][indices…], 4/5/6 = the
    /// expression variants. Tag matching uses UE's hierarchy rule (the query tag matches the
    /// vehicle tag itself or any child, e.g. Vehicle.Bike matches Vehicle.Bike.SportBike).
    /// An absent or empty query always fits.</summary>
    private static bool TagQueryMatches(JToken? query, List<string> vehicleTags)
    {
        if (query is not JObject obj) return true;
        var tokens = (obj["QueryTokenStream"] as JArray ?? []).OfType<JValue>()
            .Select(v => Convert.ToByte(v.Value)).ToList();
        var dict = (obj["TagDictionary"] as JArray ?? []).OfType<JObject>()
            .Select(o => (string?)o["TagName"] ?? "").ToList();
        if (tokens.Count == 0) return true;

        var i = 0;
        byte Next() => i < tokens.Count ? tokens[i++] : byte.MaxValue;
        bool MatchTag(string queryTag) =>
            vehicleTags.Any(tag => tag == queryTag
                || tag.StartsWith(queryTag + ".", StringComparison.Ordinal));

        bool EvalExpr() => Next() switch
        {
            1 => EvalTags(matchAll: false, invert: false),
            2 => EvalTags(matchAll: true, invert: false),
            3 => EvalTags(matchAll: false, invert: true),
            4 => EvalExprs(matchAll: false, invert: false),
            5 => EvalExprs(matchAll: true, invert: false),
            6 => EvalExprs(matchAll: false, invert: true),
            _ => false,
        };

        bool EvalTags(bool matchAll, bool invert)
        {
            var n = Next();
            if (n == byte.MaxValue) return false;
            var found = false;
            var all = true;
            for (var k = 0; k < n; k++)
            {
                var tagIdx = Next();
                if (tagIdx == byte.MaxValue) return false;
                var hit = tagIdx < dict.Count && MatchTag(dict[tagIdx]);
                found |= hit;
                all &= hit;
            }
            return invert ? !found : matchAll ? all : found;
        }

        bool EvalExprs(bool matchAll, bool invert)
        {
            var n = Next();
            if (n == byte.MaxValue) return false;
            var found = false;
            var all = true;
            for (var k = 0; k < n; k++)
            {
                var hit = EvalExpr();
                found |= hit;
                all &= hit;
            }
            return invert ? !found : matchAll ? all : found;
        }

        _ = Next();                       // stream version
        if (Next() != 1) return false;    // hasRootExpression marker
        var result = EvalExpr();
        return result && i == tokens.Count;
    }

    private static List<(string, long)> Levels(JToken? levels)
    {
        var result = new List<(string, long)>();
        if (levels is not JArray array) return result;
        foreach (var entry in array.OfType<JObject>())
        {
            var key = (string?)entry["Key"] ?? "";
            if (key.Length == 0) continue;
            result.Add((key, (long?)entry["Value"] ?? 0));
        }
        return result;
    }

    private static bool Blank(string? value) => string.IsNullOrEmpty(value);

    private static string? Suffix(string? full, string prefix) =>
        full is not null && full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..] : full;

    private static string? TypeSuffix(string? full) => Suffix(full, CargoTypePrefix);
    private static string? SpaceSuffix(string? full) => Suffix(full, SpaceTypePrefix);

    // ---- per-part stats (port of the validated PartExtractor rules) ----

    private static readonly string[] StatStructs =
    [
        "Aero", "AngleKit", "AntiRollBar", "BrakeBalance", "BrakePad", "BrakePower", "CargoBed",
        "CoolantRadiator", "Headlight", "Intake", "ItemInventory", "FuelTank",
        "RoofRack", "SuspensionDamper", "SuspensionRideHeight", "SuspensionSpring", "Taxi",
        "Tire", "TrailerHitch", "Turbocharger", "Wheel", "WheelSpacer", "Winch",
    ];

    private static readonly Dictionary<string, string> PartTypeOwnedStructs = new(StringComparer.Ordinal)
    {
        ["EMTVehiclePartType::BrakePad"] = "BrakePad",
        ["EMTVehiclePartType::CoolantRadiator"] = "CoolantRadiator",
        ["EMTVehiclePartType::TaxiLicense"] = "Taxi",
        ["EMTVehiclePartType::CargoBed"] = "CargoBed",
    };

    private void ComputeStructDefaults()
    {
        foreach (var structName in StatStructs)
        {
            var fieldCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var (_, row) in _partRowsByKey)
            {
                if (row[structName] is not JObject value) continue;
                foreach (var property in value.Properties())
                {
                    var text = property.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
                    if (!fieldCounts.TryGetValue(property.Name, out var counts))
                    {
                        counts = [];
                        fieldCounts[property.Name] = counts;
                    }
                    counts[text] = counts.GetValueOrDefault(text) + 1;
                }
            }

            var fields = new Dictionary<string, JToken?>(StringComparer.Ordinal);
            foreach (var (field, counts) in fieldCounts)
            {
                var (text, _) = counts.MaxBy(pair => pair.Value);
                fields[field] = text == "null" ? null : JToken.Parse(text);
            }
            _structDefaults[structName] = fields;
        }
    }

    private bool StructIsDefault(string structName, JObject value)
    {
        if (!_structDefaults.TryGetValue(structName, out var defaultFields)) return false;
        foreach (var property in value.Properties())
        {
            if (!defaultFields.TryGetValue(property.Name, out var defaultText)) continue;
            var text = property.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            var defText = defaultText?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            if (text != defText) return false;
        }
        return true;
    }

    private JObject Stats(JObject row)
    {
        var stats = new JObject();

        foreach (var scalar in new[] { "AirDragMultiplier", "TrailerAirDragMultiplier", "FrontDamageMultiplier" })
        {
            if (row[scalar] is JValue { Type: JTokenType.Float } value && Convert.ToDouble(value.Value) != 1.0)
                stats[scalar] = value.DeepClone();
        }
        foreach (var scalar in new[] { "AeroLift", "FrontAeroLift", "RearAeroLift" })
        {
            if (row[scalar] is JValue { Type: JTokenType.Float } value && Convert.ToDouble(value.Value) != 0.0)
                stats[scalar] = value.DeepClone();
        }
        if (row["FinalDriveRatio"] is JValue { Type: JTokenType.Float } fdr && Convert.ToDouble(fdr.Value) != -1.0)
            stats["FinalDriveRatio"] = fdr.DeepClone();

        foreach (var structName in StatStructs)
        {
            if (row[structName] is not JObject value || StructIsDefault(structName, value)) continue;
            stats[structName] = value.DeepClone();
        }
        if (PartTypeOwnedStructs.TryGetValue((string?)row["PartType"] ?? "", out var owned)
            && row[owned] is JObject ownedValue)
        {
            stats[owned] = ownedValue.DeepClone();
        }

        if (AssetPath(row["EngineAsset"]) is { } enginePath && ResolveEngine(enginePath) is { } engine)
            stats["engine"] = engine;
        if (AssetPath(row["TransmissionAsset"]) is { } transmissionPath && ResolveTransmission(transmissionPath) is { } transmission)
            stats["transmission"] = transmission;
        if (AssetPath(row["LSDAsset"]) is { } lsdPath && ResolveLsd(lsdPath) is { } lsd)
            stats["lsd"] = lsd;

        var tire = row["Tire"] as JObject;
        if (AssetPath(tire?["TirePhysicsDataAsset"]) is { } tirePath && ResolveTire(tirePath) is { } tirePhysics)
            stats["tire"] = tirePhysics;

        return stats;
    }

    private JObject? ResolveEngine(string assetPath)
    {
        var properties = AssetProperties(assetPath)?["EngineProperty"] as JObject;
        if (properties is null) return null;

        var engine = new JObject();
        CopyNumbers(properties, engine,
            "Inertia", "StarterTorque", "StarterRPM", "MaxTorque", "MaxRPM",
            "FrictionCoulombCoeff", "FrictionViscosityCoeff", "IdleThrottle", "FuelConsumption",
            "CoolingEfficiency", "HeatingPower", "BlipThrottle", "BlipDurationSeconds",
            "IntakeSpeedEfficency", "AfterFireProbability", "MaxJakeBrakeStep",
            "MaxRegenTorqueRatio", "MotorMaxPower", "MotorMaxVoltage");
        CopyEnums(properties, engine, "FuelType", "EngineType");

        var curvePath = AssetPath(properties["TorqueCurve"]);
        if (curvePath is not null && TorqueCurve(curvePath) is { } curve) engine["TorqueCurve"] = curve;
        return engine.Count > 0 ? engine : null;
    }

    private JObject? ResolveTransmission(string assetPath)
    {
        var properties = AssetProperties(assetPath)?["TransmissionProperty"] as JObject;
        if (properties is null) return null;

        var transmission = new JObject();
        CopyNumbers(properties, transmission,
            "ShiftTimeSeconds", "AutoShiftComportRPM", "TorqueConvertorStallRPM",
            "TorqueConvertorStallRatioPower", "TorqueConvertorTorqueRate");
        CopyEnums(properties, transmission, "ClutchType", "Type");
        if ((long?)properties["DefaultGearIndex"] is { } defaultGear) transmission["DefaultGearIndex"] = defaultGear;
        if (AssetProperties(assetPath)?["DevComment"] is JValue { Type: JTokenType.String } comment)
            transmission["DevComment"] = comment.DeepClone();
        if (properties["Gears"] is JArray gears)
        {
            var output = new JArray();
            foreach (var gear in gears.OfType<JObject>())
            {
                output.Add(new JObject
                {
                    ["Name"] = (string?)gear["Name"] ?? "",
                    ["GearRatio"] = gear["GearRatio"]?.DeepClone() ?? 0.0,
                });
            }
            if (output.Count > 0) transmission["Gears"] = output;
        }
        return transmission.Count > 0 ? transmission : null;
    }

    private JObject? ResolveTire(string assetPath)
    {
        var properties = AssetProperties(assetPath)?["TirePhysicsParams"] as JObject;
        if (properties is null) return null;

        var tire = new JObject();
        CopyNumbers(properties, tire,
            "PatchLengthCoefficient", "StaticMu", "SlidingMu", "OffroadFriction",
            "SpringX", "SpringY", "DampingX", "DampingY", "MaxWeightKg",
            "WearRate", "SmokeRate", "CoolDownSpeed", "WarmUpSpeed",
            "RollingResistanceCoeff");
        return tire.Count > 0 ? tire : null;
    }

    private JObject? ResolveLsd(string assetPath)
    {
        var properties = AssetProperties(assetPath);
        if (properties is null) return null;

        var type = (string?)properties["LSDType"];
        if (string.IsNullOrEmpty(type)) return null;

        var lsd = new JObject { ["LSDType"] = type };
        CopyNumbers(properties, lsd, "ClutchPackAccel", "ClutchPackBrake");
        return lsd;
    }

    private JObject? AssetProperties(string assetPath)
    {
        var packagePath = PathToPackage(assetPath);
        if (packagePath is null) return null;
        if (_dataAssets.TryGetValue(packagePath, out var cached)) return cached;
        var properties = assets.Package(packagePath)?.First()["Properties"] as JObject;
        _dataAssets[packagePath] = properties;
        return properties;
    }

    private JArray? TorqueCurve(string assetPath)
    {
        var packagePath = PathToPackage(assetPath);
        if (packagePath is null) return null;
        if (_curves.TryGetValue(packagePath, out var cached)) return cached;

        var keys = assets.Package(packagePath)?.First()["Properties"]?["FloatCurve"]?["Keys"] as JArray;
        JArray? curve = null;
        if (keys is not null)
        {
            curve = new JArray();
            foreach (var key in keys.OfType<JObject>())
            {
                curve.Add(new JObject
                {
                    ["Time"] = key["Time"]?.DeepClone() ?? 0.0,
                    ["Value"] = key["Value"]?.DeepClone() ?? 0.0,
                });
            }
        }
        _curves[packagePath] = curve;
        return curve;
    }

    private static string? AssetPath(JToken? asset)
    {
        if (asset is not JObject obj) return null;
        var path = (string?)obj["AssetPathName"];
        if (!string.IsNullOrEmpty(path)) return path;
        return (string?)obj["ObjectPath"];
    }

    private static void CopyNumbers(JObject source, JObject target, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (source[field] is JValue value && Convert.ToDouble(value.Value) != 0)
                target[field] = value.DeepClone();
        }
    }

    private static void CopyEnums(JObject source, JObject target, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (source[field] is JValue { Type: JTokenType.String } value && !string.IsNullOrEmpty((string?)value))
                target[field] = value.DeepClone();
        }
    }

    private static string Humanize(string name)
    {
        var underscored = name.Replace('_', ' ');
        return string.Concat(underscored.Select((c, i) =>
            i > 0 && underscored[i - 1] != ' ' && (
                (char.IsUpper(c) && !char.IsUpper(underscored[i - 1]))
                || (char.IsDigit(c) && char.IsLetter(underscored[i - 1]))
                || (char.IsLetter(c) && char.IsDigit(underscored[i - 1])))
                ? " " + c
                : c.ToString()));
    }

    /// <summary>"/Game/Cars/Models/Hana/Hana.Hana_C" -> pak path without extension.</summary>
    private static string? PathToPackage(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        var dot = assetPath.LastIndexOf('.');
        var package = dot < 0 ? assetPath : assetPath[..dot];
        return package.StartsWith("/Game", StringComparison.Ordinal)
            ? "MotorTown/Content" + package["/Game".Length..]
            : package;
    }

    // ------------------------------------------------------------------ cargos

    private void GatherCargos()
    {
        foreach (var path in new[] { CargosPath, CargosScheduleIPath })
        {
            var pkg = assets.Package(path);
            if (pkg is null) continue;
            foreach (var (key, rowToken) in pkg.First()["Rows"] as JObject ?? [])
            {
                if (rowToken is not JObject row) continue;
                var canonical = _cargoKeys.Canonical(key);
                var type = (string?)row["CargoType"] ?? "";

                var cargo = new CargoInfo
                {
                    Key = canonical,
                    Name = CargoName(row, canonical),
                    Type = TypeSuffix(type) ?? type,
                    Volume = (double?)row["VolumeSize"] ?? 0,
                    WeightMin = (double?)row["WeightRange"]?["X"] ?? 0,
                    WeightMax = (double?)row["WeightRange"]?["Y"] ?? 0,
                    Stackable = (bool?)row["bAllowStacking"] == true,
                    Fragile = (double?)row["Fragile"] ?? 0,
                    PaymentPerKm = (long?)row["PaymentPer1Km"] ?? 0,
                    PaymentMultiplier = (double?)row["PaymentPer1KmMultiplierByMaxWeight"] ?? 0,
                    BasePayment = (long?)row["BasePayment"] ?? 0,
                    MinDist = (double?)row["MinDeliveryDistance"] ?? 0,
                    MaxDist = (double?)row["MaxDeliveryDistance"] ?? 0,
                    Deprecated = (bool?)row["bDepcreated"] == true,
                };
                foreach (var t in (row["CargoSpaceTypes"] as JArray ?? [])
                             .Select(t => SpaceSuffix((string?)t)).Where(t => t != null))
                    cargo.SpaceTypes.Add(t!);
                foreach (var t in (row["GameplayTags"] as JArray ?? []).OfType<JValue>())
                    if (t.Value is string s) cargo.Tags.Add(s);

                cargo.WeightKg = CargoWeight((string?)row["ActorClass"]?["AssetPathName"] ?? "");
                Cargos.Add(cargo);
            }
        }
    }

    /// <summary>Weight = sum of the actor blueprint's StaticMeshComponent BodyInstance.MassInKgOverride
    /// (bulk/volume cargos carry a single 1M_Cube mesh with the weight baked in).</summary>
    private double? CargoWeight(string actorPath)
    {
        var packagePath = PathToPackage(actorPath);
        if (packagePath is null) return null;
        var pkg = assets.Package(packagePath);
        if (pkg is null) return null;

        double total = 0;
        var found = false;
        for (var i = 0; i < pkg.Exports.Count; i++)
        {
            var json = pkg.Json(i);
            var mass = json?["Properties"]?["BodyInstance"]?["MassInKgOverride"];
            if (mass is JValue { Value: not null } value)
            {
                total += Convert.ToDouble(value.Value);
                found = true;
            }
        }
        return found ? total : null;
    }

    /// <summary>The cargo display name: Name2 texts joined (locres), else the Name text, else
    /// the row key. "AppleBox" -> "Apples", "BottlePallete" -> "Water Bottle Pallet".</summary>
    private string CargoName(JObject row, string fallback)
    {
        var texts = (row["Name2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["Name"] as JObject;
        string? en = null;
        if (texts.Count > 0)
            en = string.Join(" ", texts.Select(text =>
                localization.LookupOrEnglish(Localization.English, Text.Namespace(text), Text.Key(text))
                ?? Text.Localized(text)
                ?? ""));
        if (Blank(en))
            en = name is null ? null
                : localization.LookupOrEnglish(Localization.English, Text.Namespace(name), Text.Key(name))
                  ?? (string?)name["LocalizedString"]
                  ?? (string?)name["SourceString"]
                  ?? (string?)name["CultureInvariantString"];
        return Blank(en) ? fallback : en!;
    }

    // ------------------------------------------------------------------ cargo space buckets + vehicle/part spaces

    private void GatherSpaces()
    {
        var spaces = new Dictionary<string, SpaceInfo>(StringComparer.Ordinal);
        SpaceInfo Bucket(string type)
        {
            if (!spaces.TryGetValue(type, out var bucket))
            {
                bucket = new SpaceInfo { Type = type };
                spaces[type] = bucket;
            }
            return bucket;
        }

        foreach (var cargo in Cargos)
        {
            if (cargo.Deprecated) continue;
            foreach (var t in cargo.SpaceTypes) Bucket(t).Cargos.Add(cargo);
        }

        // vehicle spaces: blueprint component, else the default CargoBed part. Both the
        // default-space lookup and the installable-space derivation below go through the
        // rendered stats (Stats["CargoBed"] is present exactly for real beds — CargoBed-type
        // parts always carry it, unrelated parts only when their struct is non-default) and
        // the single InstallableParts fit rule, so the cargo-space pages can never drift
        // from the part pages or the installable-parts pages.
        foreach (var vehicle in Vehicles)
        {
            var space = vehicle.CargoSpace;
            if (space is null)
            {
                foreach (var (slot, partKey) in vehicle.DefaultParts)
                {
                    if (!slot.StartsWith("CargoBed", StringComparison.Ordinal)) continue;
                    if (_partsByKey.TryGetValue(partKey, out var part)
                        && part.Stats["CargoBed"] is JObject bed
                        && SpaceSuffix((string?)bed["CargoSpaceType"]) is { } bedType)
                    {
                        space = PartCargoSpace(partKey, bed, bedType);
                        vehicle.CargoSpace = space;
                        break;
                    }
                }
            }
            if (space is not null) Bucket(space.Type).Vehicles.Add((vehicle, Installable: false));
        }

        // installable spaces: vehicles that ship with no cargo space but can fit a CargoBed
        // part — derived from the same InstallableParts fit rule that generates the
        // installable-parts pages (no separate filter)
        foreach (var vehicle in Vehicles)
        {
            if (vehicle.CargoSpace is not null) continue;
            foreach (var part in InstallableParts(vehicle))
            {
                // CargoBedAttachment parts modify an existing bed — they don't add space
                if (part.Type.Contains("CargoBedAttachment", StringComparison.Ordinal)) continue;
                if (part.Stats["CargoBed"] is not JObject bed) continue;
                if (SpaceSuffix((string?)bed["CargoSpaceType"]) is not { } bedType) continue;
                if (vehicle.InstallableSpaces.All(s => s.Type != bedType))
                {
                    vehicle.InstallableSpaces.Add(PartCargoSpace(part.Key, bed, bedType));
                    Bucket(bedType).Vehicles.Add((vehicle, Installable: true));
                }
            }
        }

        // part spaces: CargoBed parts
        foreach (var part in Parts)
        {
            if (part.Stats["CargoBed"] is not JObject bed) continue;
            if (SpaceSuffix((string?)bed["CargoSpaceType"]) is not { } bedType) continue;
            if (!part.Type.Contains("CargoBedAttachment", StringComparison.Ordinal))
                Bucket(bedType).Parts.Add(part);
        }

        Spaces.AddRange(spaces.Values);
    }

    private readonly Dictionary<string, List<PartInfo>> _installableCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VehicleInfo>> _installableVehiclesCache = new(StringComparer.Ordinal);

    /// <summary>Every vehicle the part fits, per the same fit rule (the inverse of
    /// InstallableParts — a vehicle can install the part iff the part is in its installable
    /// list). Memoized; used by the per-part installable_vehicles pages.</summary>
    public List<VehicleInfo> InstallableVehicles(PartInfo part)
    {
        if (_installableVehiclesCache.TryGetValue(part.Key, out var cached)) return cached;
        var result = new List<VehicleInfo>();
        if (_partRowsByKey.TryGetValue(part.Key, out var row))
            result.AddRange(Vehicles.Where(v => PartFitsVehicle(row, v)));
        _installableVehiclesCache[part.Key] = result;
        return result;
    }

    /// <summary>Every part the vehicle can install, per the fit rule (Final Drive Ratio parts
    /// always included). Order follows the pak row order. Memoized — the installable-parts
    /// pages and the cargo-space derivation share one computation.</summary>
    public List<PartInfo> InstallableParts(VehicleInfo vehicle)
    {
        if (_installableCache.TryGetValue(vehicle.Key, out var cached)) return cached;
        var result = new List<PartInfo>();
        foreach (var part in Parts)
        {
            if (!_partRowsByKey.TryGetValue(part.Key, out var row)) continue;
            if (PartFitsVehicle(row, vehicle)) result.Add(part);
        }
        _installableCache[vehicle.Key] = result;
        return result;
    }

    /// <summary>Dimensions from the CargoBed part's CargoSpaceSize vector (cm).</summary>
    private CargoSpaceInfo PartCargoSpace(string partKey, JObject bed, string type)
    {
        var size = bed["CargoSpaceSize"] as JObject;
        var x = (double?)size?["X"] ?? 0;
        var y = (double?)size?["Y"] ?? 0;
        var z = (double?)size?["Z"] ?? 0;
        return new CargoSpaceInfo
        {
            Type = type,
            LengthM = x / 100,
            WidthM = y / 100,
            HeightM = z / 100,
            VolumeM3 = (x / 100) * (y / 100) * (z / 100),
            DumpKl = (double?)bed["DumpVolume"] is { } dv && dv != 0 ? dv : null,
            FixCargo = (bool?)bed["bFixCargo"] == true,
            UnlimitedHeight = (bool?)bed["bUnlimitedHeight"] == true,
        };
    }

    // ------------------------------------------------------------------ delivery points

    private void GatherDeliveryPoints()
    {
        foreach (var file in assets.Files(DeliveryPointDir)
                     .Where(f => f.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            var path = file.Path[..^".uasset".Length];
            var point = DeliveryConfigs(path);
            if (point is null) continue;
            var key = path[(path.LastIndexOf('/') + 1)..];
            Points[key] = point;
            var pkg = assets.Package(path);
            if (pkg is not null && ObjectIndex(pkg.First()["ClassDefaultObject"]) is { } cdoIndex)
                _pointTypes[key] = (string?)pkg.Json(cdoIndex)?["Type"] ?? "";
        }
    }

    /// <summary>The location name for a delivery point in the cargo pages: the world actor's
    /// localized name (via WorldExtractor), falling back to the blueprint key.</summary>
    public string PointName(string blueprintKey)
    {
        var type = _pointTypes.GetValueOrDefault(blueprintKey);
        if (type.Length > 0 && _pointNames.TryGetValue(type, out var name) && name.Length > 0)
            return name;
        return blueprintKey;
    }

    private void GatherPointNames()
    {
        var points = new WorldExtractor(assets, localization).DeliveryPoints();
        foreach (var p in points.OfType<JObject>())
        {
            var type = (string?)p["type"];
            if (type is null || _pointNames.ContainsKey(type)) continue;
            _pointNames[type] = (string?)(p["name"] as JObject)?["en"] ?? "";
        }
    }

    private static int? ObjectIndex(JToken? objectPath)
    {
        var path = (string?)objectPath?["ObjectPath"];
        var dot = path?.LastIndexOf('.') ?? -1;
        return dot >= 0 && int.TryParse(path![(dot + 1)..], out var index) ? index : null;
    }

    private DeliveryPointInfo? DeliveryConfigs(string packagePath)
    {
        var pkg = assets.Package(packagePath);
        if (pkg is null) return null;
        for (var i = 0; i < pkg.Exports.Count; i++)
        {
            var json = pkg.Json(i);
            var props = json?["Properties"];
            if (props?["ProductionConfigs"] is null && props?["DemandConfigs"] is null) continue;

            var point = new DeliveryPointInfo { Key = packagePath[(packagePath.LastIndexOf('/') + 1)..] };
            foreach (var c in (props["ProductionConfigs"] as JArray ?? []).OfType<JObject>())
            {
                var config = new ProductionConfig { TimeSeconds = (double?)c["ProductionTimeSeconds"] ?? 0 };
                config.Inputs.AddRange(CargoRefs(c["InputCargos"]));
                config.InputTypes.AddRange(CargoRefs(c["InputCargoTypes"]));
                config.InputTags.AddRange(QueryTags(c["InputCargoGameplayTagQuery"]));
                config.Outputs.AddRange(CargoRefs(c["OutputCargos"]));
                config.OutputTypes.AddRange(CargoRefs(c["OutputCargoTypes"]));
                config.OutputTags.AddRange(QueryTags(c["OutputCargoRowGameplayTagQuery"]));
                point.Configs.Add(config);
            }
            foreach (var d in (props["DemandConfigs"] as JArray ?? []).OfType<JObject>())
            {
                var dKey = (string?)d["CargoKey"] is { Length: > 0 } dk && dk != "None" ? _cargoKeys.Canonical(dk) : null;
                var demand = new CargoRef
                {
                    Key = dKey,
                    Type = TypeSuffix((string?)d["CargoType"]) is { Length: > 0 } t && t != "None" ? t : null,
                };
                demand.Tags.AddRange(QueryTags(d["CargoGameplayTagQuery"]));
                point.Demands.Add(demand);
            }
            foreach (var s in (props["PassiveSupplies"] as JArray ?? []).OfType<JObject>())
            {
                var sKey = (string?)s["CargoKey"] is { Length: > 0 } sk && sk != "None" ? _cargoKeys.Canonical(sk) : null;
                point.PassiveSupplies.Add(new CargoRef
                {
                    Key = sKey,
                    Type = TypeSuffix((string?)s["CargoType"]) is { Length: > 0 } t && t != "None" ? t : null,
                });
            }
            return point;
        }
        return null;
    }

    private List<CargoRef> CargoRefs(JToken? token)
    {
        var list = new List<CargoRef>();
        foreach (var item in token as JArray ?? [])
        {
            var key = (string?)item["Key"];
            if (key is null) continue;
            var count = item["Value"] is JValue v ? Convert.ToDouble(v.Value) : 1;
            list.Add(new CargoRef { Key = _cargoKeys.Canonical(key), Count = count });
        }
        return list;
    }

    private static List<string> QueryTags(JToken? query)
    {
        var list = new List<string>();
        foreach (var tag in query?["TagDictionary"] as JArray ?? [])
        {
            var name = (string?)tag["TagName"];
            if (name is not null) list.Add(name);
        }
        return list;
    }

    // ------------------------------------------------------------------ lookups

    internal PartInfo? PartByKey(string key) => _partsByKey.GetValueOrDefault(key);

    public VehicleInfo? VehicleByKey(string key) =>
        Vehicles.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
}
