# Wiki Base Assertions

Durable ground truth for the AMC wiki (`wiki.aseanmotorclub.com`) vs Motor Town pak data,
compacted from review rounds 1–4 + live validation 2026-08-18. Every assertion below was
verified against the pak and/or the live wiki; treat them as axioms, not claims.

## 1. Ground truth

- **The pak is the source of truth** (`resource/MotorTown-Windows.pak`, CUE4Parse,
  `GAME_UE5_5`), not the wiki, not other sites.
- **Chassis weight** = `BodyInstance.MassInKgOverride` summed over the vehicle's class
  blueprint exports. NOT the Vehicles table `CurbWeight` (0 on all rows), NOT the parts
  sum, NOT collision-geometry mass.
- **Drivetrain** = count driven axles from `MHWheelComponent.DifferentialComponentName`
  (0 driven = blank/none, 1 front = FWD, 1 rear = RWD, 2 = AWD).
- **Drag** = class default object `AirDragCoeff`.
- **Comfort** = Vehicles table `Comport`. **Seats** = `MTSeatComponent` export count.
- **Fuel capacity** = CDO `FuelTankCapacityInLiter`; fuel type = engine
  `FuelType` (default Gasoline).
- **Part name** = localized `Name`/`Name2`; `#N` names get ` (Vehicle / Vehicle)`
  appended from `VehicleKeys` (skip `None`). **Part cost/mass** = `Cost`, `MassKg`.
- **Cargo keys** are FNames matched case-insensitively (`Terra` == `terra`); always fold
  through `CargoKeys.Canonical`.
- A field **absent from an asset = editor default**; never fabricate it. But DO emit the
  part type's own stat struct even when it equals the default (BrakePad 400 °C fade,
  CoolantRadiator 6 L/100 %, Taxi Normal, CargoBed Flatbed) — the default IS the stat.
- Cargo keys, part slugs, and vehicle keys are case-insensitive; wiki slugs are lowercase
  (`anglekit_5`, `rideheight_p1`), pak keys PascalCase (`AngleKit_5`, `RideHeight_+1`,
  `FD_1.33`, `FD_15_HM`).

## 2. Value display rules (match the game UI, not raw values)

**Multiplier fields (default 1.0 = stock) → `±%` from 100.** AirDragMultiplier
(×1.5 when the part has any lift coefficient — see README), TrailerAirDrag,
FrontDamage, AntiRollBarRate, BrakePad Heating/Cooling/Wear, BrakePower,
Suspension spring/dampers, Turbocharger BaseTorque/Torque/IntakePressure/Heating/
FuelConsumption, IntakeSpeedEfficiency, CoolantRadiator CoolingPower, engine
CoolingEfficiency/HeatingPower/IntakeSpeedEfficency, BrakeBalance Front/Rear per
side, OffroadFriction. Example: `1.15` → `+15%`, `0.7` → `-30%`.

**Probabilities/ratios where 1.0 = 100 % → absolute `%` (×100).** AfterFireProbability
(uncapped: `2.0` → `200%`), IdleThrottle (`0.017` → `1.7%`), MaxRegenTorqueRatio
(`0.3` → `30%`), tire WearRate (`0.1` → `10%`).

**Plain number (no %)**: FinalDriveRatio, StallRatioPower, BaseRPMRatio, CVT gear
ratios, RollingResistanceCoeff, PatchLengthCoefficient (100k–1.2M), Friction
Viscosity/Coulomb, CoolDown/WarmUpSpeed, SmokeRate, BlipThrottle, FuelConsumption,
TurbineAspectRatio, TorqueRate, CVT_ClutchCurvePow, ClutchPackAccel/Brake (0–100
scale), MaxJakeBrakeStep, FrictionViscosityCoeff. `RollingResistanceCoeffV1` is
unused — skip.

**Units**: tire grip = μ relabeled `X G` (never %); springs `N/m`; damping `N·s/m`;
winch cable length cm → `m`; wheel spacer cm → `mm`; brake balance `±%` per side;
enums humanized from tail (`EMTLSDType::ClutchPackLSD` → `Clutch Pack LSD`,
`EMTTransmissionClutchType::MultiPlateClutch` → `Multi Plate Clutch`).
`+−x%` sign typos must never render.

**Wiki generator rendering specifics (match these exactly in validators)**:
- Gear ratios: `ToString("F2")` with trailing zeros stripped (`1.785`→`1.78`,
  `1.315`→`1.31`, `2.105`→`2.1`) — NOT `Math.Round`/`0.##`, which differ on exact
  halves. Default Gear = raw `DefaultGearIndex` (0-based).
- Aero lift: `coef (X kg downforce @ 200 km/h)` (whole-vehicle row includes
  downforce/lift word, force = `7.098e-7 × v² × coef`, one decimal); Front/Rear Aero
  Lift omit the word.
- Air Drag row omitted when multiplier == 1.0.
- EV engine rows: wiki renders the full engine schema including zeros the pak omits
  (`Starter Torque 0 N·m`, `Idle Throttle 0%`, `Blip Throttle 0`, `Starter RPM 0 rpm`);
  Motor Max Power/Voltage get `W`/`V` units.
