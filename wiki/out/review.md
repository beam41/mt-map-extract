# Wiki fix work order (for the wiki generator agent)

Co-review: `wiki/out/validation.json` — every claim below maps 1:1 to rows in it. Use
`jq` filters to pull the exact rows per task; the tables here give the fix rule, not the
full row dump. Data sources (this repo, fresh 2026-08-18):

- `wiki/out/out_vehicle_data.json` — per-vehicle ground truth (keyed by pak key:
  `Kart_01`, `Trophy_Air`, `Trailer_9m_Flat_01`, `Elisa2`, `Civo`, `1` = Hana,
  `Pickup_02` = Ranchy; resolve wiki slug → pak key via the `name.en` field).
- `wiki/out/out_vehicle_part.json` — per-part ground truth (keyed by pak part key,
  e.g. `EF13_01`, `4SpeedManual_Daffy`; `stats.transmission` holds the transmission
  asset object).

Workflow: apply a task → regenerate the affected pages → re-run
`dotnet run -c Release --project wiki/validate -- --validate` → confirm the task's
claims are gone from `validation.json` before moving on. Pages cache under
`wiki/out/pages/`; delete it for a fresh fetch.

---

## Task 1 — Drag coefficient: render pak values (271 claims)

**What's wrong:** infobox `Drag coefficient` and the comparison-table `Drag` column show
`1.0` for vehicles whose pak value is 0.22–0.9. Comparison table: 138 rows. Infobox:
133 pages.

**Source:** `out_vehicle_data.json` → `dragCoeff` (e.g. `Tuscan` → `0.3799999952316284`).
Render the raw number (1 decimal, e.g. `0.5`, `0.8`, `0.34`). When `dragCoeff` is absent
from the pak entry, leave the current value (30 comparison rows have no pak data — not
verifiable; do not fabricate).

**Already correct (do not touch):** `trophy_air` infobox `0`, the four 30-foot trailer
infoboxes `0.8` — but their comparison rows wrongly show `1.0`; fix those.

**Verify:** `jq 'select(.field=="drag" or .field=="Drag coefficient")'` → should be empty.

## Task 2 — Transmission part pages: add 4 missing rows (51 claims)

**What's wrong:** every `parts:<transmission>` page renders only the original block
(Torque Converter Stall RPM/Ratio, Default Gear, Gears, Shift Time, Torque Converter
Torque Rate). The pak has four more fields the engine pages already render.

**Source:** `out_vehicle_part.json` → `stats.transmission`:

| Wiki row | Pak field | Render rule |
|---|---|---|
| Inspiration | `DevComment` (19 assets) | raw text, e.g. `Citroen 2CV 6`, `RTO-9513 (FLT)`, `CBR 250`, `769D`, `Murcielago LP670` |
| Clutch Type | `ClutchType` (15 assets) | enum tail, humanized: `EMTTransmissionClutchType::MultiPlateClutch` → `Multi Plate Clutch`; `TorqueConvertorV2` → `Torque Converter V2` |
| Comfort Autoshift RPM | `AutoShiftComportRPM` (14 assets) | `{n} rpm` |
| Type | `Type` (3 assets: `EF13_01`, `EF18_01`, `Scooter2Speed`) | enum tail: `EatonFuller13`, `EatonFuller18`, `CVT` |

Omit a row when the pak asset lacks the field. Keep existing rows unchanged.

**Verify:** `jq 'select(.source|startswith("parts:") and (.field=="Inspiration" or .field=="Clutch Type" or .field=="Comfort Autoshift RPM" or .field=="Type (transmission)"))'` → empty.

## Task 3 — Chassis Weight: fill the 12 zero rows

**What's wrong:** `Chassis Weight` shows `0 kg` in the comparison table, infobox
(`Weight`), and Specifications for 12 vehicles; pak `weightKg` is nonzero.

**Source:** `out_vehicle_data.json` → `weightKg`. The 12 (wiki slug → pak value):

| Vehicle | weightKg | Vehicle | weightKg |
|---|---|---|---|
| Civo | 9100 | Goliath-4 | 15000 |
| Elisa 2 | 1570 | Goliath-6 | 22000 |
| Elisa 2 Police | 1720 | Goliath-10 | 36000 |
| Longhorn Semi DC 4x2 | 7200 | Kuda Container 6x2 | 6000 |
| Atlas 6x2 Garbage | 17900 | Cotra 20 3L | 3000 |
| Jemusi Flatbed | 6100 | Small Cage Trailer | 500 |

Render `{n} kg` with thousands separators. Do NOT invent values; do NOT touch Zero
(genuinely 0). Also fix the `Total Weight` formula bug: it is currently
`Chassis + 2 × (parts mass) + 6 kg` — the ×2 double-counts and +6 is unexplained; total
should be `Chassis + Σ parts mass` (recompute all rows, not just these 12).

**Verify:** `jq 'select(.field=="chassisWeight" or .field=="Weight" or .field=="Chassis Weight")'` → empty.

## Task 4 — Drivetrain: fill blanks, fix 3 wrong (63 claims)

**What's wrong:** comparison-table `Drivetrain` column blank on 60 rows; 3 rows show
`Rear-wheel drive` but pak is AWD.

