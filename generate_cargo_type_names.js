const fs = require("fs");
const path = require("path");

// Load all available localizations
const localizationDir = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "Localization",
  "Game"
);

let languages = [];
let localizations = {};

// Check if localization directory exists
if (fs.existsSync(localizationDir)) {
  languages = fs
    .readdirSync(localizationDir)
    .filter((item) =>
      fs.statSync(path.join(localizationDir, item)).isDirectory()
    );

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
} else {
  console.error("Localization directory not found.");
  process.exit(1);
}

// Function to get CargoType keys from all localization files
const getCargoTypeKeys = () => {
  const cargoTypeKeys = new Set();

  // Collect all unique CargoType keys from all language files
  Object.values(localizations).forEach((localization) => {
    if (localization.CargoType) {
      Object.keys(localization.CargoType).forEach((key) => {
        cargoTypeKeys.add(key);
      });
    }
  });

  return Array.from(cargoTypeKeys).sort();
};

// Function to get localized text with fallback to English
const getLocalizedCargoType = (key, lang) => {
  // Try to get from the specified language
  if (
    localizations[lang] &&
    localizations[lang].CargoType &&
    localizations[lang].CargoType[key]
  ) {
    return localizations[lang].CargoType[key];
  }

  // Fallback to English
  if (
    localizations.en &&
    localizations.en.CargoType &&
    localizations.en.CargoType[key]
  ) {
    return localizations.en.CargoType[key];
  }

  // If even English doesn't have it, return the key itself
  return key;
};

// Main function to generate cargo type names
const generateCargoTypeNames = () => {
  console.log("Collecting CargoType keys from localization files...");

  const cargoTypeKeys = getCargoTypeKeys();

  if (cargoTypeKeys.length === 0) {
    console.error("No CargoType keys found in any localization file.");
    return;
  }

  console.log(`Found ${cargoTypeKeys.length} CargoType keys:`, cargoTypeKeys);

  const cargoTypeNames = {};

  cargoTypeKeys.forEach((key) => {
    const prefixedKey = `T::${key}`;
    const names = {};

    languages.forEach((lang) => {
      if (localizations[lang]) {
        names[lang] = getLocalizedCargoType(key, lang);
      }
    });

    cargoTypeNames[prefixedKey] = names;
  });

  // Write the result to cargo_type_names.json
  const outputPath = path.join(__dirname, "out_cargo_type_name.json");
  fs.writeFileSync(outputPath, JSON.stringify(cargoTypeNames, null, 2));

  console.log(
    `\n✓ Generated cargo_type_names.json with ${
      Object.keys(cargoTypeNames).length
    } entries`
  );
  console.log(`  Output file: ${outputPath}`);

  // Show sample entries
  console.log("\nSample entries:");
  const sampleEntries = Object.entries(cargoTypeNames).slice(0, 5);
  sampleEntries.forEach(([key, names]) => {
    console.log(`  ${key}:`);
    console.log(`    en: "${names.en}"`);
    console.log(`    ko: "${names.ko}"`);
    console.log(`    ja: "${names.ja}"`);
  });

  // Show language coverage statistics
  console.log(`\nLanguage coverage:`);
  languages.forEach((lang) => {
    const hasCargoType = localizations[lang] && localizations[lang].CargoType;
    const keyCount = hasCargoType
      ? Object.keys(localizations[lang].CargoType).length
      : 0;
    console.log(
      `  ${lang}: ${hasCargoType ? `${keyCount} keys` : "No CargoType section"}`
    );
  });

  return cargoTypeNames;
};

// Run the script
if (require.main === module) {
  generateCargoTypeNames();
}

module.exports = {
  generateCargoTypeNames,
  getCargoTypeKeys,
  getLocalizedCargoType,
};
