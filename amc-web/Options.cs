using System.Globalization;
using CUE4Parse.UE4.Versions;
using YamlDotNet.Serialization;

namespace AmcWeb;

/// <summary>Tile-encoding options parsed from the yaml; declared here so the standalone
/// projects that link Options.cs (wiki generator, explore) get them without their own copies.</summary>
internal enum TileFormat { Png, Jpeg, Webp, Avif }

internal enum ResampleKernel { Nearest, Linear, Cubic, Mitchell, Lanczos2, Lanczos3 }

internal sealed record Options
{
    public const string DefaultConfig = "mt-extract.yaml";

    public const string Usage = """
        Usage: dotnet run -c Release -- [options]

        Reads resource/MotorTown-Windows.pak and writes out/out_*.json, out/map.png and the
        map tiles in out/tiles/. Paths are relative to the working directory, so run it from
        the data repo.

        Options:
          --config <file>    read options from a yaml file; command line wins over it.
                             Defaults to ./mt-extract.yaml when that exists.

          --pak <file>       pak to read              (default resource/MotorTown-Windows.pak)
          --aes <key|file>   AES key, hex or a file containing it
                                                      (default resource/aes)
          --usmap <file>     type mappings            (default resource/Mappings.usmap)
          --game <EGame>     UE version               (default GAME_UE5_5)
          --out <dir>        json output directory    (default out/amc-web/data)
          --amc              rename enums for the AMC map: drop EMTAreaVolumeFlags::,
                             EDeliveryCargoType:: -> _T, _TSmallPackage2 -> _TSmallPackage

        Map and tiles:
          --map-texture <p>  texture to export
                             (default MotorTown/Content/UI/InGame/Map/WorldMap/T_WorldMap_Jeju)
          --map-out <file>   png output path          (default <out>/map.png)
          --tiles-out <dir>  tile output directory    (default <out>/tiles)
          --zoom <n|native>  highest zoom level, tiles are written for 0..n
                                                      (default 5; native = no upscaling)
          --tile-size <px>   tile size                (default 256)
          --format <fmt>     avif | webp | png | jpeg (default avif)
          --quality <0-100>  quality for avif/webp/jpeg
                                                      (default 65)
          --effort <0-9>     encoder effort, higher is smaller and slower; png uses it as the
                             compression level, webp clamps it to 6
                                                      (default 9)
          --upscale <k>      kernel used above native zoom
                                                      (default nearest)
          --downscale <k>    kernel used below native zoom
                                                      (default lanczos3)
                             kernels: nearest linear cubic mitchell lanczos2 lanczos3

        Stages - any of them can be turned off:
          --skip-json        don't write out/out_*.json (also skips reading the world)
          --skip-map         don't write the png
          --skip-tiles       don't write the tiles

        Other:
          --dump <dir>       also dump every package as FModel-style json (slow, ~5 GB)
          --root <prefix>    pak subtree for --dump   (default MotorTown/)
          --threads <n>      parallel workers         (default cpu count)
          -h, --help         this help
        """;

    /// <summary>Options that take no value, so a yaml `key: true` becomes a bare flag.</summary>
    private static readonly HashSet<string> Flags =
        ["--amc", "--skip-json", "--skip-map", "--skip-tiles", "--help"];

    /// <summary>The amc-web project dir: bin/Release/net10.0 -> amc-web. The shared projects
    /// link Options.cs, but these defaults only matter for the main extractor's own output.</summary>
    public static string ProjectRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    /// <summary>The repo root: the consolidated <c>out/</c> tree lives here, split by project
    /// (<c>out/amc-web</c>, <c>out/richtags</c>, <c>out/wiki</c>).</summary>
    public static string RepoRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    public string PakPath { get; private init; } = Path.Combine("resource", "MotorTown-Windows.pak");
    public string AesKey { get; private init; } = "";
    public string UsmapPath { get; private init; } = Path.Combine("resource", "Mappings.usmap");
    public EGame Game { get; private init; } = EGame.GAME_UE5_5;
    /// <summary>The json output directory: <c>out/amc-web/data</c> regardless of the working
    /// directory; --out overrides it.</summary>
    public string OutDir { get; private init; } = Path.Combine(RepoRoot, "out", "amc-web", "data");
    public string Root { get; private init; } = "MotorTown/";
    public bool Amc { get; private init; }

    public string MapTexture { get; private init; } = "MotorTown/Content/UI/InGame/Map/WorldMap/T_WorldMap_Jeju";
    /// <summary>Unset means <c>out/amc-web/map/map.png</c>.</summary>
    private string? MapOutPath { get; init; }

    public string MapOut => MapOutPath ?? Path.Combine(RepoRoot, "out", "amc-web", "map", "map.png");
    /// <summary>Unset means <c>out/amc-web/map/tiles</c>.</summary>
    private string? TilesOutPath { get; init; }

    public string TilesOut => TilesOutPath ?? Path.Combine(RepoRoot, "out", "amc-web", "map", "tiles");

    /// <summary>Null means the image's native zoom, i.e. never upscale.</summary>
    public int? MaxZoom { get; private init; } = 5;

    public int TileSize { get; private init; } = 256;
    public TileFormat Format { get; private init; } = TileFormat.Avif;
    public int Quality { get; private init; } = 65;
    public int Effort { get; private init; } = 9;
    public ResampleKernel Upscale { get; private init; } = ResampleKernel.Nearest;
    public ResampleKernel Downscale { get; private init; } = ResampleKernel.Lanczos3;

