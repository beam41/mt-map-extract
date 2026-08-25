import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import {
  ROTATE_SPEED_NEAR,
  ROTATE_SPEED_FAR,
  ZOOM_LOG_PER_WHEEL_DELTA,
  ZOOM_DAMPING_FACTOR,
  LOOK_UP_ANGLE_FAR_DEG,
  LOOK_UP_ANGLE_NEAR_DEG,
} from "./constants";

export interface CameraRig {
  camera: THREE.PerspectiveCamera;
  controls: OrbitControls;
  /** Call once per animate() frame, before controls.update()/rendering: applies the
   * distance-based rotate-speed and look-up-angle lerps, integrates the inertial
   * zoom velocity, then updates `controls` itself (so damping/orbit state is fully
   * settled before this frame renders). */
  update(dt: number): void;
}

/** Camera + OrbitControls setup, plus the "feel" tuning that depends on current zoom
 * distance: orbiting the same screen-space angle sweeps terrain past the camera much
 * faster, visually, when zoomed in close than when zoomed far out, and looking up
 * past the horizon only makes sense once zoomed all the way in to ground level - both
 * scaled by the same zoom fraction `t` (0 at controls.minDistance, 1 at
 * controls.maxDistance) every frame. Also owns the inertial exponential wheel-zoom
 * handler (with the same inertia as the orbit - see update()), replacing OrbitControls'
 * own multiplicative dolly. */
export function createCameraRig(renderer: THREE.WebGLRenderer): CameraRig {
  // near=5, not 1: also reduces the near/far ratio (5:100000 instead of 1:100000),
  // compounding with logarithmicDepthBuffer (see main.ts) rather than fighting it -
  // nothing in this scene needs to render closer than a few meters from the camera
  // anyway.
  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 5, 100000);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.minDistance = 30;
  controls.maxDistance = 25000;

  // Default view: straight down from max height (polar angle 0 = looking straight
  // down at the map, at the fully zoomed-out distance) - a clean top-down overview
  // instead of the old low-angle 3/4 view. Set after controls exists (the max
  // distance is a controls property); OrbitControls computes its polar angle from
  // the camera's position relative to target on the next update(), and placing the
  // camera directly above the target yields exactly polar angle 0.
  camera.position.set(0, controls.maxDistance, 0);
  camera.lookAt(controls.target);
  // Traditional-game-style bindings: left-drag pans (custom, ground-anchored - see
  // groundPan.ts), right-drag orbits, wheel zooms. OrbitControls' own pan is
  // screen-space only (not ground-anchored), so it's disabled in favor of the
  // raycast-based pan in groundPan.ts.
  controls.mouseButtons = { LEFT: null, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
  controls.enablePan = false;
  // OrbitControls' own wheel zoom is multiplicative (percentage-of-distance per
  // tick) - fast when far out, barely perceptible when close in. Replaced below with
  // a multiplicative (exponential) wheel zoom, independent of current distance.
  controls.enableZoom = false;

  // Inertial exponential wheel-zoom: wheel deltas accumulate into a LOG-DISTANCE
  // change (sum of deltaY * ZOOM_LOG_PER_WHEEL_DELTA), applied each frame as
  // `distance *= exp(logChange)` and damped toward zero - a spin of the wheel keeps
  // gliding after the finger stops, exactly like the orbit, and relative motion per
  // tick is uniform at every zoom (so scroll feel doesn't change with distance).
  // Clamped to [minDistance, maxDistance] once per frame.
  let zoomLog = 0; // accumulated log-distance change still to be applied
  renderer.domElement.addEventListener(
    "wheel",
    (event) => {
      event.preventDefault();
      const deltaLog = event.deltaY * ZOOM_LOG_PER_WHEEL_DELTA;
      // Rolling the wheel back the other way should countermand accumulated zoom
      // rather than sliding on top of it (feels sticky otherwise).
      if (deltaLog !== 0 && (zoomLog > 0) !== (deltaLog > 0)) {
        zoomLog = 0;
      }
      zoomLog += deltaLog;
    },
    { passive: false }
  );

  /** Apply + damp the accumulated exponential zoom. Damping uses the same per-frame
   * exponential form as OrbitControls' own damping, so the zoom glide feels
   * consistent with the orbit glide. */
  function integrateZoom(dt: number): void {
    if (zoomLog === 0) return;
    const offset = camera.position.clone().sub(controls.target);
    const distance = offset.length();
    // exponential integration: distance *= exp(logChange)
    const newDistance = THREE.MathUtils.clamp(
      distance * Math.exp(zoomLog),
      controls.minDistance,
      controls.maxDistance
    );
    offset.setLength(newDistance);
    camera.position.copy(controls.target).add(offset);
    zoomLog *= Math.pow(1 - ZOOM_DAMPING_FACTOR, dt * 60); // 60 = frames/sec normalization
    if (Math.abs(zoomLog) < 0.002) zoomLog = 0; // stop the micro-glide jitter
  }

  function update(dt: number): void {
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
    integrateZoom(dt);
    controls.update();
  }

  return { camera, controls, update };
}