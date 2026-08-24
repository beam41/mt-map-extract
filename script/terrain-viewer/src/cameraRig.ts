import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import {
  ROTATE_SPEED_NEAR,
  ROTATE_SPEED_FAR,
  ZOOM_UNITS_PER_WHEEL_DELTA,
  LOOK_UP_ANGLE_FAR_DEG,
  LOOK_UP_ANGLE_NEAR_DEG,
} from "./constants";

export interface CameraRig {
  camera: THREE.PerspectiveCamera;
  controls: OrbitControls;
  /** Call once per animate() frame, before controls.update()/rendering: applies the
   * distance-based rotate-speed and look-up-angle lerps, then updates `controls`
   * itself (so damping/orbit state is fully settled before this frame renders). */
  update(): void;
}

/** Camera + OrbitControls setup, plus the "feel" tuning that depends on current zoom
 * distance: orbiting the same screen-space angle sweeps terrain past the camera much
 * faster, visually, when zoomed in close than when zoomed far out, and looking up
 * past the horizon only makes sense once zoomed all the way in to ground level - both
 * scaled by the same zoom fraction `t` (0 at controls.minDistance, 1 at
 * controls.maxDistance) every frame. Also owns the linear-velocity wheel-zoom
 * handler, replacing OrbitControls' own multiplicative dolly (fast when zoomed out,
 * barely perceptible up close) with a constant world-unit step per wheel tick. */
export function createCameraRig(renderer: THREE.WebGLRenderer): CameraRig {
  // near=5, not 1: also reduces the near/far ratio (5:100000 instead of 1:100000),
  // compounding with logarithmicDepthBuffer (see main.ts) rather than fighting it -
  // nothing in this scene needs to render closer than a few meters from the camera
  // anyway.
  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 5, 100000);
  camera.position.set(0, 6000, 9000);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.minDistance = 30;
  controls.maxDistance = 25000;
  // Traditional-game-style bindings: left-drag pans (custom, ground-anchored - see
  // groundPan.ts), right-drag orbits, wheel zooms. OrbitControls' own pan is
  // screen-space only (not ground-anchored), so it's disabled in favor of the
  // raycast-based pan in groundPan.ts.
  controls.mouseButtons = { LEFT: null, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
  controls.enablePan = false;
  // OrbitControls' own wheel zoom is multiplicative (percentage-of-distance per
  // tick) - fast when far out, barely perceptible when close in. Replaced below with
  // a constant linear step per wheel tick, independent of current distance.
  controls.enableZoom = false;

  // Linear-velocity zoom: each wheel tick moves the camera a fixed distance along the
  // camera-target line, clamped to [minDistance, maxDistance].
  renderer.domElement.addEventListener(
    "wheel",
    (event) => {
      event.preventDefault();
      const offset = camera.position.clone().sub(controls.target);
      const distance = offset.length();
      const newDistance = THREE.MathUtils.clamp(
        distance + event.deltaY * ZOOM_UNITS_PER_WHEEL_DELTA,
        controls.minDistance,
        controls.maxDistance
      );
      offset.setLength(newDistance);
      camera.position.copy(controls.target).add(offset);
    },
    { passive: false }
  );

  function update(): void {
    const distance = camera.position.distanceTo(controls.target);
    const t = THREE.MathUtils.clamp(
      (distance - controls.minDistance) / (controls.maxDistance - controls.minDistance), 0, 1
    );
    controls.rotateSpeed = THREE.MathUtils.lerp(ROTATE_SPEED_NEAR, ROTATE_SPEED_FAR, t);
    // Look-up limit: continuously lerped from LOOK_UP_ANGLE_FAR_DEG at full zoom-out
    // (t=1) to LOOK_UP_ANGLE_NEAR_DEG at full zoom-in (t=0) - smoothly loosens as
    // the camera zooms in, no hard snap at one threshold.
    controls.maxPolarAngle = THREE.MathUtils.degToRad(
      THREE.MathUtils.lerp(LOOK_UP_ANGLE_NEAR_DEG, LOOK_UP_ANGLE_FAR_DEG, t)
    );
    controls.update();
  }

  return { camera, controls, update };
}
