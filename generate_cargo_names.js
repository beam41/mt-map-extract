const fs = require("fs");
const path = require("path");

// Read the Cargos.json file
const cargosPath = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "DataAsset",
  "Cargos.json"
);
const cargosData = JSON.parse(fs.readFileSync(cargosPath, "utf8"));

// Extract the Rows object
const rows = cargosData[0].Rows;

// Generate cargo names
const cargoNames = {};

for (const [cargoKey, cargoData] of Object.entries(rows)) {
  let cargoName = "";

  // Check if Name2 exists and has content
  if (
    cargoData.Name2 &&
    cargoData.Name2.Texts &&
    cargoData.Name2.Texts.length > 0
  ) {
    // Join the LocalizedString or CultureInvariantString from Name2.Texts
    const nameArray = cargoData.Name2.Texts.map((text) => {
      return text.LocalizedString || text.CultureInvariantString || "";
    }).filter((name) => name !== ""); // Remove empty strings

    cargoName = nameArray.join(" ");
  } else if (cargoData.Name) {
    // Use Name if Name2 is empty
    cargoName =
      cargoData.Name.LocalizedString ||
      cargoData.Name.CultureInvariantString ||
      "";
  }

  // Only add if we have a valid name
  if (cargoName) {
    cargoNames[cargoKey] = cargoName;
  }
}

// Write the result to cargo_name.json
const outputPath = path.join(__dirname, "cargo_name.json");
fs.writeFileSync(outputPath, JSON.stringify(cargoNames, null, 2));

console.log(
  `Generated cargo_name.json with ${Object.keys(cargoNames).length} entries`
);
console.log("Sample entries:");
const sampleEntries = Object.entries(cargoNames).slice(0, 5);
sampleEntries.forEach(([key, name]) => {
  console.log(`  ${key}: "${name}"`);
});
