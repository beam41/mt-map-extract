const fs = require("fs");
const https = require("https");
const http = require("http");

// Read the vehicles JSON file to get drag data
const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

// Create a map of slug to drag coefficient for quick lookup
const dragMap = {};
vehiclesData.forEach((vehicle) => {
  if (vehicle.drag != null) {
    dragMap[vehicle.slug] = vehicle.drag;
  }
});

// Function to make HTTP request
function makeRequest(url, options = {}) {
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url);
    const isHttps = urlObj.protocol === "https:";
    const client = isHttps ? https : http;

    const req = client.request(url, options, (res) => {
      let data = "";
      res.on("data", (chunk) => {
        data += chunk;
      });
      res.on("end", () => {
        resolve({
          statusCode: res.statusCode,
          headers: res.headers,
          body: data,
        });
      });
    });

    req.on("error", (err) => {
      reject(err);
    });

    if (options.body) {
      req.write(options.body);
    }

    req.end();
  });
}

// Function to unlock a page
async function unlockPage(slug) {
  try {
    const unlockUrl = `https://wiki.aseanmotorclub.com/vehicles:${slug}?do=unlock`;
    console.log(`Attempting to unlock: ${unlockUrl}`);

    const response = await makeRequest(unlockUrl);

    if (response.statusCode === 200 || response.statusCode === 302) {
      console.log(`✓ Page ${slug} unlocked successfully`);
      return true;
    } else {
      console.log(`Failed to unlock ${slug}: HTTP ${response.statusCode}`);
      return false;
    }
  } catch (error) {
    console.error(`Error unlocking ${slug}: ${error.message}`);
    return false;
  }
}

// Function to get the raw wiki content
async function getWikiContent(slug) {
  try {
    const url = `https://wiki.aseanmotorclub.com/vehicles:${slug}?do=edit&rev=`;
    console.log(`Fetching: ${url}`);

    const response = await makeRequest(url);

    if (response.statusCode !== 200) {
      throw new Error(`HTTP ${response.statusCode}`);
    }

    // Check for edit lock
    if (response.body.includes("This page is currently locked for editing")) {
      throw new Error("Page is locked for editing");
    }

    // Parse the edit page to extract the raw wiki content
    const html = response.body;
    const textareaMatch = html.match(
      /<textarea[^>]*name="wikitext"[^>]*>(.*?)<\/textarea>/s
    );

    if (!textareaMatch) {
      throw new Error("Could not find wikitext textarea");
    }

    // Extract sectok (security token) for form submission
    const sectokMatch = html.match(/name="sectok"\s+value="([^"]+)"/);
    const sectok = sectokMatch ? sectokMatch[1] : null;

    // Decode HTML entities
    let content = textareaMatch[1];
    content = content
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&amp;/g, "&")
      .replace(/&quot;/g, '"');

    return { content, sectok };
  } catch (error) {
    console.error(`Error fetching ${slug}: ${error.message}`);
    return null;
  }
}

// Function to update wiki content
async function updateWikiContent(
  slug,
  content,
  sectok,
  summary = "Added drag coefficient"
) {
  try {
    const url = `https://wiki.aseanmotorclub.com/vehicles:${slug}`;

    // Prepare form data for wiki submission
    const formData = new URLSearchParams();
    formData.append("do", "save");
    formData.append("id", `vehicles:${slug}`);
    formData.append("wikitext", content);
    formData.append("summary", summary);
    formData.append("sectok", sectok);
    formData.append("target", "section");
    formData.append("range", "");

    const postOptions = {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
        "Content-Length": Buffer.byteLength(formData.toString()),
      },
      body: formData.toString(),
    };

    console.log(`Submitting changes to ${slug}...`);
    const response = await makeRequest(url, postOptions);

    if (response.statusCode === 302 || response.statusCode === 200) {
      console.log(`✓ Successfully updated ${slug}`);
      return true;
    } else {
      console.error(`Failed to update ${slug}: HTTP ${response.statusCode}`);
      return false;
    }
  } catch (error) {
    console.error(`Error updating ${slug}: ${error.message}`);
    return false;
  }
}

