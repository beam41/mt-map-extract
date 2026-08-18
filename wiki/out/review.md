# Wiki review 1–4 re-validation — findings (2026-08-18, live)

Fresh live-wiki run: `wiki/validate --validate` with an empty page cache re-fetched every
page (768 part pages, 168 vehicle pages, 168 installable-parts pages, 3 list pages) on
2026-08-18. Ground truth: `wiki/out/out_vehicle_part.json` / `out_vehicle_data.json`
from the pak. Machine-readable claim dump: `wiki/out/validation.json` (510 claims). This
file is the hand-written findings summary; the claim table is not repeated here.

## The embedded question: is `parts:anglekit_5` validated?

**Yes — now it is.** Part detail pages were never validated before this run (only the
`list_of_parts` rows were). The validator now fetches all 768 `parts:<slug>` pages and
compares their infobox, Specifications, and Stats tables against the pak.

`parts:anglekit_5` itself: **no claims** — correct on every checked surface:

| Surface | Wiki shows | Pak |
|---|---|---|
| infobox name / Cost / Mass | `+5` / `10,000` / `5 kg` | AngleKit_5, 10000, 5.0 kg |
| Stats → Angle Kit → Angle Increase | `5 deg` | `AngleKit.AngleIncreaseInDegree 5.0` |

## Fix status by review round

### Review 1 — sort order: mostly fixed, 1 table still wrong

Was 23 unsorted tables; re-checked the fresh live `list_of_parts` with the review1
comparator (numeric names first, then alpha with embedded digit runs):

- **Transmission — still unsorted (1 remaining):** `13 Speed` and `18 Speed` sit before
  `4 Speed Mini Bus` (adjacent inversion `18 Speed` > `4 Speed Mini Bus`; both should sort
  after the `4–6 Speed` block, before `Bike 6 Speed`).
- **Limited Slip Differential — now correct.** The earlier "2 Way before Lockable"
  concern was a comparator artifact: digits sort before letters, so `2 Way Clutch Pack
  LSD (100)` before `Lockable` is the right order. Verified 0 inversions.

### Review 2 — display rules: applied; part-page stats: mostly right

All review2 display rules verified present: multipliers as `±%` (Anti-Roll Bar `-50%`,
Brake Power `+30%`, Brake Pad `±0%/+10%/-30%`), tire grip `0.97 G / 0.87 G`, `N/m`,
`N·s/m`, winch `m`, aero `+22.5%` (×1.5 lift rule), lift `-7561 (214.7 kg downforce
@ 200 km/h)`, LSD `Clutch Pack LSD`, sign typos fixed (`-1.5%`, `-2%`).

Remaining part-page claims: **63**, all in two groups:

1. **Transmission pages are missing the review2 extractor fields (51 claims).** The pak
   transmits `DevComment` (Inspiration), `ClutchType`, `AutoShiftComportRPM`, and `Type`
   (`EatonFuller13/18`, `CVT`), but **no** `parts:<transmission>` page renders them.
   Every transmission page is affected:

   - **Inspiration missing (19 pages)** — e.g. `4speedmanual_daffy` should show
     `Citroen 2CV 6`, `5speedsports` → `Ford Fiesta 1.25 JA8 Facelift 82ps, (2013 - 2017)`,
     `6speedsportslp670` → `Murcielago LP670`, `302` → `Ford 4R70W (Mustang)`,
     `301` → `Toyota T 40 (AE86)`, `ef18_01` → `RTO-15618`, `bike6speed` → `CBR 250`,
     `bike6speedsport` → `R1`, `hm_minetruck_7speed` → `769D`, `hd_forklift_3speed` → `TE17`.
   - **Clutch Type missing (15 pages)** — every listed transmission has
     `MultiPlateClutch` in the pak; `hm_minetruck_7speed` has `TorqueConvertorV2`.
   - **Comfort Autoshift RPM missing (14 pages)** — 2000/2500/3500/5000 rpm in pak.
   - **Type missing (3 pages)** — `ef13_01` → `EatonFuller13`, `ef18_01` → `EatonFuller18`,
     `scooter2speed` → `CVT`.

   The wiki's Transmission Physics block still shows only the original field set
   (Torque Converter Stall RPM/Ratio, Default Gear, Gears, Shift Time, Torque Converter
   Torque Rate). This is the review2 "extractor additions on wiki pages" item **still not
   applied to transmissions** (engines got the new fields; transmissions did not).

