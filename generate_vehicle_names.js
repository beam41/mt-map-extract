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

const localizationDir = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "Localization",
  "Game"
);
const languages = fs
  .readdirSync(localizationDir)
  .filter((item) =>
    fs.statSync(path.join(localizationDir, item)).isDirectory()
  );

const localizations = {};
languages.forEach((lang) => {
  try {
    const langPath = path.join(localizationDir, lang, "Game.json");
    if (fs.existsSync(langPath)) {
      localizations[lang] = JSON.parse(fs.readFileSync(langPath, "utf8"));
    }
  } catch (error) {
    console.warn(`Failed to load localization for ${lang}:`, error.message);
  }
});

const getVehicleLocales = (key, table) => {
  return (
    table.VehicleName[key] ||
    table.Vehicle[key] ||
    localizations.en.VehicleName[key] ||
    localizations.en.Vehicle[key]
  );
};

// Extract the Rows object
const rows = vehiclesData[0].Rows;

// Generate vehicle names
const vehicleNames = {};

for (const [vehicleKey, vehicleData] of Object.entries(rows)) {
  const names = {};
  languages.forEach((lang) => {
    if (localizations[lang]) {
      const localized =
        vehicleData.VehicleName2.Texts.map(
          (t) =>
            getVehicleLocales(t.Key, localizations[lang] || {}) ||
            t.LocalizedString ||
            t.CultureInvariantString
        ).join(" ") ||
        getVehicleLocales(
          vehicleData.VehicleName.Key,
          localizations[lang] || {}
        ) ||
        vehicleData.VehicleName.CultureInvariantString;

      names[lang] = localized;
    }
  });

  if (names) {
    vehicleNames[vehicleKey] = names;
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
