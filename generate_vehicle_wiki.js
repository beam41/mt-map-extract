const fs = require("fs");
const path = require("path");

// Read the vehicles JSON file
const vehiclesData = JSON.parse(fs.readFileSync("vehicles.json", "utf8"));

// Function to convert PascalCase to space-separated words
function pascalCaseToSpaces(text) {
  // Split PascalCase into words
  const words = text
    .replace(/([A-Z])/g, " $1")
    .trim()
    .split(" ");

  // Capitalize only the first word, lowercase the rest
  return words
    .map((word, index) => {
      if (index === 0) {
        return word; // Keep first word as is (already capitalized)
      } else {
        return word.toLowerCase(); // Lowercase subsequent words
      }
    })
    .join(" ");
}

// Function to format vehicle types for Type field (comma-separated)
function formatTypes(types) {
  if (types.length === 0) return "vehicle";

  // Convert PascalCase to space-separated words with proper capitalization
  const processedTypes = types.map((type) => {
    const words = pascalCaseToSpaces(type).split(" ");
    // Capitalize first word of each type, lowercase the rest
    return words
      .map((word, index) => {
        if (index === 0) {
          return word; // Keep first word capitalized
        } else {
          return word.toLowerCase();
        }
      })
      .join(" ");
  });

  // Join types with comma and space
  return processedTypes.join(", ");
}

// Function to format vehicle types for description text (duty + vehicle type + vehicle)
function formatTypesForDescription(types) {
  if (types.length === 0) return "vehicle";

  // Convert PascalCase to space-separated words and make lowercase
  const processedTypes = types.map((type) =>
    pascalCaseToSpaces(type).toLowerCase()
  );

  // If there are two parts, rearrange them (duty type + vehicle type)
  if (processedTypes.length === 2) {
    return `${processedTypes[1]} ${processedTypes[0]}`;
  }

  // For single type, check if it's "small" and append "vehicle"
  const result = processedTypes.join(" ");
  if (result === "small") {
    return "small vehicle";
  }

  return result;
}

// Function to format numbers with thousand separators
function formatNumber(number) {
  return number.toLocaleString("en-US");
}

// Function to map language codes to proper language names
function getLanguageName(langCode) {
  const languageMap = {
    cs: "Czech",
    de: "German",
    en: "English",
    "es-419": "Spanish (Latin America)",
    "es-ES": "Spanish (Spain)",
    fi: "Finnish",
    fr: "French",
    hu: "Hungarian",
    it: "Italian",
    ja: "Japanese",
    ko: "Korean",
    lt: "Lithuanian",
    nl: "Dutch",
    no: "Norwegian",
    pl: "Polish",
    "pt-BR": "Portuguese (Brazil)",
    ru: "Russian",
    sv: "Swedish",
    tr: "Turkish",
    uk: "Ukrainian",
    "zh-Hans": "Chinese (Simplified)",
    "zh-Hant": "Chinese (Traditional)",
  };

  return languageMap[langCode] || langCode;
}

