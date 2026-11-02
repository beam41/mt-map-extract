using System.Collections.Concurrent;
using MtExtract;
using Newtonsoft.Json.Linq;

namespace HouseServer;

/// <summary>
/// The pak-backed half of the server: house plots (out_house.json's source), building rows and
/// the per-building GLBs (memory + disk cached — a building model only changes with the pak).
/// Every pak access goes through <see cref="_pak"/>: CUE4Parse's provider and the
/// <see cref="AssetSource"/> package cache are not thread-safe.
/// </summary>
public sealed class HouseStore
{
    /// <summary>Bump when the GLB output changes: stale disk caches are ignored and model URLs
    /// change, so browsers holding a cached copy refetch.</summary>
    public const int ExporterVersion = 6;

    private readonly Lock _pak = new();
    private readonly BuildingAssembler _assembler;
    private readonly GlbExporter _exporter;
    private readonly Dictionary<string, JObject> _houses;
    private readonly string _glbDir;
    private readonly ConcurrentDictionary<string, byte[]> _glbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<HouseStore> _log;

    public HouseStore(AssetSource assets, string cacheDir, int maxTextureSize, ILoggerFactory logs)
    {
        _log = logs.CreateLogger<HouseStore>();
        _assembler = new BuildingAssembler(assets);
        _exporter = new GlbExporter(assets, logs.CreateLogger<GlbExporter>(), maxTextureSize);
        _glbDir = Path.Combine(cacheDir, $"glb-v{ExporterVersion}");

        _houses = new WorldExtractor(assets, assets.LoadLocalization()).Houses().OfType<JObject>()
            .ToDictionary(h => (string)h["name"]!, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, yaw) in PlotYaws(assets))
        {
            if (_houses.TryGetValue(key, out var house)) house["yaw"] = yaw;
        }
        _log.LogInformation("{Count} house plots loaded", _houses.Count);
    }

    /// <summary>out_house.json drops the plot's rotation; the AreaSize box spins with the
    /// House_C actor's root component yaw (FirstHouse: 96°, so its 25 m side runs along world Y).</summary>
    private static IEnumerable<(string Key, double Yaw)> PlotYaws(AssetSource assets)
    {
        var world = assets.RequirePackage("MotorTown/Content/Maps/Jeju/Jeju_World");
        for (var i = 0; i < world.Exports.Count; i++)
        {
            if (world.Exports[i].ExportType != "House_C") continue;
            var props = world.Json(i)["Properties"];
            var key = (string?)props?["HousegKey"];
            var rootPath = (string?)props?["RootComponent"]?["ObjectPath"];
            if (key is null || rootPath is null || !int.TryParse(rootPath[(rootPath.LastIndexOf('.') + 1)..], out var root))
                continue;
            yield return (key, (double?)world.Json(root)["Properties"]?["RelativeRotation"]?["Yaw"] ?? 0);
        }
    }

    public JObject? House(string housingKey) => _houses.GetValueOrDefault(housingKey);

    public string? RowType(string buildingKey)
    {
        lock (_pak) return _assembler.RowType(buildingKey);
    }

    public bool HasRow(string buildingKey)
    {
        lock (_pak) return _assembler.Row(buildingKey) is not null;
    }

    /// <summary>The building's GLB, or null for an unknown key / a step with nothing to draw.</summary>
    public byte[]? Glb(string buildingKey, int step)
    {
        var id = $"{buildingKey}_{step}";
        if (_glbs.TryGetValue(id, out var hit)) return hit;

        lock (_pak)
        {
            if (_glbs.TryGetValue(id, out hit)) return hit;

            var file = Path.Combine(_glbDir, id + ".glb");
            if (File.Exists(file)) return _glbs[id] = File.ReadAllBytes(file);

            var parts = _assembler.Assemble(buildingKey, step);
            if (parts is null) return null;

            var glb = _exporter.Export(id, parts);
            _log.LogInformation("{Building}: {Parts} mesh parts, {Kb} KB", id, parts.Count, glb.Length / 1024);

            Directory.CreateDirectory(_glbDir);
            File.WriteAllBytes(file, glb);
            return _glbs[id] = glb;
        }
    }

    /// <summary>The Jeju landscape's grass layer (M_Landscape_Jeju_Base samples
    /// T_GroundGrass_01/_02); house plots sit on that landscape in game.</summary>
    public static readonly IReadOnlyDictionary<string, string> GroundTextures = new Dictionary<string, string>
    {
        ["color"] = "MotorTown/Content/Env/Textures/T_GroundGrass_01_BC",
        ["normal"] = "MotorTown/Content/Env/Textures/T_GroundGrass_01_N",
    };

    /// <summary>A ground texture as PNG, or null for an unknown kind. Cached like the GLBs.</summary>
    public byte[]? GroundPng(string kind)
    {
        if (!GroundTextures.TryGetValue(kind, out var path)) return null;
        var id = "ground_" + kind;
        if (_glbs.TryGetValue(id, out var hit)) return hit;

        lock (_pak)
        {
            if (_glbs.TryGetValue(id, out hit)) return hit;

            var file = Path.Combine(_glbDir, id + ".png");
            if (File.Exists(file)) return _glbs[id] = File.ReadAllBytes(file);

            var png = _exporter.TexturePng(path);
            if (png is null) return null;

            Directory.CreateDirectory(_glbDir);
            File.WriteAllBytes(file, png);
            return _glbs[id] = png;
        }
    }
}
