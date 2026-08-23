# Jeju_World landscape heightmap extraction

The pak contains real per-vertex terrain elevation data for every landmass in
`Jeju_World`, at native UE Landscape resolution — far higher than the pre-baked minimap
texture `amc-web` decodes for `map.png`. Extracted by the standalone `heightmap` project
(`heightmap/`), which writes to `out/heightmap/`.

**Status**: per-component decode (height formula, LOD0 resolution) and the combined
world-space canvas (compositing bug root-caused and fixed with a max-elevation merge -
see "Combined-canvas compositing" below) are both verified correct. `--tiles` mode (dump
every component's raw texture, unstitched) has no coordinate math at all, for cases where
even that is too much to trust.

## Where the data lives

- `Jeju_World` is UE5.5 World Partition: the actual terrain (`LandscapeComponent`,
  `LandscapeHeightfieldCollisionComponent`, `LandscapeStreamingProxy`) is **not** in the
  always-loaded `Jeju_World.umap` persistent level — it's split across the 5738 per-cell
  `MotorTown/Content/Maps/Jeju/Jeju_World/_Generated_/*.umap` packages (see
  `.agents/knowledge/explore-tool.md`'s World Partition gotcha). `wpscan '^LandscapeComponent$'`
  finds 1464 of them across 335 `LandscapeStreamingProxy` actors.
- Each `LandscapeComponent` covers `ComponentSizeQuads` (255 here, i.e. one subsection,
  256×256 vertices) and carries a `HeightmapTexture` reference — a `Texture2D` export in
  the **same** package, `PF_B8G8R8A8`, sized `(ComponentSizeQuads+1)²` (256×256), one
  texture per component (no shared atlas: `HeightmapScaleBias.Z/W == 0`).
- `SectionBaseX`/`SectionBaseY` on the component are absolute quad coordinates **within
  its own `LandscapeGuid`** — they default to `0` and are omitted from the JSON when `0`
  (must default them, not treat missing as an error).
- There are **5 distinct `Landscape` actors** in the persistent level (find them via
  `wgrep MotorTown/Content/Maps/Jeju/Jeju_World '"Type":"Landscape"'` - note no space
  after the colon, `wgrep` serializes with `Formatting.None`), each with its own
  `LandscapeGuid`, `ActorLabel`, and component count:

  | export | ActorLabel | LandscapeGuid | components | apparent role |
  |---|---|---|---|---|
  | 22273 | `Landscape` (unnamed) | `88AA0DB8-...` | 256 | northern island (has a lake) |
  | 22274 | `Outback` | `89E45B53-...` | 952 | main southern landmass |
  | 22275 | `Gwangjin` | `2CE30675-...` | 144 | region within the southern landmass's bbox |
  | 22276 | `Ara` | `6B256D39-...` | 100 | region within the southern landmass's bbox |
  | 22277 | `OlleSpeedway_Landscape` | `028DB6A7-...` | 12 | small racetrack islet - **confirmed dead in the live game, never loads; excluded by default** |

  `Gwangjin` and `Ara` use the *same* `LandscapeMaterial` (`M_Landscape_Outback`) as the
  main landmass and their world-space bounding boxes fall **entirely inside** `Outback`'s
  - they are regions of the single visible southern landmass (not separate islands); see
  "Combined-canvas compositing" below for how they relate to `Outback`'s own data.

## Height decode (verified)

`FTexture2DMipMap.BulkData.Data` for `PF_B8G8R8A8` is raw, uncompressed, **memory order
B,G,R,A per pixel** (CUE4Parse treats this format as a pass-through "raw format" — no
channel-order tool needed, no need for `CUE4Parse-Conversion`/SkiaSharp at all):

```
height = (data[i*4 + 2] << 8) | data[i*4 + 1]   // R<<8 | G
```

**Verified**, not assumed: decoded height on a real component matched
`LandscapeComponent.CachedLocalBox.Z` (engine-computed at cook time) to 5 decimal places —
`localZ = (height - 32768) / 128.0`, `Min.Z` at `height=0` and `Max.Z` at `height=114` both
matched exactly. (A community `LandscapeExtractor`/`stitch.py` tool for older UE4 games
uses a different 4-channel "RGBE-style" packing — that does **not** apply here; always
verify against `CachedLocalBox` before trusting a decode formula on a new game/version.)

### Is this really LOD0? Mostly, verified per-component

Some UE5 games store landscape heightmaps behind `ULandscapeTextureStorageProviderFactory`
(a compressed/virtualized mip provider) instead of plain bulk data, and/or strip mip 0's
bulk data for streaming, leaving only a lower-res mip resident. CUE4Parse's
`UTexture2D.GetFirstMip()` transparently decompresses the provider case and falls back to
whatever mip *is* resident - it does not guarantee mip 0. **Always check the returned
`mip.SizeX`/`SizeY`**, don't assume 256×256.

Checked across all 1464 components (`--tiles` mode, `manifest.json`):

| LandscapeGuid | components | texture size returned |
|---|---|---|
| `89E45B53-...` (Outback, main landmass) | 952 | 256×256 (full LOD0) |
| `88AA0DB8-...` (northern island) | 256 | 256×256 (full LOD0) |
| `2CE30675-...` (Gwangjin) | 144 | 256×256 (full LOD0) |
| `6B256D39-...` (Ara) | 100 | 256×256 (full LOD0) |
| `028DB6A7-...` (OlleSpeedway islet) | 12 | 64×64 |

99.2% of components return 256×256. The OlleSpeedway islet's 64×64 is **also genuine
LOD0**, not a fallback - cross-checked against its own `ComponentSizeQuads` (63, vs. 255
everywhere else): `mip.SizeX/SizeY` always equals `ComponentSizeQuads + 1` exactly, so
every component here is confirmed full native resolution, just at two different component
sizes. (Still worth checking `mip.SizeX`/`SizeY` per component rather than hardcoding
256, in case a future game/cook does genuinely strip mip 0.)

## World-space transform gotcha

Each `LandscapeStreamingProxy`'s own `RootComponent.RelativeLocation` is **not** the
terrain's world origin — it's a per-proxy WP streaming/culling placeholder that varies
between proxies of the *same* landscape (confirmed: `RelativeScale3D` is uniform
`(200,200,100)` across every proxy of every guid, but `RelativeLocation` differs per
proxy). The real, single world transform lives on the master `Landscape` actor in the
persistent level, matched by `LandscapeGuid`. Confirmed no `RelativeRotation` is set on
any of the 5 master actors (identity rotation), so placement is pure translation:

```
worldX_cm = masterActorLocation.X + SectionBaseX * 200
worldY_cm = masterActorLocation.Y + SectionBaseY * 200
worldZ_cm = masterActorLocation.Z + ((rawHeight - 32768) / 128.0) * 100
```

`masterActorLocation.Z` is `0` for 4 of the 5 landscapes but `-21900cm` for the
OlleSpeedway islet - not safe to assume `Z=0` universally.

## Combined-canvas compositing

Compositing all 5 landscapes onto one world-space canvas (translation-only placement,
confirmed no rotation, uniform `200cm`/quad scale everywhere) produces the correct overall
two-island silhouette, but naively drawing every component in file-scan order (or even a
deliberate "biggest first, first-write-wins" order) left a visible rectangular seam. Root
cause, confirmed by direct data cross-reference (not guessed):

- `Outback` (952 components) is **not** a fully dense grid: laying out its own
  `SectionBaseX/Y` on a 255-quad step grid finds 72 missing cells (out of the 1024 a fully
  dense 32×32 grid would have), clustered into one rectangular hole.
- `Gwangjin` (144 components) and `Ara` (100 components), each placed by its own resolved
  master-actor transform, land on top of that hole **and** spatially overlap a surrounding
  ring of cells where `Outback` already has its own (different) real terrain data.
- Whichever dataset "won" an overlapping pixel under an order-based rule (last-write-wins
  or first-write-wins) created a hard-edged rectangle wherever the two datasets disagreed,
  because the rule had nothing to do with which dataset was actually more plausible there.

**Fix, part 1** (`LandscapeExtractor.Stitch`): merge overlapping landscapes by taking the
**higher elevation at every pixel**, independent of scan/draw order. Correct in both
directions an overlap can occur: it fills a landscape's real gaps with another's data
(anything real beats an unfilled `0`), and it can't carve an implausible dip into terrain
another landscape already placed.

**Fix, part 2 - reverted, was wrong**: initially concluded `Gwangjin` was an "implausible
spike" because its max (`43855`) towers over the *whole-canvas* mean (`~9467`) and
excluded it from the default composite. That comparison was misleading - the whole-canvas
mean is dragged down by the ocean (~60% of the canvas is `0`), so it says nothing about
whether a peak is plausible. Excluding `Gwangjin` didn't fix anything: it just deleted 144
components' worth of real fill data and re-opened `Outback`'s hole as a visible gap.

Redone properly: decoded every `Gwangjin` and `Outback` tile pixel (`--tiles` output) and
compared distributions, not single min/max numbers. `Gwangjin`'s percentiles rise
smoothly (p50 `126`, p90 `23833`, p99 `35715`, p99.9 `40471`, max `44006`) with 124,933
pixels (1.3% of its area) above `35000` - a gradual, natural-looking mountain massif, not
a single anomalous pixel. Cropping and locally re-normalizing that exact region shows an
ordinary peak/ridge with smooth falloff, matching the style of other peaks on the map.
There is no evidence this is bad data. `Gwangjin` (and `Ara`) are back in the default
composite; only the max-elevation merge from part 1 applies, no exclusions by default.
`--exclude-guid` still exists as an escape hatch for the future, but nothing currently
ships in it - don't reach for it again without this kind of full-distribution check first.

**Northern island ("blurry" look)**: this was a false alarm, not a bug - a real island
with one central peak and gentle slopes genuinely renders as a soft gradient blob at
preview resolution; there is no scale mismatch (`ComponentSizeQuads` confirmed `255` like
`Outback`, so no stretch between them).

**`OlleSpeedway_Landscape` (the small islet between the two main islands) - excluded by
default, confirmed against the live game**: initially assumed this was legitimate (real
elevation detail confirmed by decoding raw pixels, correct `HeightmapScaleBias`
`0.015625 = 1/64` matching its own 64×64 texture with no shared-atlas offset, so no decode
bug), just visually flat/rectangular because (a) the full-map preview normalizes against
the whole map's range so a small islet's own narrower range compresses to a gray band, and
(b) this landscape's component grid doesn't taper to sea level at its edges the way
`Outback`'s does, so its true boundary is a hard rectangle in raw elevation data (no
land/water mask is extracted here - see the `LandscapeVisibilityMask` item below). All of
that is still true, but it turned out not to matter: **the user checked the actual live
game and confirmed this landscape never loads in play** - unlike the other 4, it's dead
content left in the pak. It's excluded from the default composite
(`Options.ExcludeGuids`, default `["028DB6A7"]`; override with `--exclude-guid`).

**Lesson**: two outlier-shaped features turned up in this map, and the fix was different
each time - `Gwangjin`'s "spike" was real terrain (verified by decoding the full pixel
distribution) and excluding it was wrong; `OlleSpeedway`'s hard-edged block was real,
correctly-decoded data for content that simply isn't live in the game, and excluding it
was right. Visual weirdness alone never tells you which - it takes a distribution check
(is this a real, gradual landform or a single implausible spike?) for on-pak plausibility,
*and* independent confirmation (checking the actual game) for whether the content is even
live, before deciding to keep or drop a landscape.

