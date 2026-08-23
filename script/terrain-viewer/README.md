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
on 2 tested same-zoom boundary pairs (65 vertices each). The exaggeration slider
recomputes normals the same way every time it moves (`writeGradientNormals()`, shared
by the initial build and `applyExaggeration()`), scaling the stored unexaggerated
gradient by the current exaggeration factor rather than re-deriving it from scratch.

## Tiled LOD terrain

`selectLeafTiles()` walks the quadtree from `z0` every ~150ms (`TILE_UPDATE_INTERVAL_MS`
- the walk itself is cheap, but tile load/unload churn on every single frame is not).
At each node it computes the camera's full 3D distance to the *closest point of the
tile's own bounding box* (`THREE.Box3.distanceToPoint` - the same box already built for
frustum culling, reused rather than rebuilt); if that's closer than
`tileWorldSize * REFINE_DISTANCE_FACTOR` (`1.5`) and the tile hasn't hit `maxZoom` yet,
it recurses into the 4 children instead of rendering that node - the same idea
Cesium/OpenLayers-3D/etc. use (subdivide when the tile would cover too much of the
view), simplified here to a single distance ratio since this viewer has no true
screen-space-error budget or camera-FOV-aware projection.

**Bug, found via real browser use, not caught by the Node-side partition tests**: the
first version measured distance as a flat horizontal (XZ-plane only) distance to the
tile's center, completely ignoring camera altitude. Looking mostly straight down while
fully zoomed out puts the camera's XZ position almost exactly above whatever tile is
underneath it, so that tile's horizontal distance reads as ~0 regardless of how far the
camera has actually zoomed out in true 3D - forcing max-zoom refinement right under the
camera no matter what. Reported as "tile level 5 [`z4`, the 5th zoom level counting
from `z0`] still got selected when I zoom out fully". Fixed by measuring true 3D
distance to the tile's bounding box instead of a 2D projection to its center - a
partition/coverage test alone couldn't have caught this, since the bug is in *which*
distance metric feeds the correct algorithm, not in the algorithm's coverage logic.

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
currently selected, not by the underlying raw height tile resolution) and its own
`MeshStandardMaterial` textured with that tile's color `.avif`. `heightCache` (keyed by
`z_x_y`) avoids re-fetching a height tile's raw bytes if the camera revisits it; loaded
tile meshes are tracked in `activeTiles` and disposed (geometry, material, texture) the
moment they leave the desired leaf set, so tile churn doesn't leak GPU memory over a long
session.

### Load/unload ordering: fixing a flash on every LOD transition

Reported after real browser use as "flashing problem when tile change zoom level". Root
cause: `updateVisibleTiles()`'s first version unloaded a stale (no-longer-desired) tile
*immediately*, in the same call where its replacement tile(s) were only just queued for
an async load (fetch height bytes, decode the color texture, build the geometry) -
guaranteeing a gap with nothing rendered there for however long that takes. Fixed by
deferring every unload until the *entire* newly-desired tile set is confirmed active
(loaded, not merely pending): `desired.every(([z, x, y]) => activeTiles.has(...))`
gates the unload loop. This briefly renders old and new tiles overlapping while the
replacement set finishes loading - strictly better than a visible hole where terrain
used to be, and only lasts as long as one tile's own load time.

### Frustum culling

`selectLeafTiles()` also builds a `THREE.Frustum` from the camera's current
`projectionMatrix` / `matrixWorldInverse` (calling `camera.updateMatrixWorld()` first,
since this runs outside the render call and can't rely on the renderer having refreshed
it already) and tests every quadtree node's world-space bounding box against it before
recursing or selecting a leaf. A node entirely outside the frustum is skipped outright -
not recursed into, not fetched, not built, not rendered - so panning the camera away
from part of the map stops loading and holding geometry/textures for tiles that are no
longer (or never were) visible, not just relying on Three.js's own per-mesh
`frustumCulled` draw-call skip after the fact. Each node's bounding box uses a generous,
not exaggeration-accurate vertical range (`[minZ, maxZ] * CULL_MAX_EXAGGERATION`, `40` -
the slider's max) so a tile is never wrongly culled just because the exaggeration slider
is turned up after the box was sized; culling only needs to be *not too tight*, not
exact.

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

### Ocean quad

A single huge flat `THREE.PlaneGeometry` (`OCEAN_QUAD_SIZE = 200000` world units, far
past both the ~22km map and the camera's own far clip plane) sits at the pak's own
ocean level - see `.agents/knowledge/landscape-heightmap.md`'s "Ocean level" section for
where `oceanLevelMeters` comes from (`MTOceanConfig.OceanConfig.OceanLevel`,
cross-verified against `WaterBodyOcean`'s own transform) and how `tiles.json` carries
it through. It's exaggerated by the exact same factor as the terrain
(`oceanMesh.position.y = meta.oceanLevelMeters * currentExaggeration`, updated
alongside every tile's geometry in the slider handler) so the crossover between
"terrain above sea level" and "terrain sculpted down to the ocean floor, hidden
underwater" stays correct at any exaggeration - both scale linearly from world Z=0, so
neither can cross the other just because the slider moved.

Deliberately **not** added to `tileGroup`: the ground-anchored pan raycast only tests
`tileGroup.children`, so panning always grabs the real terrain underneath the water
surface, never the ocean quad itself - dragging near the coast doesn't suddenly anchor
to a flat plane instead of the actual seabed/shoreline geometry.

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
between the raw height floor and the highest observed peak at every exaggeration from
`1x` to the slider's `40x` max, so terrain never crosses to the wrong side of the water
plane just because the slider moved), and asset completeness (every reachable tile file
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
