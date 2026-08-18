---
name: wiki-validate
description: |
  Validate the ASEAN Motor Club wiki (https://wiki.aseanmotorclub.com) against the
  Motor Town pak data in this repo. Triggers: "validate wiki", "check wiki",
  "missing vehicle", "wrong weight", "verify part", "wiki review", "wiki validation",
  "compare wiki to game data", "installable parts check".
  Run `dotnet run -c Release --project wiki/validate` first to gather pak data, then
  `-- --validate` to fetch wiki pages and produce wiki/out/validation.json.
---

# Wiki Validation (ASEAN Motor Club)

This repo extracts Motor Town game data from `resource/MotorTown-Windows.pak` and can
validate the AMC wiki against it. Use this skill whenever asked to check, verify, fix,
or review anything on the wiki (parts, vehicles, comparison tables, weights, costs,
names, installable parts).

## The pipeline (everything lives under `wiki/`, NOT `out/`)

| Path | Purpose |
|---|---|
| `wiki/validate/` | the C# validator program (Project `wiki/validate/wiki.csproj`) |
| `wiki/out/out_vehicle_data.json` | gathered per-vehicle stats (gather mode output) |
| `wiki/out/validation.json` | machine-readable list of every incorrect claim found |
| `wiki/out/review.md` | hand-written human review (NOT auto-generated — you write it) |
| `wiki/out/pages/` | cached fetched wiki pages (raw exports) |

`out/` is the MAIN extractor's output (map data, tiles). Do not write wiki validation
output there.

## Commands

```bash
# 1. Gather pak data (needs resource/MotorTown-Windows.pak + aes + Mappings.usmap)
dotnet run -c Release --project wiki/validate

# 2. Fetch wiki pages + validate everything
dotnet run -c Release --project wiki/validate -- --validate

# Optional: different output dir
dotnet run -c Release --project wiki/validate -- --validate --wiki-out wiki/out
```

`--validate` fetches the wiki live and writes `wiki/out/validation.json` — one JSON
object per incorrect claim: `{source, vehicle, field, wiki, pak}`.

## What the validator checks

- **`list_of_parts`** — every part: name (English, incl. the `#1 → "#1 (Vehicle)"`
  augmentation), cost, mass. All 768 parts.
- **`list_of_vehicles`** — every vehicle: English name + existence in pak.
- **`vehicle_comparison`** — cost, drivetrain, chassis weight, drag for all rows.
- **Per-vehicle pages** — infobox (`Weight`, `Drag coefficient`),
  `Specifications` (`Engine`, `Transmission`, `Drivetrain`, `Chassis Weight`),
  `Capabilities` (Taxi/Bus/Limo/Race Car), `Default Parts` (slot → part set),
  `Installable Parts` (fit rule vs pak restrictions).

## Known field sources (pak ground truth)

| Wiki field | Pak source |
|---|---|
| Chassis Weight | `BodyInstance.MassInKgOverride` summed over the vehicle's class blueprint exports (NOT the Vehicles table `CurbWeight` — that is 0 everywhere, and NOT the parts sum). Some vehicles (Zero, Bongo Bus, Nimo Taxi, …) genuinely have none → 0. |
| Drag | class default object `AirDragCoeff` |
| Comfort | Vehicles table `Comport` |
| Seats | count of `MTSeatComponent` exports in the blueprint |
| Drivetrain | wheel `DifferentialComponentName` (count driven axles: 0 = blank, front = FWD, rear = RWD, 2 = AWD) |
| Fuel capacity | CDO `FuelTankCapacityInLiter`; fuel type = engine `EngineProperty.FuelType` (default Gasoline) |
| Part name | `Name`/`Name2` localized; `#N` names get ` (Vehicle / Vehicle)` appended from `VehicleKeys` |
| Part cost/mass | `Cost`, `MassKg` on the VehicleParts row |

## Writing the review

`wiki/out/review.md` is hand-written by you, not generated. Base it on
`validation.json` but add judgment:

- Group claims by surface (comparison table, infobox, specs, installable parts).
- Distinguish real errors from known-good cases (e.g. Zero genuinely 0 kg;
  Bongo Bus/Nimo Taxi are broken unused assets — their gaps are acceptable).
- Check review-notes from earlier passes (review*.md in the repo root) for
  known-fixed / still-broken items.
- Do NOT fetch or compare against other sites unless explicitly asked.

## Gotchas

- The wiki regenerates; re-fetch with `--validate` (pages are cached in
  `wiki/out/pages/` — delete the cache to force a fresh fetch).
- Slug mismatches are common: wiki slugs are lowercase
  (`anglekit_5`, `rideheight_p1`), pak keys are PascalCase (`AngleKit_5`,
  `RideHeight_+1`, `FD_1.33`). The validator resolves these.
- Trailer rows with raw pak keys (`Trailer_*_VehicleName`) or GUID slugs are a
  known wiki generator bug — flag them, don't silently match.
- The wiki may name parts with the vehicle augmented (`#1 (Dory / Dory Wrecker)`)
  — the extractor replicates this; both sides must agree.
