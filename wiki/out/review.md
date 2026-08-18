# Wiki review (for the wiki generator agent)

This review reports wiki mismatches against the game pak (extracted 2026-08-18). All
correct values are inline; nothing here requires the pak or any local tooling. The
machine-readable claim list (`validation.json`: `{source, vehicle, field, wiki, pak}`)
accompanies this review; each task names which claim rows it clears. Rows in the claim
list that no task covers are display wording, not errors.

Your job is to apply the fixes described below, in order. After each task, confirm the
named claim rows are gone and the listed values render on the page.

---

## Task 1 — Drag coefficient: render pak values (271 claims)

**What's wrong:** the infobox `Drag coefficient` field and the comparison-table `Drag`
column show `1.0` for vehicles whose pak value is 0.22–0.9. Comparison table: 138 rows.
Infobox: 133 pages.

**Fix:** render the pak `AirDragCoeff` as a plain number (1 decimal): `0.5`, `0.8`,
`0.34`. For vehicles with no pak value (30 comparison rows), leave the current value —
do not invent one.

**Already correct (do not touch):** `trophy_air` infobox `0`; the four 30-foot trailer
infoboxes `0.8` — but their comparison rows wrongly show `1.0`; fix those.

**Verify:** claim rows with `field == "drag"` or `field == "Drag coefficient"` → none left.

## Task 2 — Transmission part pages: add 4 missing rows (51 claims)

**What's wrong:** every `parts:<transmission>` page renders only the original block
(Torque Converter Stall RPM/Ratio, Default Gear, Gears, Shift Time, Torque Converter
Torque Rate). The pak has four more fields that engine pages already render.

**Fix:** add these rows to the transmission Stats block (omit a row when the asset lacks
the field):

| Wiki row | Value from pak | Render as |
|---|---|---|
| Inspiration | `DevComment` text | raw text, e.g. `Citroen 2CV 6`, `RTO-9513 (FLT)`, `CBR 250`, `769D`, `Murcielago LP670`, `Ford 4R70W (Mustang)`, `Toyota T 40 (AE86)`, `R1`, `Eaton Fuller FS-4106A`, `TE17` |
| Clutch Type | `ClutchType` enum | humanized tail: `MultiPlateClutch` → `Multi Plate Clutch`; `TorqueConvertorV2` → `Torque Converter V2` |
| Comfort Autoshift RPM | `AutoShiftComportRPM` | `{n} rpm` (2000/2500/3500/5000) |
| Type | `Type` enum tail | `EatonFuller13`, `EatonFuller18`, `CVT` (3 parts: 13 Speed, 18 Speed, Scooter CVT) |

**Verify:** claim rows with `source` starting `parts:` and `field` in (Inspiration,
Clutch Type, Comfort Autoshift RPM, Type (transmission)) → none left.

## Task 3 — Chassis Weight: fill the 12 zero rows

**What's wrong:** `Chassis Weight` shows `0 kg` in the comparison table, infobox
(`Weight`), and Specifications for 12 vehicles; the pak has nonzero values.

**Fix:** render `{n} kg` with thousands separators from these pak values:

| Vehicle | Chassis weight | Vehicle | Chassis weight |
|---|---|---|---|
| Civo | 9,100 kg | Goliath-4 | 15,000 kg |
| Elisa 2 | 1,570 kg | Goliath-6 | 22,000 kg |
| Elisa 2 Police | 1,720 kg | Goliath-10 | 36,000 kg |
| Longhorn Semi DC 4x2 | 7,200 kg | Kuda Container 6x2 | 6,000 kg |
| Atlas 6x2 Garbage | 17,900 kg | Cotra 20 3L | 3,000 kg |
| Jemusi Flatbed | 6,100 kg | Small Cage Trailer | 500 kg |

Do not touch Zero (genuinely 0). Also fix the `Total Weight` column (see Task 8.2).

**Verify:** claim rows with `field` in (chassisWeight, Weight, Chassis Weight) → none left.

## Task 4 — Drivetrain: fill blanks, fix 3 wrong (63 claims)

**What's wrong:** comparison-table `Drivetrain` column blank on 60 rows; 3 rows show
`Rear-wheel drive` but the pak is AWD.

**Fix:** derive from the pak's driven-axle count: 0 → blank, 1 front → FWD, 1 rear →
RWD, 2 → AWD. The 3 wrong are **Hana, Ranchy, Voltex** — all 2 driven axles →
`All-wheel drive`. (The broken assets Bongo Bus, Nimo Taxi, Nuke Taxi, Townie Bus +
Elisa 2 Police show RWD with no pak drivetrain — leave them.)

**Verify:** claim rows with `field == "drivetrain"` → only the 5 broken-asset rows may remain.

## Task 5 — Rebuild the 6 gutted vehicle pages

**What's wrong:** `kart`, `trophy_air`, `30_foot_container_trailer`,
`30_foot_dry_van_trailer`, `30_foot_log_trailer`, `30feet_tanker_trailer` pages have only
infobox + Axle info; Specifications / Capabilities / Default Parts sections are missing.

