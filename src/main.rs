use helper::{
    check_is_terminal, extract_additional_destinations, extract_bus_stop_guid,
    extract_delivery_point_guid, extract_max_storage, extract_name, extract_relative_location,
    extract_storage_configs, get_obj_path, map_demand_configs, map_production_configs,
};
use output_type::{BusStopPoint, DeliveryPoint, Guid};
use std::collections::HashMap;
use std::fs::{self, File};
use std::io::BufReader;
use std::num::NonZeroI64;
use std::time::Instant;
use ue_type::UObject;

use crate::helper::{
    area_volumes_to_location, extract_area_flag, extract_area_name, extract_area_size,
    extract_housereg_key, extract_top_view_lines, get_enclose_area,
};
use crate::output_type::{AreaVolume, EvChargerPoint, HousePoint};
use crate::ue_type::House;

mod deserialize_zero_as_none;
mod helper;
mod output_type;
mod ue_type;

fn extract_delivery_point(areas: &[AreaVolume]) {
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
                    .root_component
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
            let ori_storage_configs = match extract_storage_configs(world_obj) {
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
            let flat_map = {
                let mut map: HashMap<String, Vec<String>> = HashMap::new();

                // EDeliveryCargoType::SmallPackage
                map.insert(
                    "EDeliveryCargoType::SmallPackage".to_string(),
                    vec![
                        "SmallBox".to_string(),
                        "CarrotBox".to_string(),
                        "AppleBox".to_string(),
                        "OrangeBox".to_string(),
                        "GlassBottleBox".to_string(),
                        "Rice".to_string(),
                        "PumpkinBox".to_string(),
                        "CornBox".to_string(),
                        "CheeseBox".to_string(),
                        "MeatBox".to_string(),
                        "BreadBox".to_string(),
                        "SnackBox".to_string(),
                        "GiftBox_01".to_string(),
                    ],
                );

                // EDeliveryCargoType::LargePackage
                map.insert(
                    "EDeliveryCargoType::LargePackage".to_string(),
                    vec![
                        "BoxPallete_01".to_string(),
                        "BoxPallete_02".to_string(),
                        "BoxPallete_03".to_string(),
                        "PowerBox".to_string(),
                        "OrangeBoxes".to_string(),
                        "RicePallet".to_string(),
                        "PumpkinPallet".to_string(),
                        "CornPallet".to_string(),
                        "BeanPallet".to_string(),
                        "HempPallet".to_string(),
                        "CabbagePallet".to_string(),
                        "ChilliPallet".to_string(),
                        "PotatoPallet".to_string(),
                        "CheesePallet".to_string(),
                        "BreadPallet".to_string(),
                    ],
                );

                // EDeliveryCargoType::None
                map.insert(
                    "EDeliveryCargoType::None".to_string(),
                    vec![
                        "PlasticPallete".to_string(),
                        "QuicklimePallet".to_string(),
                        "Fuel".to_string(),
                        "Oil".to_string(),
                        "CrudeOil".to_string(),
                        "LiveFish_01".to_string(),
                        "MilitarySupplyBox_01_Empty".to_string(),
                        "Milk".to_string(),
                        "AirlineMealPallet".to_string(),
                        "FormulaSCM".to_string(),
                        "PlasticPipes_6m".to_string(),
                        "lHBeam_6m".to_string(),
                        "SteelCoil_10t".to_string(),
                        "Cement".to_string(),
                        "Terra".to_string(),
                        "SunflowerSeed".to_string(),
                    ],
                );

                // EDeliveryCargoType::FinalProduct
                map.insert(
                    "EDeliveryCargoType::FinalProduct".to_string(),
                    vec!["ToyBoxes".to_string(), "BottlePallete".to_string()],
                );

                // EDeliveryCargoType::Wood
                map.insert(
                    "EDeliveryCargoType::Wood".to_string(),
                    vec!["WoodPlank_14ft_5t".to_string()],
                );

                // EDeliveryCargoType::Container
                map.insert(
                    "EDeliveryCargoType::Container".to_string(),
                    vec![
                        "Container_30ft_5t".to_string(),
                        "Container_30ft_10t".to_string(),
                        "Container_30ft_20t".to_string(),
                        "Container_20ft_01".to_string(),
                        "Container_40ft_01".to_string(),
                    ],
                );

                // EDeliveryCargoType::Log
                map.insert(
                    "EDeliveryCargoType::Log".to_string(),
                    vec![
                        "Log_30ft_30t".to_string(),
                        "Log_Oak_12ft".to_string(),
                        "Log_Oak_24ft".to_string(),
                        "Log_20ft".to_string(),
                    ],
                );

                // EDeliveryCargoType::Sand
                map.insert(
                    "EDeliveryCargoType::Sand".to_string(),
                    vec!["Sand".to_string(), "FineSand".to_string()],
                );

                // EDeliveryCargoType::Coal
                map.insert(
                    "EDeliveryCargoType::Coal".to_string(),
                    vec!["Coal".to_string()],
                );

                // EDeliveryCargoType::Stone
                map.insert(
                    "EDeliveryCargoType::Stone".to_string(),
                    vec![
                        "LimestoneRock".to_string(),
                        "Limestone".to_string(),
                        "IronOre".to_string(),
                    ],
                );

                // EDeliveryCargoType::Concrete
                map.insert(
                    "EDeliveryCargoType::Concrete".to_string(),
                    vec!["Concrete".to_string()],
                );

                // EDeliveryCargoType::Garbage
                map.insert(
                    "EDeliveryCargoType::Garbage".to_string(),
                    vec!["TrashBag".to_string(), "Trash_Big".to_string()],
                );

                // EDeliveryCargoType::Furniture
                map.insert(
                    "EDeliveryCargoType::Furniture".to_string(),
                    vec![
                        "Sofa_01".to_string(),
                        "Sofa_02".to_string(),
                        "Sofa_03".to_string(),
                        "Sofa_04".to_string(),
                        "Bed_01".to_string(),
                        "Bed_02".to_string(),
                        "Bed_03".to_string(),
                    ],
                );

                // EDeliveryCargoType::Food
                map.insert(
                    "EDeliveryCargoType::Food".to_string(),
                    vec![
                        "Pizza_01".to_string(),
                        "Pizza_02".to_string(),
                        "Pizza_03".to_string(),
                        "Pizza_04".to_string(),
                        "Pizza_05".to_string(),
                        "Pizza_01_Premium".to_string(),
                        "Burger_01".to_string(),
                        "Burger_01_Signature".to_string(),
                    ],
                );

                // EDeliveryCargoType::MilitarySupply
                map.insert(
                    "EDeliveryCargoType::MilitarySupply".to_string(),
                    vec!["MilitarySupplyBox_01".to_string()],
                );

                map
            };
            // Generate flattened version of storage_configs
            let mut flattened_storage_configs = ori_storage_configs.clone();

            for config in ori_storage_configs {
                if config.cargo_key == "None" {
                    if let Some(cargo_items) = flat_map.get(&config.cargo_type) {
                        // Add all cargo items from flat_map with the same max_storage
                        for cargo_item in cargo_items {
                            // Create new storage config for each cargo item
                            let new_config = ue_type::StorageConfig {
                                cargo_key: cargo_item.clone(),
                                cargo_type: config.cargo_type.clone(),
                                max_storage: config.max_storage,
                            };
                            flattened_storage_configs.push(new_config);
                        }
                    }
                }
            }

            if (world_obj.name.as_str() == "Farm_Corn_C_UAID_005056C000019DCE01_1220230832") {
                dbg!(&flattened_storage_configs);
            }

            let demand_configs = map_demand_configs(
                world_obj,
                main_obj,
                template_obj.as_ref(),
                &flattened_storage_configs,
                max_storage,
            );

            let (production_configs, mut demand_storage_configs, supply_storage_configs) =
                map_production_configs(
                    world_obj,
                    main_obj,
                    template_obj.as_ref(),
                    &flattened_storage_configs,
                    &demand_configs,
                    max_storage,
                );

            let relative_location = extract_relative_location(scene_obj);

            let area = match relative_location {
                Some(loc) => get_enclose_area(&loc.into(), areas),
                None => vec![],
            };

            let location = area_volumes_to_location(&area);

            for cargo in &demand_configs {
                if let Some(key) = &cargo.cargo_key {
                    if !demand_storage_configs.contains_key(key) && cargo.max_storage.is_some() {
                        demand_storage_configs.insert(key.clone(), cargo.max_storage);
                    }
                } else if let Some(key) = &cargo.cargo_type {
                    if !demand_storage_configs.contains_key(key) && cargo.max_storage.is_some() {
                        demand_storage_configs.insert(key.clone(), cargo.max_storage);
                    }
                }
            }

            let demand_configs: HashMap<String, f64> = demand_configs
                .iter()
                .filter_map(|cargo| {
                    if let Some(key) = &cargo.cargo_key {
                        Some((key.clone(), cargo.payment_multiplier))
                    } else if let Some(key) = &cargo.cargo_type {
                        Some((key.clone(), cargo.payment_multiplier))
                    } else {
                        None
                    }
                })
                .collect();

            let mut drop_point: Vec<String> = vec![];

            if let Some(drop_points) = world_obj
                .properties
                .as_ref()
                .and_then(|p| Some(&p.input_inventory_share))
            {
                for drop_point_obj in drop_points {
                    let (_, drop_point_index) = get_obj_path(&drop_point_obj);
                    let drop_point_obj = &world[drop_point_index];
                    let (_, guid_short) = extract_delivery_point_guid(drop_point_obj);
                    if let Some(guid) = guid_short {
                        drop_point.push(guid);
                    }
                }
            }

            let max_delivery_distance_world = world_obj
                .properties
                .as_ref()
                .and_then(|p| p.max_delivery_distance);

            let max_delivery_distance = match max_delivery_distance_world {
                Some(distance) => Some(distance),
                None => main_obj
                    .properties
                    .as_ref()
                    .and_then(|p| p.max_delivery_distance),
            };

            let max_delivery_receive_distance_world = world_obj
                .properties
                .as_ref()
                .and_then(|p| p.max_delivery_receive_distance);

            let max_delivery_receive_distance = match max_delivery_receive_distance_world {
                Some(distance) => Some(distance),
                None => main_obj
                    .properties
                    .as_ref()
                    .and_then(|p| p.max_delivery_receive_distance),
            };

            let delivery_point_output = DeliveryPoint {
                type_field: world_obj.type_field.clone(),
                name,
                guid: guid_short,
                relative_location,
                production_configs,
                demand_configs,
                demand_storage_configs,
                supply_storage_configs,
                drop_point,
                max_delivery_distance,
                max_delivery_receive_distance,
            };
            output.push(delivery_point_output);
        }
    }
    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    // Fix storage inheritance: parent should inherit storage values from dropPoints
    let mut guid_to_index: HashMap<String, usize> = HashMap::new();
    for (idx, item) in output.iter().enumerate() {
        if let Some(guid) = &item.guid {
            guid_to_index.insert(guid.clone(), idx);
        }
    }

    // Update parent storage values to match dropPoint storage
    for i in 0..output.len() {
        let drop_point_guids = output[i].drop_point.clone();
        if !drop_point_guids.is_empty() {
            let mut updated_storage = output[i].demand_storage_configs.clone();

            for drop_point_guid in &drop_point_guids {
                if let Some(&drop_point_idx) = guid_to_index.get(drop_point_guid) {
                    let drop_point = &output[drop_point_idx];

                    // Inherit storage values from dropPoint
                    for (key, value) in &drop_point.demand_storage_configs {
                        updated_storage.insert(key.clone(), *value);
                    }
                }
            }

            output[i].demand_storage_configs = updated_storage;
        }
    }

    //fix here

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_delivery_point.json", r).unwrap();
    }
}

