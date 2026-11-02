using System.Numerics;
using MtExtract;
using Newtonsoft.Json.Linq;

namespace HouseServer;

/// <summary>One static mesh placed inside a building actor, in Unreal space (cm, left-handed
/// Z-up) relative to the actor's root. <see cref="Transform"/> is row-vector (UE/System.Numerics
/// convention: <c>v' = v * M</c>).</summary>
public sealed record MeshPart(string MeshPath, Matrix4x4 Transform, IReadOnlyList<string?> MaterialOverrides);

/// <summary>
/// Turns a <c>Buildings</c> row (building key + step) into the flat list of static meshes the
/// game would spawn: the step's actor blueprint's components (native CDO subobjects, parent and
/// own SimpleConstructionScript nodes with InheritableComponentHandler overrides, child actors
/// recursively), plus the row's per-component mesh overrides (how every furniture row reuses the
/// generic <c>_Common_Prop</c> actor).
/// </summary>
public sealed class BuildingAssembler(AssetSource assets)
{
    private const string BuildingsTable = "MotorTown/Content/DataAsset/Buildings/Buildings";
    private const int MaxChildActorDepth = 4;

    private JObject? _rows;

    public JObject? Row(string buildingKey)
    {
        _rows ??= assets.RequirePackage(BuildingsTable).First()["Rows"] as JObject ?? [];
        return _rows.Properties()
            .FirstOrDefault(p => string.Equals(p.Name, buildingKey, StringComparison.OrdinalIgnoreCase))
            ?.Value as JObject;
    }

    public string? RowType(string buildingKey) => (string?)Row(buildingKey)?["BuildingRowType"];

    public List<MeshPart>? Assemble(string buildingKey, int step)
    {
        var steps = Row(buildingKey)?["Steps"] as JArray;
        if (steps is null || steps.Count == 0) return null;
        var stepRow = steps[Math.Clamp(step, 0, steps.Count - 1)];

        var actorClass = AssetPackage((string?)stepRow["ActorClass"]?["AssetPathName"]);
        if (actorClass is null) return null;

        var meshOverrides = (stepRow["StaticMeshes"] as JArray ?? [])
            .Select(e => ((string?)e["Key"], AssetObjectPath((string?)e["Value"]?["AssetPathName"])))
            .Where(e => e.Item1 is not null && e.Item2 is not null)
            .ToDictionary(e => e.Item1!, e => e.Item2!, StringComparer.OrdinalIgnoreCase);
        var meshScale = Vec(stepRow["MeshScale"], Vector3.One);
        var rowMaterial = AssetObjectPath((string?)stepRow["Material"]?["AssetPathName"]);
        var childActor = AssetPackage((string?)stepRow["ChildActorClass"]?["AssetPathName"]);

        var parts = new List<MeshPart>();
        CollectActor(actorClass.Value.Package, actorClass.Value.ClassName, Matrix4x4.Identity, parts, 0,
            new RowOverrides(meshOverrides, meshScale, rowMaterial, childActor));
        return parts;
    }

    /// <summary>What a Buildings row swaps into its generic actor: <c>_Common_Prop</c> takes
    /// meshes by component name, <c>_Common_ChildActor</c> takes the child actor class.</summary>
    private sealed record RowOverrides(Dictionary<string, string> Meshes, Vector3 MeshScale, string? Material,
        (string Package, string ClassName)? ChildActor);

    private sealed class Component
    {
        public required string Name;
        public required JObject Props;
        public required string Type;
        public string? Parent;
    }

