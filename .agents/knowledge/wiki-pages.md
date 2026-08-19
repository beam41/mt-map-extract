# Wiki page formats (the generator's spec)

The generator emits DokuWiki markup. The syntax reference (bold, italic, tables,
headings, links, media, plugins) is kept in `.agents/knowledge/dokuwiki-syntax.md` (fetched from
https://www.dokuwiki.org/wiki:syntax) — read it before changing any page template, since
DokuWiki syntax differs from Markdown (e.g. `_(none)_` is NOT italic).

Everything `wiki/` renders, reverse-engineered from the live wiki
(https://wiki.aseanmotorclub.com, snapshot in `wiki/assertions/`) and the pak. The
generator reads the pak directly and must produce exactly these pages. "Identical to the
wiki" is the goal; the exceptions (drift, staleness, deliberate choices) are listed at
the end.

## Hand-curated content survives regeneration (detail pages only)

Some live pages carry hand-written content the pak can't produce — a custom intro
paragraph, trivia, an `image = ...` infobox field (e.g.
`https://wiki.aseanmotorclub.com/vehicles:air_city`: `image =air_city.webp|Air City` plus
two paragraphs about ridership rank and value-for-money, interleaved directly between the
generator's own intro sentence and its Specifications table). A full
regenerate-and-overwrite would silently delete all of it. The fix: **only the four
"detail" page types** (vehicles, parts, cargos, delivery_points — one page about one
specific entity) split into **three** pieces, two of them bot-owned subpages transcluded
into a human-owned live shell page via the **`include` plugin**
(`https://www.dokuwiki.org/plugin:include`, install via the Extension Manager;
`{{page>...}}` syntax). Aggregate/list pages (`cargo_space`, `cargo_type`, every
`list_of_*`, `vehicle_comparison`, `installable_parts`/`installable_vehicles`) have no
curatable content and are never split — always fully generated, single page.

- **`{ns}:{slug}:auto_infobox`** — just the `{{infobox> ... }}` block
  (`VehiclePageInfobox`/`PartPageInfobox`/`CargoPageInfobox`/`DeliveryPointPageInfobox` in
  `Render*.cs`). Wiped and rewritten in full every run — **except** the `image = ...`
  line: every run fetches the live wiki's current page for every detail-page entity
  (`LiveWiki.FetchImageLine`, tries `{ns}:{slug}:auto_infobox` first — the post-migration
  source of truth a curator edits directly — then falls back to the legacy flat
  `{ns}:{slug}` page for anything not yet migrated) and re-inserts whatever `image = ...`
  line it finds right after `name = ...` (`LiveWiki.MergeImage`) before writing. A network
  failure or absent field is not an error, just nothing to preserve. Confirmed against the
  real live wiki: 13 vehicles currently carry an `image` field, all 13 found and merged
  byte-for-byte (including irregular spacing like `image =air_city.webp|Air City`, no
  space after `=`).
- **The heading + one-sentence intro** — `====== {Name} ======` and `**{Name}** is a ...`
  (`VehiclePageHeading`/`PartPageHeading`/`CargoPageHeading`/`DeliveryPointPageHeading`).
  Generated **once**, as literal text straight into the bootstrap shell suggestion below —
  never its own `:auto_*` subpage, so a curator is free to hand-edit it (or leave it
  as-is) without the edit ever being clobbered by a later run.
- **`{ns}:{slug}:auto_details`** — Specifications (or Production, for delivery points)
  onward. Wiped and rewritten in full every run, exactly like every other generated page —
  a curator must never edit it directly, edits would be lost.
- **`{ns}:{slug}`** — the live shell page a human owns. The generator **never writes to
  this path**; it only exists on the live wiki. Its content:
  ```
  {{page>{ns}:{slug}:auto_infobox}}

  ====== {Name} ======
  **{Name}** is a ... in Motor Town.

  (hand-written prose goes here — anything at all)

  {{page>{ns}:{slug}:auto_details}}
  ```
- **`out/wiki-bootstrap/{ns}/{slug}.txt`** — the shell template above, **opt-in** (pass
  `--bootstrap`; off by default, since it's only useful once per page at migration time —
  regenerating all 1196 of them every run is noise once most pages are migrated). Paste it
  as a page's initial live content once; from then on the generator only touches the
  `:auto_infobox` and `:auto_details` subpages under it, never the shell itself.

**Migrating an existing hand-curated live page** (e.g. air_city): open it, cut everything
that matches the generator's own template shape (infobox, heading, intro sentence,
Specifications through In other languages) since that's now provided by the two
includes plus the once-generated heading/intro; paste
`out/wiki-bootstrap/vehicles/air_city.txt`'s content in; keep the hand paragraphs sitting
between the heading and the final include. Verified end-to-end in a local
DokuWiki+include preview against the real fetched air_city data: the infobox (with its
real `image` field) renders from `:auto_infobox`, heading/intro/hand paragraphs render as
shell-page literal text, Specifications-onward renders from `:auto_details`, and each
transcluded block gets its own `[Edit]` link pointing straight at the subpage that owns
it.

**Exhaustive prose-content audit** (all 171 live vehicle pages, not just the 13 with an
`image` field): fetched every `vehicles:{slug}?do=export_raw`, located the generator's own
`====== {Name} ======` / intro-sentence pair, and diffed everything between it and the
first `===== Specifications =====` heading. Only **3** vehicles carry hand-written prose
there — **air_city** (2 paragraphs), **5t_tanker_trailer** (3 paragraphs), and **lobo** (a
full `===== History =====` sub-section with an embedded image, ~4 KB of lore text). The
other 10 `image`-field vehicles (ambi, atlas_4x2_semi, campy, eastwood, enfo_gt, flanker3,
flanker3s, small_cage_trailer, spt1, tanko_40, vamos3) have **no** extra prose — the
`image` field is their only hand content, already preserved by the always-on
`LiveWiki.FetchImageLine`/`MergeImage` path. All 3 prose vehicles migrated to
`out/wiki-migrated/vehicles/{slug}.txt` (shell content: infobox include, heading, intro,
the extracted hand paragraphs verbatim, details include) and confirmed rendering correctly
in the local preview (infobox, History section with its `{{ :logo_lobo.jpg?300|...}}`
media reference, Specifications onward all present and in the right order).
`out/wiki-migrated/` is a one-off manual-migration output, not written by a normal run.

**Deployment rule — this is the entire point of the feature:** when syncing generated
output onto the live wiki, only ever push `{ns}/{slug}/auto_infobox.txt` and
`auto_details.txt` (plus the untouched aggregate/list pages) — **never** a delete-capable
sync of the top-level `{ns}/{slug}.txt` shell path once it exists on the live wiki. An
`rsync --delete` (or equivalent) that includes shell paths in its scope will delete them,
because the generator's own output tree no longer contains a flat `{ns}/{slug}.txt` for
detail pages to compare against — confirmed by reproducing exactly that deletion against
the local preview while building this feature.

## Page inventory (3352 generated pages, all .txt, no json; + 1196 with `--bootstrap`)

| Directory / file | Count | Content |
|---|---|---|
| `vehicles/{slug}/auto_infobox.txt`, `auto_details.txt` | 171×2 | split vehicle page (see above); incl. trailers and the 5 broken assets |
| `vehicles/{slug}/installable_parts.txt` | 171 | fit-rule subset of list_of_parts, one per vehicle |
| `parts/{slug}/auto_infobox.txt`, `auto_details.txt` | 758×2 | split part page; `RideHeight_-1..-10` have none (not on the wiki) |
| `parts/{slug}/installable_vehicles.txt` | 758 | fit-rule inverse, one per part |
| `cargos/{key}/auto_infobox.txt`, `auto_details.txt` | 87×2 | split cargo page (active, non-deprecated only) — plural namespace |
| `cargo_space/` | 12 | one aggregate page per `EMTCargoSpaceType`, not split |
| `cargo_type/` | 14 | one aggregate page per `EDeliveryCargoType`, not split (new, "None" excluded) |
| `delivery_points/{slug}/auto_infobox.txt`, `auto_details.txt` | 180×2 | split delivery point page, one per real-world placement (new, no wiki precedent) |
| `wiki-bootstrap/{ns}/{slug}.txt` (`--bootstrap` only) | 1196 | shell-page suggestion, one per detail-page entity — see above, never bulk-synced |
| `list_of_parts.txt` | 1 | 768 rows, 44 per-type sections |
| `list_of_vehicles.txt` | 1 | 171 bullets, 12 per-type sections |
| `list_of_cargos.txt` | 1 | 87 rows, 15 per-type sections + a trailing 14-bullet cargo type list |
| `list_of_delivery_points.txt` | 1 | 180 bullets, 7 zone sections |
| `vehicle_comparison.txt` | 1 | one row per vehicle |

Slugs: part slugs = lowercased pak key with `.`→`_`, `RideHeight_+N`→`rideheight_pN`,
leading `_` stripped (`_Deprecated_…`→`deprecated_…`); FD parts (bandaid) = `fd_` +
ratio-name; vehicle slugs = the display name (`"Elisa Taxi"`→`elisa_taxi`,
`"Goliath-4"`→`goliath_4`); cargo slugs = lowercased canonical key; delivery point slugs
= `Format.Slug(name)` (same rule as vehicles), with `_2`/`_3`… appended when two real
placements share a display name (13 pairs, e.g. `burgerjoint_jeju`/`burgerjoint_jeju_2`).

## Vehicle page

Splits into `vehicles:{slug}:auto_infobox` (⟨A⟩ below), a once-generated heading (⟨B⟩),
and `vehicles:{slug}:auto_details` (⟨C⟩) — see "Hand-curated content survives regeneration"
above. `image = ...` is not pak data; live-fetched and merged into ⟨A⟩ after `name`.

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
[Cargo space = [[cargo_space:{type}|{type}]] (installable)]  # only when the vehicle ships with NO cargo space but can install a CargoBed part (scooty: Box, gunthoo: Box); multiple types join with ", "
Drag coefficient = {drag:0.0##}           # CDO AirDragCoeff ?? 1.0 (always)
[Comfort = {stars}]                       # Math.Round(comfort); only when comfort > 0
[Fuel = {n}L ({fuelType})]               # tank > 0
[Seats = {n}]
[Level requirement = {CL tail}: {n}, ...] # "Taxi: 20, Driver: 50"
}}
⟨A: end of auto_infobox⟩

====== {en} ======
**{en}** is a {introType} vehicle in [[:motor_town|Motor Town]]     # no trailing period
⟨B: end of the once-generated heading⟩
⟨C: start of auto_details⟩
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

## Installable parts page (`vehicles:{slug}:installable_parts`)

A subset of list_of_parts filtered by the part→vehicle fit rule, one page per vehicle:

```
====== Installable Parts for {en} ======

All vehicle parts that can be installed on the **{en}** ({n} part types, {m} parts in total).

Return to [[vehicles:{slug}|{en}]].

===== {TypeEnglish} ({count}) =====

^ Part ^ Cost ^ Mass ^
| [[parts:{slug}|{en}]] | {cost:N0} | {mass} |
```

- Sections grouped by `TypeEnglish`, ordered naturally; rows per type by `En` naturally;
  Mass like list_of_parts (`0.1 kg` / `—`). No trailing blank line after the last section.
- **Fit rule** (same as the wiki's installable_vehicles pages, from vehicle-parts.md):
  FDR parts always fit (user directive — the bandaid renamed some); the override key wins;
  otherwise ALL of VehicleTypes / TruckClasses (None when `bTruckClassIncludeNone`) /
  VehicleKeys / `VehicleRowGameplayTagQuery` (CUE4Parse-style token evaluation, UE
  hierarchy matching) / vehicle `NotSupportedPartTypes`.
- **`VehicleKeys: ["None"]`** = the part is UNUSED — fits no vehicle (the generic
  RearWing_A/B/C/D). Real keys alongside it filter as usual (`["Muhan", "None"]` fits the
  Muhan). The wiki's generator instead treated "None" as a catch-all and listed the generic
  rear wings + Muhan bumper on all 171 vehicles — reproduced as a deviation, not a bug.

## Installable vehicles page (`parts:{slug}:installable_vehicles`)

The inverse — one page per part, listing every vehicle that fits it, grouped by vehicle
type like list_of_vehicles:

```
====== Installable Vehicles for {en} ({TypeEnglish}) ======

All vehicles that can install the **{en} ({TypeEnglish})** ({n} vehicles in total).

Return to [[parts:{slug}|{en}]].

===== {HumanizeType} =====
  * [[vehicles:{slug}|{en}]]
```

- Groups by `Format.HumanizeType(v.Type)`, ordered naturally; within a group by `En`
  **case-insensitive ordinal** (`Small Cage Trailer` < `SPT1`; `Goliath-10` < `Goliath-4`)
  — the wiki's installable_vehicles pages sort differently from its list_of_vehicles
  (case-sensitive ordinal). No trailing blank line after the last group.
- Same fit rule as installable parts (memoized `InstallableVehicles(part)` — a vehicle can
  install the part iff the part is in the vehicle's installable list). Parts with no
  fitting vehicle (the unused `None`-key parts) render "(0 vehicles in total)" with no
  groups; the wiki never has that case.

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

Splits into `parts:{slug}:auto_infobox` / a once-generated heading / `parts:{slug}:auto_details` (⟨A⟩/⟨B⟩/⟨C⟩ below), same rule as the vehicle page.

```
{{infobox>
name = {en}
Part Type = {typeEnglish}
Cost = {cost:N0}
[Mass = {massKg:N0} kg]
}}
⟨A: end of auto_infobox⟩

====== {en} ======

**{en}** is {an|a} {typeEnglish lower} part for vehicles in [[:motor_town|Motor Town]].   # "an" for a/e/i/o/u
⟨B/C: end of heading, start of auto_details⟩
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

Splits into `cargos:{slug}:auto_infobox` / a once-generated heading / `cargos:{slug}:auto_details` (⟨A⟩/⟨B⟩/⟨C⟩ below), same rule as the vehicle page.

```
{{infobox>
name = {locres name}
Cargo Type = {linked [[cargo_type:{slug}|{tail}]], "None" plain}
Volume = {VolumeSize}
Weight = {weight text}
Payment = ${PaymentPer1Km}/km
}}
⟨A: end of auto_infobox⟩

====== {name} ======

**{name}** is {a|an} {CargoTypeEnglish lower} cargo in [[:motor_town|Motor Town]].    # "None"-type cargos drop the type clause: "is a cargo in ..."
⟨B/C: end of heading, start of auto_details⟩
===== Specifications =====
^ Stat ^ Value ^
| Type | {linked [[cargo_type:{slug}|{tail}]], "None" plain} |
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
| [[delivery_points:{slug}|{point}]] | {inputs} | {time}s |
[==== Consumed At ====
^ Location ^ Inputs ^ Time ^
| [[delivery_points:{slug}|{point}]] | {inputs} | {time}s |]
```

- **Weight**: `WeightRange` when nonzero (single `X kg` when X=Y, `X–Y kg` when
  variable), else the actor blueprint mass sum, else `0 kg`.
- **Production rows**: one row per real-world **placement** (not per blueprint) whose
  effective config matches, `Points` is `Data`'s per-placement list built from
  `WorldExtractor.DeliveryPointDetails()` — a blueprint reused at many locations (a
  generic drop point, a construction site) gets one row per placement, each linking its
  own `delivery_points:{slug}` page; config order kept per placement, rows sorted by
  placement name. `inputs` = `N× [[cargos:{slug}|{name}]]` joined (linked; type refs
  linked to `[[cargo_type:{slug}|{name}]]` — `RenderCargos.InputText`/`CargoLink`/
  `CargoTypeText`); `(passive)` when a config has no
  inputs; demand rows render `| {point} | — | — |`. Time via `Format.Duration` (`90s` ->
  `"1m 30s"`, `120s` -> `"2m"`). Placements with a matching recipe skip their
  passive/demand rows. The 223 anonymous residential placements (`Resident_C`, no
  per-instance name) collapse to one unlinked `Resident` row instead of 223 duplicates.
- One blank line before `===== Production =====`, `==== Produced At ====` and
  `==== Consumed At ====` (the wiki has none there — normalized).

## Delivery point page (`delivery_points:{slug}`, new — no wiki precedent)

One page per real-world placement of a delivery-point blueprint (180 pages; the 223
`Resident_C` placements are excluded, see above). Splits into
`delivery_points:{slug}:auto_infobox` / a once-generated heading /
`delivery_points:{slug}:auto_details` (⟨A⟩/⟨B⟩/⟨C⟩ below), same rule as the vehicle page.

```
{{infobox>
name = {en}
[Import = {cargo1}, {cargo2}, …]            # distinct cargos/types this point consumes:
                                             # recipe inputs ∪ demand cargos, key refs
                                             # linked [[cargos:{slug}|{name}]], type refs
                                             # linked [[cargo_type:{slug}|{name}]]
[Export = {cargo1}, {cargo2}, …]            # recipe outputs ∪ passive supplies, same rule
[Required Space Type = {type1}, {type2}, …] # cargo space type(s) needed to carry the
                                             # produced output away — the in-game panel's
                                             # own requirement, derived from the resolved
                                             # output cargos' Compatible Cargo Space Types
                                             # (recipes carry no space-type field of their
                                             # own); omitted when every output is a
                                             # type-ref or a Production Speed boost
Location = {ZoneNameEn}
External Link = [[https://www.aseanmotorclub.com/map?menu=deliveries/{guid}&delivery={guid}|View on map]]
}}
⟨A: end of auto_infobox⟩

====== {en} ======

**{en}** is a delivery point in [[:motor_town|Motor Town]].
⟨B/C: end of heading, start of auto_details⟩
===== Production =====

[==== Recipes ====
^ Inputs ^ Output ^ Time ^
| {inputs or (passive)} | {linked output cargo, "Production Speed: +X.X%", or —} | {time}s |]
```

- `inputs` = `N× [[cargos:{slug}|{name}]]` joined (linked; type refs linked to
  `[[cargo_type:{slug}|{name}]]` — one shared `RenderCargos.InputText`/`CargoLink`/
  `CargoTypeText`, also used by the cargo page's own Inputs column); `(passive)` for
  `PassiveSupplies` rows (no time).
- **Output** = the linked produced cargo(s), or — when the recipe has **no output cargo**
  and a `ProductionSpeedMultiplier != 1` — `Production Speed: +100.0%` (`Format.SpeedPct`,
  always one decimal, matches the in-game production panel exactly: these recipes consume
  an input to boost the point's *other* recipes instead of producing anything, e.g. a farm
  feeding Fuel/Pallets/Quicklime for +100%/+50%/+30% speed with no output row). Falls back
  to `—` only when the multiplier is exactly 1 (not observed in practice — every real
  no-output config carries a nonzero multiplier).
- Time: `Format.Duration` — plain seconds under a minute, else `Nm` / `Nm Ss` (`90s` ->
  `"1m 30s"`).

```
[==== Demand ====
^ Cargo ^ Payment Multiplier ^
| {linked cargo or type} | {mult:F1}x |]                             # DemandConfigs

[==== Storage ====
^ Cargo ^ Max Storage ^
| {linked cargo or type} | {MaxStorage or —} |]                      # DemandConfigs, same rows

===== In other languages =====
^ Language ^ Name ^
… (22 languages)
```

- **Demand**/**Storage** are two separate tables over the same `DemandConfigs` rows —
  `PaymentMultiplier` and `MaxStorage` (the cap the point holds of that cargo before it
  stops accepting deliveries — the in-game panel's `n/MaxStorage` storage column) are
  logically distinct facts, not combined into one table.
- **Location** = the enclosing **Zone**-flagged area (point-in-polygon ray-cast against
  `WorldExtractor.AreaVolumes()`'s `TopViewLines`, C# port of amc-web's
  `area.ts` `getLocationAtPoint`/`getLocationNearPoint`), falling back to the nearest
  zone edge when the point sits inside none — same algorithm, verified zero mismatches
  against a from-scratch port for all 180 points. Only the 7 `Zone`-flag areas are
  considered (not `SmallArea`/`LargeArea`/`RaceTrack`, which nest inside zones).
- **External Link** = `[[url|View on map]]`; guid is the placement's own
  `DeliveryPointGuid` (falls back to the blueprint's when the world actor doesn't
  serialize its own — same rule `WorldExtractor.DeliveryPoints()` uses for the map site).
- A placement whose name collides with another real placement (13 pairs, e.g. two
  "BurgerJoint Jeju" locations) gets `_2`/`_3`… appended to the slug (guid-ordinal
  order); the display name (`name =`/page title) is unchanged.

## Cargo type page (`cargo_type:{slug}`, new — no wiki precedent)

One aggregate page per real `EDeliveryCargoType` value (14 pages; "None" is not a group).
Every bare type reference elsewhere (a delivery point's type-ref recipe input/output,
demand, passive supply, or Import/Export cell) links here instead of showing plain text.

```
====== {Type} Cargo Type ======

Everything of the **{Type}** cargo type.

===== Cargos ({n}) =====
  * [[cargos:{slug}|{name}]]                  # active cargos whose own CargoType == this
===== Delivery Points ({n}) =====
  * [[delivery_points:{slug}|{name}]]         # points that reference this type generically
```

- **Cargos** = every active cargo whose own `Type` field equals this type (sorted by slug,
  case-insensitive natural — same as `cargo_space:` pages).
- **Delivery Points** = every placement whose recipes/demand/passive-supplies reference
  this type via `InputCargoTypes`/`OutputCargoTypes`/`CargoType` (not a specific
  `CargoKey`); the unlinked collapsed `Resident` entry is excluded (sorted by name,
  ordinal — same as `list_of_delivery_points`).

## List of delivery points page (`list_of_delivery_points`, new)

```
====== List of Delivery Points ======

There are {n} delivery points in [[:motor_town|Motor Town]].

===== {ZoneNameEn} =====
  * [[delivery_points:{slug}|{en}]]
```

Grouped by zone (natural sort on the zone name, 7 sections), bullets sorted by name
**ordinal** (case-sensitive, matching list_of_vehicles) — same structure as
list_of_vehicles' per-type sections. `Resident` is excluded (no page).

## Cargo space page

```
====== {Type} Cargo Space ======           # raw enum tail, 6 equals

Everything that uses or accepts the **{Type}** cargo space.      # "uses" = Cargos, "accepts" = Vehicles/Parts

[===== Cargos ({n}) =====
  * [[cargos:{slug}|{name}}]]]              # sorted by slug (case-insensitive)
[===== Vehicles ({n}) =====
  * [[vehicles:{slug}|{name} (installable)]]]  # pak row order; "(installable)" when the vehicle has no default space but fits a space-giving CargoBed part
[===== Parts ({n}) =====
  * [[parts:{slug}|{name}}]]]               # pak row order
```

Each of the three sections is **omitted entirely** (no heading, no "(0)") when that
group is empty — not shown as a bare count or `_(none)_`. Vehicle/part space membership:
blueprint `MTVehicleCargoSpaceComponent` first, else a real space in the default CargoBed
part; the installable scan matches the part-side restrictions (the same fit rule) and
takes **every** space-giving struct in the rendered stats: `CargoBed` (typed) and
`RoofRack` (no `CargoSpaceType` in the pak — the enum default **Flatbed**, per user
directive). Zeroed `CargoSpaceSize` structs are not real spaces.

## List pages

- **list_of_parts**: `There are 768 vehicle parts …`; 44 sections sorted by type name
  (natural); rows per type sorted by name (natural, case-insensitive, digit runs as
  integers, pure numbers first). Mass `—` when absent.
- **list_of_vehicles**: `There are 171 vehicles …`; 12 sections by type name (natural);
  bullets sorted by name **ordinal** (case-sensitive: `SPT1` < `Small Cage Trailer`,
  `Goliath-10` < `Goliath-4`).
- **list_of_cargos**: `There are 87 active cargos …`; grouped by type (natural), rows by
  name (natural); payment plain (`$2600`, no separator); Type cells linked to
  `cargo_type:`; a trailing `===== Cargo Types (14) =====` bullet list links every type.
- **vehicle_comparison**: `^ Name ^ Type ^ Cost ^ Drivetrain ^ Chassis Weight ^ Total
  Weight ^ Drag ^`; type cell = humanized tail (`Racecar`, `Semi Tractor`); rows sorted
  by type then name (ordinal).
- **list_of_delivery_points** (new): `There are 180 delivery points …`; 7 sections by
  zone name (natural), bullets by name ordinal — full template above.

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
- **Hand content — resolved**: 13 vehicles carry an `image =` infobox field (all 13
  preserved every run by `LiveWiki`); of those, only air_city and 5t_tanker_trailer also
  had hand-written prose paragraphs, and lobo (no `image` field) had a full hand-written
  `===== History =====` sub-section — all 3 migrated, see "Exhaustive prose-content audit"
  above. air_city's `Internal key = Bus` is a plain error, not hand content.
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
- **Installable cargo space** (directive): vehicles that ship with no cargo space but can
  fit a space-giving part render "(installable)" in the infobox and in the cargo-space
  page's vehicle list — scooty/gunthoo → Box (beds), muhan/savannah → Flatbed (roof
  racks, which carry no type in the pak and default to Flatbed). The wiki shows them with
  no space at all — its data source only sees default parts; roof racks also now appear
  on the flatbed page's part list.
- **Installable parts pages** (new, generated 2026-08-19): per-vehicle pages matching the
  wiki except: (a) the FD bandaid slugs (fd_9_4/fd_13_15 — approved), (b) the generic
  RearWing_A/B/C/D + Muhan_FrontBumper_02 excluded (wiki's "None"-key catch-all is a
  generator bug — those parts are unused), (c) formula_scm has no LSD/WheelSpacer
  (pak `NotSupportedPartTypes`; the wiki lists them), (d) identical "Inventory" names sort
  in pak order (wiki's tie order is unstable).
- **Family-grouped #N part order** (directive): "#N (Family)" parts sort with the family
  first — "#1 (Dabo)", "#2 (Dabo)", "#3 (Dabo)" adjacent — instead of all "#1"s together.
  The wiki's list order shows all "#1" parts first.
- **Tag-query-keyed #N families** (directive): "#N" parts with no VehicleKeys (keyed by
  tag query, e.g. atlas_frontbumper_01 -> ANY( Vehicle.Key.Atlas )) derive the family from
  the fit rule's fitting vehicles — "#1 (Atlas)", "#1 (Goliath)". The wiki leaves them as
  bare "#1". The brand collapse tokenizes on non-alphanumeric boundaries so "Goliath-4 /
  Goliath-6 / Goliath-10" -> "Goliath".
- **Collapsed #N owner names** (directive): a "#N" part whose owners share a brand shows
  only the brand — "#1 (Brutus)" instead of "#1 (Brutus Wrecker / Brutus Tanker / Brutus
  Ambulance / Brutus Fire Engine)"; unrelated owners keep the " / " join. The wiki shows
  the full join.
- **Part type in titles** (directive): the part page heading renders `====== {en} ({TypeEnglish}) ======` and the installable_vehicles title/intro carry the type — `Installable Vehicles for 2.73 (Final Drive Ratio)`; the wiki titles have the bare name.
- **Installable vehicles pages** (new, generated 2026-08-19): per-part pages matching the
  wiki except: (a) the Jemusi vehicle links point at our `vehicles:jemusi_logger` page
  (the wiki still links the old `vehicles:jemusi` slug), (b) the 4 generic rear wings show
  "(0 vehicles in total)" (wiki's "None" bug lists 171), (c) formula_scm missing from the
  LSD/wheelspacer lists (pak `NotSupportedPartTypes`), (d) the bandaid FDR pages exist
  under fd_9_4/fd_13_15 instead of fd_10_65/fd_15_hm.
- **Cargo Location names** (directive): the Production/Consumed At Location column shows
  the location's actual name ("Gwangjin Coal Storage") instead of the blueprint key
  ("CoalWarehouse"); the wiki currently shows the keys.
- **Delivery point pages** (new feature, 2026-08-19, no wiki precedent): `delivery_points:`
  pages (180) and `list_of_delivery_points` are new namespaces the live wiki doesn't have.
  Reading recipes per **real-world placement** (via `WorldExtractor.DeliveryPointDetails()`)
  instead of per blueprint file also fixed a latent correctness bug in the
  Produced/Consumed At tables: a blueprint reused at many locations (`ComonDrop_C` ×13,
  `ConstructionSite_C` ×10, …) previously collapsed every row to the *first* placement's
  name regardless of which specific site the config actually belonged to (concrete's
  Consumed At showed "Jeju Construction Site" twice instead of all 10 real sites; an
  H-Beam demand at "Terra Factory H-Beam Drop" showed as "Tank Factory Coil Drop"). Every
  row is now the correct specific placement, linked. `common/WorldExtractor.cs` gained
  `DeliveryPointDetails()` (raw per-placement config structures, Key vs Type kept
  separate) alongside the existing `DeliveryPoints()` (flattened JSON for the map site) —
  both share one `Placements()` walker; `DeliveryPoints()`'s JSON output is
  byte-for-byte unchanged (verified).
