using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// The garage half of the pipeline: the VehicleParts data table (every part with its stats and
/// its vehicle restrictions), the localized part-type names, and the Vehicles data table joined
/// with its restriction fields so consumers can work out which parts fit which vehicle.
///
/// Vehicle parts live in one master table, MotorTown/Content/DataAsset/VehicleParts/VehicleParts
/// (768 rows). The per-type tables next to it (Engines, Wheels, AeroParts, ...) are curated
/// subsets of the same rows, so only the master is read. Likewise Vehicles (171 rows) is the
/// master; Vehicles_Truck, Vehicles_Bus, ... are subsets.
/// </summary>
internal sealed class PartExtractor(AssetSource assets, Localization localization)
{
    private const string VehiclePartsPath = "MotorTown/Content/DataAsset/VehicleParts/VehicleParts";
    private const string VehiclesPath = "MotorTown/Content/DataAsset/Vehicles/Vehicles";
    private const string PartTypePrefix = "EMTVehiclePartType::";
    private const string SlotPrefix = "EMTVehiclePartSlot::";

    /// <summary>
    /// Struct-valued columns of the parts table. A row "uses" a struct when any of its fields
    /// differs from the value most rows carry (the editor default), which is how the game's own
    /// data separates the stat that belongs to a part type from the untouched defaults.
    /// </summary>
    private static readonly string[] StatStructs =
    [
        "Aero", "AngleKit", "AntiRollBar", "BrakeBalance", "BrakePad", "BrakePower", "CargoBed",
        "CoolantRadiator", "Headlight", "Intake", "ItemInventory", "FuelTank",
        "RoofRack", "SuspensionDamper", "SuspensionRideHeight", "SuspensionSpring", "Taxi",
        "Tire", "TrailerHitch", "Turbocharger", "Wheel", "WheelSpacer", "Winch",
    ];

    /// <summary>
    /// Structs that are the part type's own statistics: emitted even when every field equals the
    /// editor default, because the default IS the stat (Basic brake pad's 400 °C fade temperature,
    /// the bike taxi license's Normal type, the cargo-bed attachment's Flatbed space type).
    /// </summary>
    private static readonly Dictionary<string, string> PartTypeOwnedStructs = new(StringComparer.Ordinal)
    {
        ["EMTVehiclePartType::BrakePad"] = "BrakePad",
        ["EMTVehiclePartType::CoolantRadiator"] = "CoolantRadiator",
        ["EMTVehiclePartType::TaxiLicense"] = "Taxi",
        ["EMTVehiclePartType::CargoBed"] = "CargoBed",
        ["EMTVehiclePartType::CargoBedAttachment"] = "CargoBed",
    };

    /// <summary>Scalar stat columns whose default is 1 rather than 0.</summary>
    private static readonly string[] OneDefaultScalars =
        ["AirDragMultiplier", "TrailerAirDragMultiplier", "FrontDamageMultiplier"];

    /// <summary>Scalar stat columns whose default is -1 rather than 0.</summary>
    private static readonly string[] MinusOneDefaultScalars = ["FinalDriveRatio"];

    /// <summary>Struct field default values, computed once as the mode over every row.</summary>
    private Dictionary<string, Dictionary<string, JToken?>>? _structDefaults;

    /// <summary>Resolved Engine/Transmission/Tire/LSD data assets, cached by package path.</summary>
    private readonly Dictionary<string, JObject?> _dataAssets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolved torque curves (CurveFloat), cached by package path.</summary>
    private readonly Dictionary<string, JArray?> _curves = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ vehicle parts

    /// <summary>out_vehicle_part.json, keyed by the part's row name.</summary>
    public JObject VehicleParts()
    {
        var rows = PartsRows();
        ComputeDefaults(rows);

        var output = new JObject();
        foreach (var (key, row) in rows)
        {
            if (row is not JObject obj) continue;
            output[key] = Part(obj, key);
        }
        return output;
    }

