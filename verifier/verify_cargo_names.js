const fs = require("fs");

const data = JSON.parse(fs.readFileSync("out_cargo_name.json", "utf8"));

console.log("Total entries:", Object.keys(data).length);
console.log("SunflowerSeed entry:", data.SunflowerSeed);
console.log("Last 5 entries:");
Object.entries(data)
  .slice(-5)
  .forEach(([k, v]) => {
    console.log(`  ${k}: "${v}"`);
  });