`out/heightmap/Jeju_World_heightmap16.png` is trustworthy for both shape and elevation
across the whole map: every *live* landscape is included, merged by a single
order-independent rule (higher elevation wins on overlap); dead content is excluded by
name, not by appearance.

## Native resolution: fixed real-world extent, kept at native quad size

The default canvas is a **fixed real-world extent** (`--origin-x -1280000 --origin-y
-320000 --map-size 2200000`, both islands plus the ocean between them) at native
resolution (one pixel per quad, 200cm each, **no resampling**) - currently
`11000×11000`. That number is `mapSizeCm / 200cm-per-quad`, i.e. a property of the chosen
real-world extent, not of the landscape data itself.

A separate question - "what's the true native resolution of just the actual placed
terrain, with no assumed map size at all?" - has a different, smaller answer: the tight
world bbox of the 4 live landscapes is X `[-1024000, 608000]` cm, Y `[-408000, 1836800]`
cm → **8161 × 11225 vertices** (quad spans 8160 × 11224, **+1** for the vertex count - a
span of N quad-steps needs N+1 vertices to include both endpoints; the first
implementation of this silently clipped the far edge row/column by omitting the `+1`,
caught by cross-checking the printed canvas size against the hand-computed one). Get this
with `--debug-auto-fit`, which ignores `--origin-x`/`--origin-y`/`--map-size` entirely and
fits the canvas tightly to the actual data instead - useful for confirming the true
native resolution, not meant for normal generation (the fixed extent aligns to the game's
own map and divides evenly for tiling; the tight extent generally won't).

## Running it

Default (no flags) writes to `out/heightmap/`:

```bash
dotnet run -c Release --project heightmap --
```

- `Jeju_World_heightmap16.png` — 16-bit grayscale, full native resolution (`11000×11000`
  by default), **no resampling at all**. Pixel value is the raw height (0-65535).
- `Jeju_World.json` — resolution, origin, quad scale, raw height range, formulas,
  `heights.bin`'s exact layout (dtype, byte order, offset formula).
- `heights.bin` — raw `uint16`, little-endian, row-major, same native resolution and
  same raw height values as the PNG - no PNG/zlib decode needed for a single point
  query, just seek to `(row * width + col) * 2` bytes (see `script/get-height.js`, which
  does exactly that). Replaced the old `tiles/t<X>_<Y>.png` grid split - a raw flat array
  is strictly more useful for point queries than a directory of PNG tiles (no tile-size
  math, no per-tile PNG/zlib decode), and the full native PNG already covers the
  "load it all at once in an image viewer" case.
- `tiles/{z}_{x}_{y}.bin` (`--tile-size`/`--max-zoom`, defaults `256`/`4`) — a
  Leaflet/OpenLayers-style tile pyramid, same raw `uint16` little-endian layout as
  `heights.bin`, deliberately using the **same `{z}_{x}_{y}` naming and the same
  zoom-to-resolution scheme as `amc-web/TileGenerator.cs`'s color tiles** (z0 = 1×1
  grid, zN = 2^N × 2^N, `2^N * tileSize` total resolution) - so `script/terrain-viewer`
  can fetch a height tile and a color tile for the same `(z, x, y)` and know they cover
  the exact same world-space rectangle at the exact same sample density. `--max-zoom`
  defaults to `4`, matching `amc-web`'s current *native* zoom for its 4096px map at the
  default tile size (ground-truth-checked against `out/amc-web/map/tiles/`: z0..z4 exist
  with 1/4/16/64/256 tiles respectively, z5's extra 1024 tiles are `amc-web`'s own
  upscaled level) - a manually-kept-in-sync constant, not auto-derived, matching the
  existing `--origin-x`/`--origin-y`/`--map-size` pattern of trusting a verified,
  documented constant over a fragile cross-project runtime dependency. Deliberately
  never generates an upscaled level itself - upscaling raw elevation invents no real
  detail. `Jeju_World.json`'s `"tiles"` object carries every consumer-needed field
  precomputed (`tileSize`, `maxZoom`, `widthMeters`/`heightMeters`,
  `originMetersX`/`originMetersY`, `minZMeters`/`maxZMeters`). Replaced the earlier
  single flat `heights_<n>px.bin` (`--web-size`) export - a real tile pyramid is what
  lets the viewer show coarser/finer detail by camera position instead of one fixed
  resolution for the whole map.
