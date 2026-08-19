# Vehicle parts in the pak

How Motor Town's vehicle-parts data is laid out in `MotorTown-Windows.pak`, how the
part→vehicle restriction rules work, and what statistics each part type carries.
Verified against the current pak (`MotorTown/Content/DataAsset/...`, 60,539 files).

## Where the data lives

| Data | Pak path | Rows |
| --- | --- | --- |
| **Parts (master)** | `MotorTown/Content/DataAsset/VehicleParts/VehicleParts` | 768 |
| Parts (per-type views) | `.../VehicleParts/{AeroParts, Engines, Wheels, BrakeBalance, BrakePad, BrakePower, Suspensions, Transmissions, UtilityParts, CargoBed, RoofRacks, Headlights, LSD, ...}` | subsets of the master |
| **Vehicles (master)** | `MotorTown/Content/DataAsset/Vehicles/Vehicles` | 171 |
| Vehicles (per-type views) | `.../Vehicles/{Vehicles_Truck, Vehicles_Bus, Vehicles_Special, ...}` | subsets of the master |
| Part names | `Game.locres` namespace `VehicleParts` (104 GUID keys + the `VehiclePartsBrand` string table keys, e.g. `MossanBX1` → "Mossan BX1") | |
| Part-type names | `Game.locres` namespace `Parts` (32 entries) | |
| Vehicle names | `Game.locres` namespaces `VehicleName`, `Vehicle`, `Vehicles_1`, `Vehicles_Truck`, ... | |
| Garage UI labels | `Game.locres` namespace `Garage` (Downforce, Air Drag, Grip, Max Load, Max Temperature, ...) | |

The per-type files are **curated subsets of the same rows**: same row struct, same row
names, same values — only the rows of that type. `Engines` (36) ⊂ `VehicleParts` (Engine,
36), `Wheels` (93) ⊂ Wheel (93), `AeroParts` (387) ⊂ the aero types, etc. The extractor
reads only the two masters. Note some row names are numeric (`"301"` is a transmission,
`"110"` a final drive ratio); they are part keys like any other.

Parts also soft-reference gameplay assets that carry the *actual* statistics:

| Part field | Asset type | Pak path pattern | What it holds |
| --- | --- | --- | --- |
| `EngineAsset` | `MHEngineDataAsset` | `/Game/Cars/Parts/Engine/*` | torque curve, MaxTorque, MaxRPM, inertia, fuel consumption, cooling |
| `TransmissionAsset` | `MTTransmissionDataAsset` | `/Game/Cars/Parts/Transmission/*` | gear ratios, shift time, torque-converter params |
| `Tire.TirePhysicsDataAsset` | `MTTirePhysicsDataAsset` | `/Game/Cars/Parts/Tire/*` | static/sliding mu, spring/damping, max weight |
| `LSDAsset` | `MTLSDDataAsset` | `/Game/Cars/Parts/LSD/*` | LSD type enum |
| `EngineProperty.TorqueCurve` | `CurveFloat` | `/Game/Cars/Parts/Engine/TorqueCurve/*` | normalized torque-vs-RPM curve (Time 0–1 = RPM/MaxRPM, Value = torque multiplier) |

`/Game/...` in an asset path maps to `MotorTown/Content/...` in the pak.

## The parts table schema

One row per part; 52 columns. `PartType` selects which columns are meaningful; the rest
carry editor defaults. The columns group into:

- **Identity / economy** — `Name` (FText), `Name2` (FText), `Desciption` (sic, FText),
  `Cost`, `MassKg`, `bIsHidden`, `GameplayTags`, `LevelRequirementToBuy` (career levels,
  e.g. `[{Key: CL_Driver, Value: 5}]`), `Slots` (`EMTVehiclePartSlot::`).
- **Restrictions** — see below.
- **Statistics** — one struct per part type (see table at the end).

## How part→vehicle restrictions work

Restrictions live on **both** the part and the vehicle row. The extractor emits them
verbatim; a consumer combines them.

### Part side (`VehicleParts` rows)

