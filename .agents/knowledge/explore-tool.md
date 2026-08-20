# The explore tool

`tools/explore/` is a throwaway exploration harness for reading Motor Town pak data
directly — no project code involved. Use it to answer "what does the pak actually
contain?" before touching the extractors or the wiki generator.

## Run

From the repo root (paths are CWD-relative):

```bash
dotnet run -c Release --project tools/explore -- <command> [args]
```

It mounts `resource/MotorTown-Windows.pak` with the repo's `aes` and `Mappings.usmap`
automatically (falls back to `<repo>/resource/…` when run elsewhere). Every command
prints to stdout; pak-package paths are used without the extension where noted.

## Pak path conventions

- Data tables: `MotorTown/Content/DataAsset/VehicleParts/VehicleParts` (768 rows),
  `MotorTown/Content/DataAsset/Vehicles/Vehicles` (171), `.../Cargos` (91),
  `.../Cargos_ScheduleI`, `.../Houses`.
- Blueprints: `/Game/Cars/Models/...` asset paths map to `MotorTown/Content/Cars/Models/...`.
  Delivery points: `MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint/`.
  The world map: `MotorTown/Content/Maps/Jeju/Jeju_World`.
- Composite data tables (`VehicleParts`, `Vehicles`, `Cargos`) read via
  `table`/`rows`/`names` with the master path; per-type files (`Engines`, `Wheels`, ...)
  are curated subsets of the same rows — read only the master.

## Commands

| Command | Purpose |
| --- | --- |
| `find <regex>` | list pak files whose path matches (case-insensitive), e.g. `find cotra`, `find Atlas_6x2` |
| `table <path>` | DataTable rows: row names + column names (no values) — the schema overview |
| `rows <path> <rowName>` | one row's full JSON — the actual values |
| `props <path>` | property names of every export in a package (blueprint structure) |
| `grep <dir> <regex>` | dump every package under `<dir>` and grep the serialized JSON for the pattern |
| `types <path>` | per `PartType`: row count + populated columns + a sample row |
| `stats <path> <type>` | per `PartType`: the columns whose values vary across rows (the real stats) |
| `names <path>` | row names, one per line (grep this to find the exact key) |
| `dump <path> <exportIndex>` | one export's full JSON — e.g. a blueprint's CDO or a component |
| `loc` | the English locres: namespace → key → value |
| `locfind <needle>` | search the locres values (English table only) for a substring |
| `veh <path> <rowName>` | a vehicle row's restrictions only (Parts, flags, classes, tags) |

## Typical flows

- **Find an asset**: `find <name>` → pak path; then `dump` the package export 0 to see
  its properties, or scan exports (`props` / a loop of `dump <path> <index>`) for the
  component you need (e.g. `MTVehicleCargoSpaceComponent`, `MHWheelComponent`,
  `BlueprintGeneratedClass`).
- **Resolve a blueprint CDO**: an object reference serializes with an
  `ObjectPath` like `MotorTown/Content/Cars/Models/Elisa/Elisa.6` — the number after
  the last `.` is the export index to `dump`.
- **Verify a generator value**: compute the expected value from the raw rows and compare
  with the wiki/generator output. E.g. cargo-space size = `2 × BoxExtent × RelativeScale3D`
  from the `MTVehicleCargoSpaceComponent` export; delivery payments =
  `DeliveryBasePayment`/`DeliveryPaymentMultiplier` on the Vehicles row.
- **Find which rows exist**: `names <path> | grep <pattern>` (exact pak keys — row names
  are PascalCase, e.g. `BrakeBalance_F50`, `FD_10.65`, `"301"`).

## Gotchas

- **Float32 values**: the pak stores floats as float32; the serialized JSON shows the
  round-trip decimal (`"1.315"`), but reading the JValue converts float32→double
  (`1.315000057220459`). The wiki generator rounds the float directly (UE5-style);
  `rows`/`dump` output shows the round-trip text. Mind the difference when verifying.
- **Output is JSON text**: pipe through `python3 -c "import sys,json; ..."` for precise
  inspection (python keeps the round-trip double).
- `grep` matches the serialized JSON text, so it finds property names and string values
  but not e.g. raw floats at a different precision. It also only scans `.uasset` files
  (`assets.Files(dir)` filtered to `f.Extension == "uasset"`) — it silently skips `.umap`
  entirely, so it can never see anything placed in the world.
- **World Partition**: `Jeju_World.umap` is only the always-loaded *persistent* level.
  UE5.5's World Partition splits most spatial content into thousands of tiny per-cell
  packages under `Jeju_World/_Generated_/*.umap` (5738 of them, confirmed) — `WorldExtractor`
  and every `find`/`grep`/`dump` invocation against `Jeju_World` alone never sees them.
  Confirmed (2026-08-20) they hold background/environment content only (foliage, trees,
  streetlamps, landscape data, splines) — no gameplay actor type used anywhere in this
  codebase (`MTDealerVehicleSpawnPoint`, `POI_*`, vendor/dealer/spawner blueprints, ...) has
  ever been found inside one; every gameplay-relevant placement lives in the persistent
  level. Use `wpscan [pattern]` to search cell export type names when checking a new lead.
- **Keyword-filtered actor-type sweeps miss things**: enumerating all ~338 actor types in
  `Jeju_World` and eyeballing/`grep`ing for expected keywords (`dealer|garage|vendor|
  spawn|police|fire|...`) is NOT exhaustive — `MWorldVehicleSpawnPoint` (134 world vehicle
  spawn points, confirmed carrying SCM Kart One and ~130 others) was missed this way twice
  because neither its name nor its `VehicleClasses`/`VehicleParams` fields happened to match
  the keyword list used. When a "this data must be in the pak somewhere" claim survives an
  exhaustive type-name sweep, pivot to **`nearbox <path> <x1> <y1> <x2> <y2>`**: every actor
  in a coordinate box, independent of type name — scope the box from a known landmark's
  `AreaVolumes()` polygon bounds (which isn't only `Zone`-flagged — `RaceTrack`/`SmallArea`/
  `LargeArea` flags exist too, e.g. "Olle Speedway" is `RaceTrack`-flagged, not `Zone`) and
  physically enumerate what's actually there.
- `loc`/`locfind` cover the English locres table only — per-language lookups need the
  code or `loc <namespace>` per table.
- The tool is throwaway — keep it hacky, prefer one-off scripts over adding commands.

## When to use instead of the code

- Checking whether a wiki value is pak-truth: `rows`/`dump` are the ground truth.
- Spot-checking the wiki generator's data sources (`.agents/knowledge/wiki-pages.md` field map).
- Debugging a missing name/translation: `locfind` + `rows` on the name fields.
