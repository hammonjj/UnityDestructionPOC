# Skid-steer loader (prop) handoff

Low-poly wheeled skid-steer loader (~3.5 t class, Cat 262 style): compact boxy body, open ROPS cab cage with overhead guard, rear engine hood with curved rear grille, four chunky tyres, radial-lift side arms and a 1.95 m bucket. It is a prop, not a destructible blockout, so the solid-box rules do not apply (the `Col_*` / `Vol_Cavity` helpers are the only box-constrained parts).

| Item | Value |
|---|---|
| Source | `Tools/blender/SkidSteer/SkidSteer.blend` (built by `Tools/blender/build_skid_steer.py`) |
| FBX | `DestructionPOC/Assets/Destruction/Models/SkidSteer/SkidSteer.fbx` (no `.meta` committed; Unity generates it) |
| Previews | `preview_three_quarter.png` (37 deg elevation, lift +35 / bucket curled, like the game camera), `preview_three_quarter_rest.png`, `preview_side.png`, `preview_side_raised_dump.png`, `preview_front.png`, `preview_top.png`. Built by `Tools/blender/render_skid_steer_previews.py`; ground, lights, posing and ram re-aiming are session-only and never saved or exported |
| Tris | 2,920 total: 2,740 visible (chassis 688, lift arm 360, bucket 284, four wheels 268 each, six ram halves 56 each) + 180 in the 15 collider/volume cubes (12 each) |
| Overall size at rest | 1.856 m over tyres (rims/hub flush with the tread), 1.95 m bucket, 3.623 m long (rear bumper x -1.323 to cutting edge x +2.300), cab guard top 2.10 m. Faces Blender +Y |
| Wheels | 0.85 m diameter (lugs included), 0.30 m wide, wheelbase 1.2 m (axles x = +-0.6), centres y = +-0.775, tyre bottom exactly z = 0 |

## Coordinates and sign conventions

Design space as in the crane/excavator: x forward, y left, z up, metres, origin on the ground at the centre of the wheelbase. Blender = (-y, x, z). Every animated node rotates about the design lateral axis = **local X** in Blender.

**Unlike the excavator, every animated node is authored in its rest pose with rotation 0** (geometry is baked at rest, origin on its pin). Unity applies signed deltas from identity.

| Node | Blender `rotation_euler.x` | Contract angle (what the brief calls "pitch") |
|---|---|---|
| `SS_LiftArm` | + turns design +x toward +z, so **+ raises the bucket end** | same as Blender, **+ = lift** |
| `SS_Bucket` | + curls back (cutting edge goes up) | **contract + = dump (edge goes down) = -rotation_euler.x**; contract - = curl back |
| `SS_Wheel_*` | spin about local X, either sign | n/a |

UNCERTAIN: the brief asks for "positive = lifts" on the arm and "positive = tips forward/dumps" on the bucket. With one Blender convention those two have opposite signs, so I defined the bucket contract angle as the negative of `rotation_euler.x`. In Unity the same `Quaternion.Euler(angle,0,0)` convention cannot give both; verify once against `Anchor_BucketEdge` (it must go down on dump and the pin must go up on lift) and flip the bucket sign in code if it is wrong. The excavator handoff's rule (X rotation keeps its sign through the FBX handedness flip) applies.

## Pivots and rest pose

| Node | Parent | Origin (world, design) | Local to parent (design) | Rest rotation |
|---|---|---|---|---|
| `SkidSteer_Root` | none | (0, 0, 0) | | 0 |
| `SS_Chassis` | Root | (0, 0, 0) | (0, 0, 0) | 0 |
| `SS_Wheel_FL` / `FR` / `RL` / `RR` | Chassis | (+-0.6, +-0.775, 0.425) (axle centre) | same | 0 (spin about local X; meshes are centred on the axle, lugs symmetric) |
| `SS_LiftArm` (lift pivot pin, rear top of chassis) | Root | (-0.65, 0, 1.65) | same | 0 |
| `SS_Bucket` (bucket pivot pin at arm tip) | LiftArm | (1.33, 0, 0.50) | (1.98, 0, -1.15) | 0 (floor parallel to ground, cutting edge z = 0) |
| `SS_LiftRam_L/R` (barrel) | Chassis | (-0.90, +-0.80, 0.90) | same | 0 |
| `SS_LiftRamRod_L/R` | LiftArm | (0.363, +-0.80, 1.004) | (1.013, +-0.80, -0.646) | 0 |
| `SS_TiltRam` (barrel) | LiftArm | (0.721, 0, 1.224) | (1.371, 0, -0.426) | 0 |
| `SS_TiltRamRod` | Bucket | (1.33, 0, 0.86) | (0, 0, 0.36) | 0 |
| `Anchor_Seat` (eye point) | Chassis | (-0.15, 0, 1.52) | same | |
| `Anchor_Door` (ground, front-left) | Chassis | (0.40, 1.35, 0) | same | |
| `Anchor_Exit_L` / `Anchor_Exit_R` (ground) | Chassis | (0, +1.45, 0) / (0, -1.45, 0) | same | |
| `Anchor_CameraFocus` (ground, 2 m ahead of the front axle) | Root | (2.6, 0, 0) | same | |
| `Anchor_BucketEdge` (cutting-edge tip) | Bucket | (2.30, 0, 0) | (0.97, 0, -0.50) | |
| `Anchor_BucketCavity` (middle of the load) | Bucket | (1.88, 0, 0.30) | (0.55, 0, -0.20) | |

