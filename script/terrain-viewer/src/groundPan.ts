import * as THREE from "three";
import type { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { PAN_DAMPING_FACTOR, PAN_FLING_SAMPLE_MS } from "./constants";

export interface GroundPan {
  /** Call once per animate() frame: integrates + damps the post-release pan fling
   * inertia, so the map keeps sliding after the pointer lifts - the same physics as
   * the orbit/zoom glide. */
  update(dt: number): void;
}

/** Ground-anchored pan (left-drag): the terrain point grabbed on mousedown stays under
 * the cursor for the whole drag, like Google Earth / most CAD tools - not the same as
 * OrbitControls' built-in pan, which just translates the camera in its own screen plane
 * regardless of what's actually under the cursor. Wires pointer events directly onto
 * `renderer.domElement`. On release the recent drag motion becomes a decaying fling
 * (see `PAN_DAMPING_FACTOR`), giving pan the same inertia-based physics as orbit and
 * zoom rather than an abrupt dead stop. */
export function setupGroundPan(
  renderer: THREE.WebGLRenderer,
  camera: THREE.Camera,
  controls: OrbitControls,
  tileGroup: THREE.Group
): GroundPan {
  const raycaster = new THREE.Raycaster();
  const pointerNDC = new THREE.Vector2();
  const panPlane = new THREE.Plane();
  const grabbedPoint = new THREE.Vector3();
  const currentPoint = new THREE.Vector3();

  let panning = false;

  // Fling inertia: the drag's recent world displacement (camera moved by `delta` each
  // move) sampled over time, so release seeds a velocity that update() then damps.
  // panVelocity is world-units per second (the direction the MAP should keep moving,
  // i.e. same as the camera shift during the drag). A short, fixed history window of
  // pointer moves means a quick scrub throws a visible coast while a slow deliberate
  // drag (near-zero recent deltas) stops almost in place.
  interface PanSample { time: number; worldX: number; worldZ: number; }
  const samples: PanSample[] = [];
  let panVelocity: THREE.Vector3 | null = null;

  function setPointerNDC(event: PointerEvent): void {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  renderer.domElement.addEventListener("contextmenu", (event) => event.preventDefault());

  renderer.domElement.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return; // left button grabs; right button orbits via OrbitControls
    panVelocity = null; // a new grab cancels any leftover fling
    samples.length = 0;
    setPointerNDC(event);
    raycaster.setFromCamera(pointerNDC, camera);
    const hits = raycaster.intersectObjects(tileGroup.children, false);
    if (hits.length === 0) return; // clicked off the terrain (open sky, or no tile loaded there yet)
    grabbedPoint.copy(hits[0].point);
    panPlane.setFromNormalAndCoplanarPoint(new THREE.Vector3(0, 1, 0), grabbedPoint);
    panning = true;
    renderer.domElement.setPointerCapture(event.pointerId);
  });

  renderer.domElement.addEventListener("pointermove", (event) => {
    if (!panning) return;
    setPointerNDC(event);
    raycaster.setFromCamera(pointerNDC, camera);
    if (!raycaster.ray.intersectPlane(panPlane, currentPoint)) return;
    // Shift camera + target by exactly what's needed to bring the grabbed point back
    // under the (moved) cursor - re-derived every move from the live camera pose, so it
    // stays exact for the whole drag rather than drifting.
    const delta = grabbedPoint.clone().sub(currentPoint);
    camera.position.add(delta);
    controls.target.add(delta);
    // Record this move in the fling-history window (world-space, so it's zoom/frame
    // independent).
    samples.push({ time: performance.now(), worldX: delta.x, worldZ: delta.z });
    if (samples.length > 32) samples.shift();
  });

  function endPan(event: PointerEvent): void {
    if (!panning) return;
    panning = false;
    if (renderer.domElement.hasPointerCapture(event.pointerId)) {
      renderer.domElement.releasePointerCapture(event.pointerId);
    }
    // Seed the fling velocity from motion within the last PAN_FLING_SAMPLE_MS.
    const now = performance.now();
    const cutoff = now - PAN_FLING_SAMPLE_MS;
    const window = samples.filter((s) => s.time >= cutoff);
    const totalX = window.reduce((a, s) => a + s.worldX, 0);
    const totalZ = window.reduce((a, s) => a + s.worldZ, 0);
    const dt = window.length > 0 ? (now - window[0].time) / 1000 : 0;
    if (dt > 0) {
      const vel = new THREE.Vector3(totalX / dt, 0, totalZ / dt);
      panVelocity = vel;
    }
    samples.length = 0;
  }
  renderer.domElement.addEventListener("pointerup", endPan);
  renderer.domElement.addEventListener("pointercancel", endPan);

  function update(dt: number): void {
    if (!panVelocity) return;
    const step = panVelocity.clone().multiplyScalar(dt);
    camera.position.add(step);
    controls.target.add(step);
    panVelocity.multiplyScalar(Math.pow(1 - PAN_DAMPING_FACTOR, dt * 60)); // 60 = frames/sec normalization
    if (panVelocity.lengthSq() < 0.25) panVelocity = null; // stop the micro-glide jitter
  }

  return { update };
}