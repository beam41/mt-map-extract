using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// Cargo keys are FNames: the game compares them case-insensitively but stores whatever the
/// designer typed, so the same cargo shows up as both "Terra" and "terra" - sometimes inside one
/// asset. Everything is folded back onto the spelling the Cargos data table uses, which is what
/// the name and metadata files are keyed by. Note this is not plain PascalCase: "lHBeam_6m" is
/// genuinely lowercase in the table.
/// </summary>
public sealed class CargoKeys(AssetSource assets)
{
    private const string CargosPath = "MotorTown/Content/DataAsset/Cargos";
    private const string CargosScheduleIPath = "MotorTown/Content/DataAsset/Cargos_ScheduleI";

    private readonly Lazy<(Dictionary<string, string> Canonical, Dictionary<string, string[]> ByType)> _data = new(() =>
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        var byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in new[] { CargosPath, CargosScheduleIPath })
        {
            foreach (var (key, row) in assets.Package(path)?.First()["Rows"] as JObject ?? [])
            {
                canonical.TryAdd(key.ToLowerInvariant(), key);

                var type = (string?)row?["CargoType"];
                if (string.IsNullOrEmpty(type)) continue;
                if (!byType.TryGetValue(type, out var members)) byType[type] = members = [];
                members.Add(key);
            }
        }
        return (canonical, byType.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal));
    });

    public string Canonical(string key) =>
        key.Length == 0 ? key : _data.Value.Canonical.GetValueOrDefault(key.ToLowerInvariant(), key);

    /// <summary>Every cargo key whose Cargos-table row carries this CargoType (e.g.
    /// "EDeliveryCargoType::Log" -> ["Log_30ft_30t", "Log_Oak_12ft", ...]), read straight from
    /// the pak instead of a hand-maintained list - the Cargos table is the source of truth and
    /// keeps gaining entries as the game adds cargo, which a hardcoded list would silently miss.
    /// Used to expand a "whole type, no specific key" storage/demand config into per-key
    /// entries.</summary>
    public string[] MembersOf(string cargoType) => _data.Value.ByType.GetValueOrDefault(cargoType, []);
}
