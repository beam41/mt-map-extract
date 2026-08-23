using MtExtract;
using Newtonsoft.Json.Linq;

namespace HeightmapExtractor;

/// <summary>Finds the ocean's world Z level, in cm, from the pak - see
/// .agents/knowledge/landscape-heightmap.md's "Ocean level" section for how this was
/// found and cross-verified.</summary>
internal static class OceanExtractor
{
    private const string JejuWorldPath = "MotorTown/Content/Maps/Jeju/Jeju_World";

    /// <summary>Reads the persistent level's <c>MTOceanConfig</c> actor
    /// (<c>OceanConfig.OceanLevel</c>) - the single game-authored value, not derived from
    /// any mesh/actor transform. Cross-verified against the <c>WaterBodyOcean</c> actor's
    /// own <c>WaterBodyOceanComponent.RelativeLocation.Z</c>, which matches exactly
    /// (both <c>-22374</c> cm) - two independent pak sources agreeing, not a guess.
    /// Returns null if the level ever loses this actor (shouldn't happen, but callers
    /// must not assume an ocean always exists on every map).</summary>
    public static double? FindOceanLevelCm(AssetSource assets)
    {
        var package = assets.Package(JejuWorldPath);
        if (package is null) return null;

        for (var i = 0; i < package.Exports.Count; i++)
        {
            if (package.Exports[i].ExportType != "MTOceanConfig") continue;
            var props = package.Json(i)["Properties"] as JObject;
            var oceanLevel = (double?)props?["OceanConfig"]?["OceanLevel"];
            if (oceanLevel is not null) return oceanLevel;
        }
        return null;
    }
}
