// Orbiting the same angle sweeps terrain past the camera much faster, visually, when
// zoomed in close than when zoomed far out - scaled by current camera distance every
// frame (see cameraRig.ts) so close-up orbiting feels controlled instead of dizzying.
export const ROTATE_SPEED_NEAR = 0.15; // at controls.minDistance
export const ROTATE_SPEED_FAR = 1.0;   // at controls.maxDistance

// How much a full wheel notch (unit of deltaY) scales distance when zooming -
// MULTIPLICATIVE (exponential), not a constant world-unit step. Constant per unit
// world distance is what made scroll feel faster the closer you zoom: the same
// fixed step is a tiny fraction of the footprint at max zoom-out but a large one
// at max zoom-in. A constant log-distance factor per notch (e.g. ~1.127x per notch
// here) makes relative motion uniform at every zoom, so scroll feel is consistent.
export const ZOOM_LOG_PER_WHEEL_DELTA = 0.00012; // log(distance) change per unit deltaY (~1.13x per full 100-unit wheel notch)

// Inertia (physics) for zoom and pan, matching the feel of the OrbitControls orbit
// (which glides to a stop via controls.enableDamping / dampingFactor). Zoom and pan
// are custom handlers (see cameraRig.ts / groundPan.ts) that update the camera
// directly with no damping of their own, so they snapped to a dead stop the moment
// input ended while orbiting still coasted - these constants give them the same
// exponential decay per frame.
//   ZOOM_DAMPING_FACTOR  - how quickly wheel-zoom velocity dies out each frame
//     (higher = shorter glide). Matches the orbit feel: 0.08 is near-instant, ~0.35
//     gives a short, perceptible coast.
//   PAN_DAMPING_FACTOR   - same, for the post-release pan fling.
//   PAN_FLING_SAMPLE_MS  - the drag-release "fling" looks back at the last ~90ms of
//     pointer motion to seed the initial pan velocity, so a quick scrub throws a
//     visible coast while a slow deliberate drag stops nearly in place.
export const ZOOM_DAMPING_FACTOR = 0.35;
export const PAN_DAMPING_FACTOR = 0.35;
export const PAN_FLING_SAMPLE_MS = 90;

// The coarsest zoom level that is ever rendered. z0 (the whole ~22km map as one
// single tile) is never selected or built - its texture was a blurry whole-map
// downscale and its only special handling (a stitched z1 composite) is long gone.
// selectLeafTiles() force-refines below MIN_RENDER_ZOOM, so the coarsest ever shown
// is z1 (a 2x2 grid, at its own low mesh resolution).
export const MIN_RENDER_ZOOM = 1;

// Tile-selection range of vision (rule 1): the orbit point (`controls.target`'s XZ -
// the ground point the camera is orbiting) is the "lod0 point". The finest zoom
// level's grid of cells is placed over the map. Each zoom level z owns a vision ring
// - a true circle (Euclidean, not Chebyshev/square) centered on the continuous orbit
// point (never snapped to a grid cell), with overall SIZE (diameter, corner to
// corner across the ring) a multiple of the width of a single z tile - i.e. RADIUS
// is half that. The quadtree is walked top-down testing, at each candidate tile of
// zoom z, whether the NEXT finer zoom's own vision ring hits that tile's square
// hitbox: a hit means part of it wants finer detail, so it splits into its 4
// children (each re-tested one zoom finer); a miss means z's own resolution already
// covers the whole tile, so it stops there and counts for zoom z - a compact
// `maxZoom` core right under the orbit point, symmetric circular rings cascading out
// in ALL directions, maximal blocks, never overlapping, exactly covering the map.
//
// Each zoom level's ring size is tied to that level's OWN tile size, but the
// diameter FACTOR differs between the finest currently-active zoom (maxZoomByAltitude
// - what's usually z5 now) and every coarser one: the finest level's own ring is
// deliberately kept tight (RING_EXTENT_FINEST_MULTIPLIER, 1.88x its own tile width),
// while every coarser level uses the wider RING_EXTENT_COARSER_MULTIPLIER (2.88x its
// own tile width) - each coarser ring is sized to comfortably contain the next
// finer level's whole cascade (z3's ring comfortably contains z4's 1x-tile-sized
// core, z2's contains z3's, z1's contains z2's), rather than every level sharing one
// factor. NOTE: this asymmetric scheme has NOT been verified to guarantee every pair
// of touching leaves stays within one zoom level of each other the way a single
// uniform factor can be (see lod.ts's selectLeafTiles doc comment) - occasional
// sharper steps between neighboring tiles are still expected.
export const RING_EXTENT_FINEST_MULTIPLIER = 2.88;
export const RING_EXTENT_COARSER_MULTIPLIER = 3.88;

