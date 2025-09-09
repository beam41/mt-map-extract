use crate::ue_type::{self};
use serde::{Deserialize, Serialize};
use std::{collections::HashMap, num::NonZeroI64};

#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector3 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector2 {
    pub x: f64,
    pub y: f64,
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

impl From<Vector3> for Vector2 {
    fn from(value: Vector3) -> Self {
        Vector2 {
            x: value.x,
            y: value.y,
        }
    }
}

impl From<ue_type::Vector3> for Vector2 {
    fn from(value: ue_type::Vector3) -> Self {
        Vector2 {
            x: value.x,
            y: value.y,
        }
    }
}

fn is_false(boolean: &bool) -> bool {
    !*boolean
}

fn is_zero(num: &f64) -> bool {
    *num == 0.0
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct ProductionCargo {
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "type")]
    pub cargo_type: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "key")]
    pub cargo_key: Option<String>,
    pub value: i64,
    #[serde(rename = "maxStorage")]
    pub max_storage: Option<NonZeroI64>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct ProductionConfig {
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    #[serde(rename = "input")]
    pub input_cargos: HashMap<String, NonZeroI64>,
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    #[serde(rename = "output")]
    pub output_cargos: HashMap<String, NonZeroI64>,
    #[serde(rename = "prodTime")]
    pub production_time_seconds: f64,
    #[serde(rename = "prodSpeedMul")]
    pub production_speed_multiplier: f64,
    #[serde(skip_serializing_if = "is_zero")]
    #[serde(rename = "foodSupply")]
    pub local_food_supply: f64,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct DemandConfig {
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "cargoType")]
    pub cargo_type: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "cargoKey")]
    pub cargo_key: Option<String>,
    #[serde(rename = "maxStorage")]
    pub max_storage: Option<NonZeroI64>,
    #[serde(rename = "paymentMul")]
    pub payment_multiplier: f64,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct DeliveryPoint {
    #[serde(rename = "type")]
    pub type_field: String,
    pub name: Option<String>,
    #[serde(rename = "coord")]
    pub relative_location: Option<Vector3>,
    pub guid: Option<String>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    #[serde(rename = "prod")]
    pub production_configs: Vec<ProductionConfig>,
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    #[serde(rename = "demand")]
    pub demand_configs: HashMap<String, f64>,
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    #[serde(rename = "demandStorage")]
    pub demand_storage_configs: HashMap<String, Option<NonZeroI64>>,
    #[serde(rename = "supplyStorage")]
    pub supply_storage_configs: HashMap<String, Option<NonZeroI64>>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    #[serde(rename = "dropPoint")]
    pub drop_point: Vec<String>,
    #[serde(rename = "maxDist")]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_delivery_distance: Option<f64>,
    #[serde(rename = "maxReceiveDist")]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_delivery_receive_distance: Option<f64>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct BusStopPoint {
    #[serde(rename = "type")]
    pub type_field: String,
    pub name: Option<String>,
    #[serde(rename = "coord")]
    pub relative_location: Option<Vector3>,
    pub guid: Option<String>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    #[serde(rename = "additionalDest")]
    pub additional_destinations_guid: Vec<Guid>,
    #[serde(skip_serializing_if = "is_false")]
    pub terminal: bool,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct Guid {
    pub guid: Option<String>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct EvChargerPoint {
    #[serde(rename = "coord")]
    pub relative_location: Option<Vector3>,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct HousePoint {
    pub name: String,
    #[serde(rename = "coord")]
    pub relative_location: Option<Vector3>,
    pub size: Vector2,
    pub cost: i64,
}

#[derive(Default, Debug, Clone, PartialEq, Serialize)]
pub struct AreaVolume {
    pub name: String,
    pub flag: String,
    pub vertex: Vec<Vector2>,
}
