using Newtonsoft.Json.Linq;

namespace HouseServer;

/// <summary>
/// Pregenerates every Buildings row × step as a static bundle, for consumers that would rather
/// host files than run this server:
/// <code>
/// manifest.json                  rows, steps, file paths, failures
/// models/{buildingKey}_{step}.glb same bytes /api/buildings/{key}/{step}.glb serves
/// ground/{kind}.png              same bytes /api/ground/{kind}.png serves
/// </code>
/// Goes through <see cref="HouseStore"/>, so the disk cache is filled (and reused) on the way.
/// </summary>
public static class BundleExporter
{
    public static int Run(HouseStore store, string outDir, ILogger log)
    {
        var modelsDir = Path.Combine(outDir, "models");
        var groundDir = Path.Combine(outDir, "ground");
        Directory.CreateDirectory(modelsDir);
        Directory.CreateDirectory(groundDir);

        var rows = store.BuildingRows();
        var total = rows.Sum(r => r.Steps);
        var done = 0;
        var failed = 0;
        var buildings = new JArray();

        foreach (var (key, rowType, stepCount) in rows)
        {
            var steps = new JArray();
            for (var step = 0; step < stepCount; step++)
            {
                done++;
                var entry = new JObject { ["step"] = step };
                try
                {
                    var glb = store.Glb(key, step);
                    if (glb is null)
                    {
                        // A step whose actor draws nothing (e.g. an empty construction stage).
                        entry["file"] = null;
                    }
                    else
                    {
                        var file = $"models/{key}_{step}.glb";
                        File.WriteAllBytes(Path.Combine(outDir, file), glb);
                        entry["file"] = file;
                        entry["bytes"] = glb.Length;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    entry["file"] = null;
                    entry["error"] = e.Message;
                    log.LogWarning("{Key}_{Step} failed: {Message}", key, step, e.Message);
                }
                steps.Add(entry);
                if (done % 25 == 0) log.LogInformation("{Done}/{Total} steps", done, total);
            }
            buildings.Add(new JObject { ["buildingKey"] = key, ["rowType"] = rowType, ["steps"] = steps });
        }

        var ground = new JObject();
        foreach (var kind in HouseStore.GroundTextures.Keys)
        {
            if (store.GroundPng(kind) is not { } png) continue;
            var file = $"ground/{kind}.png";
            File.WriteAllBytes(Path.Combine(outDir, file), png);
            ground[kind] = file;
        }

        var manifest = new JObject
        {
            ["exporterVersion"] = HouseStore.ExporterVersion,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["space"] = "glTF: meters, Y-up; UE (x, y, z) -> (x, z, y) / 100; origin = actor root. "
                        + "Place with the world dump's building location/rotation, as the house-server does.",
            ["groundTextures"] = ground,
            ["buildings"] = buildings,
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToString());

        log.LogInformation("exported {Rows} rows, {Steps} steps, {Failed} failed -> {Dir}",
            rows.Count, total, failed, Path.GetFullPath(outDir));
        return failed == 0 ? 0 : 1;
    }
}