**Fix:** populate from the pak default parts:

- **Kart**: Transmission `Kart1Speed`, FinalDriveRatio `110`, Engine `KartEngine_10HP`,
  CoolantRadiator `SmallRadiator_100`, LSD `LSD_Locked`, Tire `BasicTire_65×2,
  BasicTire_45×2`, Wheel `Kart×4`, Body `DefaultBody`, BrakePad `BrakePad_Small_01×4`;
  capabilities `Race Car`.
- **Trophy Air**: Transmission `302`, FinalDriveRatio `109`, Engine `SmallBlock_90HP`,
  CoolantRadiator `SmallRadiator_100`, LSD `LSD_Clutch_1.5_50_30`, Tire `BasicTire_65×2,
  BasicTire_45×2`, TaxiLicense `TaxiLicense0`, Wheel `Trophy×4`, Body `DefaultBody`,
  BrakePad `BrakePad_Small_01×4`; capabilities `Taxi`.
- **Four trailers** (30 Foot Container / Dry Van / Log, 30ft Tanker): Tire
  `BasicHeavyDutyRearTire×4`, Wheel `HD1×4`, Body `DefaultBody`,
  BrakePad `BrakePad_Heavy_01×4`; no capabilities.

Match the section structure of a healthy page (e.g. `vehicles:goliath_4`).

**Verify:** claim rows with `field` starting `slot ` or `field == "capabilities"` → none left.

## Task 6 — list_of_vehicles: remove 5 junk rows, fix kuda_

**Fix:**
- Delete the 2 GUID-keyed rows (`Trailer_Cotra_20_3_VehicleName`,
  `Trailer_Cotra_40_3_VehicleName`) — raw pak FNames, not real vehicles.
- Split the 3 merged trailers back into variants: `trailer_shobed` →
  `Shobed_7`/`Shobed_10`, `trailer_shotan` → `Shotan_7`/`Shotan_10`, `trailer_shovan` →
  `Shovan_7`/`Shovan_10` (7 and 10 are distinct vehicles).
- Restore `kuda_` → `kuda_flatbed`; fix cost `130,000` → `220,000` and chassis weight
  `5,050 kg` → `5,600 kg`.

**Verify:** claim rows with `source == "list_of_vehicles"` or `vehicle == "kuda_"` → none left.

## Task 7 — Sort the per-type tables in list_of_parts

**What's wrong:** the Transmission table has `13 Speed` and `18 Speed` before
`4 Speed Mini Bus`.

**Rule:** sort by displayed part name — a name that parses entirely as a number
(`50%`, `+5`, `1.8`) compares numerically; otherwise alphabetical with embedded digit
runs compared as integers (`F50` < `F60` < `F110`, `KM1-65` < `KM2-45`); digits sort
before letters. Applies to **every** row, including late-added ones — interleave a
late-added row at its sorted position, never append it at the end of its block. Correct
Transmission order starts: `4 Speed Mini Bus, 4 Speed Muscle, 4 Speed Sports, 5 Speed
Sports, 5 Speed Truck, 6 Speed Bus, 6 Speed Light Bus, 6 Speed Sports, 6 Speed Truck,
6 Speed Truck Mk1, 13 Speed, 18 Speed, Bike 6 Speed, …`.

**Verify:** re-run the sort check on the Transmission table and spot-check Bonnet,
Fender, Front Bumper, Roof, Side Skirt, Wheel (these previously had appended-at-end
Elisa 2 / Trophy2 / Longhorn Semi DC 4x2 variants) → 0 inversions everywhere. This is a
manual check — the claim list has no sort rows.

---

## Task 8 — Findings NOT in the claim list (automated checks do not cover these)

The claim list only covers name/cost/mass/drag/drivetrain/weight/slots/capabilities.
The following were found by direct page inspection — verify and fix them manually.

### 8.1 Empty Stats sections (146 part pages)

**What's wrong:** 146 part pages render `===== Stats =====` with **zero rows**. These
parts have no numeric stats in the pak (cosmetic-only parts), so nothing is flagged, but
the empty section is noise.

**Fix:** omit the Stats section entirely when the part has no stat rows. Affected types
and counts: Wheel 93, Bonnet 33, Headlight 6, Rear Window Louvers 4, Front Window Sun
Visor 3, Utility 3, Front Window Sticker 2, Cargo Bed Attachment 1, Trunk 1.
Examples: `parts:atlas`, `parts:corawheel_02`, `parts:cora_headlight_01`,
`parts:bongo_sparetire`, `parts:dory_bonnet_01`.

**Verify:** no `parts:<slug>` page has a `===== Stats =====` heading with an empty table.

### 8.2 Total Weight column formula

