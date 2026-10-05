# Wrecking Crane (prop) handoff

Low-poly crawler crane with suspended wrecking ball. A prop: not a destructible blockout, so the solid-box rules do not apply.

| Item | Value |
|---|---|
| Source | `Tools/blender/WreckingCrane/WreckingCrane.blend` (built by `Tools/blender/build_wrecking_crane.py`) |
| FBX | `DestructionPOC/Assets/Destruction/Models/WreckingCrane/WreckingCrane.fbx` |
| Previews | `preview_three_quarter.png`, `preview_side.png`, `preview_ball_closeup.png` (+ `qa_front.png`, `qa_top.png`) in this folder; render-only ground and lights, not in the export |
| Tris | 2,804 (9 meshes) |
| Overall size | 3.52 m wide (X) x 10.6 m long (Y, forward) x 11.35 m tall (Z), crane faces +Y |
| Tracks | 4.96 m long, 0.9 m wide each; crawler footprint 5.0 x 3.5 m |
| Cab / body top | cab roof 3.6 m, engine house 2.9 m, A-frame apex 5.6 m |
| Boom | 11 m, raised 55 deg, hinge at (0, 1.1, 2.1), tip at (0, 7.41, 11.11) |
| Ball | 1.5 m dia faceted icosphere (320 tris + collar + eye), centre (0, 7.41, 3.8), lowest point z = 3.05 |
| Suspension cable | 6.18 m, 6-sided, from boom tip anchor to ball eye top (z 4.93) |

## Materials (5, principled base colour / metallic / roughness only, no textures, no UVs)

CraneYellow, CraneWornYellow (chips), CraneCharcoal (tracks, counterweight, hazard black, support cables), CraneCabGlass (opaque blue-gray), CraneDarkSteel (ball, ball cable, wheels).

## Hierarchy (all scale 1, no non-uniform scale anywhere)

```
WreckingCrane_Root            (ground z=0, centred under base)
  Crane_Base                  (origin at root)
    Crane_TrackL / Crane_TrackR   (origin at ground, track centre)
    Crane_UpperCarriage       (origin on vertical slew axis, z=1.4)
      Crane_Boom              (origin at base hinge, rotated 55 deg about X)
        Anchor_BoomTipCable   (empty at boom tip)
  Crane_BoomSupportCable_L / _R   (origin at mast apex end)
  Crane_SuspensionCable       (origin at boom tip anchor, extends down local -Z)
  Crane_WreckingBall          (origin at ball centre; eye is part of the mesh)
    Anchor_BallAttach         (empty at eye top, where the cable ends)
```

Ball and cables are siblings under the root, so scaling the cable (Z) does not touch the ball.

## Export settings

FBX, selection only (MESH + EMPTY, no lights/camera/ground), scale 1.0, apply unit scale, `FBX_SCALE_NONE`, `bake_space_transform=False`, forward `-Z`, up `Y`, smoothing `FACE` (flat), no leaf bones. Same axis settings as the other models in this repo.

## Caveats

- Empties are exported (needed for anchors and root), unlike the mesh-only blockout exports.
- `Crane_UpperCarriage` has 1 non-manifold edge from overlapping solid parts; harmless for rendering.
- Pieces are overlapping solids (chips, louvres, lattice beams), not a fused watertight mesh.
