using CUE4Parse.UE4.Versions;

namespace HeightmapExtractor;

/// <summary>CLI options for the landscape heightmap extractor. See
/// .agents/knowledge/landscape-heightmap.md for the data-format background.</summary>
internal sealed record Options
{
    public const string Usage = """
        Usage: dotnet run -c Release --project heightmap -- [options]

        Stitches Jeju_World's Landscape elevation data (split across ~5738 World Partition
        cells and 5 separate LandscapeGuids) into one combined heightmap at native
        resolution (one pixel per quad, no resampling), covering a fixed real-world extent
        matching the game's own map. Writes to out/heightmap/:

          Jeju_World_heightmap16.png   16-bit grayscale, full native resolution
          Jeju_World.json              resolution, origin, quad scale, formulas
          heights.bin                  raw uint16, little-endian, row-major, native
                                       resolution - seek to (row * width + col) * 2 bytes
                                       for a single point query, no PNG/zlib decode needed
          tiles/{z}_{x}_{y}.bin        Leaflet/OpenLayers-style height tile pyramid,
                                       z0..--max-zoom, same {z}_{x}_{y} naming and
                                       zoom-to-resolution scheme as amc-web's color
                                       tiles - for script/terrain-viewer's LOD renderer
          debug/Jeju_World_preview.png 8-bit min/max-normalized, downscaled to
                                       --debug-size - the 16-bit file looks almost solid
                                       black in ordinary viewers otherwise
          debug/tiles/                 only with --debug-tiles, see below

        Options:
          --pak <file>       pak to read              (default resource/MotorTown-Windows.pak)
          --aes <key|file>   AES key, hex or a file containing it
                                                      (default resource/aes)
          --usmap <file>     type mappings            (default resource/Mappings.usmap)
          --game <EGame>     UE version               (default GAME_UE5_5)
          --out <dir>        output directory         (default out/heightmap)
          --origin-x <cm>    world X of the map's left edge
                                                      (default -1280000)
          --origin-y <cm>    world Y of the map's top edge
                                                      (default -320000)
          --map-size <cm>    map width and height, in world cm (must cover both islands
                             plus the ocean between them to match the game's own map)
                                                      (default 2200000, i.e. 22km square)
          --tile-size <px>   height tile raw-sample resolution (independent of
                             amc-web's own --tile-size - same {z}_{x}_{y} grid/zoom
                             layout either way, just more or less height detail per
                             tile; each tile file is actually (tile-size+3) samples per
                             edge - a 1px border overlap so adjacent tiles share their
                             boundary pixel exactly, plus a 1px normal halo so they also
                             agree on lighting normals at that edge - see
                             landscape-heightmap.md)
                                                      (default 256)
          --max-zoom <n>     highest height tile zoom (tiles are 0..n) - kept in sync
                             with amc-web's own native zoom (4, for its 4096px map at
                             the default tile size); never generates an upscaled level,
                             unlike amc-web's default - upscaling raw elevation invents
                             no real detail
                                                      (default 4)
          --exclude-guid <substring>
                             never place components whose LandscapeGuid contains this
                             (case-insensitive); repeatable. Replaces the default exclusion
                             list on first use - pass --exclude-guid "" to include everything
                             (default: "028DB6A7", "OlleSpeedway_Landscape" - confirmed
                             against the live game that it never actually loads in play)

        Debug options (not needed for normal generation):
          --debug-guid <substring>
                             only place components whose LandscapeGuid contains this
                             (case-insensitive) - isolates one landscape for inspection
          --debug-size <px>  resolution of debug/Jeju_World_preview.png (longer edge;
                             the other edge is scaled to preserve aspect ratio)
                                                      (default 2048)
          --debug-auto-fit   ignore --origin-x/--origin-y/--map-size; auto-fit the canvas
                             tightly to the actual placed data instead (the true native
                             bounding box, not a fixed real-world extent - see
                             landscape-heightmap.md)
          --debug-tiles      skip stitching entirely; dump every matching component's raw
                             LOD0 heightmap texture as its own 16-bit PNG (no placement or
                             world-transform math at all), to out/heightmap/debug/tiles/
          -h, --help         this help
        """;

    public string PakPath { get; private init; } = Path.Combine("resource", "MotorTown-Windows.pak");
    public string AesKey { get; private init; } = "";
    public string UsmapPath { get; private init; } = Path.Combine("resource", "Mappings.usmap");
    public EGame Game { get; private init; } = EGame.GAME_UE5_5;