2. **EV engines show zero-value rows the pak omits (12 claims).** `electric_130hp` /
   `electric_300hp` / `electric_670hp` pages render `Starter Torque 0 N·m`, `Idle Throttle
   0%`, `Blip Throttle 0`, `Starter RPM 0 rpm` — the pak engine object lacks those keys
   (zeros). Cosmetic: the values are correct, but the rows exist on the wiki and not in
   the pak. Not worth changing either side; flagged for completeness. (The remaining claim
   is `electric1speed_01` Clutch Type, part of the transmission group above.)

### Review 3 — missing vehicles: added, but junk rows and 0-kg weights remain

- **All 9 missing vehicles are now present** (list + comparison table): Civo, Elisa 2,
  Elisa 2 Police, Longhorn Semi DC 4x2, Jemusi Flatbed, Atlas 6x2 Garbage, Goliath-4/6/10.
- **Jemusi renamed to "Jemusi Logger"** — correct.
- **NOT fixed — 5 junk rows still in `list_of_vehicles`:**
  - 2 GUID-keyed rows: `[[vehicles:b623ce…|Trailer_Cotra_20_3_VehicleName]]` and
    `[[vehicles:6744ad…|Trailer_Cotra_40_3_VehicleName]]` (raw FName, no pak match).
  - 3 merged trailers: `trailer_shobed`, `trailer_shotan`, `trailer_shovan`
    (pak keys are `Shobed_7`/`Shobed_10`, `Shotan_7`/`Shotan_10`, `Shovan_7`/`Shovan_10`).
- **`kuda_` broken slug still there** — cost `130,000` vs pak `220,000`, chassis weight
  `5,050 kg` vs `5,600 kg` (wiki row is a mangled merge; pak key is `Kuda_Flatbed`).

### Review 4 — live validation: drag, drivetrain, chassis weight still wrong

- **Drag coefficient: wrong on 271 surfaces (138 comparison + 133 infobox).** The
  comparison table shows `1.0` for 138 vehicles where the pak has a real value
  (0.22–0.9); the infobox shows `1.0` for 133 vehicles. Examples: `micky` 0.34,
  `raton` 0.22, `zydro` 0.23, `duke` 0.35, `spider` 0.25, `neo` 0.33. A few vehicles are
  correct — the four 30-foot trailers (0.8) and `trophy_air` (0) in the infobox. The
  remaining 30 comparison rows are unclaimed only because the gathered pak data has no
  `dragCoeff` for them (CDO not exported) or it is exactly 1.0 — not because they were
  verified. Notably the comparison table shows `1.0` even for the trailers whose infobox
  correctly says 0.8. This is the biggest single defect.
- **Drivetrain: 60 blank + 3 wrong in the comparison table.** Blank: `gunthoo`,
  `scooty`, `zero`, `civo`, `vulcan`, … (pak says RWD/FWD/AWD). Wrong: `hana`, `ranchy`,
  `voltex` show `Rear-wheel drive`, pak says `AWD`. (The 4 user-confirmed broken assets
  `bongo_bus`/`nimo_taxi`/`nuke_taxi`/`townie_bus` + `elisa_2_police` show RWD where pak
  has no drivetrain — known-good, not flagged.)
- **Chassis Weight `0 kg` on 12 vehicles** (comparison + Specifications + infobox):
  `civo` (9100), `elisa_2` (1570), `elisa_2_police` (1720), `longhorn_semi_dc_4x2` (7200),
  `atlas_6x2_garbage` (17900), `jemusi_flatbed` (6100), `goliath_4` (15000),
  `goliath_6` (22000), `goliath_10` (36000), `kuda_container_6x2` (6000),
  `cotra_20_3l` (3000), `small_cage_trailer` (500). Pak `weightKg` values in parens —
  all nonzero. Includes every review3 vehicle except Zero.
