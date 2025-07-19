// generate_cargo_maps.js
// Reads Cargos.json and generates out_cargo_key.json and out_cargo_metadata.json
const fs = require("fs");
const path = require("path");

const cargosPath = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "DataAsset",
  "Cargos.json"
);
const outKeyPath = path.join(__dirname, "out_cargo_key.json");
const outTypePath = path.join(__dirname, "out_cargo_metadata.json");

const data = JSON.parse(fs.readFileSync(cargosPath, "utf8"));
const rows = data[0].Rows;

const cargoKeyByType = {};
const cargoMetadataByKey = {};

for (const [key, value] of Object.entries(rows)) {
  const type = value.CargoType;
  if (!cargoKeyByType[type]) cargoKeyByType[type] = [];
  cargoKeyByType[type].push(key);
  // Build metadata object with new keys
  const meta = { type };
  if (value.MinDeliveryDistance && value.MinDeliveryDistance !== 0.0) {
    meta.minDist = value.MinDeliveryDistance;
  }
  if (value.MaxDeliveryDistance && value.MaxDeliveryDistance !== 0.0) {
    meta.maxDist = value.MaxDeliveryDistance;
  }
  cargoMetadataByKey[key] = meta;
}

fs.writeFileSync(outKeyPath, JSON.stringify(cargoKeyByType, null, 2));
fs.writeFileSync(outTypePath, JSON.stringify(cargoMetadataByKey, null, 2));

console.log("Generated out_cargo_key.json and out_cargo_metadata.json");
