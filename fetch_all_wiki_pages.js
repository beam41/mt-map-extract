const fs = require("fs");
const https = require("https");
const http = require("http");
const path = require("path");

// Read the vehicles JSON file to get all vehicle slugs
const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

// Create site_wiki directory if it doesn't exist
const siteWikiDir = "site_wiki";
if (!fs.existsSync(siteWikiDir)) {
  fs.mkdirSync(siteWikiDir, { recursive: true });
}

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

// Function to get wiki page content (read-only, works for locked pages too)
async function getWikiPageContent(slug) {
  try {
    console.log(`Fetching: https://wiki.aseanmotorclub.com/vehicles:${slug}`);

    // Use regular view URL to get the rendered page
    const url = `https://wiki.aseanmotorclub.com/vehicles:${slug}`;
    const response = await makeRequest(url);

    if (response.statusCode !== 200) {
      console.log(`Failed to fetch ${slug}: HTTP ${response.statusCode}`);
      return null;
    }

    // Extract the wiki text content from the #wiki__text div
    const wikiTextMatch = response.body.match(
      /<div[^>]*id="wiki__text"[^>]*>([\s\S]*?)<\/div>/
    );

    if (!wikiTextMatch) {
      console.log(`Could not find wiki__text content for ${slug}`);
      return null;
    }

    let content = wikiTextMatch[1];

    // Clean up the HTML to make it more readable
    content = content
      .replace(/<script[\s\S]*?<\/script>/gi, "") // Remove scripts
      .replace(/<style[\s\S]*?<\/style>/gi, "") // Remove styles
      .replace(/<!--[\s\S]*?-->/g, "") // Remove comments
      .replace(/\s+/g, " ") // Normalize whitespace
      .trim();

    return content;
  } catch (error) {
    console.error(`Error fetching ${slug}: ${error.message}`);
    return null;
  }
}

// Function to get raw wiki markup (edit view - may not work for locked pages)
async function getRawWikiMarkup(slug) {
  try {
    console.log(
      `Fetching edit view: https://wiki.aseanmotorclub.com/vehicles:${slug}?do=edit&rev=`
    );

    const url = `https://wiki.aseanmotorclub.com/vehicles:${slug}?do=edit&rev=`;
    const response = await makeRequest(url);

    if (response.statusCode !== 200) {
      console.log(
        `Failed to fetch edit view for ${slug}: HTTP ${response.statusCode}`
      );
      return null;
    }

    // Check if page is locked
    if (response.body.includes("Page is locked for editing")) {
      console.log(`${slug} is locked for editing`);
      return null;
    }

    // Extract the textarea content (raw wiki markup)
    const textareaMatch = response.body.match(
      /<textarea[^>]*name="wikitext"[^>]*>([\s\S]*?)<\/textarea>/
    );

    if (!textareaMatch) {
      console.log(`Could not find textarea content for ${slug}`);
      return null;
    }

    // Decode HTML entities
    let content = textareaMatch[1];
    content = content
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&amp;/g, "&")
      .replace(/&quot;/g, '"');

    return content;
  } catch (error) {
    console.error(`Error fetching raw markup for ${slug}: ${error.message}`);
    return null;
  }
}

// Main function to fetch all pages
async function fetchAllWikiPages() {
  console.log("🚀 WIKI PAGE FETCHER");
  console.log("==================================================");
  console.log("⚠️  This script will fetch ALL vehicle wiki pages!");
  console.log("⚠️  Pages will be saved to site_wiki/ folder!");
  console.log("==================================================\n");

  console.log(
    `Starting wiki fetch process for ${vehiclesData.length} vehicles...\n`
  );

  let successCount = 0;
  let errorCount = 0;
  let lockedCount = 0;

  for (let i = 0; i < vehiclesData.length; i++) {
    const vehicle = vehiclesData[i];
    const slug = vehicle.slug;

    console.log(`--- Processing ${slug} (${i + 1}/${vehiclesData.length}) ---`);

    // Try to get raw wiki markup first (more useful for editing)
    let content = await getRawWikiMarkup(slug);
    let contentType = "markup";

    // If that fails (locked page), get the rendered content
    if (!content) {
      content = await getWikiPageContent(slug);
      contentType = "rendered";

      if (!content) {
        console.log(`❌ Failed to fetch any content for ${slug}`);
        errorCount++;
        continue;
      }

      if (contentType === "rendered") {
        lockedCount++;
      }
    }

    // Save content to file
    const filename = `${slug}_${contentType}.txt`;
    const filepath = path.join(siteWikiDir, filename);

    try {
      // Add metadata header
      const metadata = `// Fetched from: https://wiki.aseanmotorclub.com/vehicles:${slug}
// Content type: ${contentType}
// Vehicle: ${vehicle.name?.en || slug}
// Fetched at: ${new Date().toISOString()}
// ============================================================

`;

      fs.writeFileSync(filepath, metadata + content, "utf8");
      console.log(`✓ Saved ${contentType} content to ${filename}`);
      successCount++;
    } catch (error) {
      console.error(`Error saving ${slug}: ${error.message}`);
      errorCount++;
    }

    // Wait between requests to be respectful
    if (i < vehiclesData.length - 1) {
      console.log("⏳ Waiting 1 second before next request...\n");
      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
  }

  console.log("\n============================================================");
  console.log("📊 FINAL SUMMARY");
  console.log("============================================================");
  console.log(`Total vehicles: ${vehiclesData.length}`);
  console.log(`✅ Successfully fetched: ${successCount}`);
  console.log(`🔒 Locked pages (rendered content): ${lockedCount}`);
  console.log(`❌ Errors: ${errorCount}`);
  console.log(`📁 Files saved to: ${path.resolve(siteWikiDir)}`);

  if (successCount > 0) {
    console.log(`\n🎉 Successfully fetched ${successCount} wiki pages!`);
  }
}

// Run the script
fetchAllWikiPages().catch(console.error);
