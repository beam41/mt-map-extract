# Wiki Validation Review

Generated 2026-08-18 by `wiki/validate` (pak data gathered from
`MotorTown-Windows.pak` via `wiki/out/out_vehicle_data.json` + the parts
extractor's `wiki/out/*.json`). Every claim is in `wiki/out/validation.json`.
This review is hand-written — it groups, judges and explains the 447 claims.

## Verdict

The wiki is **not valid** against the game data. 447 incorrect claims across
all five surfaces (comparison table, vehicle infobox, Specifications,
Capabilities, Default Parts). The part list itself is clean — all 768 parts
validate (name, cost, mass). The problems are concentrated in:

1. **Drag: every single vehicle row shows `1.0`** — a placeholder, not data.
   The pak has real `AirDragCoeff` values (Hana 0.5, Terra 0.9, Raton 0.22).
   Both the comparison table (138 rows) and the infobox (133) are wrong.
   This is the single biggest defect: the whole drag column/field is broken.
2. **Chassis weight: 12 vehicles show `0 kg`** but have real weights in the
   pak (Civo 9.1 t, Goliath-4/6/10 15/22/36 t, Longhorn Semi DC 4x2 7.2 t,
   Atlas 6x2 Garbage 17.9 t, Jemusi Flatbed 6.1 t, Kuda Container 6x2 6 t,
   Elisa 2/2 Police 1.57/1.72 t, Small Cage Trailer 0.5 t, Cotra 20-3L 3 t).
   Same in table and infobox.
3. **Drivetrain: 60 rows blank + 8 wrong** in the table; 8 wrong in
   Specifications. The pak derives it from wheel `DifferentialComponentName`
   (count driven axles). Hana/Ranchy/Voltex are shown "Rear-wheel drive" but
   are AWD (all wheels driven); Bongo Bus/Elisa 2 Police/Nimo Taxi/Nuke
   Taxi/Townie Bus shown RWD but have no driven axle in the pak.
4. **Capabilities: 22 mismatches** — 10 pages show capabilities the pak
   doesn't have or vice versa; naming differs ("Limousine" vs "Limo").
5. **Default Parts: 35 slot mismatches** on 7 vehicles (Kart, Trophy Air,
   the four 30-foot trailers) — those pages are missing the whole Default
   Parts section or specific slots.
6. **Vehicle list: 5 name/existence problems** — the broken `kuda_` slug and
   5 entries not resolvable to pak vehicles (GUID junk rows).

## By surface

### `list_of_parts` — CLEAN ✅

All 768 parts: name (incl. `#1 → "#1 (Vehicle)"` augmentation), cost, mass.
Zero claims.

### `vehicle_comparison` — 220 claims ❌

| Field | Claims | Detail |
|---|---|---|
| drag | 138 | every row `1.0`; pak `AirDragCoeff` 0.22–0.9 |
| drivetrain | 68 | 60 blank, 8 wrong (Hana/Ranchy/Voltex AWD shown RWD; Bongo Bus/Elisa 2 Police/Nimo Taxi/Nuke Taxi/Townie Bus shown RWD, pak no driven axle) |
| chassisWeight | 13 | 12 wrong 0 kg + 1 (Kuda Flatbed row points at the wrong vehicle via `kuda_` slug) |
| cost | 1 | Kuda Flatbed row → `kuda_` slug resolves to the Kuda SemiTractor, so its cost/weight mismatch |

### Vehicle infobox — 145 claims ❌

- `Drag coefficient` = 1.0 on 133 pages (same broken value as the table).
- `Weight` = 0 kg on 12 pages (see #2 above).

### Specifications — 20 claims ❌

- `Chassis Weight` 0 kg on 12 pages.
- `Drivetrain` wrong on 8 (same AWD/RWD errors as the table).

### Capabilities — 22 claims ❌

- Missing section or wrong content on 10 vehicles: Kart shows no Capabilities
  but is a Race Car; Conter/Flaber/Hobber/Taber leads show "Can haul trailer"
  (pak `bTrailerHauling`, not modeled); Mammoth "Limousine" vs pak "Limo".

### Default Parts — 35 claims ❌

- Kart and Trophy Air pages have **no Default Parts section** (whole page is
  bare: infobox + axle info only).
- The four 30-foot trailers (container/dry-van/log/tanker) are missing Tire
  and Wheel slots (and their pages are otherwise sparse).

### `list_of_vehicles` — 5 claims ❌

- `kuda_` (Kuda Flatbed) — broken slug, resolves to the wrong pak vehicle.
- 4 trailer entries appear under raw pak-key names (`Trailer_*_VehicleName`)
  or GUID slugs, not their localized names.

## Known-good exceptions (do NOT "fix" these)

- **Zero = 0 kg** — genuinely has no `MassInKgOverride` in its blueprint.
- **Bongo Bus, Nimo Taxi, Nuke Taxi, Townie Bus** — broken/unused assets
  (user-confirmed); their missing drivetrain/weight is acceptable.
- **Trailers with no engine** — no fuel type/tank by design.

## Root causes

- The wiki generator reads a weight/drag source that no longer matches the
  pak: weight is `BodyInstance.MassInKgOverride` summed over the vehicle class
  blueprint (NOT the Vehicles table `CurbWeight`, which is 0 everywhere, and
  NOT the parts sum — see review5.md for the full wrong-claim analysis).
- Drag has a hardcoded/placeholder `1.0` (or `?? 1` fallback).
- Drivetrain is derived from a stale or partial axle source.
- The `kuda_` slug and trailer name resolution are broken in the generator
  (raw pak keys / GUIDs leaking into links).

## Fix order

1. Drag: wire the comparison table + infobox to CDO `AirDragCoeff`.
2. Chassis weight: sum `BodyInstance.MassInKgOverride` (12 rows currently 0).
3. Drivetrain: count driven axles from `DifferentialComponentName`.
4. Regenerate Kart / Trophy Air / 30-foot trailer pages (missing sections).
5. Fix `kuda_` slug → `kuda_flatbed_4x2`, and trailer name resolution.
