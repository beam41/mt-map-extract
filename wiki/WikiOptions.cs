using MtExtract;

namespace WikiGenerate;

/// <summary>The wiki generator's own mount options. The amc-web Options type is not shared —
/// each project parses its own CLI (pak/aes/usmap paths here, tile options there).</summary>
internal sealed record WikiOptions(string PakPath, string AesKey, string UsmapPath, CUE4Parse.UE4.Versions.EGame Game)
{
    public static WikiOptions Parse(string[] args)
    {
        var pak = Path.Combine("resource", "MotorTown-Windows.pak");
        var aes = Path.Combine("resource", "aes");
        var usmap = Path.Combine("resource", "Mappings.usmap");
        var game = CUE4Parse.UE4.Versions.EGame.GAME_UE5_5;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pak": pak = args[++i]; break;
                case "--aes": aes = args[++i]; break;
                case "--usmap": usmap = args[++i]; break;
                case "--game": game = Enum.Parse<CUE4Parse.UE4.Versions.EGame>(args[++i]); break;
                case "--help" or "-h": showHelp = true; break;
                default: throw new ArgumentException($"unknown option: {args[i]}");
            }
        }

        return new WikiOptions(pak, File.Exists(aes) ? File.ReadAllText(aes).Trim() : aes, usmap, game) { ShowHelp = showHelp };
    }

    public bool ShowHelp { get; init; }

    public PakOptions Pak => new(PakPath, AesKey, UsmapPath, Game);
}