Lift geometry: pivot to bucket pin is 2.2897 m, chord elevation -30.15 deg at rest.

## Verified articulation (posed headless; numbers are from the exported scene)

Lift (`SS_LiftArm`, delta from rest, + = up):

| Lift delta | Chord elevation | Bucket pin (x, z) |
|---|---|---|
| 0 (rest) | -30.15 deg | (1.330, 0.500) |
| 30 deg | -0.15 deg | (1.640, 1.644) |
| **+66.28 deg (max)** | +36.13 deg | **(1.199, 3.000)** |

Max lift limit is **+66.28 deg** (signed local X, from rest); pin height 3.000 m. Do not go below 0 (the bucket is on the ground there). Radial path: the pin moves 0.13 m rearward over the full lift.

Bucket tilt (`SS_Bucket`, contract angle, + = dump, - = curl), checked by sampling the bucket outline against the arm outline and against the chassis/ground:

| Limit | Value | Reason |
|---|---|---|
| Curl limit | **-55 deg** (rotation_euler.x +55) | beyond this the bucket back plate clips the arm; the required -45 has 10 deg margin |
| Dump limit | at least **+55 deg**, no arm clip up to +135 deg | arm clearance is not the constraint |
| Dump on the ground | the cutting edge digs below z = 0 for any dump when the lift is at rest | at +45 deg dump the lift must be above 11.25 deg; at +55 deg above 12.75 deg to keep the edge off the ground |
| Curl -45 | clear of ground, cab, wheels and hood at every lift | |

Poses (design, world): rest edge (2.300, 0.000); curl -45 at rest edge (2.369, 0.832); max lift, no tilt, edge (2.047, 3.687); **max lift with +55 dump: edge (2.248, 2.699), bucket back top (hinge-side lip) z 3.122**. Note the dump height is about 2.7 m at 55 relative deg, not the ~2.4 m in the brief: the bucket still points 11 deg above horizontal in world at that pose. Dumping to world -45 needs about +111 relative deg (edge z about 1.96 m), which is arm-clear but needs the tilt ram to reach about 1.4 m.

Clearances: the arm plates (y 0.645 to 0.725) pass 0.016 m above the tyres' circles at the tightest point over the whole lift range (tyre inner face is y 0.625, so they are over the tyre, not beside it). The body is 1.16 m wide (inside the tyres), the arms sit outside the cab cage (cage outer y +-0.55).

Rams (all static meshes, two halves, each origin on its own pin and pointing at the other pin at rest, same pattern as the excavator). The meshes are built straight along the rest pin-to-pin vector; Unity should rotate each half to aim at the other pin and stretch it along that axis by `current length / rest length`:

| Ram | Rest length | Over the full range |
|---|---|---|
| Lift (L and R) | 1.267 m | 1.639 at mid-lift, 1.889 at max (ratio 1.49) |
| Tilt | 0.709 m | 0.588 at curl -45, 1.041 at dump +55 |

Barrel meshes cover 60 percent of the rest length, rods 55 percent. The preview script re-aims and stretches the halves this way (`apply_pose`).

## Colliders (Unity builds BoxColliders from `Col_*`, hides them at import)

All are single cube meshes with no material, scale 1, rotation only where noted. Sizes are design (x, y, z).

| Node | Parent | Local centre | Size | Notes |
|---|---|---|---|---|
| `Col_Body` | Chassis | (-0.25, 0, 0.46) | 2.06 x 1.16 x 0.32 | belly and nose |
| `Col_Hood` | Chassis | (-0.875, 0, 0.96) | 0.85 x 1.16 x 0.68 | engine hood |
| `Col_Cab` | Chassis | (0.09, 0, 2.07) | 1.18 x 1.16 x 0.06 | overhead-guard slab only. The cab floor and the open sides have no collider, so the operator can stand and sit inside |
| `Col_Wheel_FL/FR/RL/RR` | Chassis | wheel centres | 0.85 x 0.30 x 0.85 | on the chassis, not the spinning wheel |
| `Col_Arm_L` / `Col_Arm_R` | LiftArm | (1.015, +-0.685, -0.532) (world 0.365, 1.118) | 2.29 x 0.08 x 0.26 | **rotated -30.15 deg about local X** to follow the arm chord |
| `Col_Bucket_Floor` | Bucket | (0.635, 0, -0.475) | 0.67 x 1.85 x 0.05 | |
| `Col_Bucket_Back` | Bucket | (0.145, 0, -0.10) | 0.05 x 1.85 x 0.40 | |
| `Col_Bucket_SideL` / `SideR` | Bucket | (0.545, +-0.95, -0.20) | 0.85 x 0.05 x 0.60 | |
| `Col_Bucket_Edge` | Bucket | (0.885, 0, -0.465) | 0.17 x 1.94 x 0.07 | |

