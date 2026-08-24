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
#    already up to date; --tile-size defaults to 256, --max-zoom to 4 - the latter
#    matches amc-web's own native zoom for its 4096px map, override only if that ever
#    changes; --tile-size is independent of amc-web's own and can be any resolution -
#    tried 512 once, reverted, no visible benefit over 256 for this map)
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
  little-endian, `tileSampleCount x tileSampleCount` (`tileSize+3` - see "1px border
  overlap" and "Normal continuity" below), still in **raw height units** (not meters).
  `src/main.js` applies the raw-height-to-meters formula client-side
  (`rawHeightToWorldZMeters`), it does not arrive pre-converted.
- `tiles/color/<z>_<x>_<y>.avif` - copied from `out/amc-web/map/tiles/`, only
  `z0..maxZoom` (the height pyramid's own depth) - skips `amc-web`'s own extra upscaled
  level if it generated one, since the height pyramid never has a matching level for it.
- `tiles.json` - a subset/rename passthrough of `Jeju_World.json`'s `"tiles"` object
  (`tileSize`, `tileSampleCount`, `tileSampleInnerCount`, `maxZoom`, `dtype`,
  `byteOrder`, `widthMeters`/`heightMeters`, `originMetersX`/`originMetersY`,
  `minZ`/`maxZ` in meters) - no computation, everything was already precomputed on the
  C# side.

This replaced an earlier version that copied one fixed-resolution `heights.bin` and one
flat `map.png` and built a single whole-map `BufferGeometry` - correct, but with no way
to show more detail near the camera without paying for that detail everywhere at once.
`script/lib/png16.js`'s PNG decoder is not used anywhere in this project (only by
`script/get-height.js`, a different use case: point queries against the un-tiled native
data).

### 1px border overlap + 1px normal halo: fixing two different seam bugs

Each height tile file actually stores `(tileSize+3) x (tileSize+3)` samples
(`tileSampleCount` in `tiles.json`), not `tileSize x tileSize`. That's two extra layers,
fixing two genuinely different bugs found via real browser use, both reported as
"seams between tile":

- **Position seam (1px border overlap, `tileSampleInnerCount` = `tileSize+1`)**: two
  adjacent tiles' shared edge reads from the exact same underlying canvas pixel on both
  sides. Without it, tile A's "last" column (canvas column `(tx+1)*tileSize - 1`) and
  tile B's "first" column (canvas column `(tx+1)*tileSize`) are neighbouring but
  genuinely *different* source pixels, even though the mesh places both at the exact
  same world X - a real height mismatch at what's treated as one shared position.
  Verified by construction: every sampled shared edge between adjacent same-zoom tiles
  matches exactly (0 mismatches across 257 samples at 4 tested boundary pairs spanning
  z2-z4).
- **Normal seam (1px normal halo, one more sample beyond the border-overlap edge)**:
  see "Normal continuity" below - a real, separate bug that survives the position fix
  above, because it's about *lighting*, not position.

Both are a **different** class of bug from the different-zoom cracks skirts cover (see
below) - they happen even between two same-zoom tiles, and skirts don't touch either of
them. The outermost edge of the whole pyramid (no real neighbour to share with) clamps
every extra sample to the last valid canvas pixel, harmlessly duplicating it.

### Normal continuity: fixing a lighting seam that survives the position fix

The border-overlap fix above makes adjacent tiles agree on vertex *position* at their
shared edge, but each tile is its own separate `BufferGeometry` - naive
`computeVertexNormals()` derives a boundary vertex's normal only from *this tile's own*
triangles, systematically skewing it toward this tile's interior. The neighbouring
tile's copy of that same world vertex gets skewed the *other* way, and two different
normals at a shared point read as a lighting-discontinuity seam even when the position
data matches exactly - reported as "seam is still a problem" even after the
border-overlap fix landed, since that fix only ever addressed position, not shading.

Fix: `buildTileGeometry()` no longer calls `computeVertexNormals()` at all. Instead it
computes each vertex's normal analytically from the height field via a central finite
difference (`normalize(-dh/dx, 1, -dh/dz)` - sign convention verified against the
mesh's actual triangle winding, not assumed) using one extra "halo" sample beyond each
tile edge (`heightmap/ImageWriter.cs`'s `WriteHeightTiles`, `tileSampleCount` =
`tileSampleInnerCount + 2`). That halo sample means a central difference at a boundary
vertex is always well-defined from data this tile alone stores - and because both tiles
derive that halo sample from the exact same deterministic area-average of the same
canvas pixel, the two independently-computed edge normals come out **bit-identical**,
not just close. Verified numerically in Node (re-implementing the exact formula against
the real generated tile files, not a synthetic model): 0 difference across every vertex
on 2 tested same-zoom boundary pairs (65 vertices each). Normals are computed once, at
build time, alongside positions - heights render at true 1:1 world scale, with no
exaggeration factor anywhere in this pipeline to recompute them for.

## Tiled LOD terrain

`selectLeafTiles()` walks the quadtree from `z0` every ~150ms (`TILE_UPDATE_INTERVAL_MS`
- the walk itself is cheap, but tile load/unload churn on every single frame is not).
At each node it computes the camera's full 3D distance to the *closest point of the
tile's own bounding box* (`THREE.Box3.distanceToPoint` - the same box already built for
frustum culling, reused rather than rebuilt), projects that distance and the tile's own
world size through the camera's actual vertical FOV and the renderer's current viewport
height into an on-screen pixel size (`projectedScreenSizePx()`), and recurses into the
4 children instead of rendering the node once that projected size crosses
`MAX_TILE_SCREEN_PX` (`900`) - a real screen-space-error budget, the same idea Cesium/
OpenLayers-3D/etc. use.

**Two bugs found via real browser use, neither caught by the Node-side partition
tests** (a partition/coverage test only checks that leaves tile the map exactly once,
not *which* leaves get picked - both bugs are in the metric feeding the algorithm, not
the algorithm's coverage logic):

- The first version measured distance as a flat horizontal (XZ-plane only) distance to
  the tile's center, completely ignoring camera altitude. Looking mostly straight down
  while fully zoomed out puts the camera's XZ position almost exactly above whatever
  tile is underneath it, so that tile's horizontal distance reads as ~0 regardless of
  how far the camera has actually zoomed out in true 3D - forcing max-zoom refinement
  right under the camera no matter what. Reported as "tile level 5 [`z4`, the 5th zoom
  level counting from `z0`] still got selected when I zoom out fully". Fixed by
  measuring true 3D distance to the tile's bounding box instead of a 2D projection to
  its center.
- The second version fixed the distance metric but still used a flat
  `distance < tileWorldSize * REFINE_DISTANCE_FACTOR` ratio (`REFINE_DISTANCE_FACTOR =
  1.5`) to decide when to refine - a fixed ratio like that has no relationship to what's
  actually on screen: the same real-world camera distance covers very different screen
  area depending on FOV and window size, so the threshold was inherently too eager on a
  typical FOV/window, not just mistuned. Reported as "zoom level 5 still render from
  quite far" even after the first fix landed. Fixed by replacing the ratio with the
  screen-space-error budget described above - deliberately more conservative than the
  old ratio (`MAX_TILE_SCREEN_PX = 900` vs. the old ratio's roughly-900px-lower
  equivalent at a typical viewport - see `projectedScreenSizePx()`'s doc comment for the
  derivation), verified in Node to be uniformly ~1.5x less eager to refine at *every*
  zoom level (not just the one level it happened to be tuned against), while still
  partitioning the map exactly once per leaf at every tested camera position.
- The third bug survived *both* fixes above, hiding in the frustum-culling box that
  the fixed 3D-box-distance metric was reusing: that box's vertical range was padded
  by a large, fixed margin (`CULL_MAX_EXAGGERATION = 40`, since removed along with the
  exaggeration slider that margin existed for) so culling would stay correct if the
  slider was cranked up later. That same oversized box was tall enough (thousands of
  world units) to contain the camera's own altitude in almost any normal viewing
  position, silently collapsing `box.distanceToPoint`'s vertical component back down
  to 0 - reproducing the *exact* "ignores camera altitude" bug the first fix above
  was meant to solve, just through a new code path. Reported as "zoom level 5 still
  render from pretty far away" even after both earlier fixes, and confirmed (not just
  theorized) with the color-by-zoom debug view showing the finest zoom selected
  directly under a camera that was clearly still high above the terrain in the
  screenshot. Fixed by removing the exaggeration slider entirely - with
  only one true 1:1 scale left, the box can just use the map's real height range
  (`meta.minZ`/`meta.maxZ`, no margin), which is naturally tight enough to make the
  vertical distance term meaningful again, for both culling and refinement.

