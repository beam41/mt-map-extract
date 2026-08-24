import * as THREE from "three";
import {
  MIN_RENDER_ZOOM,
  ALTITUDE_CAP_MIN,
  ALTITUDE_CAP_FULL,
  RING_EXTENT_FINEST_MULTIPLIER,
  RING_EXTENT_COARSER_MULTIPLIER,
} from "./constants";
import { tileWorldRect } from "./heightmap";
import type { TilesMeta, RingExtents, LeafTile } from "./types";

/** Rule 1 + rule 2's shared setup (see selectLeafTiles' doc comment): the altitude-
 * capped max zoom, the continuous orbit point (in finest-grid-cell units), and each
 * own vision-ring radius (also in finest-grid-cell units - multiply by cellSize for
 * world units). Pulled out of selectLeafTiles so the ring-extent debug view (see
 * ringDebugView.ts) can draw the exact same circles the tile-selection walk
 * actually tests against, not a re-derived approximation. */
export function computeRingExtents(
  camera: THREE.Camera,
  meta: TilesMeta,
  orbitTarget: THREE.Vector3
): RingExtents {
  // Rule 2 (altitude cap): convert camera altitude above ocean to a max allowed zoom,
  // scaled linearly across the full zoom-out range so it doesn't band narrowly.
  const altAboveOcean = camera.position.y - (meta.oceanLevelMeters ?? 0);
  const altFrac = THREE.MathUtils.clamp(
    (altAboveOcean - ALTITUDE_CAP_MIN) / (ALTITUDE_CAP_FULL - ALTITUDE_CAP_MIN), 0, 1
  );
  const maxZoomByAltitude = Math.max(
    MIN_RENDER_ZOOM,
    Math.round(MIN_RENDER_ZOOM + (meta.maxZoom - MIN_RENDER_ZOOM) * (1 - altFrac))
  );

  // Rule 1 setup: place the finest zoom's grid over the map and locate the orbit
  // point within it, in CONTINUOUS finest-grid-cell units - NOT floored/snapped to a
  // cell index. Flooring here would quantize the orbit reference to whichever cell's
  // corner it falls in, making every downstream distance (and thus the whole ring
  // cascade, including the debug ring view) jump in whole-cellSize steps instead of
  // moving smoothly with the camera - reported as "ring snaps to the edge, doesn't
  // float with the orbit point" and, since a tile right at a ring boundary could
  // then be tested against a reference point up to a full cell away from the real
  // orbit, as tiles visibly inside the ring not actually getting that zoom level.
  // ringExtent[z] is the vision RADIUS for zoom z - half of the diameter factor
  // (RING_EXTENT_FINEST_MULTIPLIER or RING_EXTENT_COARSER_MULTIPLIER) describes - a
  // true circle in these same continuous finest-grid-cell units.
  const finestGrid = 1 << maxZoomByAltitude; // cells per side of the finest grid
  const cellSize = meta.widthMeters / finestGrid;
  const orbitCX = (orbitTarget.x + meta.widthMeters / 2) / cellSize;
  const orbitCY = (orbitTarget.z + meta.heightMeters / 2) / cellSize;

  // RING_EXTENT_FINEST_MULTIPLIER / RING_EXTENT_COARSER_MULTIPLIER are DIAMETER
  // factors (the ring's overall size, corner to corner, relative to the tile) -
  // ringExtent[] itself is a RADIUS (nearestRing is a straight-line distance from a
  // point, i.e. a radius), so it's half the diameter factor: tileSideCells *
  // diameterFactor / 2. The finest currently-active zoom (maxZoomByAltitude) gets
  // the tighter FINEST factor; every coarser zoom gets the wider COARSER factor, so
  // each coarser ring comfortably contains the next finer level's own ring/core.
  const ringExtent: number[] = new Array(maxZoomByAltitude + 1);
  for (let z = MIN_RENDER_ZOOM; z <= maxZoomByAltitude; z++) {
    const tileSideCells = 1 << (maxZoomByAltitude - z); // z's own tile side, in finest cells
    const diameterFactor = z === maxZoomByAltitude ? RING_EXTENT_FINEST_MULTIPLIER : RING_EXTENT_COARSER_MULTIPLIER;
    ringExtent[z] = (diameterFactor / 2) * tileSideCells;
  }

  return { maxZoomByAltitude, cellSize, orbitCX, orbitCY, ringExtent };
}

