const fs = require("fs");
const path = require("path");

// Read the vehicles JSON file to get drag data
const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

// Create a map of slug to drag coefficient from vehicles.json
const vehiclesDragMap = {};
vehiclesData.forEach((vehicle) => {
  vehiclesDragMap[vehicle.slug] = vehicle.drag || null;
});

// Function to extract drag coefficient from wiki markup
function extractDragFromMarkup(content) {
  // Look for "Drag coefficient = X.XX" pattern in the infobox
  const dragMatch = content.match(/Drag coefficient\s*=\s*([\d.]+)/i);
  return dragMatch ? parseFloat(dragMatch[1]) : null;
}

// Function to extract vehicle name from markup
function extractNameFromMarkup(content) {
  const nameMatch = content.match(/name\s*=\s*(.+)/);
  return nameMatch ? nameMatch[1].trim() : "Unknown";
}

// Main comparison function
function compareDragCoefficients() {
  console.log("🔍 DRAG COEFFICIENT COMPARISON");
  console.log("==================================================");
  console.log("Comparing site_wiki files with vehicles.json data");
  console.log("==================================================\n");

  const siteWikiDir = "site_wiki";
  const outliers = [];
  const missing = [];
  const extra = [];
  const matches = [];

  // Get all markup files from site_wiki
  const markupFiles = fs
    .readdirSync(siteWikiDir)
    .filter((file) => file.endsWith("_markup.txt"));

  console.log(`Found ${markupFiles.length} wiki markup files to analyze...\n`);

  for (const file of markupFiles) {
    // Extract slug from filename (remove _markup.txt)
    const slug = file.replace("_markup.txt", "");
    const filePath = path.join(siteWikiDir, file);

    try {
      const content = fs.readFileSync(filePath, "utf8");
      const wikiDrag = extractDragFromMarkup(content);
      const jsonDrag = vehiclesDragMap[slug];
      const vehicleName = extractNameFromMarkup(content);

      const result = {
        slug,
        name: vehicleName,
        wikiDrag,
        jsonDrag,
        file,
      };

      // Compare values
      if (wikiDrag !== null && jsonDrag !== null) {
        if (Math.abs(wikiDrag - jsonDrag) > 0.001) {
          // Allow for small floating point differences
          result.type = "MISMATCH";
          result.difference = wikiDrag - jsonDrag;
          outliers.push(result);
        } else {
          result.type = "MATCH";
          matches.push(result);
        }
      } else if (wikiDrag !== null && jsonDrag === null) {
        result.type = "EXTRA_IN_WIKI";
        extra.push(result);
      } else if (wikiDrag === null && jsonDrag !== null) {
        result.type = "MISSING_IN_WIKI";
        missing.push(result);
      } else {
        result.type = "BOTH_NULL";
        matches.push(result);
      }
    } catch (error) {
      console.error(`Error processing ${file}: ${error.message}`);
    }
  }

  // Print results
  console.log("📊 ANALYSIS RESULTS");
  console.log("==================================================");

  if (outliers.length > 0) {
    console.log(`\n❌ MISMATCHES (${outliers.length}):`);
    console.log("──────────────────────────────────────────────────");
    outliers.forEach((item) => {
      console.log(`• ${item.name} (${item.slug})`);
      console.log(
        `  Wiki: ${item.wikiDrag} | JSON: ${
          item.jsonDrag
        } | Diff: ${item.difference.toFixed(3)}`
      );
    });
  }

  if (missing.length > 0) {
    console.log(`\n⚠️  MISSING IN WIKI (${missing.length}):`);
    console.log("──────────────────────────────────────────────────");
    missing.forEach((item) => {
      console.log(`• ${item.name} (${item.slug})`);
      console.log(`  Wiki: None | JSON: ${item.jsonDrag}`);
    });
  }

  if (extra.length > 0) {
    console.log(`\n➕ EXTRA IN WIKI (${extra.length}):`);
    console.log("──────────────────────────────────────────────────");
    extra.forEach((item) => {
      console.log(`• ${item.name} (${item.slug})`);
      console.log(`  Wiki: ${item.wikiDrag} | JSON: None`);
    });
  }

  console.log(`\n✅ MATCHES (${matches.length}):`);
  console.log("──────────────────────────────────────────────────");
  if (matches.length <= 10) {
    matches.forEach((item) => {
      const dragValue = item.wikiDrag !== null ? item.wikiDrag : "None";
      console.log(`• ${item.name} (${item.slug}): ${dragValue}`);
    });
  } else {
    console.log(`First 10 matches:`);
    matches.slice(0, 10).forEach((item) => {
      const dragValue = item.wikiDrag !== null ? item.wikiDrag : "None";
      console.log(`• ${item.name} (${item.slug}): ${dragValue}`);
    });
    console.log(`... and ${matches.length - 10} more matches`);
  }

  // Summary
  console.log("\n============================================================");
  console.log("📈 SUMMARY");
  console.log("============================================================");
  console.log(`Total files analyzed: ${markupFiles.length}`);
  console.log(`✅ Matches: ${matches.length}`);
  console.log(`❌ Mismatches: ${outliers.length}`);
  console.log(`⚠️  Missing in wiki: ${missing.length}`);
  console.log(`➕ Extra in wiki: ${extra.length}`);

  const totalIssues = outliers.length + missing.length + extra.length;
  if (totalIssues === 0) {
    console.log(
      "\n🎉 No outliers found! All drag coefficients match perfectly."
    );
  } else {
    console.log(`\n⚠️  Found ${totalIssues} outliers that need attention.`);
  }

  // Check for vehicles in JSON but not in wiki files
  const wikiSlugs = new Set(
    markupFiles.map((f) => f.replace("_markup.txt", ""))
  );
  const missingSlugs = vehiclesData.filter((v) => !wikiSlugs.has(v.slug));

  if (missingSlugs.length > 0) {
    console.log(
      `\n🚨 VEHICLES IN JSON BUT NO WIKI FILE (${missingSlugs.length}):`
    );
    console.log("──────────────────────────────────────────────────");
    missingSlugs.forEach((vehicle) => {
      const dragInfo = vehicle.drag ? ` (drag: ${vehicle.drag})` : " (no drag)";
      console.log(`• ${vehicle.name?.en || vehicle.slug}${dragInfo}`);
    });
  }

  return {
    outliers,
    missing,
    extra,
    matches,
    totalFiles: markupFiles.length,
    totalIssues,
  };
}

// Run the comparison
compareDragCoefficients();
