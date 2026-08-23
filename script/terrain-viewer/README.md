# terrain-viewer

A Three.js viewer with Leaflet/OpenLayers-style tiled 3D terrain (plain Vite dev server,
no bundler config beyond defaults): a quadtree of terrain patches, built from the
`heightmap` project's height tile pyramid and textured with `amc-web`'s matching color
tile pyramid, switches to smaller (higher-zoom) tiles near the camera and larger
(lower-zoom) tiles far away - see "Tiled LOD terrain" below for the design.

The two pyramids have independent origins in the pak (one is a baked texture, the other
is live vertex data) - there is no metadata anywhere that states they share a coordinate
frame. `heightmap/Options.cs`'s default `--origin-x -1280000 --origin-y -320000
--map-size 2200000` was chosen to match the game's own map (see
`.agents/knowledge/landscape-heightmap.md`'s "Native resolution" section), and this
viewer assumes `amc-web`'s map tiles cover that same rectangle. **This assumption is
empirically confirmed**, not just asserted: rendering the two together shows the desert
plateau / rugged mountain / forested-hills color regions land exactly on the matching
terrain relief (flat high plateau under the tan desert color, jagged peaks under the
reddish "Outback" rock texture, rolling hills under the green forest color), and the
small offshore islands appear at the correct relative position and scale. If a future
map/game update changes either extraction's default extent, re-verify visually the same
way before trusting the drape.

## Build + run

```bash
# 1. Generate the heightmap, including its tile pyramid (skip if out/heightmap/ is
#    already up to date; --tile-size/--max-zoom default to 256/4, matching amc-web's
#    own native zoom for its 4096px map - override only if that ever changes)
dotnet run -c Release --project heightmap -- [--tile-size <px>] [--max-zoom <n>]

# 2. Generate amc-web's color tile pyramid (tiles are NOT optional here, unlike the old
#    single-mesh viewer - --skip-tiles would leave nothing to texture the terrain with)
dotnet run -c Release --project amc-web

# 3. One-time: install this project's own deps (three, vite - nothing else)
cd script/terrain-viewer && pnpm install   # or npm install

# 4. Copy both tile pyramids into public/assets/tiles/
node scripts/prepare-assets.js