- Tire page shows a fixed field set (Patch Length Coefficient, Static/Sliding Grip,
  Spring X/Y, Damping X/Y, Max Load, Dual Rear) — RollingResistance/WearRate/
  Offroad/Smoke/CoolDown/WarmUp rows are NOT rendered even when pak has them.
- Numbers: thousands separators (`6,400,000 N·m`), `kg`/`rpm`/`s`/`L`/`kL`/`deg` units
  as listed; cargo space as `X cm × Y cm × Z cm` with near-zero axes → `0`.

## 3. Known exceptions (do NOT flag)

- **Zero**: genuinely 0 kg chassis weight.
- **Bongo Bus, Nimo Taxi, Nuke Taxi, Townie Bus** (+ Elisa 2 Police drivetrain):
  broken/unused assets — gaps are acceptable.
- **Trailers**: no fuel/tank fields; fuel-pump/trailer capabilities are wiki-only
  additions with no pak counterpart.
- **Wiki-only capabilities** (`Can haul trailer`, `Has fuel pump`) and label spell-out
  (`Limousine` vs pak `Limo`) are display choices, not errors.

## 4. Wiki generator bugs (persistent — flag, don't match)

- Trailer name resolution falls back to raw pak keys (`Trailer_Cotra_20_3_VehicleName`),
  GUID slugs, or merged variants (`trailer_shobed` for `Shobed_7`/`Shobed_10`) when the
  localized-name lookup fails. GUID/raw-key rows are junk; merged rows are a grouping bug.
- `Total Weight = Chassis Weight + 2 × (default parts mass) + 6 kg` — the ×2
  double-counts parts and the +6 is unexplained, but the formula is internally
  consistent, so a `Total ≈ 2×parts + 6` row means chassis really is 0 in the source.
- Empty stat tables are rendered (`^ Stat ^ Value ^` with zero rows) for parts with no
  numeric stats (93/93 wheels, headlights, some utilities) — should be omitted.
- Part pages for transmissions are missing the review2-extractor fields (Inspiration,
  Clutch Type, Comfort Autoshift RPM, Type) — engines got them, transmissions did not.
- Infobox Comfort/Fuel/Seats/Drivetrain/Drag Coefficient were missing after a
  regeneration; old revision `vehicles:dabo?rev=1756720156` is the reference layout.

## 5. Live validation state (2026-08-18, fresh fetch)

Verified with `wiki/validate --validate` (all 768 part pages, 168 vehicle pages,
installable pages, 3 list pages). 510 claims in `wiki/out/validation.json`.

**Correct / fixed**: `parts:anglekit_5` and all Angle Kits; aero display rules
(`+22.5%`, lift kg, `-2.25%`); tire `G`/`N/m`/`N·s/m`; sign typos; cooling-efficiency
omission; LSD labels; 22/23 list tables sorted (Transmission still unsorted:
`13 Speed`/`18 Speed` before `4 Speed Mini Bus`); all 9 review3 vehicles added
(Civo, Elisa 2/Police, Longhorn Semi DC 4x2, Jemusi Flatbed, Atlas 6x2 Garbage,
Goliath-4/6/10); Jemusi → "Jemusi Logger"; vehicle cost correct except `kuda_`.

**Still broken (the fix list)**:
1. Drag coefficient `1.0` on 271 surfaces (138 comparison + 133 infobox) where pak is
   0.22–0.9 (trailers/trophy_air correct in infobox only).
2. 51 transmission-page rows missing (Inspiration ×19, Clutch Type ×15, Comfort
   Autoshift RPM ×14, Type ×3); 12 EV zero-rows cosmetic.
3. Chassis Weight `0 kg` on 12 vehicles (Civo 9100, Elisa 2 1570, Elisa 2 Police 1720,
   Longhorn Semi DC 4x2 7200, Atlas 6x2 Garbage 17900, Jemusi Flatbed 6100,
   Goliath-4/6/10 15000/22000/36000, Kuda Container 6x2 6000, Cotra 20 3L 3000,
   Small Cage Trailer 500). Fix = find the original chassis-weight source and
   regenerate; do NOT invent values.
4. Drivetrain blank on 60 comparison rows; wrong on hana/ranchy/voltex (RWD shown,
   pak AWD).
5. Gutted pages: kart, trophy_air, and the four 30-foot trailers — no
   Specifications/Capabilities/Default Parts sections (infobox + Axle info only).
6. 5 junk `list_of_vehicles` rows (2 GUID, 3 merged trailers); `kuda_` broken slug
   (cost 130,000 vs 220,000; weight 5,050 vs 5,600 kg; pak key `Kuda_Flatbed`).

## 6. Verification recipe

```bash
dotnet run -c Release --project wiki/validate            # gather pak data
dotnet run -c Release --project wiki/validate -- --validate   # fetch + validate
rm -rf wiki/out/pages                                     # force fresh wiki fetch
```

All wiki tooling and output lives under `wiki/`; `out/` is the map extractor's output —
never mix. `wiki/out/validation.json` is the machine claim dump; `wiki/out/review.md`
is hand-written findings (not auto-generated).
