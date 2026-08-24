import * as THREE from "three";
import type { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";

/** Ground-anchored pan (left-drag): the terrain point grabbed on mousedown stays under
 * the cursor for the whole drag, like Google Earth / most CAD tools - not the same as
 * OrbitControls' built-in pan, which just translates the camera in its own screen plane
 * regardless of what's actually under the cursor. Wires pointer events directly onto
 * `renderer.domElement`; no return value, purely a side-effecting setup call. */
export function setupGroundPan(
  renderer: THREE.WebGLRenderer,
  camera: THREE.Camera,
  controls: OrbitControls,
  tileGroup: THREE.Group
): void {
  const raycaster = new THREE.Raycaster();
  const pointerNDC = new THREE.Vector2();
  const panPlane = new THREE.Plane();
  const grabbedPoint = new THREE.Vector3();
  const currentPoint = new THREE.Vector3();
  let panning = false;

  function setPointerNDC(event: PointerEvent): void {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  renderer.domElement.addEventListener("contextmenu", (event) => event.preventDefault());

  renderer.domElement.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return; // left button grabs; right button orbits via OrbitControls
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
  });

  function endPan(event: PointerEvent): void {
    if (!panning) return;
    panning = false;
    if (renderer.domElement.hasPointerCapture(event.pointerId)) {
      renderer.domElement.releasePointerCapture(event.pointerId);
    }
  }
  renderer.domElement.addEventListener("pointerup", endPan);
  renderer.domElement.addEventListener("pointercancel", endPan);
}
