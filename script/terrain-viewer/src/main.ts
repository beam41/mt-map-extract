import * as THREE from "three";

/**
 * Leaflet/OpenLayers-style tiled 3D terrain: drapes amc-web's color tile pyramid
 * (out/amc-web/map/tiles/) over a quadtree of terrain patches built from the
 * `heightmap` project's matching height tile pyramid (out/heightmap/tiles/). Both
 * pyramids use the same {z}_{x}_{y} naming and the same zoom-to-resolution scheme (z0 =
 * 1x1 grid, zN = 2^N x 2^N, `2^N * tileSize` total resolution) by construction - see
 * heightmap/ImageWriter.cs's WriteHeightTiles. scripts/prepare-assets.js copies both
 * pyramids as-is into public/assets/tiles/{height,color}/ plus a small tiles.json
 * metadata passthrough - no decoding/resampling in JS at all.
 *
 * Module layout:
 *  - constants.ts    - every tunable value, each with its own doc comment
 *  - types.ts        - shared interfaces (TilesMeta, LeafTile, ActiveTile, ...)
 *  - heightmap.ts    - tiles.json loading + tile-grid <-> world-space math
 *  - lod.ts          - selectLeafTiles(): the quadtree LOD selection algorithm itself
 *  - tileGeometry.ts - per-tile fetch (.bin/.avif) + mesh/border geometry building
 *  - tileManager.ts  - the load/unload/replace lifecycle that drives lod.ts's output
 *  - oceanQuad.ts    - the flat sea-level plane
 *  - cameraRig.ts    - camera/OrbitControls setup, zoom, rotate-speed/look-up feel
 *  - groundPan.ts    - ground-anchored left-drag panning
 *  - ringDebugView.ts / debugPanel.ts - the debug overlay (see the on-screen toggles)
 *
 * Every ~TILE_UPDATE_INTERVAL_MS, selectLeafTiles() (lod.ts) walks the quadtree from
 * z0 and decides, per node, whether to render it as a leaf (a real GPU tile) or
 * subdivide into its 4 children - see lod.ts's own doc comment for the full rule.
 */

import { TILE_UPDATE_INTERVAL_MS } from "./constants";
import { loadTilesMeta } from "./heightmap";
import { createOceanQuad } from "./oceanQuad";
import { createCameraRig } from "./cameraRig";
import { createTileManager } from "./tileManager";
import { createRingDebugView } from "./ringDebugView";
import { setupGroundPan } from "./groundPan";
import { createDebugPanel } from "./debugPanel";

async function main(): Promise<void> {
  const container = document.getElementById("app");
  if (!container) throw new Error("#app container not found");

  // logarithmicDepthBuffer: true - this scene mixes tiny (skirt/seam-scale) and huge
  // (map/far-clip-scale) depths in one standard depth buffer; without it, two nearly
  // coplanar surfaces far from the camera (most visibly the flat ocean quad against
  // gently-sloping shoreline terrain near the water's edge) z-fight heavily because a
  // linear depth buffer has almost no precision left out there. Reported as "heavy
  // ocean z-fighting" - this is the standard, well-documented Three.js fix for exactly
  // that failure mode, not a targeted hack for this one surface pair.
  const renderer = new THREE.WebGLRenderer({ antialias: true, logarithmicDepthBuffer: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  const skyColor = 0x8fb8e0;
  renderer.setClearColor(skyColor);
  container.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(skyColor);
  scene.fog = new THREE.Fog(skyColor, 25000, 100000);

  const { camera, controls, update: updateCameraRig } = createCameraRig(renderer);

  scene.add(new THREE.HemisphereLight(0xbfe0ff, 0x4a3d2a, 1.4));
  scene.add(new THREE.AmbientLight(0xffffff, 0.6));
  const sun = new THREE.DirectionalLight(0xfff3df, 2.4);
  sun.position.set(-8000, 12000, 6000);
  scene.add(sun);
  scene.add(sun.target); // stays at the default (0,0,0), which is where the map is centered

  const meta = await loadTilesMeta();

  createOceanQuad(scene, meta);

  const tileManager = createTileManager(scene, meta, renderer, camera, controls);
  const ringDebugView = createRingDebugView(scene, meta, camera, controls);

  function refreshVisibleTiles(): void {
    tileManager.updateVisibleTiles();
    ringDebugView.update();
  }

  controls.target.set(0, 0, 0);
  controls.update();
  refreshVisibleTiles();

  setupGroundPan(renderer, camera, controls, tileManager.tileGroup);

  // Debug controls - see ZOOM_DEBUG_COLORS' doc comment. Independent toggles: zoom
  // color replaces the real texture entirely; wireframe applies on top of whichever
  // material (real or debug-colored) is currently showing.
  const debugPanel = createDebugPanel();
  debugPanel.addToggle("debug: color by zoom", (checked) => tileManager.setZoomDebug(checked));
  debugPanel.addToggle("wireframe", (checked) => tileManager.setWireframe(checked));
  debugPanel.addToggle("show ring extents", (checked) => {
    ringDebugView.setVisible(checked);
    ringDebugView.update(); // populate immediately - refreshVisibleTiles() only runs ~150ms
  });

  const tileInfo = document.createElement("div");
  Object.assign(tileInfo.style, {
    position: "fixed", bottom: "12px", right: "8px", zIndex: "10",
    font: "12px monospace", color: "#cfe3ff", textAlign: "right",
  });
  document.body.appendChild(tileInfo);

  // Camera pose readout - lets a bug report include the exact reproduction
  // coordinates (position, rotation, and the orbit target/"lod0 point") instead of
  // a screenshot alone.
  const cameraInfo = document.createElement("div");
  Object.assign(cameraInfo.style, {
    position: "fixed", bottom: "12px", left: "8px", zIndex: "10",
    font: "12px monospace", color: "#cfe3ff", background: "rgba(0,0,0,0.35)",
    padding: "4px 6px", borderRadius: "4px", whiteSpace: "pre",
  });
  document.body.appendChild(cameraInfo);

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  let lastTileUpdate = 0;

  function animate(now: number): void {
    requestAnimationFrame(animate);
    updateCameraRig();

    if (now - lastTileUpdate > TILE_UPDATE_INTERVAL_MS) {
      lastTileUpdate = now;
      refreshVisibleTiles();
    }

    renderer.render(scene, camera);
    // renderer.info.render.triangles is reset and recomputed by render() every frame
    // (not throttled alongside refreshVisibleTiles() above) - actual GPU-submitted
    // triangle count for everything drawn this frame (tiles + skirts + the ocean
    // quad), not just a per-tile estimate multiplied out by hand.
    tileInfo.textContent = `${tileManager.activeTiles.size} tiles, ${renderer.info.render.triangles.toLocaleString()} tris`;
    const p = camera.position, r = camera.rotation, tgt = controls.target;
    cameraInfo.textContent =
      `camera pos:  ${p.x.toFixed(1)}, ${p.y.toFixed(1)}, ${p.z.toFixed(1)}\n` +
      `camera rot:  ${THREE.MathUtils.radToDeg(r.x).toFixed(1)}°, ${THREE.MathUtils.radToDeg(r.y).toFixed(1)}°, ${THREE.MathUtils.radToDeg(r.z).toFixed(1)}°\n` +
      `orbit target: ${tgt.x.toFixed(1)}, ${tgt.y.toFixed(1)}, ${tgt.z.toFixed(1)}`;
  }
  animate(0);
}

main().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="color:#f88;padding:16px;font:14px monospace">${err instanceof Error ? err.stack ?? err.message : String(err)}</pre>`;
});
