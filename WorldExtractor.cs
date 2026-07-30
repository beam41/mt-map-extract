using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// The world-data half of the pipeline: area volumes, delivery points, bus stops and houses,
/// read straight out of the pak instead of a JSON dump.
/// </summary>
internal sealed class WorldExtractor(AssetSource assets)
{
    private const string WorldPath = "MotorTown/Content/Maps/Jeju/Jeju_World";
    private const string DeliveryPointDir = "MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint/";
    private const string HousesPath = "MotorTown/Content/DataAsset/Houses";

    private static readonly string[] BusStopTypes =
        ["BusStop_01_C", "BusStop_02_C", "BusStop_03_C", "BusTerminal_01_C"];

    /// <summary>
    /// A storage config that names a cargo type but no key stands for every cargo of that type.
    /// This is the hand-maintained expansion the Rust extractor carried.
    /// </summary>
    private static readonly Dictionary<string, string[]> CargoTypeMembers = new()
    {
        ["EDeliveryCargoType::SmallPackage"] =
        [
            "SmallBox", "CarrotBox", "AppleBox", "OrangeBox", "GlassBottleBox", "Rice", "PumpkinBox",
            "CornBox", "CheeseBox", "MeatBox", "BreadBox", "SnackBox", "GiftBox_01",
        ],
        ["EDeliveryCargoType::LargePackage"] =
        [
            "BoxPallete_01", "BoxPallete_02", "BoxPallete_03", "PowerBox", "OrangeBoxes", "RicePallet",
            "PumpkinPallet", "CornPallet", "BeanPallet", "HempPallet", "CabbagePallet", "ChilliPallet",
            "PotatoPallet", "CheesePallet", "BreadPallet",
        ],
        ["EDeliveryCargoType::None"] =
        [
            "PlasticPallete", "QuicklimePallet", "Fuel", "Oil", "CrudeOil", "LiveFish_01",
            "MilitarySupplyBox_01_Empty", "Milk", "AirlineMealPallet", "FormulaSCM", "PlasticPipes_6m",
            "lHBeam_6m", "SteelCoil_10t", "Cement", "Terra", "SunflowerSeed",
        ],
        ["EDeliveryCargoType::FinalProduct"] = ["ToyBoxes", "BottlePallete"],
        ["EDeliveryCargoType::Wood"] = ["WoodPlank_14ft_5t"],
        ["EDeliveryCargoType::Container"] =
        [
            "Container_30ft_5t", "Container_30ft_10t", "Container_30ft_20t", "Container_20ft_01",
            "Container_40ft_01",
        ],
        ["EDeliveryCargoType::Log"] = ["Log_30ft_30t", "Log_Oak_12ft", "Log_Oak_24ft", "Log_20ft"],
        ["EDeliveryCargoType::Sand"] = ["Sand", "FineSand"],
        ["EDeliveryCargoType::Coal"] = ["Coal"],
        ["EDeliveryCargoType::Stone"] = ["LimestoneRock", "Limestone", "IronOre"],
        ["EDeliveryCargoType::Concrete"] = ["Concrete"],
        ["EDeliveryCargoType::Garbage"] = ["TrashBag", "Trash_Big"],
        ["EDeliveryCargoType::Furniture"] =
        ["Sofa_01", "Sofa_02", "Sofa_03", "Sofa_04", "Bed_01", "Bed_02", "Bed_03"],
        ["EDeliveryCargoType::Food"] =
        [
            "Pizza_01", "Pizza_02", "Pizza_03", "Pizza_04", "Pizza_05", "Pizza_01_Premium", "Burger_01",
            "Burger_01_Signature",
        ],
        ["EDeliveryCargoType::MilitarySupply"] = ["MilitarySupplyBox_01"],
    };

    private PackageJson World => assets.RequirePackage(WorldPath);

    // ---------------------------------------------------------------- area volumes

    /// <summary>Named areas with their top-view outline. Emitted as out_area_volume_raw.json.</summary>
    public JArray AreaVolumes()
    {
        var output = new JArray();
        foreach (var index in ExportsOfType("MTAreaVolume"))
        {
            var obj = World.Json(index);
            output.Add(new JObject
            {
                ["name"] = AreaName(obj),
                ["flag"] = AreaFlag(obj),
                ["vertex"] = TopViewLines(obj),
            });
        }
        return output;
    }

