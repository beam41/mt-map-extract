using System.Collections.Concurrent;
using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Meshes.PSK;
using CUE4Parse_Conversion.Textures;
using MtExtract;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;

namespace HouseServer;

using VertexBuilder = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;
using MeshBuilder = MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

/// <summary>
/// Writes a building's <see cref="MeshPart"/>s as one GLB in glTF space: meters, right-handed,
/// Y-up. Unreal (cm, left-handed, Z-up) maps through <see cref="UeToGltf"/> — X stays, UE Z
/// becomes Y, UE Y becomes Z. The mirror cancels UE's opposite front-face convention, so
/// CUE4Parse's index order is kept as-is (swapping corners renders every mesh inside out).
/// Meshes are shared between instances; base-color textures are embedded as PNG.
/// </summary>
public sealed class GlbExporter(AssetSource assets, ILogger<GlbExporter> log, int maxTextureSize)
{
    private const float CmToM = 0.01f;

    /// <summary>Row-vector basis change UE → glTF.</summary>
    public static readonly Matrix4x4 UeToGltf = new(
        CmToM, 0, 0, 0,
        0, 0, CmToM, 0,
        0, CmToM, 0, 0,
        0, 0, 0, 1);

    private static readonly Matrix4x4 GltfToUe = Matrix4x4.Invert(UeToGltf, out var inv) ? inv : throw new InvalidOperationException();

    private readonly ConcurrentDictionary<string, MemoryImage?> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Not thread-safe (the pak provider isn't) — callers serialize.</summary>
    public byte[] Export(string name, IReadOnlyList<MeshPart> parts)
    {
        var scene = new SceneBuilder(name);
        var meshes = new Dictionary<string, MeshBuilder?>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<string, MaterialBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var key = part.MeshPath + "|" + string.Join(",", part.MaterialOverrides);
            if (!meshes.TryGetValue(key, out var mesh))
                meshes[key] = mesh = BuildMesh(part, materials);
            if (mesh is null) continue;

            scene.AddRigidMesh(mesh, GltfToUe * part.Transform * UeToGltf);
        }

        return scene.ToGltf2().WriteGLB().ToArray();
    }

    private MeshBuilder? BuildMesh(MeshPart part, Dictionary<string, MaterialBuilder> materials)
    {
        UStaticMesh staticMesh;
        try
        {
            staticMesh = assets.Provider.LoadPackageObject<UStaticMesh>(part.MeshPath);
        }
        catch (Exception e)
        {
            log.LogWarning("mesh {Mesh} failed to load: {Error}", part.MeshPath, e.Message);
            return null;
        }

        if (!staticMesh.TryConvert(out var converted) || converted.LODs.Count == 0)
        {
            log.LogWarning("mesh {Mesh} failed to convert", part.MeshPath);
            return null;
        }

        using (converted)
        {
            var lod = converted.LODs[0];
            var verts = lod.Verts;
            var indices = lod.Indices.Value;
            var builder = new MeshBuilder(staticMesh.Name);

            foreach (var section in lod.Sections.Value)
            {
                if (!section.IsValid) continue;

                var materialPath = section.MaterialIndex < part.MaterialOverrides.Count
                    ? part.MaterialOverrides[section.MaterialIndex]
                    : null;
                var material = materialPath is not null
                    ? Load<UMaterialInterface>(materialPath)
                    : section.Material?.Load<UMaterialInterface>();

                var primitive = builder.UsePrimitive(Material(material, materials));
                for (var face = 0; face < section.NumFaces; face++)
                {
                    var i = section.FirstIndex + face * 3;
                    primitive.AddTriangle(Vertex(verts[indices[i]]), Vertex(verts[indices[i + 1]]), Vertex(verts[indices[i + 2]]));
                }
            }

            return builder;
        }
    }

    private static VertexBuilder Vertex(CMeshVertex v)
    {
        var position = new Vector3(v.Position.X, v.Position.Z, v.Position.Y) * CmToM;
        var normal = new Vector3(v.Normal.X, v.Normal.Z, v.Normal.Y);
        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
        return new VertexBuilder(new VertexPositionNormal(position, normal), new VertexTexture1(new Vector2(v.UV.U, v.UV.V)));
    }

