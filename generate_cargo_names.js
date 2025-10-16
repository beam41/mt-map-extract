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

// Load all available localizations
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

const getLocales = (namespace, key, table) => {
  return table[namespace]?.[key] || localizations.en[namespace]?.[key];
};

const getNamespace = (name) => {
  return name.Namespace || name.TableId?.split(".")[1] || '';
};

// Extract the Rows object
const rows = cargosData[0].Rows;

// Generate cargo names
const cargoNames = {};

for (const [cargoKey, cargoData] of Object.entries(rows)) {
  const names = {};
  languages.forEach((lang) => {
    if (localizations[lang]) {
      const localized =
        cargoData.Name2.Texts.map(
          (t) =>
            getLocales(
              getNamespace(t),
              t.Key,
              localizations[lang] || {}
            ) ||
            t.LocalizedString ||
            t.CultureInvariantString
        ).join(" ") ||
        getLocales(
          getNamespace(cargoData.Name),
          cargoData.Name.Key,
          localizations[lang] || {}
        ) ||
        cargoData.Name.CultureInvariantString;

      names[lang] = localized;
    }
  });

  if (names) {
    cargoNames[cargoKey] = names;
  }
}

// Write the result to out_cargo_name.json
const outputPath = path.join(__dirname, "out_cargo_name.json");
fs.writeFileSync(outputPath, JSON.stringify(cargoNames, null, 2));

console.log(
  `Generated cargo_name.json with ${Object.keys(cargoNames).length} entries`
);
console.log("Sample entries:");
const sampleEntries = Object.entries(cargoNames).slice(0, 5);
sampleEntries.forEach(([key, name]) => {
  console.log(`  ${key}: "${name}"`);
});
