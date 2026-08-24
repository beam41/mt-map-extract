import * as THREE from "three";
import { OCEAN_QUAD_SIZE } from "./constants";
import type { TilesMeta } from "./types";

/** Flat ocean quad at the pak's own ocean level (see OceanExtractor.cs /
 * Jeju_World.json's "ocean" section) - a huge horizontal plane at the map's true,
 * unexaggerated ocean level. Returns null (and logs a warning) if the meta has no
 * ocean level at all. Deliberately NOT added to tileGroup by the caller: it must
 * never be a ground-anchored-pan raycast target (see groundPan.ts), or panning would
 * grab the ocean surface instead of the real terrain underneath it.
 *
 * Lower opacity + lighter, less saturated color than the first version (was color
 * 0x1c5f8a, opacity 0.85, which read as opaque/murky) - reported as "make water
 * clearer". Known limitation, not fixed by this: any road that's a real elevated
 * bridge in-game has no separate bridge-deck geometry here (only the landscape
 * heightmap + this flat sea-level quad), so it's draped on the underlying
 * (submerged) terrain and will still show through the water surface regardless of
 * clarity - see script/terrain-viewer/README.md's "Ocean quad" section. */
export function createOceanQuad(scene: THREE.Scene, meta: TilesMeta): THREE.Mesh | null {
  if (meta.oceanLevelMeters == null) {
    console.warn("tiles.json has no oceanLevelMeters - skipping the ocean quad");
    return null;
  }
  const geometry = new THREE.PlaneGeometry(OCEAN_QUAD_SIZE, OCEAN_QUAD_SIZE);
  geometry.rotateX(-Math.PI / 2); // PlaneGeometry is XY-facing by default; lay it flat (XZ)
  const material = new THREE.MeshStandardMaterial({
    color: 0x2f7fa8, roughness: 0.2, metalness: 0.05,
    transparent: true, opacity: 0.15, side: THREE.DoubleSide,
  });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.y = meta.oceanLevelMeters;
  scene.add(mesh);
  return mesh;
}