    private static JArray AreaName(JObject obj)
    {
        var names = new JArray();
        if (Props(obj)?["AreaName"] is JObject single)
        {
            names.Add(Text.ProjectMapIconName(single));
            return names;
        }

        foreach (var text in Props(obj)?["AreaNameTexts"]?["Texts"] as JArray ?? [])
        {
            if (text is JObject entry) names.Add(Text.ProjectMapIconName(entry));
        }
        return names;
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

    /// <summary>Emitted as out_delivery_point_raw.json.</summary>
    public JArray DeliveryPoints()
    {
        var points = new List<JObject>();

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

            foreach (var index in ExportsOfType(ExportType(main)))
            {
                var worldObj = World.Json(index);
                var sceneIndex = ObjectIndex(Props(worldObj)?["RootComponent"]);
                if (sceneIndex is null) continue;

                var maxStorage = MaxStorage(worldObj) ?? mainMaxStorage;
                var storageConfigs = Flatten(FirstNonEmpty(
                    StorageConfigs(worldObj), StorageConfigs(main),
                    template is null ? [] : StorageConfigs(template)));

                var demandConfigs = MapDemandConfigs(worldObj, main, template, storageConfigs, maxStorage);
                var (production, demandStorage, supplyStorage) =
                    MapProductionConfigs(worldObj, main, template, storageConfigs, demandConfigs, maxStorage);

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
                foreach (var share in Props(worldObj)?["InputInventoryShare"] as JArray ?? [])
                {
                    var shareIndex = ObjectIndex(share);
                    if (shareIndex is null) continue;
                    if (DeliveryPointGuid(World.Json(shareIndex.Value)) is { } guid) dropPoints.Add(guid);
                }

                var point = new JObject
                {
                    ["type"] = ExportType(worldObj),
                    ["name"] = NameJson(Name(worldObj) ?? mainName),
                    ["coord"] = Coord(World.Json(sceneIndex.Value)),
                    ["guid"] = DeliveryPointGuid(worldObj),
                    ["supplyStorage"] = supplyStorage,
                };

                if (production.Count > 0) point["prod"] = production;
                if (demand.Count > 0) point["demand"] = demand;
                if (demandStorage.Count > 0) point["demandStorage"] = demandStorage;
                if (dropPoints.Count > 0) point["dropPoint"] = dropPoints;

                if ((Props(worldObj)?["MaxDeliveryDistance"] ?? Props(main)?["MaxDeliveryDistance"]) is { } maxDist)
                    point["maxDist"] = maxDist.DeepClone();

                if ((Props(worldObj)?["MaxDeliveryReceiveDistance"]
                     ?? Props(main)?["MaxDeliveryReceiveDistance"]) is { } maxReceiveDist)
                    point["maxReceiveDist"] = maxReceiveDist.DeepClone();

                points.Add(point);
            }
        }

        InheritDropPointStorage(points);

        var output = new JArray();
        foreach (var point in points) output.Add(Reorder(point));
        return output;
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

    private static List<StorageConfig> Flatten(List<StorageConfig> configs)
    {
        var flattened = new List<StorageConfig>(configs);
        foreach (var config in configs)
        {
            if (config.CargoKey != "None" || !CargoTypeMembers.TryGetValue(config.CargoType, out var members)) continue;
            flattened.AddRange(members.Select(member => config with { CargoKey = member }));
        }
        return flattened;
    }

    private static List<DemandConfig> MapDemandConfigs(
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

    private static (JArray Production, JObject DemandStorage, JObject SupplyStorage) MapProductionConfigs(
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

    // ------------------------------------------------------------------- plumbing

    private IEnumerable<int> ExportsOfType(string type)
    {
        for (var index = 0; index < World.Exports.Count; index++)
        {
            if (World.Exports[index].ExportType == type) yield return index;
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

    private static List<StorageConfig> StorageConfigs(JObject obj) =>
        (Props(obj)?["StorageConfigs"] as JArray ?? []).Select(c => new StorageConfig(
            (string?)c["CargoType"] ?? "",
            (string?)c["CargoKey"] ?? "",
            NonZero(c["MaxStorage"]))).ToList();

    private static List<UeDemandConfig>? DemandConfigs(JObject obj) =>
        Props(obj)?["DemandConfigs"] is not JArray configs
            ? null
            : configs.Select(c => new UeDemandConfig(
                (string?)c["CargoType"] ?? "",
                (string?)c["CargoKey"] ?? "",
                NonZero(c["MaxStorage"]),
                Number(c["PaymentMultiplier"]))).ToList();

    private static List<JToken> ProductionConfigs(JObject obj) =>
        (Props(obj)?["ProductionConfigs"] as JArray ?? []).ToList();

    private static IEnumerable<(string Key, long Value)> CargoAmounts(JToken? cargos) =>
        (cargos as JArray ?? []).Select(c => ((string?)c["Key"] ?? "", (long?)c["Value"] ?? 0));

    private static List<T> FirstNonEmpty<T>(params List<T>[] candidates) =>
        candidates.FirstOrDefault(c => c.Count > 0) ?? [];

    private static JToken NameJson(List<JObject>? texts)
    {
        if (texts is null) return JValue.CreateNull();

        var array = new JArray();
        foreach (var text in texts) array.Add(Text.Project(text));
        return array;
    }

    private record StorageConfig(string CargoType, string CargoKey, long? MaxStorage);

    private record UeDemandConfig(string CargoType, string CargoKey, long? MaxStorage, JToken PaymentMultiplier);

    private record DemandConfig(string? CargoKey, string? CargoType, long? MaxStorage, JToken PaymentMultiplier);

    private record ProductionCargo(string? CargoKey, string? CargoType, long? MaxStorage, long Value);
}
