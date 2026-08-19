# mt-map-extract

C# (.NET 10) tooling that reads Motor Town's game pak (`resource/MotorTown-Windows.pak`,
UE 5.5) via CUE4Parse and produces everything the ASEAN Motor Club site and wiki need:
the map-site data + world map + tile pyramid, every DokuWiki page, and the rich-text tag
report. Four standalone projects share the pak helpers from `common/` via
`<ProjectReference>` — no per-project copies. All outputs land in the root `out/` tree,
split by project.

## Projects

| Project | What it does | Output |
| --- | --- | --- |
| `common/` | shared pak helpers: `AssetSource` (mount, AES/usmap, cached FModel-shaped packages, locres), `CargoKeys`, `TableExtractor`, `WorldExtractor`, `PakOptions` | — |
| `amc-web/` | the map-site extractor: `out_*.json`, `map.png`, and the Leaflet tile pyramid (AVIF via libvips) | `out/amc-web/data/`, `out/amc-web/map/` |
| `wiki/` | the DokuWiki generator: every generated page (vehicles, parts, cargos, cargo spaces, the four list pages) as plain .txt — no json | `out/wiki/` |
| `richtags/` | rich-text tag scanner | `out/richtags/rich_text_tags.md` |
| `tools/explore/` | throwaway exploration harness for reading pak data directly | — |

## Requirements

- `resource/MotorTown-Windows.pak` (~2.8 GB, from the Steam install) — gitignored.
- `resource/aes` — the AES-256 key (hex, one line); rotated by the developer.
- `resource/Mappings.usmap` — UE property mappings; regenerate after a game update.

## Quick start

Run from the repo root (paths are working-directory relative):

```bash
# Full map run: data + world map + tiles (~15s + ~1m AVIF at effort 9)
dotnet run -c Release --project amc-web

# Data only (fast)
dotnet run -c Release --project amc-web -- --skip-tiles

# Wiki pages (1032 DokuWiki .txt pages)
dotnet run -c Release --project wiki

# Rich-text tag report
dotnet run -c Release --project richtags

# Explore the pak (see .agents/knowledge/explore-tool.md)
dotnet run -c Release --project tools/explore -- <command>
```

`amc-web/mt-extract.yaml` is picked up automatically; CLI flags win. `--skip-json` /
`--skip-map` / `--skip-tiles` disable stages independently. See `amc-web/README.md` for
the extractor's details (options, failure signatures).

## Output tree

```
out/
  amc-web/data/       out_*.json (areas, delivery points, bus stops, houses, cargo tables, names)
  amc-web/map/        map.png + tiles/{z}_{x}_{y}.avif (native zoom = 4096px map at z4)
  wiki/               vehicles/, parts/, cargos/, cargo_space/, list_of_*.txt, vehicle_comparison.txt
  richtags/           rich_text_tags.md
```

## Reference docs

Everything an agent must read before touching pak data or the wiki lives in
`.agents/knowledge/`:

- `vehicle-parts.md` — the VehicleParts table, part→vehicle restrictions, per-type statistics
- `vehicles.md` — the Vehicles table, blueprint-derived stats, axles, capabilities, cargo spaces
- `cargos.md` — the Cargos tables, cargo weights, cargo-space types, DeliveryPoint recipes
- `wiki-pages.md` — the exact DokuWiki templates + display rules the generator reproduces
- `dokuwiki-syntax.md` — the DokuWiki markup reference (https://www.dokuwiki.org/wiki:syntax)
- `explore-tool.md` — how to inspect the pak with the explore harness
- `wiki-base-assertions.md` — stable pak-data axioms
- `mt-pak-extract-review.md` — read-only review of the mt-pak-extract pipeline
