const fs = require("fs");
const path = require("path");

// Read the Vehicles.json file
const vehiclesPath = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "DataAsset",
  "Vehicles",
  "Vehicles.json"
);
const vehiclesData = JSON.parse(fs.readFileSync(vehiclesPath, "utf8"));

// Extract the Rows object
const rows = vehiclesData[0].Rows;

// Generate vehicle names
const vehicleNames = {};

for (const [vehicleKey, vehicleData] of Object.entries(rows)) {
  let vehicleName = "";

  // Check if VehicleName2 exists and has content
  if (
    vehicleData.VehicleName2 &&
    vehicleData.VehicleName2.Texts &&
    vehicleData.VehicleName2.Texts.length > 0
  ) {
    // Join the LocalizedString or CultureInvariantString from VehicleName2.Texts
    const nameArray = vehicleData.VehicleName2.Texts.map((text) => {
      return text.LocalizedString || text.CultureInvariantString || "";
    }).filter((name) => name !== ""); // Remove empty strings

    vehicleName = nameArray.join(" ");
  } else if (vehicleData.VehicleName) {
    // Use VehicleName if VehicleName2 is empty
    vehicleName =
      vehicleData.VehicleName.LocalizedString ||
      vehicleData.VehicleName.CultureInvariantString ||
      "";
  }

  // Only add if we have a valid name
  if (vehicleName) {
    vehicleNames[vehicleKey] = vehicleName;
  }
}

// Write the result to vehicles_name.json
const outputPath = path.join(__dirname, "vehicles_name.json");
fs.writeFileSync(outputPath, JSON.stringify(vehicleNames, null, 2));

console.log(
  `Generated vehicles_name.json with ${
    Object.keys(vehicleNames).length
  } entries`
);
console.log("Sample entries:");
const sampleEntries = Object.entries(vehicleNames).slice(0, 5);
sampleEntries.forEach(([key, name]) => {
  console.log(`  ${key}: "${name}"`);
});