| Field | Meaning |
| --- | --- |
| `VehicleTypes` | `EMTVehicleType::` list. Empty = no type filter; non-empty = the vehicle's `VehicleType` must be in it. |
| `TruckClasses` | `EMTTruckClass::` list. Empty = no class filter. |
| `bTruckClassIncludeNone` | With a non-empty `TruckClasses`, whether vehicles with `TruckClass: None` (non-trucks) are also allowed. |
| `VehicleKeys` | Explicit vehicle row names (e.g. `Voltex`, `Terra`). Empty = no key filter. A part with `VehicleKeys: [Voltex]` and empty `VehicleTypes` fits only the Voltex. |
| `OverrideAllowedVehicleKeys` | Vehicle keys allowed **regardless of every other restriction** — the escape hatch. E.g. `I4_150HP` (an engine for Small/Pickup non-EVs) also carries `["FormulaSCM"]` so the Racecar Formula SCM can use it. |
| `VehicleRowGameplayTagQuery` | UE `FGameplayTagQuery` evaluated against the vehicle row's `GameplayTags`; emitted as its readable `AutoDescription`, e.g. `NONE( Vehicle.EV )` = "does not fit electric vehicles". |
| `GameplayTags` | The part's own tags, e.g. `VehiclePart.Utility.Small`, `VehiclePart.VehicleKeySpecific.LonghornCab`, `VehiclePart.Slot.Right/Left`, `VehiclePart.Hidden` — matched by the vehicle's slot queries. |
| `Slots` | Which `EMTVehiclePartSlot::` slots the part can be fitted into (`Utility0`, `Winch0`, `Crane0`...). |

Worked examples from the data:

- Wheel `Voltex`: `VehicleTypes: [Small, Pickup]`, `VehicleKeys: []` → fits every Small and
  Pickup vehicle.
- Wheel `Terra`: `VehicleTypes: [HeavyMachinery]`, `VehicleKeys: [Terra]` → fits only the
  Terra (the heavy-machinery EV).
- `KartEngine_10HP`: `VehicleTypes: [Kart]` + `tagQuery: NONE( Vehicle.EV )` → any Kart
  that is not an EV.
- `SmallRadiator_100` / `BasicTire_65` / `I4_150HP`: `OverrideAllowedVehicleKeys: [FormulaSCM]`
  → usable on the Formula SCM no matter what its type/tags say.
- `RearWing_A`: `VehicleKeys: ["None"]` → a literal key named "None" (data as-is; likely
  the catch-all the game matches when a vehicle row has no key).

### Vehicle side (`Vehicles` rows)

| Field | Meaning |
| --- | --- |
| `VehicleType`, `TruckClass`, `GameplayTags` | What part-side filters match against. Tags are namespaced: `Vehicle.EV`, `Vehicle.Key.Goliath`, `Vehicle.Bus`, `Vehicle.Delivery.{Cheap,Modern,Taxi,Heavy}`, `Vehicle.MineTruck`, `Vehicle.Bike.{SportBike,Scooter,Standard}`, ... |
| `NotSupportedPartTypes` | Part types this vehicle cannot take at all (e.g. Formula SCM: `[LSD, WheelSpacer]`). |
| `NotOptionalPartTypes` | Part types that must be installed (shown as required in the garage). |
| `OptionalPartTypes` | Part types that may be left empty (e.g. many cars: `[SideSkirt]` or `[Roof]`). |
| `NotOptionalPartSlots` | Specific slots that must be filled (e.g. Brutus Wrecker: `[Utility0, Utility1, Crane0]`). |
| `SlotSupportedPartsQueries` | Per-slot `FGameplayTagQuery` a part must satisfy to fit that slot, e.g. `Utility0: ALL( VehiclePart.Utility.Small )` — only small utility parts; or `ALL( VehiclePart.VehicleKeySpecific )` for the wrecker's crane slots. |
| `VehicleTypeFlags` | Bitmask the game stores (not in the usmap): `4` = most vehicles, `16` = taxi/special, `2` = wrecker, `8` = ? — passed through raw as `typeFlags`. |
| `LevelRequirementToDrive` | Career levels required to buy/drive, `[{Key: CL_Truck, Value: 5}, ...]`. |
| `Parts` | Factory-default part per slot (`EMTVehiclePartSlot::` → part key) — what the vehicle ships with. |
| `PartValues` | Attachment slot configs (light positions etc.), raw. |

