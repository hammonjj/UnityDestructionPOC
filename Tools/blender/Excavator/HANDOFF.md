# Excavator (prop) handoff

Low-poly tracked excavator (~20 t class) with four interchangeable demolition attachments: crusher, shear, breaker and grapple. It is a prop, not a destructible blockout, so the solid-box rules do not apply. The machine and all four attachments are in one FBX.

| Item | Value |
|---|---|
| Source | `Tools/blender/Excavator/Excavator.blend` (built by `Tools/blender/build_excavator.py`) |
| FBX | `DestructionPOC/Assets/Destruction/Models/Excavator/Excavator.fbx` (no `.meta` committed; Unity generates it) |
| Previews | `preview_three_quarter.png` (35 deg overhead, crusher mounted on the wrist), `preview_side.png`, `preview_attachments.png` (all four, side view), plus `qa_front.png`, `qa_top.png`, `qa_attachments_front.png` and `qa_wrist_<att>.png`. Built by `Tools/blender/render_excavator_previews.py [-- --extra]`. Ground, lights and the re-posing are session-only and are never saved or exported |
| Tris | 3,704 total: machine 2,212, crusher 320, shear 348, breaker 356, grapple 468 (31 meshes and empties) |
| Overall machine size | 2.64 m wide over the tracks (2.87 m including the cab steps) x 8.2 m long at rest (rear of the counterweight to the stick nose) x 5.84 m tall at rest (stick tail). Faces Blender +Y |
| Tracks | 3.77 m long, 0.6 m body (0.64 m over the cleats), 0.86 m tall, centred at design y = +-1.0 |
| Upper | 2.6 m wide x 3.25 m long (x -1.96 to +1.30; the boom-ram lugs reach +1.62). Engine hood top z 2.1, cab roof z 2.92 |
| Cab | Left front: design y 0.3 to 1.3 (1.0 m wide), x 0.0 to 1.3 |

## Coordinates

All positions below are in **design space**: x = forward, y = left, z = up, metres, with the origin on the ground on the slew axis. Blender = (-y, x, z). Every articulated part rotates about the design lateral axis, which is the **local X axis** of every node in Blender. "Pitch" means `rotation_euler.x`: positive pitch turns design +x (forward) toward +z (up).

In Unity the FBX root carries the axis conversion. As with the crane, the imported model faces -Z until you turn it. The FBX handedness flip mirrors X only, so a rotation about local X keeps its sign: `localRotation = authored * Quaternion.Euler(angle, 0, 0)` with the signed angles below. Check the direction once against the Bite or Grip anchor.

## Pivots and rest pose

| Node | Origin (world, design) | Local to parent (design) | Rest rotation (pitch) |
|---|---|---|---|
| `Excavator_Root` | (0, 0, 0) | | 0 |
| `Exc_Chassis` | (0, 0, 0) | (0, 0, 0) | 0 |
| `Exc_TrackL` / `Exc_TrackR` | (0, +1.0, 0) / (0, -1.0, 0) | same | 0 |
| `Exc_Upper` (slew axis, top of slew ring) | (0, 0, 1.10) | (0, 0, 1.10) | 0 (slew = rotate about local Z) |
| `Exc_Boom` (foot pin) | (1.15, 0, 1.60) | (1.15, 0, 0.50) | **+42.0 deg** (chord elevation) |
| `Exc_Stick` (boom-tip/stick pin) | (5.014, 0, 5.079) | (5.20, 0, 0) along the boom chord | **-110.0 deg** relative to the boom; world -68 deg, 22 deg off vertical, leaning forward |
| `Exc_Wrist` (attachment pin) | **(5.988, 0, 2.669)** | (2.60, 0, 0) along the stick | **+68.0 deg**, which cancels the parent rotations so world rotation = identity (residual 3e-8) |
| `Anchor_Seat` | (0.60, 0.80, 2.50) | (0.60, 0.80, 1.40) | 0 |
| `Anchor_Door` | (0.60, 1.90, 0.00) | (0.60, 1.90, -1.10) | 0 (ground, 0.6 m outside the left track, beside the steps) |

- The boom chord (foot pin to stick pin) is 5.20 m. Its banana profile rises at about 57 deg to the knee and then about 28 deg to the tip. The stick is 2.60 m from pin to pin, with a 0.8 m tail above the boom pin.
- If the wrist pose is changed in code, keep `Exc_Wrist` at world identity at rest, or re-derive it as `-(boom pitch + stick pitch)`.

## Rams

The rams are static meshes and do not animate correctly. Each double-acting ram is split into a barrel and a rod. Each half is parented to the part it is pinned to, has its origin at its own pin, and points at the other pin in the rest pose. When the boom or stick moves, the two halves stay attached at both ends and slide over each other, approximately.

| Node | Parent | Pin (world, design) |
|---|---|---|
| `Exc_BoomRam_L` / `_R` (barrels) | `Exc_Upper` | (1.45, +-0.36, 1.21) |
| `Exc_BoomRamRod_L` / `_R` | `Exc_Boom` | (2.51, +-0.36, 3.228) |
| `Exc_StickRam` (barrel) | `Exc_Boom` | (2.11, 0, 3.971) |
| `Exc_StickRamRod` | `Exc_Stick` | (4.659, 0, 5.691) |
| attachment ram and link plates | baked into `Exc_Stick` (both ends sit on the stick) | |

## Attachments