Every one of the 341 `(z, x, y)` positions the quadtree can ever reach (`z0` through
`z4`) was confirmed to have both a height tile and a color tile actually present under
`public/assets/tiles/` - zero possible 404s from the LOD system's own tile requests.

Each visible tile is its own small `BufferGeometry` (`MESH_RESOLUTION x MESH_RESOLUTION`
= `65x65` vertices, independent of the tile's own raw data resolution - fixed density
regardless of zoom, so total scene triangle count is bounded by how many leaf tiles are
currently selected, not by the underlying raw height tile resolution) and its own
`MeshStandardMaterial` textured with that tile's color `.avif`. `heightCache` (keyed by
`z_x_y`) avoids re-fetching a height tile's raw bytes if the camera revisits it; loaded
tile meshes are tracked in `activeTiles` and disposed (geometry, material, texture) the
moment they leave the desired leaf set, so tile churn doesn't leak GPU memory over a long
session.

### Load/unload ordering: fixing a flash, then a "dark patch" the first fix caused

Reported after real browser use as "flashing problem when tile change zoom level". Root
cause: `updateVisibleTiles()`'s first version unloaded a stale (no-longer-desired) tile
*immediately*, in the same call where its replacement tile(s) were only just queued for
an async load (fetch height bytes, decode the color texture, build the geometry) -
guaranteeing a gap with nothing rendered there for however long that takes. Fixed by
deferring every unload until the *entire* newly-desired tile set is confirmed active
(loaded, not merely pending): `desired.every(([z, x, y]) => activeTiles.has(...))`
gates the unload loop.