Combined fit rule (as implied by the data; the engine's exact boolean algebra is not in
the pak): a part fits vehicle V in slot S when V's key is in `OverrideAllowedVehicleKeys`,
**or** all of

- `VehicleTypes` empty or `V.VehicleType ∈ VehicleTypes`,
- `TruckClasses` empty or (`V.TruckClass ∈ TruckClasses`, with `None` also allowed when
  `bTruckClassIncludeNone`),
- `VehicleKeys` empty or `V.key ∈ VehicleKeys`,
- `VehicleRowGameplayTagQuery` empty or it matches `V.GameplayTags`,
- `V.NotSupportedPartTypes` does not contain `PartType`,
- `V.SlotSupportedPartsQueries[S]` empty or it matches the part's `GameplayTags`.

`bTruckClassIncludeNone` deserves a caveat: with `TruckClasses` set it either *adds* class
`None` vehicles or marks that the `None` class is covered; the flag is emitted as-is
(`truckClassIncludeNone` in `restrict`) so the consumer can pick the reading that matches
observed gameplay.

## Statistics per part type

`stats` holds only what the row actually uses: struct columns are emitted when any field
differs from the value most rows carry (the editor default), aero scalars when non-default,
and the engine/transmission/tire/LSD asset stats are resolved through the soft refs. Two
exceptions always emit: the part type's own stat struct (BrakePad, CoolantRadiator, Taxi,
CargoBed — so a Basic brake pad still carries its 400 °C fade temperature and the bike taxi
license its Normal type), and the resolved asset stats. The purely-default parts
(DefaultBody, DefaultAttachment, ...) still come out with no stats block.