    private void CollectActor(string package, string className, Matrix4x4 actorWorld, List<MeshPart> parts,
        int depth, RowOverrides? row, JObject? childActorTemplate = null)
    {
        if (depth > MaxChildActorDepth) return;

        var components = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var actorProps = new JObject();
        string? root = null;
        CollectClassComponents(package, className, components, actorProps, ref root);
        if (childActorTemplate is not null) actorProps.Merge(childActorTemplate);
        ApplyMeshVariables(actorProps, components);

        // The row's MeshScale is set as the overridden component's relative scale, replacing the
        // template's (_Common_Text_01's mesh is already 3x and its rows say 3x: 3x in game, not 9x).
        if (row is not null)
        {
            foreach (var c in components.Values.Where(c => row.Meshes.ContainsKey(c.Name)))
            {
                c.Props = (JObject)c.Props.DeepClone();
                c.Props["RelativeScale3D"] = new JObject { ["X"] = row.MeshScale.X, ["Y"] = row.MeshScale.Y, ["Z"] = row.MeshScale.Z };
            }
        }

        var worlds = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        Matrix4x4 World(Component c, int guard)
        {
            if (worlds.TryGetValue(c.Name, out var cached)) return cached;
            // The root component's relative transform is replaced by the actor's spawn transform.
            var local = string.Equals(c.Name, root, StringComparison.OrdinalIgnoreCase)
                ? Matrix4x4.Identity
                : Relative(c.Props);
            var parent = c.Parent is not null && guard < 64 && components.TryGetValue(c.Parent, out var p)
                ? World(p, guard + 1)
                : actorWorld;
            return worlds[c.Name] = local * parent;
        }

        foreach (var c in components.Values)
        {
            if (Hidden(c.Props)) continue;

            if (c.Type.EndsWith("StaticMeshComponent", StringComparison.Ordinal))
            {
                var overridden = row is not null && row.Meshes.TryGetValue(c.Name, out _);
                var mesh = overridden ? row!.Meshes[c.Name] : ObjectPackageRef(c.Props["StaticMesh"]);
                if (mesh is null) continue;
                var world = World(c, 0);

                var materials = (c.Props["OverrideMaterials"] as JArray ?? [])
                    .Select(m => ObjectPackageRef(m)).ToList();
                if (overridden && row!.Material is not null) materials = [row.Material];

                foreach (var instance in Instances(c.Props))
                    parts.Add(new MeshPart(mesh, instance * world, materials));
            }
            else if (c.Type == "ChildActorComponent" && ChildActorClass(c, row) is { } child)
            {
                // A row-swapped child class doesn't match the blueprint's per-instance template.
                var template = row?.ChildActor is null && Ref(c.Props["ChildActorTemplate"]) is { } t ? Props(Json(t)) : null;
                CollectActor(child.Package, child.ClassName, World(c, 0), parts, depth + 1, null, template);
            }
        }
    }

    /// <summary>
    /// Actor-level <c>StaticMesh</c> variables are fed to the mesh component by the construction
    /// script (<c>_Deprecated_BaseDoor</c>: <c>LeftDoor.SetStaticMesh(DoorMesh)</c>, with DoorMesh
    /// set per house through the ChildActorTemplate). Bytecode isn't interpreted, so this only
    /// applies when the target is unambiguous: one variable, one mesh component.
    /// </summary>
    private static void ApplyMeshVariables(JObject actorProps, Dictionary<string, Component> components)
    {
        var meshVariables = actorProps.Properties()
            .Where(p => p.Value is JObject o && ((string?)o["ObjectName"])?.StartsWith("StaticMesh'", StringComparison.Ordinal) == true)
            .ToList();
        var meshComponents = components.Values
            .Where(c => c.Type.EndsWith("StaticMeshComponent", StringComparison.Ordinal)).ToList();
        if (meshVariables.Count != 1 || meshComponents.Count != 1) return;

        var target = meshComponents[0];
        target.Props = (JObject)target.Props.DeepClone();
        target.Props["StaticMesh"] = meshVariables[0].Value.DeepClone();
    }

    private (string Package, string ClassName)? ChildActorClass(Component c, RowOverrides? row)
    {
        if (row?.ChildActor is { } fromRow) return fromRow;
        if (Ref(c.Props["ChildActorClass"]) is not { } child || child.Package.StartsWith("/Script", StringComparison.Ordinal))
            return null;
        return ClassNameAt(child.Package, child.Index) is { } name ? (child.Package, name) : null;
    }

    /// <summary>Instanced static mesh components carry their transforms per instance; plain
    /// components are a single identity instance.</summary>
    private static IEnumerable<Matrix4x4> Instances(JObject props)
    {
        if (props["PerInstanceSMData"] is not JArray data) return [Matrix4x4.Identity];
        return data.Select(d => d["TransformData"]).OfType<JObject>().Select(m => MatrixFromJson(m));
    }

    private static Matrix4x4 MatrixFromJson(JObject m)
    {
        float F(string plane, string axis) => (float?)m[plane]?[axis] ?? 0;
        return new Matrix4x4(
            F("XPlane", "X"), F("XPlane", "Y"), F("XPlane", "Z"), F("XPlane", "W"),
            F("YPlane", "X"), F("YPlane", "Y"), F("YPlane", "Z"), F("YPlane", "W"),
            F("ZPlane", "X"), F("ZPlane", "Y"), F("ZPlane", "Z"), F("ZPlane", "W"),
            F("WPlane", "X"), F("WPlane", "Y"), F("WPlane", "Z"), F("WPlane", "W"));
    }