fn extract_bus_stop(areas: &[AreaVolume]) {
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
            .and_then(|n| n.localized_string.as_ref());
        let name = if bus_stop_name.is_some() {
            Some(
                bus_stop_name
                    .unwrap()
                    .texts
                    .iter()
                    .map(|t| t.localized_string.as_deref().unwrap_or(""))
                    .collect::<Vec<_>>()
                    .join(" "),
            )
        } else {
            bus_stop_display_name.cloned()
        };
        let relative_location = extract_relative_location(scene_obj);
        let terminal = check_is_terminal(world_obj);
        let additional_destinations = extract_additional_destinations(world_obj);

        let area = match relative_location {
            Some(loc) => get_enclose_area(&loc.into(), areas),
            None => vec![],
        };

        let location = area_volumes_to_location(&area);

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
            // location,
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_bus_stop.json", r).unwrap();
    }
}

fn extract_ev_charger(areas: &[AreaVolume]) {
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

            let area = match relative_location {
                Some(loc) => get_enclose_area(&loc.into(), areas),
                None => vec![],
            };

            let location = area_volumes_to_location(&area);

            output.push(EvChargerPoint {
                relative_location,
                // location,
            });
        }
    }
    let elapsed = now.elapsed();
    println!("Aggregate+Parse data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_ev_charger.json", r).unwrap();
    }
}

