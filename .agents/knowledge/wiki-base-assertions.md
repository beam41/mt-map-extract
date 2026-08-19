# Wiki Base Assertions

Axioms about Motor Town game data (the pak), stable across wiki states and game-version
changes only when the data model changes. Current wiki state is NOT asserted here — it
lives in `wiki/assertions/` (the live wiki snapshot) and `out/wiki/` (the generator output)
order). These assertions are the reference both sides must agree on.

## 1. Data sources (what each fact is, in the pak)

- **The pak is the source of truth** (`resource/MotorTown-Windows.pak`, CUE4Parse,
  `GAME_UE5_5`), not the wiki, not other sites.
- **Chassis weight** = `BodyInstance.MassInKgOverride` summed over the vehicle's class
  blueprint exports. NOT the Vehicles table `CurbWeight` (0 on all rows), NOT the parts
  sum, NOT collision-geometry mass.
- **Drivetrain** = count of driven axles from `MHWheelComponent.DifferentialComponentName`
  (0 driven = none, 1 front = FWD, 1 rear = RWD, 2 = AWD).
- **Drag** = class default object `AirDragCoeff`.
- **Comfort** = Vehicles table `Comport`. **Seats** = `MTSeatComponent` export count.
- **Fuel capacity** = CDO `FuelTankCapacityInLiter`; fuel type = engine `FuelType`
  (default Gasoline).
- **Part name** = localized `Name`/`Name2`; `#N` names get ` (Vehicle / Vehicle)`
  appended from `VehicleKeys` (skip `None`). **Part cost/mass** = `Cost`, `MassKg` on
  the VehicleParts row.
- **Cargo keys** are FNames matched case-insensitively (`Terra` == `terra`); always fold
  through `CargoKeys.Canonical`.
- A field **absent from an asset = editor default**; never fabricate it. But DO emit the
  part type's own stat struct even when it equals the default (BrakePad 400 °C fade,
  CoolantRadiator 6 L/100 %, Taxi Normal, CargoBed Flatbed) — the default IS the stat.
- Keys are case-insensitive; wiki slugs are lowercase (`anglekit_5`, `rideheight_p1`),
  pak keys PascalCase (`AngleKit_5`, `RideHeight_+1`, `FD_1.33`, `FD_15_HM`).

## 2. Value semantics (game UI display rules)

**Multiplier fields (default 1.0 = stock) → `±%` from 100.** AirDragMultiplier (×1.5
when the part has any lift coefficient — see README), TrailerAirDrag, FrontDamage,
AntiRollBarRate, BrakePad Heating/Cooling/Wear, BrakePower, Suspension spring/dampers,
Turbocharger BaseTorque/Torque/IntakePressure/Heating/FuelConsumption,
IntakeSpeedEfficiency, CoolantRadiator CoolingPower, engine
CoolingEfficiency/HeatingPower/IntakeSpeedEfficency, BrakeBalance Front/Rear per side,
OffroadFriction. Example: `1.15` → `+15%`, `0.7` → `-30%`.

**Probabilities/ratios where 1.0 = 100 % → absolute `%` (×100).** AfterFireProbability
(uncapped: `2.0` → `200%`), IdleThrottle (`0.017` → `1.7%`), MaxRegenTorqueRatio
(`0.3` → `30%`), tire WearRate (`0.1` → `10%`).

**Plain number (no %)**: FinalDriveRatio, StallRatioPower, BaseRPMRatio, CVT gear
ratios, RollingResistanceCoeff, PatchLengthCoefficient (100k–1.2M), Friction
Viscosity/Coulomb, CoolDown/WarmUpSpeed, SmokeRate, BlipThrottle, FuelConsumption,
TurbineAspectRatio, TorqueRate, CVT_ClutchCurvePow, ClutchPackAccel/Brake (0–100
scale), MaxJakeBrakeStep. `RollingResistanceCoeffV1` is unused — skip.

**Units**: tire grip = μ relabeled `X G` (never %); springs `N/m`; damping `N·s/m`;
winch cable length cm → `m`; wheel spacer cm → `mm`; brake balance `±%` per side;
enums humanized from tail (`EMTLSDType::ClutchPackLSD` → `Clutch Pack LSD`,
`EMTTransmissionClutchType::MultiPlateClutch` → `Multi Plate Clutch`).
`+−x%` sign typos must never render.

**Table sort rule** (applies to every `list_of_parts` per-type table): sort rows by the
displayed part name.

- A name that parses entirely as a number (`50%`, `+5`, `1.8`, `-10cm`) compares
  numerically.
- Any other name compares alphabetically, but embedded digit runs compare as integers:
  `F50` < `F60` < `F110`, `KM1-65` < `KM2-45`, `Bike 6 Speed` > `6 Speed Truck Mk1`.
- Digits sort before letters at the same position: `13 Speed`/`18 Speed` come after the
  `4–6 Speed` block but before `Bike 6 Speed`; `2 Way Clutch Pack LSD (100)` sorts
  before `Lockable`; `1 Way` < `1.5 Way` < `2 Way` < `Lockable` < `Locked Differential`.

This is the ordering the wiki's own tables are expected to use. Every row is subject to
it, including rows added after a table was first generated: a late-added row must be
interleaved at its sorted position, never appended at the end of its block. Appending
(e.g. new vehicle variants like Elisa 2 or Longhorn Semi DC 4x2 dumped after the existing
block) is the classic violation — the row is unsorted even though its neighbors are.

## 3. Data facts (pak-side, not wiki-side)

- **Zero** genuinely has 0 kg chassis weight (no `MassInKgOverride` on its blueprint).
- **Bongo Bus, Nimo Taxi, Nuke Taxi, Townie Bus** are broken/unused assets (no usable
  drivetrain or stats) — their gaps are acceptable.
- **Trailers** have no fuel/tank fields; some vehicles carry `trailerHauling` /
  `hasFuelPump` flags, others do not — absence of the flag is data, not an error.