    /// <summary>Gathers the class's components, ancestors first so overrides win: the native
    /// default subobjects of the CDO, then every blueprint ancestor's SCS, then this class's
    /// InheritableComponentHandler records and SCS.</summary>
    private void CollectClassComponents(string package, string className,
        Dictionary<string, Component> components, JObject actorProps, ref string? root)
    {
        var pkg = assets.Package(package);
        if (pkg is null) return;

        var classIndex = FindExport(pkg, className, "BlueprintGeneratedClass");
        if (classIndex is null) return;
        var classJson = pkg.Json(classIndex.Value);

        if (Ref(classJson["SuperStruct"]) is { } super && !super.Package.StartsWith("/Script", StringComparison.Ordinal)
            && ClassNameAt(super.Package, super.Index) is { } superName)
        {
            CollectClassComponents(super.Package, superName, components, actorProps, ref root);
        }

        // Native default subobjects (and their per-class overrides) hang off the CDO.
        var cdoName = "Default__" + className;
        var cdoIndex = FindExport(pkg, cdoName, null);
        if (cdoIndex is not null)
        {
            var cdoProps = Props(pkg.Json(cdoIndex.Value));
            actorProps.Merge(cdoProps);
            if ((string?)cdoProps["RootComponent"]?["ObjectName"] is { } rootRef) root = InnerName(rootRef);

            for (var i = 0; i < pkg.Exports.Count; i++)
            {
                var export = pkg.Exports[i];
                if (!export.ExportType.EndsWith("Component", StringComparison.Ordinal)) continue;
                var json = pkg.Json(i);
                // Matched by the serialized outer name: export.Outer missed these subobjects.
                if ((string?)json["Outer"]?["ObjectName"] is not { } outer || InnerName(outer) != cdoName) continue;
                Merge(components, export.Name, export.ExportType, Props(json),
                    (string?)Props(json)["AttachParent"]?["ObjectName"] is { } attach ? InnerName(attach) : null);
            }
        }

        // Inherited SCS components re-templated by this class.
        if (Ref(Props(classJson)["InheritableComponentHandler"]) is { } ich)
        {
            foreach (var record in Props(Json(ich))["Records"] as JArray ?? [])
            {
                var variable = (string?)record["ComponentKey"]?["SCSVariableName"];
                if (variable is null || Ref(record["ComponentTemplate"]) is not { } template) continue;
                var json = Json(template);
                Merge(components, variable, (string)json["Type"]!, Props(json), parent: null);
            }
        }

        if (Ref(Props(classJson)["SimpleConstructionScript"]) is { } scs)
        {
            foreach (var node in Props(Json(scs))["RootNodes"] as JArray ?? [])
            {
                if (Ref(node) is { } nodeRef) CollectScsNode(Json(nodeRef), null, components);
            }
        }
    }

    private void CollectScsNode(JObject node, string? parent, Dictionary<string, Component> components)
    {
        var props = Props(node);
        var name = (string?)props["InternalVariableName"];
        if (name is null || Ref(props["ComponentTemplate"]) is not { } template) return;

        var json = Json(template);
        parent ??= (string?)props["ParentComponentOrVariableName"];
        Merge(components, name, (string)json["Type"]!, Props(json), parent);

        foreach (var child in props["ChildNodes"] as JArray ?? [])
        {
            if (Ref(child) is { } childRef) CollectScsNode(Json(childRef), name, components);
        }
    }

