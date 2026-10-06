# Wheel loader (prop) handoff

Low-poly articulated wheel loader (~18 t class, Cat 950 style). It is a prop, not a destructible blockout, so the solid-box rules do not apply (only the `Col_*` / `Vol_Cavity` helpers are boxes).

| Item | Value |
|---|---|
| Source | `Tools/blender/WheelLoader/WheelLoader.blend` (built by `Tools/blender/build_wheel_loader.py`, which also prints the verification numbers below) |
| FBX | `DestructionPOC/Assets/Destruction/Models/WheelLoader/WheelLoader.fbx` (no `.meta` committed; Unity generates it) |
| Previews | `preview_three_quarter.png` (37 deg overhead, bucket raised and curled, 12 deg steer), `preview_side.png`, `preview_front.png`, plus `qa_*.png` (top, raised, dumped, steer +-40, rear). Built by `Tools/blender/render_wheel_loader_previews.py [-- --extra]`; ground, lights and posing are session-only |
| Tris | 3,140 visible (rear frame 416, front frame 496, lift arm 316, bucket 344, four wheels 308 each, six ram halves 56 each, tilt ram halves 2 x 56) + 192 in the 16 hidden collider/volume cubes |
| Overall size | 3.00 m over the tires (bucket 3.20 m, the widest part) x 8.445 m long at rest (counterweight grille -2.945 to teeth +5.50) x 3.3 m to the cab roof (3.5 m to the beacon). Faces Blender +Y |
| Tires | 1.70 m dia x 0.65 m wide, centres at design y +-1.175, axles at x = +-1.70, z = 0.85. Lowest vertex z = -0.007 (tread block corners), contact z = 0 |

## Coordinates

All positions are **design space**: x = forward, y = left, z = up, metres, origin on the ground at the articulation pivot axis. Blender = (-y, x, z). Every node keeps scale 1. Rotation lives on the animated nodes only (and on `Col_Arm_*`, a baked tilt, see below).

**Sign conventions (read this first)**

| Node | Axis | `rotation_euler` sign | Meaning |
|---|---|---|---|
| `WL_FrontFrame` | Blender local Z (up) | **positive = turns toward design +y (left)** | steering, verified +-40 deg |
| `WL_LiftArm` | Blender local X | **positive = bucket end goes UP** | `rotation_euler.x` positive turns design +x toward +z |
| `WL_Bucket` | Blender local X | **positive rotation_euler.x = edge goes UP = curls BACK** | same sign rule as the arm, since it is the same axis |
| `WL_Wheel_*` | Blender local X | either | spin about the lateral axis |

**Conflict with the brief.** The brief says the arm lifts with positive pitch and the bucket dumps (edge down) with positive pitch. Those cannot both hold on the same local axis. I kept the arm as specified and the bucket on the same physical sense. **In the brief's bucket sign (positive = dump) the angle is `-rotation_euler.x`.** All bucket numbers below are given as `brief pitch` (dump +, curl -), with `rotation_euler.x = -brief pitch`. In Unity, flip the bucket sign once if it moves the wrong way. Check against `Anchor_BucketEdge`.

In Unity the imported FBX faces -Z until you turn it, as with the crane and excavator.

## Pivots and rest pose

| Node | Origin (world, design) | Local to parent (design) | Rest rotation |
|---|---|---|---|
| `WheelLoader_Root` | (0, 0, 0) ground, on the articulation axis | | 0 |
| `WL_RearFrame` | (0, 0, 0) | (0, 0, 0) | 0, static |
| `WL_FrontFrame` | (0, 0, 0), same point | (0, 0, 0) | 0; steer about local Z |
| `WL_Wheel_RL` / `RR` | (-1.70, +-1.175, 0.85) | same (parent `WL_RearFrame`) | spin local X |
| `WL_Wheel_FL` / `FR` | (+1.70, +-1.175, 0.85) | same (parent `WL_FrontFrame`) | spin local X |
| `WL_LiftArm` (lift pivot pin) | (1.30, 0, 2.25) | (1.30, 0, 2.25) | 0 |
| `WL_Bucket` (bucket pivot pin) | (3.72, 0, 1.45) | (2.42, 0, -0.80) in the arm | 0 (floor level, edge on ground) |
| `Anchor_Seat` | (0.15, 0, 2.65) | same, `WL_FrontFrame` | |
| `Anchor_Door` | (0.05, 1.40, 0) | same, `WL_FrontFrame` | ground beside the left-hand cab steps |
| `Anchor_Exit_L` / `_R` | (0, +-2.30, 0) | same, `WL_FrontFrame` | ground, clear of the tires |
| `Anchor_CameraFocus` | (4.70, 0, 0) | same, `WL_FrontFrame` | 3.0 m ahead of the front axle |
| `Anchor_BucketEdge` | (5.50, 0, 0) | (1.78, 0, -1.45) bucket-local | tooth tips, on the ground at rest |
| `Anchor_BucketCavity` | (4.295, 0, 0.31) | (0.575, 0, -1.14) bucket-local | 0.25 m above the floor top |

