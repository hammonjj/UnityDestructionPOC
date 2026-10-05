---
name: destruction-blockout
description: Author or fix a building blockout for the Unity Destruction Lab — a structure made of separate axis-aligned box pieces that the lab breaks apart. Use whenever modelling, converting or repairing a building, wall, tower, bridge or ruin that will be imported into the destruction POC (hammonjj/UnityDestructionPOC), when a model was rejected or skipped on import, when a structure collapses on load, or when asked for a "destructible", "blockout", "fracture-ready" or "structural proxy" building.
---

# Destruction blockout for the Unity Destruction Lab

The lab simulates structural failure: pieces carry load to the ground, joints overload and hinge, and violent hits shatter pieces into rubble. It does that by reading your model as a set of **axis-aligned box pieces that touch face to face**.

This is a structural proxy, not a beauty pass. Model it like a blockout: simple, blocky, deliberate. Detail, bevels, curves and trim belong on a separate render mesh that the lab never sees.

**The piece is the unit of destruction.** A piece is what breaks off, hinges, hangs and shatters. One wall modelled as one object breaks off as one slab. Split walls into panels, floors into bays, roofs into beams and planks.

## The contract

| # | Rule | Why |
|---|---|---|
| 1 | One mesh object per structural piece | The object *is* the piece |
| 2 | Every object is a **solid box**, axis-aligned. Rotations of exactly 90° are fine | The importer reads world-space bounding boxes |
| 3 | Pieces **touch, never overlap** | Connections are found from shared faces; overlap misreads the structure |
| 4 | Real-world **metres**, scale and rotation applied | Mass comes from volume, so wrong size means wrong behaviour |
| 5 | Pieces that should be anchored **rest on z = 0** | Ground contact is what anchors the structure |
| 6 | Material from a **name suffix** | No custom properties needed, survives any exporter |
| 7 | Openings are **gaps between panels**, never boolean holes | A hole leaves a box-shaped bounding volume with no mesh in it |

Break rule 2 or 7 and the piece is **skipped with a warning** — it vanishes from the structure. Break rule 3 and you get a warning plus a building that may collapse the moment it loads.

## Naming

```
Wall_U_N0__brick       role_storey_face_index + material
Pier_G_WS__concrete
Floor_1_02__concrete
Beam_1__wood
Statue__concrete__noshatter
```

- **Material suffix** (optional, defaults to concrete): `__concrete`, `__wood`, `__brick`.
- **`__noshatter`** (optional): the piece can break off and fall, but never fragments into rubble. Use for things that should stay recognisable.
- **Name prefix** (optional) hints at the role, which only affects the design floor load: `Floor`/`Slab`, `Pier`/`Column`/`Post`, `Wall`/`Panel`. Otherwise the shape decides.

Names must be unique. Duplicate-suffixed names from the exporter (`.001`) are fine.

## What good looks like

A two-storey cottage, 6 × 5 × 5.75 m, comes to about 50 pieces:

- **Corner piers per storey**, not one full-height pier. A full-height pier would have to interpenetrate the floor slab.
- **Walls between the piers**, split into 2–3 panels per face per storey.
- **Floor slab over the whole footprint**, as a 3 × 3 grid of bays, bearing on the storey below.
- **A door** as two wall panels with a gap, and a lintel exactly filling the gap so it butts against both panels.
- **A window** as a gap between panels, with nothing above it, or a panel resting on the panels either side.
- **Roof** as timber beams spanning the short way, with planks across them.
- **Parapets** only above solid wall, never floating over an opening.

Aim for **40–150 pieces** for a building. Fewer and nothing interesting breaks; many more and the simulation budget suffers.

## Make every piece carry load

Before you finish, trace the path to the ground for each piece. A piece whose only neighbour is above it will hang and tear off; a piece touching nothing will fall on the first frame. Both are usually mistakes.

Watch for the three that bite:

1. **A wall ending where no pier faces it.** Two boxes that merely share an edge or a corner do not connect; they need overlapping faces. If a side wall runs to y = 2.0 and the pier occupies x 2.5–3.0 while the wall occupies x 2.2–2.5, their faces share only a line, so there is no joint.
2. **A piece overhanging its support.** If an upper wall sits 10 cm proud of the slab below it, it bears on a 10 cm strip, which the lab will correctly overload and tear.
3. **A lintel overlapping the panels it sits on.** Make it exactly fill the opening.

## Export

FBX, selected objects, **scale 1**, **-Z forward, Y up**, no space-transform baking, mesh only. Apply scale and rotation first.

The importer reads world-space bounds, so it does not care how the exporter maps Blender's Z-up onto Unity's Y-up. It does care that your boxes are boxes.

## Validate before handing over

Run the checker in `validate_blockout.py` (next to this file). It applies the same rules as the importer and names every offending object.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background your_building.blend \
    --python validate_blockout.py
```

It exits non-zero when it finds errors. Treat that as "not ready to hand over". It reports:

- objects that are not solid axis-aligned boxes, with how much of their bounding box is empty;
- pieces that interpenetrate, and by how much;
- pieces touching nothing, and pieces with no path to the ground;
- unapplied scale or rotation, non-metre scale, and objects not resting on z = 0;
- a piece count and the material breakdown.

Fix everything it reports before exporting. A clean run is the handover criterion.

## Common rejections and what to do

| Symptom | Cause | Fix |
|---|---|---|
| "is not a solid box (it is rounded, tapered, or has holes cut into it)" | A cylinder, sphere, wedge, bevelled or boolean-cut mesh | Replace with a box, or split into boxes. Model openings as gaps. |
| "is rotated off the world axes" | Rotation that is not a multiple of 90°, or unapplied rotation | Apply rotation; snap to an axis. A tilted roof must be stepped boxes. |
| "overlaps X by 0.2 m" | Pieces interpenetrate | Move faces flush. Use per-storey piers so slabs do not cut through them. |
| "is thinner than 2 cm" | A detail sliver | Remove it, or merge into a neighbour. |
| Building collapses on load | Overlaps, or pieces bearing on a thin strip | Run the validator; check the three load-path traps above. |
| A piece is missing in the lab | It was skipped; read the console warning naming it | Fix the shape. |

## Out of scope

Rotated, organic or pre-fractured art meshes are **not supported**. Convex decomposition and baked contacts for arbitrary meshes are tracked separately. If the subject genuinely cannot be boxes, say so rather than shipping something that will be silently skipped.
