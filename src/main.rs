use helper::{
    extract_delivery_point_guid, extract_max_storage, extract_name, extract_relative_location,
    extract_storage_configs, get_obj_path, map_demand_configs, map_production_configs,
};
use output_type::DeliveryPoint;
use std::fs::{self, File};
use std::io::BufReader;
use std::num::NonZeroI64;
use std::time::Instant;
use ue_type::UObject;

mod deserialize_zero_as_none;
mod helper;
mod output_type;
mod ue_type;

fn main() {
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

            let (guid, guid_short) = extract_delivery_point_guid(world_obj);
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
                guid,
                guid_short,
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
        fs::write("./out.json", r).unwrap();
    }
}