All the anchors in `WL_FrontFrame` rotate with steering (that is on purpose for the camera focus). Re-parent in Unity if you want them world-fixed.

## Verified articulation (measured headless by posing the .blend)

| Test | Result |
|---|---|
| Steering -40 / 0 / +40 | `WL_Wheel_FL` goes to (2.058, -0.193) / (1.70, 1.175) / (0.547, 1.993) design; bucket edge goes to y -3.54 / 0 / +3.54. The front frame bends at the pivot. No mesh overlap between front group and rear group at any steer from -40 to +40 (5 deg steps) |
| Lift range | 0 to 64 deg (the arm rotation limit I report). Pin height 1.45 m at 0, 2.33 at 20, 3.19 at 40, 3.77 at 55, 3.95 at 60, **4.07 m at 64 deg** |
| Lift for pin z = 4.00 m | **+61.65 deg** (`rotation_euler.x` of `WL_LiftArm`). |
| Arm vs front frame, tires, cab | No mesh overlap at any lift 0 to 64 (4 deg steps) |
| Bucket vs arm, 1 deg steps, every 8 deg of lift | **Clip-free for brief pitch from -110 (curl) to +67 (dump)** at every lift. The curl side was only swept to -110 and never clipped. Practical limit comes from the tilt ram, below |
| Bucket vs front frame and tires | Same range except at lift 0, where dumping past brief **+52** drives the bucket back into the front tire |
| Bucket stays above the ground | Dump to brief +55 needs a lift between 16 and 24 deg (measured in 8 deg steps: lift 16 allows dump up to +46 above ground, lift 24 allows +67). At lift 0 the bucket cannot dump at all without digging in (edge goes below z = 0); curl is free at every lift |
| **Recommended working limits** | **Brief pitch -45 (curl) to +55 (dump)** (`rotation_euler.x` = +45 to -55). Meets the required range. No mesh clipping at any lift above lift 8; at lift 0 the front tire clips beyond +52 |

Ram lengths (pin to pin) over lift 0 to 64 and brief pitch -50 to +60: tilt ram 1.15 to 1.47 m (rest 1.15, the minimum), lift ram 1.95 to 2.08 m (rest 1.95, the minimum; a short 0.13 m stroke).

## Rams

