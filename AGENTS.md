# AGENT.md

C# (.NET 10) tooling around Motor Town's `resource/MotorTown-Windows.pak` (UE 5.5), read via
CUE4Parse. Five standalone .NET projects, each with its own csproj, sharing the pak helpers
from `common/` via `<ProjectReference>` — no per-project copies. All outputs go into the
root `out/` tree, split by project. `script/terrain-viewer/` is a separate, npm-based
Three.js viewer that consumes two of those outputs (`out/heightmap/`,
`out/amc-web/map/map.png`) - not part of the .NET solution.

## Read before starting

Any work on pak data or the wiki pages **must** start by reading the domain docs —
they are the verified ground truth (layout, schemas, display rules, gotchas):

- `.agents/knowledge/vehicle-parts.md` — the VehicleParts table, part→vehicle restrictions, per-type statistics
- `.agents/knowledge/vehicles.md` — the Vehicles table, blueprint-derived stats (weight/drag/seats/fuel/axles), capabilities, default parts, cargo spaces
- `.agents/knowledge/cargos.md` — the Cargos tables, cargo weights, cargo-space types, DeliveryPoint recipes
- `.agents/knowledge/wiki-pages.md` — the exact DokuWiki templates + display rules the generator must reproduce
- `.agents/knowledge/dokuwiki-syntax.md` — the DokuWiki markup reference (https://www.dokuwiki.org/wiki:syntax)
- `.agents/knowledge/explore-tool.md` — how to inspect the pak directly with the explore harness
- `.agents/knowledge/live-wiki-publish.md` — how to push a generated page straight to the live wiki (only when explicitly asked)

Update the relevant doc whenever a schema or a display rule changes.

## Run

Paths are relative to the **working directory** — run from the repo root:

```bash
dotnet run -c Release --project amc-web          # amc-web extractor: data + map + tiles (~15s + ~1m AVIF)
dotnet run -c Release --project amc-web -- --skip-tiles   # data only
dotnet run -c Release --project wiki             # wiki generator: every DokuWiki page as .txt (out/wiki/, no json)
dotnet run -c Release --project richtags         # rich-text tag finder (writes out/richtags/)
dotnet run -c Release --project heightmap        # landscape heightmap extractor (writes out/heightmap/, ~75s)
dotnet run -c Release --project tools/explore    # throwaway parts-data exploration harness
```

`amc-web/mt-extract.yaml` is picked up automatically; CLI flags win over it. `--help` lists
every option. `--skip-json` / `--skip-map` / `--skip-tiles` disable stages independently.

`script/terrain-viewer/` is npm-based, not part of the above - see its own
`script/terrain-viewer/README.md` for build/run steps (`pnpm install`,
`node scripts/prepare-assets.js`, `pnpm dev`).

## Layout

| Path | Role |
| --- | --- |
| `common/` | the shared pak helpers: `AssetSource.cs` (pak mount, AES/usmap, cached `PackageJson`, `LoadLocalization`), `Localization.cs` (locres tables + `Text` helpers + `Output.WriteJson`), `CargoKeys.cs` (canonical cargo keys), `TableExtractor.cs` (cargo/vehicle name tables), `WorldExtractor.cs` (areas, delivery points, bus stops, houses), `PakOptions.cs` (the minimal mount config). Referenced by every project — no `<Compile Include>` sharing. |
| `amc-web/` | the amc-web extractor (`amc-web`): `Program.cs` (orchestration, `DecodeMapTexture`), `Options.cs` (CLI + yaml — amc-web-only: tile/dump options), `TileGenerator.cs` (libvips tile pyramid). Writes `out/amc-web/data/` (the `out_*.json`), `out/amc-web/map/map.png`, `out/amc-web/map/tiles/`. |
| `wiki/` | the DokuWiki generator (`wiki`): `Program.cs`, `Data.cs` (pak gathering), `RenderVehicles.cs` / `RenderParts.cs` / `RenderCargos.cs` / `RenderDelivery.cs` (page templates), `Format.cs` (display rules), `LiveWiki.cs` (live-wiki `image =` field fetch/merge), `WikiOptions.cs` (its own CLI, incl. `--bootstrap`). Writes `out/wiki/` (vehicles/, parts/, cargos/, cargo_space/, cargo_type/, delivery_points/, list_of_*.txt, vehicle_comparison.txt) — no json. The four "detail" page types (vehicles, parts, cargos, delivery_points) each split into `{slug}:auto_infobox` + `{slug}:auto_details` (bot-owned, regenerated every run — `auto_infobox`'s `image =` line is live-fetched from the real wiki and merged back in every run) plus a once-generated heading, so a human curator can transclude the two subpages into a hand-owned live shell page via the DokuWiki `include` plugin without losing hand-written prose on regeneration; `out/wiki-bootstrap/` (opt-in, `--bootstrap`) holds the one-time shell suggestion for each. See `.agents/knowledge/wiki-pages.md` for the migration recipe and the deployment rule (never delete-sync a shell page path). |
| `richtags/` | rich-text tag scanner (`richtags`), standalone, writes `out/richtags/rich_text_tags.md`. |
| `heightmap/` | the landscape heightmap extractor (`heightmap`): `Program.cs` (orchestration), `Options.cs` (core CLI: `--origin-x`/`--origin-y`/`--map-size` default to the game's real map extent, `--max-zoom` (default 4, matches amc-web's own native zoom) for the height tile pyramid (per-zoom mesh-matched resolutions z1=8..z4=65, z0 never generated), `--exclude-guid` default excludes "OlleSpeedway_Landscape" (confirmed dead in the live game), plus `--pak`/`--aes`/`--usmap`/`--out`; debug-only options prefixed `--debug-guid`/`--debug-size`/`--debug-auto-fit`/`--debug-tiles`), `LandscapeExtractor.cs` (World Partition cell scan + texture decode + world-space stitch at native resolution, no resampling; live landscapes merged by taking the higher elevation per pixel), `OceanExtractor.cs` (reads the ocean's world Z from the persistent level's `MTOceanConfig` actor, cross-verified against `WaterBodyOcean`'s own transform), `ImageWriter.cs` (native 16-bit PNG + raw `heights.bin` (uint16 little-endian, row-major, for point queries) + Leaflet/OpenLayers-style `tiles/<z>_<x>_<y>.bin` height tile pyramid (same `{z}_{x}_{y}` naming/zoom scheme as `amc-web`'s color tiles, `tileSize+3` samples per tile - a 1px border overlap plus a 1px normal halo so adjacent tiles agree exactly on both position and lighting normals at their shared edge - for `script/terrain-viewer`'s tiled LOD renderer) + `debug/` downscaled preview via libvips), `TileDumper.cs` (unstitched per-component dump, `--debug-tiles`). Writes `out/heightmap/` (`Jeju_World_heightmap16.png`, `Jeju_World.json` (incl. an `"ocean"` object with the level in cm/meters), `heights.bin`, `tiles/`, `debug/`). See `.agents/knowledge/landscape-heightmap.md`. |
| `tools/explore/` | throwaway exploration harness for parts data (`find`, `table`, `rows`, `grep`, `stats`, …); keep hacky. |
| `script/terrain-viewer/` | Three.js 3D viewer with Leaflet/OpenLayers-style tiled terrain (`pnpm`/`npm`, TypeScript, not part of the .NET solution): a quadtree of terrain patches built from the `heightmap` project's height tile pyramid, textured with `amc-web`'s matching color tile pyramid, refining to smaller/higher-zoom tiles near the camera, culling tiles entirely outside the camera's view frustum, and a huge flat ocean quad at the pak's own ocean level. `scripts/prepare-assets.js` is a pure direct-copy build step - no decode/downsampling of its own - copying both pyramids into `public/assets/tiles/{height,color}/` plus a small `tiles.json` metadata passthrough (incl. `oceanLevelMeters`, gitignored, generated); `src/` is split by concern (`constants.ts`, `types.ts`, `heightmap.ts`, `lod.ts` - `selectLeafTiles()` walks the quadtree every frame - `tileGeometry.ts`, `tileManager.ts`, `oceanQuad.ts`, `cameraRig.ts`, `groundPan.ts`, `ringDebugView.ts`, `debugPanel.ts`, `main.ts` orchestrates); `heightmap.ts` applies the raw-height-to-meters formula client-side (`rawHeightToWorldZMeters`); renders with Vite + Three.js (`pnpm dev`, `pnpm typecheck`). See its own `README.md` for build/run steps, the map/heightmap alignment assumption, the LOD/skirt/culling/ocean design, and every UI gotcha (texture flip, custom pan/zoom/orbit). |
| `.agents/knowledge/vehicle-parts.md` | full data map of VehicleParts/Vehicles tables; update when part fields change |
| `.agents/knowledge/vehicles.md` | vehicle domain: Vehicles table, blueprint stats, axles, capabilities, cargo spaces |
| `.agents/knowledge/cargos.md` | cargo domain: Cargos tables, weights, space types, DeliveryPoint recipes |
| `.agents/knowledge/wiki-pages.md` | the exact DokuWiki templates, display rules, and pak→wiki field map the generator must reproduce |
| `.agents/knowledge/mt-pak-extract-review.md` | read-only review of the mt-pak-extract extraction pipeline (deviations found 2026-08-19) |
| `.agents/knowledge/live-wiki-publish.md` | the plain HTTP form-POST flow for pushing a generated page straight to the live wiki, on request |

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
