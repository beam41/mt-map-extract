const fs = require("fs");
const path = require("path");

/**
 * Remove duplicate language keys from a name object based on matching values with the "en" key
 * @param {Object} nameObj - The name object containing language translations
 * @param {string} context - Context for logging (file name and item info)
 * @returns {Object} - Name object with duplicate language keys removed
 */
function removeDuplicateLanguageKeys(nameObj, context) {
  if (!nameObj || typeof nameObj !== "object" || !nameObj.en) {
    return nameObj;
  }

  const result = {};
  const enValue = nameObj.en;
  let removedKeys = [];

  // Always keep the "en" key as the reference
  result.en = enValue;

  // Check all other language keys
  for (const [langKey, langValue] of Object.entries(nameObj)) {
    if (langKey === "en") {
      continue; // Already added above
    }

    if (langValue === enValue) {
      // This language has the same value as English, so remove it
      removedKeys.push(langKey);
    } else {
      // Keep this language as it has a different value
      result[langKey] = langValue;
    }
  }

  if (removedKeys.length > 0) {
    console.log(
      `  [${context}] Removed duplicate language keys: ${removedKeys.join(
        ", "
      )} (same as en: "${enValue}")`
    );
  }

  return result;
}

/**
 * Process JSON data to remove duplicate language keys within name objects
 * @param {Object|Array} data - The JSON data to process
 * @param {string} fileName - The name of the file being processed for better logging
 * @returns {Object|Array} - Processed data with duplicate language keys removed
 */
function removeDuplicatesFromNames(data, fileName) {
  if (Array.isArray(data)) {
    // Handle array format (like out_area_volume.json and out_delivery_point.json)
    return data.map((item, index) => {
      if (item && item.name && typeof item.name === "object") {
        const context = `${fileName} - item ${index}`;
        return {
          ...item,
          name: removeDuplicateLanguageKeys(item.name, context),
        };
      }
      return item;
    });
  } else if (typeof data === "object" && data !== null) {
    // Handle object format (like out_vehicles_name.json and cargo_name.json)
    const result = {};
    for (const [key, value] of Object.entries(data)) {
      if (value && typeof value === "object") {
        const context = `${fileName} - key "${key}"`;
        result[key] = removeDuplicateLanguageKeys(value, context);
      } else {
        result[key] = value;
      }
    }
    return result;
  }

  return data;
}

/**
 * Process a single JSON file to remove duplicate language keys
 * @param {string} filePath - Path to the JSON file
 */
function processFile(filePath) {
  const fileName = path.basename(filePath);
  console.log(`\n📁 Processing file: ${fileName}`);

  try {
    // Check if file exists
    if (!fs.existsSync(filePath)) {
      console.log(`❌ File not found: ${filePath}`);
      return;
    }

    // Read the file
    const fileContent = fs.readFileSync(filePath, "utf8");
    const originalData = JSON.parse(fileContent);

    // Count original language keys
    let originalLangKeysCount = 0;
    let processedLangKeysCount = 0;

    // Count function for original data
    function countLanguageKeys(data) {
      let count = 0;
      if (Array.isArray(data)) {
        data.forEach((item) => {
          if (item && item.name && typeof item.name === "object") {
            count += Object.keys(item.name).length;
          }
        });
      } else if (typeof data === "object" && data !== null) {
        Object.values(data).forEach((value) => {
          if (value && typeof value === "object") {
            count += Object.keys(value).length;
          }
        });
      }
      return count;
    }

    originalLangKeysCount = countLanguageKeys(originalData);
    console.log(`📊 Original language keys: ${originalLangKeysCount}`);

    // Remove duplicate language keys
    const processedData = removeDuplicatesFromNames(originalData, fileName);

    // Count remaining language keys
    processedLangKeysCount = countLanguageKeys(processedData);
    console.log(`📊 Remaining language keys: ${processedLangKeysCount}`);

    const removedCount = originalLangKeysCount - processedLangKeysCount;

    if (removedCount > 0) {
      console.log(
        `🗑️  Removed: ${removedCount} duplicate language key${
          removedCount === 1 ? "" : "s"
        }`
      );

      // Write back to file with proper formatting
      const jsonString = JSON.stringify(processedData, null, 2);
      fs.writeFileSync(filePath, jsonString, "utf8");

      console.log(`✅ File updated: ${fileName}`);
    } else {
      console.log(`✨ No duplicate language keys found in ${fileName}`);
    }
  } catch (error) {
    console.error(`❌ Error processing ${fileName}:`, error.message);
  }
}

/**
 * Main function to process all specified files
 */
function main() {
  console.log("🚀 Starting duplicate language key removal process...");
  console.log("=".repeat(60));

  const filesToProcess = [
    "out_area_volume.json", // Array format with name.en structure
    "out_delivery_point.json", // Array format with name.en structure
    "out_vehicles_name.json", // Object format with direct en property
    "out_cargo_name.json", // Object format with direct en property
    "out_cargo_type_name.json", // Object format with direct en property
  ];

  // Get the current directory
  const currentDir = process.cwd();

  for (const fileName of filesToProcess) {
    const filePath = path.join(currentDir, fileName);
    processFile(filePath);
  }

  console.log("\n" + "=".repeat(60));
  console.log("🎉 Duplicate language key removal process completed!");
}
// Run the script
if (require.main === module) {
  main();
}

module.exports = {
  removeDuplicateLanguageKeys,
  removeDuplicatesFromNames,
  processFile,
  main,
};
