use crate::deserialize_zero_as_none::deserialize_zero_as_none;
use serde_derive::Deserialize;
use serde_derive::Serialize;
use serde_json::Value;
use serde_with::skip_serializing_none;
use std::num::NonZeroI64;

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct UObject {
    #[serde(rename = "Type")]
    pub type_field: String,
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "Outer")]
    pub outer: Option<String>,
    #[serde(rename = "Class")]
    pub class: String,
    #[serde(rename = "Template")]
    pub template: Option<ObjectPath>,
    #[serde(rename = "Flags")]
    pub flags: String,
    #[serde(rename = "Properties")]
    pub properties: Option<Properties>,
    #[serde(rename = "PersistentLevel")]
    pub persistent_level: Option<ObjectPath>,
    #[serde(rename = "ExtraReferencedObjects")]
    #[serde(default)]
    pub extra_referenced_objects: Vec<Value>,
    #[serde(rename = "StreamingLevels")]
    #[serde(default)]
    pub streaming_levels: Vec<Value>,
    #[serde(rename = "bStaticLightingBuiltGUID")]
    pub b_static_lighting_built_guid: Option<String>,
    #[serde(rename = "RelativeRotation")]
    pub relative_rotation: Option<RelativeRotation>,
    #[serde(rename = "RelativeLocation")]
    pub relative_location: Option<Vector3>,
    #[serde(rename = "IntensityNits")]
    pub intensity_nits: Option<f64>,
    #[serde(rename = "Bounds")]
    pub bounds: Option<CacheMeshExtendedBounds>,
    #[serde(rename = "Vectors")]
    #[serde(default)]
    pub vectors: Vec<Vector3>,
    #[serde(rename = "Points")]
    #[serde(default)]
    pub points: Vec<Vector3>,
    #[serde(rename = "Nodes")]
    #[serde(default)]
    pub nodes: Vec<Nodes2>,
    #[serde(rename = "Surfs")]
    #[serde(default)]
    pub surfs: Vec<Surf>,
    #[serde(rename = "NumSharedSides")]
    pub num_shared_sides: Option<i64>,
    #[serde(rename = "VertexBuffer")]
    pub vertex_buffer: Option<VertexBuffer>,
    #[serde(rename = "LightingGuid")]
    pub lighting_guid: Option<String>,
    #[serde(rename = "LightmassSettings")]
    #[serde(default)]
    pub lightmass_settings: Vec<LightmassSetting>,
    #[serde(rename = "LoadedMaterialResources")]
    #[serde(default)]
    pub loaded_material_resources: Vec<Value>,
    #[serde(rename = "CachedData")]
    pub cached_data: Option<CachedData>,
    #[serde(rename = "Actors")]
    #[serde(default)]
    pub actors: Vec<Option<ObjectPath>>,
    #[serde(rename = "URL")]
    pub url: Option<Url>,
    #[serde(rename = "Model")]
    pub model: Option<ObjectPath>,
    #[serde(rename = "ModelComponents")]
    #[serde(default)]
    pub model_components: Vec<ObjectPath>,
    #[serde(rename = "LevelScriptActor")]
    pub level_script_actor: Option<ObjectPath>,
    #[serde(rename = "NavListStart")]
    pub nav_list_start: Option<Value>,
    #[serde(rename = "NavListEnd")]
    pub nav_list_end: Option<Value>,
    #[serde(rename = "PrecomputedVisibilityHandler")]
    pub precomputed_visibility_handler: Option<PrecomputedVisibilityHandler>,
    #[serde(rename = "PrecomputedVolumeDistanceField")]
    pub precomputed_volume_distance_field: Option<PrecomputedVolumeDistanceField>,
    #[serde(rename = "PerInstanceSMData")]
    #[serde(default)]
    pub per_instance_smdata: Vec<PerInstanceSmdata>,
    #[serde(rename = "ClusterTree")]
    #[serde(default)]
    pub cluster_tree: Vec<ClusterTree>,
    #[serde(rename = "SuperStruct")]
    pub super_struct: Option<ObjectPath>,
    #[serde(rename = "ChildProperties")]
    #[serde(default)]
    pub child_properties: Vec<ChildProperty>,
    #[serde(rename = "FunctionFlags")]
    pub function_flags: Option<String>,
    #[serde(rename = "BodySetupGuid")]
    pub body_setup_guid: Option<String>,
    #[serde(rename = "CookedFormatData")]
    pub cooked_format_data: Option<CookedFormatData>,
    #[serde(rename = "Children")]
    pub children: Option<Vec<ObjectPath>>,
    #[serde(rename = "FuncMap")]
    pub func_map: Option<FuncMap>,
    #[serde(rename = "ClassFlags")]
    pub class_flags: Option<String>,
    #[serde(rename = "ClassWithin")]
    pub class_within: Option<ObjectPath>,
    #[serde(rename = "ClassConfigName")]
    pub class_config_name: Option<String>,
    #[serde(rename = "bCooked")]
    pub b_cooked: Option<bool>,
    #[serde(rename = "ClassDefaultObject")]
    pub class_default_object: Option<ObjectPath>,
    #[serde(rename = "EditorTags")]
    pub editor_tags: Option<EditorTags>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ObjectPath {
    #[serde(rename = "ObjectName")]
    pub object_name: String,
    #[serde(rename = "ObjectPath")]
    pub object_path: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Properties {
    #[serde(rename = "ActiveGameplayCues")]
    pub active_gameplay_cues: Option<ActiveGameplayCues>,
    #[serde(rename = "MinimalReplicationGameplayCues")]
    pub minimal_replication_gameplay_cues: Option<ActiveGameplayCues>,
    #[serde(rename = "ChildActor")]
    pub child_actor: Option<ObjectPath>,
    #[serde(rename = "MTSpawnVehicleList")]
    pub mtspawn_vehicle_list: Option<ObjectPath>,
    #[serde(rename = "InteractionCube")]
    pub interaction_cube: Option<ObjectPath>,
    #[serde(rename = "Scene")]
    pub scene: Option<ObjectPath>,
    #[serde(rename = "MTInteractable")]
    pub mtinteractable: Option<ObjectPath>,
    #[serde(rename = "RootComponent")]
    pub root_component: Option<ObjectPath>,
    #[serde(rename = "BlueprintCreatedComponents")]
    #[serde(default)]
    pub blueprint_created_components: Vec<Option<ObjectPath>>,
    #[serde(rename = "WidgetComponent")]
    pub widget_component: Option<ObjectPath>,
    #[serde(rename = "ParentComponent")]
    pub parent_component: Option<ObjectPath>,
    #[serde(rename = "OnDestroyed")]
    pub on_destroyed: Option<OnDestroyed>,
    #[serde(rename = "SceneComponent")]
    pub scene_component: Option<ObjectPath>,
    #[serde(rename = "bForceNoPrecomputedLighting")]
    pub b_force_no_precomputed_lighting: Option<bool>,
    #[serde(rename = "NavigationSystemConfig")]
    pub navigation_system_config: Option<ObjectPath>,
    #[serde(rename = "WorldPartition")]
    pub world_partition: Option<ObjectPath>,
    #[serde(rename = "DefaultGameMode")]
    pub default_game_mode: Option<ObjectPath>,
    #[serde(rename = "BookmarkArray")]
    pub bookmark_array: Option<Vec<Option<ObjectPath>>>,
    #[serde(rename = "UseAlignedGridLevels")]
    pub use_aligned_grid_levels: Option<String>,
    #[serde(rename = "SnapNonAlignedGridLevelsToLowerLevels")]
    pub snap_non_aligned_grid_levels_to_lower_levels: Option<String>,
    #[serde(rename = "PlacePartitionActorsUsingLocation")]
    pub place_partition_actors_using_location: Option<String>,
    #[serde(rename = "StreamingGrids")]
    pub streaming_grids: Option<Vec<StreamingGrid>>,
    #[serde(rename = "LevelStreaming")]
    pub level_streaming: Option<ObjectPath>,
    #[serde(rename = "CellGuid")]
    pub cell_guid: Option<String>,
    #[serde(rename = "RuntimeCellData")]
    pub runtime_cell_data: Option<ObjectPath>,
    #[serde(rename = "DataLayers")]
    #[serde(default)]
    pub data_layers: Vec<String>,
    #[serde(rename = "bIsAlwaysLoaded")]
    pub b_is_always_loaded: Option<bool>,
    #[serde(rename = "Position")]
    pub position: Option<Vector3>,
    #[serde(rename = "Extent")]
    pub extent: Option<f64>,
    #[serde(rename = "GridName")]
    pub grid_name: Option<String>,
    #[serde(rename = "ContentBounds")]
    pub content_bounds: Option<WorldBounds>,
    #[serde(rename = "Level")]
    pub level: Option<i64>,
    #[serde(rename = "SourceWorldAssetPath")]
    pub source_world_asset_path: Option<SourceWorldAssetPath>,
    #[serde(rename = "SubObjectsToCellRemapping")]
    #[serde(default)]
    pub sub_objects_to_cell_remapping: Vec<SubObjectsToCellRemapping>,
    #[serde(rename = "StreamingCell")]
    pub streaming_cell: Option<ObjectPath>,
    #[serde(rename = "OuterWorldPartition")]
    pub outer_world_partition: Option<ObjectPath>,
    #[serde(rename = "WorldAsset")]
    pub world_asset: Option<WorldAsset>,
    #[serde(rename = "PackageNameToLoad")]
    pub package_name_to_load: Option<String>,
    #[serde(rename = "RuntimeHash")]
    pub runtime_hash: Option<ObjectPath>,
    #[serde(rename = "DataLayerInstances")]
    pub data_layer_instances: Option<Vec<ObjectPath>>,
    #[serde(rename = "ContentBundleManager")]
    pub content_bundle_manager: Option<ObjectPath>,
    #[serde(rename = "BodyInstance")]
    pub body_instance: Option<BodyInstance>,
    #[serde(rename = "AttachParent")]
    pub attach_parent: Option<ObjectPath>,
    #[serde(rename = "RenderTargetResolution")]
    pub render_target_resolution: Option<Vector2>,
    #[serde(rename = "WaterMesh")]
    pub water_mesh: Option<ObjectPath>,
    #[serde(rename = "ZoneExtent")]
    pub zone_extent: Option<Vector2>,
    #[serde(rename = "FarDistanceMaterial")]
    pub far_distance_material: Option<ObjectPath>,
    #[serde(rename = "FarDistanceMeshExtent")]
    pub far_distance_mesh_extent: Option<f64>,
    #[serde(rename = "ExtentInTiles")]
    pub extent_in_tiles: Option<Vector2>,
    #[serde(rename = "CachedMaxDrawDistance")]
    pub cached_max_draw_distance: Option<f64>,
    #[serde(rename = "RelativeLocation")]
    pub relative_location: Option<Vector3>,
    #[serde(rename = "RelativeScale3D")]
    pub relative_scale3d: Option<Vector3>,
    #[serde(rename = "MotorTownInteractable")]
    pub motor_town_interactable: Option<ObjectPath>,
    #[serde(rename = "DeliveryPointGuid")]
    pub delivery_point_guid: Option<String>,
    #[serde(rename = "MissionPointName")]
    pub mission_point_name: Option<MissionPointName>,
    #[serde(rename = "MaxDeliveries")]
    pub max_deliveries: Option<i64>,
    #[serde(rename = "DemandConfigs")]
    #[serde(default)]
    pub demand_configs: Vec<DemandConfig>,
    #[serde(rename = "PaymentMultiplier")]
    pub payment_multiplier: Option<f64>,
    #[serde(rename = "MaxPassiveDeliveries")]
    pub max_passive_deliveries: Option<i64>,
    #[serde(rename = "MTMapIconPlaceName")]
    pub mtmap_icon_place_name: Option<ObjectPath>,
    #[serde(rename = "MTVendor")]
    pub mtvendor: Option<ObjectPath>,
    #[serde(rename = "MTWorldPartitionHider")]
    pub mtworld_partition_hider: Option<ObjectPath>,
    #[serde(rename = "NavigationInvoker")]
    pub navigation_invoker: Option<ObjectPath>,
    #[serde(rename = "AbilityComponent")]
    pub ability_component: Option<ObjectPath>,
    #[serde(rename = "CameraBoom")]
    pub camera_boom: Option<ObjectPath>,
    #[serde(rename = "FollowCamera")]
    pub follow_camera: Option<ObjectPath>,
    #[serde(rename = "Mesh")]
    pub mesh: Option<ObjectPath>,
    #[serde(rename = "CharacterMovement")]
    pub character_movement: Option<ObjectPath>,
    #[serde(rename = "CapsuleComponent")]
    pub capsule_component: Option<ObjectPath>,
    #[serde(rename = "SpawnCollisionHandlingMethod")]
    pub spawn_collision_handling_method: Option<String>,
    #[serde(rename = "MTDialogue")]
    pub mtdialogue: Option<ObjectPath>,
    #[serde(rename = "MapIconName")]
    pub map_icon_name: Option<MapIconName>,
    #[serde(rename = "Sphere")]
    pub sphere: Option<ObjectPath>,
    #[serde(rename = "BaseTranslationOffset")]
    pub base_translation_offset: Option<Vector3>,
    #[serde(rename = "BaseRotationOffset")]
    pub base_rotation_offset: Option<BaseRotationOffset>,
    #[serde(rename = "InstanceComponents")]
    #[serde(default)]
    pub instance_components: Vec<Option<ObjectPath>>,
    #[serde(rename = "BoxComponent")]
    pub box_component: Option<ObjectPath>,
    #[serde(rename = "ParkingSpaceType")]
    pub parking_space_type: Option<String>,
    #[serde(rename = "VehicleDeliveryVehicleQuery")]
    pub vehicle_delivery_vehicle_query: Option<VehicleDeliveryVehicleQuery>,
    #[serde(rename = "VehicleDeliveryVehicleTypes")]
    pub vehicle_delivery_vehicle_types: Option<Vec<String>>,
    #[serde(rename = "VehicleDeliveryVehicleTypeFlags")]
    pub vehicle_delivery_vehicle_type_flags: Option<i64>,
    #[serde(rename = "MTDeliveryPointInventoryRatio")]
    pub mtdelivery_point_inventory_ratio: Option<ObjectPath>,
    #[serde(rename = "VehicleParams")]
    #[serde(default)]
    pub vehicle_params: Vec<VehicleParam>,
    #[serde(rename = "EditorVisualVehicleClass")]
    pub editor_visual_vehicle_class: Option<ObjectPath>,
    #[serde(rename = "TriggerSphere")]
    pub trigger_sphere: Option<ObjectPath>,
    #[serde(rename = "StaticMeshComponent")]
    pub static_mesh_component: Option<ObjectPath>,
    #[serde(rename = "Tags")]
    #[serde(default)]
    pub tags: Vec<String>,
    #[serde(rename = "SM_Prop_Dumptser_03_Lid_02")]
    pub sm_prop_dumptser_03_lid_02: Option<ObjectPath>,
    #[serde(rename = "SM_Prop_Dumptser_03_Lid_01")]
    pub sm_prop_dumptser_03_lid_01: Option<ObjectPath>,
    #[serde(rename = "StaticMesh")]
    pub static_mesh: Option<ObjectPath>,
    #[serde(rename = "SpawnerGuid")]
    pub spawner_guid: Option<String>,
    #[serde(rename = "SM_Prop_RubbishBin_02_Lid")]
    pub sm_prop_rubbish_bin_02_lid: Option<ObjectPath>,
    #[serde(rename = "SM_Prop_TrashBag_02")]
    pub sm_prop_trash_bag_02: Option<Value>,
    #[serde(rename = "Stop")]
    pub stop: Option<ObjectPath>,
    #[serde(rename = "Line_White2")]
    pub line_white2: Option<ObjectPath>,
    #[serde(rename = "Line_White1")]
    pub line_white1: Option<ObjectPath>,
    #[serde(rename = "VehicleDeliveryTruckClasses")]
    #[serde(default)]
    pub vehicle_delivery_truck_classes: Vec<String>,
    #[serde(rename = "PointName")]
    pub point_name: Option<PointName>,
    #[serde(rename = "InputInventoryShare")]
    #[serde(default)]
    pub input_inventory_share: Vec<ObjectPath>,
    #[serde(rename = "UCSSerializationIndex")]
    pub ucsserialization_index: Option<i64>,
    #[serde(rename = "bNetAddressable")]
    pub b_net_addressable: Option<bool>,
    #[serde(rename = "CreationMethod")]
    pub creation_method: Option<String>,
    #[serde(rename = "bEvaluateWorldPositionOffsetInRayTracing")]
    pub b_evaluate_world_position_offset_in_ray_tracing: Option<bool>,
    #[serde(rename = "OverrideMaterials")]
    #[serde(default)]
    pub override_materials: Vec<Option<ObjectPath>>,
    #[serde(rename = "CastShadow")]
    pub cast_shadow: Option<bool>,
    #[serde(rename = "Mobility")]
    pub mobility: Option<String>,
    #[serde(rename = "RelativeRotation")]
    pub relative_rotation: Option<RelativeRotation>,
    #[serde(rename = "LDMaxDrawDistance")]
    pub ldmax_draw_distance: Option<f64>,
    #[serde(rename = "bAllowCullDistanceVolume")]
    pub b_allow_cull_distance_volume: Option<bool>,
    #[serde(rename = "bUseDefaultCollision")]
    pub b_use_default_collision: Option<bool>,
    #[serde(rename = "CanCharacterStepUpOn")]
    pub can_character_step_up_on: Option<String>,
    #[serde(rename = "bHasNoStreamableTextures")]
    pub b_has_no_streamable_textures: Option<bool>,
    #[serde(rename = "bCanEverAffectNavigation")]
    pub b_can_ever_affect_navigation: Option<bool>,
    #[serde(rename = "AssetUserData")]
    #[serde(default)]
    pub asset_user_data: Vec<ObjectPath>,
    #[serde(rename = "bVisible")]
    pub b_visible: Option<bool>,
    #[serde(rename = "bHiddenInGame")]
    pub b_hidden_in_game: Option<bool>,
    #[serde(rename = "bHidden")]
    pub b_hidden: Option<bool>,
    #[serde(rename = "bRelevantForLevelBounds")]
    pub b_relevant_for_level_bounds: Option<bool>,
    #[serde(rename = "SplineParams")]
    pub spline_params: Option<SplineParams>,
    #[serde(rename = "CachedMeshBodySetupGuid")]
    pub cached_mesh_body_setup_guid: Option<String>,
    #[serde(rename = "BodySetup")]
    pub body_setup: Option<ObjectPath>,
    #[serde(rename = "bNeverNeedsCookedCollisionData")]
    pub b_never_needs_cooked_collision_data: Option<bool>,
    #[serde(rename = "ForwardAxis")]
    pub forward_axis: Option<String>,
    #[serde(rename = "SplineCurves")]
    pub spline_curves: Option<SplineCurves>,
    #[serde(rename = "bSplineHasBeenEdited")]
    pub b_spline_has_been_edited: Option<bool>,
    #[serde(rename = "bClosedLoop")]
    pub b_closed_loop: Option<bool>,
    #[serde(rename = "ComponentTags")]
    #[serde(default)]
    pub component_tags: Vec<String>,
    #[serde(rename = "SphereRadius")]
    pub sphere_radius: Option<f64>,
    #[serde(rename = "PrimaryComponentTick")]
    pub primary_component_tick: Option<PrimaryComponentTick>,
    #[serde(rename = "SkeletalMesh")]
    pub skeletal_mesh: Option<ObjectPath>,
    #[serde(rename = "MTComponentLOD")]
    pub mtcomponent_lod: Option<ObjectPath>,
    #[serde(rename = "Capsule")]
    pub capsule: Option<ObjectPath>,
    #[serde(rename = "LightGuid")]
    pub light_guid: Option<String>,
    #[serde(rename = "NavMeshResolutionParams")]
    pub nav_mesh_resolution_params: Option<NavMeshResolutionParams>,
    #[serde(rename = "NavMeshResolutionParams[1]")]
    pub nav_mesh_resolution_params_1: Option<NavMeshResolutionParams>,
    #[serde(rename = "NavMeshResolutionParams[2]")]
    pub nav_mesh_resolution_params_2: Option<NavMeshResolutionParams>,
    #[serde(rename = "AgentRadius")]
    pub agent_radius: Option<f64>,
    #[serde(rename = "AgentHeight")]
    pub agent_height: Option<f64>,
    #[serde(rename = "AgentMaxStepHeight")]
    pub agent_max_step_height: Option<f64>,
    #[serde(rename = "NavDataConfig")]
    pub nav_data_config: Option<NavDataConfig>,
    #[serde(rename = "SupportedAreas")]
    #[serde(default)]
    pub supported_areas: Vec<SupportedAreas>,
    #[serde(rename = "bNetLoadOnClient")]
    pub b_net_load_on_client: Option<bool>,
    #[serde(rename = "DefaultSceneRoot")]
    pub default_scene_root: Option<ObjectPath>,
    #[serde(rename = "Box")]
    pub box_field: Option<ObjectPath>,
    #[serde(rename = "CourseName")]
    pub course_name: Option<String>,
    #[serde(rename = "RaceSectionIndex")]
    pub race_section_index: Option<i64>,
    #[serde(rename = "ProceduralComponent")]
    pub procedural_component: Option<ObjectPath>,
    #[serde(rename = "BrushType")]
    pub brush_type: Option<String>,
    #[serde(rename = "Brush")]
    pub brush: Option<ObjectPath>,
    #[serde(rename = "BrushComponent")]
    pub brush_component: Option<ObjectPath>,
    #[serde(rename = "SavedSelections")]
    #[serde(default)]
    pub saved_selections: Vec<SavedSelection>,
    #[serde(rename = "FoliageSpawner")]
    pub foliage_spawner: Option<ObjectPath>,
    #[serde(rename = "SpawningVolume")]
    pub spawning_volume: Option<ObjectPath>,
    #[serde(rename = "ProceduralGuid")]
    pub procedural_guid: Option<String>,
    #[serde(rename = "ItemId")]
    pub item_id: Option<String>,
    #[serde(rename = "Settings")]
    pub settings: Option<Settings>,
    #[serde(rename = "POIGuid")]
    pub poiguid: Option<String>,
    #[serde(rename = "AreaFlags")]
    pub area_flags: Option<i64>,
    #[serde(rename = "GameplayTagContainer")]
    #[serde(default)]
    pub gameplay_tag_container: Vec<String>,
    #[serde(rename = "TaxiPaymentMultiplier")]
    pub taxi_payment_multiplier: Option<f64>,
    #[serde(rename = "GoodSprite")]
    pub good_sprite: Option<Value>,
    #[serde(rename = "BadSprite")]
    pub bad_sprite: Option<Value>,
    #[serde(rename = "PlayerStartTag")]
    pub player_start_tag: Option<String>,
    #[serde(rename = "Camera")]
    pub camera: Option<ObjectPath>,
    #[serde(rename = "LandscapeCacheObject")]
    pub landscape_cache_object: Option<ObjectPath>,
    #[serde(rename = "GridGuids")]
    #[serde(default)]
    pub grid_guids: Vec<SubObjectsToCellRemapping>,
    #[serde(rename = "bHasDescriptor")]
    pub b_has_descriptor: Option<bool>,
    #[serde(rename = "Descriptor")]
    pub descriptor: Option<Descriptor>,
    #[serde(rename = "bHasRootLocation")]
    pub b_has_root_location: Option<bool>,
    #[serde(rename = "RootLocation")]
    pub root_location: Option<Vector3>,
    #[serde(rename = "GeneratedComponent")]
    pub generated_component: Option<WorldAsset>,
    #[serde(rename = "Crc")]
    pub crc: Option<Crc>,
    #[serde(rename = "GeneratedActors")]
    #[serde(default)]
    pub generated_actors: Vec<WorldAsset>,
    #[serde(rename = "CachedLayerNames")]
    #[serde(default)]
    pub cached_layer_names: Vec<String>,
    #[serde(rename = "ParametersOverrides")]
    pub parameters_overrides: Option<ParametersOverrides>,
    #[serde(rename = "Graph")]
    pub graph: Option<ObjectPath>,
    #[serde(rename = "bGenerated")]
    pub b_generated: Option<bool>,
    #[serde(rename = "GraphInstance")]
    pub graph_instance: Option<ObjectPath>,
    #[serde(rename = "GeneratedResources")]
    #[serde(default)]
    pub generated_resources: Vec<ObjectPath>,
    #[serde(rename = "LastGeneratedBounds")]
    pub last_generated_bounds: Option<WorldBounds>,
    #[serde(rename = "LandscapeTagsHasAny")]
    #[serde(default)]
    pub landscape_tags_has_any: Vec<String>,
    #[serde(rename = "DeliveryPointName")]
    pub delivery_point_name: Option<DeliveryPointName>,
    #[serde(rename = "Asset")]
    pub asset: Option<ObjectPath>,
    #[serde(rename = "OverrideParameters")]
    pub override_parameters: Option<OverrideParameters>,
    #[serde(rename = "OnSystemFinished")]
    pub on_system_finished: Option<OnDestroyed>,
    #[serde(rename = "NiagaraComponent")]
    pub niagara_component: Option<ObjectPath>,
    #[serde(rename = "bCreateOnClient")]
    pub b_create_on_client: Option<bool>,
    #[serde(rename = "bAutoSpawnMissingNavData")]
    pub b_auto_spawn_missing_nav_data: Option<bool>,
    #[serde(rename = "bSpawnNavDataInNavBoundsLevel")]
    pub b_spawn_nav_data_in_nav_bounds_level: Option<bool>,
    #[serde(rename = "NavigationSystemClass")]
    pub navigation_system_class: Option<WorldAsset>,
    #[serde(rename = "VehicleClasses")]
    #[serde(default)]
    pub vehicle_classes: Vec<Option<ObjectPath>>,
    #[serde(rename = "WeatherConfig")]
    pub weather_config: Option<WeatherConfig>,
    #[serde(rename = "SkyAtmosphere")]
    pub sky_atmosphere: Option<ObjectPath>,
    #[serde(rename = "CompassMesh")]
    pub compass_mesh: Option<Value>,
    #[serde(rename = "PostProcess_Old")]
    pub post_process_old: Option<ObjectPath>,
    #[serde(rename = "RootSceneComponent")]
    pub root_scene_component: Option<ObjectPath>,
    #[serde(rename = "DirectionalLight_Sun2")]
    pub directional_light_sun2: Option<ObjectPath>,
    #[serde(rename = "DirectionalLight_Moon2")]
    pub directional_light_moon2: Option<ObjectPath>,
    #[serde(rename = "SkyLight2")]
    pub sky_light2: Option<ObjectPath>,
    #[serde(rename = "PostProcess")]
    pub post_process: Option<ObjectPath>,
    #[serde(rename = "VechileKeys")]
    #[serde(default)]
    pub vechile_keys: Vec<String>,
    #[serde(rename = "Objects")]
    #[serde(default)]
    pub objects: Vec<Object>,
    #[serde(rename = "Segments")]
    #[serde(default)]
    pub segments: Vec<Segment>,
    #[serde(rename = "OceanConfig")]
    pub ocean_config: Option<OceanConfig>,
    #[serde(rename = "PlaceName")]
    pub place_name: Option<MapIconName>,
    #[serde(rename = "InteractableType")]
    pub interactable_type: Option<String>,
    #[serde(rename = "bInteractbyActor")]
    pub b_interactby_actor: Option<bool>,
    #[serde(rename = "Config")]
    pub config: Option<Config>,
    #[serde(rename = "VehicleClass")]
    pub vehicle_class: Option<ObjectPath>,
    #[serde(rename = "OnUpdate")]
    pub on_update: Option<OnDestroyed>,
    #[serde(rename = "WalkableFloorZ")]
    pub walkable_floor_z: Option<f64>,
    #[serde(rename = "NavAgentProps")]
    pub nav_agent_props: Option<NavAgentProps>,
    #[serde(rename = "Routes")]
    pub routes: Option<Vec<Routes>>,
    #[serde(rename = "AreaName")]
    pub area_name: Option<MapIconName>,
    #[serde(rename = "AreaVolumeFlags")]
    #[serde(default)]
    pub area_volume_flags: Vec<String>,
    #[serde(rename = "TopViewLines")]
    #[serde(default)]
    pub top_view_lines: Vec<Vector3>,
    #[serde(rename = "ZoneKey")]
    pub zone_key: Option<String>,
    #[serde(rename = "ZoneColor")]
    pub zone_color: Option<DebugColor>,
    #[serde(rename = "bInManipulation")]
    pub b_in_manipulation: Option<bool>,
    #[serde(rename = "AreaNameTexts")]
    pub area_name_texts: Option<AreaNameTexts>,
    #[serde(rename = "SpawnSettings")]
    pub spawn_settings: Option<Vec<SpawnSetting>>,
    #[serde(rename = "NumResidents")]
    pub num_residents: Option<i64>,
    #[serde(rename = "NumHitchhikers")]
    pub num_hitchhikers: Option<i64>,
    #[serde(rename = "NumTaxiPassengers")]
    pub num_taxi_passengers: Option<i64>,
    #[serde(rename = "NumAmbulancePassengers")]
    pub num_ambulance_passengers: Option<i64>,
    #[serde(rename = "RoadType")]
    pub road_type: Option<String>,
    #[serde(rename = "CourseKey")]
    pub course_key: Option<String>,
    #[serde(rename = "bCopyFromLandscape")]
    pub b_copy_from_landscape: Option<bool>,
    #[serde(rename = "CopyFromLandscapeWidthMultiplier")]
    pub copy_from_landscape_width_multiplier: Option<f64>,
    #[serde(rename = "Spline")]
    pub spline: Option<ObjectPath>,
    #[serde(rename = "SplineBounds")]
    pub spline_bounds: Option<WorldBounds>,
    #[serde(rename = "LaneWidth")]
    pub lane_width: Option<f64>,
    #[serde(rename = "SpeedLimitKPH")]
    pub speed_limit_kph: Option<f64>,
    #[serde(rename = "NumForwardLanes")]
    pub num_forward_lanes: Option<i64>,
    #[serde(rename = "NumBackwardLanes")]
    pub num_backward_lanes: Option<i64>,
    #[serde(rename = "RoadFlags")]
    pub road_flags: Option<i64>,
    #[serde(rename = "ExcludeConnectionRoads")]
    #[serde(default)]
    pub exclude_connection_roads: Vec<ObjectPath>,
    #[serde(rename = "MaxRoadSideTowDistance")]
    pub max_road_side_tow_distance: Option<f64>,
    #[serde(rename = "GraphData")]
    pub graph_data: Option<GraphData>,
    #[serde(rename = "ModelBodySetup")]
    pub model_body_setup: Option<ObjectPath>,
    #[serde(rename = "MaxStorage")]
    #[serde(default, deserialize_with = "deserialize_zero_as_none")]
    pub max_storage: Option<NonZeroI64>,
    #[serde(rename = "ProductionConfigs")]
    #[serde(default)]
    pub production_configs: Vec<ProductionConfig>,
    #[serde(rename = "Configs")]
    #[serde(default)]
    pub configs: Vec<Config2>,
    #[serde(rename = "Parent")]
    pub parent: Option<ObjectPath>,
    #[serde(rename = "ScalarParameterValues")]
    #[serde(default)]
    pub scalar_parameter_values: Vec<ScalarParameterValues>,
    #[serde(rename = "BasePropertyOverrides")]
    pub base_property_overrides: Option<BasePropertyOverrides>,
    #[serde(rename = "bIncludedInBaseGame")]
    pub b_included_in_base_game: Option<bool>,
    #[serde(rename = "Demands")]
    #[serde(default)]
    pub demands: Vec<Value>,
    #[serde(rename = "MaxDeliveryDistance")]
    pub max_delivery_distance: Option<f64>,
    #[serde(rename = "DemandPriority")]
    pub demand_priority: Option<i64>,
    #[serde(rename = "bRemoveUnusedInputCargo")]
    pub b_remove_unused_input_cargo: Option<bool>,
    #[serde(rename = "SM_Bld_Silo_Small_01")]
    pub sm_bld_silo_small_01: Option<ObjectPath>,
    #[serde(rename = "Model")]
    pub model: Option<ObjectPath>,
    #[serde(rename = "ModelComponents")]
    #[serde(default)]
    pub model_components: Vec<ObjectPath>,
    #[serde(rename = "LevelScriptActor")]
    pub level_script_actor: Option<ObjectPath>,
    #[serde(rename = "StaticNavigableGeometry")]
    #[serde(default)]
    pub static_navigable_geometry: Vec<Vector3>,
    #[serde(rename = "LevelBuildDataId")]
    pub level_build_data_id: Option<String>,
    #[serde(rename = "bIsPartitioned")]
    pub b_is_partitioned: Option<bool>,
    #[serde(rename = "WorldSettings")]
    pub world_settings: Option<ObjectPath>,
    #[serde(rename = "WorldDataLayers")]
    pub world_data_layers: Option<ObjectPath>,
    #[serde(rename = "PostBoxLocatorComponent")]
    pub post_box_locator_component: Option<ObjectPath>,
    #[serde(rename = "AreaSize")]
    pub area_size: Option<Vector3>,
    #[serde(rename = "HousegKey")]
    pub houseg_key: Option<String>,
    #[serde(rename = "Connections")]
    pub connections: Option<Connections>,
    #[serde(rename = "Connections[1]")]
    pub connections_1: Option<Connections>,
    #[serde(rename = "SplineInfo")]
    pub spline_info: Option<SplineInfo>,
    #[serde(rename = "Points")]
    #[serde(default)]
    pub points: Vec<Point4>,
    #[serde(rename = "Bounds")]
    pub bounds: Option<WorldBounds>,
    #[serde(rename = "LocalMeshComponents")]
    #[serde(default)]
    pub local_mesh_components: Vec<ObjectPath>,
    #[serde(rename = "ControlPoints")]
    #[serde(default)]
    pub control_points: Vec<ObjectPath>,
    #[serde(rename = "Location")]
    pub location: Option<Vector3>,
    #[serde(rename = "Rotation")]
    pub rotation: Option<RelativeRotation>,
    #[serde(rename = "LeftSideLayerFalloffFactor")]
    pub left_side_layer_falloff_factor: Option<f64>,
    #[serde(rename = "RightSideLayerFalloffFactor")]
    pub right_side_layer_falloff_factor: Option<f64>,
    #[serde(rename = "EndFalloff")]
    pub end_falloff: Option<f64>,
    #[serde(rename = "ConnectedSegments")]
    #[serde(default)]
    pub connected_segments: Vec<ConnectedSegment>,
    #[serde(rename = "SideFalloff")]
    pub side_falloff: Option<f64>,
    #[serde(rename = "LeftSideFalloffFactor")]
    pub left_side_falloff_factor: Option<f64>,
    #[serde(rename = "RightSideFalloffFactor")]
    pub right_side_falloff_factor: Option<f64>,
    #[serde(rename = "Width")]
    pub width: Option<f64>,
    #[serde(rename = "LayerWidthRatio")]
    pub layer_width_ratio: Option<f64>,
    #[serde(rename = "LandscapeGuid")]
    pub landscape_guid: Option<String>,
    #[serde(rename = "UberGraphFrame")]
    pub uber_graph_frame: Option<OverrideParameters>,
    #[serde(rename = "PrimaryActorTick")]
    pub primary_actor_tick: Option<PrimaryActorTick>,
    #[serde(rename = "MaxDeliveryReceiveDistance")]
    pub max_delivery_receive_distance: Option<f64>,
    #[serde(rename = "SortedInstances")]
    #[serde(default)]
    pub sorted_instances: Vec<i64>,
    #[serde(rename = "NumBuiltInstances")]
    pub num_built_instances: Option<i64>,
    #[serde(rename = "BuiltInstanceBounds")]
    pub built_instance_bounds: Option<WorldBounds>,
    #[serde(rename = "CacheMeshExtendedBounds")]
    pub cache_mesh_extended_bounds: Option<CacheMeshExtendedBounds>,
    #[serde(rename = "InstanceCountToRender")]
    pub instance_count_to_render: Option<i64>,
    #[serde(rename = "InstancingRandomSeed")]
    pub instancing_random_seed: Option<i64>,
    #[serde(rename = "InstanceStartCullDistance")]
    pub instance_start_cull_distance: Option<i64>,
    #[serde(rename = "InstanceEndCullDistance")]
    pub instance_end_cull_distance: Option<i64>,
    #[serde(rename = "InstanceReorderTable")]
    #[serde(default)]
    pub instance_reorder_table: Vec<i64>,
    #[serde(rename = "bGenerateOverlapEvents")]
    pub b_generate_overlap_events: Option<bool>,
    #[serde(rename = "bAffectDistanceFieldLighting")]
    pub b_affect_distance_field_lighting: Option<bool>,
    #[serde(rename = "OcclusionLayerNumNodes")]
    pub occlusion_layer_num_nodes: Option<i64>,
    #[serde(rename = "bCastDynamicShadow")]
    pub b_cast_dynamic_shadow: Option<bool>,
    #[serde(rename = "bCastStaticShadow")]
    pub b_cast_static_shadow: Option<bool>,
    #[serde(rename = "MTCargoReceiver")]
    pub mtcargo_receiver: Option<ObjectPath>,
    #[serde(rename = "GarageFlags")]
    pub garage_flags: Option<i64>,
    #[serde(rename = "MTFuelPump")]
    pub mtfuel_pump: Option<ObjectPath>,
    #[serde(rename = "InteractionCube1")]
    pub interaction_cube1: Option<ObjectPath>,
    #[serde(rename = "SM_Prop_Gaspump_01")]
    pub sm_prop_gaspump_01: Option<ObjectPath>,
    #[serde(rename = "StorageConfigs")]
    pub storage_configs: Option<Vec<StorageConfig>>,
    #[serde(rename = "DestinationTypes")]
    #[serde(default)]
    pub destination_types: Vec<Value>,
    #[serde(rename = "SM_Prop_Lab_Funnel_01")]
    pub sm_prop_lab_funnel_01: Option<ObjectPath>,
    #[serde(rename = "FogHeightFalloff")]
    pub fog_height_falloff: Option<f64>,
    #[serde(rename = "StartDistance")]
    pub start_distance: Option<f64>,
    #[serde(rename = "bEnableVolumetricFog")]
    pub b_enable_volumetric_fog: Option<bool>,
    #[serde(rename = "VolumetricFogDistance")]
    pub volumetric_fog_distance: Option<f64>,
    #[serde(rename = "Component")]
    pub component: Option<ObjectPath>,
    #[serde(rename = "ForwardShadingPriority")]
    pub forward_shading_priority: Option<i64>,
    #[serde(rename = "Intensity")]
    pub intensity: Option<f64>,
    #[serde(rename = "Billboard")]
    pub billboard: Option<ObjectPath>,
    #[serde(rename = "DataLayerAsset")]
    pub data_layer_asset: Option<ObjectPath>,
    #[serde(rename = "InitialRuntimeState")]
    pub initial_runtime_state: Option<String>,
    #[serde(rename = "CullDistances")]
    pub cull_distances: Option<Vec<CullDistances>>,
    #[serde(rename = "bUseAsDestinationInteraction")]
    pub b_use_as_destination_interaction: Option<bool>,
    #[serde(rename = "RectLight")]
    pub rect_light: Option<ObjectPath>,
    #[serde(rename = "AreaClassOverride")]
    pub area_class_override: Option<ObjectPath>,
    #[serde(rename = "bUseSystemDefaultObstacleAreaClass")]
    pub b_use_system_default_obstacle_area_class: Option<bool>,
    #[serde(rename = "BusStopGuid")]
    pub bus_stop_guid: Option<String>,
    #[serde(rename = "BusStopDisplayName")]
    pub bus_stop_display_name: Option<BusStopDisplayName>,
    #[serde(rename = "BusStopFlags")]
    #[serde(default)]
    pub bus_stop_flags: Vec<String>,
    #[serde(rename = "ParkingMeshComponent")]
    pub parking_mesh_component: Option<ObjectPath>,
    #[serde(rename = "PassengerSpawnBoxComponent")]
    pub passenger_spawn_box_component: Option<ObjectPath>,
    #[serde(rename = "AIDestinationComponent")]
    pub aidestination_component: Option<ObjectPath>,
    #[serde(rename = "bUseAIDestination")]
    pub b_use_aidestination: Option<bool>,
    #[serde(rename = "InteractableComponent")]
    pub interactable_component: Option<ObjectPath>,
    #[serde(rename = "ChildActor1")]
    pub child_actor1: Option<ObjectPath>,
    #[serde(rename = "SM_Bld_Concrete_Floor_02")]
    pub sm_bld_concrete_floor_02: Option<ObjectPath>,
    #[serde(rename = "MTSeat1")]
    pub mtseat1: Option<ObjectPath>,
    #[serde(rename = "MTSeat")]
    pub mtseat: Option<ObjectPath>,
    #[serde(rename = "SM_Prop_BusStop_01_Glass")]
    pub sm_prop_bus_stop_01_glass: Option<ObjectPath>,
    #[serde(rename = "SM_Prop_BusStop_01")]
    pub sm_prop_bus_stop_01: Option<ObjectPath>,
    #[serde(rename = "BusStopName")]
    pub bus_stop_name: Option<PointName>,
    #[serde(rename = "BrushBodySetup")]
    pub brush_body_setup: Option<ObjectPath>,
    #[serde(rename = "PCG")]
    pub pcg: Option<ObjectPath>,
    #[serde(rename = "bEnableRock")]
    pub b_enable_rock: Option<bool>,
    #[serde(rename = "bEnableRoad")]
    pub b_enable_road: Option<bool>,
    #[serde(rename = "bEnableBush")]
    pub b_enable_bush: Option<bool>,
    #[serde(rename = "bEnableShrub")]
    pub b_enable_shrub: Option<bool>,
    #[serde(rename = "Point Spacing")]
    pub point_spacing: Option<f64>,
    #[serde(rename = "bEnableWaterPlant")]
    pub b_enable_water_plant: Option<bool>,
    #[serde(rename = "Road_Size")]
    pub road_size: Option<Vector3>,
    #[serde(rename = "Road_Width")]
    pub road_width: Option<f64>,
    #[serde(rename = "Road_SideSize")]
    pub road_side_size: Option<Vector3>,
    #[serde(rename = "Road_SideWidth")]
    pub road_side_width: Option<f64>,
    #[serde(rename = "Road_SideW")]
    pub road_side_w: Option<f64>,
    #[serde(rename = "Road_InnerSize")]
    pub road_inner_size: Option<Vector3>,
    #[serde(rename = "Road_SideSpacing")]
    pub road_side_spacing: Option<f64>,
    #[serde(rename = "Road_SideVariagion")]
    pub road_side_variagion: Option<f64>,
    #[serde(rename = "Bush_Density")]
    pub bush_density: Option<f64>,
    #[serde(rename = "Shrub_Density")]
    pub shrub_density: Option<f64>,
    #[serde(rename = "WaterPlant_Density")]
    pub water_plant_density: Option<f64>,
    #[serde(rename = "Road_Density")]
    pub road_density: Option<f64>,
    #[serde(rename = "BoxExtent")]
    pub box_extent: Option<Vector3>,
    #[serde(rename = "AggGeom")]
    pub agg_geom: Option<AggGeom>,
    #[serde(rename = "bGenerateMirroredCollision")]
    pub b_generate_mirrored_collision: Option<bool>,
    #[serde(rename = "CollisionTraceFlag")]
    pub collision_trace_flag: Option<String>,
    #[serde(rename = "bDoubleSidedGeometry")]
    pub b_double_sided_geometry: Option<bool>,
    #[serde(rename = "DefaultInstance")]
    pub default_instance: Option<DefaultInstance>,
    #[serde(rename = "PhysicsType")]
    pub physics_type: Option<String>,
    #[serde(rename = "PhysMaterial")]
    pub phys_material: Option<ObjectPath>,
    #[serde(rename = "UberGraphFunction")]
    pub uber_graph_function: Option<ObjectPath>,
    #[serde(rename = "AdditionalDestinations")]
    pub additional_destinations: Option<Vec<ObjectPath>>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ActiveGameplayCues {
    #[serde(rename = "Owner")]
    pub owner: ObjectPath,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OnDestroyed {
    #[serde(rename = "InvocationList")]
    pub invocation_list: Vec<InvocationList>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct InvocationList {
    #[serde(rename = "Object")]
    pub object: ObjectPath,
    #[serde(rename = "FunctionName")]
    pub function_name: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StreamingGrid {
    #[serde(rename = "GridName")]
    pub grid_name: String,
    #[serde(rename = "Origin")]
    pub origin: Vector3,
    #[serde(rename = "CellSize")]
    pub cell_size: i64,
    #[serde(rename = "LoadingRange")]
    pub loading_range: f64,
    #[serde(rename = "bBlockOnSlowStreaming")]
    pub b_block_on_slow_streaming: bool,
    #[serde(rename = "DebugColor")]
    pub debug_color: DebugColor,
    #[serde(rename = "GridLevels")]
    pub grid_levels: Vec<GridLevel>,
    #[serde(rename = "WorldBounds")]
    pub world_bounds: WorldBounds,
    #[serde(rename = "bClientOnlyVisible")]
    pub b_client_only_visible: bool,
    #[serde(rename = "HLODLayer")]
    pub hlodlayer: Value,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector3 {
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
    #[serde(rename = "Z")]
    pub z: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DebugColor {
    #[serde(rename = "R")]
    pub r: f64,
    #[serde(rename = "G")]
    pub g: f64,
    #[serde(rename = "B")]
    pub b: f64,
    #[serde(rename = "A")]
    pub a: f64,
    #[serde(rename = "Hex")]
    pub hex: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GridLevel {
    #[serde(rename = "LayerCells")]
    pub layer_cells: Vec<LayerCell>,
    #[serde(rename = "LayerCellsMapping")]
    pub layer_cells_mapping: Vec<CargoKeyAndAmountValue>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LayerCell {
    #[serde(rename = "GridCells")]
    pub grid_cells: Vec<ObjectPath>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CargoKeyAndAmountValue {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WorldBounds {
    #[serde(rename = "Min")]
    pub min: Vector3,
    #[serde(rename = "Max")]
    pub max: Vector3,
    #[serde(rename = "IsValid")]
    pub is_valid: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceWorldAssetPath {
    #[serde(rename = "PackageName")]
    pub package_name: String,
    #[serde(rename = "AssetName")]
    pub asset_name: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SubObjectsToCellRemapping {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WorldAsset {
    #[serde(rename = "AssetPathName")]
    pub asset_path_name: String,
    #[serde(rename = "SubPathString")]
    pub sub_path_string: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BodyInstance {
    #[serde(rename = "MaxAngularVelocity")]
    pub max_angular_velocity: Option<f64>,
    #[serde(rename = "bInterpolateWhenSubStepping")]
    pub b_interpolate_when_sub_stepping: Option<bool>,
    #[serde(rename = "CollisionEnabled")]
    pub collision_enabled: Option<String>,
    #[serde(rename = "CollisionProfileName")]
    pub collision_profile_name: Option<String>,
    #[serde(rename = "CollisionResponses")]
    pub collision_responses: Option<CollisionResponses>,
    #[serde(rename = "bAutoWeld")]
    pub b_auto_weld: Option<bool>,
    #[serde(rename = "ObjectType")]
    pub object_type: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CollisionResponses {
    #[serde(rename = "ResponseArray")]
    pub response_array: Vec<ResponseArray>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ResponseArray {
    #[serde(rename = "Channel")]
    pub channel: String,
    #[serde(rename = "Response")]
    pub response: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector2 {
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct MissionPointName {
    #[serde(rename = "TableId")]
    pub table_id: Option<String>,
    #[serde(rename = "Key")]
    pub key: Option<String>,
    #[serde(rename = "SourceString")]
    pub source_string: Option<String>,
    #[serde(rename = "LocalizedString")]
    pub localized_string: Option<String>,
    #[serde(rename = "CultureInvariantString")]
    pub culture_invariant_string: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DemandConfig {
    #[serde(rename = "CargoType")]
    pub cargo_type: String,
    #[serde(rename = "CargoKey")]
    pub cargo_key: String,
    #[serde(rename = "CargoGameplayTagQuery")]
    pub cargo_gameplay_tag_query: CargoGameplayTagQuery,
    #[serde(rename = "PaymentMultiplier")]
    pub payment_multiplier: f64,
    #[serde(rename = "MaxStorage")]
    #[serde(default, deserialize_with = "deserialize_zero_as_none")]
    pub max_storage: Option<NonZeroI64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CargoGameplayTagQuery {
    #[serde(rename = "TokenStreamVersion")]
    pub token_stream_version: i64,
    #[serde(rename = "TagDictionary")]
    pub tag_dictionary: Vec<Value>,
    #[serde(rename = "QueryTokenStream")]
    pub query_token_stream: Vec<Value>,
    #[serde(rename = "UserDescription")]
    pub user_description: String,
    #[serde(rename = "AutoDescription")]
    pub auto_description: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct MapIconName {
    #[serde(rename = "TableId")]
    pub table_id: String,
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "SourceString")]
    pub source_string: String,
    #[serde(rename = "LocalizedString")]
    pub localized_string: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct BaseRotationOffset {
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
    #[serde(rename = "Z")]
    pub z: f64,
    #[serde(rename = "W")]
    pub w: f64,
    #[serde(rename = "IsNormalized")]
    pub is_normalized: bool,
    #[serde(rename = "Size")]
    pub size: f64,
    #[serde(rename = "SizeSquared")]
    pub size_squared: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct VehicleDeliveryVehicleQuery {
    #[serde(rename = "TagDictionary")]
    pub tag_dictionary: Vec<TagDictionary>,
    #[serde(rename = "QueryTokenStream")]
    pub query_token_stream: Vec<i64>,
    #[serde(rename = "AutoDescription")]
    pub auto_description: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TagDictionary {
    #[serde(rename = "TagName")]
    pub tag_name: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct VehicleParam {
    #[serde(rename = "VehicleKey")]
    pub vehicle_key: String,
    #[serde(rename = "Customizations")]
    pub customizations: Vec<Customization>,
    #[serde(rename = "Parts")]
    pub parts: Vec<Part>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Customization {
    #[serde(rename = "BodyMaterialIndex")]
    pub body_material_index: i64,
    #[serde(rename = "BodyColors")]
    pub body_colors: Vec<BodyColor>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BodyColor {
    #[serde(rename = "MaterialSlotName")]
    pub material_slot_name: String,
    #[serde(rename = "Color")]
    pub color: Color,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Color {
    #[serde(rename = "B")]
    pub b: i64,
    #[serde(rename = "G")]
    pub g: i64,
    #[serde(rename = "R")]
    pub r: i64,
    #[serde(rename = "A")]
    pub a: i64,
    #[serde(rename = "Hex")]
    pub hex: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Part {
    #[serde(rename = "Slot")]
    pub slot: String,
    #[serde(rename = "PartKey")]
    pub part_key: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PointName {
    #[serde(rename = "Texts")]
    pub texts: Vec<Text>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Text {
    #[serde(rename = "TableId")]
    pub table_id: Option<String>,
    #[serde(rename = "Key")]
    pub key: Option<String>,
    #[serde(rename = "SourceString")]
    pub source_string: Option<String>,
    #[serde(rename = "LocalizedString")]
    pub localized_string: Option<String>,
    #[serde(rename = "CultureInvariantString")]
    pub culture_invariant_string: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RelativeRotation {
    #[serde(rename = "Pitch")]
    pub pitch: f64,
    #[serde(rename = "Yaw")]
    pub yaw: f64,
    #[serde(rename = "Roll")]
    pub roll: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SplineParams {
    #[serde(rename = "StartPos")]
    pub start_pos: Option<Vector3>,
    #[serde(rename = "StartTangent")]
    pub start_tangent: Vector3,
    #[serde(rename = "StartRoll")]
    pub start_roll: Option<f64>,
    #[serde(rename = "StartOffset")]
    pub start_offset: Option<Vector2>,
    #[serde(rename = "EndPos")]
    pub end_pos: Vector3,
    #[serde(rename = "EndTangent")]
    pub end_tangent: Vector3,
    #[serde(rename = "EndRoll")]
    pub end_roll: Option<f64>,
    #[serde(rename = "EndOffset")]
    pub end_offset: Option<Vector2>,
    #[serde(rename = "StartScale")]
    pub start_scale: Option<Vector2>,
    #[serde(rename = "EndScale")]
    pub end_scale: Option<Vector2>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SplineCurves {
    #[serde(rename = "Position")]
    pub position: Position,
    #[serde(rename = "Rotation")]
    pub rotation: Option<Rotation>,
    #[serde(rename = "Scale")]
    pub scale: Option<Position>,
    #[serde(rename = "ReparamTable")]
    pub reparam_table: ReparamTable,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Position {
    #[serde(rename = "Points")]
    pub points: Vec<Point>,
    #[serde(rename = "bIsLooped")]
    pub b_is_looped: Option<bool>,
    #[serde(rename = "LoopKeyOffset")]
    pub loop_key_offset: Option<f64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Point {
    #[serde(rename = "InVal")]
    pub in_val: f64,
    #[serde(rename = "OutVal")]
    pub out_val: Vector3,
    #[serde(rename = "ArriveTangent")]
    pub arrive_tangent: Vector3,
    #[serde(rename = "LeaveTangent")]
    pub leave_tangent: Vector3,
    #[serde(rename = "InterpMode")]
    pub interp_mode: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Rotation {
    #[serde(rename = "Points")]
    #[serde(default)]
    pub points: Vec<Point2>,
    #[serde(rename = "bIsLooped")]
    pub b_is_looped: Option<bool>,
    #[serde(rename = "LoopKeyOffset")]
    pub loop_key_offset: Option<f64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Point2 {
    #[serde(rename = "InVal")]
    pub in_val: f64,
    #[serde(rename = "OutVal")]
    pub out_val: BaseRotationOffset,
    #[serde(rename = "ArriveTangent")]
    pub arrive_tangent: BaseRotationOffset,
    #[serde(rename = "LeaveTangent")]
    pub leave_tangent: BaseRotationOffset,
    #[serde(rename = "InterpMode")]
    pub interp_mode: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ReparamTable {
    #[serde(rename = "Points")]
    pub points: Vec<Point3>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Point3 {
    #[serde(rename = "InVal")]
    pub in_val: f64,
    #[serde(rename = "OutVal")]
    pub out_val: f64,
    #[serde(rename = "ArriveTangent")]
    pub arrive_tangent: f64,
    #[serde(rename = "LeaveTangent")]
    pub leave_tangent: f64,
    #[serde(rename = "InterpMode")]
    pub interp_mode: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PrimaryComponentTick {
    #[serde(rename = "EndTickGroup")]
    pub end_tick_group: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct NavMeshResolutionParams {
    #[serde(rename = "AgentMaxStepHeight")]
    pub agent_max_step_height: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct NavDataConfig {
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "Color")]
    pub color: Color,
    #[serde(rename = "DefaultQueryExtent")]
    pub default_query_extent: Option<Vector3>,
    #[serde(rename = "AgentRadius")]
    pub agent_radius: Option<f64>,
    #[serde(rename = "AgentHeight")]
    pub agent_height: Option<f64>,
    #[serde(rename = "AgentStepHeight")]
    pub agent_step_height: Option<f64>,
    #[serde(rename = "PreferredNavData")]
    pub preferred_nav_data: WorldAsset,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SupportedAreas {
    #[serde(rename = "AreaClassName")]
    pub area_class_name: String,
    #[serde(rename = "AreaID")]
    pub area_id: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SavedSelection {
    #[serde(rename = "Type")]
    pub type_field: i64,
    #[serde(rename = "Index")]
    pub index: i64,
    #[serde(rename = "SelectionIndex")]
    pub selection_index: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Settings {
    #[serde(rename = "WeightedBlendables")]
    pub weighted_blendables: Option<WeightedBlendables>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WeightedBlendables {
    #[serde(rename = "Array")]
    pub array: Vec<Array>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Array {
    #[serde(rename = "Weight")]
    pub weight: f64,
    #[serde(rename = "Object")]
    pub object: ObjectPath,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Descriptor {
    #[serde(rename = "StaticMesh")]
    pub static_mesh: ObjectPath,
    #[serde(rename = "Mobility")]
    pub mobility: String,
    #[serde(rename = "InstanceStartCullDistance")]
    pub instance_start_cull_distance: i64,
    #[serde(rename = "InstanceEndCullDistance")]
    pub instance_end_cull_distance: i64,
    #[serde(rename = "bAffectDistanceFieldLighting")]
    pub b_affect_distance_field_lighting: Option<bool>,
    #[serde(rename = "OverrideMaterials")]
    #[serde(default)]
    pub override_materials: Vec<ObjectPath>,
    #[serde(rename = "BodyInstance")]
    pub body_instance: Option<BodyInstance2>,
    #[serde(rename = "bCastShadow")]
    pub b_cast_shadow: Option<bool>,
    #[serde(rename = "bCastDynamicShadow")]
    pub b_cast_dynamic_shadow: Option<bool>,
    #[serde(rename = "bCastStaticShadow")]
    pub b_cast_static_shadow: Option<bool>,
    #[serde(rename = "bGenerateOverlapEvents")]
    pub b_generate_overlap_events: Option<bool>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BodyInstance2 {
    #[serde(rename = "ObjectType")]
    pub object_type: String,
    #[serde(rename = "CollisionEnabled")]
    pub collision_enabled: Option<String>,
    #[serde(rename = "CollisionProfileName")]
    pub collision_profile_name: String,
    #[serde(rename = "CollisionResponses")]
    pub collision_responses: Option<CollisionResponses>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Crc {
    #[serde(rename = "Value")]
    pub value: i64,
    #[serde(rename = "bValid")]
    pub b_valid: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ParametersOverrides {
    #[serde(rename = "Parameters")]
    pub parameters: Parameters,
    #[serde(rename = "PropertiesIDsOverridden")]
    pub properties_ids_overridden: Vec<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Parameters {
    #[serde(rename = "PropertyDescs")]
    pub property_descs: Vec<PropertyDesc>,
    #[serde(rename = "SerialSize")]
    pub serial_size: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PropertyDesc {
    #[serde(rename = "ValueTypeObject")]
    pub value_type_object: Option<ObjectPath>,
    #[serde(rename = "ID")]
    pub id: String,
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "ValueType")]
    pub value_type: String,
    #[serde(rename = "ContainerType")]
    pub container_type: String,
    #[serde(rename = "ContainerTypes")]
    pub container_types: ContainerTypes,
    #[serde(rename = "MetaData")]
    pub meta_data: Value,
    #[serde(rename = "MetaClass")]
    pub meta_class: Value,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ContainerTypes {
    #[serde(rename = "Types")]
    pub types: Vec<Value>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DeliveryPointName {
    #[serde(rename = "Name")]
    pub name: Name,
    #[serde(rename = "Number")]
    pub number: Option<i64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Name {
    #[serde(rename = "CultureInvariantString")]
    pub culture_invariant_string: Option<String>,
    #[serde(rename = "TableId")]
    pub table_id: Option<String>,
    #[serde(rename = "Key")]
    pub key: Option<String>,
    #[serde(rename = "SourceString")]
    pub source_string: Option<String>,
    #[serde(rename = "LocalizedString")]
    pub localized_string: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OverrideParameters {}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WeatherConfig {
    #[serde(rename = "FogParameters")]
    pub fog_parameters: Vec<FogParameter>,
    #[serde(rename = "WeatherSchedules")]
    pub weather_schedules: Vec<WeatherSchedule>,
    #[serde(rename = "WeatherSchedules2")]
    pub weather_schedules2: Vec<Value2>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FogParameter {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: Value,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Value0 {
    #[serde(rename = "FogDensity")]
    pub fog_density: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WeatherSchedule {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: Value2,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Value2 {
    #[serde(rename = "Probability")]
    pub probability: i64,
    #[serde(rename = "Weather")]
    pub weather: String,
    #[serde(rename = "WeatherStartSecondsRange")]
    pub weather_start_seconds_range: Vector2,
    #[serde(rename = "WeatherDurationSecondsRange")]
    pub weather_duration_seconds_range: Vector2,
    #[serde(rename = "Fog")]
    pub fog: Value,
    #[serde(rename = "Temperature")]
    pub temperature: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Object {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: Value3,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Value3 {
    #[serde(rename = "ObjectActorClasses")]
    pub object_actor_classes: Vec<Option<ObjectPath>>,
    #[serde(rename = "StepDistance")]
    pub step_distance: f64,
    #[serde(rename = "SideDistance")]
    pub side_distance: f64,
    #[serde(rename = "StartOffset")]
    pub start_offset: f64,
    #[serde(rename = "EndOffset")]
    pub end_offset: f64,
    #[serde(rename = "HeightOffset")]
    pub height_offset: f64,
    #[serde(rename = "bRaycast")]
    pub b_raycast: bool,
    #[serde(rename = "RaycastStartOffset")]
    pub raycast_start_offset: f64,
    #[serde(rename = "RaycastDistance")]
    pub raycast_distance: f64,
    #[serde(rename = "bStacking")]
    pub b_stacking: bool,
    #[serde(rename = "LocalOffset")]
    pub local_offset: Vector3,
    #[serde(rename = "RandomScaleMin")]
    pub random_scale_min: Vector3,
    #[serde(rename = "RandomScaleMax")]
    pub random_scale_max: Vector3,
    #[serde(rename = "Rotation")]
    pub rotation: RelativeRotation,
    #[serde(rename = "RandomRotation")]
    pub random_rotation: RelativeRotation,
    #[serde(rename = "bMirrorRotateRightSide")]
    pub b_mirror_rotate_right_side: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Segment {
    #[serde(rename = "Key")]
    pub key: Option<String>,
    #[serde(rename = "Value")]
    pub value: Option<Value4>,
    #[serde(rename = "ObjectName")]
    pub object_name: Option<String>,
    #[serde(rename = "ObjectPath")]
    pub object_path: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Value4 {
    #[serde(rename = "SegmentBounds")]
    pub segment_bounds: WorldBounds,
    #[serde(rename = "Objects")]
    pub objects: Vec<Object2>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Object2 {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "Value")]
    pub value: Value5,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Value5 {
    #[serde(rename = "ActorParams")]
    pub actor_params: Vec<ActorParam>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ActorParam {
    #[serde(rename = "ClassIndex")]
    pub class_index: i64,
    #[serde(rename = "Transform")]
    pub transform: Transform,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Transform {
    #[serde(rename = "Rotation")]
    pub rotation: BaseRotationOffset,
    #[serde(rename = "Translation")]
    pub translation: Vector3,
    #[serde(rename = "Scale3D")]
    pub scale3d: Vector3,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OceanConfig {
    #[serde(rename = "OceanLevel")]
    pub ocean_level: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Config {
    #[serde(rename = "CargoSpawns")]
    pub cargo_spawns: Vec<CargoSpawn>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CargoSpawn {
    #[serde(rename = "Flags")]
    pub flags: i64,
    #[serde(rename = "CargoKey")]
    pub cargo_key: String,
    #[serde(rename = "MaxNum")]
    pub max_num: i64,
    #[serde(rename = "bUseSpawner")]
    pub b_use_spawner: bool,
    #[serde(rename = "SpawnCooltimeSecondsMin")]
    pub spawn_cooltime_seconds_min: f64,
    #[serde(rename = "SpawnCooltimeSecondsMax")]
    pub spawn_cooltime_seconds_max: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct NavAgentProps {
    #[serde(rename = "AgentRadius")]
    pub agent_radius: f64,
    #[serde(rename = "AgentHeight")]
    pub agent_height: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Routes {
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "DisplayName")]
    pub display_name: DisplayName,
    #[serde(rename = "BusRouteFlags")]
    pub bus_route_flags: Vec<Value>,
    #[serde(rename = "PaymentMultiplier")]
    pub payment_multiplier: f64,
    #[serde(rename = "BusStops")]
    pub bus_stops: Vec<ObjectPath>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DisplayName {
    #[serde(rename = "CultureInvariantString")]
    pub culture_invariant_string: Option<String>,
    #[serde(rename = "TableId")]
    pub table_id: Option<String>,
    #[serde(rename = "Key")]
    pub key: Option<String>,
    #[serde(rename = "SourceString")]
    pub source_string: Option<String>,
    #[serde(rename = "LocalizedString")]
    pub localized_string: Option<String>,
    #[serde(rename = "Namespace")]
    pub namespace: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AreaNameTexts {
    #[serde(rename = "Texts")]
    pub texts: Vec<MapIconName>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SpawnSetting {
    #[serde(rename = "SettingKey")]
    pub setting_key: String,
    #[serde(rename = "SpawnType")]
    pub spawn_type: String,
    #[serde(rename = "VehicleClass")]
    pub vehicle_class: Value,
    #[serde(rename = "VehicleTypes")]
    pub vehicle_types: Vec<String>,
    #[serde(rename = "GameplayTagQuery")]
    pub gameplay_tag_query: GameplayTagQuery,
    #[serde(rename = "GameplayTagQuery2")]
    pub gameplay_tag_query2: GameplayTagQuery,
    #[serde(rename = "bSpawnAIController")]
    pub b_spawn_aicontroller: bool,
    #[serde(rename = "bSpawnRoadSide")]
    pub b_spawn_road_side: bool,
    #[serde(rename = "bDespawnIfPlayersAreFar")]
    pub b_despawn_if_players_are_far: bool,
    #[serde(rename = "bAllowCloseToPlayer")]
    pub b_allow_close_to_player: bool,
    #[serde(rename = "bAllowCloseToOtherVehicle")]
    pub b_allow_close_to_other_vehicle: bool,
    #[serde(rename = "bDespawnIfNotMoveForLong")]
    pub b_despawn_if_not_move_for_long: bool,
    #[serde(rename = "MaxLifetimeSeconds")]
    pub max_lifetime_seconds: f64,
    #[serde(rename = "MaxCount")]
    pub max_count: i64,
    #[serde(rename = "MinCount")]
    pub min_count: i64,
    #[serde(rename = "SpawnOverMinCountCoolDownTimeSeconds")]
    pub spawn_over_min_count_cool_down_time_seconds: f64,
    #[serde(rename = "CountMultiplierScheduleType")]
    pub count_multiplier_schedule_type: String,
    #[serde(rename = "MinDistanceFromRoad")]
    pub min_distance_from_road: f64,
    #[serde(rename = "MaxDistanceFromRoad")]
    pub max_distance_from_road: f64,
    #[serde(rename = "bIncludeTrailer")]
    pub b_include_trailer: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GameplayTagQuery {
    #[serde(rename = "TokenStreamVersion")]
    pub token_stream_version: i64,
    #[serde(rename = "TagDictionary")]
    pub tag_dictionary: Vec<TagDictionary>,
    #[serde(rename = "QueryTokenStream")]
    pub query_token_stream: Vec<i64>,
    #[serde(rename = "UserDescription")]
    pub user_description: String,
    #[serde(rename = "AutoDescription")]
    pub auto_description: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphData {
    #[serde(rename = "Nodes")]
    pub nodes: Vec<Nodes>,
    #[serde(rename = "Bounds")]
    pub bounds: WorldBounds,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Nodes {
    #[serde(rename = "Road")]
    pub road: ObjectPath,
    #[serde(rename = "SplineDistance")]
    pub spline_distance: f64,
    #[serde(rename = "AbsoluteLocation")]
    pub absolute_location: Vector3,
    #[serde(rename = "Direction")]
    pub direction: Vector3,
    #[serde(rename = "RightVector")]
    pub right_vector: Vector3,
    #[serde(rename = "NodeIndex")]
    pub node_index: i64,
    #[serde(rename = "LateralDistance")]
    pub lateral_distance: f64,
    #[serde(rename = "LateralOffset")]
    pub lateral_offset: f64,
    #[serde(rename = "Lane")]
    pub lane: i64,
    #[serde(rename = "SplinePointIndex")]
    pub spline_point_index: i64,
    #[serde(rename = "Flags")]
    pub flags: i64,
    #[serde(rename = "AutoConnectDistance")]
    pub auto_connect_distance: f64,
    #[serde(rename = "Edges")]
    pub edges: Vec<Edges>,
    #[serde(rename = "SpeedLimit")]
    pub speed_limit: f64,
    #[serde(rename = "IslandId")]
    pub island_id: i64,
    #[serde(rename = "CrossroadId")]
    pub crossroad_id: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Edges {
    #[serde(rename = "NodeIndex")]
    pub node_index: i64,
    #[serde(rename = "Cost")]
    pub cost: f64,
    #[serde(rename = "Distance")]
    pub distance: f64,
    #[serde(rename = "Flags")]
    pub flags: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ProductionConfig {
    #[serde(rename = "InputCargos")]
    pub input_cargos: Vec<CargoKeyAndAmountValue>,
    #[serde(rename = "InputCargoTypes")]
    pub input_cargo_types: Vec<CargoKeyAndAmountValue>,
    #[serde(rename = "InputCargoGameplayTagQuery")]
    pub input_cargo_gameplay_tag_query: CargoGameplayTagQuery,
    #[serde(rename = "OutputCargos")]
    pub output_cargos: Vec<CargoKeyAndAmountValue>,
    #[serde(rename = "OutputCargoTypes")]
    pub output_cargo_types: Vec<CargoKeyAndAmountValue>,
    #[serde(rename = "OutputCargoRowGameplayTagQuery")]
    pub output_cargo_row_gameplay_tag_query: CargoGameplayTagQuery,
    #[serde(rename = "bStoreInputCargo")]
    pub b_store_input_cargo: bool,
    #[serde(rename = "ProductionTimeSeconds")]
    pub production_time_seconds: f64,
    #[serde(rename = "ProductionSpeedMultiplier")]
    pub production_speed_multiplier: f64,
    #[serde(rename = "LocalFoodSupply")]
    pub local_food_supply: f64,
    #[serde(rename = "bHidden")]
    pub b_hidden: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Config2 {
    #[serde(rename = "CargoKey")]
    pub cargo_key: String,
    #[serde(rename = "SpawnCooltimeSecondsMin")]
    pub spawn_cooltime_seconds_min: f64,
    #[serde(rename = "SpawnCooltimeSecondsMax")]
    pub spawn_cooltime_seconds_max: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ScalarParameterValues {
    #[serde(rename = "ParameterInfo")]
    pub parameter_info: ParameterInfo,
    #[serde(rename = "ParameterValue")]
    pub parameter_value: f64,
    #[serde(rename = "ExpressionGUID")]
    pub expression_guid: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ParameterInfo {
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "Association")]
    pub association: String,
    #[serde(rename = "Index")]
    pub index: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BasePropertyOverrides {
    #[serde(rename = "OpacityMaskClipValue")]
    pub opacity_mask_clip_value: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Connections {
    #[serde(rename = "ControlPoint")]
    pub control_point: ObjectPath,
    #[serde(rename = "TangentLen")]
    pub tangent_len: Option<f64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SplineInfo {
    #[serde(rename = "Points")]
    pub points: Vec<Point>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Point4 {
    #[serde(rename = "Center")]
    pub center: Vector3,
    #[serde(rename = "Left")]
    pub left: Vector3,
    #[serde(rename = "Right")]
    pub right: Vector3,
    #[serde(rename = "FalloffLeft")]
    pub falloff_left: Vector3,
    #[serde(rename = "FalloffRight")]
    pub falloff_right: Vector3,
    #[serde(rename = "LayerLeft")]
    pub layer_left: Vector3,
    #[serde(rename = "LayerRight")]
    pub layer_right: Vector3,
    #[serde(rename = "LayerFalloffLeft")]
    pub layer_falloff_left: Vector3,
    #[serde(rename = "LayerFalloffRight")]
    pub layer_falloff_right: Vector3,
    #[serde(rename = "StartEndFalloff")]
    pub start_end_falloff: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ConnectedSegment {
    #[serde(rename = "Segment")]
    pub segment: ObjectPath,
    #[serde(rename = "End")]
    pub end: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PrimaryActorTick {
    #[serde(rename = "bCanEverTick")]
    pub b_can_ever_tick: Option<bool>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CacheMeshExtendedBounds {
    #[serde(rename = "Origin")]
    pub origin: Vector3,
    #[serde(rename = "BoxExtent")]
    pub box_extent: Vector3,
    #[serde(rename = "SphereRadius")]
    pub sphere_radius: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StorageConfig {
    #[serde(rename = "CargoType")]
    pub cargo_type: String,
    #[serde(rename = "CargoKey")]
    pub cargo_key: String,
    #[serde(rename = "MaxStorage")]
    #[serde(default, deserialize_with = "deserialize_zero_as_none")]
    pub max_storage: Option<NonZeroI64>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CullDistances {
    #[serde(rename = "Size")]
    pub size: f64,
    #[serde(rename = "CullDistance")]
    pub cull_distance: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BusStopDisplayName {
    #[serde(rename = "TableId")]
    pub table_id: String,
    #[serde(rename = "Key")]
    pub key: String,
    #[serde(rename = "SourceString")]
    pub source_string: Option<String>,
    #[serde(rename = "LocalizedString")]
    pub localized_string: Option<String>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AggGeom {
    #[serde(rename = "ConvexElems")]
    pub convex_elems: Vec<ConvexElem>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ConvexElem {
    #[serde(rename = "VertexData")]
    pub vertex_data: Vec<Vector3>,
    #[serde(rename = "IndexData")]
    pub index_data: Vec<i64>,
    #[serde(rename = "ElemBox")]
    pub elem_box: WorldBounds,
    #[serde(rename = "Transform")]
    pub transform: Transform,
    #[serde(rename = "RestOffset")]
    pub rest_offset: f64,
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "bContributeToMass")]
    pub b_contribute_to_mass: bool,
    #[serde(rename = "CollisionEnabled")]
    pub collision_enabled: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DefaultInstance {
    #[serde(rename = "ObjectType")]
    pub object_type: String,
    #[serde(rename = "CollisionProfileName")]
    pub collision_profile_name: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Nodes2 {
    #[serde(rename = "Plane")]
    pub plane: Plane,
    #[serde(rename = "iVertPool")]
    pub i_vert_pool: i64,
    #[serde(rename = "iSurf")]
    pub i_surf: i64,
    #[serde(rename = "iVertexIndex")]
    pub i_vertex_index: i64,
    #[serde(rename = "ComponentIndex")]
    pub component_index: i64,
    #[serde(rename = "ComponentNodeIndex")]
    pub component_node_index: i64,
    #[serde(rename = "ComponentElementIndex")]
    pub component_element_index: i64,
    #[serde(rename = "iBack")]
    pub i_back: i64,
    #[serde(rename = "iFront")]
    pub i_front: i64,
    #[serde(rename = "iPlane")]
    pub i_plane: i64,
    #[serde(rename = "iCollisionBound")]
    pub i_collision_bound: i64,
    #[serde(rename = "iZone0")]
    pub i_zone0: i64,
    #[serde(rename = "iZone1")]
    pub i_zone1: i64,
    #[serde(rename = "NumVertices")]
    pub num_vertices: i64,
    #[serde(rename = "NodeFlags")]
    pub node_flags: i64,
    #[serde(rename = "iLeaf0")]
    pub i_leaf0: i64,
    #[serde(rename = "iLeaf1")]
    pub i_leaf1: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Plane {
    #[serde(rename = "Vector")]
    pub vector: Vector3,
    #[serde(rename = "W")]
    pub w: f64,
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
    #[serde(rename = "Z")]
    pub z: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Surf {
    #[serde(rename = "Material")]
    pub material: Option<ObjectPath>,
    #[serde(rename = "PolyFlags")]
    pub poly_flags: i64,
    #[serde(rename = "pBase")]
    pub p_base: i64,
    #[serde(rename = "vNormal")]
    pub v_normal: i64,
    #[serde(rename = "vTextureU")]
    pub v_texture_u: i64,
    #[serde(rename = "vTextureV")]
    pub v_texture_v: i64,
    #[serde(rename = "iBrushPoly")]
    pub i_brush_poly: i64,
    #[serde(rename = "Actor")]
    pub actor: Value,
    #[serde(rename = "Plane")]
    pub plane: Plane,
    #[serde(rename = "LightMapScale")]
    pub light_map_scale: f64,
    #[serde(rename = "iLightmassIndex")]
    pub i_lightmass_index: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct VertexBuffer {
    #[serde(rename = "Vertices")]
    pub vertices: Vec<Vertices>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Vertices {
    #[serde(rename = "Position")]
    pub position: Vector3,
    #[serde(rename = "TangentX")]
    pub tangent_x: Vector3,
    #[serde(rename = "TangentZ")]
    pub tangent_z: Vector4,
    #[serde(rename = "TexCoord")]
    pub tex_coord: Vector2,
    #[serde(rename = "ShadowTexCoord")]
    pub shadow_tex_coord: Vector2,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Vector4 {
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
    #[serde(rename = "Z")]
    pub z: f64,
    #[serde(rename = "W")]
    pub w: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LightmassSetting {
    #[serde(rename = "bUseTwoSidedLighting")]
    pub b_use_two_sided_lighting: bool,
    #[serde(rename = "bShadowIndirectOnly")]
    pub b_shadow_indirect_only: bool,
    #[serde(rename = "bUseEmissiveForStaticLighting")]
    pub b_use_emissive_for_static_lighting: bool,
    #[serde(rename = "bUseVertexNormalForHemisphereGather")]
    pub b_use_vertex_normal_for_hemisphere_gather: bool,
    #[serde(rename = "EmissiveLightFalloffExponent")]
    pub emissive_light_falloff_exponent: f64,
    #[serde(rename = "EmissiveLightExplicitInfluenceRadius")]
    pub emissive_light_explicit_influence_radius: f64,
    #[serde(rename = "EmissiveBoost")]
    pub emissive_boost: f64,
    #[serde(rename = "DiffuseBoost")]
    pub diffuse_boost: f64,
    #[serde(rename = "FullyOccludedSamplesFraction")]
    pub fully_occluded_samples_fraction: f64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CachedData {
    #[serde(rename = "ParentLayerIndexRemap")]
    pub parent_layer_index_remap: Vec<Value>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Url {
    #[serde(rename = "Protocol")]
    pub protocol: String,
    #[serde(rename = "Host")]
    pub host: String,
    #[serde(rename = "Port")]
    pub port: i64,
    #[serde(rename = "Valid")]
    pub valid: bool,
    #[serde(rename = "Map")]
    pub map: String,
    #[serde(rename = "Op")]
    pub op: Vec<Value>,
    #[serde(rename = "Portal")]
    pub portal: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PrecomputedVisibilityHandler {
    #[serde(rename = "PrecomputedVisibilityCellBucketOriginXY")]
    pub precomputed_visibility_cell_bucket_origin_xy: Vector2,
    #[serde(rename = "PrecomputedVisibilityCellSizeXY")]
    pub precomputed_visibility_cell_size_xy: f64,
    #[serde(rename = "PrecomputedVisibilityCellSizeZ")]
    pub precomputed_visibility_cell_size_z: f64,
    #[serde(rename = "PrecomputedVisibilityCellBucketSizeXY")]
    pub precomputed_visibility_cell_bucket_size_xy: i64,
    #[serde(rename = "PrecomputedVisibilityNumCellBuckets")]
    pub precomputed_visibility_num_cell_buckets: i64,
    #[serde(rename = "PrecomputedVisibilityCellBuckets")]
    pub precomputed_visibility_cell_buckets: Vec<Value>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PrecomputedVolumeDistanceField {
    #[serde(rename = "VolumeMaxDistance")]
    pub volume_max_distance: f64,
    #[serde(rename = "VolumeBox")]
    pub volume_box: WorldBounds,
    #[serde(rename = "VolumeSizeX")]
    pub volume_size_x: i64,
    #[serde(rename = "VolumeSizeY")]
    pub volume_size_y: i64,
    #[serde(rename = "VolumeSizeZ")]
    pub volume_size_z: i64,
    #[serde(rename = "Data")]
    pub data: Vec<Value>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PerInstanceSmdata {
    #[serde(rename = "TransformData")]
    pub transform_data: TransformData,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TransformData {
    #[serde(rename = "Rotation")]
    pub rotation: BaseRotationOffset,
    #[serde(rename = "Translation")]
    pub translation: Vector3,
    #[serde(rename = "Scale3D")]
    pub scale3d: Vector3,
    #[serde(rename = "IsRotationNormalized")]
    pub is_rotation_normalized: bool,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ClusterTree {
    #[serde(rename = "MinInstanceScale")]
    pub min_instance_scale: Vector3,
    #[serde(rename = "MaxInstanceScale")]
    pub max_instance_scale: Vector3,
    #[serde(rename = "BoundMin")]
    pub bound_min: Vector3,
    #[serde(rename = "FirstChild")]
    pub first_child: i64,
    #[serde(rename = "BoundMax")]
    pub bound_max: Vector3,
    #[serde(rename = "LastChild")]
    pub last_child: i64,
    #[serde(rename = "FirstInstance")]
    pub first_instance: i64,
    #[serde(rename = "LastInstance")]
    pub last_instance: i64,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ChildProperty {
    #[serde(rename = "Type")]
    pub type_field: String,
    #[serde(rename = "Name")]
    pub name: String,
    #[serde(rename = "Flags")]
    pub flags: String,
    #[serde(rename = "ElementSize")]
    pub element_size: i64,
    #[serde(rename = "PropertyFlags")]
    pub property_flags: Option<String>,
    #[serde(rename = "Struct")]
    pub struct_field: Option<ObjectPath>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CookedFormatData {
    #[serde(rename = "PhysXPC")]
    pub phys_xpc: PhysXpc,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PhysXpc {
    #[serde(rename = "BulkDataFlags")]
    pub bulk_data_flags: String,
    #[serde(rename = "ElementCount")]
    pub element_count: i64,
    #[serde(rename = "SizeOnDisk")]
    pub size_on_disk: i64,
    #[serde(rename = "OffsetInFile")]
    pub offset_in_file: String,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FuncMap {
    #[serde(rename = "ExecuteUbergraph_Jeju_World")]
    pub execute_ubergraph_jeju_world: Option<ObjectPath>,
    #[serde(rename = "ReceiveTick")]
    pub receive_tick: Option<ObjectPath>,
}

#[skip_serializing_none]
#[derive(Default, Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EditorTags {
    #[serde(rename = "BlueprintType")]
    pub blueprint_type: String,
}
