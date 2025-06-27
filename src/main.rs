use helper::{
    check_is_terminal, extract_additional_destinations, extract_bus_stop_guid,
    extract_delivery_point_guid, extract_max_storage, extract_name, extract_relative_location,
    extract_storage_configs, get_obj_path, map_demand_configs, map_production_configs,
};
use output_type::{BusStopPoint, DeliveryPoint, Guid};
use std::fs::{self, File};
use std::io::BufReader;
use std::num::NonZeroI64;
use std::time::Instant;
use ue_type::UObject;

use crate::helper::{extract_area_flag, extract_area_volume_key, extract_housereg_key};
use crate::output_type::{AreaVolume, EvChargerPoint, HousePoint};

mod deserialize_zero_as_none;
mod helper;
mod output_type;
mod ue_type;

fn extract_delivery_point() {
    let world_file = File::open("./MotorTown/Content/Maps/Jeju/Jeju_World.json").unwrap();
    let reader = BufReader::new(world_file);
    let now = Instant::now();
    let world = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

    let elapsed = now.elapsed();
    println!("Parse world JSON took: {:.2?}", elapsed);

    let now = Instant::now();
    let mut output: Vec<DeliveryPoint> = vec![];
    let delivery_point_dir =
        fs::read_dir("./MotorTown/Content/Objects/Mission/Delivery/DeliveryPoint").unwrap();
    for file_result in delivery_point_dir {
        let file = file_result.unwrap();
        let point_file = File::open(file.path()).unwrap();
        let reader = BufReader::new(point_file);
        let obj_metadata = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

        let blueprint_generated = &obj_metadata[0];

        let blueprint_default_object = blueprint_generated.class_default_object.as_ref().unwrap();

        let (_, main_obj_index) = get_obj_path(blueprint_default_object);

        let main_obj = &obj_metadata[main_obj_index];
        let template_obj = if main_obj.template.is_some() {
            let (main_obj_template_path, main_obj_template_index) =
                get_obj_path(main_obj.template.as_ref().unwrap());

            let template_file = File::open(main_obj_template_path.to_string() + ".json").unwrap();
            let reader = BufReader::new(template_file);
            let obj_metadata = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

            Some(obj_metadata[main_obj_template_index].clone())
        } else {
            None
        };

        let main_obj_name = match extract_name(main_obj) {
            Some(n) => Some(n),
            None => match template_obj.as_ref() {
                Some(obj) => extract_name(obj),
                None => None,
            },
        };

        let main_obj_max_storage = match extract_max_storage(main_obj) {
            Some(n) => Some(n),
            None => match template_obj.as_ref() {
                Some(obj) => extract_max_storage(obj),
                None => NonZeroI64::new(100),
            },
        };

        for world_obj in &world {
            if world_obj.type_field != main_obj.type_field {
                continue;
            }

            let (_, scene_obj_index) = get_obj_path(
                world_obj
                    .properties
                    .as_ref()
                    .unwrap()
                    .scene_component
                    .as_ref()
                    .unwrap(),
            );

            let scene_obj = &world[scene_obj_index];

            let (_, guid_short) = extract_delivery_point_guid(world_obj);
            let name = match extract_name(world_obj) {
                Some(n) => Some(n),
                None => main_obj_name.clone(),
            };
            let max_storage = match extract_max_storage(world_obj) {
                Some(s) => Some(s),
                None => main_obj_max_storage,
            };
            let storage_configs = match extract_storage_configs(world_obj) {
                Some(n) if n.len() > 0 => n,
                _ => match extract_storage_configs(main_obj) {
                    Some(n) if n.len() > 0 => n,
                    _ => match template_obj.as_ref() {
                        Some(obj) => match extract_storage_configs(obj) {
                            Some(n) if n.len() > 0 => n,
                            _ => &vec![],
                        },
                        None => &vec![],
                    },
                },
            };
            let demand_configs = map_demand_configs(
                world_obj,
                main_obj,
                template_obj.as_ref(),
                storage_configs,
                max_storage,
            );
            let production_configs = map_production_configs(
                world_obj,
                main_obj,
                template_obj.as_ref(),
                storage_configs,
                &demand_configs,
                max_storage,
            );

            let delivery_point_output = DeliveryPoint {
                type_field: world_obj.type_field.clone(),
                name,
                guid: guid_short,
                relative_location: extract_relative_location(scene_obj),
                production_configs,
                demand_configs,
            };
            output.push(delivery_point_output);
        }
    }
    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_delivery_point.json", r).unwrap();
    }
}

