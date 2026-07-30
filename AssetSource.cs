using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MtExtract;

/// <summary>
/// The mounted pak plus the two things everything else needs from it: packages as
/// FModel-shaped JSON, and the localization tables.
/// </summary>
internal sealed class AssetSource : IDisposable
{
    private readonly DefaultFileProvider _provider;
    private readonly JsonSerializer _serializer = JsonSerializer.CreateDefault();
    private readonly Dictionary<string, PackageJson?> _packages = new(StringComparer.OrdinalIgnoreCase);

    public AssetSource(Options opts)
    {
        _provider = new DefaultFileProvider(
            Path.GetDirectoryName(Path.GetFullPath(opts.PakPath))!,
            SearchOption.TopDirectoryOnly,
            new VersionContainer(opts.Game));

        // Register only the requested pak instead of Initialize()'s whole-directory scan.
        _provider.RegisterVfs(new FileInfo(opts.PakPath));
        _provider.SubmitKey(new FGuid(), new FAesKey(opts.AesKey));
        _provider.Mount(); // any container that needed no key
        _provider.PostMount();
        _provider.MappingsContainer = new FileUsmapTypeMappingsProvider(opts.UsmapPath);
    }

    public DefaultFileProvider Provider => _provider;

    public int FileCount => _provider.Files.Count;

    public IEnumerable<GameFile> Files(string prefix) => _provider.Files.Values
        .Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .DistinctBy(f => f.Path)
        .OrderBy(f => f.Path, StringComparer.Ordinal);

    /// <summary>Loads a package by pak path, with or without extension. Cached.</summary>
    public PackageJson? Package(string path)
    {
        if (_packages.TryGetValue(path, out var cached)) return cached;

        var package = Load(path);
        _packages[path] = package;
        return package;
    }

    public PackageJson RequirePackage(string path) =>
        Package(path) ?? throw new FileNotFoundException($"package not found in pak: {path}");

    private PackageJson? Load(string path)
    {
        var candidates = Path.HasExtension(path) ? [path] : new[] { path + ".uasset", path + ".umap", path };
        foreach (var candidate in candidates)
        {
            if (_provider.TryLoadPackage(candidate, out var package))
                return new PackageJson(package, _serializer);
        }
        return null;
    }

    /// <summary>Serializes exports the way FModel's "Save Properties (.json)" does.</summary>
    public void WritePackageJson(GameFile file, string outPath)
    {
        switch (file.Extension)
        {
            case "locres":
            {
                using var ar = file.CreateReader();
                Output.Write(outPath, JsonConvert.SerializeObject(new FTextLocalizationResource(ar), Formatting.Indented));
                return;
            }
            case "locmeta":
            {
                using var ar = file.CreateReader();
                Output.Write(outPath, JsonConvert.SerializeObject(new FTextLocalizationMetaDataResource(ar), Formatting.Indented));
                return;
            }
        }

        // Written one export at a time - the Jeju world package alone is ~850 MB of JSON.
        var package = _provider.LoadPackage(file);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        using var stream = new StreamWriter(outPath);
        using var writer = new JsonTextWriter(stream) { Formatting = Formatting.Indented };
        writer.WriteStartArray();
        foreach (var export in package.GetExports())
        {
            var json = JObject.FromObject(export, _serializer);
            // CUE4Parse now serializes Outer as {ObjectName, ObjectPath}; the consumers of these
            // dumps were written against FModel's older output, which was the bare outer name.
            if (json.ContainsKey("Outer")) json["Outer"] = export.Outer?.Name.Text;
            json.WriteTo(writer);
        }
        writer.WriteEndArray();
    }

    public Localization LoadLocalization()
    {
        const string prefix = "MotorTown/Content/Localization/Game/";
        var tables = new SortedDictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);

        foreach (var file in Files(prefix).Where(f => f.Extension == "locres"))
        {
            var culture = file.Path[prefix.Length..].Split('/')[0];
            if (culture.Equals("Game.locres", StringComparison.OrdinalIgnoreCase)) continue;

            using var ar = file.CreateReader();
            var json = JObject.FromObject(new FTextLocalizationResource(ar), _serializer);

            var table = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var (ns, entries) in json)
            {
                if (entries is not JObject entryObject) continue;
                var strings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in entryObject)
                {
                    if (value?.Type == JTokenType.String) strings[key] = (string)value!;
                }
                table[ns] = strings;
            }
            tables[culture] = table;
        }

        return new Localization(tables);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>A loaded package whose exports are converted to JSON on demand and cached.</summary>
internal sealed class PackageJson
{
    private readonly JsonSerializer _serializer;
    private readonly Dictionary<int, JObject> _json = new();

    public PackageJson(IPackage package, JsonSerializer serializer)
    {
        Package = package;
        _serializer = serializer;
        Exports = package.GetExports().ToList();
    }

    public IPackage Package { get; }

    /// <summary>Exports in export-index order - the order the JSON dumps use.</summary>
    public IReadOnlyList<CUE4Parse.UE4.Assets.Exports.UObject> Exports { get; }

    public JObject Json(int index)
    {
        if (_json.TryGetValue(index, out var cached)) return cached;

        var json = JObject.FromObject(Exports[index], _serializer);
        _json[index] = json;
        return json;
    }

    public JObject First() => Json(0);
}