That fix introduced a different, worse-looking bug: `loadTile()`'s first version
called `loadColorTexture()` but never awaited the image actually finishing - a
`THREE.TextureLoader` returns a texture object immediately and decodes the image in
the background, so the mesh was being built and marked active (`activeTiles.set()`,
which is exactly what the fix above gates unloading on) *before* its texture had
finished loading, rendering as a blank/dark patch for however long the AVIF took to
decode. Reported as "flashing still problem, now it show as weird dark patch" -
correctly diagnosed as a load-ordering bug, not a rendering bug: "while the zoom level
data is loaded, texture is not loaded yet... it should be fully loaded then the swap
is performed". Fixed by making `loadColorTexture()` return a `Promise` that only
resolves in the loader's `onLoad` callback (once the image has actually decoded), and
awaiting it in `Promise.all(...)` alongside the height fetch in `loadTile()` - the mesh
is now either fully built with a fully-decoded texture, or not added to the scene at
all, never rendered partway through loading. (An earlier attempt at this fix used a
zoom-biased `polygonOffset` to paper over the symptom via depth-test priority instead
of fixing the actual load-ordering bug; removed once the real cause was identified.)

### Frustum culling

`selectLeafTiles()` also builds a `THREE.Frustum` from the camera's current
`projectionMatrix` / `matrixWorldInverse` (calling `camera.updateMatrixWorld()` first,
since this runs outside the render call and can't rely on the renderer having refreshed
it already) and tests every quadtree node's world-space bounding box against it before
recursing or selecting a leaf. A node entirely outside the frustum is skipped outright -
not recursed into, not fetched, not built, not rendered - so panning the camera away
from part of the map stops loading and holding geometry/textures for tiles that are no
longer (or never were) visible, not just relying on Three.js's own per-mesh
`frustumCulled` draw-call skip after the fact. Each node's bounding box uses the map's
real, true-scale vertical range (`meta.minZ`/`meta.maxZ`) - the same range the
refine-distance check now uses too (see "Tiled LOD terrain"'s third bug above for why
that box must stay tight, not generously padded).

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