// Function to generate DokuWiki content for a vehicle
function generateVehicleWiki(vehicle) {
  let content = "{{infobox>\n";
  content += `name = ${vehicle.name.en}\n`;
  content += `Internal key = ${vehicle.key}\n`;
  content += `Type = ${formatTypes(vehicle.type)}\n`;
  content += `Cost = ${formatNumber(vehicle.cost)}\n`;

  // Add level requirements if they exist
  if (vehicle.level && Object.keys(vehicle.level).length > 0) {
    const levelRequirements = Object.entries(vehicle.level)
      .map(
        ([type, level]) =>
          `${type.charAt(0).toUpperCase() + type.slice(1)}: ${level}`
      )
      .join(", ");
    content += `Level requirement = ${levelRequirements}\n`;
  }

  // Add comfort unless it's a trailer
  const isTrailer = vehicle.type.some(
    (type) => type === "SmallTrailer" || type === "SemiTrailer"
  );

  if (!isTrailer) {
    if (vehicle.comfort > 0) {
      const stars = "⭐".repeat(vehicle.comfort);
      content += `Comfort = ${stars}\n`;
    } else {
      content += `Comfort = No comfort\n`;
    }
  }

  // Only add fuel line if not a trailer
  if (!isTrailer && vehicle.fuel !== null) {
    if (vehicle.fuel.cap) {
      const unit = vehicle.fuel.type === "Electric" ? "kWh" : "L";
      content += `Fuel = ${vehicle.fuel.cap}${unit} (${vehicle.fuel.type})\n`;
    } else {
      const unit =
        vehicle.fuel && vehicle.fuel.type === "Electric" ? "kWh" : "L";
      content += `Fuel = 0${unit}\n`;
    }
  }

  // Only add seats if not a trailer
  if (!isTrailer) {
    content += `Seats = ${vehicle.seats}\n`;
  }

  content += `Weight = ${formatNumber(vehicle.weight)}kg\n`;

  // Add drivetrain info if vehicle has driven wheels and is not a trailer
  if (vehicle.axles && vehicle.axles.length > 0) {
    const drivenAxles = vehicle.axles
      .map((axle, index) => ({
        index,
        driven: axle.driven,
      }))
      .filter((axle) => axle.driven);

    if (!isTrailer) {
      if (drivenAxles.length === 0) {
        // No driven axles
        content += `Drivetrain = No driven axle\n`;
      } else {
        let drivetrainType;
        const totalAxles = vehicle.axles.length;

        // Determine drivetrain type based on driven axle positions
        if (drivenAxles.length === totalAxles) {
          drivetrainType = "All-wheel drive";
        } else if (drivenAxles.length === 1) {
          // Single driven axle
          if (drivenAxles[0].index === 0) {
            drivetrainType = "Front-wheel drive";
          } else {
            drivetrainType = "Rear-wheel drive";
          }
        } else {
          // Multiple but not all axles driven
          const frontDriven = drivenAxles.some((axle) => axle.index === 0);
          const rearDriven = drivenAxles.some((axle) => axle.index > 0);

          if (frontDriven && rearDriven) {
            drivetrainType = "All-wheel drive";
          } else if (frontDriven) {
            drivetrainType = "Front-wheel drive";
          } else {
            drivetrainType = "Rear-wheel drive";
          }
        }

        content += `Drivetrain = ${drivetrainType}\n`;
      }
    }

    // Add drag coefficient after drivetrain if available
    if (vehicle.drag != null) {
      content += `Drag coefficient = ${vehicle.drag}\n`;
    }

    // Check if any axle has lift capability (only for Truck and SemiTractor)
    const canShowAxleLift = vehicle.type.some(
      (type) => type === "Truck" || type === "SemiTractor"
    );

    if (canShowAxleLift) {
      const hasLift = vehicle.axles.some((axle) => axle.lift);
      content += `Axle lift = ${hasLift ? "Yes" : "No"}\n`;
    }
  }

  content += "}}\n\n";
  content += `====== ${vehicle.name.en} ======\n`;
  content += `**${vehicle.name.en}** is a ${formatTypesForDescription(
    vehicle.type
  )} in [[:motor_town|Motor Town]]\n\n`;

  // Add axle information table
  content += `===== Axle info =====\n`;
  if (vehicle.axles && vehicle.axles.length > 0) {
    content += `^ Axle ^ Break Ratio ^ Driven ^ Dual Wheels ^ Liftable ^\n`;
    vehicle.axles.forEach((axle, index) => {
      // Generate human-readable axle names
      let axleName;
      const totalAxles = vehicle.axles.length;

      if (totalAxles === 1) {
        axleName = "Single";
      } else if (totalAxles === 2) {
        axleName = index === 0 ? "Front" : "Rear";
      } else if (totalAxles === 3) {
        if (index === 0) axleName = "Front";
        else if (index === 1) axleName = "Middle";
        else axleName = "Rear";
      } else if (totalAxles === 4) {
        if (index === 0) axleName = "Front";
        else if (index === 1) axleName = "Front Middle";
        else if (index === 2) axleName = "Rear Middle";
        else axleName = "Rear";
      } else {
        // For 5+ axles, use Front, Middle 1, Middle 2, etc., Rear
        if (index === 0) {
          axleName = "Front";
        } else if (index === totalAxles - 1) {
          axleName = "Rear";
        } else {
          axleName = `Middle ${index}`;
        }
      }

      const liftable = axle.lift ? "**Yes**" : "No";
      const dualWheels = axle.dual ? "**Yes**" : "No";
      const driven = axle.driven ? "**Yes**" : "No";

      // Format break ratio as percentage
      let breakRatio;
      if (axle.breakRatio === null || axle.breakRatio === undefined) {
        breakRatio = "//*N/A*//";
      } else if (axle.breakRatio === 0) {
        breakRatio = "0%";
      } else {
        breakRatio = `${(axle.breakRatio * 100).toFixed(1)}%`;
      }

      content += `| ${axleName} | ${breakRatio} | ${driven} | ${dualWheels} | ${liftable} |\n`;
    });
  } else {
    content += `No axle information available.\n`;
  }
  content += `\n`;

  // Add "In other languages" section
  content += `===== In other languages =====\n`;
  content += `^ Language ^ Name ^\n`;
  Object.entries(vehicle.name).forEach(([lang, name]) => {
    if (lang !== "en") {
      // Skip English as it's already shown in the title
      const languageName = getLanguageName(lang);
      content += `| ${languageName} | ${name} |\n`;
    }
  });

  return content;
}

// Create vehicles directory if it doesn't exist
const vehiclesDir = "wiki/vehicles";
if (!fs.existsSync(vehiclesDir)) {
  fs.mkdirSync(vehiclesDir, { recursive: true });
}

// Generate wiki files for each vehicle
let processedCount = 0;
let totalVehicles = vehiclesData.length;

vehiclesData.forEach((vehicle) => {
  const filename = `${vehicle.slug}.txt`;
  const filepath = path.join(vehiclesDir, filename);
  const wikiContent = generateVehicleWiki(vehicle);

  fs.writeFileSync(filepath, wikiContent, "utf8");
  processedCount++;

  if (processedCount % 10 === 0 || processedCount === totalVehicles) {
    console.log(`Processed ${processedCount}/${totalVehicles} vehicles`);
  }
});

console.log(
  `\nSuccessfully generated ${processedCount} DokuWiki files in ${vehiclesDir}/`
);
console.log("Files are named using vehicle slugs with .txt extension");
