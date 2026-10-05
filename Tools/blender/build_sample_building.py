"""Author the sample blockout building and export it for the Destruction Lab.

Run headless from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/build_sample_building.py

It writes Tools/blender/SampleBuilding.blend and
DestructionPOC/Assets/Destruction/Models/SampleBuilding.fbx.

Authoring convention (see README):
  * one mesh object per structural piece, each an unrotated box;
  * metres, scale applied, object origin at the box centre;
  * material from a name suffix: __concrete (default), __wood, __brick;
  * __noshatter marks a piece that may break off but never fragments.
Object names carry through the FBX, so the suffix is all Unity needs.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BLEND_PATH = os.path.join(REPO, "Tools", "blender", "SampleBuilding.blend")
FBX_PATH = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "SampleBuilding.fbx")


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0


def box(name, center, size):
    """A box piece. Blender is Z-up, so `center`/`size` are (x, y, z) with z as height."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


# Outer shell: x in [-3, 3], y in [-2.5, 2.5]. Pieces touch face to face and never interpenetrate.
W, D = 6.0, 5.0        # outer footprint
T = 0.3                # wall thickness
PIER = 0.5             # corner pier footprint
H1, H2 = 2.6, 2.4      # storey heights
SLAB = 0.25
TOP = H1 + SLAB        # underside of the upper storey
ROOF = TOP + H2


def span(lo, hi, axis_other, thickness, z0, z1):
    """Centre and size for a box spanning lo..hi on one axis, centred on `axis_other`."""
    return (lo + hi) / 2, hi - lo, axis_other, thickness, (z0 + z1) / 2, z1 - z0


def build():
    """A two-storey cottage: per-storey piers, walls with a door and a window, a floor, a timber roof."""
    pieces = []
    px, py = W / 2 - PIER / 2, D / 2 - PIER / 2          # pier centres
    wall_y = D / 2 - T / 2                                # north/south wall centreline
    wall_x = W / 2 - T / 2                                # east/west wall centreline
    inner_x = W / 2 - PIER                                # north/south walls run between the piers
    inner_y = D / 2 - PIER                                # east/west walls run between the piers

    for storey, (z0, z1, tag) in enumerate(((0.0, H1, "G"), (TOP, ROOF, "U"))):
        h = z1 - z0
        zc = (z0 + z1) / 2

        # Corner piers for this storey.
        for sx in (-1, 1):
            for sy in (-1, 1):
                pieces.append(box(
                    f"Pier_{tag}_{'E' if sx > 0 else 'W'}{'N' if sy > 0 else 'S'}__concrete",
                    (sx * px, sy * py, zc), (PIER, PIER, h)))

        # North and south walls, between the piers.
        for sy in (-1, 1):
            face = "N" if sy > 0 else "S"
            door = storey == 0 and sy < 0        # doorway on the ground-floor south face
            window = storey == 1 and sy > 0      # window on the upper north face
            if door or window:
                gap = 0.95 if door else 0.9
                for i, (lo, hi) in enumerate(((-inner_x, -gap), (gap, inner_x))):
                    pieces.append(box(
                        f"Wall_{tag}_{face}{i}__brick",
                        ((lo + hi) / 2, sy * wall_y, zc), (hi - lo, T, h)))
                if door:
                    # The lintel exactly fills the opening, so it butts against both panels.
                    pieces.append(box(
                        f"Lintel_{face}__concrete",
                        (0.0, sy * wall_y, z1 - 0.2), (2 * gap, T, 0.4)))
            else:
                width = 2 * inner_x / 3
                for i in range(3):
                    lo = -inner_x + i * width
                    pieces.append(box(
                        f"Wall_{tag}_{face}{i}__brick",
                        (lo + width / 2, sy * wall_y, zc), (width, T, h)))

        # East and west walls, between the piers, two panels each.
        for sx in (-1, 1):
            face = "E" if sx > 0 else "W"
            for i in range(2):
                lo = -inner_y + i * inner_y
                pieces.append(box(
                    f"Wall_{tag}_{face}{i}__brick",
                    (sx * wall_x, lo + inner_y / 2, zc), (T, inner_y, h)))

    # First floor slab over the whole footprint, 3 x 3 panels, bearing on the ground storey.
    for ix in range(3):
        for iy in range(3):
            pieces.append(box(
                f"Floor_1_{ix}{iy}__concrete",
                (-W / 2 + (ix + 0.5) * W / 3, -D / 2 + (iy + 0.5) * D / 3, H1 + SLAB / 2),
                (W / 3, D / 3, SLAB)))

    # Timber roof: beams across the short span, planks on top.
    for i, y in enumerate((-1.6, 0.0, 1.6)):
        pieces.append(box(f"Beam_{i}__wood", (0.0, y, ROOF + 0.1), (W, 0.25, 0.2)))
    for i, x in enumerate((-2.25, -0.75, 0.75, 2.25)):
        pieces.append(box(f"RoofPlank_{i}__wood", (x, 0.0, ROOF + 0.3), (1.4, 4.4, 0.2)))

    # Parapets, only above solid wall, deliberately small so they make good rubble.
    for sy in (-1, 1):
        face = "N" if sy > 0 else "S"
        if sy > 0:   # upper north wall has the window gap
            runs = ((-inner_x, -0.9), (0.9, inner_x))
        else:
            runs = ((-inner_x, 0.0), (0.0, inner_x))
        k = 0
        for lo, hi in runs:
            mid = (lo + hi) / 2
            for a, b in ((lo, mid), (mid, hi)):
                pieces.append(box(
                    f"Parapet_{face}{k}__brick",
                    ((a + b) / 2, sy * wall_y, ROOF + 0.25), (b - a, T, 0.5)))
                k += 1
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
        raise SystemExit("[sample-building] pieces interpenetrate:\n  " + "\n  ".join(bad[:10]))
    print(f"[sample-building] overlap check passed for {len(boxes)} pieces")


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
    print(f"[sample-building] {len(pieces)} pieces")
    print(f"[sample-building] extent x {hi[0]-lo[0]:.2f} m, y {hi[1]-lo[1]:.2f} m, z {hi[2]-lo[2]:.2f} m")
    print(f"[sample-building] ground z {lo[2]:.3f}")
    print(f"[sample-building] wrote {BLEND_PATH}")
    print(f"[sample-building] wrote {FBX_PATH}")


if __name__ == "__main__":
    main()
    sys.exit(0)
