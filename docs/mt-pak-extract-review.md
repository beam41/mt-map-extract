# mt-pak-extract extraction review (2026-08-19, read-only)

Code review of https://github.com/ASEAN-Motor-Club/mt-pak-extract (Rust pak extractor +
`csharp/UAssetTool` parser + `scripts/aggregate_to_sqlite.py` + `scripts/wiki_sync.py`)
against the validated pak ground truth in this repo. No changes were made to that repo.
Purpose: decide which wiki deviations are explainable by its extraction (and therefore
allowed) vs. which are genuine pak-data facts.

## Architecture

1. Rust (`src/main.rs`, repak): decrypts/compresses the pak and writes raw
   `.uasset`/`.uexp` files (no parsing).
2. C# `UAssetTool` (`--batch`): parses each asset with a patched UAssetAPI fork
   (BoolProperty 0-byte CDO encoding, CDO-only IsTopLevel, EnumProperty CdoTopLevel —
   the real UE5.5 unversioned pitfalls) into `out/*_parsed.json` (DataTable rows, blueprint
   exports).
3. `aggregate_to_sqlite.py`: builds `motortown.db` (vehicles, vehicle_parts, part_tuning,
   cargo_bed_specs, vehicle_cargo_space, vehicle_weights, cargo_weights, delivery_points,
   …).
4. `wiki_sync.py`: renders the wiki pages from the db.

## Correct (matches this repo's ground truth)

- Cargo-space size: `2 × BoxExtent(cm) × RelativeScale3D` from
  `MTVehicleCargoSpaceComponent` (plus `bFixCargo`, `bUnlimitedHeight`, `DumpVolume`).
- Delivery payment: `DeliveryBasePayment` / `DeliveryPaymentMultiplier` on the Vehicles
  table.
- Vehicle flags: taxiable/limoable/busable/race_car/trailer_hauling/fuel_pump/hidden.
- Engine HP from the part id (`SmallBlock_240HP` → 240).
- Cargo fields: payment/multiplier/distance/fragile/stacking/deprecated, space types,
  blueprint weights.
- UAssetAPI fork fixes address the real UE5.5 unversioned serialization traps.

## Deviations (must be treated as bugs in that pipeline)

### Fabricated parts injected as pak data
`process_blueprint_variants` inserts ~550 rows that are NOT in the pak, from "live
observation": `RideHeight_±11..20` (20), `Spring 550/600/700/800/1000` (5), ~525
generated `FD*` ratios (loop 0.05→25.0), `EF6_4106` (1). The wiki's part list is exactly
768 pak parts; this pipeline would show ~1300.

### Missing extraction entirely
- **Drag**: no CDO `AirDragCoeff`; `wiki_sync` substitutes the default part's
  `AirDragMultiplier` (a different quantity).
- **Fuel tank + fuel type, seats, axles, LevelRequirementToDrive** — no schema columns.
- **Locres/localization** — no locres read at all: names are raw SourceStrings, no
  Name2 joins, no `#N (Vehicle)` augmentation, no In-other-languages.
- **Engine/transmission/LSD asset contents** — torque curves, gears, clutch type,
  tire physics: `part_tuning` covers 15 structs only; the tire physics asset path is
  stored as `hash(asset_name) % 1000000` (lossy numeric).
- **Delivery `DemandConfigs` + `PassiveSupplies`** — only `ProductionConfigs` read, so
  cargo "Consumed At" and `(passive)` rows are impossible.

### Broken reads (nested access against flat parser output)
- `WeightRange` → `weight_range.get("WeightRange")`: the parser emits `{X, Y}` directly
  → every cargo's weight range is 0 (the wiki's `3,000–30,000 kg` renders impossible).
- `BoxExtent`/`RelativeScale3D` → same nested pattern → non-50 extents silently default
  to 50.
- `GameplayTags` → `tags.get("GameplayTags")`: the parser returns a plain list → all
  vehicle tags empty → tag-query installable filtering breaks.
- Delivery config field names: the aggregate reads `ProductionTimeSeconds`,
  `InputCargos`, `OutputCargos` — those ARE the pak field names (verified against this
  repo's gather), so the recipe extraction itself is sound; only the demands/passives
  are missing.

### Minor
- `vehicle_weights` skips zero-mass exports and does not insert a row when the total is
  0 (Trophy_Air, Zero); masked by `COALESCE` in the views.
- `break` after the first cargo-space component per blueprint (this repo does the same —
  consistent).
- `Comport` as INTEGER column (sqlite affinity tolerates it).
- Part list reads multiple tables (VehicleParts, Engines, Transmissions, …) instead of
  only the master — rows outside the master would inflate the 768.

## Policy applied to the wiki generator

The generator in `wiki/` is the pak-direct right source. Where the live wiki
deviates from the pak, the deviation is allowed (generator keeps pak truth) when it is
explainable by the wiki being stale or generated from buggy extraction; it is fixed
(kept identical) when the wiki is correct. No mt-pak-extract edits; the allowed
deviations are enumerated in `docs/wiki-pages.md` ("Known wiki deviations").
