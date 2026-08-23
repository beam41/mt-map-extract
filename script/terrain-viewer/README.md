# terrain-viewer

A Three.js viewer (plain Vite dev server, no bundler config beyond defaults) that drapes
`out/amc-web/map/map.png` (the pre-baked in-game minimap texture, decoded straight from
`T_WorldMap_Jeju` with no world-bounds metadata of its own - see `amc-web/Program.cs`'s
`DecodeMapTexture`) over a mesh displaced by the `heightmap` project's extracted
elevation data. The two assets have independent origins in the pak (one is a baked
texture, the other is live vertex data) - there is no metadata anywhere that states they
share a coordinate frame. `heightmap/Options.cs`'s default `--origin-x -1280000
--origin-y -320000 --map-size 2200000` was chosen to match the game's own map (see
`.agents/knowledge/landscape-heightmap.md`'s "Native resolution" section), and this
viewer assumes `map.png` covers that same rectangle. **This assumption is empirically
confirmed**, not just asserted: rendering the two together shows the desert plateau /
rugged mountain / forested-hills color regions in `map.png` land exactly on the matching
terrain relief (flat high plateau under the tan desert color, jagged peaks under the
reddish "Outback" rock texture, rolling hills under the green forest color), and the
small offshore islands appear at the correct relative position and scale. If a future
map/game update changes either extraction's default extent, re-verify visually the same
way before trusting the drape.

## Build + run

```bash
# 1. Generate the heightmap, including the web-optimized downsample (skip if
#    out/heightmap/ is already up to date; --web-size defaults to 512, override if you
#    need a different resolution)
dotnet run -c Release --project heightmap -- [--web-size <n>]

# 2. Generate map.png (skip tiles - the viewer only needs the flat PNG)
dotnet run -c Release --project amc-web -- --skip-tiles

# 3. One-time: install this project's own deps (three, vite - nothing else)
cd script/terrain-viewer && pnpm install   # or npm install

# 4. Copy the two outputs above into public/assets/
node scripts/prepare-assets.js