| Part type | Rows | Stat fields |
| --- | ---: | --- |
| Engine | 36 | `engine`: `MaxTorque`, `MaxRPM`, `Inertia`, `StarterTorque`, `StarterRPM`, `FuelConsumption`, `CoolingEfficiency`, `HeatingPower`, `FrictionCoulombCoeff`, `FrictionViscosityCoeff`, `IdleThrottle` (fraction, ×100 = %), `BlipThrottle`, `BlipDurationSeconds`, `IntakeSpeedEfficency`, `AfterFireProbability`, `TorqueCurve` (`[{Time, Value}]`, normalized), `FuelType`, `EngineType` (enums), EV/truck extras (`MaxRegenTorqueRatio`, `MotorMaxPower`, `MotorMaxVoltage`, `MaxJakeBrakeStep`). Plus inline `Intake` (`Slope`, `BaseRPMRatio`, `IntakeSpeedEfficencyMultiplier`) and `Turbocharger` where fitted. Power ≈ `MaxTorque × curve(RPM/MaxRPM)`. |
| Transmission | 23 | `transmission`: `Gears` (`[{Name, GearRatio, Inertia}]`, incl. R/N), `DefaultGearIndex`, `ShiftTimeSeconds`, `AutoShiftComportRPM`, `ClutchType` (enum), `Type` (enum: EatonFuller13/18/CVT; present on some), `TorqueConvertorStallRPM`, `TorqueConvertorStallRatioPower`, `TorqueConvertorTorqueRate`, `DevComment` (dev note, e.g. "Citroen 2CV 6"); CVT-only: `CVT_InputRPMRange` (vector), `CVT_GearRatios` (vector), `CVT_ClutchCurvePow` |
| Tire | 12 | `Tire` (asset ref + `bIsDualRearWheel`) + `tire`: `StaticMu`, `SlidingMu`, `OffroadFriction`, `SpringX/Y`, `DampingX/Y`, `MaxWeightKg`, `PatchLengthCoefficient`, `WearRate`, `SmokeRate`, `CoolDownSpeed`, `WarmUpSpeed`, `RollingResistanceCoeff` (`RollingResistanceCoeffV1` exists in the pak but is unused — not emitted) |
| LSD | 6 | `lsd`: `LSDType` (`Locked`, `ClutchPackLSD`, ...), `ClutchPackAccel`, `ClutchPackBrake` (clutch-pack LSDs only) |
| Turbocharger | 5 | `Turbocharger`: `bIsValid`, `BaseTorqueMultiplier`, `TorqueMultiplier`, `TurbineAspectRatio`, `IntakePressureMultiplier`, `HeatingMultiplier`, `FuelConsumptionMultiplier`, `TurbineWeight` |
| Intake | 2 | `Intake`: `Slope`, `BaseRPMRatio`, `IntakeSpeedEfficencyMultiplier` |
| CoolantRadiator | 5 | `CoolantRadiator`: `CoolingPower`, `CoolantWaterInLiter` |
| FinalDriveRatio | 28 | scalar `FinalDriveRatio` (default −1) |
| BrakePad | 8 | `BrakePad`: `HeatingMultiplier`, `CoolingMultiplier`, `FadeTemperature`, `WearMultiplier` |
| BrakeBalance | 40 | `BrakeBalance`: `FrontMultiplier`, `RearMultiplier` |
| BrakePower | 3 | `BrakePower`: `BrakePowerMultiplier` |
| Suspension_Damper | 8 | `SuspensionDamper`: `BoundDampingRateMultiplier`, `ReboundDampingRateMultiplier` |
| Suspension_Spring | 8 | `SuspensionSpring`: `SpringRateMultiplier` |
| Suspension_RideHeight | 20 | `SuspensionRideHeight`: `RideHeightChange` |
| AntiRollBar | 4 | `AntiRollBar`: `AntiRollBarRateMultiplier` |
| WheelSpacer | 14 | `WheelSpacer`: `Space` |
| AngleKit | 2 | `AngleKit`: `AngleIncreaseInDegree` |
| Aero body parts (FrontBumper 109, RearBumper 95, SideSkirt 44, Bonnet 33, Fender 22, RearWing 24, RearSpoiler 38, Roof 10, FrontSpoiler 2, Headlight 6, RoofRack 5, CargoBed 8, Utility 18, ...) | | `Aero` (mesh/socket config), `AirDragMultiplier`, `TrailerAirDragMultiplier`, `AeroLift` / `FrontAeroLift` / `RearAeroLift` (downforce), `FrontDamageMultiplier`, plus per-kind extras: `CargoBed` (`CargoSpaceLocation/Size/Type`, `bFixCargo`, `bUnlimitedHeight`, `DumpVolume`), `RoofRack` (cargo space), `Headlight` (`LightOnAnim`), `ItemInventory` (`NumSlots`), `FuelTank` (`FuelLiter`) |
| CargoBed | 8 | `CargoBed`: cargo space volume/type (`Flatbed`, `Box`, `Tanker`), `DumpVolume` |
| RoofRack | 5 | `RoofRack`: cargo space |
| TrailerHitch | 3 | `TrailerHitch`: `Mesh`, `ConnectionType` (`Hitch`/`Ring`) |
| Winch | 10 | `Winch`: `MaxForceKg`, `MaxLength` (+ meshes/sounds), `Slots` (`Winch0` or `Crane0..2`) |
| Utility | 18 | `ItemInventory` (`NumSlots`), `FuelTank` (`FuelLiter`), `Slots` (`Utility0..3`) |
| Wheel | 93 | `Wheel`: `LeftWheelMesh`/`RightWheelMesh`/`DRW*`/`Rear*`/`QuadWheelMesh` |
| Wheel/body cosmetics | | `BodyMaterialNames`, `ColorSlots`, `DecalableMaterialSlotNames` (paintable surfaces) |
| Licenses (TaxiLicense 4, BusLicense 1, EscortLicense 4) | | no stats; restriction data only (`VehicleTypes`/`TruckClasses`/`Taxi` roof-sign class) |
| Body (1), Attachment (1), CargoBedAttachment (1), Trunk (1) | | defaults only ("DefaultBody" etc.) |

`Cost` is the garage purchase price; `LevelRequirementToBuy` gates it by career level
(`CL_Driver`, `CL_Truck`, `CL_Racer`, `CL_Wrecker`, ...).
