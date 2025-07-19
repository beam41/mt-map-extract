use crate::output_type::{self, AreaVolume, Vector2};
use crate::output_type::{ProductionCargo, Vector3};
use crate::ue_type::{self, DemandConfig, ObjectPath, ProductionConfig, StorageConfig, UObject};
use std::collections::HashMap;
use std::num::NonZeroI64;

pub fn get_obj_path(obj_path: &ObjectPath) -> (String, usize) {
    let obj_path_parsed: Vec<String> = obj_path
        .object_path
        .clone()
        .unwrap_or("".to_string())
        .split('.')
        .map(|s| s.to_string())
        .collect();
    let obj_path_index_str = &obj_path_parsed[1];
    let obj_path_index = obj_path_index_str.parse::<usize>().unwrap();
    (obj_path_parsed[0].to_string(), obj_path_index)
}

pub fn extract_name(obj: &UObject) -> Option<String> {
    let mission_point_name = obj
        .properties
        .as_ref()
        .and_then(|p| p.mission_point_name.as_ref());
    let point_name = obj.properties.as_ref().and_then(|p| p.point_name.as_ref());
    let delivery_point_name = obj
        .properties
        .as_ref()
        .and_then(|p| p.delivery_point_name.as_ref());

    let name = if point_name.is_some() {
        Some(
            point_name
                .unwrap()
                .texts
                .iter()
                .map(|t| t.localized_string.as_deref().unwrap_or(""))
                .collect::<Vec<_>>()
                .join(" "),
        )
    } else {
        None
    };

    if name.is_some() {
        return name;
    }

    let name = if mission_point_name.is_some() {
        let name = mission_point_name.unwrap();
        match name.localized_string.as_ref() {
            Some(s) => Some(s.clone()),
            None => name.culture_invariant_string.clone(),
        }
    } else {
        None
    };

    if name.is_some() {
        return name;
    }

    let name = if delivery_point_name.is_some() {
        let d_point = delivery_point_name.unwrap();
        let name = &d_point.name;
        let name = match name.localized_string.as_ref() {
            Some(s) => Some(s.clone()),
            None => name.culture_invariant_string.clone(),
        };
        if name.is_some() && d_point.number.is_some() {
            Some(format!("{} {}", name.unwrap(), d_point.number.unwrap()))
        } else {
            name.clone()
        }
    } else {
        None
    };

    if name.is_some() {
        return name;
    }

    None
}

pub fn extract_relative_location(obj: &UObject) -> Option<Vector3> {
    obj.properties
        .as_ref()
        .and_then(|p| p.relative_location.map(|r| r.into()))
}

pub fn extract_delivery_point_guid(obj: &UObject) -> (Option<String>, Option<String>) {
    let delivery_point_guid = obj
        .properties
        .as_ref()
        .and_then(|p| p.delivery_point_guid.clone());
    let delivery_point_guid_short = delivery_point_guid
        .as_ref()
        .and_then(|id| Some(id.replace("-", "").to_lowercase()));
    (delivery_point_guid, delivery_point_guid_short)
}

pub fn extract_max_storage(obj: &UObject) -> Option<NonZeroI64> {
    obj.properties.as_ref().and_then(|p| p.max_storage)
}

pub fn extract_production_configs(obj: &UObject) -> &Vec<ProductionConfig> {
    obj.properties
        .as_ref()
        .and_then(|p| Some(&p.production_configs))
        .unwrap()
}

pub fn extract_demand_configs(obj: &UObject) -> Option<&Vec<DemandConfig>> {
    obj.properties
        .as_ref()
        .and_then(|p| p.demand_configs.as_ref())
    }

pub fn extract_storage_configs(obj: &UObject) -> Option<&Vec<StorageConfig>> {
    obj.properties
        .as_ref()
        .and_then(|p| Some(&p.storage_configs))
        .unwrap()
        .as_ref()
}

pub fn map_max_storage(
    storage_config: Option<&StorageConfig>,
    default_max_storage: Option<NonZeroI64>,
) -> Option<NonZeroI64> {
    match storage_config {
        Some(config) => match config.max_storage {
            Some(max_storage) => Some(max_storage),
            None => default_max_storage,
        },
        None => default_max_storage,
    }
}