    /// <summary>A template only serializes properties that differ from its archetype, so an
    /// override is layered over what an ancestor already declared.</summary>
    private static void Merge(Dictionary<string, Component> components, string rawName, string type, JObject props,
        string? parent)
    {
        var name = rawName.EndsWith("_GEN_VARIABLE", StringComparison.Ordinal) ? rawName[..^"_GEN_VARIABLE".Length] : rawName;
        if (components.TryGetValue(name, out var existing))
        {
            var merged = (JObject)existing.Props.DeepClone();
            merged.Merge(props, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            existing.Props = merged;
            existing.Parent = parent ?? existing.Parent;
            return;
        }
        components[name] = new Component { Name = name, Type = type, Props = props, Parent = parent };
    }

    private static bool Hidden(JObject props) =>
        (bool?)props["bVisible"] == false || (bool?)props["bHiddenInGame"] == true;

    // ------------------------------------------------------------------ transforms

    private static Matrix4x4 Relative(JObject props)
    {
        var location = Vec(props["RelativeLocation"], Vector3.Zero);
        var scale = Vec(props["RelativeScale3D"], Vector3.One);
        var rotation = props["RelativeRotation"];
        return Matrix4x4.CreateScale(scale)
               * Rotator((float?)rotation?["Pitch"] ?? 0, (float?)rotation?["Yaw"] ?? 0, (float?)rotation?["Roll"] ?? 0)
               * Matrix4x4.CreateTranslation(location);
    }

    /// <summary>UE's <c>FRotationMatrix</c>, row-vector form, degrees in.</summary>
    public static Matrix4x4 Rotator(float pitch, float yaw, float roll)
    {
        var (sp, cp) = MathF.SinCos(pitch * MathF.PI / 180);
        var (sy, cy) = MathF.SinCos(yaw * MathF.PI / 180);
        var (sr, cr) = MathF.SinCos(roll * MathF.PI / 180);
        return new Matrix4x4(
            cp * cy, cp * sy, sp, 0,
            sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp, 0,
            -(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp, 0,
            0, 0, 0, 1);
    }

    private static Vector3 Vec(JToken? v, Vector3 fallback) => v is JObject o
        ? new Vector3((float?)o["X"] ?? fallback.X, (float?)o["Y"] ?? fallback.Y, (float?)o["Z"] ?? fallback.Z)
        : fallback;

    // -------------------------------------------------------------- json plumbing

    private readonly record struct ObjRef(string Package, int Index);

    private static JObject Props(JObject json) => json["Properties"] as JObject ?? [];

    private JObject Json(ObjRef r) => assets.RequirePackage(r.Package).Json(r.Index);

    /// <summary>Parses <c>{"ObjectPath": "Pkg/Path.N"}</c> into package + export index.</summary>
    private static ObjRef? Ref(JToken? token)
    {
        var path = token is JObject o ? (string?)o["ObjectPath"] : null;
        if (path is null) return null;
        var dot = path.LastIndexOf('.');
        if (dot < 0 || !int.TryParse(path[(dot + 1)..], out var index)) return new ObjRef(path, 0);
        return new ObjRef(path[..dot], index);
    }

    /// <summary>The package path of a referenced asset (mesh, material) - the form
    /// <c>LoadPackageObject</c> takes.</summary>
    private static string? ObjectPackageRef(JToken? token)
    {
        if (Ref(token) is not { } r) return null;
        var name = (string?)token?["ObjectName"];
        var inner = name is null ? null : InnerName(name);
        return inner is null ? r.Package : $"{r.Package}.{inner}";
    }

    /// <summary><c>Class'Outer:Name'</c> / <c>Class'Name'</c> → <c>Name</c>.</summary>
    private static string? InnerName(string objectName)
    {
        var start = objectName.IndexOf('\'');
        var end = objectName.LastIndexOf('\'');
        if (start < 0 || end <= start) return null;
        var inner = objectName[(start + 1)..end];
        var colon = inner.LastIndexOf(':');
        var dot = inner.LastIndexOf('.');
        return inner[(Math.Max(colon, dot) + 1)..];
    }

    private string? ClassNameAt(string package, int index)
    {
        var pkg = assets.Package(package);
        return pkg is null || index >= pkg.Exports.Count ? null : pkg.Exports[index].Name;
    }

    private static int? FindExport(PackageJson pkg, string name, string? type)
    {
        for (var i = 0; i < pkg.Exports.Count; i++)
        {
            var e = pkg.Exports[i];
            if (e.Name == name && (type is null || e.ExportType == type)) return i;
        }
        return null;
    }

    /// <summary><c>/Game/A/B.B_C</c> → (<c>MotorTown/Content/A/B</c>, <c>B_C</c>).</summary>
    private static (string Package, string ClassName)? AssetPackage(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("/Game/", StringComparison.Ordinal)) return null;
        var dot = assetPath.LastIndexOf('.');
        if (dot < 0) return null;
        return ("MotorTown/Content/" + assetPath["/Game/".Length..dot], assetPath[(dot + 1)..]);
    }

    /// <summary><c>/Game/A/B.B</c> → <c>MotorTown/Content/A/B.B</c>.</summary>
    private static string? AssetObjectPath(string? assetPath) =>
        string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("/Game/", StringComparison.Ordinal)
            ? null
            : "MotorTown/Content/" + assetPath["/Game/".Length..];
}