# 5. Run it
pnpm dev   # http://localhost:5173
```

`scripts/prepare-assets.js` does **no decoding or resampling of its own** - both
pyramids are already generated at the right zoom/resolution scheme in C#
(`heightmap/ImageWriter.cs`'s `WriteHeightTiles`, `amc-web/TileGenerator.cs`). The
script is a pure direct-copy build step:

- `tiles/height/<z>_<x>_<y>.bin` - copied from `out/heightmap/tiles/` - raw `uint16`,
  little-endian, `tileSize x tileSize`, still in **raw height units** (not meters).
  `src/main.js` applies the raw-height-to-meters formula client-side
  (`rawHeightToWorldZMeters`), it does not arrive pre-converted.
- `tiles/color/<z>_<x>_<y>.avif` - copied from `out/amc-web/map/tiles/`, only
  `z0..maxZoom` (the height pyramid's own depth) - skips `amc-web`'s own extra upscaled
  level if it generated one, since the height pyramid never has a matching level for it.
- `tiles.json` - a subset/rename passthrough of `Jeju_World.json`'s `"tiles"` object
  (`tileSize`, `maxZoom`, `dtype`, `byteOrder`, `widthMeters`/`heightMeters`,
  `originMetersX`/`originMetersY`, `minZ`/`maxZ` in meters) - no computation, everything
  was already precomputed on the C# side.

This replaced an earlier version that copied one fixed-resolution `heights.bin` and one
flat `map.png` and built a single whole-map `BufferGeometry` - correct, but with no way
to show more detail near the camera without paying for that detail everywhere at once.
`script/lib/png16.js`'s PNG decoder is not used anywhere in this project (only by
`script/get-height.js`, a different use case: point queries against the un-tiled native
data).

## Tiled LOD terrain

`selectLeafTiles()` walks the quadtree from `z0` every ~150ms (`TILE_UPDATE_INTERVAL_MS`
- the walk itself is cheap, but tile load/unload churn on every single frame is not).
At each node it computes the tile's own world size and the camera's distance to the
tile's center; if the camera is closer than `tileWorldSize * REFINE_DISTANCE_FACTOR`
(`1.5`) and the tile hasn't hit `maxZoom` yet, it recurses into the 4 children instead of
rendering that node - the same idea Cesium/OpenLayers-3D/etc. use (subdivide when the
tile would cover too much of the view), simplified here to a single distance ratio since
this viewer has no true screen-space-error budget or camera-FOV-aware projection.

Verified by construction, not just by eye (WebGL context creation fails in this sandbox
environment - see below): the leaf set returned by `selectLeafTiles()` for a given
camera position was checked in Node against a synthetic coverage grid at
`2^maxZoom x 2^maxZoom` resolution - every cell is covered by exactly one leaf tile (no
gaps, no overlaps) at every tested camera position (far outside the map, at the map
center, at a map corner), the total leaf area always equals the map's real area exactly,
`maxZoom` is never exceeded, and refinement concentrates around the camera's actual
position (e.g. camera at the exact map corner selects that corner's own `z4` tile as a
leaf, while the far corner stays a single coarse `z2`/`z3` tile). Every one of the 341
`(z, x, y)` positions the quadtree can ever reach (`z0` through `z4`) was confirmed to
have both a height tile and a color tile actually present under `public/assets/tiles/` -
zero possible 404s from the LOD system's own tile requests.

Each visible tile is its own small `BufferGeometry` (`MESH_RESOLUTION x MESH_RESOLUTION`
= `65x65` vertices, independent of the tile's own raw data resolution - fixed density
regardless of zoom, so total scene triangle count is bounded by how many leaf tiles are
currently selected, not by the underlying `256x256` height data) and its own
`MeshStandardMaterial` textured with that tile's color `.avif`. `heightCache` (keyed by
`z_x_y`) avoids re-fetching a height tile's raw bytes if the camera revisits it; loaded
tile meshes are tracked in `activeTiles` and disposed (geometry, material, texture) the
moment they leave the desired leaf set, so tile churn doesn't leak GPU memory over a long
session.

### LOD-boundary cracks: skirts, not seamless stitching

Neighboring tiles at different zoom levels sample the native heightmap at different
resolutions, so their shared edge rarely lines up vertex-for-vertex - a classic
quadtree-terrain crack (a proper fix requires stitching matching edge resolutions or a
shared skeleton, real engineering for another day). `buildTileGeometry()` instead adds a
"skirt": every border vertex gets a mirrored twin, `SKIRT_DROP` (`400` world units)
lower, connected by a thin wall of extra triangles running all the way around the tile.
This doesn't close the geometric gap - it hides it behind a wall deep enough that any
crack reads as a shadowed seam instead of a hole punched through the terrain. Winding on
the four skirt walls wasn't rigorously derived (four different edge orientations); the
tile material is `side: THREE.DoubleSide` specifically so an inverted skirt triangle
still renders instead of being backface-culled into a visible gap of its own.

The exaggeration slider (see below) recomputes every active tile's vertex `Y` in place
(`applyExaggeration()`, using each vertex's stored pre-exaggeration `baseHeights` meters
and per-vertex `skirtDrop` - `0` for main-grid vertices, `SKIRT_DROP` for skirt
vertices) - no re-fetch, no re-sampling, and skirts stay `SKIRT_DROP` world units below
their main-grid twin at any exaggeration level, since the drop is applied *after*
exaggeration rather than scaled by it.

### WebGL verification note

This sandbox's headless Chromium fails `Error creating WebGL context` at
`new THREE.WebGLRenderer()` - reproduced before any of this feature's code runs, and
persists across full browser/tab restarts - so the render output itself could not be
screenshotted here. Everything checkable without a live WebGL context was checked (see
above): the tile pyramids' data (spot-checked against the native `heights.bin` at every
zoom level), the quadtree selection algorithm (partition correctness, distance-based
behavior, `maxZoom` clamping), and asset completeness (every reachable tile file
exists). A `vite build` of the full bundle succeeds cleanly. Visually confirm the actual
render (does refinement track the camera, do skirts hide seams acceptably) in a normal
browser before relying on this for anything beyond internal review.

## Self-shadowing terrain

The terrain both casts and receives shadows from the sun `DirectionalLight`
(every tile mesh gets `castShadow = receiveShadow = true` in `loadTile()`,
`renderer.shadowMap.enabled = true`, `THREE.PCFSoftShadowMap`) - mountains cast real
shadows into neighbouring valleys instead
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
pre-fix render. The tiled LOD renderer applies the identical fix per-tile, in
`loadColorTexture()` - every tile's UVs follow the same un-flipped row/col convention,
so every tile's texture needs the same `flipY = false`.

## Camera controls: left-drag pan (ground-anchored), right-drag orbit

`OrbitControls`' defaults (left-drag rotate, right-drag pan) were swapped to match
conventional 3D-editor/map-tool bindings (`controls.mouseButtons = { LEFT: null, RIGHT:
THREE.MOUSE.ROTATE }`, `enablePan = false`). `enableZoom = false` too - `MIDDLE`-drag and
wheel dolly are both gated by that one flag in `OrbitControls` - zoom is fully custom (see
below). Left-drag is **not** `OrbitControls`' own pan (that translates the camera in its
own screen-space plane, with no relationship to what's actually under the cursor) - it's a
hand-rolled ground-anchored drag: on `pointerdown`, raycast the click against
`tileGroup.children` (every currently-loaded tile mesh, not a plane) to find the exact
grabbed world point, then build a
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
