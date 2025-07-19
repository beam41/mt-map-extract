const fs = require("fs");

const jsonPath = "out_delivery_point.json";

const data = JSON.parse(fs.readFileSync(jsonPath, "utf-8"));

function findExtraKeys(demand, prodInput) {
  if (
    typeof demand !== "object" ||
    demand === null ||
    typeof prodInput !== "object" ||
    prodInput === null
  ) {
    return [];
  }
  return Object.keys(demand).filter((k) => !(k in prodInput));
}

// Collect all guids that exist in any dropPoint
let allDropPointGuids = new Set();
if (Array.isArray(data)) {
  data.forEach((obj) => {
    if (Array.isArray(obj.dropPoint)) {
      obj.dropPoint.forEach((guid) => allDropPointGuids.add(guid));
    }
  });
}

if (Array.isArray(data)) {
  data.forEach((obj, idx) => {
    // Skip if this object's guid exists in any dropPoint or type is Resident_C
    if (
      (obj.guid && allDropPointGuids.has(obj.guid)) ||
      obj.type === "Resident_C"
    )
      return;
    // prod can be array or object, but skip if no prod.input exists
    let prodInput = {};
    let hasProdInput = false;
    if (Array.isArray(obj.prod)) {
      obj.prod.forEach((prod) => {
        if (prod && prod.input && typeof prod.input === "object") {
          prodInput = { ...prodInput, ...prod.input };
          hasProdInput = true;
        }
      });
    } else if (obj.prod && obj.prod.input) {
      prodInput = obj.prod.input;
      hasProdInput = true;
    }
    if (!hasProdInput) return;
    const demand = obj.demand || {};
    const extra = findExtraKeys(demand, prodInput);
    if (extra.length > 0) {
      console.log(
        `Object ${obj.name}: Extra keys in demand not in prod.input:`,
        extra
      );
    }
  });
} else {
  const demand = data.demand || {};
  const prodInput = (data.prod && data.prod.input) || {};
  const extra = findExtraKeys(demand, prodInput);
  if (extra.length > 0) {
    console.log("Extra keys in demand not in prod.input:", extra);
  }
}
