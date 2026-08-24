import * as THREE from "three";
import type { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { MIN_RENDER_ZOOM, ZOOM_DEBUG_COLORS, RING_DEBUG_SEGMENTS } from "./constants";
import { computeRingExtents } from "./lod";
import type { TilesMeta } from "./types";

export interface RingDebugView {
  /** Added to the scene once; toggle visibility via setVisible() rather than
   * removing/re-adding. */
  group: THREE.Group;
  setVisible(visible: boolean): void;
  /** Recomputes every circle's position/radius from the CURRENT camera + orbit
   * target. No-ops entirely (cheap) while hidden. */
  update(): void;
}

/** Debug view: draws each zoom level's own vision-ring circle (see
 * RING_EXTENT_FINEST_MULTIPLIER/RING_EXTENT_COARSER_MULTIPLIER and lod.ts's
 * computeRingExtents()) centered on the current orbit point - shows exactly the
 * circle selectLeafTiles() tests tiles against, one per zoom, in that zoom's own
 * ZOOM_DEBUG_COLORS color (darkened - see below). One shared unit-circle geometry,
 * scaled per zoom/per update to its actual world-space radius rather than
 * rebuilding geometry every update. */
export function createRingDebugView(
  scene: THREE.Scene,
  meta: TilesMeta,
  camera: THREE.Camera,
  controls: OrbitControls
): RingDebugView {
  const unitCirclePositions = new Float32Array(RING_DEBUG_SEGMENTS * 3);
  for (let i = 0; i < RING_DEBUG_SEGMENTS; i++) {
    const a = (i / RING_DEBUG_SEGMENTS) * Math.PI * 2;
    unitCirclePositions[i * 3 + 0] = Math.cos(a);
    unitCirclePositions[i * 3 + 1] = 0;
    unitCirclePositions[i * 3 + 2] = Math.sin(a);
  }
  const unitCircleGeometry = new THREE.BufferGeometry();
  unitCircleGeometry.setAttribute("position", new THREE.BufferAttribute(unitCirclePositions, 3));

  const group = new THREE.Group();
  group.visible = false;
  scene.add(group);

  const circles: Array<{ z: number; circle: THREE.LineLoop }> = [];
  for (let z = MIN_RENDER_ZOOM; z <= meta.maxZoom; z++) {
    // depthTest disabled: these are debug gizmos, meant to stay visible over terrain
    // relief rather than getting buried inside a hill under the ring's own center.
    // Darkened well below the zoom's own fill color (see ZOOM_DEBUG_COLORS) - at full
    // brightness a ring was nearly invisible sitting right on top of a same-colored
    // tile; darkening keeps the per-zoom color identity while giving real contrast.
    const ringColor = new THREE.Color(ZOOM_DEBUG_COLORS[z]).multiplyScalar(0.35);
    const material = new THREE.LineBasicMaterial({ color: ringColor, depthTest: false });
    const circle = new THREE.LineLoop(unitCircleGeometry, material);
    circle.renderOrder = 999;
    group.add(circle);
    circles.push({ z, circle });
  }

  function update(): void {
    if (!group.visible) return;
    const { maxZoomByAltitude, cellSize, orbitCX, orbitCY, ringExtent } = computeRingExtents(camera, meta, controls.target);
    const centerX = -meta.widthMeters / 2 + orbitCX * cellSize;
    const centerZ = -meta.heightMeters / 2 + orbitCY * cellSize;
    const centerY = controls.target.y + 100; // hover well above the ground, unaffected by depthTest anyway
    for (const { z, circle } of circles) {
      circle.visible = z <= maxZoomByAltitude;
      if (!circle.visible) continue;
      const radius = ringExtent[z] * cellSize;
      circle.position.set(centerX, centerY, centerZ);
      circle.scale.set(radius, 1, radius);
    }
  }

  return {
    group,
    setVisible(visible: boolean) {
      group.visible = visible;
    },
    update,
  };
}
