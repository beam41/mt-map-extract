# Vehicles in the pak

How Motor Town's vehicle data is laid out in `MotorTown-Windows.pak`, what each wiki
field maps to, and how the blueprint-derived statistics are computed. Verified against
the current pak (`MotorTown/Content/DataAsset/...`, 60,539 files).

## Where the data lives

| Data | Pak path | Rows |
| --- | --- | --- |
| **Vehicles (master)** | `MotorTown/Content/DataAsset/Vehicles/Vehicles` | 171 |
| Vehicles (per-type views) | `.../Vehicles/{Vehicles_Truck, Vehicles_Bus, Vehicles_Special, ...}` | subsets of the master |
| Vehicle blueprints | `/Game/Cars/Models/**` → `MotorTown/Content/Cars/Models/**` | one per `VehicleClass` |
| Vehicle names | `Game.locres` namespaces `VehicleName`, `Vehicle`, `Vehicles_1`, `Vehicles_Truck`, ... | |
| Engine parts | `VehicleParts` table rows + `MHEngineDataAsset` assets | 36 engines |

The per-type files are curated subsets of the same rows (same row struct/names/values);
read only the master. `VehicleClass` is an object ref to the blueprint whose exports
carry the physical stats (weight, seats, axles, drag, fuel tank, cargo space).

## The Vehicles table schema (fields the wiki uses)

| Field | Wiki use |
| --- | --- |
| `VehicleName` / `VehicleName2` | display name; `VehicleName2` texts joined per language, else `VehicleName`; locres lookup per language, English fallback |
| `VehicleType` | `EMTVehicleType::` → the wiki's type word (`Small`, `SemiTrailer` → "Semi trailer") |
| `TruckClass` | `EMTTruckClass::` appended in sentence case ("Semi trailer, Heavy duty"); `None` omitted |
| `Cost` | garage price, `N0` |
| `Comport` | comfort stars (Math.Round); 0 → no Comfort row |
| `bIsTaxiable/bIsLimoable/bIsBusable/bIsRaceCar` | Capabilities: Taxi / Limousine / Bus / Race car |
| `bTrailerHauling` / `bHasFuelPump` | Capabilities: Can haul trailer / Has fuel pump |
| `LevelRequirementToDrive` | `[{Key: CL_..., Value: n}]` → "Driver: 2", multi → "Taxi: 20, Driver: 50" |
| `Parts` | ordered default parts: `EMTVehiclePartSlot::` key → part key |
| `DeliveryBasePayment` / `DeliveryPaymentMultiplier` | the Delivery section: `$500` (plain) / `3.0x` |
| `GameplayTags` | tag-query installable-part filtering |

## Blueprint-derived stats

Resolved from the `VehicleClass` blueprint package (exports are CUE4Parse JSON).

| Stat | Source | Notes |
| --- | --- | --- |
| Chassis Weight | Σ `BodyInstance.MassInKgOverride` across exports | NOT the table's `CurbWeight` (0 everywhere); Zero / Bongo Bus / Nimo Taxi have none → 0 |
| Seats | count of `MTSeatComponent` exports | 0 → no Seats row |
| Axles | `MHWheelComponent` exports, axle index = wheel name digits ÷ 2 | driven = `DifferentialComponentName` present; dual = `WheelFlags` contains `DualRearWheel`; lift = CDO `LiftAxles` wheel indices; brake ratio = Σ `BrakeRatio` per axle |
| Drag | CDO `AirDragCoeff` | the wiki renders `?? 1.0` when absent; the Specifications row only when `0 < drag != 1` |
| Fuel tank | CDO `FuelTankCapacityInLiter` | `{n}L ({fuelType})`, tank > 0 only |
| Fuel type | default engine's `MHEngineDataAsset.EngineProperty.FuelType` | default "Gasoline" when absent |
| Cargo space | `MTVehicleCargoSpaceComponent` | see below |

The CDO is found through the `BlueprintGeneratedClass` export's
`ClassDefaultObject.ObjectPath` (last `.N` = export index).

### Axles → drivetrain

Driven-axle count: 0 → no Drivetrain row, 1 → front axle = "Front-wheel drive", rear =
"Rear-wheel drive", ≥2 → "All-wheel drive". The 5 broken/unused assets always display
"Rear-wheel drive" (wiki convention, no pak signal): `Bongo_Bus`, `Nimo_Taxi`,
`Nuke_Taxi`, `Townie_Bus`, `Elisa2_Police`.

### Cargo space

From `MTVehicleCargoSpaceComponent` (first component wins):

- size = `2 × BoxExtent(cm) × RelativeScale3D` per axis (extent defaults 50, scale 1)
- volume = raw L×W×H product
- `bFixCargo` → "Fixed Cargo Yes", `bUnlimitedHeight` → "Unlimited Height Yes",
  `DumpVolume` → "Dump Volume `0.0` kL"

Vehicles without a component use their default `CargoBed` part's `CargoSpaceSize` (cm)
and `DumpVolume`/`bFixCargo`/`bUnlimitedHeight` from the part's `CargoBed` struct.

## Default Parts rendering

The wiki groups the pak `Parts` array by base slot (trailing digits stripped:
`Tire0..3` → `Tire`, `Utility0/1` → `Utility`), one row per distinct part in
first-occurrence order, `×N` when the count > 1, Total Mass = `part.MassKg × N` (`—`
when the part has no mass). Slot order = the pak array order.

## Broken/unused assets

`Bongo_Bus`, `Nimo_Taxi`, `Nuke_Taxi`, `Townie_Bus`, `Elisa2_Police` have no usable
drivetrain in the pak (all axles non-driven) yet carry full engine/transmission
defaults. The wiki displays them as normal cars with "Rear-wheel drive".

## Names and localization

Display name resolution per language: `VehicleName2` texts joined via locres lookup,
else the `VehicleName` FText (locres, then LocalizedString/SourceString). The "In other
languages" section lists all 22 non-English locres languages with English display names
and the English fallback. Vehicle slugs come from the English display name
(`"Elisa Taxi"` → `elisa_taxi`, `"Goliath-4"` → `goliath_4`, `"Air City"` → `air_city`).