**What's wrong:** every comparison-table `Total Weight` value equals
`Chassis Weight + 2 × (default parts mass) + 6 kg` — parts are double-counted and the +6
is unexplained (e.g. Hana: 1,500 + 2×413 + 6 = 2,332).

**Fix:** total should be `Chassis Weight + Σ default parts mass` (recompute all rows, not
just the 12 from Task 3).

**Verify:** spot-check that `Total − Chassis` equals the sum of the listed default parts
for a few rows.

### 8.3 Comparison-table Type column

**What's wrong:** the `Type` column is never checked against the pak.

**Fix:** verify every row's Type matches the pak vehicle type (Small, Pickup, Truck,
Bus, Semi Trailer, ...). Known pak types: Kart → `Kart`, Hana → `Pickup`, the 30-foot
trailers → `Semi trailer, Heavy duty`, Trophy Air → `Small`.

**Verify:** Type column matches the pak for all 168 rows.

### 8.4 Vehicle infobox: Comfort / Fuel / Seats / Drivetrain

**What's wrong:** the infobox is only checked for `Weight` and `Drag coefficient`.
Comfort, Fuel, Seats, Drivetrain presence is unverified on all 168 vehicle pages.

**Fix:** every vehicle infobox must include `Comfort` (stars), `Fuel` (`{n}L
({Type})`), `Seats` (`{n}`), `Drivetrain`. Reference layout:
`vehicles:dabo?rev=1756720156`.

**Verify:** every `vehicles:<slug>` page has all four fields in the infobox.

### 8.5 Reverse direction: pak rows missing from wiki lists

**What's wrong:** the claim list only flags wiki rows that do not exist in the pak — it
never flags **pak vehicles/parts that are missing from the wiki lists entirely**.
Earlier this is how 9 vehicles (Goliath-4/6/10, Elisa 2/Police, Civo, Longhorn Semi DC
4x2, Jemusi Flatbed, Atlas 6x2 Garbage) went missing.

**Fix:** confirm every pak vehicle (171) appears in `list_of_vehicles` and every pak part
(768) appears in `list_of_parts`; add any missing row with the pak English name.

**Verify:** counts match (168+ listed vehicles, 768 listed parts) and no pak key is
absent.

### 8.6 Level requirement

**What's wrong:** the `Level requirement` field is never checked anywhere.

**Fix:** spot-check it renders the pak career-level gate (`CL_Driver`, `CL_Truck`,
`CL_Racer`, `CL_Wrecker`, ...) where present.

---

## Already correct — verify, then do not change

| Surface | What it should show (pak-confirmed) |
|---|---|
| `parts:anglekit_5` and all Angle Kit pages | infobox `+5` / Cost `10,000` / Mass `5 kg`; Stats → `Angle Increase 5 deg` |
| Aero display on aero parts | multipliers as `±%` (`+22.5%` when raw `1.15` ×1.5 lift rule; `-2.25%` on `elisa2_rearspoiler_02`); lift as `coef (kg @ 200 km/h)` |
| Tire stats | grip `0.97 G / 0.87 G`, springs `N/m`, damping `N·s/m`, `Dual Rear Yes/No` |
| Brake stats | `±%` (`+30%`, `±0%`, `-30%`), Fade Temperature `400 °C` |
| LSD pages | `Clutch Pack LSD` type label |
| Negative percentages | never a `+-` sign prefix — plain `-1.5%`, `-2%` |
| `list_of_parts` | all tables except Transmission (Task 7) are correctly sorted |
| `vehicle_comparison` cost | matches pak for every vehicle except `kuda_` (Task 6) |
| Capabilities | wiki may spell out `Limousine`, `Can haul trailer`, `Has fuel pump` — display wording, not data errors |
| EV engine pages | zero-value rows (`Starter Torque 0 N·m`, `Idle Throttle 0%`, `Blip Throttle 0`, `Starter RPM 0 rpm`) are intentional — the wiki renders the full engine schema, the pak omits zeros |

## Data conventions (read before editing any page)

- **Key casing:** pak keys are PascalCase (`AngleKit_5`, `RideHeight_+1`, `FD_1.33`),
  wiki slugs are lowercase (`anglekit_5`, `rideheight_p1`, `fd_1_33`). Match keys
  case-insensitively; a casing difference is not a missing part.
- **Absent fields:** a pak field absent from an asset means the part uses the game's
  default value. Never invent rows the pak does not have.
- **Engine pages** already render the review2 field set correctly (Starter RPM, Fuel
  Type, Engine Type, Heating Power, ...). When adding the transmission rows (Task 2),
  copy the engine pages' field order and units.
- **Chassis weights** come from `BodyInstance.MassInKgOverride` summed over the
  vehicle's blueprint — never from the Vehicles table `CurbWeight` (0 on every row) or a
  sum of part masses.
- **Claim-list wording:** `wiki: "(missing row)"` = the wiki lacks a row the pak has;
  `"(wiki only)"` = the wiki shows a row the pak lacks; `"(blank)"` / `"(none)"` = empty
  cell/section.