// Function to modify wiki content
function addDragToWikiContent(content, dragValue) {
  // Check if drag coefficient already exists
  if (content.includes("Drag coefficient =")) {
    return { modified: false, reason: "Drag coefficient already exists" };
  }

  // For trailers and vehicles without drivetrain, add drag coefficient after Weight
  // For vehicles with drivetrain, add drag coefficient after drivetrain
  const drivetrainRegex = /^Drivetrain = (.+)$/m;
  const weightRegex = /^Weight = (.+)$/m;
  const drivetrainMatch = content.match(drivetrainRegex);
  const weightMatch = content.match(weightRegex);

  const dragLine = `Drag coefficient = ${dragValue}`;
  let updatedContent;

  if (drivetrainMatch) {
    // Vehicle has drivetrain - add drag coefficient after drivetrain
    updatedContent = content.replace(
      drivetrainRegex,
      `${drivetrainMatch[0]}\n${dragLine}`
    );
  } else if (weightMatch) {
    // Vehicle has no drivetrain (trailer) - add drag coefficient after weight
    updatedContent = content.replace(
      weightRegex,
      `${weightMatch[0]}\n${dragLine}`
    );
  } else {
    return { modified: false, reason: "No drivetrain or weight found" };
  }

  return { modified: true, content: updatedContent };
} // Main function to process all vehicles with drag data
async function processAllVehicles() {
  console.log("Starting wiki update process for all vehicles...\n");

  // Get vehicles that have drag data
  const vehiclesWithDrag = vehiclesData.filter((v) => v.drag != null);
  console.log(`Found ${vehiclesWithDrag.length} vehicles with drag data\n`);

  let processedCount = 0;
  let modifiedCount = 0;
  let errorCount = 0;
  let skippedCount = 0;

  // Process vehicles one by one to avoid overwhelming the server
  for (const vehicle of vehiclesWithDrag) {
    try {
      console.log(
        `\n--- Processing ${vehicle.slug} (${processedCount + 1}/${
          vehiclesWithDrag.length
        }) ---`
      );

      // Get current wiki content
      const result = await getWikiContent(vehicle.slug);
      if (!result) {
        console.error("Failed to fetch wiki content");
        errorCount++;
        processedCount++;
        continue;
      }

      const { content, sectok } = result;
      console.log("Successfully fetched wiki content");
      console.log(`Content length: ${content.length} characters`);

      // Try to add drag coefficient
      const modResult = addDragToWikiContent(content, vehicle.drag);

      if (modResult.modified) {
        console.log("✓ Content modified successfully");

        // Save backup
        const localPath = `wiki/vehicles/${vehicle.slug}_updated.txt`;
        fs.writeFileSync(localPath, modResult.content, "utf8");
        console.log(`✓ Saved backup to ${localPath}`);

        // Update the wiki
        const success = await updateWikiContent(
          vehicle.slug,
          modResult.content,
          sectok
        );

        if (success) {
          modifiedCount++;
          console.log(
            `✅ Successfully added drag coefficient: ${vehicle.drag}`
          );
        } else {
          errorCount++;
          console.error("❌ Failed to update wiki");
        }
      } else {
        skippedCount++;
        console.log(`⏭ Skipped: ${modResult.reason}`);
      }

      processedCount++;

      // Add delay between requests to be respectful to the server
      if (processedCount < vehiclesWithDrag.length) {
        console.log("⏳ Waiting 2 seconds before next request...");
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    } catch (error) {
      console.error(`❌ Error processing ${vehicle.slug}: ${error.message}`);
      errorCount++;
      processedCount++;
    }
  }

  console.log("\n" + "=".repeat(60));
  console.log("📊 FINAL SUMMARY");
  console.log("=".repeat(60));
  console.log(`Total vehicles with drag data: ${vehiclesWithDrag.length}`);
  console.log(`✅ Successfully modified: ${modifiedCount}`);
  console.log(`⏭ Skipped (already present): ${skippedCount}`);
  console.log(`❌ Errors: ${errorCount}`);
  console.log(`📝 Total processed: ${processedCount}`);

  if (modifiedCount > 0) {
    console.log(
      `\n🎉 Successfully updated ${modifiedCount} wiki pages with drag coefficients!`
    );
  }
}

// Run the script
if (require.main === module) {
  console.log("🚀 WIKI DRAG COEFFICIENT UPDATER");
  console.log("=".repeat(50));
  console.log("⚠️  WARNING: This script will modify ALL vehicle wiki pages!");
  console.log("⚠️  Make sure you have proper authentication and permissions!");
  console.log("⚠️  This will make REAL changes to the live wiki!");
  console.log("⚠️  Backups will be saved locally before updating.");
  console.log("=".repeat(50));
  console.log("");

  processAllVehicles().catch(console.error);
}

module.exports = { getWikiContent, updateWikiContent, addDragToWikiContent };