/** Selects the quadtree leaves to render, from camera + orbit state only:
 *
 * Rule 1 (range of vision from the orbit point): the "lod0 point" is the continuous
 * orbit point itself (`controls.target`'s XZ, the ground point the camera is
 * orbiting) - NOT snapped/floored to a grid cell (see computeRingExtents' doc
 * comment for why that snapping was a real bug). Each zoom level z has its own
 * vision ring - a true circle (Euclidean, not Chebyshev/square) centered on that
 * exact point, with overall SIZE (diameter) a multiple of the width of a single z
 * tile - RING_EXTENT_FINEST_MULTIPLIER for the finest currently-active zoom,
 * RING_EXTENT_COARSER_MULTIPLIER for every coarser one (radius is half that - see
 * those constants' doc comment). The quadtree is walked top-down: a candidate tile
 * at zoom z is a maximal leaf once the NEXT finer zoom's own vision ring misses
 * that tile's square hitbox entirely: no part of it wants finer detail, so it stops
 * right there and counts for zoom z. If the finer ring does hit the hitbox, the
 * tile splits into its 4 children and each is tested the same way one zoom finer -
 * a compact `maxZoom` core sits right under the orbit point, with symmetric
 * circular rings cascading out in ALL directions. This construction guarantees, for
 * every rendered leaf and every zoom level L, that if L's own vision ring touches
 * the leaf's hitbox at all the leaf is never coarser than L (verified by exhaustive
 * random-continuous-orbit simulation against an independently-written distance
 * check, zero violations across millions of checks) - exactly the "if it hits,
 * count it for that level" rule. See RING_EXTENT_FINEST_MULTIPLIER/
 * RING_EXTENT_COARSER_MULTIPLIER's doc comment: this asymmetric scheme has NOT been
 * verified to guarantee every pair of touching leaves stays within one zoom level of
 * each other the way a single uniform factor can be; occasional sharper steps
 * between neighbors are expected.
 *
 * Rule 2 (altitude cap, overrides rule 1): the camera's height above the ocean
 * (`camera.position.y - meta.oceanLevelMeters`) restricts the maximum zoom any tile
 * may use, scaled linearly across the full zoom-out range (ALTITUDE_CAP_MIN at min
 * zoom to ALTITUDE_CAP_FULL at max zoom-out) so it bands broadly - flying really high
 * up never selects fine LODs even directly under the orbit point.
 *
 * A node entirely outside the camera's frustum is still culled outright (not
 * recursed into, not selected, not loaded) - that part is unchanged. */
export function selectLeafTiles(
  camera: THREE.Camera,
  meta: TilesMeta,
  orbitTarget: THREE.Vector3
): LeafTile[] {
  camera.updateMatrixWorld(); // ensures matrixWorldInverse below reflects this frame's pose
  const viewProjMatrix = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
  const frustum = new THREE.Frustum().setFromProjectionMatrix(viewProjMatrix);
  const minY = Math.min(meta.minZ, meta.maxZ, 0);
  const maxY = Math.max(meta.minZ, meta.maxZ, 0);

  const { maxZoomByAltitude, orbitCX, orbitCY, ringExtent } = computeRingExtents(camera, meta, orbitTarget);
  const leaves: LeafTile[] = [];
  const box = new THREE.Box3();

  // Walk the quadtree top-down. A node at zoom z is a maximal leaf once the NEXT
  // finer zoom's ring (a true circle around the continuous orbit point) misses its
  // square hitbox entirely; otherwise the ring reaches into it and it splits into
  // its 4 children.
  function visit(z: number, x: number, y: number): void {
    const { worldX0, worldZ0, tileSize } = tileWorldRect(meta, z, x, y);
    box.min.set(worldX0, minY, worldZ0);
    box.max.set(worldX0 + tileSize, maxY, worldZ0 + tileSize);
    if (!frustum.intersectsBox(box)) return; // fully offscreen - cull, don't recurse or load

    // This node's own hitbox covers finest cells [x0..x1]x[y0..y1] - i.e. the
    // CONTINUOUS range [x0, xEdge)x[y0, yEdge) where xEdge/yEdge = x0/y0 + side (one
    // past the last included cell). True Euclidean (circular, not Chebyshev/square)
    // distance from the continuous orbit point to that hitbox's nearest point:
    // per-axis overhang (dx, dy), combined via straight-line distance. Using x1/y1
    // (the last INCLUDED cell index) directly as the far edge here was a real bug -
    // it's one cell short of the box's true edge (xEdge = x1+1), so orbitCX-x1
    // overstated the distance to a box the orbit point is past the right/bottom
    // side of by a full cell width, making that box look ~1 tile-width farther than
    // it truly is and letting it wrongly stay coarse - reported as "the ring visibly
    // touches this tile but it doesn't get that LOD".
    const side = 1 << (maxZoomByAltitude - z); // node side length in finest cells
    const x0 = x * side, y0 = y * side, xEdge = x0 + side, yEdge = y0 + side;
    const dx = Math.max(0, x0 - orbitCX, orbitCX - xEdge);
    const dy = Math.max(0, y0 - orbitCY, orbitCY - yEdge);
    const nearestRing = Math.hypot(dx, dy);
    if (z >= maxZoomByAltitude || nearestRing > ringExtent[z + 1]) {
      leaves.push([z, x, y]);
      return;
    }
    visit(z + 1, x * 2, y * 2);
    visit(z + 1, x * 2 + 1, y * 2);
    visit(z + 1, x * 2, y * 2 + 1);
    visit(z + 1, x * 2 + 1, y * 2 + 1);
  }
  visit(0, 0, 0);
  return leaves;
}
