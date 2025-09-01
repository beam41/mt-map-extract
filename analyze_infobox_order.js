const fs = require("fs");
const path = require("path");

// Function to extract infobox content and parse keys
function extractInfoboxKeys(content) {
  // Extract the infobox content between {{infobox> and }}
  const infoboxMatch = content.match(/{{infobox>([\s\S]*?)}}/);
  if (!infoboxMatch) {
    return null;
  }

  const infoboxContent = infoboxMatch[1];
  const keys = [];

  // Split by lines and extract key names
  const lines = infoboxContent.split("\n");
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed && trimmed.includes("=")) {
      const keyMatch = trimmed.match(/^([^=]+)\s*=/);
      if (keyMatch) {
        const key = keyMatch[1].trim();
        // Ignore image key when comparing
        if (key && key.toLowerCase() !== "image") {
          keys.push(key);
        }
      }
    }
  }

  return keys;
}

// Function to extract vehicle name from markup
function extractNameFromMarkup(content) {
  const nameMatch = content.match(/name\s*=\s*(.+)/);
  return nameMatch ? nameMatch[1].trim() : "Unknown";
}

// Function to compare infobox key order between site_wiki and local wiki files
function compareInfoboxKeyOrder() {
  console.log("🔍 INFOBOX KEY ORDER COMPARISON");
  console.log("==================================================");
  console.log("Comparing key ordering between site_wiki and wiki/vehicles");
  console.log("==================================================\n");

  const siteWikiDir = "site_wiki";
  const localWikiDir = "wiki/vehicles";

  // Get files from both directories
  const siteMarkupFiles = fs
    .readdirSync(siteWikiDir)
    .filter((file) => file.endsWith("_markup.txt"));

  const localWikiFiles = fs
    .readdirSync(localWikiDir)
    .filter((file) => file.endsWith(".txt"));

  const comparisons = [];
  const orderMismatches = [];
  const missingFiles = [];

  console.log(
    `Comparing ${siteMarkupFiles.length} site_wiki files with local wiki files...\n`
  );

  // Process each site_wiki file and find its local counterpart
  for (const siteFile of siteMarkupFiles) {
    const slug = siteFile.replace("_markup.txt", "");
    const localFile = `${slug}.txt`;

    const siteFilePath = path.join(siteWikiDir, siteFile);
    const localFilePath = path.join(localWikiDir, localFile);

    try {
      // Read site wiki file
      const siteContent = fs.readFileSync(siteFilePath, "utf8");
      const siteKeys = extractInfoboxKeys(siteContent);
      const siteName = extractNameFromMarkup(siteContent);

      // Check if local file exists
      if (!fs.existsSync(localFilePath)) {
        missingFiles.push({
          slug,
          name: siteName,
          type: "LOCAL_MISSING",
          message: `Local file ${localFile} not found`,
        });
        continue;
      }

      // Read local wiki file
      const localContent = fs.readFileSync(localFilePath, "utf8");
      const localKeys = extractInfoboxKeys(localContent);
      const localName = extractNameFromMarkup(localContent);

      if (!siteKeys || !localKeys) {
        missingFiles.push({
          slug,
          name: siteName || localName,
          type: "NO_INFOBOX",
          message: `Missing infobox in ${!siteKeys ? "site" : "local"} file`,
        });
        continue;
      }

      // Compare key orders
      const siteOrder = siteKeys.join(" → ");
      const localOrder = localKeys.join(" → ");
      const isMatch = siteOrder === localOrder;

      const comparison = {
        slug,
        siteName,
        localName,
        siteKeys,
        localKeys,
        siteOrder,
        localOrder,
        isMatch,
        siteKeyCount: siteKeys.length,
        localKeyCount: localKeys.length,
      };

      comparisons.push(comparison);

      if (!isMatch) {
        orderMismatches.push(comparison);
        console.log(`❌ ${siteName} (${slug})`);
        console.log(`   Site:  ${siteOrder}`);
        console.log(`   Local: ${localOrder}`);

        // Find specific differences
        const maxLength = Math.max(siteKeys.length, localKeys.length);
        const differences = [];
        for (let i = 0; i < maxLength; i++) {
          const siteKey = siteKeys[i] || "[missing]";
          const localKey = localKeys[i] || "[missing]";
          if (siteKey !== localKey) {
            differences.push(`Position ${i}: "${siteKey}" vs "${localKey}"`);
          }
        }
        if (differences.length > 0) {
          console.log(`   Diffs: ${differences.join(", ")}`);
        }
        console.log("");
      }
    } catch (error) {
      console.error(`Error processing ${siteFile}: ${error.message}`);
    }
  }

  // Print results
  console.log("📊 COMPARISON RESULTS");
  console.log("==================================================");

  const matches = comparisons.filter((c) => c.isMatch);

  if (orderMismatches.length > 0) {
    console.log(`\n❌ KEY ORDER MISMATCHES (${orderMismatches.length}):`);
    console.log("──────────────────────────────────────────────────");
    // Already printed above during processing
  }

  if (missingFiles.length > 0) {
    console.log(`\n⚠️  MISSING FILES OR INFOBOXES (${missingFiles.length}):`);
    console.log("──────────────────────────────────────────────────");
    missingFiles.forEach((item) => {
      console.log(`• ${item.name} (${item.slug}): ${item.message}`);
    });
  }

  console.log(`\n✅ MATCHING KEY ORDERS (${matches.length}):`);
  console.log("──────────────────────────────────────────────────");
  if (matches.length <= 10) {
    matches.forEach((item) => {
      console.log(
        `• ${item.siteName} (${item.slug}): ${item.siteKeyCount} keys`
      );
    });
  } else {
    console.log(`First 10 matches:`);
    matches.slice(0, 10).forEach((item) => {
      console.log(
        `• ${item.siteName} (${item.slug}): ${item.siteKeyCount} keys`
      );
    });
    console.log(`... and ${matches.length - 10} more matches`);
  }

  // Key count differences
  const keyCountDiffs = comparisons.filter(
    (c) => c.siteKeyCount !== c.localKeyCount
  );
  if (keyCountDiffs.length > 0) {
    console.log(`\n📊 KEY COUNT DIFFERENCES (${keyCountDiffs.length}):`);
    console.log("──────────────────────────────────────────────────");
    keyCountDiffs.forEach((item) => {
      console.log(
        `• ${item.siteName} (${item.slug}): Site ${item.siteKeyCount} vs Local ${item.localKeyCount}`
      );
    });
  }

  console.log("\n============================================================");
  console.log("📈 SUMMARY");
  console.log("============================================================");
  console.log(`Total comparisons: ${comparisons.length}`);
  console.log(`✅ Matching key orders: ${matches.length}`);
  console.log(`❌ Key order mismatches: ${orderMismatches.length}`);
  console.log(`📊 Key count differences: ${keyCountDiffs.length}`);
  console.log(`⚠️  Missing files/infoboxes: ${missingFiles.length}`);

  const totalIssues =
    orderMismatches.length + keyCountDiffs.length + missingFiles.length;
  if (totalIssues === 0) {
    console.log(
      "\n🎉 All infobox key orders match perfectly between site and local files!"
    );
  } else {
    console.log(`\n⚠️  Found ${totalIssues} issues that may need attention.`);
  }

  return {
    comparisons,
    matches,
    orderMismatches,
    keyCountDiffs,
    missingFiles,
    totalIssues,
  };
}

// Run the comparison
compareInfoboxKeyOrder();
