using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace MtExtract;

/// <summary>
/// The world-data half of the pipeline: area volumes, delivery points, bus stops and houses,
/// read straight out of the pak instead of a JSON dump.
/// </summary>
public sealed class WorldExtractor(AssetSource assets, Localization localization)
{
    private const string WorldPath = "MotorTown/Content/Maps/Jeju/Jeju_World";
    private const string DeliveryPointDir = "MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint/";
    private const string HousesPath = "MotorTown/Content/DataAsset/Houses";

    private static readonly string[] BusStopTypes =
        ["BusStop_01_C", "BusStop_02_C", "BusStop_03_C", "BusTerminal_01_C"];

    private readonly CargoKeys _cargoKeys = new(assets);

    private PackageJson World => assets.RequirePackage(WorldPath);

    // ---------------------------------------------------------------- area volumes

    /// <summary>Named areas with their top-view outline. Emitted as out_area_volume.json.</summary>
    public JArray AreaVolumes()
    {
        var output = new JArray();
        foreach (var index in ExportsOfType("MTAreaVolume"))
        {
            var obj = World.Json(index);
            output.Add(new JObject
            {
                ["name"] = LocalizedName(AreaName(obj)),
                ["flag"] = AreaFlag(obj),
                ["vertex"] = TopViewLines(obj),
            });
        }
        return (JArray)Output.JsNumbers(output);
    }

