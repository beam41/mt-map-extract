using CUE4Parse.UE4.Assets.Exports.Texture;
using MtExtract;
using Newtonsoft.Json.Linq;

namespace HeightmapExtractor;

/// <summary>
/// Scans, decodes, and stitches Jeju_World's Landscape elevation data into one combined
/// world-space map. UE5.5 World Partition splits every LandscapeComponent + its
/// HeightmapTexture across ~5738 per-cell _Generated_/*.umap packages, invisible to plain
/// uasset-only pak scans. The map is NOT one Landscape actor: it's composed of 5 separate
/// LandscapeGuids (a large southern landmass plus 4 smaller pieces, one of which is the
/// northern island) whose SectionBaseX/Y are only absolute *within* their own guid - they
/// must be placed by resolved world position (actor location + SectionBase*scale), not
/// grouped by guid, to reproduce the actual map. Full background:
/// .agents/knowledge/landscape-heightmap.md.
/// </summary>
internal static class LandscapeExtractor
{
    private const string GeneratedCellsPrefix = "MotorTown/Content/Maps/Jeju/Jeju_World/_Generated_/";
    private const string PersistentLevelPath = "MotorTown/Content/Maps/Jeju/Jeju_World";

    public sealed record Transform(double LocX, double LocY, double LocZ, double ScaleX, double ScaleY, double ScaleZ);

    public sealed record Map(
        int Width, int Height, ushort[] Heights, ushort RawMin, ushort RawMax,
        double OriginXCm, double OriginYCm, double QuadScaleCm, int ComponentCount, int LandscapeCount);

    private sealed record Component(
        string Guid, int SectionBaseX, int SectionBaseY, int ComponentSizeQuads,
        string PackagePath, int TextureExportIndex);

