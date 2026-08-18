# Wiki review — additional findings (validator-blind)

Companion to the main review (Tasks 1–7). **Update (2026-08-18): all six findings below
are now automated validator checks** — the next `validation.json` run covers them, so
they no longer need manual inspection. This file is kept as the record of what was added
and how each maps to claim rows.

| Finding | Now a validator check | Claim fields in validation.json |
|---|---|---|
| Empty Stats sections (146 part pages) | `ValidatePartPage`: `===== Stats =====` heading with zero rows on parts that have no pak stats | `parts:<slug> Stats` → `field: "empty stats section"` |
| Total Weight column formula | `ValidateComparison`: `Total Weight` must equal `weightKg + Σ default part masses` (wiki's `+2×parts+6` is wrong) | `vehicle_comparison` → `field: "totalWeight"` |
| Comparison-table Type column | `ValidateComparison`: cell must match humanized pak `EMTVehicleType` ("Heavy Machinery", "Semi Tractor", "Racecar") | `vehicle_comparison` → `field: "type"` |
| Vehicle infobox Comfort / Fuel / Seats / Drivetrain / Level requirement | `ValidateVehiclePage` infobox: presence + value (stars for Comfort, `{n}L ({Type})` for Fuel, `{n}` for Seats, drivetrain spelling or abbreviation, `Driver: 2` level) | `vehicles:<slug> infobox` → fields `Comfort`, `Fuel`, `Seats`, `Drivetrain`, `Level requirement` |
| Infobox Type (type + truck class, sentence case) | `ValidateVehiclePage` infobox: `Type` = "Semi trailer, Heavy duty" etc. | `vehicles:<slug> infobox` → `field: "Type"` |
| Reverse direction: pak rows missing from wiki lists | `ValidatePartList` / `ValidateVehicleList`: every non-hidden pak part (768) and every pak vehicle must appear in the wiki list | `list_of_parts` / `list_of_vehicles` → `field: "part"` / `"vehicle"` with `wiki: "(not listed)"` |

Current live run (2026-08-18, fresh fetch): **1362 claims** total. New claim counts from
these checks: totalWeight 163, empty stats section 146, Comfort 133, Level requirement
123, Seats 119, Fuel 95, Drivetrain (infobox) 71, type 1, vehicle-not-listed 9.

Remaining manual caveats (still not checkable):

- Multi-level vehicles (e.g. `Taxi_01` with both `CL_Taxi` and `CL_Driver`) — value is
  compared only when exactly one level exists; presence is always checked.
- `Drivetrain` on the 5 broken assets (Bongo Bus, Nimo Taxi, Nuke Taxi, Townie Bus,
  Elisa 2 Police) shows RWD with no pak drivetrain — known-good, leave as-is.
