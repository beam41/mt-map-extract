---
name: wiki-validate-cargo
description: |
  Generate the ASEAN Motor Club wiki's cargo pages (https://wiki.aseanmotorclub.com)
  from the Motor Town pak data in this repo. Triggers: "cargo wiki", "check cargo",
  "validate cargo", "cargo review", "cargo pages", "Compatible Cargo Space",
  "production/dropoff cargo", "cargo improvement".
  Run `dotnet run -c Release --project wiki/generate` to write every DokuWiki page as
  .txt under wiki/out/ (no json).
---

# Cargo Wiki Generator (ASEAN Motor Club)

This repo reads Motor Town game data from `resource/MotorTown-Windows.pak` and the
`wiki/generate/` project renders every wiki page including the cargo domain: the
per-cargo pages (`cargos/`), the aggregate `cargo_space/` pages, and `list_of_cargos.txt`.
Use this skill whenever asked to check, verify, fix, review, or regenerate anything
cargo-related on the wiki (cargo pages, the `list_of_cargos` page, cargo space types,
production/dropoff recipes, cargo names or weights). For parts/vehicles/comparison use
the sibling `wiki-validate` skill.

The cargo domain has its own ground-truth sources distinct from parts/vehicles: the

## Cargo ground truth

| Wiki content | Pak source |
|---|---|
| Cargo name | `Name2` texts joined (locres), else the `Name` text ("AppleBox" → "Apples") |
| Cargo Type | `EDeliveryCargoType` tail (displayed as-is, e.g. `None`, `SmallPackage`) |
| Weight | `WeightRange` (single `X kg` when X=Y, `X–Y kg` when variable), else the actor blueprint `BodyInstance.MassInKgOverride` sum, else `0 kg` |
| Payment | `PaymentPer1Km` (plain number), `PaymentPer1KmMultiplierByMaxWeight`, `BasePayment` |
| Delivery distances | `MinDeliveryDistance` / `MaxDeliveryDistance` (> 0 only) |
| Stackable / Fragile | `bAllowStacking` / `Fragile` (`Level X.Y` when > 0) |
| Can be pickup | cargo type ∈ {SmallPackage, Food, MilitarySupply} |
| Cargo space types | `CargoSpaceTypes` array (pak order) |
| Produced At / Consumed At | DeliveryPoint `ProductionConfigs` (inputs/outputs with counts + types + tag queries), `DemandConfigs`, `PassiveSupplies`; rows sorted by point, config order kept; `(passive)` for configs with no inputs; `— | —` for demands |
| Vehicle cargo space | `MTVehicleCargoSpaceComponent` (type, `2 × BoxExtent × RelativeScale3D` size, `bFixCargo`, `bUnlimitedHeight`, `DumpVolume`) or the default CargoBed part's `CargoSpaceSize` |
| Part cargo space | the part's `CargoBed` struct (`CargoSpaceType`, `CargoSpaceSize`, `DumpVolume`) |

## Commands

```bash
# Generate every page from the pak (needs resource/MotorTown-Windows.pak + aes + Mappings.usmap)
dotnet run -c Release --project wiki/generate

# Compare generated pages against the snapshot
diff -r wiki/out wiki/assertions
```

## Known intentional deviations from the live wiki

- Cargo pages use the plural namespace `cargos:` (the wiki's `cargo:` pages are ghosts).
- The aggregate `cargo_space:` cargos list is sorted by slug; the wiki had a few
  misplaced entries (lhbeam_6m, trash_big) — regenerating sorts them.
- Cargo space pages list part display names; the wiki used raw pak keys there.
- The vehicle rename (Jemusi → "Jemusi Logger") shows the current name/slug.

## Gotchas

- Cargo keys are FNames matched case-insensitively — fold through `CargoKeys.Canonical`.
- Cargo slugs are the lowercased canonical key (`WoodPlank_14ft_5t` → `woodplank_14ft_5t`).
- `wiki/generate/` links `AssetSource.cs`/`Options.cs`/`Localization.cs`/`CargoKeys.cs`/
  `TableExtractor.cs` from the repo root via `<Compile Include="../../…">` — shared code
  changes affect it.