Heights render at true 1:1 world scale everywhere - there's no exaggeration slider or
per-frame recompute to keep in sync; a tile's geometry (skirts included) is built once
and never changes afterward.

### Ocean quad

A single huge flat `THREE.PlaneGeometry` (`OCEAN_QUAD_SIZE = 200000` world units, far
past both the ~22km map and the camera's own far clip plane) sits at the pak's own
ocean level - see `.agents/knowledge/landscape-heightmap.md`'s "Ocean level" section for
where `oceanLevelMeters` comes from (`MTOceanConfig.OceanConfig.OceanLevel`,
cross-verified against `WaterBodyOcean`'s own transform) and how `tiles.json` carries
it through. Colored/opacity-tuned for clarity, not the first version's darker, more
opaque look (`color: 0x1c5f8a, opacity: 0.85` -> `color: 0x2f7fa8, opacity: 0.45`) -
reported as "make water clearer". Sits at the map's true, unexaggerated ocean level -
no exaggeration slider to keep it in sync with anymore.

**Known limitation, not fixed by the clarity tweak**: real in-game bridges (an elevated
road deck spanning open water) have no separate geometry here - only the landscape
heightmap (which is genuinely submerged under the bridge, since the terrain itself dips
for the water) and this flat sea-level quad. A road that's actually a bridge in-game
gets draped on the submerged landscape mesh and will still visually read as "under
water" here regardless of how clear the water material is - reported as "some part of
road is under water (aka bridge)". Fixing this for real needs separate bridge/road
actor geometry extracted from the pak (a genuinely new extraction target, not a viewer
tweak) - out of scope here.

Deliberately **not** added to `tileGroup`: the ground-anchored pan raycast only tests
`tileGroup.children`, so panning always grabs the real terrain underneath the water
surface, never the ocean quad itself - dragging near the coast doesn't suddenly anchor
to a flat plane instead of the actual seabed/shoreline geometry.

### Debug view: color-by-zoom + wireframe

Two independent checkboxes, top-right. Requested directly to actually diagnose a
reported seam instead of guessing at a cause ("make debug view that color each zoom
level as different color, don't jump to avif conclusion"):

- **color by zoom**: replaces every active tile's real texture with one of
  `ZOOM_DEBUG_COLORS` (5 distinct, maximally-separated colors, one per zoom `z0`-`z4`)
  - shaded (`MeshStandardMaterial`, same `roughness`/`metalness` as the real tile
  material) rather than flat/unlit, so terrain relief and any normal-continuity seam
  (a lighting discontinuity) stay visible in this view too, not just the flat
  zoom-level color itself. Answers directly: is a given seam between two
  *different*-zoom tiles (a color change right at the seam) or two *same*-zoom tiles
  (identical color on both sides, so it can't be an LOD-stitching artifact at all)?
- **wireframe**: applies on top of whichever material is currently showing (real or
  debug-colored) - shows the actual `MESH_RESOLUTION x MESH_RESOLUTION` triangle grid.
  A real geometric gap between two tiles shows as an actual break in the wireframe; a
  seam with no such break, even up close, is a shading-only (normal/lighting)
  discontinuity, not a position crack - the two have different causes and different
  fixes (see "Normal continuity" and "LOD-boundary cracks" above).

Debug materials (5 total, one per zoom, shared by every tile at that zoom) are built
once and toggled per-tile rather than rebuilt - `unloadTile()` only ever disposes a
tile's own real `MeshStandardMaterial`, never one of the 5 shared debug materials.
Both checkboxes apply immediately to every currently active tile and to every tile
loaded afterward, independent of each other and of which tile the camera is looking at.

**Result: confirmed AVIF, not geometry/lighting.** With color-by-zoom on (no texture
at all, real shading still active) the seam wasn't visible even at a same-zoom
boundary - ruling out both the position-stitching and normal-continuity code entirely.
Root-caused with real pixel evidence, not another guess: `amc-web/TileGenerator.cs`
resizes the *whole* map once per zoom, then crops individual tiles as plain array
slices from that one already-resized canvas - so two adjacent same-zoom tiles'
*source* pixels at their shared edge are byte-identical before encoding, confirmed by
reading the code, not assumed. Decoded two real adjacent tiles' actual AVIF output in
a browser tab (`createImageBitmap`/`<canvas>.getImageData`, comparing every row along
their shared edge) and found real, substantial disagreement anyway - up to 92 (summed
R+G+B delta) at the worst row, 11.1 average across the whole 256px edge, entirely from
independent per-tile AVIF encoding (`Options.cs`'s default `Quality = 65`, `Effort =
9`, each tile compressed as its own standalone lossy image with zero knowledge of its
neighbour's pixels beyond the edge).

Tried raising the encode quality (`--quality 90` vs. the default `65`) and re-ran the
identical pixel comparison against the regenerated tiles: only a marginal improvement
(worst-row delta 92 -> 77, average 11.1 -> 9.76) - confirming this is a fundamental
property of compressing each tile independently and lossily, not a quality-tuning
problem with an easy knob. Abandoned pursuing a real fix here: closing it for real
would need either a lossless tile format (a real file-size/generation-time regression
for `amc-web`'s *other* consumers too, e.g. the wiki map viewer - not a call to make
unilaterally) or baking a border-overlap scheme into `amc-web`'s own tile pyramid (the
same fix already applied to the *height* pyramid, mirrored onto a shared resource used
well beyond this one viewer - a real, separate feature, not a quick tweak). Tiles were
regenerated back to the documented default quality (`65`) afterward, so this
investigation left no drift from `amc-web/Options.cs`'s committed defaults.

### WebGL verification note

This sandbox's own headless Chromium fails `Error creating WebGL context` at
`new THREE.WebGLRenderer()` - reproduced before any of this feature's code runs, and
persists across full browser/tab restarts - so screenshots from *this* environment
aren't possible; real-browser testing (a normal desktop browser, which does have
working WebGL) is the actual source of truth and has already caught bugs the
algorithmic checks below couldn't (the altitude-blind LOD distance metric, the
load/unload flash, the lighting seam at tile boundaries - none are visible in a
partition/coverage/completeness test, only in an actual render). Everything checkable
without a live WebGL context was also checked, as a second line of defense: the tile
pyramids' data (spot-checked against the native `heights.bin` at every zoom level, and
the halo-based normal formula re-implemented in Node against the real generated tile
files - 0 difference at every tested boundary vertex), the quadtree selection algorithm
(partition correctness, distance-based behavior, `maxZoom` clamping), frustum culling
(a hand-rolled 2D wedge-intersection sanity check in Node confirmed a narrower view
cone selects strictly fewer tiles, and a camera looking entirely away from the map
selects zero - not a substitute for testing the real `THREE.Frustum`/matrix code, which
needs a browser), the ocean quad's Y math (confirmed `oceanLevelMeters` sits strictly
between the raw height floor and the highest observed peak, so terrain never crosses
to the wrong side of the water plane), and asset completeness (every reachable tile file
exists). A `vite build` of the full bundle succeeds cleanly. Depth-precision fixes
(`logarithmicDepthBuffer`, the raised near plane) and the analytic-normal lighting fix
have no equivalent static check - they can only be confirmed by looking at the actual
render, which is exactly how the underlying bugs (heavy ocean z-fighting, a visible
tile-boundary lighting seam) were originally reported.

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
