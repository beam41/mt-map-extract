import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";

/**
 * Drapes out/amc-web/map/map.png over the terrain extracted by the `heightmap` project.
 * scripts/prepare-assets.js does a direct copy of the `heightmap` project's
 * `--web-size`-downsampled out/heightmap/heights_<n>px.bin into public/assets/heights.bin
 * (still raw uint16 height units, not meters - see rawHeightToWorldZMeters() below) plus
 * a small heights.json metadata passthrough and a copy of map.png.
 *
 * Both assets are assumed to share the same real-world extent (the origin/map-size the
 * `heightmap` project's --origin-x/--origin-y/--map-size default to) - map.png has no
 * embedded coordinate metadata of its own, so this can't be verified in code, only
 * visually (mountain ridges in the color map should align with raised terrain here).
 */

// Real elevation differences (tens of meters) are imperceptible against a 22km-wide map
// at true 1:1 scale - this is a deliberate, visible artistic exaggeration, not a
// measurement. Adjustable live via the on-screen slider.
const DEFAULT_EXAGGERATION = 6;

// Orbiting the same angle sweeps terrain past the camera much faster, visually, when
// zoomed in close than when zoomed far out - scaled by current camera distance every
// frame (see animate()) so close-up orbiting feels controlled instead of dizzying.
const ROTATE_SPEED_NEAR = 0.15; // at controls.minDistance
const ROTATE_SPEED_FAR = 1.0;   // at controls.maxDistance

// World units of camera distance per unit of wheel deltaY - constant regardless of
// current zoom level (unlike OrbitControls' default multiplicative dolly).
const ZOOM_UNITS_PER_WHEEL_DELTA = 5;

/**
 * Raw height unit (0-65535, as stored in heights.bin) -> world Z, in meters. Matches
 * `worldZFormulaCm` in Jeju_World.json exactly (that formula in cm, divided by 100 here):
 * `((rawHeight - 32768) / 128.0) * 100.0 / 100.0` simplifies to the form below. Kept as
 * its own small function here (not imported from script/lib/png16.js, which is
 * CommonJS for Node/get-height.js) rather than fighting Vite's bundler over a
 * CJS/ESM interop that isn't needed for one pure, one-line formula.
 */
function rawHeightToWorldZMeters(rawHeight) {
  return (rawHeight - 32768) / 128.0;
}

async function loadAssets() {
  const [meta, heightsBuffer] = await Promise.all([
    fetch("/assets/heights.json").then((r) => {
      if (!r.ok) throw new Error(`heights.json: HTTP ${r.status}`);
      return r.json();
    }),
    fetch("/assets/heights.bin").then((r) => {
      if (!r.ok) throw new Error(`heights.bin: HTTP ${r.status}`);
      return r.arrayBuffer();
    }),
  ]);
  if (meta.dtype !== "uint16" || meta.byteOrder !== "little") {
    throw new Error(`unsupported heights.bin format: dtype=${meta.dtype} byteOrder=${meta.byteOrder}`);
  }
  // TypedArray-from-ArrayBuffer uses the platform's native byte order, which is
  // little-endian on every browser deployment target - matches heights.bin's declared
  // byteOrder, checked above rather than assumed silently.
  return { meta, rawHeights: new Uint16Array(heightsBuffer) };
}

function buildTerrainGeometry(meta, rawHeights) {
  const { grid, widthMeters, heightMeters } = meta;
  const geometry = new THREE.BufferGeometry();

  const positions = new Float32Array(grid * grid * 3);
  const uvs = new Float32Array(grid * grid * 2);
  const baseHeights = new Float32Array(grid * grid); // unscaled, for live exaggeration changes

  for (let row = 0; row < grid; row++) {
    for (let col = 0; col < grid; col++) {
      const i = row * grid + col;
      const x = (col / (grid - 1) - 0.5) * widthMeters;
      const z = (row / (grid - 1) - 0.5) * heightMeters;
      const y = rawHeightToWorldZMeters(rawHeights[i]);

      positions[i * 3 + 0] = x;
      positions[i * 3 + 1] = y * DEFAULT_EXAGGERATION;
      positions[i * 3 + 2] = z;
      baseHeights[i] = y;

      uvs[i * 2 + 0] = col / (grid - 1);
      uvs[i * 2 + 1] = row / (grid - 1);
    }
  }

  const indices = [];
  for (let row = 0; row < grid - 1; row++) {
    for (let col = 0; col < grid - 1; col++) {
      const a = row * grid + col;
      const b = a + 1;
      const c = a + grid;
      const d = c + 1;
      indices.push(a, c, b, b, c, d);
    }
  }

  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();

  return { geometry, baseHeights };
}

