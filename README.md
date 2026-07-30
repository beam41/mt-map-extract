# mt-extract

Reads `MotorTown-Windows.pak` and writes every `out/out_*.json` the map site needs, the world
map as `out/map.png`, and the Leaflet tile pyramid in `out/tiles/` - in one pass. This replaces the
old three-stage pipeline (FModel-style dump into `MotorTown/` → `cargo run` → six Node scripts)
and the standalone `tilegen`; nothing is written to disk between stages any more, so a full run
is ~15s of extraction plus however long the tile encoder takes (~1m for AVIF at effort 9).

Paths are relative to the **working directory**, so run it from the repo root:

```bash
dotnet run -c Release
```

Nothing needs installing beyond the .NET SDK: the native libraries it leans on (libvips for
tiles, Skia for the png) ship in the NuGet packages for Linux, Windows and macOS, x64 and
arm64, glibc and musl. Publishing for one target only is `dotnet publish -c Release -r <rid>`.

## What you need in `resource/`

| File | What it is | Where it comes from |
| --- | --- | --- |
| `MotorTown-Windows.pak` | the game's content archive (~2.8 GB) | the game install, `.../steamapps/common/Motor Town Behind The Wheel/MotorTown/Content/Paks/`. Copy it out; a game update replaces it in place |
| `aes` | AES-256 key that decrypts the pak - hex, one line, leading `0x` optional | dumped from the game binary (AES key dumper, or the usual community sources). Changes whenever the developer rotates it |
| `Mappings.usmap` | property type mappings, needed because the shipped build carries no reflection data | generated against the game binary with a usmap dumper (Dumper-7, UE4SS). Regenerate after a game update or structs deserialize as garbage |

All three are overridable: `--pak`, `--aes` (a key or a file holding one), `--usmap`.
A run reads nothing else from the repo and writes only into `out/`.

Symptoms of a mismatch:

| Symptom | Cause |
| --- | --- |
| `nothing mounted - wrong AES key?` | stale or malformed `aes` |
| `VersionException: Read size is smaller than zero` | wrong `--game` for this build (see below) |
| missing properties, nonsense values | `Mappings.usmap` is from a different game version |

## What it writes

| File | From |
| --- | --- |
| `out_area_volume.json` | `MTAreaVolume` actors in `Jeju_World`, names localized |
| `out_delivery_point.json` | every `DeliveryPoint` blueprint matched against the world, with its production/demand/storage configs |
| `out_bus_stop.json` | `BusStop_0*_C` / `BusTerminal_01_C` actors |
| `out_house.json` | `House_C` actors joined with the `Houses` data table |
| `out_cargo_key.json` / `out_cargo_metadata.json` | the `Cargos` data table (plus `Cargos_ScheduleI` when the game still ships it) |
| `out_cargo_name.json`, `out_cargo_type_name.json`, `out_vehicles_name.json` | data tables joined with `Game.locres` for all 23 cultures |
| `map.png` | the world map `Texture2D`, decoded from DXT1 |
| `tiles/{z}_{x}_{y}.avif` | tiles cut from that same decoded image, zoom 0 through 5 |

Cargo keys are folded onto the spelling the `Cargos` data table uses. They are FNames, which
the game matches case-insensitively while storing whatever was typed, so the raw assets contain
both `Terra` and `terra` - sometimes in the same file. Note the canonical form is not plain
PascalCase: `lHBeam_6m` really does start lowercase.

Name maps drop languages that match English and list `en` first, as the old
`remove_duplicates.js` pass did. By default every enum keeps the game's own spelling
(`EDeliveryCargoType::Log`, `EMTAreaVolumeFlags::LargeArea`).

Two orderings differ from the old pipeline, contents are unchanged: delivery points come out
sorted by blueprint path (the Rust used raw directory order) and `out_vehicles_name.json`
follows the data table's row order (JSON.stringify used to hoist the numeric keys `1`-`4`).

## Tiles

The tile half is a port of the old `tilegen` (same libvips pipeline, so the output is
pixel-identical), with AVIF added. Tiles are named `{z}_{x}_{y}.{ext}` in the Leaflet/OpenLayers
scheme: z0 is a single tile, zN is a 2^N x 2^N grid. Native zoom is the smallest N where
2^N × `tile-size` covers the image - for the 4096px map that is z4, so the default `zoom: 5`
upscales one level with nearest-neighbour.

| Option | Default | Notes |
| --- | --- | --- |
| `--zoom <n\|native>` | 5 | writes z0..zn; `native` stops where upscaling would start |
| `--tile-size <px>` | 256 | |
| `--format` | avif | `avif`, `webp`, `png`, `jpeg` |
| `--quality <0-100>` | 65 | avif/webp/jpeg |
| `--effort <0-9>` | 9 | png uses it as compression level, webp clamps to 6 |
| `--upscale`, `--downscale` | nearest, lanczos3 | `nearest linear cubic mitchell lanczos2 lanczos3` |
| `--tiles-out <dir>` | `<out>/tiles` | |

At the defaults that is 1365 tiles, ~5.8 MB, about a minute on 8 cores - AVIF at effort 9 is
most of the runtime. `--format webp` or a lower `--effort` cuts that to seconds.

## Stages

Each output can be turned off on its own, so you can re-cut the tiles without re-reading the
world, or refresh the data without waiting on the encoder:

| Flag | yaml | Skips |
| --- | --- | --- |
| `--skip-json` | `skip-json: true` | `out/out_*.json` - the world isn't even read, so the run is instant |
| `--skip-map` | `skip-map: true` | `out/map.png` |
| `--skip-tiles` | `skip-tiles: true` | `out/tiles/` |

```bash
dotnet run -c Release -- --skip-json --skip-map     # tiles only
dotnet run -c Release -- --skip-map --skip-tiles    # data only
```

## Config file

Anything above can live in a yaml file instead. `./mt-extract.yaml` is picked up automatically;
`--config <file>` points somewhere else. Keys are the long option names, dashes or underscores,
and command line flags win over the file:

```yaml
out: out
amc: false
zoom: 5
format: avif
quality: 65
effort: 9
```

## Other options

```bash
dotnet run -c Release -- --amc             # AMC map enum spellings
dotnet run -c Release -- --skip-map        # skip the png
dotnet run -c Release -- --dump MotorTown  # full asset dump
```

`--amc` rewrites every key and string on the way out:

| Default | With `--amc` |
| --- | --- |
| `EMTAreaVolumeFlags::LargeArea` | `LargeArea` |
| `EDeliveryCargoType::Log` | `_TLog` |
| `EDeliveryCargoType::SmallPackage2` | `_TSmallPackage` |

It also drops the `name` off `Resident_C` delivery points - all 223 of them are just
"Resident" in 23 languages, and the AMC map labels those itself. That alone takes
`out_delivery_point.json` from 512 KB to 347 KB.

`--dump <dir>` additionally writes every package under `MotorTown/` as FModel-style JSON
(~22k files, ~5 GB) - the old `MotorTown/` folder. The pipeline itself never needs it.

## Game version

The pak is UE 5.5 (`FileVersionUE5` 1013), so the provider defaults to `--game GAME_UE5_5`.
After a game update that bumps the engine, pass e.g. `--game GAME_UE5_6`.
