using CUE4Parse.UE4.Versions;
using HouseServer;
using MtExtract;
using Newtonsoft.Json.Linq;

// POC backend for the amc-web 3D house viewer. Run from the repo root (paths are CWD-relative):
//   dotnet run -c Release --project house-server
// Settings come from ASP.NET configuration, so any of them can be passed as `--Name value`.
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

string Setting(string name, string fallback) => config[name] is { Length: > 0 } v ? v : fallback;

if (config["urls"] is null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
    builder.WebHost.UseUrls("http://localhost:5199");

var cacheDir = Setting("CacheDir", Path.Combine("out", "house-server"));
var worldUrl = Setting("WorldUrl", "https://server.aseanmotorclub.com/api/world/");
var worldTtl = TimeSpan.FromMinutes(double.Parse(Setting("WorldTtlMinutes", "30")));
var maxTextureSize = int.Parse(Setting("MaxTextureSize", "1024"));

var aes = Setting("Aes", Path.Combine("resource", "aes"));
var aesKey = File.Exists(aes) ? File.ReadAllText(aes).Trim() : aes.Trim();
if (!aesKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) aesKey = "0x" + aesKey;

var pak = new PakOptions(
    Setting("Pak", Path.Combine("resource", "MotorTown-Windows.pak")),
    aesKey,
    Setting("Usmap", Path.Combine("resource", "Mappings.usmap")),
    Enum.Parse<EGame>(Setting("Game", nameof(EGame.GAME_UE5_5))));

builder.Services.AddSingleton(_ => new AssetSource(pak));
builder.Services.AddSingleton(sp => new HouseStore(
    sp.GetRequiredService<AssetSource>(), cacheDir, maxTextureSize, sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton(sp => new WorldCache(
    WorldCache.CreateHttpClient(), worldUrl, Path.Combine(cacheDir, "world.json"), worldTtl,
    sp.GetRequiredService<ILogger<WorldCache>>()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// Mount the pak and read the plots before the first request instead of on it.
var houseStore = app.Services.GetRequiredService<HouseStore>();

// `--Export <dir>`: write every building model + a manifest as static files, then exit.
if (config["Export"] is { Length: > 0 } exportDir)
    return BundleExporter.Run(houseStore, exportDir, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Export"));

app.MapGet("/api/houses/{housingKey}", async (string housingKey, HouseStore store, WorldCache world, CancellationToken ct) =>
{
    var house = store.House(housingKey);
    if (house is null) return Results.NotFound();
    var key = (string)house["name"]!;

    var snapshot = await world.GetAsync(ct);
    var housing = (snapshot.World["housings"] as JObject ?? []).Properties()
        .Select(p => p.Value)
        .FirstOrDefault(h => string.Equals((string?)h["housingKey"], key, StringComparison.OrdinalIgnoreCase));

    var buildings = new JArray();
    foreach (var b in snapshot.World["buildings"] as JArray ?? [])
    {
        if (!string.Equals((string?)b["housingKey"], key, StringComparison.OrdinalIgnoreCase)) continue;
        var buildingKey = (string?)b["buildingKey"];
        if (buildingKey is null || !store.HasRow(buildingKey)) continue;

        var step = (int?)b["buildingStep"] ?? 0;
        buildings.Add(new JObject
        {
            ["guid"] = b["guid"],
            ["buildingKey"] = buildingKey,
            ["buildingStep"] = step,
            ["rowType"] = store.RowType(buildingKey)?.Replace("EMTBuildingRowType::", ""),
            ["location"] = b["location"],
            ["rotation"] = b["rotation"],
            ["modelUrl"] = $"/api/buildings/{Uri.EscapeDataString(buildingKey)}/{step}.glb?v={HouseStore.ExporterVersion}",
        });
    }

    var body = new JObject
    {
        ["housingKey"] = key,
        ["coord"] = house["coord"],
        ["size"] = house["size"],
        ["yaw"] = house["yaw"] ?? 0,
        ["cost"] = house["cost"],
        ["ownerName"] = housing?["ownerName"],
        ["worldFetchedAt"] = snapshot.FetchedAt.ToString("O"),
        ["groundTextures"] = new JObject(HouseStore.GroundTextures.Keys.Select(kind =>
            new JProperty(kind, $"/api/ground/{kind}.png?v={HouseStore.ExporterVersion}"))),
        ["buildings"] = buildings,
    };
    return Results.Text(body.ToString(Newtonsoft.Json.Formatting.None), "application/json");
});

app.MapGet("/api/buildings/{buildingKey}/{step:int}.glb", (string buildingKey, int step, HouseStore store, HttpResponse response) =>
{
    if (!store.HasRow(buildingKey)) return Results.NotFound();
    var glb = store.Glb(buildingKey, step);
    if (glb is null) return Results.NotFound();

    // Models only change with the pak; let the browser keep them.
    response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(glb, "model/gltf-binary");
});

app.MapGet("/api/ground/{kind}.png", (string kind, HouseStore store, HttpResponse response) =>
{
    var png = store.GroundPng(kind);
    if (png is null) return Results.NotFound();

    response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});

app.Run();
return 0;
