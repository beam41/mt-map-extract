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
  console.warn("Localization directory not found. Using default locales.");
  // Fallback to common locales if localization directory doesn't exist
  languages = [
    "en",
    "ko",
    "ja",
    "zh-Hans",
    "zh-Hant",
    "de",
    "fr",
    "es-ES",
    "it",
    "ru",
  ];
}

// Copied from generate_cargo_names.js
const getLocales = (namespace, key, table) => {
  return table[namespace]?.[key] || localizations.en?.[namespace]?.[key];
};

const getNamespace = (name) => {
  return name.Namespace || name.TableId?.split(".")[1] || "";
};

// Function to convert name array to locale object
const convertNameToLocales = (nameArray) => {
  if (!nameArray || !Array.isArray(nameArray) || nameArray.length === 0) {
    return null;
  }

  const localeObject = {};

  // If we have localization data, use it
  if (Object.keys(localizations).length > 0) {
    languages.forEach((lang) => {
      if (localizations[lang]) {
        // Process each name entry in the array
        const localizedNames = nameArray.map((nameEntry) => {
          const namespace = getNamespace(nameEntry);
          const key = nameEntry.Key;

          return (
            getLocales(namespace, key, localizations[lang] || {}) ||
            nameEntry.LocalizedString ||
            nameEntry.CultureInvariantString
          );
        });

        localeObject[lang] = localizedNames.join(" ");
      }
    });
  } else {
    // Fallback: use SourceString or LocalizedString for all locales
    const fallbackName =
      nameArray[0].LocalizedString || nameArray[0].SourceString;
    languages.forEach((lang) => {
      localeObject[lang] = fallbackName;
    });
  }

  return localeObject;
};

// Function to process a single raw JSON file
const processRawJsonFile = (inputPath, outputPath) => {
  try {
    console.log(`Processing ${inputPath}...`);

    const rawData = JSON.parse(fs.readFileSync(inputPath, "utf8"));

    if (!Array.isArray(rawData)) {
      console.error(`Expected array in ${inputPath}, got ${typeof rawData}`);
      return false;
    }

    const processedData = rawData.map((item) => {
      if (item.name && Array.isArray(item.name)) {
        const localeObject = convertNameToLocales(item.name);
        if (localeObject) {
          return { ...item, name: localeObject };
        }
      }
      return item;
    });

    fs.writeFileSync(outputPath, JSON.stringify(processedData, null, 2));
    console.log(`✓ Converted ${inputPath} -> ${outputPath}`);
    console.log(`  Processed ${processedData.length} entries`);

    return true;
  } catch (error) {
    console.error(`Failed to process ${inputPath}:`, error.message);
    return false;
  }
};

// Main function to process all *_raw.json files
const processAllRawFiles = () => {
  const files = fs.readdirSync(__dirname);
  const rawFiles = files.filter((file) => file.endsWith("_raw.json"));

  if (rawFiles.length === 0) {
    console.log("No *_raw.json files found in the current directory.");
    return;
  }

  console.log(`Found ${rawFiles.length} raw JSON files to process:`);
  rawFiles.forEach((file) => console.log(`  - ${file}`));
  console.log();

  let successCount = 0;

  rawFiles.forEach((file) => {
    const inputPath = path.join(__dirname, file);
    const outputPath = path.join(__dirname, file.replace("_raw.json", ".json"));

    if (processRawJsonFile(inputPath, outputPath)) {
      successCount++;
    }
  });

  console.log(
    `\nCompleted: ${successCount}/${rawFiles.length} files processed successfully`
  );

  if (Object.keys(localizations).length === 0) {
    console.log(
      "\nNote: No localization files were found. All entries use the original source strings."
    );
  } else {
    console.log(
      `\nUsed localizations for ${
        Object.keys(localizations).length
      } languages: ${Object.keys(localizations).join(", ")}`
    );
  }
};

// Run the script
if (require.main === module) {
  processAllRawFiles();
}

module.exports = {
  convertNameToLocales,
  processRawJsonFile,
  processAllRawFiles,
  getLocales,
  getNamespace,
};
