# Wiki page formats (the generator's spec)

The generator emits DokuWiki markup. The syntax reference (bold, italic, tables,
headings, links, media, plugins) is kept in `docs/dokuwiki-syntax.md` (fetched from
https://www.dokuwiki.org/wiki:syntax) — read it before changing any page template, since
DokuWiki syntax differs from Markdown (e.g. `_(none)_` is NOT italic).

Everything `wiki/` renders, reverse-engineered from the live wiki
(https://wiki.aseanmotorclub.com, snapshot in `wiki/assertions/`) and the pak. The
generator reads the pak directly and must produce exactly these pages. "Identical to the
wiki" is the goal; the exceptions (drift, staleness, deliberate choices) are listed at
the end.

## Page inventory (1032 pages, all .txt, no json)

| Directory / file | Count | Content |
|---|---|---|
| `vehicles/` | 171 | one page per pak vehicle (incl. trailers and the 5 broken assets) |
| `parts/` | 758 | one page per part; `RideHeight_-1..-10` have none (not on the wiki) |
| `cargos/` | 87 | one page per active (non-deprecated) cargo — plural namespace |
| `cargo_space/` | 12 | one aggregate page per `EMTCargoSpaceType` |
| `list_of_parts.txt` | 1 | 768 rows, 44 per-type sections |
| `list_of_vehicles.txt` | 1 | 171 bullets, 12 per-type sections |
| `list_of_cargos.txt` | 1 | 87 rows, 15 per-type sections |
| `vehicle_comparison.txt` | 1 | one row per vehicle |

Slugs: part slugs = lowercased pak key with `.`→`_`, `RideHeight_+N`→`rideheight_pN`,
leading `_` stripped (`_Deprecated_…`→`deprecated_…`); FD parts (bandaid) = `fd_` +
ratio-name; vehicle slugs = the display name (`"Elisa Taxi"`→`elisa_taxi`,
`"Goliath-4"`→`goliath_4`); cargo slugs = lowercased canonical key.

## Vehicle page

```
{{infobox>
name = {en}
Internal key = {pakKey}
Type = {InfoboxType}                      # "Semi trailer, Heavy duty" (sentence case)
Cost = {cost:N0}
Weight = {weightKg:N0} kg
[Engine = {hp} HP]                        # hp from the default engine part name (\d+ HP)
[Drivetrain = Front/Rear/All-wheel drive] # broken assets -> "Rear-wheel drive"
[Cargo space = [[cargo_space:{type}|{type}]]]
Drag coefficient = {drag:0.0##}           # CDO AirDragCoeff ?? 1.0 (always)
[Comfort = {stars}]                       # Math.Round(comfort); only when comfort > 0
[Fuel = {n}L ({fuelType})]               # tank > 0
[Seats = {n}]
[Level requirement = {CL tail}: {n}, ...] # "Taxi: 20, Driver: 50"
}}

====== {en} ======
**{en}** is a {introType} vehicle in [[:motor_town|Motor Town]]     # no trailing period

===== Specifications =====
^ Stat ^ Value ^
[| Engine | [[parts:{slug}|{name}]] ({hp} HP) |]
[| Transmission | [[parts:{slug}|{name}]] |]
[| Drivetrain | {spelled} |]
[| Final Drive Ratio | {Format.Drag(fdrField)} |]   # plain, from the FD part's ratio field
| Chassis Weight | {weightKg:N0} kg |
| Total Weight (stock) | {chassis + Σ default part masses:N0} kg |
[| Drag Coefficient | {drag:0.0##} |]               # only when 0 < drag != 1

[===== Cargo Space =====
^ Stat ^ Value ^
| Type | {spaceType} |
| Length | {L:0.0} m |
| Width | {W:0.0} m |
| Height | {H:0.0} m |
| Volume | {raw L×W×H:0.0} m³ |
[| Dump Volume | {dump:0.0} kL |]
[| Unlimited Height | Yes |]
[| Fixed Cargo | Yes |]]

[===== Capabilities =====               # AFTER Cargo Space, BEFORE Delivery
  * Taxi
  * Bus
  * Limousine          # pak limoable; "Race car" is lowercase-c on the wiki
  * Race car
  * Can haul trailer
  * Has fuel pump]

[===== Delivery =====
^ Stat ^ Value ^
[| Base Payment | ${DeliveryBasePayment} |]        # plain, no thousands separator
[| Payment Multiplier | {DeliveryPaymentMultiplier:0.0}x |]]

===== Default Parts =====
^ Slot ^ Part ^ Total Mass ^
| {baseSlot} | [[parts:{slug}|{name}]] [(×{count})] | {massKg × count:N0} kg |   # "—" when no mass

===== Installable Parts =====

See [[vehicles:{slug}:installable_parts|Installable parts for {en}]].

[===== Axle info =====
^ Axle ^ Break Ratio ^ Driven ^ Dual Wheels ^ Liftable ^
| {Front|Middle|Rear…} | {0% | 0.0%} | {No|**Yes**} | {No|**Yes**} | {No|**Yes**} |]

===== In other languages =====
^ Language ^ Name ^
| Czech | {locres name, English fallback} |
… (22 languages, English display names)
```

- **Axle labels**: 2 axles `Front, Rear`; 3 `Front, Middle, Rear`; 4
  `Front, Front Middle, Rear Middle, Rear`.
- **Default Parts**: pak `Parts` array order; base slot = trailing digits stripped
  (`Tire0`→`Tire`); one row per distinct part per slot, `×N` when count > 1; Total Mass
  = part mass × count.
- **Section order**: Specifications → Cargo Space → Capabilities → Delivery → Default
  Parts → Installable Parts → Axle info → In other languages.

## Part page

```
{{infobox>
name = {en}
Part Type = {typeEnglish}
Cost = {cost:N0}
[Mass = {massKg:N0} kg]
}}

====== {en} ======

**{en}** is {an|a} {typeEnglish lower} part for vehicles in [[:motor_town|Motor Town]].   # "an" for a/e/i/o/u

===== Specifications =====
^ Stat ^ Value ^
| Type | {typeEnglish} |
| Cost | {cost:N0} |
[| Mass | {massKg:N0} kg |]

[===== Stats =====            # omitted when the part has no stats (wheels, bonnets, …)
(blank)
(blank)
==== {statsHeading} ====      # "Engine Physics", "Transmission Physics", per-struct names
^ Stat ^ Value ^
| {label} | {value} |
…                          # aero parts leave ONE EXTRA blank line after the table
]

===== Installable Vehicles =====
See [[parts:{slug}:installable_vehicles|Vehicles that can install {en}]].


===== In other languages =====
^ Language ^ Name ^
… (22 languages)
```

Every heading is preceded by **exactly one blank line** (page titles start the file). No
stray blank runs — the wiki's 2/3-blank quirks around the Stats sub-heading, stat-less
pages, and the In-other-languages section are normalized away.

### Stats headings

`engine`→`Engine Physics`, `transmission`→`Transmission Physics`, `tire`→`Tire` (with a
`==== Tire ====` / `==== Tire Physics ====` sub-split), `lsd`→`LSD`, aero→`Aero`,
`FinalDriveRatio`→`Final Drive Ratio`, else per struct:
Angle Kit, Anti-Roll Bar, Brake Balance, Brake Pad, Brake Power, Suspension Damper,
Suspension Spring, Suspension Ride Height, Coolant Radiator, Turbocharger, Intake,
Wheel Spacer, Winch, Trailer Hitch, Taxi, Cargo Bed, Roof Rack, Inventory, Fuel Tank.

### Row order + formats per group

**Engine Physics** (order fixed, rows conditional on presence; EV engines get zero rows
`Starter Torque 0 N·m` / `Idle Throttle 0%` / `Blip Throttle 0` / `Starter RPM 0 rpm`):
Rotational Inertia `kg·m²` → Starter Torque `N0 N·m` → Max Torque `N0 N·m` → Max RPM
`rpm` → Friction Viscosity → Idle Throttle `%` → Fuel Consumption → Blip Throttle →
Torque Curve `0.## @ 0.##` → Starter RPM `rpm` → Friction Coulomb Coefficient → Fuel
Type → Engine Type → Intake Speed Efficency → Blip Duration `s` → Max Jake Brake Step →
After-Fire Probability `%` → Heating Power `±%` → Cooling Efficiency `±%` → Max Regen
Torque Ratio `%` → Motor Max Power `W` → Motor Max Voltage `V`. Starter/Max Torque and
Motor Power/Voltage use N0 (thousands) when whole, `0.##` otherwise.

**Transmission Physics**: Torque Converter Stall RPM → Torque Converter Stall Ratio
Power → Default Gear → Gears (`Name:F2trim`, pak array order) → Inspiration (DevComment)
→ Shift Time `s` → Torque Converter Torque Rate → Clutch Type (humanized;
`TorqueConvertorV2`→`Torque Converter V2`) → Comfort Autoshift RPM → Type (transmission).

**Tire**: `==== Tire ====` Dual Rear (Yes/No) then `==== Tire Physics ====`: Patch
Length Coefficient, Static Grip `G`, Sliding Grip `G`, Spring Rate X/Y `N/m`, Damping
X/Y `N·s/m`, Max Load `kg`.

**LSD**: LSD Type (humanized tail), Clutch Pack Acceleration, Clutch Pack Brake.

**Aero** — per-type fixed schema, `-` when the field is at default (multipliers 1,
lifts 0); Air Drag = `±%` of `(mult−1)×1.5` when the part has a lift:

| Part type | Rows |
|---|---|
| Front Bumper | Air Drag, Front Damage, Aero Lift, Front Aero Lift, Rear Aero Lift |
| Rear Bumper | Air Drag, Aero Lift, Rear Aero Lift |
| Side Skirt | Air Drag, Aero Lift |
| Rear Spoiler | Air Drag, Trailer Air Drag, Rear Aero Lift |
| Rear Wing | Air Drag, Rear Aero Lift |
| Roof | Air Drag, Trailer Air Drag |
| Fender / Front Spoiler | Air Drag, Aero Lift, Front Aero Lift |
| Bullbar | Front Damage |

Aero lift values: `coef (X kg downforce|lift @ 200 km/h)` for Aero Lift (kind word),
`coef (X kg @ 200 km/h)` for Front/Rear (no kind); force = 7.098e-7 × v² × coef at
200 km/h.

**Other structs**: Angle Increase `deg`; Anti-Roll Bar Rate / Brake Power / Spring Rate /
Bound+Rebound Damping Rate / Front+Rear Brake Bias / Cooling Power / Base Torque /
Torque / Intake Pressure / Heating / Fuel Consumption / Turbine Weight `±%`; Fade
Temperature `°C`; Ride Height Change `cm`; Coolant Capacity `L`; Turbine Aspect Ratio;
Intake Torque Slope / Base RPM Ratio / Intake Speed Efficiency; Width (WheelSpacer)
`mm` (= Space×10); Max Force `kg` / Cable Length `m`; Connection (TrailerHitch tail);
Type (Taxi tail); Cargo Space Location / Size (`X cm × Y cm × Z cm`, |axis|<0.01 → 0) /
Cargo Space Type / Dump Volume `kL`; Slots; Fuel Capacity `L`.

### Type display names

Locres `Parts` value when it differs from the enum tail (`AntiRollBar`→"Anti-roll Bar",
`FrontWindowSticker`→"Front Windshield Sticker"); else the humanized tail
(`SideSkirt`→"Side Skirt" — the locres value "SideSkirt" is unhelpful); `LSD` spells
out to "Limited Slip Differential".

### FD part bandaid

The pak's FD name text can be stale (part `FD_10.65` carries ratio **9.4** after a
retune; `FD_15_HM` → 13.15). When the name text doesn't numerically match the
`FinalDriveRatio` field, the generator names the part from the field and slugs it
`fd_<name>` (`fd_9_4`, `fd_13_15`). Numeric keys keep their page (`101` → "2.73").

## Cargo page

```
{{infobox>
name = {locres name}
Cargo Type = {EDeliveryCargoType tail}      # "None" shown as-is
Volume = {VolumeSize}
Weight = {weight text}
Payment = ${PaymentPer1Km}/km
}}

====== {name} ======

===== Specifications =====
^ Stat ^ Value ^
| Type | {type} |
| Weight | {weight text} |
| Payment per km | ${payment} |
[| Payment multiplier | {F1} |]             # when != 1
[| Base payment | ${BasePayment} |]         # when > 0
[| Min delivery distance | {n}m |]          # when > 0
[| Max delivery distance | {n}m |]          # when > 0
| Stackable | Yes|No |
| Can be pickup | Yes|No |                  # type ∈ {SmallPackage, Food, MilitarySupply}
| Fragile | No|Level X.Y |                  # Fragile > 0

[===== Compatible Cargo Space Types =====   # no leading blank when the section is absent
  * [[cargo_space:{type}|{type}}]]          # pak array order, deduped

[===== Production =====                     # when any producer/consumer exists
==== Produced At ====
^ Location ^ Inputs ^ Time ^
| {point} | {inputs} | {time}s |
[==== Consumed At ====
^ Location ^ Inputs ^ Time ^
| {point} | {inputs} | {time}s |]
```

- **Weight**: `WeightRange` when nonzero (single `X kg` when X=Y, `X–Y kg` when
  variable), else the actor blueprint mass sum, else `0 kg`.
- **Production rows**: per DeliveryPoint, config order kept; rows sorted by point only.
  `inputs` = `N× {canonical cargo key}` joined; `(passive)` when a config has no inputs;
  demand rows render `| {point} | — | — |`. Points with a matching recipe skip their
  passive/demand rows.
- One blank line before `===== Production =====`, `==== Produced At ====` and
  `==== Consumed At ====` (the wiki has none there — normalized).

## Cargo space page

```
====== {Type} Cargo Space ======           # raw enum tail, 6 equals

Everything that provides or accepts the **{Type}** cargo space.

===== Cargos ({n}) =====
  * [[cargos:{slug}|{name}}]]              # sorted by slug (case-insensitive)
===== Vehicles ({n}) =====
  * [[vehicles:{slug}|{name}}]]            # pak row order
===== Parts ({n}) =====
  * [[parts:{slug}|{name}}]]               # pak row order
```

Empty sections render `_(none)_`. Vehicle/part space membership: blueprint
`MTVehicleCargoSpaceComponent` first, else the default CargoBed part's struct.

## List pages

- **list_of_parts**: `There are 768 vehicle parts …`; 44 sections sorted by type name
  (natural); rows per type sorted by name (natural, case-insensitive, digit runs as
  integers, pure numbers first). Mass `—` when absent.
- **list_of_vehicles**: `There are 171 vehicles …`; 12 sections by type name (natural);
  bullets sorted by name **ordinal** (case-sensitive: `SPT1` < `Small Cage Trailer`,
  `Goliath-10` < `Goliath-4`).
- **list_of_cargos**: `There are 87 active cargos …`; grouped by type (natural), rows by
  name (natural); payment plain (`$2600`, no separator).
- **vehicle_comparison**: `^ Name ^ Type ^ Cost ^ Drivetrain ^ Chassis Weight ^ Total
  Weight ^ Drag ^`; type cell = humanized tail (`Racecar`, `Semi Tractor`); rows sorted
  by type then name (ordinal).

## Number formatting rules

| Rule | Example |
|---|---|
| Whole numbers | `Num` plain (`4000`), `N0` with separators per row (`3,700,000 N·m`, `1,000 kg`) |
| Multipliers | `Pct(x−1)`: 1.15 → `+15%`, 0.98 → `-2%`, 1.0 → `±0%` |
| Probabilities | `x×100:0.##%` (`0.8` → `80%`, `2.0` → `200%`) |
| Drag | `0.0##` (0.232 → `0.232`, 1.0 → `1.0`) |
| Gear ratios | `F2` trailing zeros stripped (1.785 → `1.78`, 1.0 → `1`) |
| Break ratios | 0 → `0%`, else `0.0%` (1.6 → `160.0%`) |
| Cargo-space dims | `0.0` m, volume raw product `0.0` m³; Dump Volume `0.0` kL on vehicle pages, `Num` kL on part pages |
| Floats | the pak stores float32; round the float directly (UE5-style) — do NOT round the JSON round-trip text |
| Costs | `N0`; cargo payments and Delivery `Base Payment` plain |

## Pak → wiki field map

| Wiki field | Pak source |
|---|---|
| Chassis Weight | Σ `BodyInstance.MassInKgOverride` over the vehicle class blueprint exports |
| Drag | CDO `AirDragCoeff` (default 1.0 when absent) |
| Seats | count of `MTSeatComponent` exports |
| Axles | `MHWheelComponent`: driven = `DifferentialComponentName`, dual = `WheelFlags`, lift = CDO `LiftAxles` wheel indices, brake ratio = Σ `BrakeRatio` |
| Fuel | CDO `FuelTankCapacityInLiter`; type from engine `EngineProperty.FuelType` (default Gasoline) |
| Engine HP | `\d+ HP` in the engine part's name |
| Final Drive Ratio | the FD part's `FinalDriveRatio` field, `0.0##` format |
| Cargo space size | `MTVehicleCargoSpaceComponent`: `2 × BoxExtent(cm) × RelativeScale3D`; or CargoBed part `CargoSpaceSize` (cm) |
| Capabilities | `bIsTaxiable/bIsLimoable/bIsBusable/bIsRaceCar/bTrailerHauling/bHasFuelPump` |
| Delivery | `DeliveryBasePayment`, `DeliveryPaymentMultiplier` |
| Cargo weight | actor blueprint `BodyInstance` mass sum |
| Cargo names | `Name2` texts joined (locres), else `Name` |
| Production | DeliveryPoint `ProductionConfigs` (`InputCargos`/`OutputCargos` maps, `InputCargoTypes`, tag queries, `ProductionTimeSeconds`), `DemandConfigs`, `PassiveSupplies` |
| In other languages | locres per language, English fallback; 22 languages, English display names, locres order |

## Known wiki deviations (generator output differs on purpose)

- **Full-22 "In other languages"** on pages showing English-only or native-name rows
  (~789 pages) — the wiki mixed generator generations.
- **Hand content dropped**: `image =` fields + custom intros/history on 13 vehicle
  pages (air_city, ambi, lobo, 5t_tanker_trailer, …); air_city's `Internal key = Bus`
  is a plain error.
- **Old-template pages** (12): atlas_6x2_garbage, civo, elisa_2(_police), goliath_4/6/10,
  jemusi_flatbed, longhorn_semi_dc_4x2, trailer_shobed/shotan/shovan lack Axle info /
  In other languages; kart's `Comfort = No comfort` and trophy_air's `Fuel = 50L` are
  hand artifacts with no pak backing.
- **Ghost pages** for removed/renamed vehicles: conter_lead, conter_rear, jemusi,
  trailer_shobed, trailer_shotan, trailer_shovan (not produced); Jemusi is now
  "Jemusi Logger" (`jemusi_logger`); `parts:rideheight` is a dead link.
- **Stale values** where the pak changed after generation: some cargo-space dimensions
  (atlas 6x2 garbage length 4.5→4.4, tanko_40 height 2.1→2.2, terra volume 13.6→13.7,
  30-foot dry van volume 68.5→68.4, lobovan 112.3→112.4, lomax height 3.1→3.2,
  daffy width 0.9→1.0), several drags (elisa, cervos, … → 1.0 default), locres
  translations (30-foot trailers, brutus, campy, kart, dabo, …).
- **FD bandaid**: fd_10_65 → fd_9_4, fd_15_hm → fd_13_15.
- **Wiki drift** kept canonical: brake-balance list order (same-name rows), the 7
  one-blank no-stats pages (buslicense0, defaultattachment, defaultbody,
  escort_license_*), misplaced aggregate cargos (lhbeam_6m, trash_big), cargo_space part
  lists using raw keys instead of names.
- **UE5 float rounding** (directive): gears render 1.32 / 2.11 where the wiki shows
  1.31 / 2.1 (the wiki rounded the JSON round-trip text).
- **Cargo Location names** (directive): the Production/Consumed At Location column shows
  the location's actual name ("Gwangjin Coal Storage") instead of the blueprint key
  ("CoalWarehouse"); the wiki currently shows the keys.
