# Wiki review — additional findings (validator-blind)

Companion to the main review (the one with Tasks 1–7). These findings are **not** in the
claim list: automated checks only cover name/cost/mass/drag/drivetrain/weight/slots/
capabilities, so the following were found by direct page inspection. All values come from
the game pak (2026-08-18). Apply these alongside the main review.

## Extra 1 — Empty Stats sections (146 part pages)

**What's wrong:** 146 part pages render `===== Stats =====` with **zero rows**. These
parts have no numeric stats in the pak (cosmetic-only parts), so nothing is flagged, but
the empty section is noise.

**Fix:** omit the Stats section entirely when the part has no stat rows. Affected types
and counts: Wheel 93, Bonnet 33, Headlight 6, Rear Window Louvers 4, Front Window Sun
Visor 3, Utility 3, Front Window Sticker 2, Cargo Bed Attachment 1, Trunk 1.
Examples: `parts:atlas`, `parts:corawheel_02`, `parts:cora_headlight_01`,
`parts:bongo_sparetire`, `parts:dory_bonnet_01`.

**Verify:** no `parts:<slug>` page has a `===== Stats =====` heading with an empty table.

## Extra 2 — Total Weight column formula

**What's wrong:** every comparison-table `Total Weight` value equals
`Chassis Weight + 2 × (default parts mass) + 6 kg` — parts are double-counted and the +6
is unexplained (e.g. Hana: 1,500 + 2×413 + 6 = 2,332).

**Fix:** total should be `Chassis Weight + Σ default parts mass` (recompute all rows, not
just the 12 from main-review Task 3).

**Verify:** spot-check that `Total − Chassis` equals the sum of the listed default parts
for a few rows.

## Extra 3 — Comparison-table Type column

**What's wrong:** the `Type` column is never checked against the pak.

**Fix:** verify every row's Type matches the pak vehicle type (Small, Pickup, Truck,
Bus, Semi Trailer, ...). Known pak types: Kart → `Kart`, Hana → `Pickup`, the 30-foot
trailers → `Semi trailer, Heavy duty`, Trophy Air → `Small`.

**Verify:** Type column matches the pak for all 168 rows.

## Extra 4 — Vehicle infobox: Comfort / Fuel / Seats / Drivetrain

**What's wrong:** the infobox is only checked for `Weight` and `Drag coefficient`.
Comfort, Fuel, Seats, Drivetrain presence is unverified on all 168 vehicle pages.

**Fix:** every vehicle infobox must include `Comfort` (stars), `Fuel` (`{n}L
({Type})`), `Seats` (`{n}`), `Drivetrain`. Reference layout:
`vehicles:dabo?rev=1756720156`.

**Verify:** every `vehicles:<slug>` page has all four fields in the infobox.

## Extra 5 — Reverse direction: pak rows missing from wiki lists

**What's wrong:** the claim list only flags wiki rows that do not exist in the pak — it
never flags **pak vehicles/parts that are missing from the wiki lists entirely**.
Earlier this is how 9 vehicles (Goliath-4/6/10, Elisa 2/Police, Civo, Longhorn Semi DC
4x2, Jemusi Flatbed, Atlas 6x2 Garbage) went missing.

**Fix:** confirm every pak vehicle (171) appears in `list_of_vehicles` and every pak part
(768) appears in `list_of_parts`; add any missing row with the pak English name.

**Verify:** counts match (168+ listed vehicles, 768 listed parts) and no pak key is
absent.

## Extra 6 — Level requirement

**What's wrong:** the `Level requirement` field is never checked anywhere.

**Fix:** spot-check it renders the pak career-level gate (`CL_Driver`, `CL_Truck`,
`CL_Racer`, `CL_Wrecker`, ...) where present.