fn extract_bus_stop() {
    let world_file = File::open("./MotorTown/Content/Maps/Jeju/Jeju_World.json").unwrap();
    let reader = BufReader::new(world_file);
    let now = Instant::now();
    let world = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

    let elapsed = now.elapsed();
    println!("Parse world JSON took: {:.2?}", elapsed);

    let now = Instant::now();
    let mut output: Vec<BusStopPoint> = vec![];
    for world_obj in &world {
        if !matches!(
            world_obj.type_field.as_str(),
            "BusStop_01_C" | "BusStop_02_C" | "BusStop_03_C" | "BusTerminal_01_C"
        ) {
            continue;
        }

        let (_, scene_obj_index) = get_obj_path(
            world_obj
                .properties
                .as_ref()
                .unwrap()
                .root_component
                .as_ref()
                .unwrap(),
        );

        let scene_obj = &world[scene_obj_index];

        let (_, guid_short) = extract_bus_stop_guid(world_obj);
        let bus_stop_name = world_obj
            .properties
            .as_ref()
            .and_then(|p| p.bus_stop_name.as_ref());
        let bus_stop_display_name = world_obj
            .properties
            .as_ref()
            .and_then(|p| p.bus_stop_display_name.as_ref())
            .and_then(|n| n.source_string.as_ref());
        let name = if bus_stop_name.is_some() {
            Some(
                bus_stop_name
                    .unwrap()
                    .texts
                    .iter()
                    .map(|t| t.source_string.as_deref().unwrap_or(""))
                    .collect::<Vec<_>>()
                    .join(" "),
            )
        } else {
            bus_stop_display_name.cloned()
        };
        let relative_location = extract_relative_location(scene_obj);
        let terminal = check_is_terminal(world_obj);
        let additional_destinations = extract_additional_destinations(world_obj);

        output.push(BusStopPoint {
            type_field: world_obj.type_field.clone(),
            name,
            relative_location,
            guid: guid_short,
            terminal,
            additional_destinations_guid: additional_destinations
                .iter()
                .map(|ref_obj_path| {
                    let (_, ref_obj_index) = get_obj_path(ref_obj_path);
                    let ref_obj = &world[ref_obj_index];
                    let (_, guid_short) = extract_bus_stop_guid(ref_obj);
                    Guid { guid: guid_short }
                })
                .collect(),
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_bus_stop.json", r).unwrap();
    }
}

fn extract_ev_charger() {
    let now = Instant::now();
    let mut output: Vec<EvChargerPoint> = vec![];
    let generated_dir =
        fs::read_dir("./MotorTown/Content/Maps/Jeju/Jeju_World/_Generated_").unwrap();
    for file_result in generated_dir {
        let file = file_result.unwrap();
        let point_file = File::open(file.path()).unwrap();
        let reader = BufReader::new(point_file);
        let obj_metadata = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

        for obj in &obj_metadata {
            if obj.type_field.as_str() != "EVCharger_C" {
                continue;
            }

            let (_, scene_obj_index) = get_obj_path(
                obj.properties
                    .as_ref()
                    .unwrap()
                    .root_component
                    .as_ref()
                    .unwrap(),
            );

            let scene_obj = &obj_metadata[scene_obj_index];
            let relative_location = extract_relative_location(scene_obj);

            output.push(EvChargerPoint { relative_location });
        }
    }
    let elapsed = now.elapsed();
    println!("Aggregate+Parse data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_ev_charger.json", r).unwrap();
    }
}

fn extract_house() {
    let world_file = File::open("./MotorTown/Content/Maps/Jeju/Jeju_World.json").unwrap();
    let reader = BufReader::new(world_file);
    let now = Instant::now();
    let world = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

    let elapsed = now.elapsed();
    println!("Parse world JSON took: {:.2?}", elapsed);

    let now = Instant::now();
    let mut output: Vec<HousePoint> = vec![];
    for world_obj in &world {
        if world_obj.type_field.as_str() != "House_C" {
            continue;
        }

        let (_, scene_obj_index) = get_obj_path(
            world_obj
                .properties
                .as_ref()
                .unwrap()
                .root_component
                .as_ref()
                .unwrap(),
        );

        let scene_obj = &world[scene_obj_index];
        let relative_location = extract_relative_location(scene_obj);

        output.push(HousePoint {
            name: extract_housereg_key(world_obj),
            relative_location,
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_house.json", r).unwrap();
    }
}

fn extract_area_volume() {
    let world_file = File::open("./MotorTown/Content/Maps/Jeju/Jeju_World.json").unwrap();
    let reader = BufReader::new(world_file);
    let now = Instant::now();
    let world = serde_json::from_reader::<_, Vec<UObject>>(reader).unwrap();

    let elapsed = now.elapsed();
    println!("Parse world JSON took: {:.2?}", elapsed);

    let now = Instant::now();
    let mut output: Vec<AreaVolume> = vec![];
    for world_obj in &world {
        if world_obj.type_field.as_str() != "MTAreaVolume" {
            continue;
        }
        output.push(AreaVolume {
            name: extract_area_volume_key(world_obj),
            flag: extract_area_flag(world_obj),
            vertex: helper::extract_top_view_lines(world_obj),
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_area_volume.json", r).unwrap();
    }
}

fn main() {
    extract_delivery_point();
    extract_bus_stop();
    extract_ev_charger();
    extract_house();
    extract_area_volume();
}