    public string? DumpDir { get; private init; }
    public int Threads { get; private init; } = Environment.ProcessorCount;
    public bool SkipJson { get; private init; }
    public bool SkipMap { get; private init; }
    public bool SkipTiles { get; private init; }
    public bool ShowHelp { get; private init; }

    public string Out(string fileName) => Path.Combine(OutDir, fileName);

    public static Options Parse(string[] args)
    {
        var config = ConfigPath(args);
        var tokens = new List<string>();
        if (config is not null) tokens.AddRange(FromYaml(config));
        tokens.AddRange(args); // the command line is applied last, so it wins

        var o = new Options();
        var aes = Path.Combine("resource", "aes");

        for (var i = 0; i < tokens.Count; i++)
        {
            string Value(string name) => i + 1 < tokens.Count
                ? tokens[++i]
                : throw new ArgumentException($"{name} needs a value");

            switch (tokens[i])
            {
                case "--config": Value("--config"); break; // already read above
                case "--pak": o = o with { PakPath = Value("--pak") }; break;
                case "--aes": aes = Value("--aes"); break;
                case "--usmap": o = o with { UsmapPath = Value("--usmap") }; break;
                case "--out": o = o with { OutDir = Value("--out") }; break;
                case "--root": o = o with { Root = Value("--root").TrimEnd('/') + "/" }; break;
                case "--amc": o = o with { Amc = true }; break;
                case "--map-texture": o = o with { MapTexture = Value("--map-texture") }; break;
                case "--map-out": o = o with { MapOutPath = Value("--map-out") }; break;
                case "--tiles-out": o = o with { TilesOutPath = Value("--tiles-out") }; break;
                case "--skip-json": o = o with { SkipJson = true }; break;
                case "--skip-map": o = o with { SkipMap = true }; break;
                case "--skip-tiles": o = o with { SkipTiles = true }; break;
                case "--dump": o = o with { DumpDir = Value("--dump") }; break;
                case "-h" or "--help": o = o with { ShowHelp = true }; break;

                case "--game": o = o with { Game = Enum<EGame>(Value("--game"), "--game") }; break;
                case "--format": o = o with { Format = Enum<TileFormat>(Value("--format"), "--format") }; break;
                case "--upscale": o = o with { Upscale = Enum<ResampleKernel>(Value("--upscale"), "--upscale") }; break;
                case "--downscale":
                    o = o with { Downscale = Enum<ResampleKernel>(Value("--downscale"), "--downscale") };
                    break;

                case "--threads": o = o with { Threads = Number(Value("--threads"), "--threads", 1, 1024) }; break;
                case "--tile-size": o = o with { TileSize = Number(Value("--tile-size"), "--tile-size", 1, 8192) }; break;
                case "--quality": o = o with { Quality = Number(Value("--quality"), "--quality", 0, 100) }; break;
                case "--effort": o = o with { Effort = Number(Value("--effort"), "--effort", 0, 9) }; break;
                case "--zoom":
                {
                    var raw = Value("--zoom");
                    o = o with
                    {
                        MaxZoom = raw.Equals("native", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : Number(raw, "--zoom", 0, 12),
                    };
                    break;
                }

                default: throw new ArgumentException($"unknown option '{tokens[i]}'");
            }
        }

        return o with { AesKey = ReadAesKey(aes) };
    }

    private static string? ConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--config") continue;
            var path = args[i + 1];
            if (!File.Exists(path)) throw new ArgumentException($"config file not found: {path}");
            return path;
        }

        if (File.Exists(DefaultConfig)) return DefaultConfig;
        var projectConfig = Path.Combine(ProjectRoot, DefaultConfig);
        return File.Exists(projectConfig) ? projectConfig : null;
    }

    /// <summary>
    /// Turns `key: value` pairs into the command line they stand for, so the yaml file and the
    /// flags go through exactly the same parsing. Keys are the long option names without the
    /// dashes; underscores are accepted for kebab-case.
    /// </summary>
    private static IEnumerable<string> FromYaml(string path)
    {
        var yaml = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object?>>(File.ReadAllText(path));
        if (yaml is null) yield break;

        foreach (var (key, value) in yaml)
        {
            if (value is null) continue;

            var name = "--" + key.Trim().TrimStart('-').Replace('_', '-');
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            if (Flags.Contains(name))
            {
                if (!bool.TryParse(text, out var enabled))
                    throw new ArgumentException($"{path}: {key} must be true or false, got '{text}'");
                if (enabled) yield return name;
                continue;
            }

            yield return name;
            yield return text;
        }
    }

    private static T Enum<T>(string value, string option) where T : struct, Enum
    {
        if (System.Enum.TryParse<T>(value, true, out var parsed)) return parsed;

        var names = System.Enum.GetNames<T>();
        var expected = names.Length <= 12 ? $", expected one of {string.Join(" ", names).ToLowerInvariant()}" : "";
        throw new ArgumentException($"unknown {option} '{value}'{expected}");
    }

    private static int Number(string value, string option, int min, int max) =>
        int.TryParse(value, out var number) && number >= min && number <= max
            ? number
            : throw new ArgumentException($"{option} must be a number between {min} and {max}, got '{value}'");

    private static string ReadAesKey(string keyOrFile)
    {
        var key = File.Exists(keyOrFile) ? File.ReadAllText(keyOrFile).Trim() : keyOrFile.Trim();
        if (!key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) key = "0x" + key;
        if (key.Length != 66)
            throw new ArgumentException($"AES key must be 32 bytes of hex, got {key.Length - 2} chars");
        return key;
    }
}
