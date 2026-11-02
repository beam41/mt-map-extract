# house-server

POC backend for amc-web's 3D house viewer (`/house/<housingKey>`). Serves a house plot with its
placed buildings, and every building as a GLB extracted straight from the pak.

```bash
dotnet run -c Release --project house-server        # from the repo root; listens on http://localhost:5199
```

Settings are ASP.NET configuration, so any can be passed as `--Name value` (or env vars):

| Setting | Default | |
| --- | --- | --- |
| `Pak` / `Aes` / `Usmap` / `Game` | `resource/…`, `GAME_UE5_5` | same inputs as every other project; `Aes` takes the key or a file |
| `CacheDir` | `out/house-server` | `world.json` + `glb-v<N>/<BuildingKey>_<step>.glb` |
| `WorldUrl` | `https://server.aseanmotorclub.com/api/world/` | the ~7 MB world dump |
| `WorldTtlMinutes` | `30` | at most one upstream fetch per TTL; a failed refresh serves the stale copy |
| `MaxTextureSize` | `1024` | base-color textures are downscaled to this mip |
| `urls` | `http://localhost:5199` | |

## Endpoints

- `GET /api/houses/{housingKey}` — `{ housingKey, coord, size, yaw, cost, ownerName, worldFetchedAt, buildings[] }`.
  `coord`/`size`/`cost` are out_house.json's (via `WorldExtractor.Houses()`), `yaw` is the
  House_C root component's yaw (out_house.json drops it). `buildings` are the world dump's
  `buildings` filtered by `housingKey`, each with `rowType` (`House`/`Furniture`), UE
  `location`/`rotation` and a `modelUrl`.
- `GET /api/ground/{color|normal}.png` — the Jeju landscape's grass layer (`T_GroundGrass_01_BC` /
  `_N`, what `M_Landscape_Jeju_Base` samples), for the plot surface. The house response lists both
  as `groundTextures`. The normal map is UE/DirectX-style (green down).
- `GET /api/buildings/{buildingKey}/{step}.glb` — the building in glTF space (m, Y-up; UE
  `(x, y, z)` → `(x, z, y) / 100`, the same mirror FModel uses), origin = actor root. Cached on
  disk forever and sent with `Cache-Control: public, max-age=86400`; bump
  `HouseStore.ExporterVersion` when the output changes — it renames the disk cache dir and the
  `?v=` on every `modelUrl`.

## How a building becomes a GLB

`Buildings` DataTable row → `Steps[step].ActorClass` blueprint. `BuildingAssembler` walks the
blueprint chain (parents first): CDO native subobjects, `InheritableComponentHandler` overrides,
`SimpleConstructionScript` nodes, `ChildActorComponent`s recursively — composing each component's
relative transform up its attach chain the way UE's `FTransform` does (scale multiplied per local
axis, not as matrices: they differ when a non-uniformly scaled parent holds a rotated child, e.g.
Depot_02's apron road tiles). The row then overrides the generic actor:
`StaticMeshes` (component name → mesh, used by `_Common_Prop` furniture) and `ChildActorClass`
(used by `_Common_ChildActor` furniture, e.g. lights, benches). `GlbExporter` converts each static
mesh's LOD0 with CUE4Parse-Conversion and embeds the material's diffuse texture as PNG.

Construction scripts aren't run; two of their effects are reproduced by rule. A row's `MeshScale`
*replaces* the overridden component's relative scale (`_Common_Text_01` is already 3x and its rows
say 3x). An actor's single `StaticMesh` variable, merged from the CDO and the parent's
ChildActorTemplate, is set on its single mesh component (`_Deprecated_BaseDoor` does
`LeftDoor.SetStaticMesh(DoorMesh)`).

Materials: base color texture × tint. The texture comes from `CMaterialParams2.Diffuse` groups,
then any `*Diffuse*`/`*BaseColor*`/`*BaseTexture*` texture parameter (`PM_Diffuse` on Synty parents,
`BaseTexture` on `M_GenericBase` — the palette atlas most Synty props sample), then the first
non-normal/mask texture (often an engine default — a prop that renders flat grey usually means a
missing name here). The tint comes from `DiffuseColors`, then `*BaseColor*` colors (`BaseColor_Tint`
on the `MI_Matte*` family, white texture × tint). No normal/ORM maps; point lights are ignored;
`buildingStep` > 0 rows are exported but untested.
