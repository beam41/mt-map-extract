# AGENT.md

C# (.NET 10) tooling around Motor Town's `resource/MotorTown-Windows.pak` (UE 5.5), read via
CUE4Parse. Four standalone projects, each with its own csproj, sharing the pak helpers from
`common/` via `<ProjectReference>` — no per-project copies. All outputs go into the root
`out/` tree, split by project.

## Read before starting

Any work on pak data or the wiki pages **must** start by reading the domain docs —
they are the verified ground truth (layout, schemas, display rules, gotchas):

- `docs/vehicle-parts.md` — the VehicleParts table, part→vehicle restrictions, per-type statistics
- `docs/vehicles.md` — the Vehicles table, blueprint-derived stats (weight/drag/seats/fuel/axles), capabilities, default parts, cargo spaces
- `docs/cargos.md` — the Cargos tables, cargo weights, cargo-space types, DeliveryPoint recipes
- `docs/wiki-pages.md` — the exact DokuWiki templates + display rules the generator must reproduce
- `docs/dokuwiki-syntax.md` — the DokuWiki markup reference (https://www.dokuwiki.org/wiki:syntax)

Update the relevant doc whenever a schema or a display rule changes.

## Run

Paths are relative to the **working directory** — run from the repo root:

```bash
dotnet run -c Release --project amc-web          # amc-web extractor: data + map + tiles (~15s + ~1m AVIF)
dotnet run -c Release --project amc-web -- --skip-tiles   # data only
dotnet run -c Release --project wiki             # wiki generator: every DokuWiki page as .txt (out/wiki/, no json)
dotnet run -c Release --project richtags         # rich-text tag finder (writes out/richtags/)
dotnet run -c Release --project tools/explore    # throwaway parts-data exploration harness
```

`amc-web/mt-extract.yaml` is picked up automatically; CLI flags win over it. `--help` lists
every option. `--skip-json` / `--skip-map` / `--skip-tiles` disable stages independently.

## Layout

| Path | Role |
| --- | --- |
| `common/` | the shared pak helpers: `AssetSource.cs` (pak mount, AES/usmap, cached `PackageJson`, `LoadLocalization`), `Localization.cs` (locres tables + `Text` helpers + `Output.WriteJson`), `CargoKeys.cs` (canonical cargo keys), `TableExtractor.cs` (cargo/vehicle name tables), `WorldExtractor.cs` (areas, delivery points, bus stops, houses), `PakOptions.cs` (the minimal mount config). Referenced by every project — no `<Compile Include>` sharing. |
| `amc-web/` | the amc-web extractor (`mt-extract`): `Program.cs` (orchestration, `DecodeMapTexture`), `Options.cs` (CLI + yaml — amc-web-only: tile/dump options), `TileGenerator.cs` (libvips tile pyramid). Writes `out/amc-web/data/` (the `out_*.json`), `out/amc-web/map/map.png`, `out/amc-web/map/tiles/`. |
| `wiki/` | the DokuWiki generator (`mt-wiki-generate`): `Program.cs`, `Data.cs` (pak gathering), `RenderVehicles.cs` / `RenderParts.cs` / `RenderCargos.cs` (page templates), `Format.cs` (display rules), `WikiOptions.cs` (its own CLI). Writes `out/wiki/` (vehicles/, parts/, cargos/, cargo_space/, list_of_*.txt, vehicle_comparison.txt) — no json. |
| `richtags/` | rich-text tag scanner (`richtags`), standalone, writes `out/richtags/rich_text_tags.md`. |
| `tools/explore/` | throwaway exploration harness for parts data (`find`, `table`, `rows`, `grep`, `stats`, …); keep hacky. |
| `docs/vehicle-parts.md` | full data map of VehicleParts/Vehicles tables; update when part fields change |
| `docs/vehicles.md` | vehicle domain: Vehicles table, blueprint stats, axles, capabilities, cargo spaces |
| `docs/cargos.md` | cargo domain: Cargos tables, weights, space types, DeliveryPoint recipes |
| `docs/wiki-pages.md` | the exact DokuWiki templates, display rules, and pak→wiki field map the generator must reproduce |
| `docs/mt-pak-extract-review.md` | read-only review of the mt-pak-extract extraction pipeline (deviations found 2026-08-19) |

The wiki generator wipes `out/wiki/` on every run and writes only .txt pages. `wiki/assertions/`
holds a snapshot of the live wiki pages for diffing generated output. Every project writes into
the root `out/` tree, split by project; `out/` is gitignored.

## Inputs (`resource/`, gitignored)

- `MotorTown-Windows.pak` (~2.8 GB) — game content archive; copied from the Steam install.
- `aes` — AES-256 key (hex, one line); rotated by the developer, re-dump from the game binary.
- `Mappings.usmap` — property mappings; regenerate after a game update or structs deserialize as garbage.

Overridable via `--pak`, `--aes`, `--usmap`. Failure signatures: `nothing mounted - wrong AES key?`
= stale AES; `VersionException: Read size is smaller than zero` = wrong `--game`
(currently `GAME_UE5_5`; bump to `GAME_UE5_6` style after an engine update); garbage values
= stale usmap.

## Conventions

- Game data paths are pak paths like `MotorTown/Content/DataAsset/Cargos` (case-insensitive lookups).
- Cargo keys are FNames — game matches case-insensitively while storing whatever was typed, so
  both `Terra` and `terra` exist. Always fold through `CargoKeys.Canonical`.
- Name maps drop languages identical to English, list `en` first. Enum spellings keep the game's
  own (`EDeliveryCargoType::Log`) unless `--amc` rewrites to AMC spellings (`_TLog`, `LargeArea`).
- Locres texts authored in the editor have an empty (not null) namespace — `Text.Namespace`
  falls back to `""`, not null.
- `Output.WriteJson` is the only sanctioned way to write output files.
- No test suite. Verification is a smoke run of the affected project and an inspection of its
  `out/` subtree. Keep runs cheap with the skip flags.

## Gotchas

- Never commit to `out/`, `resource/`, `bin/`, `obj/`, `*.json` at root (gitignored).
- The pak is 2.6 GB; don't copy it around or read it wholesale — `AssetSource` loads packages on demand.
- Tile encoding at default `effort 9` AVIF takes ~1m; use `--skip-tiles` for data work.
- Game updates rotate AES + usmap together; a partial refresh yields nonsense, not errors.
