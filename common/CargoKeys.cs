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

    private readonly Lazy<Dictionary<string, string>> _canonical = new(() =>
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in new[] { CargosPath, CargosScheduleIPath })
        {
            foreach (var (key, _) in assets.Package(path)?.First()["Rows"] as JObject ?? [])
            {
                canonical.TryAdd(key.ToLowerInvariant(), key);
            }
        }
        return canonical;
    });

    public string Canonical(string key) =>
        key.Length == 0 ? key : _canonical.Value.GetValueOrDefault(key.ToLowerInvariant(), key);
}
