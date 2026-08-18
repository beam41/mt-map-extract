---
name: wiki-validate
description: |
  Validate the ASEAN Motor Club wiki (https://wiki.aseanmotorclub.com) against the
  Motor Town pak data in this repo. Triggers: "validate wiki", "check wiki",
  "missing vehicle", "wrong weight", "verify part", "wiki review", "wiki validation",
  "compare wiki to game data", "installable parts check", "fix wiki".
  Run `dotnet run -c Release --project wiki/validate` first to gather pak data, then
  `-- --validate` to fetch wiki pages and produce wiki/out/validation.json.
---

# Wiki Validation (ASEAN Motor Club)

This repo extracts Motor Town game data from `resource/MotorTown-Windows.pak` and can
validate the AMC wiki against it. Use this skill whenever asked to check, verify, fix,
or review anything on the wiki (parts, vehicles, comparison tables, weights, costs,
names, installable parts).

Durable ground truth (pak data sources, game-UI display rules, pak-side data facts)
lives in `.agents/knowledge/wiki-base-assertions.md` — read it before interpreting
results. Current wiki state is in `wiki/out/validation.json` + `wiki/out/review.md`,
not in the assertions file.

## The pipeline (everything lives under `wiki/`, NOT `out/`)

| Path | Purpose |
|---|---|
| `wiki/validate/` | the whole wiki pipeline, one C# project (`wiki/validate/wiki.csproj`): `PartExtractor.cs` (part/vehicle extraction + per-part pages), `Program.cs` (gather + validate modes), `Validator.cs` (wiki checks) |
| `wiki/out/out_vehicle_part.json` | every part: name/cost/massKg/restrict/stats (gather mode output) |
| `wiki/out/out_vehicle_part_type_name.json` | localized part-type names (gather mode output) |
| `wiki/out/out_vehicle.json` | every vehicle with restriction fields + default parts (gather mode output) |
| `wiki/out/parts/<key>.json` | one human-readable page per part, `type` translated (gather mode output) |
| `wiki/out/out_vehicle_data.json` | gathered per-vehicle stats: weight, axles, drag, fuel, seats (gather mode output) |
| `wiki/out/validation.json` | machine-readable list of every incorrect claim found |
| `wiki/out/review.md` | hand-written review for the wiki generator agent (see "Writing the review") |
| `wiki/out/review-extra.md` | record of validator-blind findings that were later automated (kept as history) |
| `wiki/out/pages/` | cached fetched wiki pages (raw exports) |

`out/` is the MAIN extractor's output (map data, tiles). Do not write wiki validation
output there. There is no separate `parts/` project — extraction is gather mode of the
wiki pipeline.

## Commands

```bash
# 1. Gather everything from the pak (needs resource/MotorTown-Windows.pak + aes + Mappings.usmap)
dotnet run -c Release --project wiki/validate

# 2. Fetch wiki pages + validate everything
dotnet run -c Release --project wiki/validate -- --validate

# Optional: different output dir
dotnet run -c Release --project wiki/validate -- --validate --wiki-out wiki/out
```

Gather mode (default) writes the four part/vehicle JSONs, the per-part pages under
`wiki/out/parts/`, and `out_vehicle_data.json` — all from one pak mount. `--validate`
additionally fetches the wiki live and writes `wiki/out/validation.json` — one JSON
object per incorrect claim: `{source, vehicle, field, wiki, pak}`.

## What the validator checks

- **`list_of_parts`** — every part: name (English, incl. the `#1 → "#1 (Vehicle)"`
  augmentation), cost, mass. All 768 parts. Plus the reverse direction: every non-hidden
  pak part must appear in the list (`(not listed)` claims).
- **`list_of_vehicles`** — every vehicle: English name + existence in pak. Plus the
  reverse direction: every pak vehicle must appear in the list.
- **`vehicle_comparison`** — cost, type, drivetrain, chassis weight, **total weight**
  (must equal `weightKg + Σ default part masses` — the wiki's `+2×parts+6` formula is a
  bug), drag for all rows.
- **Per-part pages (`parts:<slug>`)** — every one of the 768 part detail pages:
  infobox (`name`, `Part Type`, `Cost`, `Mass`), `Specifications`, and every `Stats`
  table (engine, transmission, tire, LSD, aero, brakes, suspension, intake, radiator,
  turbo, wheel spacer, winch, cargo bed, fuel tank, taxis). Value formatting matches
  the wiki generator: `±%` multipliers, `G`/`N/m`/`N·s/m` units, aero lift
  `coef (kg @ 200 km/h)` (kind word only on the whole-vehicle row), Air Drag omitted at
  multiplier 1.0, gear ratios as `F2` with trailing zeros stripped, `Default Gear` as
  the raw `DefaultGearIndex`, vector axes within ±0.01 → `0`. EV engine zero rows
  (`Starter Torque 0 N·m`, ...) are expected — the wiki renders the full engine schema,
  the pak omits zeros; do not flag. A `===== Stats =====` heading with zero rows on a
  no-stat part is flagged (`empty stats section`).
- **Per-vehicle pages** — infobox (`Weight`, `Drag coefficient`, `Type` in sentence
  case + truck class, `Comfort` as stars, `Fuel` `{n}L ({Type})`, `Seats`, `Drivetrain`
  spelled or abbreviated, `Level requirement` `Driver: 2`), `Specifications` (`Engine`,
  `Transmission`, `Drivetrain`, `Chassis Weight`), `Capabilities` (Taxi/Bus/Limo/Race
  Car), `Default Parts` (slot → part set), `Installable Parts` (fit rule vs pak
  restrictions).

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

`wiki/out/review.md` is hand-written by you, not auto-generated. It is a **review**, not
a change log: your job is to review and report, the wiki generator agent does the fix.
Never assume you also fix the wiki or run its tooling.

The review must be **self-contained** — the wiki generator agent does not have this repo,
the pak, or the validator. Rules:

- **Inline every correct value** (page slug, field, exact value). Do not point at repo
  files (`wiki/out/…`), pak keys, or commands (`dotnet …`, `jq` over repo files).
- The only external artifact is the **co-review claim list** (`validation.json`, data,
  not a path you tell them to run): reference claim fields (`source`/`vehicle`/`field`)
  so they can match rows, but the review must make sense without it.
- Write numbered fix tasks: what's wrong → the correct value → where it goes → how to
  verify (which claim rows should disappear, or a page-level check).
- Add a section for findings the automated checks do **not** cover. The validator now
  catches most things (empty Stats sections, Total Weight formula, comparison Type,
  infobox Comfort/Fuel/Seats/Drivetrain/Level, reverse-direction missing rows — see
  `review-extra.md` for the mapping), so this section is for genuinely manual checks
  (multi-level vehicles, broken-asset drivetrains, page structure not covered by a
  claim). Do not re-list automated findings as manual tasks.
- Include an "already correct" section: surfaces verified against the pak that must not
  change, with the exact pak-confirmed values.
- Include a data-conventions section (key casing, absent-field policy, chassis-weight
  source, what `(missing row)` / `(wiki only)` / `(blank)` mean) so an agent with no
  repo history can act.
- Distinguish real errors from known-good cases (e.g. Zero genuinely 0 kg; Bongo
  Bus/Nimo Taxi are broken unused assets — their gaps are acceptable).
- Do NOT fetch or compare against other sites unless explicitly asked.

Update the review only when the wiki or the validation results change; do not paste the
raw `validation.json` rows into it — point at the claim list instead.

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
- `wiki/validate/` links `AssetSource.cs`/`Options.cs`/`Localization.cs`/`CargoKeys.cs`
  from the repo root via `<Compile Include="../../…">` — shared code changes affect it.
