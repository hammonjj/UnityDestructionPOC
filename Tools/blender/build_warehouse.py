"""Author the two-storey brick warehouse blockout and export it for the Destruction Lab.

Run headless from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/build_warehouse.py

It writes Tools/blender/Warehouse.blend and
DestructionPOC/Assets/Destruction/Models/Warehouse.fbx.

Layout (Blender Z-up, metres). Footprint x in [-5, 5], y in [-3.5, 3.5].
  * Long sides are the y = +/-3.5 faces. The roller door is in the south (-y) ground wall.
  * Ground storey z 0..3.4. Floor layer z 3.4..3.7. Upper storey z 3.7..6.6. Roof to 7.05.
  * The floor layer is a mezzanine over the back (north) half plus a perimeter edge strip
    in the front half, so upper walls always bear on concrete and the front stays open to the roof.
  * Per-storey piers and columns, so nothing passes through a slab.
  * Openings are gaps between panels; the door has a lintel that exactly fills it, windows have a
    sill panel below and a header above, both butting the panels either side.
Material comes from the name suffix: __concrete, __wood, __brick.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BLEND_PATH = os.path.join(REPO, "Tools", "blender", "Warehouse.blend")
FBX_PATH = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "Warehouse.fbx")

HX, HY = 5.0, 3.5          # half footprint (10 m x 7 m)
T = 0.3                    # wall thickness
PIER = 0.5                 # corner pier footprint
COL = 0.4                  # centre column footprint
Z0, Z1 = 0.0, 3.4          # ground storey
Z2 = 3.7                   # underside of the upper storey (top of the floor layer)
Z3 = 6.6                   # top of the upper walls
BEAM_H, PLANK_H = 0.3, 0.15
MEZ_Y0 = -0.2              # front edge of the mezzanine
DOOR_HALF = 1.5            # 3 m wide door, 3 m tall (lintel 0.4 above)
DOOR_H = 3.0
WIN_CENTRES = (-2.25, 2.25)
WIN_HALF = 0.7             # 1.4 m wide windows
SILL_TOP, HEAD_BOT = 4.6, 6.0   # window opening z 4.6..6.0


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0


def box(name, x0, x1, y0, y1, z0, z1):
    """An axis-aligned box piece spanning the given world extents."""
    centre = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=centre)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def splits(lo, hi, n):
    step = (hi - lo) / n
    return [(lo + i * step, lo + (i + 1) * step) for i in range(n)]


def build():
    pieces = []
    add = pieces.append
    ix, iy = HX - PIER, HY - PIER          # walls run between the piers: x +/-4.5, y +/-3.0

    for tag, za, zb in (("G", Z0, Z1), ("U", Z2, Z3)):
        # Corner piers.
        for sx, ex in ((-1, "W"), (1, "E")):
            for sy, ny in ((-1, "S"), (1, "N")):
                xs = sorted((sx * HX, sx * ix))
                ys = sorted((sy * HY, sy * iy))
                add(box(f"Pier_{tag}_{ex}{ny}__concrete", xs[0], xs[1], ys[0], ys[1], za, zb))

        # Long walls (north, south).
        for sy, face in ((-1, "S"), (1, "N")):
            ys = sorted((sy * HY, sy * (HY - T)))
            y0, y1 = ys
            k = 0
            if tag == "G" and sy < 0:
                # Roller door: two panels each side, lintel exactly fills the opening.
                for lo, hi in splits(-ix, -DOOR_HALF, 2) + splits(DOOR_HALF, ix, 2):
                    add(box(f"Wall_G_{face}{k}__brick", lo, hi, y0, y1, za, zb))
                    k += 1
                add(box(f"Lintel_G_{face}__concrete", -DOOR_HALF, DOOR_HALF, y0, y1, DOOR_H, Z1))
            elif tag == "G":
                for lo, hi in splits(-ix, ix, 6):
                    add(box(f"Wall_G_{face}{k}__brick", lo, hi, y0, y1, za, zb))
                    k += 1
            else:
                # Upper storey: full-height panels between two windows, sill and header at each window.
                edges = [-ix]
                for c in WIN_CENTRES:
                    edges += [c - WIN_HALF, c + WIN_HALF]
                edges.append(ix)
                solid = [(edges[0], edges[1]), (edges[2], edges[3]), (edges[4], edges[5])]
                # centre run between the windows is 3.1 m: split it in two.
                panels = [solid[0]] + splits(solid[1][0], solid[1][1], 2) + [solid[2]]
                for lo, hi in panels:
                    add(box(f"Wall_U_{face}{k}__brick", lo, hi, y0, y1, za, zb))
                    k += 1
                for j, c in enumerate(WIN_CENTRES):
                    lo, hi = c - WIN_HALF, c + WIN_HALF
                    add(box(f"Sill_U_{face}{j}__brick", lo, hi, y0, y1, za, SILL_TOP))
                    add(box(f"Lintel_U_{face}{j}__concrete", lo, hi, y0, y1, HEAD_BOT, zb))

        # Short walls (east, west), three panels each between the piers.
        for sx, face in ((-1, "W"), (1, "E")):
            xs = sorted((sx * HX, sx * (HX - T)))
            for k, (lo, hi) in enumerate(splits(-iy, iy, 3)):
                add(box(f"Wall_{tag}_{face}{k}__brick", xs[0], xs[1], lo, hi, za, zb))

    # Centre column row at y = 0, one set per storey, under the mezzanine edge and the roof beams.
    col_x = (-4.0, -2.0, 0.0, 2.0, 4.0)
    for tag, za, zb in (("G", Z0, Z1), ("U", Z2, Z3)):
        for i, x in enumerate(col_x):
            add(box(f"Column_{tag}_{i}__concrete", x - COL / 2, x + COL / 2, -COL / 2, COL / 2, za, zb))

    # Floor layer z 3.4..3.7: mezzanine bays over the back half, edge strips round the open front.
    xb = splits(-HX, HX, 5)
    yb = splits(MEZ_Y0, HY, 2)
    for i, (xlo, xhi) in enumerate(xb):
        for j, (ylo, yhi) in enumerate(yb):
            add(box(f"Floor_1_M{i}{j}__concrete", xlo, xhi, ylo, yhi, Z1, Z2))
    for i, (xlo, xhi) in enumerate(splits(-HX, HX, 2)):
        add(box(f"Floor_1_FrontEdge{i}__concrete", xlo, xhi, -HY, -iy, Z1, Z2))
    add(box("Floor_1_EdgeE__concrete", ix, HX, -iy, MEZ_Y0, Z1, Z2))
    add(box("Floor_1_EdgeW__concrete", -HX, -ix, -iy, MEZ_Y0, Z1, Z2))

    # Timber roof: beams across the short span, split at the column row, planks across them.
    for i, x in enumerate(col_x):
        for j, (ylo, yhi) in enumerate(((-HY, 0.0), (0.0, HY))):
            add(box(f"Beam_{i}{j}__wood", x - 0.15, x + 0.15, ylo, yhi, Z3, Z3 + BEAM_H))
    for i, (xlo, xhi) in enumerate(splits(-HX, HX, 4)):
        for j, (ylo, yhi) in enumerate(splits(-HY, HY, 4)):
            add(box(f"RoofPlank_{i}{j}__wood", xlo, xhi, ylo, yhi, Z3 + BEAM_H, Z3 + BEAM_H + PLANK_H))
    return pieces


def check_no_overlaps(pieces, tolerance=1e-4):
    """Authored pieces must touch, not interpenetrate, or the connection search misreads the structure."""
    boxes = []
    for p in pieces:
        c = p.matrix_world.translation
        s = p.dimensions
        boxes.append((p.name, [c[k] for k in range(3)], [s[k] for k in range(3)]))

    bad = []
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            (na, ca, sa), (nb, cb, sb) = boxes[i], boxes[j]
            overlap = [sa[k] / 2 + sb[k] / 2 - abs(ca[k] - cb[k]) for k in range(3)]
            if all(o > tolerance for o in overlap):
                bad.append(f"{na} overlaps {nb} by {min(overlap):.3f} m")
    if bad:
        raise SystemExit("[warehouse] pieces interpenetrate:\n  " + "\n  ".join(bad[:10]))
    print(f"[warehouse] overlap check passed for {len(boxes)} pieces")


def main():
    clear_scene()
    pieces = build()
    check_no_overlaps(pieces)
    for p in pieces:
        p.select_set(False)

    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False,
        object_types={"MESH"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        axis_forward="-Z",
        axis_up="Y",
    )

    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for p in pieces:
        for corner in p.bound_box:
            world = p.matrix_world @ Vector(corner)
            for k in range(3):
                lo[k] = min(lo[k], world[k])
                hi[k] = max(hi[k], world[k])
    print(f"[warehouse] {len(pieces)} pieces")
    print(f"[warehouse] extent x {hi[0]-lo[0]:.2f} m, y {hi[1]-lo[1]:.2f} m, z {hi[2]-lo[2]:.2f} m")
    print(f"[warehouse] ground z {lo[2]:.3f}")
    print(f"[warehouse] wrote {BLEND_PATH}")
    print(f"[warehouse] wrote {FBX_PATH}")


if __name__ == "__main__":
    main()
    sys.exit(0)
