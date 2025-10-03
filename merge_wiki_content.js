const fs = require("fs");
const path = require("path");

/**
 * Script to merge content from site_wiki and wiki/vehicles folders
 * Creates a to_be_update folder with merged content
 *
 * Rules:
 * - If file exists in both: merge content, keeping site_wiki content but replacing 'In other languages' section with wiki/vehicles version
 * - If file exists only in one: copy directly to to_be_update
 */

// Configuration
const SITE_WIKI_DIR = path.join(__dirname, "site_wiki");
const WIKI_VEHICLES_DIR = path.join(__dirname, "wiki", "vehicles");
const OUTPUT_DIR = path.join(__dirname, "to_be_update");

// File mapping utilities
function getBaseNameFromSiteWiki(filename) {
  // Remove _markup.txt suffix
  return filename.replace(/_markup\.txt$/, "");
}

function getBaseNameFromWikiVehicles(filename) {
  // Remove .txt suffix
  return filename.replace(/\.txt$/, "");
}

function mapSiteWikiToVehicleFile(siteWikiFilename) {
  const baseName = getBaseNameFromSiteWiki(siteWikiFilename);
  return baseName + ".txt";
}

function mapVehicleToSiteWikiFile(vehicleFilename) {
  const baseName = getBaseNameFromWikiVehicles(vehicleFilename);
  return baseName + "_markup.txt";
}

// Content parsing utilities
function extractInOtherLanguagesSection(content) {
  // Look for the "In other languages" section and extract it
  const regex = /===== In other languages =====[\s\S]*?(?=(?:=====|$))/;
  const match = content.match(regex);
  return match ? match[0].trim() : null;
}

function extractInfoboxSection(content) {
  // Extract the infobox section
  const regex = /\{\{infobox>[\s\S]*?\}\}/;
  const match = content.match(regex);
  return match ? match[0].trim() : null;
}

function extractAxleInfoSection(content) {
  // Extract the axle info section
  const regex = /===== Axle info =====[\s\S]*?(?=(?:=====|$))/;
  const match = content.match(regex);
  return match ? match[0].trim() : null;
}

function extractImageFromInfobox(infoboxContent) {
  // Extract image line from infobox
  const regex = /^image\s*=.*$/m;
  const match = infoboxContent.match(regex);
  return match ? match[0].trim() : null;
}

function mergeInfobox(siteWikiInfobox, vehicleInfobox, preserveImage = true) {
  if (!vehicleInfobox) return siteWikiInfobox;
  if (!siteWikiInfobox) return vehicleInfobox;

  let mergedInfobox = vehicleInfobox;

  // If preserveImage is true and site_wiki has an image line, preserve it
  if (preserveImage) {
    const imageFromSiteWiki = extractImageFromInfobox(siteWikiInfobox);
    if (imageFromSiteWiki) {
      // Replace or add the image line in the merged infobox
      const lines = mergedInfobox.split("\n");
      let imageAdded = false;

      for (let i = 0; i < lines.length; i++) {
        if (lines[i].trim().startsWith("image")) {
          lines[i] = imageFromSiteWiki;
          imageAdded = true;
          break;
        } else if (lines[i].trim().startsWith("name")) {
          // Insert image after name line if no image line exists
          lines.splice(i + 1, 0, imageFromSiteWiki);
          imageAdded = true;
          break;
        }
      }

      if (!imageAdded && lines.length > 1) {
        // Insert after the opening line if we couldn't find name
        lines.splice(1, 0, imageFromSiteWiki);
      }

      mergedInfobox = lines.join("\n");
    }
  }

  return mergedInfobox;
}

function preserveSpecialContent(originalContent, newContent, baseName) {
  // Special handling for zero file - preserve content in ((...))
  if (baseName === "zero") {
    const specialContentRegex = /\(\([^)]*\)\)/g;
    const specialMatches = originalContent.match(specialContentRegex);

    if (specialMatches) {
      let result = newContent;
      specialMatches.forEach((match) => {
        // Find the context where this special content should be preserved
        // For zero, it's in the Weight line
        if (match.includes("Zero weighted zero")) {
          result = result.replace(/Weight = 0kg/, `Weight = 0kg${match}`);
        }
      });
      return result;
    }
  }

  return newContent;
}

function removeInOtherLanguagesSection(content) {
  // Remove the "In other languages" section from content
  const regex = /===== In other languages =====[\s\S]*?(?=(?:=====|$))/;
  return content.replace(regex, "").trim();
}

function removeInfoboxSection(content) {
  // Remove the infobox section from content
  const regex = /\{\{infobox>[\s\S]*?\}\}/;
  return content.replace(regex, "").trim();
}

