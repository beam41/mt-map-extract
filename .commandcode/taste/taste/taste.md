# Taste
- Prefers consolidating separate build/transform scripts (e.g. rust extract + JS transform) into a single unified program rather than maintaining split scripts. Confidence: 0.8
- After consolidating/replacing functionality, prefers old scripts and dead code removed cleanly, not left behind. Confidence: 0.8
- Prefers not generating artifact files the user didn't ask for (e.g. no `_raw` output); only produce the requested outputs. Confidence: 0.7
- Prefers program options to be configurable via a YAML file. Confidence: 0.7
- Prefers normalizing output data to a consistent PascalCase automatically ("no need for separate flag") rather than requiring an extra option for correct casing. Confidence: 0.7
- Favors avoiding extra flags when a behavior should just be the correct/default one — keep the flag surface minimal. Confidence: 0.7
- Prefers explicit, conventional default output paths (e.g. `map.png` in the output folder). Confidence: 0.6