**Source:** `out_vehicle_data.json` → `axles[]` with `driven` flags; count driven axles:
0 → blank, 1 front → FWD, 1 rear → RWD, 2 → AWD. The 3 wrong: **Hana, Ranchy, Voltex** —
all have 2 driven axles → `All-wheel drive`. (The 4 broken assets Bongo Bus, Nimo Taxi,
Nuke Taxi, Townie Bus + Elisa 2 Police show RWD with no pak drivetrain — leave as-is.)

**Verify:** `jq 'select(.field=="drivetrain")'` → empty (or only the 5 broken assets remain).

## Task 5 — Rebuild the 6 gutted vehicle pages

**What's wrong:** `kart`, `trophy_air`, `30_foot_container_trailer`,
`30_foot_dry_van_trailer`, `30_foot_log_trailer`, `30feet_tanker_trailer` pages have only
infobox + Axle info; Specifications / Capabilities / Default Parts sections are missing
(rendered empty or absent). 35 slot claims + 2 capabilities claims.

**Source:** `out_vehicle_data.json` (key → `defaultParts`, `flags`, `weightKg`):

- Kart (`Kart_01`): Transmission `Kart1Speed`, FinalDriveRatio `110`, Engine
  `KartEngine_10HP`, CoolantRadiator `SmallRadiator_100`, LSD `LSD_Locked`, Tire
  `BasicTire_65×2, BasicTire_45×2`, Wheel `Kart×4`, Body `DefaultBody`,
  BrakePad `BrakePad_Small_01×4`; capabilities `Race Car`.
- Trophy Air (`Trophy_Air`): Transmission `302`, FinalDriveRatio `109`, Engine
  `SmallBlock_90HP`, CoolantRadiator `SmallRadiator_100`, LSD `LSD_Clutch_1.5_50_30`,
  Tire `BasicTire_65×2, BasicTire_45×2`, TaxiLicense `TaxiLicense0`, Wheel `Trophy×4`,
  Body `DefaultBody`, BrakePad `BrakePad_Small_01×4`; capabilities `Taxi`.
- Four trailers: Tire `BasicHeavyDutyRearTire×4`, Wheel `HD1×4`, Body `DefaultBody`,
  BrakePad `BrakePad_Heavy_01×4`; no capabilities flags.

Match the section structure of a healthy page (e.g. `vehicles:goliath_4`).

**Verify:** `jq 'select(.field|startswith("slot ") or .field=="capabilities")'` → empty.

## Task 6 — list_of_vehicles: remove 5 junk rows, fix kuda_

**What's wrong:**
- 2 GUID-keyed rows: `[[vehicles:b623ce444b2bf6eb5e24c980a43bfa8e|Trailer_Cotra_20_3_VehicleName]]`,
  `[[vehicles:6744ad4242d1bff1330af885dd966aa6|Trailer_Cotra_40_3_VehicleName]]` — raw
  pak FNames, no real vehicle. Delete.
- 3 merged trailers: `trailer_shobed`, `trailer_shotan`, `trailer_shovan` — split back
  into variants `Shobed_7`/`Shobed_10`, `Shotan_7`/`Shotan_10`, `Shovan_7`/`Shovan_10`
  (7 and 10 are distinct vehicles, not sizes of one).
- `kuda_` broken slug — restore to `kuda_flatbed` (`Kuda_Flatbed`); fix cost
  `130,000` → `220,000` and chassis weight `5,050 kg` → `5,600 kg` (from
  `out_vehicle_data.json`).

**Verify:** `jq 'select(.source=="list_of_vehicles" or .vehicle=="kuda_")'` → empty.

## Task 7 — Sort the Transmission table in list_of_parts

**What's wrong:** `13 Speed` and `18 Speed` sit before `4 Speed Mini Bus`.

**Rule:** numeric-name sort (name parses as number → numeric compare; else alpha with
embedded digit runs numeric). Correct order starts: `4 Speed Mini Bus, 4 Speed Muscle,
4 Speed Sports, 5 Speed Sports, 5 Speed Truck, 6 Speed Bus, 6 Speed Light Bus, 6 Speed
Sports, 6 Speed Truck, 6 Speed Truck Mk1, 13 Speed, 18 Speed, Bike 6 Speed, …`.

**Verify:** re-run the sort check; 0 inversions.

---

## Do NOT touch (verified correct)

- `parts:anglekit_5` and all Angle Kit pages; aero display (`+22.5%`, lift kg,
  `-2.25%`); tire `G`/`N/m`/`N·s/m`; brake `±%`; LSD labels; sign typos (already fixed).
- EV engine pages' zero rows (`Starter Torque 0 N·m` etc.) — wiki renders the full
  engine schema; pak omits zeros. Cosmetic, both sides fine.
- Capabilities label spell-outs (`Limousine` vs pak `Limo`; `Can haul trailer`;
  `Has fuel pump`) — display choices, not errors.
- The 22/23 already-sorted list tables; cost on all vehicles except `kuda_`.

## Handoff notes

- Pak keys are PascalCase (`AngleKit_5`), wiki slugs lowercase (`anglekit_5`) — match
  case-insensitively.
- A pak field absent = editor default; never fabricate rows the pak lacks.
- Engine pages already render the review2 fields correctly — copy their field order and
  units when adding the transmission rows.
- Chassis weights must come from `weightKg` (BodyInstance.MassInKgOverride sum), never
  from the Vehicles table `CurbWeight` (0 everywhere) or a parts sum.
