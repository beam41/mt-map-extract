# AGENT.md

C# (.NET 10) tool that reads Motor Town's `resource/MotorTown-Windows.pak` (UE 5.5) via CUE4Parse
and writes every `out/out_*.json` the map site needs, `out/map.png`, and the Leaflet tile pyramid
`out/tiles/` in one pass. Replaces the old three-stage dump + Rust + Node pipeline.

## Run

Paths are relative to the **working directory** — run from the repo root:

```bash
dotnet run -c Release                       # full run: data + map + tiles (~15s + ~1m AVIF)
dotnet run -c Release -- --skip-tiles       # data only
dotnet run -c Release --project wiki/validate   # wiki data: parts + vehicles + per-part pages (wiki/out/)
dotnet run -c Release --project wiki/validate -- --validate   # also fetch wiki + validate it
dotnet run -c Release --project richtags    # standalone rich-text tag finder
```

`mt-extract.yaml` is picked up automatically; CLI flags win over it. `--help` lists every option.
`--skip-json` / `--skip-map` / `--skip-tiles` disable stages independently.

## Layout

| File | Role |
| --- | --- |
| `Program.cs` | entry, orchestrates the pass; `DecodeMapTexture` (DXT1 → png via SkiaSharp), optional `--dump` |
| `Options.cs` | CLI + yaml parsing (`Options` record, flags enum, validation) |
| `AssetSource.cs` | pak mount, AES/keys/usmap; cached `PackageJson` (FModel-shaped exports); `LoadLocalization` |
| `WorldExtractor.cs` | `out_area_volume/delivery_point/bus_stop/house.json` from world actors |
| `TableExtractor.cs` | `out_cargo_key/metadata/name`, `out_cargo_type_name`, `out_vehicles_name` from data tables |
| `CargoKeys.cs` | folds FName cargo keys onto the `Cargos` table spelling (case-insensitive match) |
| `Localization.cs` | locres tables + `Text` FText helpers + `Output.WriteJson` |
| `TileGenerator.cs` | libvips (NetVips) tile pyramid; `{z}_{x}_{y}.{ext}`, native zoom = 4096px map at z4 |
| `wiki/validate/` | wiki pipeline (`mt-wiki`): `Program.cs` (gather + validate modes), `PartExtractor.cs` (vehicle parts + per-part pages), `Validator.cs` (wiki checks); links in `AssetSource.cs`, `Options.cs`, `Localization.cs`, `CargoKeys.cs` from the root via `<Compile Include="../../…">` — shared code changes affect it |
| `richtags/` | standalone rich-text tag scanner; own mounting, no shared files |
| `tools/explore/` | throwaway exploration harness for parts data (`find`, `table`, `rows`, `grep`, `stats`, …); keep hacky |
| `docs/vehicle-parts.md` | full data map of VehicleParts/Vehicles tables; update when part fields change |

`richtags/`, `tools/` are excluded from the main project's compile glob in
`MtExtract.csproj`; each has its own csproj. The wiki pipeline writes only under `wiki/out/`
(out/ is the map extractor's output).

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
- No test suite. Verification is a smoke run: `dotnet run -c Release` and inspect the affected
  `out/out_*.json`. Keep runs cheap with the skip flags.

## Gotchas

- Never commit to `out/`, `resource/`, `bin/`, `obj/`, `*.json` at root (gitignored).
- The pak is 2.6 GB; don't copy it around or read it wholesale — `AssetSource` loads packages on demand.
- Tile encoding at default `effort 9` AVIF takes ~1m; use `--skip-tiles` for data work.
- Game updates rotate AES + usmap together; a partial refresh yields nonsense, not errors.
