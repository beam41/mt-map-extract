// Validate that all keys in prod.input, prod.output, and demand are present in storage for each item
const fs = require("fs");

const data = JSON.parse(fs.readFileSync("out_delivery_point.json", "utf8"));

let hasError = false;
data.forEach((item, idx) => {
  const storageKeys = item.storage ? Object.keys(item.storage) : [];
  let requiredKeys = new Set();

  // Collect keys from prod.input and prod.output
  if (Array.isArray(item.prod)) {
    item.prod.forEach((prod) => {
      if (prod.input) {
        Object.keys(prod.input).forEach((k) => requiredKeys.add(k));
      }
      if (prod.output) {
        Object.keys(prod.output).forEach((k) => requiredKeys.add(k));
      }
    });
  }
  // Collect keys from demand
  if (item.demand) {
    Object.keys(item.demand).forEach((k) => requiredKeys.add(k));
  }

  // Check for missing keys
  const missing = Array.from(requiredKeys).filter(
    (k) => !storageKeys.includes(k)
  );
  if (missing.length > 0) {
    hasError = true;
    console.log(
      `Item #${idx} (${item.name || item.type}): missing in storage:`,
      missing
    );
  }

  // Check that all storage values are greater than 0
  const zeroOrNegative = storageKeys.filter(
    (k) => typeof item.storage[k] !== "number" || item.storage[k] <= 0
  );
  if (zeroOrNegative.length > 0) {
    hasError = true;
    console.log(
      `Item #${idx} (${
        item.name || item.type
      }): storage keys with non-positive value:`,
      zeroOrNegative
    );
  }
});

if (!hasError) {
  console.log("All delivery points have correct storage keys.");
}
