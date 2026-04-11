const fs = require("fs");
const path = require("path");

// Read the vehicles JSON file
const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

// Function to convert name to title case
function toTitleCase(str) {
  return str.replace(/\w\S*/g, function (txt) {
    return txt.charAt(0).toUpperCase() + txt.substr(1).toLowerCase();
  });
}

// Extract vehicle names and slugs, then sort alphabetically by name
const vehicles = vehiclesData
  .map((vehicle) => ({
    name: vehicle.name.en,
    slug: vehicle.slug,
  }))
  .sort((a, b) => {
    // Sort alphabetically by name (case-insensitive)
    return a.name.localeCompare(b.name, "en", { sensitivity: "base" });
  });

// Generate the list in DokuWiki format
let listContent = `====== List of Vehicles ======

Currently there are ${vehicles.length} vehicles in [[:motor_town|Motor Town]]. For comparison see [[:vehicle_comparison|vehicle comparison table]].
`;

// Add each vehicle as a list item
vehicles.forEach((vehicle) => {
  listContent += `  * [[vehicles:${vehicle.slug}|${vehicle.name}]]\n`;
});

// Write to the wiki directory
const outputPath = path.join("wiki", "list.txt");
fs.writeFileSync(outputPath, listContent, "utf8");

console.log(
  `Generated vehicle list with ${vehicles.length} vehicles at ${outputPath}`
);