    private MaterialBuilder Material(UMaterialInterface? material, Dictionary<string, MaterialBuilder> cache)
    {
        var key = material?.GetPathName() ?? "";
        if (cache.TryGetValue(key, out var cached)) return cached;

        var builder = new MaterialBuilder(material?.Name ?? "default").WithMetallicRoughnessShader()
            .WithMetallicRoughness(0, 0.8f);
        if (material is not null)
        {
            var parameters = new CMaterialParams2();
            material.GetParams(parameters, EMaterialFormat.AllLayers);

            var tint = DiffuseTint(parameters) is { } color ? new Vector4(color.R, color.G, color.B, 1) : Vector4.One;

            var texture = DiffuseTexture(parameters);
            var image = texture is not null ? Png(texture) : null;

            if (parameters.IsTranslucent)
            {
                tint.W = 0.35f;
                builder.WithAlpha(AlphaMode.BLEND, 0.5f);
            }

            if (image is { } png) builder.WithBaseColor(png, tint);
            else builder.WithBaseColor(tint);
        }
        else
        {
            builder.WithBaseColor(new Vector4(0.8f, 0.8f, 0.8f, 1));
        }

        return cache[key] = builder;
    }

    /// <summary>CUE4Parse's known tint names, then any color named like a base-color tint —
    /// MotorTown's MI_Matte* family is a white texture times <c>BaseColor_Tint</c> (linear, as glTF wants).</summary>
    private static FLinearColor? DiffuseTint(CMaterialParams2 parameters)
    {
        foreach (var names in CMaterialParams2.DiffuseColors)
        {
            if (parameters.TryGetLinearColor(out var known, names)) return known;
        }
        foreach (var (name, color) in parameters.Colors)
        {
            if (name.Contains("BaseColor", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Albedo", StringComparison.OrdinalIgnoreCase))
                return color;
        }
        return null;
    }

    private static readonly string[] NonColorHints = ["emissive", "normal", "rough", "metal", "mask", "orm", "ao", "height", "opacity"];

    /// <summary>CUE4Parse's known diffuse names first (every group, then its fallback), then any
    /// parameter named like base color (<c>PM_Diffuse</c> on Synty parent materials such as
    /// Mat_PolygonShops_01_A, <c>BaseTexture</c> on <c>M_GenericBase</c>), then the first texture
    /// that isn't obviously a non-color map.</summary>
    private static UTexture? DiffuseTexture(CMaterialParams2 parameters)
    {
        foreach (var names in CMaterialParams2.Diffuse)
        {
            if (parameters.TryGetTexture2d(out var known, names)) return known;
        }
        if (parameters.TryGetTexture2d(out var fallback, CMaterialParams2.FallbackDiffuse)) return fallback;

        static bool Has(string name, params string[] hints) =>
            hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));

        var textures = parameters.Textures.Where(t => t.Value is UTexture).ToList();
        var byName = textures.FirstOrDefault(t => Has(t.Key, "diffuse", "basecolor", "base_color", "basetexture", "albedo"));
        if (byName.Value is UTexture named) return named;
        return textures.FirstOrDefault(t => !Has(t.Key, NonColorHints) && !Has(t.Value.Name, NonColorHints)).Value as UTexture;
    }

    /// <summary>A standalone texture as PNG (downscaled to the max texture size). Not
    /// thread-safe, like <see cref="Export"/>.</summary>
    public byte[]? TexturePng(string texturePath) =>
        Load<UTexture>(texturePath) is { } texture ? Png(texture)?.Content.ToArray() : null;

    private MemoryImage? Png(UTexture texture) => _textures.GetOrAdd(texture.GetPathName(), _ =>
    {
        try
        {
            var decoded = texture.Decode(maxTextureSize, assets.Provider.Versions.Platform);
            var bytes = decoded?.Encode(ETextureFormat.Png, false, out _);
            return bytes is null ? (MemoryImage?)null : new MemoryImage(bytes);
        }
        catch (Exception e)
        {
            log.LogWarning("texture {Texture} failed to decode: {Error}", texture.Name, e.Message);
            return null;
        }
    });

    private T? Load<T>(string path) where T : CUE4Parse.UE4.Assets.Exports.UObject
    {
        try
        {
            return assets.Provider.LoadPackageObject<T>(path);
        }
        catch (Exception e)
        {
            log.LogWarning("{Path} failed to load: {Error}", path, e.Message);
            return null;
        }
    }
}