- **Kart and Trophy Air pages are gutted.** Review4 said kart/trophy_air were removed;
  they are back but with **no Specifications, no Capabilities, no Default Parts sections**
  (only infobox + Axle info). Default Parts slot claims for both: `kart` should have
  `Kart1Speed` / `110` / `KartEngine_10HP` / `SmallRadiator_100` / `LSD_Locked` /
  `BasicTire_45,BasicTire_65` / `Kart` / `DefaultBody` / `BrakePad_Small_01`;
  `trophy_air` should have `302` / `109` / `SmallBlock_90HP` / `TaxiLicense0` / `Trophy`
  wheels etc. Capabilities: `kart` → `Race Car`, `trophy_air` → `Taxi` — both absent.
- **The four 30-foot trailers are gutted the same way.** `30_foot_container_trailer`,
  `30_foot_dry_van_trailer`, `30_foot_log_trailer`, `30feet_tanker_trailer` pages exist
  (infobox correct: weight 4,000 kg, drag 0.8) but have no Specifications / Default Parts
  sections; pak default parts (e.g. `BasicHeavyDutyRearTire`) are not shown.
- **Capabilities label drift (4 claims).** Wiki `Limousine` vs pak `Limo`
  (`mammoth`, `monarch_limo`, `nimo`, `nimo_taxi`) — the wiki spells it out; pak short
  form. Cosmetic label choice, not a data error. `brutus_fire_engine` / `brutus_tanker` /
  `dinky_tanker` / `jemusi_tanker` wiki "Has fuel pump" and 12 trailer "Can haul trailer"
  rows have no pak counterpart — wiki-only capabilities, not flagged as errors.

## What the validator checks now (coverage)

- `list_of_parts`: name/cost/mass for all 768 rows — clean (0 claims).
- `list_of_vehicles`: slug ↔ pak key — 5 junk rows above.
- `vehicle_comparison`: cost (1 claim: `kuda_`), drivetrain, chassis weight, drag.
- Every `vehicles:<slug>` page: infobox (Weight, Drag coefficient), Specifications
  (Chassis Weight, Drivetrain, Engine, Transmission), Capabilities, Default Parts
  (grouped base slots).
- Every `parts:<slug>` page: infobox (name/Cost/Mass), Specifications, Stats tables —
  engine, transmission, tire, LSD, aero, brakes, suspension, intake, radiator, turbo,
  wheel spacer, winch, cargo bed, fuel tank, taxis.

## Known-good (validated, not flagged)

- `parts:anglekit_5` and all other Angle Kit pages.
- Aero display rules (`+22.5%`, lift kg, `-2.25%` air drag on `elisa2_rearspoiler_02`).
- Default Gear / Gears on transmissions (index + gear-ratio rounding verified).
- Cost on all vehicles except `kuda_`.

## Action list for the wiki maintainer

1. **Drag coefficient** — populate from pak `AirDragCoeff` in infobox + comparison
   (271 rows).
2. **Transmission part pages** — add Inspiration / Clutch Type / Comfort Autoshift RPM /
   Type rows (51 rows across 19 pages).
3. **Chassis Weight** — fill the 12 `0 kg` rows from `weightKg`.
4. **Drivetrain** — fill 60 blank comparison rows; fix `hana`/`ranchy`/`voltex` to AWD.
5. **Rebuild kart, trophy_air, and the four 30-foot trailer pages** with
   Specifications / Capabilities / Default Parts.
6. **Remove 5 junk rows** from `list_of_vehicles` (2 GUID, 3 merged trailers); restore
   `kuda_flatbed`; fix `kuda_` cost.
7. **Sort the Transmission table** (`13 Speed`/`18 Speed` before `4 Speed Mini Bus`).
