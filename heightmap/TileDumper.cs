using NetVips;
using Newtonsoft.Json.Linq;
using MtExtract;

namespace HeightmapExtractor;

/// <summary>Writes every LandscapeComponent's decoded LOD0 heightmap texture as its own
/// 16-bit PNG, unstitched - no placement/world-transform math, pure ground truth per tile.</summary>
internal static class TileDumper
{
    public static void DumpAll(AssetSource assets, string? guidFilter, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var manifest = new JArray();
        var count = 0;

        foreach (var tile in LandscapeExtractor.ExtractTiles(assets, guidFilter))
        {
            var guidShort = tile.Guid.Length >= 8 ? tile.Guid[..8] : tile.Guid;
            var fileName = $"{guidShort}_{tile.SectionBaseX}_{tile.SectionBaseY}.png";
            var path = Path.Combine(outDir, fileName);

            using var image = Image.NewFromMemory(tile.Heights, tile.Width, tile.Height, 1,
                Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Grey16);
            image.Pngsave(path, compression: 6);

            manifest.Add(new JObject
            {
                ["file"] = fileName,
                ["landscapeGuid"] = tile.Guid,
                ["sectionBaseX"] = tile.SectionBaseX,
                ["sectionBaseY"] = tile.SectionBaseY,
                ["componentSizeQuads"] = tile.ComponentSizeQuads,
                ["textureWidth"] = tile.Width,
                ["textureHeight"] = tile.Height,
            });
            count++;
            if (count % 100 == 0) Console.Write($"\r  {count} tiles written   ");
        }
        Console.WriteLine($"\r  {count} tiles written   ");

        Output.Write(Path.Combine(outDir, "manifest.json"), manifest.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"  wrote manifest.json ({count} entries)");
    }
}