    private static List<JObject> AreaName(JObject obj)
    {
        if (Props(obj)?["AreaName"] is JObject single) return [single];

        return (Props(obj)?["AreaNameTexts"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
    }

    private static string AreaFlag(JObject obj) =>
        (string?)(Props(obj)?["AreaVolumeFlags"] as JArray)?.FirstOrDefault() ?? "";

    private static JArray TopViewLines(JObject obj)
    {
        var vertices = new JArray();
        foreach (var line in Props(obj)?["TopViewLines"] as JArray ?? [])
        {
            vertices.Add(new JObject { ["x"] = Number(line["X"]), ["y"] = Number(line["Y"]) });
        }
        return vertices;
    }

    // ------------------------------------------------------------- delivery points

    /// <summary>Emitted as out_delivery_point.json.</summary>
    public JArray DeliveryPoints()
    {
        var points = new List<JObject>();

        foreach (var p in Placements())
        {
            var maxStorage = MaxStorage(p.WorldObj) ?? p.MainMaxStorage;
            var storageConfigs = Flatten(FirstNonEmpty(
                StorageConfigs(p.WorldObj), StorageConfigs(p.Main),
                p.Template is null ? [] : StorageConfigs(p.Template)));

            var demandConfigs = MapDemandConfigs(p.WorldObj, p.Main, p.Template, storageConfigs, maxStorage);
            var (production, demandStorage, supplyStorage) =
                MapProductionConfigs(p.WorldObj, p.Main, p.Template, storageConfigs, demandConfigs, maxStorage);

            foreach (var cargo in demandConfigs)
            {
                var key = cargo.CargoKey ?? cargo.CargoType;
                if (key is null || cargo.MaxStorage is null || demandStorage.ContainsKey(key)) continue;
                demandStorage[key] = cargo.MaxStorage;
            }

            var demand = new JObject();
            foreach (var cargo in demandConfigs)
            {
                var key = cargo.CargoKey ?? cargo.CargoType;
                if (key is not null) demand[key] = cargo.PaymentMultiplier.DeepClone();
            }

            var dropPoints = new JArray();
            foreach (var share in Props(p.WorldObj)?["InputInventoryShare"] as JArray ?? [])
            {
                var shareIndex = ObjectIndex(share);
                if (shareIndex is null) continue;
                if (DeliveryPointGuid(World.Json(shareIndex.Value)) is { } guid) dropPoints.Add(guid);
            }

            var point = new JObject
            {
                ["type"] = ExportType(p.WorldObj),
                ["name"] = LocalizedName(Name(p.WorldObj) ?? p.MainName),
                ["coord"] = p.Coord,
                ["guid"] = DeliveryPointGuid(p.WorldObj) ?? DeliveryPointGuid(p.Main),
                ["supplyStorage"] = supplyStorage,
            };

            if (production.Count > 0) point["prod"] = production;
            if (demand.Count > 0) point["demand"] = demand;
            if (demandStorage.Count > 0) point["demandStorage"] = demandStorage;
            if (dropPoints.Count > 0) point["dropPoint"] = dropPoints;

            if ((Props(p.WorldObj)?["MaxDeliveryDistance"] ?? Props(p.Main)?["MaxDeliveryDistance"]) is { } maxDist)
                point["maxDist"] = maxDist.DeepClone();

            if ((Props(p.WorldObj)?["MaxDeliveryReceiveDistance"]
                 ?? Props(p.Main)?["MaxDeliveryReceiveDistance"]) is { } maxReceiveDist)
                point["maxReceiveDist"] = maxReceiveDist.DeepClone();

            points.Add(point);
        }

        InheritDropPointStorage(points);

        var output = new JArray();
        foreach (var point in points) output.Add(Reorder(point));
        return (JArray)Output.JsNumbers(output);
    }

    /// <summary>Per-placement delivery point detail for callers that want the raw config
    /// structures before DeliveryPoints() flattens them into cargo-key -> number maps (the
    /// wiki generator's per-point production pages need Key vs Type kept separate, and its
    /// own CargoRef parsing already understands this raw shape). Same worldObj -> main ->
    /// template fallback DeliveryPoints() uses.</summary>
    public IEnumerable<DeliveryPointDetail> DeliveryPointDetails()
    {
        foreach (var p in Placements())
        {
            yield return new DeliveryPointDetail(
                BlueprintKey: p.BlueprintKey,
                Type: ExportType(p.WorldObj),
                NameTexts: Name(p.WorldObj) ?? p.MainName ?? [],
                Coord: p.Coord,
                Guid: DeliveryPointGuid(p.WorldObj) ?? DeliveryPointGuid(p.Main),
                ProductionConfigs: FirstNonEmpty(ProductionConfigs(p.WorldObj), ProductionConfigs(p.Main),
                    p.Template is null ? [] : ProductionConfigs(p.Template)),
                DemandConfigsRaw: FirstNonEmptyArray(
                    Props(p.WorldObj)?["DemandConfigs"] as JArray, Props(p.Main)?["DemandConfigs"] as JArray,
                    p.Template is null ? null : Props(p.Template)?["DemandConfigs"] as JArray),
                PassiveSuppliesRaw: FirstNonEmptyArray(
                    Props(p.WorldObj)?["PassiveSupplies"] as JArray, Props(p.Main)?["PassiveSupplies"] as JArray,
                    p.Template is null ? null : Props(p.Template)?["PassiveSupplies"] as JArray));
        }
    }

    private static JArray? FirstNonEmptyArray(params JArray?[] candidates) =>
        candidates.FirstOrDefault(a => a is { Count: > 0 });

    /// <summary>One placed world actor of a delivery-point blueprint, with the blueprint's CDO
    /// and (optional) template resolved for the worldObj -> main -> template fallback every
    /// field derived from a placement uses.</summary>
    private readonly record struct Placement(
        string BlueprintKey, JObject WorldObj, JObject Main, JObject? Template, JToken Coord,
        List<JObject>? MainName, long? MainMaxStorage);

    private IEnumerable<Placement> Placements()
    {
        foreach (var file in assets.Files(DeliveryPointDir).Where(f => f.Extension == "uasset"))
        {
            var package = assets.RequirePackage(file.PathWithoutExtension);
            var blueprint = package.First();
            var mainIndex = ObjectIndex(blueprint["ClassDefaultObject"]);
            if (mainIndex is null) continue;

            var main = package.Json(mainIndex.Value);
            var template = Template(main);
            var mainName = Name(main) ?? (template is null ? null : Name(template));
            var mainMaxStorage = MaxStorage(main) ?? (template is null ? 100 : MaxStorage(template));
            var blueprintKey = file.PathWithoutExtension[(file.PathWithoutExtension.LastIndexOf('/') + 1)..];

            foreach (var index in ExportsOfType(ExportType(main)))
            {
                var worldObj = World.Json(index);
                var sceneIndex = ObjectIndex(Props(worldObj)?["RootComponent"]);
                if (sceneIndex is null) continue;

                yield return new Placement(blueprintKey, worldObj, main, template,
                    Coord(World.Json(sceneIndex.Value)), mainName, mainMaxStorage);
            }
        }
    }

    /// <summary>Field order the Rust struct serialized in, kept so the files diff cleanly.</summary>
    private static JObject Reorder(JObject point)
    {
        string[] order =
        [
            "type", "name", "coord", "guid", "prod", "demand", "demandStorage", "supplyStorage",
            "dropPoint", "maxDist", "maxReceiveDist",
        ];

        var ordered = new JObject();
        foreach (var field in order)
        {
            if (point[field] is { } value) ordered[field] = value;
        }
        return ordered;
    }

    /// <summary>A point that shares its inventory with drop points reports their storage too.</summary>
    private static void InheritDropPointStorage(List<JObject> points)
    {
        var byGuid = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var point in points)
        {
            if ((string?)point["guid"] is { } guid) byGuid[guid] = point;
        }

        foreach (var point in points)
        {
            if (point["dropPoint"] is not JArray dropPoints || dropPoints.Count == 0) continue;

            var storage = point["demandStorage"] as JObject ?? new JObject();
            foreach (var dropPoint in dropPoints)
            {
                if (!byGuid.TryGetValue((string)dropPoint!, out var target)) continue;
                foreach (var (key, value) in target["demandStorage"] as JObject ?? [])
                {
                    storage[key] = value?.DeepClone();
                }
            }

            if (storage.Count > 0) point["demandStorage"] = storage;
        }
    }

    private List<StorageConfig> Flatten(List<StorageConfig> configs)
    {
        var flattened = new List<StorageConfig>(configs);
        foreach (var config in configs)
        {
            if (config.CargoKey != "None") continue;
            var members = _cargoKeys.MembersOf(config.CargoType);
            flattened.AddRange(members.Select(member => config with { CargoKey = member }));
        }
        return flattened;
    }

    private List<DemandConfig> MapDemandConfigs(
        JObject worldObj, JObject main, JObject? template, List<StorageConfig> storageConfigs, long? defaultMaxStorage)
    {
        var configs = DemandConfigs(worldObj)
                      ?? DemandConfigs(main)
                      ?? (template is null ? null : DemandConfigs(template))
                      ?? [];

        return configs.Select(config =>
        {
            var storage = storageConfigs.FirstOrDefault(
                c => c.CargoKey == config.CargoKey && c.CargoType == config.CargoType);

            return new DemandConfig(
                CargoKey: config.CargoKey == "None" ? null : config.CargoKey,
                CargoType: config.CargoType == "EDeliveryCargoType::None" ? null : config.CargoType,
                MaxStorage: config.MaxStorage ?? (storage?.MaxStorage ?? defaultMaxStorage),
                PaymentMultiplier: config.PaymentMultiplier);
        }).ToList();
    }

    private (JArray Production, JObject DemandStorage, JObject SupplyStorage) MapProductionConfigs(
        JObject worldObj, JObject main, JObject? template, List<StorageConfig> storageConfigs,
        List<DemandConfig> demandConfigs, long? defaultMaxStorage)
    {
        var configs = FirstNonEmpty(
            ProductionConfigs(worldObj), ProductionConfigs(main),
            template is null ? [] : ProductionConfigs(template));

        var demandStorage = new JObject();
        var supplyStorage = new JObject();
        var production = new JArray();

        foreach (var config in configs)
        {
            var input = new List<ProductionCargo>();
            foreach (var (key, value) in CargoAmounts(config["InputCargos"]))
            {
                var demand = demandConfigs.FirstOrDefault(c => c.CargoKey == key);
                var storage = storageConfigs.FirstOrDefault(c => c.CargoKey == key);
                input.Add(new ProductionCargo(key, null,
                    demand?.MaxStorage ?? storage?.MaxStorage ?? defaultMaxStorage, value));
            }
            foreach (var (key, value) in CargoAmounts(config["InputCargoTypes"]))
            {
                var demand = demandConfigs.FirstOrDefault(c => c.CargoType == key);
                var storage = storageConfigs.FirstOrDefault(c => c.CargoType == key);
                input.Add(new ProductionCargo(null, key,
                    demand?.MaxStorage ?? storage?.MaxStorage ?? defaultMaxStorage, value));
            }

            var output = new List<ProductionCargo>();
            foreach (var (key, value) in CargoAmounts(config["OutputCargos"]))
            {
                var storage = storageConfigs.FirstOrDefault(c => c.CargoKey == key);
                output.Add(new ProductionCargo(key, null, storage?.MaxStorage ?? defaultMaxStorage, value));
            }
            foreach (var (key, value) in CargoAmounts(config["OutputCargoTypes"]))
            {
                // Matched against CargoKey on purpose - the Rust did the same for output types.
                var storage = storageConfigs.FirstOrDefault(c => c.CargoKey == key);
                output.Add(new ProductionCargo(null, key, storage?.MaxStorage ?? defaultMaxStorage, value));
            }

            Record(input, demandStorage);
            Record(output, supplyStorage);

            var entry = new JObject();
            var inputJson = CargoMap(input);
            var outputJson = CargoMap(output);
            if (inputJson.Count > 0) entry["input"] = inputJson;
            if (outputJson.Count > 0) entry["output"] = outputJson;
            entry["prodTime"] = Number(config["ProductionTimeSeconds"]);
            entry["prodSpeedMul"] = Number(config["ProductionSpeedMultiplier"]);
            if (((double?)config["LocalFoodSupply"] ?? 0) != 0) entry["foodSupply"] = Number(config["LocalFoodSupply"]);
            production.Add(entry);
        }

        return (production, demandStorage, supplyStorage);

        static void Record(List<ProductionCargo> cargos, JObject storage)
        {
            foreach (var cargo in cargos)
            {
                var key = cargo.CargoKey ?? cargo.CargoType;
                if (key is null || cargo.MaxStorage is null || storage.ContainsKey(key)) continue;
                storage[key] = cargo.MaxStorage;
            }
        }

        static JObject CargoMap(List<ProductionCargo> cargos)
        {
            var map = new JObject();
            foreach (var cargo in cargos)
            {
                var key = cargo.CargoKey ?? cargo.CargoType;
                if (key is not null) map[key] = cargo.Value;
            }
            return map;
        }
    }

    // ------------------------------------------------------------------ bus stops

    /// <summary>Emitted as out_bus_stop.json - names here are English only, as before.</summary>
    public JArray BusStops()
    {
        var output = new JArray();

        for (var index = 0; index < World.Exports.Count; index++)
        {
            if (!BusStopTypes.Contains(World.Exports[index].ExportType)) continue;

            var obj = World.Json(index);
            var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
            if (sceneIndex is null) continue;

            var destinations = new JArray();
            foreach (var destination in Props(obj)?["AdditionalDestinations"] as JArray ?? [])
            {
                var destinationIndex = ObjectIndex(destination);
                if (destinationIndex is null) continue;
                destinations.Add(new JObject { ["guid"] = BusStopGuid(World.Json(destinationIndex.Value)) });
            }

            var stop = new JObject
            {
                ["type"] = ExportType(obj),
                ["name"] = BusStopName(obj),
                ["coord"] = Coord(World.Json(sceneIndex.Value)),
                ["guid"] = BusStopGuid(obj),
            };

            if (destinations.Count > 0) stop["additionalDest"] = destinations;
            if (Props(obj)?["Tags"] is JArray tags && tags.Any(t => (string?)t == "BusTerminal"))
                stop["terminal"] = true;

            output.Add(stop);
        }

        return output;
    }

    private static JToken BusStopName(JObject obj)
    {
        if (Props(obj)?["BusStopName"]?["Texts"] is JArray texts)
            return string.Join(" ", texts.Select(t => (string?)t["LocalizedString"] ?? ""));

        return Props(obj)?["BusStopDisplayName"]?["LocalizedString"]?.DeepClone() ?? JValue.CreateNull();
    }

    // --------------------------------------------------------------------- houses

    /// <summary>Emitted as out_house.json.</summary>
    public JArray Houses()
    {
        var costs = assets.RequirePackage(HousesPath).First()["Rows"] as JObject ?? [];
        var output = new JArray();

        foreach (var index in ExportsOfType("House_C"))
        {
            var obj = World.Json(index);
            var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
            if (sceneIndex is null) continue;

            var name = (string?)Props(obj)?["HousegKey"] ?? "";
            var size = Props(obj)?["AreaSize"];
            var empty = ((double?)size?["X"] ?? 0) == 0 && ((double?)size?["Y"] ?? 0) == 0;

            output.Add(new JObject
            {
                ["name"] = name,
                ["coord"] = Coord(World.Json(sceneIndex.Value)),
                ["size"] = empty
                    ? new JObject { ["x"] = 2000.0, ["y"] = 2000.0 }
                    : new JObject { ["x"] = Number(size?["X"]), ["y"] = Number(size?["Y"]) },
                ["cost"] = costs[name]?["Cost"]?.DeepClone() ?? 0,
            });
        }

        return output;
    }

    // ------------------------------------------------------- vehicle sale & spawn points

    /// <summary>Emitted for the wiki's vehicle "Sold At" table: every MTDealerVehicleSpawnPoint
    /// world placement, keyed by the vehicle blueprint package it displays on the lot (matches
    /// a Vehicles row's VehicleClass once resolved through the same /Game -> pak path rule the
    /// wiki generator already applies to VehicleClass elsewhere). 28 of the 183 placements
    /// leave VehicleClass unset and carry the vehicle in EditorVisualVehicleClass instead
    /// (an editor-preview field that turns out to be load-bearing for these) - falls back to
    /// it, then to the first non-null entry of the plain object-ref array VehicleClasses[] when
    /// both singular fields are unset (SCM Kart One's only dealer placement: VehicleClass and
    /// EditorVisualVehicleClass are both empty, VehicleClasses = [SCM_Kart_One, null] - this
    /// was the vehicle's actual "Sold At" signal, missed entirely before this fallback), or the
    /// whole placement has no vehicle to display and is skipped.</summary>
    public List<(string VehicleClassPackage, JToken Coord)> DealerVehicleSpawnPoints()
    {
        var result = new List<(string, JToken)>();
        foreach (var index in ExportsOfType("MTDealerVehicleSpawnPoint"))
        {
            var obj = World.Json(index);
            var props = Props(obj);
            var classPath = ObjectPackage(props?["VehicleClass"])
                            ?? ObjectPackage(props?["EditorVisualVehicleClass"])
                            ?? (props?["VehicleClasses"] as JArray ?? [])
                                .OfType<JObject>().Select(ObjectPackage).FirstOrDefault(c => c is not null);
            var sceneIndex = ObjectIndex(props?["RootComponent"]);
            if (classPath is null || sceneIndex is null) continue;
            result.Add((classPath, Coord(World.Json(sceneIndex.Value))));
        }
        return result;
    }

    /// <summary>One MWorldVehicleSpawnPoint world placement: ambient/world vehicle spawning
    /// (134 instances - random-traffic trailer/car spawns, plus special single-vehicle spots
    /// like SCM Kart One's own spot at Olle Speedway). A separate actor type from
    /// MTDealerVehicleSpawnPoint entirely - not found via the earlier keyword-filtered actor
    /// scan since neither "MWorldVehicleSpawnPoint" nor "VehicleClasses" matched any of the
    /// dealer/garage/vendor keywords searched for.</summary>
    public sealed record WorldSpawnPoint(List<string> VehicleKeys, List<string> ClassPackages, JToken Coord);

    public List<WorldSpawnPoint> WorldVehicleSpawnPoints()
    {
        var result = new List<WorldSpawnPoint>();
        foreach (var index in ExportsOfType("MWorldVehicleSpawnPoint"))
        {
            var obj = World.Json(index);
            var props = Props(obj);
            var sceneIndex = ObjectIndex(props?["RootComponent"]);
            if (sceneIndex is null) continue;

            var vehicleKeys = (props?["VehicleParams"] as JArray ?? [])
                .Select(p => (string?)p["VehicleKey"]).Where(k => k is { Length: > 0 }).Select(k => k!).ToList();
            var classPackages = (props?["VehicleClasses"] as JArray ?? [])
                .Select(ObjectPackage).Where(c => c is not null).Select(c => c!).ToList();
            if (vehicleKeys.Count == 0 && classPackages.Count == 0
                && ObjectPackage(props?["EditorVisualVehicleClass"]) is { } fallback)
                classPackages.Add(fallback);
            if (vehicleKeys.Count == 0 && classPackages.Count == 0) continue;

            result.Add(new WorldSpawnPoint(vehicleKeys, classPackages, Coord(World.Json(sceneIndex.Value))));
        }
        return result;
    }

    /// <summary>Every manual "spawn menu inside a spawn box" placement in the world (its
    /// MTInteractableComponent's Interactions is EMotorTownInteractableType::SpawnVehicle) -
    /// matched by owning an MTSpawnVehicleListComponent, not by literal actor type name.
    /// VehicleSpawner_C is the generic one (3 instances) with a per-instance vehicle list (e.g.
    /// SCM Kart One, or Zydro/Cora/Panther/FormulaSCM together); Terra, Vulcan, the four taxi
    /// keys (Trophy_Taxi/Nimo_Taxi/Nuke_Taxi/Elisa2), and the delivery Scooty each have their
    /// own dedicated single-purpose blueprint (TerraSpawner_C, VulcanSpawner_C, TaxiSpawner_C,
    /// DeliveryScooterSpawner_C) with the key list fixed on the blueprint's own
    /// MTSpawnVehicleListComponent archetype instead (same "list lives on the blueprint, not
    /// per instance" pattern as ServiceVehicleSpawners) - resolved via the instance component's
    /// own Template reference when the instance itself carries no override. Matching by literal
    /// type name alone (pre-2026-08-20) silently dropped every one of these dedicated
    /// blueprints' "Spawned At" row (Vulcan: found 1 of its real 2 - the ambient
    /// MWorldVehicleSpawnPoint only, missing this spawn box entirely). Feeds the wiki's
    /// "Spawned At" table, not "Sold At" - see DealerVehicleSpawnPoints for the real sale
    /// signal. Police/Fire/Ambulance/Bus job spawners are excluded here (handled separately by
    /// ServiceVehicleSpawners - a different semantic, job-vehicle access, not personal use).
    /// Type/tag-only siblings with no explicit vehicle key anywhere (Truck/Trailer/Wrecker/
    /// GarbageTruck spawners: match by VehicleTypes/a GameplayTag against "whatever you already
    /// own", not one specific model) naturally yield no keys and are skipped - they don't
    /// correspond to any single vehicle's "Spawned At" row.</summary>
    public sealed record VehicleSpawnerPoint(List<string> VehicleKeys, JToken Coord);

    public List<VehicleSpawnerPoint> VehicleSpawnerPoints()
    {
        var excludedTypes = ServiceSpawnerBlueprints.Select(b => b.ActorType).ToHashSet();
        var result = new List<VehicleSpawnerPoint>();
        foreach (var index in ExportsOfType("MTSpawnVehicleListComponent"))
        {
            var comp = World.Json(index);
            var ownerIndex = ObjectIndex(comp["Outer"]);
            if (ownerIndex is null) continue;
            if (excludedTypes.Contains(World.Exports[ownerIndex.Value].ExportType)) continue;

            var owner = World.Json(ownerIndex.Value);
            var sceneIndex = ObjectIndex(Props(owner)?["RootComponent"]);
            if (sceneIndex is null) continue;

            var vehicleKeys = VehicleKeysOf(Props(comp));
            if (vehicleKeys.Count == 0) vehicleKeys = VehicleKeysOf(Props(Template(comp)));
            if (vehicleKeys.Count == 0) continue;

            result.Add(new VehicleSpawnerPoint(vehicleKeys, Coord(World.Json(sceneIndex.Value))));
        }
        return result;
    }

    private static List<string> VehicleKeysOf(JObject? list)
    {
        var keys = (list?["VehicleParams"] as JArray ?? [])
            .Select(p => (string?)p["VehicleKey"]).Where(k => k is { Length: > 0 }).Select(k => k!).ToList();
        keys.AddRange((list?["VechileKeys"] as JArray ?? []).OfType<JValue>()
            .Select(v => (string?)v.Value).Where(k => k is { Length: > 0 }).Select(k => k!));
        return keys;
    }

    /// <summary>A handful of vehicles (Raven, Formula SCM) are sold from a dedicated factory
    /// "production dealer" actor instead of a generic MTDealerVehicleSpawnPoint - one bespoke
    /// blueprint per vehicle, always named `VehicleDealer_{VehicleKey}_Production_C` (built to
    /// order rather than pre-spawned on a lot, but still the wiki's "Sold At" table), which
    /// the row key comes straight out of.</summary>
    private static readonly Regex ProductionDealerType = new(@"^VehicleDealer_(.+)_Production_C$");

    public List<(string VehicleKey, JToken Coord)> ProductionDealers()
    {
        var result = new List<(string, JToken)>();
        for (var index = 0; index < World.Exports.Count; index++)
        {
            var match = ProductionDealerType.Match(World.Exports[index].ExportType);
            if (!match.Success) continue;
            var obj = World.Json(index);
            var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
            if (sceneIndex is null) continue;
            result.Add((match.Groups[1].Value, Coord(World.Json(sceneIndex.Value))));
        }
        return result;
    }

    /// <summary>One service-vehicle spawner blueprint (police/fire/ambulance/bus): the fixed
    /// match criteria from its MTSpawnVehicleListComponent CDO (explicit VehicleParams keys,
    /// unioned with a GameplayTags query the wiki's TagQueryMatches evaluates) plus every world
    /// placement's coordinate. The vehicle list lives on the blueprint, not per instance - every
    /// placement of one spawner type shares the same criteria (verified: instance-level
    /// MTSpawnVehicleList components carry no property overrides in the current pak).</summary>
    public sealed record ServiceSpawner(string Label, List<string> VehicleKeys, JToken? TagQuery, List<JToken> Coords);

    private static readonly (string ActorType, string BlueprintPath, string Label)[] ServiceSpawnerBlueprints =
    [
        ("PoliceVehicleSpawner_C", "MotorTown/Content/Objects/Interaction/PoliceVehicleSpawner", "police"),
        ("FireFighterVehicleSpawner_C", "MotorTown/Content/Objects/Interaction/FireFighterVehicleSpawner", "fire"),
        ("AmbulanceSpawner_C", "MotorTown/Content/Objects/Interaction/AmbulanceSpawner", "ambulance"),
        ("BusSpawner_C", "MotorTown/Content/Objects/Interaction/BusSpawner", "bus"),
    ];

    public List<ServiceSpawner> ServiceVehicleSpawners()
    {
        var groups = new List<ServiceSpawner>();
        foreach (var (actorType, blueprintPath, label) in ServiceSpawnerBlueprints)
        {
            var package = assets.Package(blueprintPath);
            if (package is null) continue;

            // The blueprint's CDO serializes with no Properties at all for these actors - the
            // vehicle list lives on its own MTSpawnVehicleListComponent archetype export
            // instead, found by type (one per package).
            JObject? list = null;
            for (var i = 0; i < package.Exports.Count; i++)
            {
                if (package.Exports[i].ExportType != "MTSpawnVehicleListComponent") continue;
                list = package.Json(i);
                break;
            }

            var vehicleKeys = (Props(list)?["VehicleParams"] as JArray ?? [])
                .Select(p => (string?)p["VehicleKey"]).Where(k => k is { Length: > 0 }).Select(k => k!).ToList();
            var tagQuery = Props(list)?["VehicleRowGameplayTagQuery"] ?? Props(list)?["GameplayTagQuery"];

            var coords = new List<JToken>();
            foreach (var index in ExportsOfType(actorType))
            {
                var obj = World.Json(index);
                var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
                if (sceneIndex is null) continue;
                coords.Add(Coord(World.Json(sceneIndex.Value)));
            }
            groups.Add(new ServiceSpawner(label, vehicleKeys, tagQuery, coords));
        }
        return groups;
    }

    // ---------------------------------------------------------- points of interest

    /// <summary>Named POI actors placed directly in the world with an MTMapIconPlaceName
    /// component (car dealers): one entry per placement, English display name (instance
    /// override, falling back to the blueprint's own default) + coordinate.</summary>
    public List<(string Name, JToken Coord)> NamedPois(params string[] actorTypes)
    {
        var result = new List<(string, JToken)>();
        foreach (var index in ExportsOfType(actorTypes))
        {
            var obj = World.Json(index);
            var nameIndex = ObjectIndex(Props(obj)?["MTMapIconPlaceName"]);
            var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
            if (nameIndex is null || sceneIndex is null) continue;
            var name = EnglishText(PlaceNameTexts(World.Json(nameIndex.Value)));
            result.Add((name, Coord(World.Json(sceneIndex.Value))));
        }
        return result;
    }

    /// <summary>Bare coordinate markers with no name in the pak (police/fire stations, ambulance
    /// patient drop-offs, and the per-town wrecker-mission delivery destinations that densify
    /// them - most of those carry no meaningful ActorLabel either, so treated the same way).</summary>
    public List<JToken> CoordMarkers(params string[] actorTypes)
    {
        var result = new List<JToken>();
        foreach (var index in ExportsOfType(actorTypes))
        {
            var obj = World.Json(index);
            var sceneIndex = ObjectIndex(Props(obj)?["RootComponent"]);
            if (sceneIndex is null) continue;
            result.Add(Coord(World.Json(sceneIndex.Value)));
        }
        return result;
    }

    /// <summary>An MTMapIconPlaceNameComponent's PlaceNameTexts: the instance's own override, or
    /// its blueprint default when the instance carries none (most vendors: every "Flower Shop"
    /// placement shares the one blueprint-level name, no per-instance override).</summary>
    private List<JObject> PlaceNameTexts(JObject component)
    {
        var texts = (Props(component)?["PlaceNameTexts"]?["Texts"] as JArray)?.OfType<JObject>().ToList();
        if (texts is { Count: > 0 }) return texts;
        var template = Template(component);
        return (Props(template)?["PlaceNameTexts"]?["Texts"] as JArray ?? []).OfType<JObject>().ToList();
    }

    /// <summary>English-only text join (POI display names on the wiki are never localized).</summary>
    private string EnglishText(IEnumerable<JObject> texts) => string.Join(" ", texts.Select(t =>
        localization.Lookup(Localization.English, Text.Namespace(t), Text.Key(t)) ?? Text.Localized(t) ?? Text.Source(t) ?? ""));

    // ------------------------------------------------------------------- plumbing

    private IEnumerable<int> ExportsOfType(params string[] types)
    {
        for (var index = 0; index < World.Exports.Count; index++)
        {
            if (types.Contains(World.Exports[index].ExportType)) yield return index;
        }
    }

    private JObject? Template(JObject obj)
    {
        if (obj["Template"] is not JObject template) return null;

        var path = ObjectPackage(template);
        var index = ObjectIndex(template);
        if (path is null || index is null) return null;

        return assets.Package(path)?.Json(index.Value);
    }

    /// <summary>
    /// The name a point shows on the map: an explicit PointName, else a place name plus its
    /// number, else the generic mission point name.
    /// </summary>
    private static List<JObject>? Name(JObject obj)
    {
        var props = Props(obj);

        if (props?["PointName"] is JObject pointName)
            return (pointName["Texts"] as JArray ?? []).OfType<JObject>().ToList();

        if (props?["DeliveryPointName"] is JObject deliveryPointName)
        {
            var texts = new List<JObject>();
            if (deliveryPointName["Name"] is JObject name) texts.Add(name);
            if ((long?)deliveryPointName["Number"] is { } number) texts.Add(Text.Invariant(number.ToString()));
            if (texts.Count > 0) return texts;
        }

        if (props?["MissionPointName"] is JObject missionPointName) return [missionPointName];

        return null;
    }

    private static JObject? Props(JObject? obj) => obj?["Properties"] as JObject;

    private static string ExportType(JObject obj) => (string?)obj["Type"] ?? "";

    /// <summary>"MotorTown/Content/Maps/Jeju/Jeju_World.69" -> package path and export index.</summary>
    private static string? ObjectPackage(JToken? objectPath)
    {
        var path = (string?)objectPath?["ObjectPath"];
        var dot = path?.LastIndexOf('.') ?? -1;
        return dot < 0 ? null : path![..dot];
    }

    private static int? ObjectIndex(JToken? objectPath)
    {
        var path = (string?)objectPath?["ObjectPath"];
        var dot = path?.LastIndexOf('.') ?? -1;
        return dot >= 0 && int.TryParse(path![(dot + 1)..], out var index) ? index : null;
    }

    private static JToken Coord(JObject scene)
    {
        var location = Props(scene)?["RelativeLocation"];
        if (location is null) return JValue.CreateNull();

        return new JObject
        {
            ["x"] = Number(location["X"]),
            ["y"] = Number(location["Y"]),
            ["z"] = Number(location["Z"]),
        };
    }

    private static string? DeliveryPointGuid(JObject obj) => ShortGuid((string?)Props(obj)?["DeliveryPointGuid"]);

    private static string? BusStopGuid(JObject obj) => ShortGuid((string?)Props(obj)?["BusStopGuid"]);

    private static string? ShortGuid(string? guid) => guid?.Replace("-", "").ToLowerInvariant();

    private static long? MaxStorage(JObject obj) => NonZero(Props(obj)?["MaxStorage"]);

    /// <summary>MaxStorage of 0 means "unset" throughout this data.</summary>
    private static long? NonZero(JToken? token)
    {
        var value = (long?)token;
        return value is null or 0 ? null : value;
    }

    /// <summary>Floats are passed through as-is so they print exactly like the source data.</summary>
    private static JToken Number(JToken? token) => token?.DeepClone() ?? new JValue(0.0);

    private List<StorageConfig> StorageConfigs(JObject obj) =>
        (Props(obj)?["StorageConfigs"] as JArray ?? []).Select(c => new StorageConfig(
            (string?)c["CargoType"] ?? "",
            _cargoKeys.Canonical((string?)c["CargoKey"] ?? ""),
            NonZero(c["MaxStorage"]))).ToList();

    private List<UeDemandConfig>? DemandConfigs(JObject obj) =>
        Props(obj)?["DemandConfigs"] is not JArray configs
            ? null
            : configs.Select(c => new UeDemandConfig(
                (string?)c["CargoType"] ?? "",
                _cargoKeys.Canonical((string?)c["CargoKey"] ?? ""),
                NonZero(c["MaxStorage"]),
                Number(c["PaymentMultiplier"]))).ToList();

    private static List<JToken> ProductionConfigs(JObject obj) =>
        (Props(obj)?["ProductionConfigs"] as JArray ?? []).ToList();

    private IEnumerable<(string Key, long Value)> CargoAmounts(JToken? cargos) =>
        (cargos as JArray ?? []).Select(c => (_cargoKeys.Canonical((string?)c["Key"] ?? ""), (long?)c["Value"] ?? 0));

    private static List<T> FirstNonEmpty<T>(params List<T>[] candidates) =>
        candidates.FirstOrDefault(c => c.Count > 0) ?? [];

    /// <summary>
    /// A name as {culture: string}: each part looked up in the locres, joined, and stripped of
    /// the languages that match English.
    /// </summary>
    private JToken LocalizedName(List<JObject>? texts)
    {
        if (texts is null) return JValue.CreateNull();
        if (texts.Count == 0) return new JArray();

        var names = new JObject();
        foreach (var language in localization.Languages)
        {
            names[language] = string.Join(" ", texts.Select(text =>
                localization.LookupOrEnglish(language, Text.Namespace(text), Text.Key(text))
                ?? Text.Localized(text)
                ?? ""));
        }
        return Output.Dedupe(names);
    }

    private record StorageConfig(string CargoType, string CargoKey, long? MaxStorage);

    private record UeDemandConfig(string CargoType, string CargoKey, long? MaxStorage, JToken PaymentMultiplier);

    private record DemandConfig(string? CargoKey, string? CargoType, long? MaxStorage, JToken PaymentMultiplier);

    private record ProductionCargo(string? CargoKey, string? CargoType, long? MaxStorage, long Value);
}

/// <summary>One placed delivery-point world actor with its raw config structures (Key vs
/// Type kept separate, unlike DeliveryPoints()'s flattened JSON), for consumers that parse
/// ProductionConfigs/DemandConfigs/PassiveSupplies themselves.</summary>
public sealed record DeliveryPointDetail(
    string BlueprintKey, string Type, List<JObject> NameTexts, JToken Coord, string? Guid,
    List<JToken> ProductionConfigs, JArray? DemandConfigsRaw, JArray? PassiveSuppliesRaw);
