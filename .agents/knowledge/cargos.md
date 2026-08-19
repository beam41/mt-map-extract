# Cargos in the pak

How Motor Town's cargo domain is laid out in `MotorTown-Windows.pak`: the Cargos tables,
cargo actor weights, cargo-space types, and the DeliveryPoint production/dropoff
recipes. Verified against the current pak (`MotorTown/Content/DataAsset/...`,
`MotorTown/Content/Objects/Mission/Delivery/...`).

## Where the data lives

| Data | Pak path | Rows |
| --- | --- | --- |
| **Cargos (master)** | `MotorTown/Content/DataAsset/Cargos` | 91 (87 active) |
| Cargos (schedule I) | `MotorTown/Content/DataAsset/Cargos_ScheduleI` | folded case-insensitively into the master |
| Cargo actor blueprints | `/Game/Objects/Mission/Delivery/*` | per `ActorClass` |
| **Delivery points** | `MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint/*.uasset` | 83 |
| Cargo names | `Game.locres` namespaces `CargoType`, `Cargo`, `MapIcon`, `Item`, `Common` | |
| Cargo-space types | `EMTCargoSpaceType` enum (12: Box, Tanker, Flatbed, Dump, Log, Container, Grain, Garbage, DryBulk, LiveFishTanker, CarCarrier, ConcreteMixer) | |

## The Cargos table schema (fields the wiki uses)

| Field | Wiki use |
| --- | --- |
| `Name` / `Name2` | display name: `Name2` texts joined (locres), else `Name` — "AppleBox" → "Apples", "BottlePallete" → "Water Bottle Pallet" |
| `CargoType` | `EDeliveryCargoType::` tail shown as-is (`None`, `SmallPackage`, `Food`, ...) |
| `VolumeSize` | infobox `Volume` |
| `WeightRange` | `X–Y` vector: single `X kg` when X=Y, `X–Y kg` when variable; zero → actor weight |
| `PaymentPer1Km` | `$N/km` (plain, no thousands separator) |
| `PaymentPer1KmMultiplierByMaxWeight` | `F1` row, omitted when 1 |
| `BasePayment` | `Base payment` row, omitted when 0 |
| `MinDeliveryDistance` / `MaxDeliveryDistance` | `{n}m` rows, > 0 only |
| `bAllowStacking` | Stackable Yes/No |
| `Fragile` | `Level X.Y` when > 0, else No |
| `bDepcreated` (sic) | deprecated cargos get no page and are excluded from `list_of_cargos` (87 active) |
| `CargoSpaceTypes` | the Compatible Cargo Space Types bullets, pak array order |
| `GameplayTags` | recipe tag-query matching |
| `ActorClass` | the actor blueprint whose `BodyInstance.MassInKgOverride` sum is the weight (bulk cargos carry a single 1M_Cube with the weight baked in); absent (FormulaSCM) → `0 kg` |

## Cargo space types

`CargoSpaceTypes` is the pak array order (e.g. applebox → `Flatbed, Box` — the wiki
renders that order, not sorted). The aggregate `cargo_space:<type>` pages list:

- **Cargos** — sorted by slug (case-insensitive);
- **Vehicles** — pak Vehicles-table order (component space, else default CargoBed part);
- **Parts** — pak order (CargoBed-type parts).

Empty buckets render `_(none)_`.

## DeliveryPoint recipes

Each DeliveryPoint blueprint's CDO carries the production configs:

| Field | Meaning |
| --- | --- |
| `ProductionConfigs[]` | recipes: `InputCargos` / `OutputCargos` maps (canonical cargo key → count), `InputCargoTypes` / `OutputCargoTypes` (enum refs), `InputCargoGameplayTagQuery` / `OutputCargoRowGameplayTagQuery` (tag queries), `ProductionTimeSeconds`, `bHidden`, `MissionPointName` |
| `DemandConfigs[]` | consumers: `CargoKey`, `CargoType`, `CargoGameplayTagQuery`, `PaymentMultiplier`, `MaxStorage` |
| `PassiveSupplies[]` | passive producers: `CargoKey`, `CargoType`, `MaxDeliveries` |

Rendering per cargo page:

- **Produced At** = configs whose outputs match the cargo (key, or type + tag query) —
  `| {location} | {inputs} | {time}s |`; rows sorted by location, config order kept.
- **Location** = the location's actual name, resolved from the world actors via
  `WorldExtractor.DeliveryPoints()` (the same reference as the map site's
  `out_delivery_point.json`): the CDO type of the DeliveryPoint blueprint (`CoalWarehouse`
  → `CoalWarehouse_C`) maps to the placed actor's localized name ("Gwangjin Coal
  Storage"). Blueprints with no placed actor (`Farm_Base_`, `Store_SantaCabin`) fall back
  to the blueprint key.
- **Consumed At** = configs whose inputs match, plus demand rows (`| {point} | — | — |`).
- `inputs` = `N× {canonical key}` joined (types render as their tail); `(passive)` when
  a config has no input refs; points with a matching recipe skip their passive/demand
  rows.
- A point with no matching recipe but a matching `PassiveSupplies` entry renders
  `| {point} | (passive) | — |`.

`ProductionConfigs`/`DemandConfigs`/`PassiveSupplies` are PascalCase in the pak — the
field names are verbatim (a common trap: the camelCase guesses `timeSeconds`/`inputs`
do not exist).

## Cargo keys

Cargo keys are FNames matched case-insensitively while the table stores whatever was
typed (`Terra` and `terra` both exist). Always fold through `CargoKeys.Canonical`
(the Cargos-table spelling). Cargo slugs are the lowercased canonical key
(`WoodPlank_14ft_5t` → `woodplank_14ft_5t`).

## The wiki's cargo page layout

Infobox (`name`, `Cargo Type`, `Volume`, `Weight`, `Payment`) → `====== {name} ======` →
`===== Specifications =====` (Type, Weight, Payment per km, Payment multiplier, Base
payment, Min/Max delivery distance, Stackable, Can be pickup, Fragile) → Compatible
Cargo Space Types → `===== Production =====` (Produced At / Consumed At). No
In-other-languages section on cargo pages. "Can be pickup" = type ∈ {SmallPackage,
Food, MilitarySupply}. The full template is in `.agents/knowledge/wiki-pages.md`.