- `debug/Jeju_World_preview.png` — 8-bit min/max-normalized, downscaled so the longer
  edge is `--debug-size` px (default `2048`; aspect ratio preserved, cubic resample) - the
  16-bit file looks almost solid black in ordinary viewers otherwise.

Debug options (`--debug-*`, not needed for normal generation):

```bash
# Isolate one landscape, at whatever the fixed/auto-fit canvas resolves to
dotnet run -c Release --project heightmap -- --debug-guid <substring>

# True native resolution of just the placed data, ignoring --origin-x/-y/--map-size
dotnet run -c Release --project heightmap -- --debug-auto-fit

# Unstitched: every component's own texture as its own PNG, no placement math at all -
# writes debug/tiles/<guid8>_<SectionBaseX>_<SectionBaseY>.png + manifest.json
dotnet run -c Release --project heightmap -- --debug-tiles [--debug-guid <substring>]
```

### Regression: adding `--exclude-guid` silently dropped the `--tiles` case

While adding the `--exclude-guid` switch case to `Options.Parse`, the pre-existing
`--tiles` case got overwritten instead of preserved (`PUT` replaced a range that included
it). Build succeeded (still valid C#) but every `--tiles` invocation then failed with
`unknown option '--tiles'`. Caught by actually re-running `--tiles` afterward, not by the
build passing. **Editing an existing switch/match block: re-read the full block after the
edit and diff case-by-case, don't trust "it compiled" as evidence every case survived.**

Scanning all 5738 cells (~50-80s, dominated by the scan itself, not by decoding) happens
regardless of mode.

### NetVips 16-bit PNG gotcha

`Image.NewFromMemory(ushort[], w, h, 1, Enums.BandFormat.Ushort)` alone is not enough to
get a 16-bit PNG out of `Pngsave` - libvips picks the output bit depth from the image's
**interpretation** tag, not just its band format. Without an explicit interpretation the
saved PNG silently comes out 8-bit (values truncated, not just re-scaled). Fix: `.Copy(
interpretation: Enums.Interpretation.Grey16)` on the raw ushort image before any
resize/save. The inverse bug bites the normalized 8-bit preview: `Linear(..., uchar:
true)` casts the pixel format to `Uchar` but does **not** clear an inherited `Grey16`
interpretation tag, so `Pngsave` still writes 16-bit output and (since the true data only
has 8 significant bits) inflates every value by ~257x on save. Fix: also `.Copy(
interpretation: Enums.Interpretation.Bw)` after the `Linear` call, before saving.

## Sample Z-lookup: `script/get-height.js`

A dependency-free Node.js sample program: given a world `(X, Y)` in cm, prints the
terrain elevation `Z`. Implements the exact lookup a consumer would need: reads
`Jeju_World.json` for origin/quad-scale/`heights.bin` layout, computes the raw byte
offset for the one point requested, and reads exactly 2 bytes from `heights.bin` via
`fs.readSync` against an open file descriptor - never loads the full native array or
decodes any PNG. Verified matching the old PNG-tile-decode path's output exactly at the
same points before the switch (same raw height, same `Z`).

```bash
node script/get-height.js <worldX_cm> <worldY_cm>
```

## 3D web viewer: `script/terrain-viewer/`

A Three.js viewer with Leaflet/OpenLayers-style tiled 3D terrain: it drapes
`out/amc-web/map/map.png`'s color tile pyramid over a quadtree of terrain patches built
from this project's matching height tile pyramid, switching to smaller (higher-zoom)
tiles near the camera and larger (lower-zoom) tiles far away - confirming visually that
the two pyramids share a coordinate frame (desert plateau / mountain / forest color
regions land exactly on the matching relief). See `script/terrain-viewer/README.md` for
the build/run steps, the map/heightmap alignment assumption, the quadtree LOD design
(including the skirt technique for hiding LOD-boundary cracks), and every other gotcha
found building it (texture flipY, custom ground-anchored pan, linear-velocity zoom,
zoom-scaled orbit speed).

## Known limitations / follow-ups

- Height `0` (the minimum representable value) is used both for genuinely low/flat
  terrain (verified via `CachedLocalBox`) and, likely, for World-Partition padding tiles
  outside the sculpted area — this extraction does not distinguish real "hole" flags
  (the `LandscapeVisibilityMask` layer weightmap) from legitimately low terrain.
  - Weightmap textures (`WeightmapTextures`) use the same raw-`PF_B8G8R8A8`-per-pixel
    approach but pack up to 4 layer weights per texture (RGBA channels) — not decoded here.
- ~~No `.r16` raw-array output~~ - resolved: `heights.bin` is exactly that (raw `uint16`,
  little-endian, row-major, native resolution), replacing the old `tiles/t<X>_<Y>.png`
  grid split for point-query use cases.
