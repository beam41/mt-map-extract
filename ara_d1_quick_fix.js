const fs = require("fs");

// Read the raw delivery point data
const rawData = JSON.parse(
  fs.readFileSync("out_delivery_point_raw.json", "utf8")
);

// Find the Ara Construction Site F1 entry
const f1Entry = rawData.find(
  (entry) => entry && entry.guid === "c9706ffb47e057140a3262bee1d6a896"
);

if (!f1Entry) {
  console.error("Could not find Ara Construction Site F1 entry");
  process.exit(1);
}

console.log("Found Ara Construction Site F1 entry");

// Create a deep copy of the F1 entry for D1
const d1Entry = JSON.parse(JSON.stringify(f1Entry));

// Modify the guid (convert to lowercase and remove dashes to match file format)
d1Entry.guid = "0f0ce05643dabcbc05af558932011bd6";

// Modify the coordinates
d1Entry.coord = {
  x: 412910.0 + -102.6871,
  y: 1104760.0 + -1180.4766,
  z: -19100.037 + 0.124391824,
};

// Update the name to indicate it's D1 instead of F1
if (d1Entry.name && Array.isArray(d1Entry.name)) {
  d1Entry.name = d1Entry.name.map((nameItem) => {
    if (nameItem.CultureInvariantString === "F1") {
      return { ...nameItem, CultureInvariantString: "D1" };
    }
    return nameItem;
  });
}

console.log("Created Ara Construction Site D1 entry with:");
console.log(`- GUID: ${d1Entry.guid}`);
console.log(
  `- Coordinates: x=${d1Entry.coord.x}, y=${d1Entry.coord.y}, z=${d1Entry.coord.z}`
);
console.log(`- Name suffix: D1`);

// Add the D1 entry to the data
rawData.push(d1Entry);

// Write the updated data back to the file
fs.writeFileSync(
  "out_delivery_point_raw.json",
  JSON.stringify(rawData, null, 2)
);

console.log(
  "✅ Successfully added Ara Construction Site D1 to out_delivery_point_raw.json"
);
console.log(`Total entries in file: ${rawData.length}`);