    /// <summary>
    /// Stitches every LandscapeComponent (optionally restricted to guids matching
    /// <paramref name="guidFilter"/>, a case-insensitive substring, and always skipping
    /// <paramref name="excludeGuids"/>) onto one canvas in world space. When
    /// <paramref name="originXCm"/>/<paramref name="originYCm"/>/<paramref name="sizeCm"/>
    /// are all null, the canvas is auto-fit tightly around the actual placed data (the
    /// landscapes' own SectionBaseX/Y + ComponentSizeQuads extents - the true native
    /// resolution, not an assumption); pass all three explicitly to instead align the
    /// canvas to a known external reference (e.g. the game's own minimap image bounds).
    /// </summary>
    public static Map Extract(AssetSource assets, string? guidFilter, IReadOnlyCollection<string> excludeGuids,
        double? originXCm, double? originYCm, double? sizeCm)
    {
        Console.WriteLine("resolving master Landscape actor transform(s)...");
        var masters = ResolveMasterTransforms(assets);
        Console.WriteLine($"  {masters.Count} found");

        var files = assets.Files(GeneratedCellsPrefix).Where(f => f.Extension == "umap").ToList();
        Console.WriteLine($"scanning {files.Count} World Partition cells for LandscapeComponents...");

        var components = new List<Component>();
        var scanned = 0;
        foreach (var file in files)
        {
            var package = assets.Package(file.PathWithoutExtension);
            if (package is not null) CollectComponents(package, guidFilter, components);
            scanned++;
            if (scanned % 1000 == 0 || scanned == files.Count)
                Console.Write($"\r  {scanned}/{files.Count} cells scanned   ");
        }
        Console.WriteLine();

        var guidCount = components.Select(c => c.Guid).Distinct().Count();
        Console.WriteLine($"{components.Count} components across {guidCount} landscape(s)");

        if (excludeGuids.Count > 0)
        {
            var before = components.Count;
            components = components
                .Where(c => !excludeGuids.Any(x => c.Guid.Contains(x, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            guidCount = components.Select(c => c.Guid).Distinct().Count();
            Console.WriteLine($"  excluded {before - components.Count} components matching " +
                              $"[{string.Join(", ", excludeGuids)}] -> {components.Count} across {guidCount} landscape(s)");
        }

        // "Gwangjin" and "Ara" sit entirely inside the main landmass ("Outback")'s bounding
        // box and spatially overlap real Outback terrain (they're patches, not separate
        // islands - see .agents/knowledge/landscape-heightmap.md). Stitch merges overlaps
        // by taking the higher elevation at every pixel - order-independent, fills
        // Outback's real gaps with another landscape's data (anything real beats an
        // unfilled 0) and never carves an implausible dip into terrain that's already
        // there. Every guid seen so far uses the same 200cm/quad scale - the canvas below
        // assumes one shared pixel grid across all of them. Fail loudly instead of
        // silently misplacing components if a future guid breaks that assumption.
        var scales = masters.Values.Select(t => (t.ScaleX, t.ScaleY)).Distinct().ToList();
        if (scales.Count > 1)
            throw new InvalidOperationException(
                $"landscapes use different quad scales ({string.Join(", ", scales)}) - " +
                "the single shared-canvas assumption in this extractor no longer holds");
        var quadScale = scales.Count == 1 ? scales[0].ScaleX : 200.0;

        double originX, originY, sizeXCm, sizeYCm;
        if (originXCm is null || originYCm is null || sizeCm is null)
        {
            (originX, originY, sizeXCm, sizeYCm) = ComputeTightBounds(components, masters, quadScale);
            Console.WriteLine($"auto-fit canvas from placed data: X:[{originX},{originX + sizeXCm}] " +
                              $"Y:[{originY},{originY + sizeYCm}] (--debug-auto-fit)");
        }
        else
        {
            (originX, originY, sizeXCm, sizeYCm) = (originXCm.Value, originYCm.Value, sizeCm.Value, sizeCm.Value);
        }

        var width = (int)Math.Round(sizeXCm / quadScale);
        var height = (int)Math.Round(sizeYCm / quadScale);
        Console.WriteLine($"canvas: {width}x{height} pixels ({quadScale}cm/quad) covering " +
                          $"X:[{originX},{originX + sizeXCm}) Y:[{originY},{originY + sizeYCm})");

        var (heights, min, max, placed) = Stitch(assets, components, masters, quadScale, originX, originY, width, height);
        Console.WriteLine($"  placed {placed}/{components.Count} components " +
                          $"({components.Count - placed} fell outside the canvas bounds)");

        return new Map(width, height, heights, min, max, originX, originY, quadScale, components.Count, guidCount);
    }

    /// <summary>The true native canvas: the tight world-space bounding box of every placed
    /// component's actual footprint, derived purely from SectionBaseX/Y + ComponentSizeQuads
    /// and each guid's resolved master transform - no assumed map size. Adds one quad-step
    /// to each span: a span of N quad-steps needs N+1 vertices to include both endpoints,
    /// or the last row/column of real vertices (the map's far edge) gets clipped.</summary>
    private static (double OriginX, double OriginY, double SizeX, double SizeY) ComputeTightBounds(
        List<Component> components, Dictionary<string, Transform> masters, double quadScale)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var c in components)
        {
            if (!masters.TryGetValue(c.Guid, out var t)) continue;
            var x0 = t.LocX + c.SectionBaseX * quadScale;
            var y0 = t.LocY + c.SectionBaseY * quadScale;
            var x1 = t.LocX + (c.SectionBaseX + c.ComponentSizeQuads) * quadScale;
            var y1 = t.LocY + (c.SectionBaseY + c.ComponentSizeQuads) * quadScale;
            if (x0 < minX) minX = x0;
            if (y0 < minY) minY = y0;
            if (x1 > maxX) maxX = x1;
            if (y1 > maxY) maxY = y1;
        }

        if (minX > maxX) throw new InvalidOperationException("no placeable components - nothing to bound");
        return (minX, minY, maxX - minX + quadScale, maxY - minY + quadScale);
    }

    public sealed record Tile(string Guid, int SectionBaseX, int SectionBaseY, int ComponentSizeQuads,
        ushort[] Heights, int Width, int Height);

    /// <summary>
    /// Decodes every matching LandscapeComponent's LOD0 heightmap texture as-is, with no
    /// placement/stitching math at all - the raw per-component tile, for inspecting the
    /// source data independent of any world-transform assumptions.
    /// </summary>
    public static IEnumerable<Tile> ExtractTiles(AssetSource assets, string? guidFilter)
    {
        var files = assets.Files(GeneratedCellsPrefix).Where(f => f.Extension == "umap").ToList();
        Console.WriteLine($"scanning {files.Count} World Partition cells for LandscapeComponents...");

        var components = new List<Component>();
        var scanned = 0;
        foreach (var file in files)
        {
            var package = assets.Package(file.PathWithoutExtension);
            if (package is not null) CollectComponents(package, guidFilter, components);
            scanned++;
            if (scanned % 1000 == 0 || scanned == files.Count)
                Console.Write($"\r  {scanned}/{files.Count} cells scanned   ");
        }
        Console.WriteLine();
        Console.WriteLine($"{components.Count} components found; decoding LOD0 heightmap textures...");

        foreach (var c in components)
        {
            var pkg = assets.RequirePackage(c.PackagePath);
            if (pkg.Exports[c.TextureExportIndex] is not UTexture2D texture) continue;
            var decoded = DecodeHeightmapTexture(texture);
            if (decoded is null) continue;
            var (data, w, h) = decoded.Value;
            yield return new Tile(c.Guid, c.SectionBaseX, c.SectionBaseY, c.ComponentSizeQuads, data, w, h);
        }
    }

    private static void CollectComponents(PackageJson package, string? guidFilter, List<Component> into)
    {
        for (var i = 0; i < package.Exports.Count; i++)
        {
            if (package.Exports[i].ExportType != "LandscapeComponent") continue;

            var comp = package.Json(i);
            var props = comp["Properties"] as JObject;
            var ownerIdx = ResolveIndex((string?)comp["Outer"]?["ObjectPath"]);
            if (ownerIdx is null) continue;

            var owner = package.Json(ownerIdx.Value)["Properties"] as JObject;
            var guid = (string?)owner?["LandscapeGuid"] ?? "";
            if (guidFilter is not null && !guid.Contains(guidFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var texIdx = ResolveIndex((string?)(props?["HeightmapTexture"] as JObject)?["ObjectPath"]);
            if (texIdx is null) continue;

            into.Add(new Component(guid,
                (int?)props?["SectionBaseX"] ?? 0, (int?)props?["SectionBaseY"] ?? 0,
                (int?)props?["ComponentSizeQuads"] ?? 0,
                package.Package.Name, texIdx.Value));
        }
    }

    /// <summary>
    /// Every LandscapeStreamingProxy's own RootComponent.RelativeLocation is a WP
    /// streaming/culling placeholder, not the terrain's world origin - it varies between
    /// proxies of the same landscape even though RelativeScale3D is uniform. The true,
    /// single world transform lives on the master "Landscape" actor in the persistent
    /// level that every proxy's LandscapeActorRef points back to, keyed by LandscapeGuid.
    /// This is also what lets components from different guids share one world-space canvas:
    /// each guid's own SectionBaseX/Y is only meaningful relative to its own master transform.
    /// </summary>
    private static Dictionary<string, Transform> ResolveMasterTransforms(AssetSource assets)
    {
        var result = new Dictionary<string, Transform>();
        var worldPkg = assets.Package(PersistentLevelPath);
        if (worldPkg is null) return result;

        for (var i = 0; i < worldPkg.Exports.Count; i++)
        {
            if (worldPkg.Exports[i].ExportType != "Landscape") continue;

            var props = worldPkg.Json(i)["Properties"] as JObject;
            var guid = (string?)props?["LandscapeGuid"] ?? "";
            var rcIdx = ResolveIndex((string?)(props?["RootComponent"] as JObject)?["ObjectPath"]);
            var rc = rcIdx is not null ? worldPkg.Json(rcIdx.Value)["Properties"] as JObject : null;
            var loc = rc?["RelativeLocation"];
            var scale = rc?["RelativeScale3D"];

            result[guid] = new Transform(
                (double?)loc?["X"] ?? 0, (double?)loc?["Y"] ?? 0, (double?)loc?["Z"] ?? 0,
                (double?)scale?["X"] ?? 1, (double?)scale?["Y"] ?? 1, (double?)scale?["Z"] ?? 1);
        }

        return result;
    }

    private static (ushort[] Heights, ushort Min, ushort Max, int Placed) Stitch(
        AssetSource assets, List<Component> components, Dictionary<string, Transform> masters,
        double quadScale, double originXCm, double originYCm, int width, int height)
    {
        var heights = new ushort[(long)width * height];
        ushort min = ushort.MaxValue, max = 0;
        var placed = 0;
        var originQuadX = originXCm / quadScale;
        var originQuadY = originYCm / quadScale;

        foreach (var c in components)
        {
            if (!masters.TryGetValue(c.Guid, out var t)) continue; // no master actor -> unplaceable in world space

            // World position of this component's (0,0) vertex, in quads on the shared canvas
            // grid: the guid's own actor origin (also in quads) plus its local SectionBase.
            var quadX = t.LocX / quadScale + c.SectionBaseX;
            var quadY = t.LocY / quadScale + c.SectionBaseY;
            var ox = (int)Math.Round(quadX - originQuadX);
            var oy = (int)Math.Round(quadY - originQuadY);
            if (ox + c.ComponentSizeQuads < 0 || oy + c.ComponentSizeQuads < 0 || ox >= width || oy >= height) continue;

            var pkg = assets.RequirePackage(c.PackagePath);
            if (pkg.Exports[c.TextureExportIndex] is not UTexture2D texture) continue;
            var decoded = DecodeHeightmapTexture(texture);
            if (decoded is null) continue;
            var (data, texW, texH) = decoded.Value;

            placed++;
            for (var y = 0; y < texH; y++)
            {
                var py = oy + y;
                if (py < 0 || py >= height) continue;
                var rowBase = (long)py * width;
                for (var x = 0; x < texW; x++)
                {
                    var px = ox + x;
                    if (px < 0 || px >= width) continue;
                    var idx = rowBase + px;
                    var h = data[y * texW + x];
                    // Merge overlapping landscapes by taking the higher elevation at every
                    // pixel - order-independent, and correct either way an overlap is
                    // resolved: it fills a landscape's real gaps with another's data
                    // (anything real beats an unfilled 0) without ever carving an
                    // implausible dip into terrain that another landscape already placed.
                    if (h > heights[idx]) heights[idx] = h;
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }
        }

        return (heights, min, max, placed);
    }

    /// <summary>
    /// Decodes a landscape Heightmap Texture2D's LOD0 mip directly from raw bytes: PF_B8G8R8A8,
    /// per-pixel memory order B,G,R,A (CUE4Parse treats this as a pass-through raw format, no
    /// CUE4Parse-Conversion/SkiaSharp needed). height = R&lt;&lt;8 | G. Verified against the
    /// engine-computed LandscapeComponent.CachedLocalBox.Z on a real component: localZ =
    /// (height - 32768) / 128.0 matched to 5 decimal places. GetFirstMip() transparently
    /// decompresses ULandscapeTextureStorageProviderFactory-backed mips (some UE5 games store
    /// heightmaps behind a virtualized/compressed mip provider) - never read
    /// PlatformData.Mips[n].BulkData directly.
    /// </summary>
    private static (ushort[] Data, int Width, int Height)? DecodeHeightmapTexture(UTexture2D texture)
    {
        var mip = texture.GetFirstMip();
        var raw = mip?.BulkData?.Data;
        if (mip is null || raw is null) return null;

        var data = new ushort[mip.SizeX * mip.SizeY];
        for (var y = 0; y < mip.SizeY; y++)
        {
            var srcBase = y * mip.SizeX * 4;
            var dstBase = y * mip.SizeX;
            for (var x = 0; x < mip.SizeX; x++)
            {
                var s = srcBase + x * 4;
                data[dstBase + x] = (ushort)((raw[s + 2] << 8) | raw[s + 1]);
            }
        }

        return (data, mip.SizeX, mip.SizeY);
    }

    private static int? ResolveIndex(string? objectPath)
    {
        if (objectPath is null) return null;
        var dot = objectPath.LastIndexOf('.');
        return dot >= 0 && int.TryParse(objectPath[(dot + 1)..], out var idx) ? idx : null;
    }
}
