const fs = require("fs");
const path = require("path");

const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

function pascalCaseToSpaces(text) {
  const words = text
    .replace(/([A-Z])/g, " $1")
    .trim()
    .split(" ");
  return words
    .map((word, index) => (index === 0 ? word : word.toLowerCase()))
    .join(" ");
}

function formatTypes(types) {
  if (!types || types.length === 0) return "vehicle";
  const processedTypes = types.map((type) => {
    const words = pascalCaseToSpaces(type).split(" ");
    return words
      .map((word, index) => (index === 0 ? word : word.toLowerCase()))
      .join(" ");
  });
  return processedTypes.join(", ");
}

function formatLevelRequirement(level) {
  if (!level || Object.keys(level).length === 0) return "";
  return Object.entries(level)
    .map(
      ([type, lvl]) => `${type.charAt(0).toUpperCase() + type.slice(1)}: ${lvl}`
    )
    .join(", ");
}

function formatNumber(number) {
  return number != null ? number.toLocaleString("en-US") : "";
}

function getDrivetrain(vehicle) {
  const isTrailer = vehicle.type.some(
    (type) => type === "SmallTrailer" || type === "SemiTrailer"
  );
  if (isTrailer) return "";
  if (!vehicle.axles || vehicle.axles.length === 0) return "";
  const drivenAxles = vehicle.axles
    .map((axle, index) => ({ index, driven: axle.driven }))
    .filter((axle) => axle.driven);
  const totalAxles = vehicle.axles.length;
  if (drivenAxles.length === 0) return "";
  if (drivenAxles.length === totalAxles) return "AWD";
  if (drivenAxles.length === 1) {
    if (drivenAxles[0].index === 0) return "FWD";
    else return "RWD";
  }
  const frontDriven = drivenAxles.some((axle) => axle.index === 0);
  const rearDriven = drivenAxles.some((axle) => axle.index > 0);
  if (frontDriven && rearDriven) return "AWD";
  if (frontDriven) return "FWD";
  return "RWD";
}

function getFuel(vehicle) {
  const isTrailer = vehicle.type.some(
    (type) => type === "SmallTrailer" || type === "SemiTrailer"
  );
  if (isTrailer) return "";
  if (!vehicle.fuel) return "";
  const cap = vehicle.fuel.cap ? vehicle.fuel.cap : 0;
  const unit = vehicle.fuel.type === "Electric" ? "kWh" : "L";
  return `${cap}${unit}`;
}

function getFuelType(vehicle) {
  const isTrailer = vehicle.type.some(
    (type) => type === "SmallTrailer" || type === "SemiTrailer"
  );
  if (isTrailer) return "";
  if (!vehicle.fuel) return "";
  return vehicle.fuel.type;
}

function getDrag(vehicle) {
  return vehicle.drag != null ? vehicle.drag.toString() : "";
}

let table =
  "^ Name ^ Type ^ Level Requirement ^ Cost ^ Drivetrain ^ Weight ^ Fuel ^ Fuel Type ^ Drag ^\n";
vehiclesData.forEach((vehicle) => {
  // Special case for display names
  let displayName = vehicle.name.en;
  if (vehicle.slug === "bongo_bus") {
    displayName = "Bongo (Bus)";
  }

  const name = `[[:vehicles:${vehicle.slug}|${displayName}]]`;
  const type = formatTypes(vehicle.type);
  const levelReq = formatLevelRequirement(vehicle.level);
  const cost = formatNumber(vehicle.cost);
  const drivetrain = getDrivetrain(vehicle);
  const weight = formatNumber(vehicle.weight) + "kg";
  const fuel = getFuel(vehicle);
  const fuelType = getFuelType(vehicle);
  const drag = getDrag(vehicle);
  table += `| ${name} | ${type} | ${levelReq} | ${cost} | ${drivetrain} | ${weight} | ${fuel} | ${fuelType} | ${drag} |\n`;
});

const filePath = path.join("wiki", "vehicle_comparison.txt");
const header = `====== Vehicle Comparison Table ======
`;
const fullContent = header + table;
fs.writeFileSync(filePath, fullContent, "utf8");

console.log(
  "Vehicle comparison table generated in wiki/vehicle_comparison.txt"
);
