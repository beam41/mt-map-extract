use crate::ue_type::{self};
use serde::{Deserialize, Serialize};
use std::num::NonZeroI64;

#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector3 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl From<ue_type::Vector3> for Vector3 {
    fn from(value: ue_type::Vector3) -> Self {
        Vector3 {
            x: value.x,
            y: value.y,
            z: value.z,
        }
    }
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct ProductionCargo {
    #[serde(rename = "cargoType")]
    pub cargo_type: Option<String>,
    #[serde(rename = "cargoKey")]
    pub cargo_key: Option<String>,
    pub value: i64,
    #[serde(rename = "maxStorage")]
    pub max_storage: Option<NonZeroI64>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct ProductionConfig {
    #[serde(rename = "inputCargos")]
    pub input_cargos: Vec<ProductionCargo>,
    #[serde(rename = "outputCargos")]
    pub output_cargos: Vec<ProductionCargo>,
    #[serde(rename = "productionTimeSeconds")]
    pub production_time_seconds: f64,
    #[serde(rename = "productionSpeedMultiplier")]
    pub production_speed_multiplier: f64,
    #[serde(rename = "localFoodSupply")]
    pub local_food_supply: f64,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct DemandConfig {
    #[serde(rename = "cargoType")]
    pub cargo_type: Option<String>,
    #[serde(rename = "cargoKey")]
    pub cargo_key: Option<String>,
    #[serde(rename = "maxStorage")]
    pub max_storage: Option<NonZeroI64>,
    #[serde(rename = "paymentMultiplier")]
    pub payment_multiplier: f64,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct DeliveryPoint {
    #[serde(rename = "type")]
    pub type_field: String,
    pub name: Option<String>,
    #[serde(rename = "relativeLocation")]
    pub relative_location: Option<Vector3>,
    pub guid: Option<String>,
    #[serde(rename = "guidShort")]
    pub guid_short: Option<String>,
    #[serde(rename = "productionConfigs")]
    pub production_configs: Vec<ProductionConfig>,
    #[serde(rename = "demandConfigs")]
    pub demand_configs: Vec<DemandConfig>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct BusStopPoint {
    #[serde(rename = "type")]
    pub type_field: String,
    pub name: Option<String>,
    #[serde(rename = "relativeLocation")]
    pub relative_location: Option<Vector3>,
    pub guid: Option<String>,
    #[serde(rename = "guidShort")]
    pub guid_short: Option<String>,
    #[serde(rename = "additionalDestinationsGuid")]
    pub additional_destinations_guid: Vec<Guid>,
    pub terminal: bool,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct Guid {
    pub guid: Option<String>,
    #[serde(rename = "guidShort")]
    pub guid_short: Option<String>,
}
