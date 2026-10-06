# Landfill dozer (prop) handoff

Low-poly landfill / waste-handler bulldozer (~38 t, Cat D8T-WH with SU-style blade and trash rack). It is a prop, not a destructible blockout, so the solid-box rules do not apply (only the `Col_*` helpers are boxes).

| Item | Value |
|---|---|
| Source | `Tools/blender/LandfillDozer/LandfillDozer.blend` (built by `Tools/blender/build_landfill_dozer.py`, which also prints the verification numbers below) |
| FBX | `DestructionPOC/Assets/Destruction/Models/LandfillDozer/LandfillDozer.fbx` (no `.meta` committed; Unity generates it) |
| Previews | `preview_three_quarter.png` (blade lifted 8 deg), `preview_side.png` (blade raised 30 deg), `preview_front.png` (rest), plus `qa_*.png` (top, side rest, side max lift, rear). Built by `Tools/blender/render_landfill_dozer_previews.py [-- --extra]`; ground, lights and posing are session-only |
| Tris | 5,146 visible (chassis 1,892, blade 982, tracks 2 x 648, sprockets 2 x 216, idlers 2 x 160, four ram halves 56 each) + 132 in the 11 hidden collider cubes |
| Overall size | 3.92 m over the tracks (shoes y +-1.96), blade 4.30 m wide (4.32 with trunnion bosses), 8.22 m long at rest (rear hitch x -3.55 to wrapped blade ends +4.67), 3.50 m to the cab roof (3.60 work lights, 3.64 exhaust cap), rack top 2.90 m |
| Tracks | 0.66 m shoes, centre y +-1.62 (inner face 1.29, outer 1.96), flat ground contact 3.2 m (x -1.55 to +1.65), whole track envelope x -2.71 to +2.36. Lowest vertex z = 0.000 |

## Coordinates

All positions are **design space**: x = forward, y = left, z = up, metres, origin on the ground at the centre of the track footprint. Blender = (-y, x, z); the machine faces Blender +Y. Every node keeps scale 1 and is authored at rest with rotation 0, geometry baked in the rest pose, node origin at its pivot. The only rotated nodes are the `Col_*` cubes if you re-pitch them (none are, all are axis-aligned here).

**Sign conventions (read this first)**

| Node | Axis | `rotation_euler` sign | Meaning |
|---|---|---|---|
| `DZ_Blade` | Blender local X | **positive = blade tip goes UP** | positive `rotation_euler.x` turns design +x toward +z. Verified: edge z 0 / 0.42 / 0.86 / 1.29 / 1.62 at 0 / 10 / 20 / 30 / 38 deg |
| `DZ_Sprocket_L/R`, `DZ_Idler_L/R` | Blender local X | **NEGATIVE = machine drives FORWARD** | verified headless: a tooth at the sprocket bottom moves design x +0.080 for +10 deg and -0.080 for -10 deg. When driving forward the track bottom runs backward, so spin with negative `rotation_euler.x` (positive = reverse) |

In Unity the imported FBX faces -Z until you turn it, as with the crane, excavator and loaders.

## Pivots and rest pose

| Node | Origin (world, design) | Parent | Notes |
|---|---|---|---|
| `LandfillDozer_Root` | (0, 0, 0) ground, track footprint centre | none | |
| `DZ_Chassis` | (0, 0, 0) | Root | hull, hood, cab, ROPS, stack, rear, track frames, belly guards, rollers, steps. Static |
| `DZ_Sprocket_L` / `R` | (-2.05, +-1.62, 1.25) axle centre | `DZ_Chassis` | spin about local X. Tip radius **0.46** (hull radius 0.62 incl. shoes), 12 teeth |
| `DZ_Idler_L` / `R` | (1.65, +-1.62, 0.71) axle centre | `DZ_Chassis` | spin about local X, radius 0.50 |
| `DZ_Track_L` / `R` | (0, +-1.62, 0) | `DZ_Chassis` | static chain ring with 0.3 m grousers; mesh only, no animation |
| `DZ_Blade` | (2.10, 0, 0.80) = push-arm trunnion pin on the lateral axis | Root | blade + both push arms + trash rack, one rigid mesh |
| `DZ_LiftRam_L` / `R` (barrels) | (2.30, +-1.05, 1.55) chassis pin | `DZ_Chassis` | points at the rod pin at rest |
| `DZ_LiftRamRod_L` / `R` | (3.95, +-1.05, 1.45) blade pin | `DZ_Blade` | points at the barrel pin at rest |
| `Anchor_BladeEdge` | (4.46, 0, 0) world; (2.36, 0, -0.80) blade-local | `DZ_Blade` | centre of the cutting edge on the ground at rest |
| `Anchor_Seat` | (-0.85, 0, 2.95) | `DZ_Chassis` | operator eye |
| `Anchor_Door` | (-0.65, +2.75, 0) | `DZ_Chassis` | ground beside the left cab steps |
| `Anchor_Exit_L` / `_R` | (0, +-2.85, 0) | `DZ_Chassis` | ground, 0.9 m clear of the tracks (outer y 1.96) |
| `Anchor_CameraFocus` | (8.46, 0, 0) | `DZ_Chassis` | ground, 4.0 m ahead of the blade edge |