// Altitude cap (rule 2, overrides rule 1): the camera's height above the ocean sets
// the maximum zoom any tile may use - looking down from very high up, fine tiles
// would be pure waste (and a load/unload storm as the camera moves). The cap scales
// linearly across the *full* zoom-out range so it never bands narrowly: ALTITUDE_CAP
// is roughly the altitude right at min zoom, and ALTITUDE_CAP_FULL (itself ~the
// camera's max-distance altitude) is where it reaches MIN_RENDER_ZOOM.
export const ALTITUDE_CAP_MIN = 500;    // altitude above ocean where the cap is still inactive (max zoom)
export const ALTITUDE_CAP_FULL = 25000; // altitude above ocean where the cap reaches MIN_RENDER_ZOOM

// Look-up angle limit: the camera may never tilt above this polar angle (0 = looking
// straight down at the map, 90 = horizontal, 180 = straight up). Continuously
// lerped between LOOK_UP_ANGLE_FAR_DEG (60 - fairly restricted) at full zoom-out and
// LOOK_UP_ANGLE_NEAR_DEG (90 - up to the horizon, never past it) at full zoom-in,
// scaled by the same zoom fraction `t` as the rotate-speed lerp above - smoothly
// loosening the cap as the camera zooms in, not a hard snap at one threshold.
export const LOOK_UP_ANGLE_FAR_DEG = 60;
export const LOOK_UP_ANGLE_NEAR_DEG = 90;

// World-unit drop for the skirt (a thin vertical wall of extra geometry around every
// tile's border, dropped straight down). Neighboring tiles at different zoom levels
// sample the native heightmap at different resolutions, so their shared edge rarely
// lines up vertex-for-vertex - a classic quadtree-terrain crack. The skirt doesn't fix
// the crack geometrically; it hides it behind a wall deep enough that the gap reads as
// a shadowed seam instead of a hole punched through the terrain.
export const SKIRT_DROP = 400;

// World-unit side length of the flat ocean quad - deliberately much larger than the
// ~22km map itself so it reads as an endless sea to the horizon rather than a visibly
// bounded tile, out to and past the camera's own far clip plane (100000).
export const OCEAN_QUAD_SIZE = 200000;

// Debug view: color each active tile by its own zoom level instead of its real
// texture - lets you see directly whether a given seam sits between two *different*-
// zoom tiles or two *same*-zoom ones, instead of assuming it's an LOD-stitching
// artifact. One distinct, maximally-separated color per zoom (z0..z5) - shaded
// (`MeshStandardMaterial`, same roughness/metalness as the real tile material) rather
// than flat/unlit, so terrain relief and any *normal*-continuity seam (a lighting
// discontinuity, not a position crack) stay visible in this view too, not just the
// zoom-level boundaries themselves. Combine with the wireframe toggle below to also
// see the actual triangle mesh at a boundary - a real geometric gap shows as a break
// in the wireframe grid; a seam with no such break is a shading-only (normal/lighting)
// artifact, not a position crack.
export const ZOOM_DEBUG_COLORS = [0xff3b30, 0xff9500, 0xffdd00, 0x34c759, 0x0a84ff, 0xaf52de];

// World-unit lift for the debug per-tile border line (see tileGeometry.ts's
// buildTileBorder()) above the tile's own terrain surface - just enough to avoid
// z-fighting with the tile mesh it traces, negligible next to the map's real
// elevation range.
export const DEBUG_BORDER_Y_OFFSET = 2;

// Debug view: how many segments approximate each vision-ring circle (see
// ringDebugView.ts) - high enough that the polygon reads as a smooth circle at any
// zoom level actually used in this viewer.
export const RING_DEBUG_SEGMENTS = 96;

// How often (ms) the animate loop re-runs selectLeafTiles()/updateVisibleTiles() -
// throttled because the quadtree walk itself is cheap, but tile load/unload churn
// on every single rendered frame is not.
export const TILE_UPDATE_INTERVAL_MS = 150;