async function main() {
  const container = document.getElementById("app");

  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const skyColor = 0x8fb8e0;
  renderer.setClearColor(skyColor);
  container.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(skyColor);
  scene.fog = new THREE.Fog(skyColor, 25000, 100000);

  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 1, 100000);
  camera.position.set(0, 6000, 9000);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.minDistance = 30;
  controls.maxDistance = 55000;
  // Traditional-game-style bindings: left-drag pans (custom, ground-anchored - see below),
  // right-drag orbits, wheel zooms. OrbitControls' own pan is screen-space only (not
  // ground-anchored), so it's disabled in favor of the raycast-based pan further down.
  controls.mouseButtons = { LEFT: null, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
  controls.enablePan = false;
  // OrbitControls' own wheel zoom is multiplicative (percentage-of-distance per tick) -
  // fast when far out, barely perceptible when close in. Replaced below with a constant
  // linear step per wheel tick, independent of current distance.
  controls.enableZoom = false;

  scene.add(new THREE.HemisphereLight(0xbfe0ff, 0x4a3d2a, 1.4));
  scene.add(new THREE.AmbientLight(0xffffff, 0.6));
  const sun = new THREE.DirectionalLight(0xfff3df, 2.4);
  const sunDirection = new THREE.Vector3(-8000, 12000, 6000).normalize();
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  // Large terrain, coarse texel size at that map resolution - offset along the surface
  // normal (world units, meters) rather than a flat depth bias, which is far more robust
  // against self-shadowing acne on sloped terrain than a small constant bias.
  sun.shadow.normalBias = 15;
  scene.add(sun);
  scene.add(sun.target); // stays at the default (0,0,0), which is where the terrain is centered

  const { meta, rawHeights } = await loadAssets();
  const { geometry, baseHeights } = buildTerrainGeometry(meta, rawHeights);

  // Size the sun's orthographic shadow frustum to comfortably contain the terrain at
  // any exaggeration up to the slider's max (40x) - a single static frustum, not
  // recomputed per-frame or per-exaggeration-change, so shadow resolution is a fixed
  // (coarse, ~dozens of meters/texel across a 22km map) tradeoff of this single-cascade
  // setup, not a bug.
  const SHADOW_MAX_EXAGGERATION = 40;
  const horizontalRadius = 0.5 * Math.sqrt(meta.widthMeters ** 2 + meta.heightMeters ** 2);
  const verticalHalfRange = Math.max(Math.abs(meta.minZ), Math.abs(meta.maxZ)) * SHADOW_MAX_EXAGGERATION;
  const shadowRadius = horizontalRadius + verticalHalfRange;
  // Place the sun far enough past shadowRadius that its shadow camera's near/far span
  // (centered on the light-to-target distance) stays comfortably positive - too close
  // and the bounding sphere below would swallow the light position itself.
  sun.position.copy(sunDirection).multiplyScalar(shadowRadius * 2);
  const shadowCam = sun.shadow.camera;
  shadowCam.left = -shadowRadius;
  shadowCam.right = shadowRadius;
  shadowCam.top = shadowRadius;
  shadowCam.bottom = -shadowRadius;
  shadowCam.near = sun.position.length() - shadowRadius - 1000;
  shadowCam.far = sun.position.length() + shadowRadius + 1000;
  shadowCam.updateProjectionMatrix();

  const texture = new THREE.TextureLoader().load("/assets/map.png");
  // Our UVs (v = row / (grid-1)) already follow the heightmap's un-flipped row->worldY
  // mapping (row 0 = originCm.Y, the map's north/top edge - see LandscapeExtractor's
  // Stitch). TextureLoader defaults flipY=true (assumes the plane's own default UVs,
  // v=0 at bottom), which would flip map.png's north/south axis relative to that -
  // disable it so the texture uses the same raw top-down row order as the geometry.
  texture.flipY = false;
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = renderer.capabilities.getMaxAnisotropy();

  const material = new THREE.MeshStandardMaterial({ map: texture, roughness: 0.95, metalness: 0 });
  const terrain = new THREE.Mesh(geometry, material);
  terrain.castShadow = true;
  terrain.receiveShadow = true;
  scene.add(terrain);

  controls.target.set(0, 0, 0);
  controls.update();

  // Ground-anchored pan (left-drag): the terrain point grabbed on mousedown stays under
  // the cursor for the whole drag, like Google Earth / most CAD tools - not the same as
  // OrbitControls' built-in pan, which just translates the camera in its own screen plane
  // regardless of what's actually under the cursor.
  const raycaster = new THREE.Raycaster();
  const pointerNDC = new THREE.Vector2();
  const panPlane = new THREE.Plane();
  const grabbedPoint = new THREE.Vector3();
  const currentPoint = new THREE.Vector3();
  let panning = false;

  function setPointerNDC(event) {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  renderer.domElement.addEventListener("contextmenu", (event) => event.preventDefault());

  renderer.domElement.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return; // left button grabs; right button orbits via OrbitControls
    setPointerNDC(event);
    raycaster.setFromCamera(pointerNDC, camera);
    const hits = raycaster.intersectObject(terrain, false);
    if (hits.length === 0) return; // clicked off the terrain (open sky) - nothing to grab
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

  function endPan(event) {
    if (!panning) return;
    panning = false;
    if (renderer.domElement.hasPointerCapture(event.pointerId)) {
      renderer.domElement.releasePointerCapture(event.pointerId);
    }
  }
  renderer.domElement.addEventListener("pointerup", endPan);
  renderer.domElement.addEventListener("pointercancel", endPan);

  // Linear-velocity zoom: each wheel tick moves the camera a fixed distance along the
  // camera-target line, clamped to [minDistance, maxDistance] - replaces OrbitControls'
  // disabled built-in dolly (see controls.enableZoom = false above).
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

  // Live exaggeration slider - real terrain relief is a few tens of meters over a 22km
  // map, invisible at 1:1; this lets you dial it back to (near) true scale to see that.
  const slider = document.createElement("input");
  slider.type = "range";
  slider.min = "1";
  slider.max = "40";
  slider.step = "1";
  slider.value = String(DEFAULT_EXAGGERATION);
  Object.assign(slider.style, { position: "fixed", bottom: "12px", left: "8px", zIndex: 10, width: "220px" });
  document.body.appendChild(slider);
  const label = document.createElement("div");
  Object.assign(label.style, {
    position: "fixed", bottom: "34px", left: "8px", zIndex: 10,
    font: "12px monospace", color: "#cfe3ff",
  });
  label.textContent = `vertical exaggeration: ${DEFAULT_EXAGGERATION}x`;
  document.body.appendChild(label);

  const positionAttr = geometry.getAttribute("position");
  slider.addEventListener("input", () => {
    const factor = Number(slider.value);
    label.textContent = `vertical exaggeration: ${factor}x`;
    for (let i = 0; i < baseHeights.length; i++) {
      positionAttr.setY(i, baseHeights[i] * factor);
    }
    positionAttr.needsUpdate = true;
    geometry.computeVertexNormals();
  });

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  function animate() {
    requestAnimationFrame(animate);
    const distance = camera.position.distanceTo(controls.target);
    const t = THREE.MathUtils.clamp(
      (distance - controls.minDistance) / (controls.maxDistance - controls.minDistance), 0, 1
    );
    controls.rotateSpeed = THREE.MathUtils.lerp(ROTATE_SPEED_NEAR, ROTATE_SPEED_FAR, t);
    controls.update();
    renderer.render(scene, camera);
  }
  animate();
}

main().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="color:#f88;padding:16px;font:14px monospace">${err.stack || err.message}</pre>`;
});