pub fn map_max_storage_demand_config(
    demand_config: Option<&output_type::DemandConfig>,
    storage_config: Option<&StorageConfig>,
    default_max_storage: Option<NonZeroI64>,
) -> Option<NonZeroI64> {
    let others_max_storage = map_max_storage(storage_config, default_max_storage);

    match demand_config {
        Some(config) => match config.max_storage {
            Some(max_storage) => Some(max_storage),
            None => others_max_storage,
        },
        None => others_max_storage,
    }
}

pub fn map_production_configs(
    world_obj: &UObject,
    main_obj: &UObject,
    template_obj: Option<&UObject>,
    storage_configs: &Vec<StorageConfig>,
    demand_configs: &Vec<output_type::DemandConfig>,
    default_max_storage: Option<NonZeroI64>,
) -> (Vec<output_type::ProductionConfig>, HashMap<String, Option<NonZeroI64>>) {
    let production_configs = match extract_production_configs(world_obj) {
        n if n.len() > 0 => n,
        _ => match extract_production_configs(main_obj) {
            n if n.len() > 0 => n,
            _ => match template_obj.as_ref() {
                Some(obj) => extract_production_configs(obj),
                None => &vec![],
            },
        },
    };

    let mut storage: HashMap<String, Option<NonZeroI64>> = HashMap::new();

    let config = production_configs
        .iter()
        .map(|config| {
            let mut input_cargos: Vec<ProductionCargo> = vec![];
            for cargo in &config.input_cargos {
                let demand_configs = demand_configs
                    .iter()
                    .find(|c| c.cargo_key.as_ref() == Some(&cargo.key));
                let max_storage = map_max_storage_demand_config(
                    demand_configs,
                    storage_configs.iter().find(|c| c.cargo_key == cargo.key),
                    default_max_storage,
                );
                input_cargos.push(ProductionCargo {
                    cargo_key: Some(cargo.key.clone()),
                    cargo_type: None,
                    max_storage,
                    value: cargo.value,
                });
            }
            for cargo in &config.input_cargo_types {
                let demand_configs = demand_configs
                    .iter()
                    .find(|c| c.cargo_type.as_ref() == Some(&cargo.key));
                let max_storage = map_max_storage_demand_config(
                    demand_configs,
                    storage_configs.iter().find(|c| c.cargo_type == cargo.key),
                    default_max_storage,
                );
                input_cargos.push(ProductionCargo {
                    cargo_type: Some(cargo.key.clone()),
                    cargo_key: None,
                    max_storage,
                    value: cargo.value,
                });
            }

            let mut output_cargos: Vec<ProductionCargo> = vec![];
            for cargo in &config.output_cargos {
                let demand_configs = demand_configs
                    .iter()
                    .find(|c| c.cargo_key.as_ref() == Some(&cargo.key));
                let max_storage = map_max_storage_demand_config(
                    demand_configs,
                    storage_configs.iter().find(|c| c.cargo_key == cargo.key),
                    default_max_storage,
                );
                output_cargos.push(ProductionCargo {
                    cargo_key: Some(cargo.key.clone()),
                    cargo_type: None,
                    max_storage,
                    value: cargo.value,
                });
            }
            for cargo in &config.output_cargo_types {
                let demand_configs = demand_configs
                    .iter()
                    .find(|c| c.cargo_type.as_ref() == Some(&cargo.key));
                let max_storage = map_max_storage_demand_config(
                    demand_configs,
                    storage_configs.iter().find(|c| c.cargo_type == cargo.key),
                    default_max_storage,
                );
                output_cargos.push(ProductionCargo {
                    cargo_type: Some(cargo.key.clone()),
                    cargo_key: None,
                    max_storage,
                    value: cargo.value,
                });
            }

            for cargo in &input_cargos {
                if let Some(key) = &cargo.cargo_key {
                    if !storage.contains_key(key) && cargo.max_storage.is_some() {
                        storage.insert(
                            key.clone(),
                            cargo.max_storage,
                        );
                    }
                } else if let Some(key) = &cargo.cargo_type {
                    if !storage.contains_key(key) && cargo.max_storage.is_some() {
                        storage.insert(
                            key.clone(),
                            cargo.max_storage,
                        );
                    }
                }
            }

            for cargo in &output_cargos {
                if let Some(key) = &cargo.cargo_key {
                    if !storage.contains_key(key) && cargo.max_storage.is_some() {
                        storage.insert(
                            key.clone(),
                              cargo.max_storage,
                        );
                    }
                } else if let Some(key) = &cargo.cargo_type {
                    if !storage.contains_key(key) && cargo.max_storage.is_some() {
                        storage.insert(
                            key.clone(),
                             cargo.max_storage,
                        );
                    }
                }
            }

            let input_cargos: HashMap<String, NonZeroI64> = input_cargos
                .into_iter()
                .filter_map(|cargo| {
                    if let Some(key) = cargo.cargo_key {
                        Some((key, NonZeroI64::new(cargo.value).unwrap()))
                    } else if let Some(key) = cargo.cargo_type {
                        Some((key, NonZeroI64::new(cargo.value).unwrap()))
                    } else {
                        None
                    }
                })
                .collect();

            let output_cargos: HashMap<String, NonZeroI64> = output_cargos
                .into_iter()
                .filter_map(|cargo| {
                    if let Some(key) = cargo.cargo_key {
                        Some((key, NonZeroI64::new(cargo.value).unwrap()))
                    } else if let Some(key) = cargo.cargo_type {
                        Some((key, NonZeroI64::new(cargo.value).unwrap()))
                    } else {
                        None
                    }
                })
                .collect();

            (output_type::ProductionConfig {
                input_cargos,
                output_cargos,
                production_time_seconds: config.production_time_seconds,
                production_speed_multiplier: config.production_speed_multiplier,
                local_food_supply: config.local_food_supply,
            })
        })
        .collect();
    (config, storage)
}

