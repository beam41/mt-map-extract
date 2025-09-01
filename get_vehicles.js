const fs = require("fs");
const path = require("path");

// Read data from Vehicles.json
const vehiclesPath = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "DataAsset",
  "Vehicles",
  "Vehicles.json"
);
const data = JSON.parse(fs.readFileSync(vehiclesPath, "utf8"))[0].Rows;

// Load all available localizations
const localizationDir = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "Localization",
  "Game"
);
const languages = fs
  .readdirSync(localizationDir)
  .filter((item) =>
    fs.statSync(path.join(localizationDir, item)).isDirectory()
  );

const localizations = {};
languages.forEach((lang) => {
  try {
    const langPath = path.join(localizationDir, lang, "Game.json");
    if (fs.existsSync(langPath)) {
      localizations[lang] = JSON.parse(fs.readFileSync(langPath, "utf8"));
    }
  } catch (error) {
    console.warn(`Failed to load localization for ${lang}:`, error.message);
  }
});

const getVehicleLocales = (key, table) => {
  return (
    table.VehicleName[key] ||
    table.Vehicle[key] ||
    localizations.en.VehicleName[key] ||
    localizations.en.Vehicle[key]
  );
};

const enginesPath = path.join(
  __dirname,
  "MotorTown",
  "Content",
  "DataAsset",
  "VehicleParts",
  "Engines.json"
);
const engineData = JSON.parse(fs.readFileSync(enginesPath, "utf8"))[0].Rows;

const result = JSON.stringify(
  Object.entries(data).map(([key, data]) => {
    // Get vehicle name in English for readable_key
    const enVehicleName =
      getVehicleLocales(data.VehicleName.Key, localizations.en || {}) ||
      data.VehicleName2.Texts.map(
        (t) =>
          getVehicleLocales(t.Key, localizations.en || {}) ||
          t.CultureInvariantString
      ).join(" ") ||
      data.VehicleName.CultureInvariantString;

    // Get vehicle names in all available languages
    const names = {};
    languages.forEach((lang) => {
      if (localizations[lang]) {
        const localized =
          getVehicleLocales(data.VehicleName.Key, localizations[lang]) ||
          data.VehicleName2.Texts.map(
            (t) =>
              getVehicleLocales(t.Key, localizations[lang]) ||
              t.CultureInvariantString
          ).join(" ") ||
          data.VehicleName.CultureInvariantString;

        names[lang] = localized;
      }
    });

    const [vehicleClassPath, vehicleClassIndex] =
      data.VehicleClass.ObjectPath.split(".");

    const vehicleClassFilePath = path.join(
      __dirname,
      vehicleClassPath + ".json"
    );

    const vehicleClassFile = JSON.parse(
      fs.readFileSync(vehicleClassFilePath, "utf8")
    );

    const vehicleClass = vehicleClassFile[+vehicleClassIndex];

    const classDefaultObjIndex =
      vehicleClass.ClassDefaultObject.ObjectPath.split(".")[1];

    const classDefaultObj = vehicleClassFile[+classDefaultObjIndex];

    const weight = vehicleClassFile.reduce((acc, curr) => {
      return acc + (curr.Properties?.BodyInstance?.MassInKgOverride || 0);
    }, 0);

    const wheels = vehicleClassFile
      .filter((item) => item.Type === "MHWheelComponent")
      .sort((a, b) => a.Name.charAt(5) - b.Name.charAt(5));

    const axles = Array.from({ length: wheels.length / 2 }, () => ({
      lift: false,
      dual: false,
      driven: false,
      breakRatio: 0,
    }));

    wheels.forEach((wheel, i) => {
      const axleIndex = Math.floor(i / 2);
      axles[axleIndex].dual =
        wheel.Properties?.WheelFlags?.includes(
          "EMTWheelFlags::DualRearWheel"
        ) || false;
      axles[axleIndex].driven =
        wheel.Properties?.DifferentialComponentName != null;
      axles[axleIndex].breakRatio += wheel.Properties?.BrakeRatio || 0;
    });

    classDefaultObj.Properties?.LiftAxles?.forEach((liftAxle) => {
      liftAxle.WheelIndexToHeight.forEach((wheel) => {
        const axleIndex = Math.floor(wheel.Key / 2);
        if (wheel.Value > 0) {
          axles[axleIndex].lift = true;
        }
      });
    });

    const seats = vehicleClassFile.filter(
      (item) => item.Type === "MTSeatComponent"
    ).length;

    let engineFile;

    const engine = data.Parts.find(
      (part) => part.Key === "EMTVehiclePartSlot::Engine"
    )?.Value;

    if (engine) {
      const enginePath =
        engineData[engine].EngineAsset.ObjectPath.split(".")[0];

      engineFile = JSON.parse(
        fs.readFileSync(path.join(__dirname, enginePath + ".json"), "utf8")
      );
    }

    return {
      key,
      slug:
        key === "Bongo_Bus" || key === "Nimo_Taxi"
          ? key.toLowerCase().replace(/\s+/g, "_").replace(/-/g, "_")
          : (names.en || enVehicleName)
              .toLowerCase()
              .replace(/\s+/g, "_")
              .replace(/-/g, "_"),
      name: names,
      cost: data.Cost,
      comfort: data.Comport,
      fuel: engineFile
        ? {
            cap: classDefaultObj.Properties.FuelTankCapacityInLiter ?? 50,
            type:
              engineFile[0].Properties.EngineProperty.FuelType?.split(
                "::"
              )[1] ?? "Gasoline",
          }
        : null,
      drag: classDefaultObj.Properties.AirDragCoeff ?? 1,
      weight: weight,
      type: [
        data.VehicleType.split("::")[1],
        data.TruckClass.split("::")[1],
      ].filter((t) => t !== "None"),
      seats: seats,
      level: {
        driver: data.LevelRequirementToDrive.find(
          ({ Key }) => Key === "CL_Driver"
        )?.Value,
        taxi: data.LevelRequirementToDrive.find(({ Key }) => Key === "CL_Taxi")
          ?.Value,
        bus: data.LevelRequirementToDrive.find(({ Key }) => Key === "CL_Bus")
          ?.Value,
        truck: data.LevelRequirementToDrive.find(
          ({ Key }) => Key === "CL_Truck"
        )?.Value,
        racer: data.LevelRequirementToDrive.find(
          ({ Key }) => Key === "CL_Racer"
        )?.Value,
        wrecker: data.LevelRequirementToDrive.find(
          ({ Key }) => Key === "CL_Wrecker"
        )?.Value,
        police: data.LevelRequirementToDrive.find(
          ({ Key }) => Key === "CL_Police"
        )?.Value,
      },
      axles: axles,
    };
  }),
  null,
  2
);

// Write result to vehicles.json
fs.writeFileSync("vehicles.json", result, "utf8");
console.log("Vehicle names extracted and saved to vehicles.json");