fn extract_house(areas: &[AreaVolume]) {
    let world_file = File::open("./MotorTown/Content/Maps/Jeju/Jeju_World.json").unwrap();
    let world_reader = BufReader::new(world_file);

    let house_file = File::open("./MotorTown/Content/DataAsset/Houses.json").unwrap();
    let house_reader = BufReader::new(house_file);

    let now = Instant::now();
    let world = serde_json::from_reader::<_, Vec<UObject>>(world_reader).unwrap();
    let house_vec = serde_json::from_reader::<_, Vec<House>>(house_reader).unwrap();
    let house = &house_vec[0];

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

        let area = match relative_location {
            Some(loc) => get_enclose_area(&loc.into(), areas),
            None => vec![],
        };

        let location = area_volumes_to_location(&area);
        let mut area_size = extract_area_size(&world_obj);
        let name = extract_housereg_key(world_obj);

        let house_cost = house
            .rows
            .get(&name)
            .and_then(|h| Some(h.cost))
            .unwrap_or(0);

        if area_size.x == 0.0 && area_size.y == 0.0 {
            area_size = ue_type::Vector3 {
                x: 2000.0,
                y: 2000.0,
                z: 0.0,
            };
        }

        output.push(HousePoint {
            name,
            relative_location,
            // location,
            size: area_size.into(),
            cost: house_cost,
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_house.json", r).unwrap();
    }
}

fn extract_area_volume() -> Vec<AreaVolume> {
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
            name: extract_area_name(world_obj),
            flag: extract_area_flag(world_obj),
            vertex: extract_top_view_lines(world_obj),
        });
    }

    let elapsed = now.elapsed();
    println!("Aggregate data took: {:.2?}", elapsed);

    if let Ok(r) = serde_json::to_string_pretty(&output) {
        fs::write("./out_area_volume.json", r).unwrap();
    }

    output
}

fn main() {
    let areas = extract_area_volume();
    extract_delivery_point(&areas);
    extract_bus_stop(&areas);
    extract_ev_charger(&areas);
    extract_house(&areas);
}