Same barrel/rod split as the excavator. Each half is parented to the part its pin is on, has its origin at that pin, and points at the other pin at rest. Unity must re-aim them each frame (look at the other half's origin). Each half is 0.62 x the longest ram length over the range, so the halves overlap at every pose.

| Node | Parent | Pin (world, design, at rest) |
|---|---|---|
| `WL_LiftRam_L` / `_R` (barrels) | `WL_FrontFrame` | (0.62, +-0.97, 1.55) |
| `WL_LiftRamRod_L` / `_R` | `WL_LiftArm` | (2.40, +-0.97, 2.35) |
| `WL_TiltRam` (barrel) | `WL_LiftArm` | (2.60, 0, 2.75) |
| `WL_TiltRamRod` | `WL_Bucket` | (3.32, 0, 1.85) |

The tilt ram connects directly from a lug on the arm cross tube to a lug above the bucket back. There is no Z-bar or bell crank.

## Bucket

Closed solids with outward normals (checked: every mesh has positive signed volume; rays cast from inside the cavity hit the floor, back and both side walls from the inside with normals facing the ray origin; the front is open). Plates are 0.06 m thick, the cutting-edge plate 0.10 m.

| Part | Description |
|---|---|
| Floor | 0.06 m plate, x -0.05 to 1.45 in bucket space |
| Back | 3 segments, height 1.10 m above the floor top, curved back, inner face at x about -0.43 |
| Side walls | 0.06 m plates, 0.8 m high at the back, tapering to 0.2 m at the front; y = +-1.54 to +-1.60 |
| Cutting edge | steel plate (x 1.30 to 1.62) with 7 teeth to x = 1.78 |
| Rear | three ribs, pin ears outside the arms (y 0.88 to 1.00), tilt-ram lug at (-0.40, 0, 0.40) |

Bucket-local coordinates (design axes, origin at the pivot pin, edge on the ground at z = -1.45):

| Item | Value |
|---|---|
| `Vol_Cavity` bounds | x -0.40 to 1.55, y -1.54 to +1.54, z -1.39 to -0.59 (centre (0.575, 0, -0.99), size 1.95 x 3.08 x 0.80) |
| `Vol_Cavity` box volume | 4.80 m3. This is the bounding box (side-wall top at the BACK height). The true tapered interior, integrated numerically, is **2.62 m3**, so scale a fill fraction by about 0.55 if you want true capacity |
| `Anchor_BucketEdge` | (1.78, 0, -1.45) |
| `Anchor_BucketCavity` | (0.575, 0, -1.14) |

## Colliders (all cubes, axis-aligned in the parent space, rotation allowed, size baked into the mesh)

| Node | Parent | Design bounds (parent space) |
|---|---|---|
| `Col_Rear` | `WL_RearFrame` | x -2.92 to -0.95, y +-0.8, z 0.5 to 1.85 |
| `Col_RearHood` | `WL_RearFrame` | x -2.92 to -1.0, y +-1.15, z 1.85 to 2.5 |
| `Col_Front` | `WL_FrontFrame` | x -0.55 to 2.09, y +-0.5, z 0.72 to 1.42 |
| `Col_Cab` | `WL_FrontFrame` | x -0.4 to 1.05, y +-0.88, z 1.42 to 3.4 |
| `Col_Wheel_RL` / `RR` | `WL_RearFrame` | tire box, 1.7 x 0.65 x 1.7 at the axle, not on the spinning wheel |
| `Col_Wheel_FL` / `FR` | `WL_FrontFrame` | same |
| `Col_Arm_L` / `Col_Arm_R` | `WL_LiftArm` | one box each along the pivot-to-pin chord, 2.60 x 0.16 x 0.50, **rotated -21.43 deg** (rest slope), centred at y = +-0.78 |
| `Col_Bucket_Floor` | `WL_Bucket` | x -0.45 to 1.50, y +-1.6, z -1.45 to -1.39 |
| `Col_Bucket_Back` | `WL_Bucket` | x -0.50 to -0.40, y +-1.6, z -1.45 to -0.29 |
| `Col_Bucket_SideL` / `SideR` | `WL_Bucket` | x -0.50 to 1.50, y 1.54 to 1.60 (mirrored), z -1.45 to -0.59 |
| `Col_Bucket_Edge` | `WL_Bucket` | x 1.30 to 1.62, y +-1.6, z -1.45 to -1.35 |
| `Vol_Cavity` | `WL_Bucket` | see above |

The opening (above the floor, in front of the back, between the side walls) is free of colliders. The back box starts at x -0.40, which is the cavity's minimum x. The lowest part of the curved back is a bit proud of the box towards the floor, and the cavity box is only a bounding box there.

## Materials

Five materials with the same names and values as the crane and excavator, so Unity remaps them. Each mesh carries only the slots it uses; the `Col_*` / `Vol_Cavity` cubes carry none.

| Material | Used for |
|---|---|
| CraneYellow | hood, cab, front-frame deck and towers, lift arms, bucket sides and ribs, mudguards |
| CraneWornYellow | bucket floor and back, chip plates and wear strips |
| CraneCharcoal | tires, lower frame, counterweight, cab roof, louvres, grille |
| CraneCabGlass | cab windows, lights |
| CraneDarkSteel | rims, hubs, axles, pins, rams, steps, exhaust, bucket cutting edge and teeth |

## Hierarchy

```
WheelLoader_Root
  WL_RearFrame
    WL_Wheel_RL, WL_Wheel_RR
    Col_Rear, Col_RearHood, Col_Wheel_RL, Col_Wheel_RR
  WL_FrontFrame
    WL_Wheel_FL, WL_Wheel_FR
    WL_LiftArm
      WL_Bucket
        WL_TiltRamRod
        Vol_Cavity, Col_Bucket_Floor/Back/SideL/SideR/Edge
        Anchor_BucketEdge, Anchor_BucketCavity
      WL_TiltRam
      WL_LiftRamRod_L, WL_LiftRamRod_R
      Col_Arm_L, Col_Arm_R
    WL_LiftRam_L, WL_LiftRam_R
    Col_Front, Col_Cab, Col_Wheel_FL, Col_Wheel_FR
    Anchor_Seat, Anchor_Door, Anchor_Exit_L, Anchor_Exit_R, Anchor_CameraFocus
```

38 objects, all names unique, all scale 1. The FBX re-imports with the same names, parents and material assignment (checked). The `.blend` has the colliders as wire display and render-hidden; the FBX does not carry that, so Unity must hide `Col_*` and `Vol_Cavity` by name.

## Export settings

FBX, selection only (MESH and EMPTY), scale 1.0, apply unit scale, `FBX_SCALE_NONE`, `bake_space_transform=False`, forward `-Z`, up `Y`, smoothing `FACE`, no leaf bones. The same as the crane and excavator.

## Caveats

- Bucket sign conflicts with the brief; see "Sign conventions".
- Pin height at rest is 1.45 m, not the 1.3 m I started with: at 1.3 m the bucket back hit the arm at brief +55 dump. 1.45 m clears to +67.
- Cab roof is 3.30 m; the beacon reaches 3.5 m.
- Parts are overlapping solids (pins, ears, ribs, chips), not one fused watertight mesh.
- Rams are rigid meshes and must be re-aimed by Unity. The lift ram has only a 0.13 m stroke in this layout.
- Steering +-40 does not collide in my mesh test, but the ladder and cab rear corner come within about 0.1 m of the rear hood at full left lock.
- The tire tread blocks are 16 boxes per tire, so a wheel lowest point varies by 7 mm as it spins (z -0.007 to 0).
- The ladder and exit anchors rotate with the front frame.