    /// <summary>The repo root: bin/Release/net10.0 -> heightmap -> repo root. The consolidated
    /// out/ tree lives here, split by project (out/amc-web, out/heightmap, ...).</summary>
    public static string RepoRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private string? OutDirPath { get; init; }
    public string OutDir => OutDirPath ?? Path.Combine(RepoRoot, "out", "heightmap");

    private IReadOnlyList<string>? ExcludeGuidsOverride { get; init; }
    /// <summary>Excludes "OlleSpeedway_Landscape" by default - confirmed against the live
    /// game that this landscape never actually loads in play, unlike the other 4. Not a
    /// heuristic guess: user-verified ground truth. See --exclude-guid usage text.</summary>
    public IReadOnlyList<string> ExcludeGuids => ExcludeGuidsOverride ?? ["028DB6A7"];

    private double? OriginXCmOverride { get; init; }
    private double? OriginYCmOverride { get; init; }
    private double? MapSizeCmOverride { get; init; }
    /// <summary>The game's known real-world map extent (both islands + the ocean between
    /// them), null'd out by --debug-auto-fit instead of used directly.</summary>
    public double OriginXCm => OriginXCmOverride ?? -1_280_000;
    public double OriginYCm => OriginYCmOverride ?? -320_000;
    public double MapSizeCm => MapSizeCmOverride ?? 2_200_000;

    public int TileSize { get; private init; } = 256;
    public int MaxZoom { get; private init; } = 4;
    public string? DebugGuidFilter { get; private init; }
    public int DebugSize { get; private init; } = 2048;
    public bool DebugAutoFit { get; private init; }
    public bool DebugTiles { get; private init; }
    public bool ShowHelp { get; private init; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        var aes = Path.Combine("resource", "aes");

        for (var i = 0; i < args.Length; i++)
        {
            string Value(string name) => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"{name} needs a value");

            switch (args[i])
            {
                case "--pak": o = o with { PakPath = Value("--pak") }; break;
                case "--aes": aes = Value("--aes"); break;
                case "--usmap": o = o with { UsmapPath = Value("--usmap") }; break;
                case "--game": o = o with { Game = ParseGame(Value("--game")) }; break;
                case "--out": o = o with { OutDirPath = Value("--out") }; break;
                case "--origin-x": o = o with { OriginXCmOverride = Double(Value("--origin-x"), "--origin-x") }; break;
                case "--origin-y": o = o with { OriginYCmOverride = Double(Value("--origin-y"), "--origin-y") }; break;
                case "--map-size": o = o with { MapSizeCmOverride = Double(Value("--map-size"), "--map-size") }; break;
                case "--tile-size": o = o with { TileSize = Number(Value("--tile-size"), "--tile-size", 16, 8192) }; break;
                case "--max-zoom": o = o with { MaxZoom = Number(Value("--max-zoom"), "--max-zoom", 0, 12) }; break;
                case "--exclude-guid":
                {
                    var v = Value("--exclude-guid");
                    var current = o.ExcludeGuidsOverride ?? [];
                    o = o with { ExcludeGuidsOverride = v.Length == 0 ? [] : [.. current, v] };
                    break;
                }
                case "--debug-guid": o = o with { DebugGuidFilter = Value("--debug-guid") }; break;
                case "--debug-size": o = o with { DebugSize = Number(Value("--debug-size"), "--debug-size", 16, 32768) }; break;
                case "--debug-auto-fit": o = o with { DebugAutoFit = true }; break;
                case "--debug-tiles": o = o with { DebugTiles = true }; break;
                case "-h" or "--help": o = o with { ShowHelp = true }; break;
                default: throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        return o with { AesKey = ReadAesKey(aes) };
    }

    private static EGame ParseGame(string value)
    {
        if (Enum.TryParse<EGame>(value, true, out var parsed)) return parsed;
        throw new ArgumentException($"unknown --game '{value}'");
    }

    private static int Number(string value, string option, int min, int max) =>
        int.TryParse(value, out var number) && number >= min && number <= max
            ? number
            : throw new ArgumentException($"{option} must be a number between {min} and {max}, got '{value}'");

    private static double Double(string value, string option) =>
        double.TryParse(value, out var number)
            ? number
            : throw new ArgumentException($"{option} must be a number, got '{value}'");

    private static string ReadAesKey(string keyOrFile)
    {
        var key = File.Exists(keyOrFile) ? File.ReadAllText(keyOrFile).Trim() : keyOrFile.Trim();
        if (!key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) key = "0x" + key;
        if (key.Length != 66)
            throw new ArgumentException($"AES key must be 32 bytes of hex, got {key.Length - 2} chars");
        return key;
    }
}
