"""Check a Blender scene against the Destruction Lab blockout contract.

    /Applications/Blender.app/Contents/MacOS/Blender --background your_building.blend \
        --python validate_blockout.py

Exits non-zero when anything would be skipped or misread on import, so it can gate a handover.
Add `-- --selected` to check only the selected objects.

The box test mirrors the Unity importer: a piece must fill its own world-space bounding box.
"""

import sys
from collections import Counter

import bmesh
import bpy
from mathutils import Vector

BOX_TOLERANCE = 0.02      # bounding box may hold at most 2% more volume than the mesh
MIN_SIZE = 0.02           # metres; thinner pieces are skipped by the importer
OVERLAP_TOLERANCE = 1e-4  # metres of interpenetration before it counts
TOUCH_TOLERANCE = 1e-3    # metres; faces this close count as touching
GROUND_TOLERANCE = 1e-3
MATERIAL_SUFFIXES = ("__concrete", "__wood", "__brick")

errors, warnings, notes = [], [], []


def mesh_volume(obj):
    """World-space volume of the object's mesh. Meaningless for open meshes, which we flag separately."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    open_edges = [e for e in bm.edges if len(e.link_faces) != 2]
    volume = abs(bm.calc_volume())
    bm.free()
    return volume, len(open_edges)


def world_box(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(c[i] for c in corners) for i in range(3)))
    hi = Vector((max(c[i] for c in corners) for i in range(3)))
    return lo, hi


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only_selected = "--selected" in args

    objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if only_selected:
        objects = [o for o in objects if o.select_get()]
    non_mesh = [o.name for o in bpy.context.scene.objects if o.type not in ("MESH", "EMPTY")]
    if non_mesh:
        warnings.append(f"ignored {len(non_mesh)} non-mesh object(s) (not exported): {', '.join(non_mesh[:5])}")

    if not objects:
        print("[blockout] no mesh objects found")
        return 1

    scale_length = bpy.context.scene.unit_settings.scale_length
    if abs(scale_length - 1.0) > 1e-6:
        errors.append(f"scene unit scale is {scale_length}, not 1.0; the lab works in metres")

    boxes = []   # (name, lo, hi) for pieces that passed the shape test
    for obj in objects:
        name = obj.name
        lo, hi = world_box(obj)
        size = hi - lo
        box_volume = size.x * size.y * size.z

        if any(abs(s - 1.0) > 1e-4 for s in obj.scale):
            warnings.append(f"{name}: scale is not applied ({tuple(round(s, 3) for s in obj.scale)})")
        if any(abs(r) > 1e-4 and abs(abs(r) % 1.5707963 - 0.0) > 1e-3 for r in obj.rotation_euler):
            errors.append(f"{name}: rotation is not a multiple of 90 degrees; it will be skipped on import")

        volume, open_edges = mesh_volume(obj)
        if open_edges:
            errors.append(f"{name}: mesh is not closed ({open_edges} open edge(s)); volume cannot be trusted")
            continue
        if volume <= 1e-9 or box_volume <= 1e-9:
            errors.append(f"{name}: has no volume; it will be skipped on import")
            continue

        error = box_volume / volume - 1.0
        if error >= BOX_TOLERANCE:
            errors.append(
                f"{name}: is not a solid axis-aligned box — its bounding box holds "
                f"{error * 100:.0f}% more volume than the mesh. It will be skipped on import. "
                "Rounded, tapered, rotated or boolean-cut shapes all fail this.")
            continue

        if min(size) < MIN_SIZE:
            errors.append(f"{name}: is {min(size):.3f} m at its thinnest, under {MIN_SIZE} m; it will be skipped")
            continue

        if not any(s in name.lower() for s in MATERIAL_SUFFIXES):
            notes.append(f"{name}: no material suffix, so it imports as concrete")

        boxes.append((name, lo, hi))

    # Overlaps. Overlapping pairs also count as connected below, so one mistake is reported once.
    overlapping = set()
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            na, loa, hia = boxes[i]
            nb, lob, hib = boxes[j]
            overlap = [min(hia[k], hib[k]) - max(loa[k], lob[k]) for k in range(3)]
            if all(o > OVERLAP_TOLERANCE for o in overlap):
                errors.append(f"{na}: overlaps {nb} by {min(overlap):.3f} m; pieces must touch, not interpenetrate")
                overlapping.add((na, nb))

    # Contact graph, so we can find floating pieces and anything with no path to the ground.
    ground = set()
    lowest = min(lo.z for _, lo, _ in boxes) if boxes else 0.0
    if abs(lowest) > GROUND_TOLERANCE:
        warnings.append(f"the model's lowest point is at z = {lowest:.3f}, not 0; it will be dropped onto the ground on import")

    neighbours = {name: set() for name, _, _ in boxes}
    for i in range(len(boxes)):
        na, loa, hia = boxes[i]
        if abs(loa.z - lowest) <= GROUND_TOLERANCE:
            ground.add(na)
        for j in range(i + 1, len(boxes)):
            nb, lob, hib = boxes[j]
            touching = True
            contact_axes = 0
            for k in range(3):
                gap = max(loa[k], lob[k]) - min(hia[k], hib[k])
                if gap > TOUCH_TOLERANCE:
                    touching = False
                    break
                if abs(gap) <= TOUCH_TOLERANCE:
                    contact_axes += 1
            # A real joint shares a face: touching on one axis and genuinely overlapping on the other two.
            if (touching and contact_axes == 1) or (na, nb) in overlapping:
                neighbours[na].add(nb)
                neighbours[nb].add(na)

    lonely = [n for n, links in neighbours.items() if not links and n not in ground]
    for n in lonely:
        errors.append(f"{n}: touches nothing and does not rest on the ground; it will fall immediately")

    reached, stack = set(ground), list(ground)
    while stack:
        current = stack.pop()
        for nxt in neighbours[current]:
            if nxt not in reached:
                reached.add(nxt)
                stack.append(nxt)
    unsupported = [n for n, _, _ in boxes if n not in reached and n not in lonely]
    for n in unsupported:
        warnings.append(f"{n}: has no path to the ground, so it will fall or hang from a joint")

    materials = Counter()
    for name, _, _ in boxes:
        tag = next((s for s in MATERIAL_SUFFIXES if s in name.lower()), "__concrete (default)")
        materials[tag] += 1

    print(f"\n[blockout] {len(objects)} mesh object(s), {len(boxes)} usable piece(s)")
    print(f"[blockout] materials: {dict(materials)}")
    print(f"[blockout] grounded pieces: {len(ground)}")
    if len(boxes) < 20:
        print("[blockout] note: under 20 pieces gives very coarse destruction; 40-150 is a good range for a building")

    for label, items in (("ERROR", errors), ("WARNING", warnings), ("NOTE", notes)):
        for item in items:
            print(f"[blockout] {label}: {item}")

    if errors:
        print(f"\n[blockout] FAILED with {len(errors)} error(s). Fix these before exporting.")
        return 1
    print(f"\n[blockout] PASSED ({len(warnings)} warning(s), {len(notes)} note(s)).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