pub fn map_demand_configs(
    world_obj: &UObject,
    main_obj: &UObject,
    template_obj: Option<&UObject>,
    storage_configs: &Vec<StorageConfig>,
    default_max_storage: Option<NonZeroI64>,
) -> Vec<output_type::DemandConfig> {
    let demand_configs = match extract_demand_configs(world_obj) {
        Some(n) => n.to_owned(),
        _ => match extract_demand_configs(main_obj) {
            Some(n) => n.to_owned(),
            _ => match template_obj.as_ref() {
                Some(obj) => match extract_demand_configs(obj) {
                    Some(n) => n.to_owned(),
                    None => vec![],
                },
                None => vec![],
            },
        },
    };

    demand_configs
        .iter()
        .map(|config| {
            let max_storage = map_max_storage(
                storage_configs
                    .iter()
                    .find(|c| c.cargo_key == config.cargo_key && c.cargo_type == config.cargo_type),
                default_max_storage,
            );

            output_type::DemandConfig {
                cargo_key: match config.cargo_key.as_str() {
                    "None" => None,
                    key => Some(key.to_string()),
                },
                cargo_type: match config.cargo_type.as_str() {
                    "EDeliveryCargoType::None" => None,
                    key => Some(key.to_string()),
                },
                max_storage: config.max_storage.or(max_storage),
                payment_multiplier: config.payment_multiplier,
            }
        })
        .collect()
}

pub fn extract_bus_stop_guid(obj: &UObject) -> (Option<String>, Option<String>) {
    let bus_stop_guid = obj
        .properties
        .as_ref()
        .and_then(|p| p.bus_stop_guid.clone());
    let bus_stop_guid_short = bus_stop_guid
        .as_ref()
        .and_then(|id| Some(id.replace("-", "").to_lowercase()));
    (bus_stop_guid, bus_stop_guid_short)
}

pub fn check_is_terminal(obj: &UObject) -> bool {
    obj.properties
        .as_ref()
        .and_then(|p| Some(p.tags.iter().any(|t| t == "BusTerminal")))
        .unwrap_or(false)
}

pub fn extract_additional_destinations(obj: &UObject) -> Vec<ObjectPath> {
    obj.properties
        .as_ref()
        .and_then(|p| p.additional_destinations.clone())
        .unwrap_or(vec![])
}

