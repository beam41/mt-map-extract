const fs = require("fs");
const https = require("https");
const http = require("http");
const path = require("path");

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

// Function to read content from to_be_update folder
function readUpdateFile(filename) {
  try {
    const filePath = path.join("to_be_update", filename);

    if (!fs.existsSync(filePath)) {
      console.error(`File not found: ${filePath}`);
      return null;
    }

    const content = fs.readFileSync(filePath, "utf8");
    console.log(`✓ Read ${content.length} characters from ${filename}`);
    return content;
  } catch (error) {
    console.error(`Error reading ${filename}: ${error.message}`);
    return null;
  }
}

// Function to get all update files and their corresponding slugs
function getUpdateFiles() {
  try {
    const files = fs.readdirSync("to_be_update");
    const txtFiles = files.filter((file) => file.endsWith(".txt"));

    return txtFiles.map((file) => ({
      filename: file,
      slug: file.replace(".txt", ""),
    }));
  } catch (error) {
    console.error(`Error reading to_be_update directory: ${error.message}`);
    return [];
  }
}

// Function to update wiki content with complete replacement
async function updateWikiContent(
  slug,
  content,
  sectok,
  summary = "Updated wiki content"
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

// Function to backup original wiki content
function backupOriginalContent(slug, content) {
  try {
    // Create wiki backup directory if it doesn't exist
    const backupDir = "wiki_backups";
    if (!fs.existsSync(backupDir)) {
      fs.mkdirSync(backupDir, { recursive: true });
    }

    const backupPath = path.join(backupDir, `${slug}_original.txt`);
    fs.writeFileSync(backupPath, content, "utf8");
    console.log(`✓ Backed up original content to ${backupPath}`);
    return true;
  } catch (error) {
    console.error(`Error backing up ${slug}: ${error.message}`);
    return false;
  }
}

// Main function to process all files in to_be_update folder
async function processAllWikiUpdates() {
  console.log("Starting wiki replacement process for all files...\n");

  // Get all update files
  const updateFiles = getUpdateFiles();
  console.log(`Found ${updateFiles.length} files to update\n`);

  let processedCount = 0;
  let successCount = 0;
  let errorCount = 0;
  let skippedCount = 0;

  // Process files one by one to avoid overwhelming the server
  for (const fileInfo of updateFiles) {
    try {
      console.log(
        `\n--- Processing ${fileInfo.slug} (${processedCount + 1}/${
          updateFiles.length
        }) ---`
      );

      // Read the new content from to_be_update folder
      const newContent = readUpdateFile(fileInfo.filename);
      if (!newContent) {
        console.error("Failed to read update file");
        errorCount++;
        processedCount++;
        continue;
      }

      // Get current wiki content (needed for sectok)
      const result = await getWikiContent(fileInfo.slug);
      if (!result) {
        console.error("Failed to fetch wiki page for sectok");
        errorCount++;
        processedCount++;
        continue;
      }

      const { content: originalContent, sectok } = result;
      console.log("Successfully fetched wiki page");

      // Backup original content
      const backupSuccess = backupOriginalContent(
        fileInfo.slug,
        originalContent
      );
      if (!backupSuccess) {
        console.warn("Failed to backup original content, continuing anyway...");
      }

      // Update the wiki with new content
      const success = await updateWikiContent(
        fileInfo.slug,
        newContent,
        sectok,
        "Complete wiki content replacement"
      );

      if (success) {
        successCount++;
        console.log(
          `✅ Successfully replaced wiki content for ${fileInfo.slug}`
        );
      } else {
        errorCount++;
        console.error("❌ Failed to update wiki");
      }

      processedCount++;

      // Add delay between requests to be respectful to the server
      if (processedCount < updateFiles.length) {
        console.log("⏳ Waiting 2 seconds before next request...");
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    } catch (error) {
      console.error(`❌ Error processing ${fileInfo.slug}: ${error.message}`);
      errorCount++;
      processedCount++;
    }
  }

  console.log("\n" + "=".repeat(60));
  console.log("📊 FINAL SUMMARY");
  console.log("=".repeat(60));
  console.log(`Total files to update: ${updateFiles.length}`);
  console.log(`✅ Successfully updated: ${successCount}`);
  console.log(`⏭ Skipped: ${skippedCount}`);
  console.log(`❌ Errors: ${errorCount}`);
  console.log(`📝 Total processed: ${processedCount}`);

  if (successCount > 0) {
    console.log(
      `\n🎉 Successfully updated ${successCount} wiki pages with new content!`
    );
    console.log(`📁 Original content backed up to wiki_backups/ folder`);
  }
}

// Run the script
if (require.main === module) {
  console.log("🚀 WIKI CONTENT REPLACEMENT UPDATER");
  console.log("=".repeat(50));
  console.log(
    "⚠️  WARNING: This script will COMPLETELY REPLACE ALL wiki pages!"
  );
  console.log(
    "⚠️  Content will be replaced with files from to_be_update/ folder!"
  );
  console.log("⚠️  Make sure you have proper authentication and permissions!");
  console.log("⚠️  This will make REAL changes to the live wiki!");
  console.log(
    "⚠️  Original content will be backed up to wiki_backups/ folder."
  );
  console.log("=".repeat(50));
  console.log("");

  processAllWikiUpdates().catch(console.error);
}

module.exports = {
  getWikiContent,
  updateWikiContent,
  readUpdateFile,
  getUpdateFiles,
  backupOriginalContent,
};