    /// <summary>out_vehicle_part_type_name.json, keyed by the full PartType enum.</summary>
    public JObject PartTypeNames()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, row) in PartsRows())
        {
            var type = (string?)row?["PartType"] ?? "";
            if (type.StartsWith(PartTypePrefix, StringComparison.Ordinal)) keys.Add(type);
        }

        var output = new JObject();
        foreach (var key in keys)
        {
            var tail = key[PartTypePrefix.Length..];
            var nameKey = Humanize(tail);

            var names = new JObject();
            foreach (var language in localization.Languages)
            {
                names[language] = localization.LookupOrEnglish(language, "Parts", nameKey)
                                  ?? localization.LookupOrEnglish(language, "Parts", tail)
                                  ?? nameKey;
            }
            output[key] = Output.Dedupe(names);
        }
        return output;
    }

    /// <summary>out_vehicle.json, keyed by the vehicle's row name.</summary>
    public JObject Vehicles()
    {
        var rows = assets.RequirePackage(VehiclesPath).First()["Rows"] as JObject ?? [];
        var englishIndex = localization.IndexByEnglish("VehicleName", "Vehicle", "Brand", "Common", "");

        var output = new JObject();
        foreach (var (key, row) in rows)
        {
            if (row is not JObject obj) continue;

            var vehicle = new JObject
            {
                ["name"] = VehicleName(obj, englishIndex),
                ["type"] = (string?)obj["VehicleType"] ?? "",
                ["truckClass"] = (string?)obj["TruckClass"] ?? "",
            };

            if (NonEmpty(obj["GameplayTags"]) is { } tags) vehicle["tags"] = tags;
            if (NonZero(obj["Cost"]) is { } cost) vehicle["cost"] = cost;
            if (Levels(obj["LevelRequirementToDrive"]) is { } level) vehicle["level"] = level;

            if (obj["Parts"] is JArray parts && parts.Count > 0)
            {
                var map = new JObject();
                foreach (var entry in parts)
                {
                    var slot = (string?)entry?["Key"] ?? "";
                    if (slot.StartsWith(SlotPrefix, StringComparison.Ordinal)) slot = slot[SlotPrefix.Length..];
                    map[slot] = (string?)entry?["Value"] ?? "";
                }
                vehicle["parts"] = map;
            }

            if (obj["PartValues"] is JArray partValues && partValues.Count > 0)
                vehicle["partValues"] = partValues;

            // Bitmask the game's type flags serialize as; the enum is not in the usmap, so it is
            // passed through raw (4 = most vehicles, 16 = taxi/special, 2 = wrecker, ...).
            if ((long?)obj["VehicleTypeFlags"] is { } typeFlags && typeFlags != 0) vehicle["typeFlags"] = typeFlags;

            var restrict = new JObject();
            if (NonEmpty(obj["NotSupportedPartTypes"]) is { } notSupported) restrict["notSupportedTypes"] = notSupported;
            if (NonEmpty(obj["NotOptionalPartTypes"]) is { } notOptionalTypes) restrict["notOptionalTypes"] = notOptionalTypes;
            if (NonEmpty(obj["OptionalPartTypes"]) is { } optionalTypes) restrict["optionalTypes"] = optionalTypes;
            if (NonEmpty(obj["NotOptionalPartSlots"]) is { } notOptionalSlots) restrict["notOptionalSlots"] = notOptionalSlots;
            if (SlotQueries(obj["SlotSupportedPartsQueries"]) is { } slotQueries) restrict["slotQueries"] = slotQueries;
            if (restrict.Count > 0) vehicle["restrict"] = restrict;

            foreach (var (field, name) in new[]
                     {
                         ("bIsTaxiable", "taxiable"), ("bIsLimoable", "limoable"), ("bIsBusable", "busable"),
                         ("bIsRaceCar", "raceCar"), ("bTrailerHauling", "trailerHauling"),
                         ("bHasFuelPump", "hasFuelPump"), ("bHidden", "hidden"), ("bDisabled", "disabled"),
                     })
            {
                if ((bool?)obj[field] == true) vehicle[name] = true;
            }

            output[key] = vehicle;
        }
        return output;
    }

    // --------------------------------------------------------------------- one part

    private JObject Part(JObject row, string key)
    {
        var part = new JObject
        {
            ["name"] = PartName(row, key),
            ["type"] = (string?)row["PartType"] ?? "",
            ["cost"] = NonZero(row["Cost"]) ?? 0,
        };

        if ((bool?)row["bIsHidden"] == true) part["hidden"] = true;
        if (NonZero(row["MassKg"]) is { } massKg) part["massKg"] = massKg;
        if (Levels(row["LevelRequirementToBuy"]) is { } level) part["level"] = level;
        if (NonEmpty(row["Slots"]) is { } slots) part["slots"] = slots;

        var restrict = new JObject();
        if (NonEmpty(row["VehicleTypes"]) is { } types) restrict["types"] = types;
        if (NonEmpty(row["TruckClasses"]) is { } truckClasses)
        {
            restrict["truckClasses"] = truckClasses;
            restrict["truckClassIncludeNone"] = (bool?)row["bTruckClassIncludeNone"] != false;
        }
        if (NonEmpty(row["VehicleKeys"]) is { } keys) restrict["keys"] = keys;
        if (NonEmpty(row["OverrideAllowedVehicleKeys"]) is { } overrideKeys) restrict["overrideKeys"] = overrideKeys;
        if (TagQuery(row["VehicleRowGameplayTagQuery"]) is { } tagQuery) restrict["tagQuery"] = tagQuery;
        if (NonEmpty(row["GameplayTags"]) is { } tags) restrict["tags"] = tags;
        if (restrict.Count > 0) part["restrict"] = restrict;

        var stats = Stats(row);
        if (stats.Count > 0) part["stats"] = stats;

        return part;
    }

    /// <summary>The per-type stat block: the structs the row actually uses, the aero scalars, and
    /// the resolved Engine/Transmission/Tire/LSD data assets the soft refs point at.</summary>
    private JObject Stats(JObject row)
    {
        var stats = new JObject();

        foreach (var scalar in OneDefaultScalars)
        {
            if (row[scalar] is JValue { Type: JTokenType.Float } value && Convert.ToDouble(value.Value) != 1.0)
                stats[scalar] = value.DeepClone();
        }
        foreach (var scalar in new[] { "AeroLift", "FrontAeroLift", "RearAeroLift" })
        {
            if (row[scalar] is JValue { Type: JTokenType.Float } value && Convert.ToDouble(value.Value) != 0.0)
                stats[scalar] = value.DeepClone();
        }
        foreach (var scalar in MinusOneDefaultScalars)
        {
            if (row[scalar] is JValue { Type: JTokenType.Float } value && Convert.ToDouble(value.Value) != -1.0)
                stats[scalar] = value.DeepClone();
        }

        foreach (var structName in StatStructs)
        {
            if (row[structName] is not JObject value || StructIsDefault(structName, value)) continue;
            stats[structName] = value.DeepClone();
        }

        // The part type's own stat struct is meaningful even at the editor default.
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

        // Tire physics live in the asset the Tire struct points at, not in the struct itself.
        var tire = row["Tire"] as JObject;
        if (AssetPath(tire?["TirePhysicsDataAsset"]) is { } tirePath && ResolveTire(tirePath) is { } tirePhysics)
            stats["tire"] = tirePhysics;

        return stats;
    }

    // --------------------------------------------------------- resolved data assets

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
        foreach (var field in new[] { "CVT_ClutchCurvePow" })
        {
            if (properties[field] is JValue value && Convert.ToDouble(value.Value) != 0)
                transmission[field] = value.DeepClone();
        }
        foreach (var field in new[] { "CVT_InputRPMRange", "CVT_GearRatios" })
        {
            if (properties[field] is JObject vector) transmission[field] = vector.DeepClone();
        }
        if ((long?)properties["DefaultGearIndex"] is { } defaultGear) transmission["DefaultGearIndex"] = defaultGear;
        // DevComment ("Citroen 2CV 6", "769D", ...) sits on the asset root, next to TransmissionProperty.
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
                    ["Inertia"] = gear["Inertia"]?.DeepClone() ?? 0.0,
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

    /// <summary>Loads a data asset package and returns the first export's Properties.</summary>
    private JObject? AssetProperties(string assetPath)
    {
        var packagePath = PackagePath(assetPath);
        if (packagePath is null) return null;

        if (_dataAssets.TryGetValue(packagePath, out var cached)) return cached;

        var properties = assets.Package(packagePath)?.First()["Properties"] as JObject;
        _dataAssets[packagePath] = properties;
        return properties;
    }

    /// <summary>The normalized torque curve of a CurveFloat, as {Time, Value} key pairs.</summary>
    private JArray? TorqueCurve(string assetPath)
    {
        var packagePath = PackagePath(assetPath);
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

    /// <summary>"/Game/Cars/Parts/Engine/Kart_10HP.Kart_10HP" -> "MotorTown/Content/Cars/Parts/Engine/Kart_10HP".</summary>
    private static string? PackagePath(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        var dot = assetPath.LastIndexOf('.');
        var package = dot < 0 ? assetPath : assetPath[..dot];
        return package.StartsWith("/Game", StringComparison.Ordinal)
            ? "MotorTown/Content" + package["/Game".Length..]
            : package;
    }

    // ------------------------------------------------------------------------ wiki

    /// <summary>
    /// Writes one human-readable page per part to <paramref name="dir"/> (&lt;key&gt;.json): the
    /// same data as out_vehicle_part.json with only the part type translated to plain English.
    /// </summary>
    public int WikiParts(string dir)
    {
        var rows = PartsRows();
        ComputeDefaults(rows);

        var written = 0;
        foreach (var (key, row) in rows)
        {
            if (row is not JObject obj) continue;
            var page = TranslatePart(Part(obj, key), key);
            Output.Write(Path.Combine(dir, key + ".json"), page.ToString(Newtonsoft.Json.Formatting.Indented));
            written++;
        }
        return written;
    }

    /// <summary>
    /// A part page is the part data with only the type translated to plain English; the whole
    /// restrict block and everything else stay exactly as in out_vehicle_part.json.
    /// </summary>
    private JObject TranslatePart(JObject part, string key)
    {
        var page = new JObject
        {
            ["key"] = key,
            ["type"] = PartTypeEnglish((string?)part["type"] ?? ""),
        };

        foreach (var property in part.Properties())
        {
            if (property.Name == "type") continue;
            page[property.Name] = property.Value;
        }
        return page;
    }

    /// <summary>The English name of a part type: the Parts locres name when it exists, else a
    /// humanized enum tail. Mirrors PartTypeNames().</summary>
    private string PartTypeEnglish(string type)
    {
        if (!type.StartsWith(PartTypePrefix, StringComparison.Ordinal)) return type;
        var tail = type[PartTypePrefix.Length..];
        var nameKey = Humanize(tail);
            return localization.Lookup(Localization.English, "Parts", nameKey)
                   ?? localization.Lookup(Localization.English, "Parts", tail)
                   ?? nameKey;
    }

    // ------------------------------------------------------------------ plumbing

    private JObject PartsRows() =>
        assets.RequirePackage(VehiclePartsPath).First()["Rows"] as JObject ?? [];

    /// <summary>Per struct field, the value most rows carry - the editor default.</summary>
    private void ComputeDefaults(JObject rows)
    {
        if (_structDefaults is not null) return;

        var defaults = new Dictionary<string, Dictionary<string, JToken?>>(StringComparer.Ordinal);
        foreach (var structName in StatStructs)
        {
            var fieldCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var (_, row) in rows)
            {
                if (row is not JObject obj || obj[structName] is not JObject value) continue;
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
            defaults[structName] = fields;
        }
        _structDefaults = defaults;
    }

    private bool StructIsDefault(string structName, JObject value)
    {
        if (_structDefaults is not { } defaults || !defaults.TryGetValue(structName, out var defaultFields))
            return false;

        foreach (var property in value.Properties())
        {
            if (!defaultFields.TryGetValue(property.Name, out var defaultText)) continue;
            var text = property.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            var defText = defaultText?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            if (text != defText) return false;
        }
        return true;
    }

    /// <summary>The part's display name: Name2 texts joined, else Name, localized. A generic
    /// "#N" name gets the owning vehicles appended - "#1" + VehicleKeys [Dory, Dory_Wrecker]
    /// -> "#1 (Dory / Dory Wrecker)" - the same augmentation the wiki applies.</summary>
    private JToken PartName(JObject row, string key)
    {
        var texts = (row["Name2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["Name"] as JObject;

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
                      ?? Text.Localized(name)
                      ?? Text.Source(name)
                : joined;

            if (!Blank(localized)) names[language] = localized;
        }

        // A row whose text is empty in every language still deserves a name: the row key.
        if (Blank((string?)names[Localization.English]))
        {
            var fallback = name is null ? null : Text.Source(name);
            names[Localization.English] = fallback ?? key;
        }

        // Generic "#N" names are ambiguous on their own; append the vehicles that use the part.
        if (Regex.IsMatch((string?)names[Localization.English] ?? "", @"^#\d+$")
            && VehicleKeys(row) is { Count: > 0 } keys)
        {
            var vehicleKeys = keys;
            var vehicles = VehicleNamesByKey();
            foreach (var language in localization.Languages)
            {
                if (names[language] is not JValue value) continue;
                var suffix = string.Join(" / ", vehicleKeys
                    .Where(vk => vk != "None")
                    .Select(vk => (string?)vehicles.GetValueOrDefault(vk)?[language] ?? vk)
                    .Where(v => !Blank(v)));
                if (!Blank(suffix)) names[language] = new JValue($"{value} ({suffix})");
            }
        }

        return Output.Dedupe(names);
    }

    /// <summary>The part's VehicleKeys list, or null when empty.</summary>
    private static List<string>? VehicleKeys(JObject row)
    {
        if (row["VehicleKeys"] is not JArray array || array.Count == 0) return null;
        return array.OfType<JValue>().Select(v => (string?)v.Value ?? "").Where(k => k.Length > 0).ToList();
    }

    /// <summary>Vehicle key -> localized display names, resolved once from the Vehicles table.</summary>
    private Dictionary<string, JObject>? _vehicleNames;

    private Dictionary<string, JObject> VehicleNamesByKey()
    {
        if (_vehicleNames is not null) return _vehicleNames;

        var rows = assets.RequirePackage(VehiclesPath).First()["Rows"] as JObject ?? [];
        var englishIndex = localization.IndexByEnglish("VehicleName", "Vehicle", "Brand", "Common", "");
        var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vk, row) in rows)
        {
            if (row is JObject obj) map[vk] = (JObject)VehicleName(obj, englishIndex);
        }
        _vehicleNames = map;
        return map;
    }

    /// <summary>The vehicle's display name, same resolution order as out_vehicles_name.json.</summary>
    private JToken VehicleName(JObject row, Dictionary<string, (string Namespace, string Key)> englishIndex)
    {
        var texts = (row["VehicleName2"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
        var name = row["VehicleName"] as JObject;

        var names = new JObject();
        foreach (var language in localization.Languages)
        {
            var localized = texts.Count > 0
                ? string.Join(" ", texts
                    .Select(text => Text.LocalizeVehicleText(text, language, localization, englishIndex))
                    .Where(part => !Blank(part)))
                : name is null ? null : Text.LocalizeVehicleText(name, language, localization, englishIndex);

            if (!Blank(localized)) names[language] = localized;
        }

        if (Blank((string?)names[Localization.English])) names[Localization.English] = name is null ? "" : Text.Source(name) ?? "";
        return Output.Dedupe(names);
    }

    /// <summary>[{Key, Value}] pairs to a {key: value} map.</summary>
    private static JObject? Levels(JToken? levels)
    {
        if (levels is not JArray array || array.Count == 0) return null;

        var map = new JObject();
        foreach (var entry in array.OfType<JObject>())
        {
            var key = (string?)entry["Key"] ?? "";
            if (key.Length == 0) continue;
            map[key] = entry["Value"]?.DeepClone() ?? 0;
        }
        return map.Count > 0 ? map : null;
    }

    /// <summary>An array whose non-empty value survives, else null.</summary>
    private static JArray? NonEmpty(JToken? token) =>
        token is JArray { Count: > 0 } array ? (JArray)array.DeepClone() : null;

    private static JToken? NonZero(JToken? token) =>
        token is JValue { Value: not null } value && Convert.ToDouble(value.Value) != 0
            ? value.DeepClone()
            : null;

    private static string? TagQuery(JToken? query)
    {
        if (query is not JObject obj) return null;
        var description = ((string?)obj["AutoDescription"] ?? "").Trim();
        return description.Length == 0 ? null : description;
    }

    /// <summary>Slot -> tag-query description, for the vehicle's SlotSupportedPartsQueries.</summary>
    private static JObject? SlotQueries(JToken? queries)
    {
        if (queries is not JArray array || array.Count == 0) return null;

        var map = new JObject();
        foreach (var entry in array.OfType<JObject>())
        {
            var slot = (string?)entry["Key"] ?? "";
            if (slot.StartsWith(SlotPrefix, StringComparison.Ordinal)) slot = slot[SlotPrefix.Length..];
            var description = ((string?)entry["Value"]?["AutoDescription"] ?? "").Trim();
            if (slot.Length > 0 && description.Length > 0) map[slot] = description;
        }
        return map.Count > 0 ? map : null;
    }

    private static string? AssetPath(JToken? asset)
    {
        if (asset is not JObject obj) return null;
        var path = (string?)obj["AssetPathName"];
        if (!string.IsNullOrEmpty(path)) return path;
        // FSoftObjectPath serializes as AssetPathName, plain object refs as ObjectPath.
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

    /// <summary>Enum-valued string fields (e.g. "EMTFuelType::Diesel"), copied verbatim.</summary>
    private static void CopyEnums(JObject source, JObject target, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (source[field] is JValue { Type: JTokenType.String } value && !string.IsNullOrEmpty((string?)value))
                target[field] = value.DeepClone();
        }
    }

    /// <summary>"Suspension_Damper" -> "Suspension Damper", "Utility0" -> "Utility 0": the
    /// humanized display name of an enum tail or slot name.</summary>
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

    private static bool Blank(string? value) => string.IsNullOrEmpty(value);
}