## Blade, rack and arms

| Item | Value |
|---|---|
| **Blade face plane** | x = **4.40** (flat centre section, |y| < 1.50). Ends wrap forward to x = 4.62 at |y| = 2.15 (0.22 m over 0.65 m) |
| Face height | 1.90 m (z 0 to 1.90, including the 0.30 m edge plate). Top lip kicks forward to x = 4.47 |
| Cutting edge | steel plate x 4.30 to 4.42, z 0 to 0.30; five bolt-on segments (x to 4.46, z 0 to 0.24, 3 bolt heads each) plus a segment on each wrapped end. Edge bottom at **z = 0** exactly |
| **Grate top height** | **z = 2.90 m** (1.00 m above the blade top edge). 14 vertical bars (0.30 m pitch), horizontal bars at z 2.30 to 2.40 and 2.80 to 2.90, box side posts at y +-2.08, four back braces (y +-2.08 and +-1.30) down to a top girder on the blade back |
| Push arms | plates y +-2.00 to +-2.14 (outside the track outer face 1.96, 4 cm clear), x 1.78 to 4.30, z 0.42 to 1.18, trunnion bosses at (2.10, +-2.08, 0.80). Chassis pads at y 1.955 to 1.995 hold the pins |
| Rear of blade | top girder, mid girder, lower cross beam, 5 ribs, clevis plates for the rams (0.28 m gap) |

## Verified lift range (posed headless, 76 steps from 0 to 38 deg)

| Test | Result |
|---|---|
| Lift range | 0 to **+38 deg** (`DZ_Blade` `rotation_euler.x`) |
| Edge height at max | **1.623 m** (1.287 at 30, 0.855 at 20). Edge x goes 4.46 to 4.59 (at 20) and back to 4.45 |
| Rack top-front at max | z 3.85 m |
| Ground | blade lowest z = 0.00000 at every pose, never below ground |
| Mesh clipping | BVH overlap of `DZ_Blade` (blade, arms, rack) against `DZ_Chassis` (hood, cab, ROPS, stack, grille, pads), both tracks, both sprockets and both idlers: **none at any of the 76 poses** |
| Rams | axis-to-geometry clearance (ram radius 0.07 subtracted, clevis ends excluded) is >= 0.030 m against chassis, tracks and blade at every pose |
| Ram length (pin to pin) | rest **1.653 m (the longest)**, min 1.244 at 38 deg, 0.41 m stroke |
| Halves | each half is 0.62 x 1.653 = **1.025 m**; sum 2.05 m vs 1.24 m at the shortest pose, so barrel and rod halves overlap at every pose (checked) |