Each attachment root is an empty with **world rotation identity**, with its origin at its coupler pin. It hangs straight down from the pin (working end toward -z), and its jaws work in the design x-z plane about local X. To mount one, set the root's world pose to `Exc_Wrist`'s world pose; no extra rotation is needed. In the export the roots sit in a row at x = 9.0, z = 3.0: crusher y = +3.6, shear +1.2, breaker -1.2, grapple -3.6.

Each jaw mesh has its open pose baked in, with rotation 0 and its origin on its hinge pin. **The closing angle is the signed local-X rotation from the authored open pose to closed.** Values are measured on the exported meshes by rotating them and checking the tip positions.

| Attachment | Size (pin to lowest point) | Hinge(s), local to root (design) | Closing rotation (local X) | Result when closed | Anchor (local, design) |
|---|---|---|---|---|---|
| Crusher | 1.38 m open, 1.50 m closed; jaws 0.44 m wide; tips 1.20 m apart open | JawA (+0.16, 0, -0.78), JawB (-0.16, 0, -0.78) | **JawA -50.40 deg, JawB +50.40 deg** | tips meet at z -1.50 and overlap 4 cm | `Anchor_Crusher_Bite` (0, 0, -1.18) |
| Shear | 1.86 m (fixed lower jaw, hooked, two slotted plates) | Blade (0, 0, -0.80) | **Blade +40.00 deg** | blade tip at (+0.02, -1.73), inside the fixed jaw's slot; the cutting edges overlap about 0.15 m (scissor) | `Anchor_Shear_Cut` (-0.08, 0, -1.18), the throat |
| Breaker | 2.70 m retracted (housing and bracket 2.09 m, bit 0.85 m, 0.60 m of it showing) | n/a | n/a | n/a | `Anchor_Breaker_Tip` (bit-local 0, 0, -0.85), world-local (0, 0, -2.70) retracted |
| Grapple | 1.18 m open, 1.45 m closed; 1.65 m open span; ClawA has 2 tines, ClawB has 3, and they interleave | ClawA (+0.16, 0, -0.56), ClawB (-0.16, 0, -0.56) | **ClawA -59.55 deg, ClawB +59.55 deg** | tines interleave, tips pass 7 cm beyond each other at z -1.45 | `Anchor_Grapple_Grip` (0, 0, -1.05) |

**Breaker bit stroke: 0.25 m.** `Att_Breaker_Bit` has its origin at its top in the retracted position, at root-local (0, 0, -1.85). It extends by moving its local position 0.25 m along design -z, which is Blender local -Z. The tip goes from root-local z -2.70 to -2.95. `Anchor_Breaker_Tip` is a child of the bit, so it follows.

## Materials

There are five materials, with names and values identical to the crane so Unity remaps them to the existing crane materials. Each mesh carries only the slots it uses.

| Material | Used for |
|---|---|
| CraneYellow | upper, boom, stick, track-frame top plates, attachment bodies and brackets |
| CraneWornYellow | painted chip plates |
| CraneCharcoal | tracks, chassis, counterweight, cab roof, louvres, breaker clamp bands |
| CraneCabGlass | cab windows (opaque blue-grey) |
| CraneDarkSteel | slew ring, rollers, pins, rams, all jaws, blades, tines and the breaker bit |

## Hierarchy (all scale 1; rotation only on `Exc_Boom`, `Exc_Stick`, `Exc_Wrist`)

```
Excavator_Root
  Exc_Chassis
    Exc_TrackL / Exc_TrackR
  Exc_Upper
    Exc_Boom
      Exc_Stick
        Exc_Wrist                  (empty, world identity)
        Exc_StickRamRod
      Exc_StickRam
      Exc_BoomRamRod_L / _R
    Exc_BoomRam_L / _R
    Anchor_Seat
    Anchor_Door
Att_Crusher   -> Att_Crusher_Body, Att_Crusher_JawA, Att_Crusher_JawB, Anchor_Crusher_Bite
Att_Shear     -> Att_Shear_Body, Att_Shear_Blade, Anchor_Shear_Cut
Att_Breaker   -> Att_Breaker_Body, Att_Breaker_Bit -> Anchor_Breaker_Tip
Att_Grapple   -> Att_Grapple_Body, Att_Grapple_ClawA, Att_Grapple_ClawB, Anchor_Grapple_Grip
```

`Exc_Upper` is a sibling of `Exc_Chassis` under the root. This differs from the crane, where the carriage is a child of the base. Slewing the upper does not touch the undercarriage, and driving moves `Excavator_Root`. The attachment roots are FBX root nodes, siblings of `Excavator_Root`.

## Export settings

FBX, selection only (MESH and EMPTY, no lights, camera or ground). Scale 1.0, apply unit scale, `FBX_SCALE_NONE`, `bake_space_transform=False`, forward `-Z`, up `Y`, smoothing `FACE` (flat), no leaf bones. These are the same settings as the crane.

## Caveats

- Empties are exported, because they are needed for the anchors, the wrist and the attachment roots.
- Parts are overlapping solids (pins, lugs, chips, teeth), not one fused watertight mesh.
- At rest the stick is 22 deg off vertical, not quite "near vertical". With a ~5 m boom and the wrist about 6 m ahead of the slew axis, it cannot hang more steeply without a longer boom.
- The crusher's 1.5 m height holds with the jaws closed. Open, the jaws swing up to 1.38 m.
