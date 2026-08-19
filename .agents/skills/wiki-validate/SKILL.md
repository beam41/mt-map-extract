---
name: wiki-validate
description: |
  Generate the ASEAN Motor Club wiki pages (https://wiki.aseanmotorclub.com) from the
  Motor Town pak data in this repo. Triggers: "validate wiki", "check wiki",
  "missing vehicle", "wrong weight", "verify part", "wiki review", "wiki validation",
  "compare wiki to game data", "installable parts check", "fix wiki".
  Run `dotnet run -c Release --project wiki/generate` to write every DokuWiki page as
  .txt under wiki/out/ (no json).
---

# Wiki Generator (ASEAN Motor Club)

This repo reads Motor Town game data from `resource/MotorTown-Windows.pak` and the
`wiki/generate/` project renders every wiki page (vehicles, parts, cargos, cargo
spaces, and the four list pages) as plain DokuWiki .txt. Use this skill whenever asked
to check, verify, fix, review, or regenerate anything on the wiki (parts, vehicles,
comparison tables, weights, costs, names, cargo pages, production recipes).

Durable ground truth (pak data sources, game-UI display rules, pak-side data facts)
lives in `.agents/knowledge/wiki-base-assertions.md` — read it before interpreting
results.

## The pipeline (everything lives under `wiki/`, NOT `out/`)

| Path | Purpose |
|---|---|
| `wiki/generate/` | the whole generator, one C# project (`wiki/generate/wiki-generate.csproj`): `Data.cs` (pak gathering), `RenderVehicles.cs` / `RenderParts.cs` / `RenderCargos.cs` (page templates), `Format.cs` (display rules) |
| `wiki/assertions/` | snapshot of the live wiki pages (raw exports) — diff generated output against it |
| `wiki/out/vehicles/` | one page per vehicle |
| `wiki/out/parts/` | one page per part (RideHeight_-N and removed vehicles have none) |
| `wiki/out/cargos/` | one page per active cargo |
| `wiki/out/cargo_space/` | one aggregate page per space type |
| `wiki/out/list_of_parts.txt`, `list_of_vehicles.txt`, `list_of_cargos.txt`, `vehicle_comparison.txt` | the list pages |

`out/` is the MAIN extractor's output (map data, tiles). Do not write wiki output
there. The generator wipes `wiki/out/` on every run and writes only .txt — there is no
json output.

## Commands

```bash
# Generate every page from the pak (needs resource/MotorTown-Windows.pak + aes + Mappings.usmap)
dotnet run -c Release --project wiki/generate

# Compare generated pages against the snapshot
diff -r wiki/out wiki/assertions
```

## Display conventions baked into the generator

- **In other languages**: all 22 non-English locres languages, English display names,
  English fallback per language.
- **Numbers**: whole numbers plain or N0 per row; multipliers as ±% from 100;
  probabilities as %; gear ratios as `F2` with trailing zeros stripped; drag up to 3
  decimals; brake ratios `0%` for zero else `0.0%`. Floats are rounded the way UE5 C++
  rounds (the float32 value directly).
- **Aero parts** (FrontBumper/RearBumper/SideSkirt/RearSpoiler/RearWing/Roof/Fender/
  FrontSpoiler/Bullbar): a fixed per-type row schema, `-` for default values; the wiki's
  aero block leaves one extra blank line.
- **Tires**: two sub-sections — `==== Tire ====` (Dual Rear) and `==== Tire Physics ====`.
- **Capabilities** after Cargo Space, before Delivery: Taxi / Bus / Limousine / Race car /
  Can haul trailer / Has fuel pump.
- **FD parts** (bandaid): the pak's final-drive-ratio name text can be stale
  (FD_10.65's text says "10.65" but its ratio field is 9.4) — the name and slug come
  from the ratio field when they mismatch.
- **Cargo space size**: `2 × BoxExtent(cm) × RelativeScale3D` from
  `MTVehicleCargoSpaceComponent`; volume = the raw product.
- **Cargo weight**: WeightRange when nonzero (single `X kg` or `X–Y kg`), else the actor
  mesh mass, else `0 kg`.
- **Production rows**: per delivery point, config order kept; `(passive)` for configs
  with no inputs; `— | —` for demand rows.

## Known field sources (pak ground truth)

| Wiki field | Pak source |
|---|---|
| Chassis Weight | `BodyInstance.MassInKgOverride` summed over the vehicle's class blueprint exports (NOT the Vehicles table `CurbWeight` — that is 0 everywhere). Some vehicles (Zero, Bongo Bus, Nimo Taxi, …) genuinely have none → 0. |
| Drag | class default object `AirDragCoeff`, defaulting to 1.0 when absent |
| Comfort | Vehicles table `Comport` |
| Seats | count of `MTSeatComponent` exports in the blueprint |
| Drivetrain | wheel `DifferentialComponentName` (0 driven = blank, front = FWD, rear = RWD, 2 = AWD); the 5 broken assets (Bongo_Bus, Nimo_Taxi, Nuke_Taxi, Townie_Bus, Elisa2_Police) display "Rear-wheel drive" |
| Fuel capacity | CDO `FuelTankCapacityInLiter`; fuel type = engine `EngineProperty.FuelType` (default Gasoline) |
| Part name | `Name`/`Name2` localized; `#N` names get ` (Vehicle / Vehicle)` appended from `VehicleKeys` |
| Part cost/mass | `Cost`, `MassKg` on the VehicleParts row |
| Cargo name | `Name2` texts joined (locres), else the `Name` text |
| Delivery payment | Vehicles table `DeliveryBasePayment` / `DeliveryPaymentMultiplier` |

## Known intentional deviations from the live wiki

The live wiki mixes several generator generations. Regenerating normalizes:

- the full-22 "In other languages" section on pages that show English-only or native-name
  rows (stale),
- hand-added `image =` fields and custom intro/history text (13 vehicle pages),
- the 12 old-template vehicle pages that lack Axle info / In other languages,
- ghost pages for removed vehicles (conter_lead, conter_rear, jemusi, trailer_shobed,
  trailer_shotan, trailer_shovan) and renamed ones (Jemusi → "Jemusi Logger",
  jemusi_logger),
- stale values where the pak changed (cargo-space dimensions, drag, translations),
- the FD bandaid renames (fd_10_65 → fd_9_4, fd_15_hm → fd_13_15).

## Gotchas

- Slug mismatches: wiki slugs are lowercase (`anglekit_5`, `rideheight_p1`), pak keys
  are PascalCase (`AngleKit_5`, `RideHeight_+1`, `FD_1.33`). Vehicle slugs come from
  the display name ("Elisa Taxi" → `elisa_taxi`).
- The pak serializes float32; the JSON text shows the round-trip decimal. The generator
  rounds the float32 directly (UE5-style).
- `wiki/generate/` links `AssetSource.cs`/`Options.cs`/`Localization.cs`/`CargoKeys.cs`/
  `TableExtractor.cs` from the repo root via `<Compile Include="../../…">` — shared code
  changes affect it.