The bucket opening is free of colliders (the chamfer and the sloped front of the side walls are slightly proud of the boxes, not the other way round). No convex hull.

`Vol_Cavity` (child of `SS_Bucket`, cube, hidden by Unity): bucket-local design bounds **x 0.17 to 0.97, y -0.925 to +0.925, z -0.45 to +0.10**, centre (0.57, 0, -0.175), size 0.80 x 1.85 x 0.55 = 0.81 m^3 of bounds. The actual interior (chamfer, sloped side-wall tops) is about 0.55 m^3, so a fill logic that trusts the bounds will be about 45 percent generous.

## Bucket

Closed solid, plate thickness 0.05 m (floor, back, side walls), outward normals (signed volume +0.2148 m^3; checked that the floor-top, back-inside and both wall-inside faces point into the cavity). Floor + 45 deg chamfer + back as one prism, two side walls with the top sloping from 0.60 m at the back to about 0.38 m at the lip, a protruding steel cutting-edge plate (extends 0.07 m past the floor), four rear ribs, a top back rail, two pivot ears (y 0.735 to 0.795, outside the arm plates) and a centre bracket for the tilt ram. Width 1.95 m, depth 0.97 m measured from the back to the edge tip (0.85 m floor depth), back height 0.60 m above ground.

## Materials

Five materials with names and values identical to the crane (Unity remaps to the existing ones). Collider and volume cubes carry none.

| Material | Used for |
|---|---|
| CraneYellow | body, hood, towers, lift arms, cross tube, bucket |
| CraneWornYellow | paint chips on the hood and sill |
| CraneCharcoal | tyres, skid plate, bumper, cab cage frame, roof guard bars, rear grille slats, vents, seat |
| CraneCabGlass | rear window, rear quarter windows, roof skylight under the guard bars |
| CraneDarkSteel | rims, hub nuts, pins, cutting edge, ram halves, exhaust, lap bar and levers |

## Hierarchy (all scale 1; rotation only on `SS_LiftArm`, `SS_Bucket` and the wheels in use; `Col_Arm_*` carry the fixed -30.15 deg)

```
SkidSteer_Root
  SS_Chassis
    SS_Wheel_FL / FR / RL / RR
    SS_LiftRam_L / SS_LiftRam_R              (barrels, origin at the chassis pin)
    Col_Body, Col_Hood, Col_Cab, Col_Wheel_FL / FR / RL / RR
    Anchor_Seat, Anchor_Door, Anchor_Exit_L, Anchor_Exit_R
  SS_LiftArm
    SS_Bucket
      Vol_Cavity, Col_Bucket_Floor / Back / SideL / SideR / Edge
      Anchor_BucketEdge, Anchor_BucketCavity
      SS_TiltRamRod                          (origin at the bucket pin)
    SS_LiftRamRod_L / SS_LiftRamRod_R        (origin at the arm pin)
    SS_TiltRam                               (barrel, origin at the arm pin)
    Col_Arm_L, Col_Arm_R
  Anchor_CameraFocus
```

The FBX was re-imported to check names, hierarchy and scale 1 on every node.

## Export settings

FBX, selection only (MESH and EMPTY), scale 1.0, apply unit scale, `FBX_SCALE_NONE`, `bake_space_transform=False`, forward `-Z`, up `Y`, smoothing `FACE` (flat), no leaf bones. Same as the crane and excavator.

## Caveats

- Differential steering is code only; the four wheels are centred on their axles and symmetrical, so they can spin about local X without wobble.
- The two lift rams stretch about 1.5x and the tilt ram about 1.8x over their ranges, so they must be re-aimed and stretched in code (see Rams). Left static they will detach.
- The brief's ~2.4 m dump height is not matched literally; see the poses paragraph. Pin height 3.0 m is exact.
- Parts overlap as solids (pins, ears, ribs), not one fused watertight mesh.
- The real machine is entered over the bucket; here the open left side of the cage is the way in. Cab sills are 0.95 m high, so the walk-up system should teleport to `Anchor_Seat` rather than path through.
- `Col_Arm_*` ride on `SS_LiftArm`, so they follow the lift; their -30.15 deg object rotation is fixed in the arm's local frame.