function removeAxleInfoSection(content) {
  // Remove the axle info section from content
  const regex = /===== Axle info =====[\s\S]*?(?=(?:=====|$))/;
  return content.replace(regex, "").trim();
}

function removeCommentHeaders(content) {
  // Remove comment headers (// lines) from the beginning of the file
  const lines = content.split("\n");
  const nonCommentStartIndex = lines.findIndex(
    (line) => !line.trim().startsWith("//") && line.trim() !== ""
  );

  if (nonCommentStartIndex === -1) {
    // If all lines are comments or empty, return empty string
    return "";
  }

  return lines.slice(nonCommentStartIndex).join("\n");
}

function hasInOtherLanguagesSection(content) {
  return /===== In other languages =====/.test(content);
}

// Content merging utilities
function mergeContent(siteWikiContent, vehicleContent, baseName) {
  // Remove comment headers from site_wiki content
  let cleanedSiteWikiContent = removeCommentHeaders(siteWikiContent);

  // Extract sections from both contents
  const siteWikiInfobox = extractInfoboxSection(cleanedSiteWikiContent);
  const vehicleInfobox = extractInfoboxSection(vehicleContent);
  const vehicleAxleInfo = extractAxleInfoSection(vehicleContent);
  const vehicleLanguageSection = extractInOtherLanguagesSection(vehicleContent);

  // Remove sections that will be replaced from site_wiki content
  let mergedContent = removeInOtherLanguagesSection(cleanedSiteWikiContent);
  mergedContent = removeInfoboxSection(mergedContent);
  mergedContent = removeAxleInfoSection(mergedContent);

  // Merge infobox (preserve image from site_wiki if it exists)
  const finalInfobox = mergeInfobox(siteWikiInfobox, vehicleInfobox, true);

  // Build the final content
  let finalContent = "";

  // Add infobox first
  if (finalInfobox) {
    finalContent += finalInfobox + "\n\n";
  }

  // Add the remaining content (without sections we removed)
  finalContent += mergedContent;

  // Add axle info if available
  if (vehicleAxleInfo) {
    if (!finalContent.endsWith("\n")) finalContent += "\n";
    finalContent += "\n" + vehicleAxleInfo;
  }

  // Add language section if available
  if (vehicleLanguageSection) {
    if (!finalContent.endsWith("\n")) finalContent += "\n";
    finalContent += "\n" + vehicleLanguageSection;
  }

  // Apply special content preservation (like for zero file)
  finalContent = preserveSpecialContent(
    cleanedSiteWikiContent,
    finalContent,
    baseName
  );

  return finalContent;
}

function processFileContent(baseName, fileInfo) {
  const siteWikiPath = fileInfo.siteWiki
    ? path.join(SITE_WIKI_DIR, fileInfo.siteWiki)
    : null;
  const vehiclePath = fileInfo.vehicle
    ? path.join(WIKI_VEHICLES_DIR, fileInfo.vehicle)
    : null;

  let outputContent = "";
  let outputFilename = "";
  let operation = "";

  if (siteWikiPath && vehiclePath) {
    // Both files exist - merge them
    const siteWikiContent = readFileContent(siteWikiPath);
    const vehicleContent = readFileContent(vehiclePath);

    if (siteWikiContent && vehicleContent) {
      outputContent = mergeContent(siteWikiContent, vehicleContent, baseName);
      outputFilename = baseName + ".txt"; // Use .txt extension for output
      operation = "merged";
    } else {
      console.error(`Failed to read content for ${baseName}`);
      return null;
    }
  } else if (siteWikiPath) {
    // Only site_wiki file exists - copy it but remove comment headers
    const siteWikiContent = readFileContent(siteWikiPath);
    if (siteWikiContent) {
      outputContent = removeCommentHeaders(siteWikiContent);
      outputFilename = baseName + ".txt"; // Convert to .txt extension
      operation = "copied from site_wiki";
    } else {
      console.error(`Failed to read site_wiki content for ${baseName}`);
      return null;
    }
  } else if (vehiclePath) {
    // Only vehicle file exists - copy it
    const vehicleContent = readFileContent(vehiclePath);
    if (vehicleContent) {
      outputContent = vehicleContent;
      outputFilename = baseName + ".txt";
      operation = "copied from wiki/vehicles";
    } else {
      console.error(`Failed to read vehicle content for ${baseName}`);
      return null;
    }
  } else {
    console.error(`No files found for ${baseName}`);
    return null;
  }

  return {
    content: outputContent,
    filename: outputFilename,
    operation: operation,
  };
}