Unity re-aims both halves of each ram every frame (look at the other half's origin), as for the loaders. The sweep above did exactly that (quaternion from the rest direction to the current pin-to-pin direction).

## Solid check

`DZ_Blade` is 64 loose closed prisms (overlapping, not fused). Signed volume 3.26 m3 (positive = outward normals); no island has a non-positive volume; 538 of 538 random rays hit a face whose normal faces the ray origin (0 bad).

## Colliders (all cubes, axis-aligned, size baked into the mesh, no material, wire display and render-hidden in Blender; Unity hides `Col_*` by name)

| Node | Parent | Design bounds (world, rest) |
|---|---|---|
| `Col_Chassis` | `DZ_Chassis` | x -2.15 to 2.56, y +-1.22, z 0.45 to 1.30 |
| `Col_Cab` | `DZ_Chassis` | x -1.70 to 0.30, y +-1.02, z 1.30 to 3.50 |
| `Col_Track_L` / `R` | `DZ_Chassis` | x -2.67 to 2.30, y +-(1.29 to 1.95), z 0 to 1.89 |
| `Col_Hood` (extra) | `DZ_Chassis` | x 0.35 to 2.33, y +-0.85, z 1.30 to 2.15 |
| `Col_Rear` (extra) | `DZ_Chassis` | x -3.55 to -2.15, y +-1.15, z 0.75 to 1.95 |
| `Col_Blade_Face` | `DZ_Blade` | x 4.28 to 4.42, y +-2.15, z 0.30 to 1.90 |
| `Col_Blade_Rack` | `DZ_Blade` | x 4.26 to 4.40, y +-2.15, z 1.90 to 2.90 (one box over the whole grate) |
| `Col_Blade_Edge` | `DZ_Blade` | x 4.42 to 4.50, y +-2.15, z 0 to 0.14 (thin, low, in front of the face, bottom on the ground) |
| `Col_Arm_L` / `R` | `DZ_Blade` | x 1.78 to 4.30, y +-(2.00 to 2.14), z 0.46 to 1.12 |

## Materials

Five materials with the same names and values as the crane and the other machines. Each mesh carries only the slots it uses; the `Col_*` cubes carry none.

| Material | Used for |
|---|---|
| CraneYellow | hood, deck, cab, counterweight top, blade face/ends/back structure, push arms, rack-free paint |
| CraneWornYellow | blade lower wear strip, door panel, chip patches |
| CraneCharcoal | belly guards, track ring, counterweight, cab roof and window guard bars, grille frame, edge plate, mud-guard screens |
| CraneCabGlass | cab windows, head and work lights |
| CraneDarkSteel | grousers, sprocket and idler, rollers, rack bars and braces, edge segments, pins, rams, steps, exhaust pipe, drawbar |

## Hierarchy

```
LandfillDozer_Root
  DZ_Chassis
    DZ_Sprocket_L, DZ_Sprocket_R
    DZ_Idler_L, DZ_Idler_R
    DZ_Track_L, DZ_Track_R
    DZ_LiftRam_L, DZ_LiftRam_R
    Col_Chassis, Col_Hood, Col_Rear, Col_Cab, Col_Track_L, Col_Track_R
    Anchor_Seat, Anchor_Door, Anchor_Exit_L, Anchor_Exit_R, Anchor_CameraFocus
  DZ_Blade
    DZ_LiftRamRod_L, DZ_LiftRamRod_R
    Col_Blade_Face, Col_Blade_Rack, Col_Blade_Edge, Col_Arm_L, Col_Arm_R
    Anchor_BladeEdge
```

30 objects, all names unique, all scale 1. The FBX re-imports with the same names, parents and material assignment (checked in the build script). The FBX does not carry wire/hidden state, so Unity must hide `Col_*` by name.

## Export settings

FBX, selection only (MESH and EMPTY), scale 1.0, apply unit scale, `FBX_SCALE_NONE`, `bake_space_transform=False`, forward `-Z`, up `Y`, smoothing `FACE`, no leaf bones. The same as the other machines.

## Caveats

- **Length is 8.22 m, not ~9.5 m.** The lift requirement (edge to ~1.6 m at +38 deg) fixes the trunnion-to-edge distance at about 2.4 m (edge z = 0.8(1-cos t) + dx sin t), which puts the trunnion at x = 2.10, at the front of the track frames. Reaching 9.5 m would mean either arms ~4 m longer (edge height at 38 deg about 3 m) or a lift of only ~22 deg.
- The trunnion pins sit on pads outside the track outer face, not through the track frame, because the idler occupies that spot.
- Nothing rides on the track animation: `DZ_Track_*` is a static ring; Unity may scroll a texture or leave it. Sprocket/idler spin is cosmetic.
- Parts are overlapping solids (pins, braces, ribs, grousers), not one fused watertight mesh.
- Rams are rigid meshes and must be re-aimed by Unity. The blade does not tilt or angle.
- `Col_Hood` and `Col_Rear` are extra to the contract so the hood and counterweight are solid; delete them in Unity if unwanted.
- Cab headroom is tight (floor z 1.80, roof 3.50) because the floor sits above the track top for the walkway.