pub fn extract_housereg_key(obj: &UObject) -> String {
    obj.properties
        .as_ref()
        .and_then(|p| p.houseg_key.clone())
        .unwrap_or("".to_string())
}

pub fn extract_area_name(obj: &UObject) -> String {
    let name = obj
        .properties
        .as_ref()
        .and_then(|p| p.area_name.as_ref())
        .and_then(|p| Some(p.localized_string.clone()))
        .unwrap_or("".to_string());

    if name.len() > 0 {
        return name;
    }

    obj.properties
        .as_ref()
        .and_then(|p| p.area_name_texts.as_ref())
        .and_then(|texts| {
            Some(
                texts
                    .texts
                    .iter()
                    .map(|t| t.localized_string.clone())
                    .collect::<Vec<_>>()
                    .join(" "),
            )
        })
        .unwrap_or("".to_string())
}

pub fn extract_area_flag(obj: &UObject) -> String {
    obj.properties
        .as_ref()
        .and_then(|p| p.area_volume_flags.get(0).map(|f| f.clone()))
        .unwrap_or("".to_string())
}

pub fn extract_top_view_lines(obj: &UObject) -> Vec<Vector2> {
    obj.properties
        .as_ref()
        .and_then(|p| {
            Some(
                p.top_view_lines
                    .iter()
                    .map(|l| Vector2 { x: l.x, y: l.y })
                    .collect::<Vec<Vector2>>(),
            )
        })
        .unwrap_or(vec![])
}

pub fn get_enclose_area(point: &Vector2, areas: &[AreaVolume]) -> Vec<AreaVolume> {
    areas
        .iter()
        .filter(|area| point_in_polygon(point, &area.vertex))
        .cloned()
        .collect()
}

/// Check if a point is inside a polygon using the ray casting algorithm
fn point_in_polygon(point: &Vector2, polygon: &[Vector2]) -> bool {
    let mut line: Vec<(Vector2, Vector2)> = vec![];
    for i in (0..polygon.len()).step_by(2) {
        if i + 1 < polygon.len() {
            line.push((polygon[i], polygon[i + 1]));
        }
    }

    let mut count = 0;
    for (a, b) in &line {
        let (y1, y2) = (a.y, b.y);
        let (x1, x2) = (a.x, b.x);
        if (y1 > point.y) != (y2 > point.y) {
            let x_intersect = x1 + (point.y - y1) * (x2 - x1) / (y2 - y1);
            if x_intersect > point.x {
                count += 1;
            }
        }
    }
    count % 2 == 1
}

pub fn area_volumes_to_location(area: &[AreaVolume]) -> String {
    let racetrack = area
        .iter()
        .find(|a| a.flag == "EMTAreaVolumeFlags::RaceTrack")
        .map(|a| a.name.clone());
    let small = area
        .iter()
        .find(|a| a.flag == "EMTAreaVolumeFlags::SmallArea")
        .map(|a| a.name.clone());
    let large = area
        .iter()
        .find(|a| a.flag == "EMTAreaVolumeFlags::LargeArea")
        .map(|a| a.name.clone());
    let zone = area
        .iter()
        .find(|a| a.flag == "EMTAreaVolumeFlags::Zone")
        .map(|a| a.name.clone());

    let mut result = String::new();
    if let Some(racetrack) = racetrack {
        result.push_str(&racetrack);
    }
    if let Some(small) = small {
        if result.len() > 0 {
            result.push_str(", ");
        }
        result.push_str(&small);
    }
    if let Some(large) = large {
        if result.len() > 0 {
            result.push_str(", ");
        }
        result.push_str(&large);
    }
    if let Some(zone) = zone {
        if result.len() > 0 {
            result.push_str(", ");
        }
        result.push_str(&zone);
    }

    result
}

pub fn extract_area_size(obj: &UObject) -> ue_type::Vector3 {
    obj.properties
        .as_ref()
        .and_then(|p| p.area_size)
        .unwrap_or(ue_type::Vector3 {
            x: 0.0,
            y: 0.0,
            z: 0.0,
        })
}