# 5. Run it
pnpm dev   # http://localhost:5173
```

`scripts/prepare-assets.js` does **no decoding or resampling of its own** - all of that
now happens once, in C#, at generation time (`heightmap/ImageWriter.cs`'s
`WriteWebHeightsBin`, the exact same area-average-pooling algorithm this script used to
run in JS against the full native PNG on every build). The script is a pure direct-copy
build step:

- `heights.bin` - a byte-for-byte copy of `out/heightmap/heights_<n>px.bin` (raw
  `uint16`, little-endian, row-major, `n x n` per `--web-size`, still in **raw height
  units** - not meters). `src/main.js` applies the raw-height-to-meters formula
  client-side (`rawHeightToWorldZMeters`), it does not arrive pre-converted.
- `heights.json` - a subset/rename passthrough of `Jeju_World.json`'s `"web"` object
  (`grid`, `dtype`, `byteOrder`, `widthMeters`/`heightMeters`,
  `originMetersX`/`originMetersY`, `minZ`/`maxZ` in meters) - no computation, everything
  was already precomputed on the C# side.
- `map.png` - a plain copy of `out/amc-web/map/map.png`, used as-is for the texture; no
  per-pixel correspondence with the heightmap grid is needed, it's draped over the mesh
  purely via UVs (`col/(grid-1), row/(grid-1)`, matching the same row/col orientation used
  to place each vertex).

This replaced an earlier version that itself decoded the full native
`Jeju_World_heightmap16.png` (~80MB, `zlib.inflateSync` via `script/lib/png16.js`) and
downsampled it in JS on every asset-prep run - correct, but redundant work repeated in
two languages. `script/lib/png16.js`'s PNG decoder is no longer used anywhere in this
project (still used by `script/get-height.js`, which reads the un-downsampled
`heights.bin`/native PNG directly, a different use case).

`src/main.js` builds one `BufferGeometry` (a `grid x grid` vertex plane, two triangles
per quad) centered on the origin, applies a **vertical exaggeration** (default `6x`,
live-adjustable via the on-screen slider, `1x`-`40x`) to the Y (height) coordinate only -
real elevation differences here are a few tens of meters across a 22km-wide map, visually
flat at true 1:1 scale, so the exaggeration is a deliberate, disclosed rendering choice,
not a measurement. `MeshStandardMaterial` + a `HemisphereLight` + `AmbientLight` + one
`DirectionalLight` ("sun") light the relief with real shading instead of a flat color
texture; a light sky-blue background/fog (not an initial near-black void) reads as
daylight rather than looking underlit.

No CDN dependencies: `three` and `vite` are installed into `node_modules/` (gitignored)
and bundled/served locally; nothing is fetched from a CDN at runtime.

## Self-shadowing terrain

The terrain both casts and receives shadows from the sun `DirectionalLight`
(`terrain.castShadow = terrain.receiveShadow = true`, `renderer.shadowMap.enabled = true`,
`THREE.PCFSoftShadowMap`) - mountains cast real shadows into neighbouring valleys instead
of every slope's brightness coming from its normal alone. The light's orthographic
shadow camera is sized once, generously, from real terrain extent: a bounding-sphere
radius covering the full horizontal footprint (`widthMeters`/`heightMeters`) plus the
tallest possible vertical extent at the **slider's maximum** exaggeration (`40x` -
`Math.max(|minZ|, |maxZ|) * 40`), not the current slider value, so cranking the
exaggeration slider live never clips the shadow frustum. The sun is placed at
`2 x shadowRadius` along its direction vector specifically so the shadow camera's
near/far span (centered on that light-to-target distance) stays comfortably positive -
placed too close, the bounding sphere would swallow the light's own position and produce
a degenerate (negative-near) frustum.

This is a single, un-cascaded shadow map (`4096x4096`) spanning the whole ~22km map, so
resolution is coarse by construction (low tens of meters per texel) - acceptable for a
large-scale relief viewer, not fine enough for e.g. crisp building shadows. `normalBias
= 15` (world units/meters, comparable to one texel) offsets the sampled depth along the
surface normal rather than a flat depth bias, which holds up far better against
self-shadowing acne on sloped terrain at this resolution than a small constant bias
would.

## Texture-vs-geometry north/south flip gotcha

Initial version showed the terrain relief correctly oriented (verified independently
against known geography - see below) but `map.png` mirrored on the north/south axis only.
Root cause: `THREE.TextureLoader` defaults `texture.flipY = true` (it assumes a plane's
*default* generated UVs, which put `v=0` at the bottom), but this mesh's UVs are assigned
by hand (`v = row/(grid-1)`) to directly match the heightmap's own un-flipped
`row -> worldY` mapping (row 0 = `originCm.Y`, confirmed in `LandscapeExtractor.Stitch` -
`py` is used as the pixel row with no flip). The vertex positions used that same raw row
value and were therefore correct; the texture got an extra, uncoordinated flip on top.
Fix: `texture.flipY = false` right after creating the `TextureLoader` texture, so both the
geometry and the texture read `map.png` in the same top-down row order. Only the V axis is
affected - `THREE.Texture` has no equivalent automatic U-axis flip, and none was needed
here (world X and image column both increase together with no flip anywhere in the
pipeline).

Confirmed fixed by geography, not just by eye: `Outback` (the *main southern landmass*,
containing the desert-plateau material - see `.agents/knowledge/landscape-heightmap.md`)
now renders near the camera in a default south-facing framing, with the northern island
(`88AA0DB8`, has a lake) correctly appearing far in the background - the reverse of the
pre-fix render.

## Camera controls: left-drag pan (ground-anchored), right-drag orbit

`OrbitControls`' defaults (left-drag rotate, right-drag pan) were swapped to match
conventional 3D-editor/map-tool bindings (`controls.mouseButtons = { LEFT: null, RIGHT:
THREE.MOUSE.ROTATE }`, `enablePan = false`). `enableZoom = false` too - `MIDDLE`-drag and
wheel dolly are both gated by that one flag in `OrbitControls` - zoom is fully custom (see
below). Left-drag is **not** `OrbitControls`' own pan (that translates the camera in its
own screen-space plane, with no relationship to what's actually under the cursor) - it's a
hand-rolled ground-anchored drag: on `pointerdown`, raycast the click against the
`terrain` mesh itself (not a plane) to find the exact grabbed world point, then build a
horizontal `THREE.Plane` through it. On every `pointermove` for the rest of that drag,
raycast the *current* mouse position against that same fixed plane using the *current*
(already-moved) camera pose, and translate `camera.position` and `controls.target` by
`grabbedPoint - currentIntersection`. This is exact, not approximate: translating the
camera without rotating it shifts every ray by the same rigid delta, so a fixed plane's
intersection shifts by that same delta - after each step the grabbed point is, by
construction, exactly back under the cursor. The plane is only recomputed at drag start
(from the real terrain height there), so relief crossed *during* one drag doesn't
re-anchor mid-gesture - matching how Google Earth/CAD-style ground-drag panning behaves
elsewhere.

Both the pan and the zoom below mutate `camera.position`/`controls.target` directly
rather than going through `OrbitControls`' rotate/pan/dolly API. This is safe: `update()`
re-derives its internal spherical coordinates from the object's *current* position and
target at the top of every call (`OrbitControls.js`, `_spherical.setFromVector3` off
`position - target`), rather than treating them as owned state that could stomp an
external change - confirmed by reading the actual `update()` body in
`node_modules/three/examples/jsm/controls/OrbitControls.js`, not assumed from the public
API surface.

## Zoom: linear velocity, custom range

`controls.minDistance = 30` / `maxDistance = 55000` (world units = meters). Wheel zoom is
fully custom, not `OrbitControls`' default: its built-in dolly is *multiplicative*
(`radius *= scale` per tick), which feels fast when already far out and barely
perceptible when close in. Replaced with a `wheel` listener that changes distance by a
**constant** step per unit of `event.deltaY` (`ZOOM_UNITS_PER_WHEEL_DELTA = 5`),
independent of current distance, then clamps to `[minDistance, maxDistance]`. `scene.fog`
was widened to `(25000, 100000)` to match the larger `maxDistance` - the original
`(9000, 35000)` range fully washed the whole map out to near-solid sky color well before
reaching the old `maxDistance` (40000), which was the actual "why does zooming out this
far look broken" complaint, not the distance limit itself.

## Orbit speed scales with zoom

Rotating the camera by a fixed angle sweeps terrain across the screen much faster,
visually, when the camera is close to the ground than when it's far out (same angular
change, much smaller radius). `animate()` recomputes `controls.rotateSpeed` every frame
from the current `camera.position`-to-`target` distance, linearly interpolated between
`ROTATE_SPEED_NEAR = 0.15` (at `minDistance`) and `ROTATE_SPEED_FAR = 1.0` (at
`maxDistance`) - reading `controls.rotateSpeed` fresh on every internal rotate
computation, so updating it continuously outside of `OrbitControls` is safe.