// Read file content with error handling
function readFileContent(filePath) {
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch (error) {
    console.error(`Error reading file ${filePath}:`, error.message);
    return null;
  }
}

// Get all files from both directories
function getFileInventory() {
  let siteWikiFiles = [];
  let vehicleFiles = [];

  // Safely read site_wiki directory
  try {
    if (fs.existsSync(SITE_WIKI_DIR)) {
      siteWikiFiles = fs
        .readdirSync(SITE_WIKI_DIR)
        .filter((f) => f.endsWith("_markup.txt"));
      console.log(`Found ${siteWikiFiles.length} files in site_wiki directory`);
    } else {
      console.warn(`site_wiki directory not found: ${SITE_WIKI_DIR}`);
    }
  } catch (error) {
    console.error(`Error reading site_wiki directory: ${error.message}`);
  }

  // Safely read wiki/vehicles directory
  try {
    if (fs.existsSync(WIKI_VEHICLES_DIR)) {
      vehicleFiles = fs
        .readdirSync(WIKI_VEHICLES_DIR)
        .filter((f) => f.endsWith(".txt"));
      console.log(
        `Found ${vehicleFiles.length} files in wiki/vehicles directory`
      );
    } else {
      console.warn(`wiki/vehicles directory not found: ${WIKI_VEHICLES_DIR}`);
    }
  } catch (error) {
    console.error(`Error reading wiki/vehicles directory: ${error.message}`);
  }

  // Create mapping of base names to file info
  const fileMap = new Map();

  // Process site_wiki files
  siteWikiFiles.forEach((filename) => {
    const baseName = getBaseNameFromSiteWiki(filename);
    if (!fileMap.has(baseName)) {
      fileMap.set(baseName, {});
    }
    fileMap.get(baseName).siteWiki = filename;
  });

  // Process wiki/vehicles files
  vehicleFiles.forEach((filename) => {
    const baseName = getBaseNameFromWikiVehicles(filename);
    if (!fileMap.has(baseName)) {
      fileMap.set(baseName, {});
    }
    fileMap.get(baseName).vehicle = filename;
  });

  return fileMap;
}

// Ensure output directory exists
function createOutputDirectory() {
  if (fs.existsSync(OUTPUT_DIR)) {
    console.log("Removing existing to_be_update directory...");
    fs.rmSync(OUTPUT_DIR, { recursive: true, force: true });
  }

  console.log("Creating to_be_update directory...");
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
}

// Main execution function
async function main() {
  try {
    console.log("Starting wiki content merge process...");
    createOutputDirectory();

    // Get inventory of all files
    const fileMap = getFileInventory();
    console.log(`Found ${fileMap.size} unique vehicle files to process`);

    const stats = {
      merged: 0,
      copiedFromSiteWiki: 0,
      copiedFromVehicles: 0,
      failed: 0,
    };

    // Process each file
    for (const [baseName, fileInfo] of fileMap) {
      console.log(`Processing: ${baseName}`);

      const result = processFileContent(baseName, fileInfo);
      if (result) {
        // Write the output file
        const outputPath = path.join(OUTPUT_DIR, result.filename);
        try {
          fs.writeFileSync(outputPath, result.content, "utf8");
          console.log(`  ✓ ${result.operation} -> ${result.filename}`);

          // Update stats
          if (result.operation === "merged") stats.merged++;
          else if (result.operation === "copied from site_wiki")
            stats.copiedFromSiteWiki++;
          else if (result.operation === "copied from wiki/vehicles")
            stats.copiedFromVehicles++;
        } catch (writeError) {
          console.error(
            `  ✗ Failed to write ${result.filename}:`,
            writeError.message
          );
          stats.failed++;
        }
      } else {
        console.log(`  ✗ Failed to process ${baseName}`);
        stats.failed++;
      }
    }

    // Print summary
    console.log("\n=== PROCESSING SUMMARY ===");
    console.log(`Files merged: ${stats.merged}`);
    console.log(`Files copied from site_wiki: ${stats.copiedFromSiteWiki}`);
    console.log(`Files copied from wiki/vehicles: ${stats.copiedFromVehicles}`);
    console.log(`Failed: ${stats.failed}`);
    console.log(
      `Total processed: ${
        stats.merged +
        stats.copiedFromSiteWiki +
        stats.copiedFromVehicles +
        stats.failed
      }`
    );

    console.log("\nMerge process completed successfully!");
  } catch (error) {
    console.error("Error during merge process:", error);
    process.exit(1);
  }
}

// Run the script
if (require.main === module) {
  main();
}

module.exports = {
  main,
  createOutputDirectory,
};
