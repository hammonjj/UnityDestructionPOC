"""Author the low-poly crawler crane with wrecking ball (a PROP, not a destructible blockout).

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/build_wrecking_crane.py

Writes Tools/blender/WreckingCrane/WreckingCrane.blend and
DestructionPOC/Assets/Destruction/Models/WreckingCrane/WreckingCrane.fbx.

Geometry is authored in "design space" (x = forward, y = left, z = up) and mapped to Blender
space by a +90 deg Z rotation of the vertex data, so the crane faces +Y (Unity +Z after export).
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "WreckingCrane")
BLEND_PATH = os.path.join(OUT_DIR, "WreckingCrane.blend")
FBX_DIR = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "WreckingCrane")
FBX_PATH = os.path.join(FBX_DIR, "WreckingCrane.fbx")

RZ = Matrix.Rotation(math.radians(90), 4, "Z")


def R(v):
    """Design-space point/offset -> Blender space."""
    return Vector((-v[1], v[0], v[2]))


# ---------------------------------------------------------------- materials
MAT_DEFS = [
    # name, base colour, metallic, roughness
    ("CraneYellow", (0.95, 0.62, 0.04, 1), 0.0, 0.6),
    ("CraneWornYellow", (0.66, 0.38, 0.04, 1), 0.0, 0.7),
    ("CraneCharcoal", (0.075, 0.08, 0.09, 1), 0.2, 0.65),
    ("CraneCabGlass", (0.30, 0.40, 0.50, 1), 0.0, 0.25),
    ("CraneDarkSteel", (0.13, 0.14, 0.16, 1), 0.85, 0.4),
]
YEL, WORN, CHAR, GLASS, STEEL = range(5)


def make_materials():
    mats = []
    for name, col, met, rough in MAT_DEFS:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = col
        b.inputs["Metallic"].default_value = met
        b.inputs["Roughness"].default_value = rough
        m.diffuse_color = col
        m.metallic = met
        m.roughness = rough
        mats.append(m)
    return mats


# ---------------------------------------------------------------- geometry helpers
def add_box(bm, lo, hi, mat):
    lo, hi = Vector(lo), Vector(hi)
    c, s = (lo + hi) / 2, hi - lo
    m = Matrix.Translation(c) @ Matrix.Diagonal((s.x, s.y, s.z, 1))
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = mat
    return r["verts"]


def add_beam(bm, a, b, size, mat, roll_ref=Vector((0, 0, 1))):
    a, b = Vector(a), Vector(b)
    d = b - a
    ln = d.length
    d.normalize()
    ref = roll_ref if abs(d.dot(roll_ref)) < 0.95 else Vector((0, 1, 0))
    u = d.cross(ref).normalized()
    v = d.cross(u).normalized()
    m = Matrix(((d.x * ln, u.x * size, v.x * size, (a.x + b.x) / 2),
                (d.y * ln, u.y * size, v.y * size, (a.y + b.y) / 2),
                (d.z * ln, u.z * size, v.z * size, (a.z + b.z) / 2),
                (0, 0, 0, 1)))
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    for f in {f for vv in r["verts"] for f in vv.link_faces}:
        f.material_index = mat


def add_cyl(bm, center, radius, length, axis, segs, mat, cap=True):
    """Cylinder along design axis 'x','y' or 'z'."""
    rot = {"z": Matrix.Identity(4),
           "x": Matrix.Rotation(math.radians(90), 4, "Y"),
           "y": Matrix.Rotation(math.radians(90), 4, "X")}[axis]
    m = Matrix.Translation(center) @ rot
    r = bmesh.ops.create_cone(bm, cap_ends=cap, segments=segs, radius1=radius, radius2=radius,
                              depth=length, matrix=m)
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = mat


def add_slanted_bar(bm, y0, w, z0, z1, x, thick, shift, mat):
    """Hazard stripe: parallelogram bar on a face normal to x, sheared along y."""
    r = bmesh.ops.create_cube(bm, size=1.0,
                              matrix=Matrix.Translation((x, y0, (z0 + z1) / 2))
                              @ Matrix.Diagonal((thick, w, z1 - z0, 1)))
    for v in r["verts"]:
        if v.co.z > (z0 + z1) / 2:
            v.co.y += shift
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = mat


def finish(name, bm, mats, loc=(0, 0, 0), flat=True):
    """Map to Blender space, fix normals, create object (location is design-space)."""
    bmesh.ops.transform(bm, matrix=RZ, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = not flat
    ob = bpy.data.objects.new(name, me)
    ob.location = R(loc)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def parent(child, par):
    child.parent = par
    child.matrix_parent_inverse = Matrix.Identity(4)


def empty(name, loc, par, size=0.3, kind="PLAIN_AXES"):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    parent(e, par)
    return e


# ---------------------------------------------------------------- parts
def build_track(name, side, mats):
    """Stadium-profile track, origin at ground level, centred under the track."""
    bm = bmesh.new()
    cz, rad, half = 0.55, 0.5, 1.95
    pts = []
    n = 6
    for i in range(n + 1):  # front arc
        a = math.radians(-90 + 180 * i / n)
        pts.append((half + rad * math.cos(a), cz + rad * math.sin(a)))
    for i in range(n + 1):  # rear arc
        a = math.radians(90 + 180 * i / n)
        pts.append((-half + rad * math.cos(a), cz + rad * math.sin(a)))
    w = 0.4  # half width of the track body
    ring_a = [bm.verts.new((x, -w, z)) for x, z in pts]
    ring_b = [bm.verts.new((x, w, z)) for x, z in pts]
    bm.faces.new(ring_a[::-1]).material_index = 0
    bm.faces.new(ring_b).material_index = 0
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((ring_a[i], ring_a[j], ring_b[j], ring_b[i])).material_index = 0
    # cleats along every profile edge
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        p0, p1 = Vector(pts[i]), Vector(pts[j])
        mid, d = (p0 + p1) / 2, p1 - p0
        ln = d.length
        if ln < 0.2:
            continue
        d.normalize()
        nrm = Vector((d.y, -d.x))  # outward for ccw profile
        c = mid + nrm * 0.0
        ang = math.atan2(d.y, d.x)
        m = (Matrix.Translation((c.x, 0, c.y)) @ Matrix.Rotation(-ang, 4, "Y")
             @ Matrix.Diagonal((ln * 0.5, 0.9, 0.1, 1)))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        for f in {f for v in r["verts"] for f in v.link_faces}:
            f.material_index = 0
    # bogie wheels on the outer face
    ys = 0.4 + 0.03 if side > 0 else -0.43
    for x in (-1.5, -0.75, 0.0, 0.75, 1.5):
        add_cyl(bm, (x, side * 0.43, 0.55), 0.26, 0.06, "y", 8, 1)
    add_cyl(bm, (-half, side * 0.43, 0.55), 0.36, 0.06, "y", 10, 1)
    add_cyl(bm, (half, side * 0.43, 0.55), 0.36, 0.06, "y", 10, 1)
    return finish(name, bm, [mats[CHAR], mats[STEEL]], loc=(0, side * 1.3, 0))


def build_base(mats):
    bm = bmesh.new()
    add_box(bm, (-1.8, -0.85, 0.4), (1.8, 0.85, 1.2), 0)
    add_cyl(bm, (0, 0, 1.3), 1.2, 0.2, "z", 12, 1)
    # track frame links to tracks (axle beams)
    add_box(bm, (-1.2, -0.9, 0.55), (1.2, 0.9, 0.9), 0)
    # yellow bumper blocks front/rear
    add_box(bm, (1.8, -0.7, 0.45), (2.0, 0.7, 0.95), 2)
    add_box(bm, (-2.0, -0.7, 0.45), (-1.8, 0.7, 0.95), 2)
    return finish("Crane_Base", bm, [mats[CHAR], mats[STEEL], mats[YEL]])


def build_carriage(mats):
    bm = bmesh.new()
    # deck
    add_box(bm, (-1.5, -1.3, 0.0), (1.5, 1.3, 0.45), 0)
    # engine house
    add_box(bm, (-1.5, -1.2, 0.45), (-0.1, 1.2, 1.5), 0)
    # engine louvres (dark) on both sides
    for s in (-1, 1):
        for i in range(3):
            x = -1.25 + i * 0.4
            add_box(bm, (x, s * 1.2, 0.75), (x + 0.25, s * 1.23, 1.35), 1)
    # exhaust stack
    add_cyl(bm, (-0.45, -0.7, 1.9), 0.1, 0.8, "z", 6, 1)
    # cab
    cab = add_box(bm, (0.1, 0.5, 0.45), (1.5, 1.3, 2.1), 0)
    add_box(bm, (0.0, 0.52, 2.1), (1.6, 1.4, 2.2), 1)  # roof slab
    add_box(bm, (1.5, 0.6, 0.95), (1.53, 1.2, 1.95), 3)  # front window
    add_box(bm, (0.3, 1.3, 0.95), (1.4, 1.33, 1.95), 3)  # outer side window
    add_box(bm, (0.07, 0.6, 0.95), (0.1, 1.2, 1.95), 3)  # rear window
    # cab steps/handrail post
    add_box(bm, (0.5, 1.3, 0.1), (1.3, 1.5, 0.2), 1)
    # counterweight (charcoal) with hazard band
    add_box(bm, (-2.4, -1.2, 0.1), (-1.5, 1.2, 1.5), 1)
    for i in range(8):
        y = -1.0 + i * 0.28
        add_slanted_bar(bm, y, 0.14, 1.0, 1.4, -2.41, 0.03, 0.25, 0)
    # A-frame mast
    apex = Vector((-1.7, 0, 4.2))
    for s in (-1, 1):
        add_beam(bm, (-1.0, s * 0.95, 1.5), (apex.x, s * 0.15, apex.z), 0.16, 0)
    add_beam(bm, (-1.35, -0.55, 2.7), (-1.35, 0.55, 2.7), 0.1, 0)
    add_box(bm, (apex.x - 0.15, -0.3, apex.z - 0.1), (apex.x + 0.15, 0.3, apex.z + 0.1), 1)
    # boom hinge lugs
    for s in (-1, 1):
        add_box(bm, (0.9, s * 0.45 - 0.05, 0.45), (1.3, s * 0.45 + 0.05, 0.9), 1)
    # worn / chipped patches (thin proud plates)
    add_box(bm, (-1.0, 1.2, 0.5), (-0.55, 1.215, 0.68), 2)
    add_box(bm, (0.6, 1.3, 0.5), (0.95, 1.315, 0.62), 2)
    add_box(bm, (-0.6, -1.3, 0.3), (-0.2, -1.315, 0.45), 2)
    add_box(bm, (1.5, 0.7, 0.5), (1.515, 1.0, 0.65), 2)
    return finish("Crane_UpperCarriage", bm, mats_sel(mats), loc=(0, 0, 1.4))


def mats_sel(mats):
    # slot order used by carriage builder: 0 yellow, 1 charcoal, 2 worn, 3 glass
    return [mats[YEL], mats[CHAR], mats[WORN], mats[GLASS]]


def add_chips(ob):
    """Re-assign a few faces to the worn-yellow slot for painted-chip variation."""
    pass


def build_boom(mats, length, panels):
    bm = bmesh.new()
    h0, h1 = 0.4, 0.17
    st = [i * length / panels for i in range(panels + 1)]
    half = [h0 + (h1 - h0) * s / length for s in st]
    corners = [(1, 1), (-1, 1), (-1, -1), (1, -1)]  # (y, z) signs

    def P(i, c):
        return (st[i], c[0] * half[i], c[1] * half[i])

    for c in corners:
        add_beam(bm, P(0, c), P(panels, c), 0.13, 0)
    for i in range(panels + 1):
        for k in range(4):
            add_beam(bm, P(i, corners[k]), P(i, corners[(k + 1) % 4]), 0.08, 0)
    for i in range(panels):
        for k in range(4):
            c0, c1 = corners[k], corners[(k + 1) % 4]
            if (i + k) % 2 == 0:
                add_beam(bm, P(i, c0), P(i + 1, c1), 0.07, 0)
            else:
                add_beam(bm, P(i, c1), P(i + 1, c0), 0.07, 0)
    # hinge pin + tip sheave housing
    add_cyl(bm, (0, 0, 0), 0.2, 0.95, "y", 8, 1)
    add_cyl(bm, (length, 0, 0), 0.25, 0.5, "y", 10, 1)
    # painted chip patches on the lower boom
    add_box(bm, (1.2, -0.43, 0.2), (1.9, -0.37, 0.3), 2)
    add_box(bm, (3.4, 0.37, -0.35), (3.9, 0.43, -0.25), 2)
    return bm


def build_cable(name, top, bottom, radius, segs, mat, par):
    """Low-sided cylinder, origin at `top` (design-space world point), pointing at `bottom`."""
    top, bottom = Vector(top), Vector(bottom)
    d = bottom - top
    ln = d.length
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius,
                          depth=ln, matrix=Matrix.Translation((0, 0, -ln / 2)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    wd = R(d)
    ob.rotation_euler = wd.to_track_quat("-Z", "Y").to_euler()
    ob.location = R(top)
    parent(ob, par)
    return ob


def build_ball(mats):
    bm = bmesh.new()
    rad = 0.75
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=rad)
    for f in bm.faces:
        f.material_index = 0
    # collar + attachment eye (torus standing in the plane containing the design-x axis)
    add_cyl(bm, (0, 0, rad - 0.02), 0.2, 0.1, "z", 8, 0)
    R_, r_ = 0.17, 0.05
    ez = rad + 0.04 + R_
    segs_major, segs_minor = 10, 6
    verts = []
    for i in range(segs_major):
        a = 2 * math.pi * i / segs_major
        ring = []
        for j in range(segs_minor):
            b = 2 * math.pi * j / segs_minor
            rr = R_ + r_ * math.cos(b)
            ring.append(bm.verts.new((rr * math.cos(a), r_ * math.sin(b), ez + rr * math.sin(a))))
        verts.append(ring)
    for i in range(segs_major):
        i2 = (i + 1) % segs_major
        for j in range(segs_minor):
            j2 = (j + 1) % segs_minor
            bm.faces.new((verts[i][j], verts[i][j2], verts[i2][j2], verts[i2][j])).material_index = 0
    eye_top_centre = ez + R_
    return bm, eye_top_centre


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    mats = make_materials()

    root = bpy.data.objects.new("WreckingCrane_Root", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 1.0
    sc.collection.objects.link(root)

    base = build_base(mats)
    parent(base, root)
    for nm, side in (("Crane_TrackL", 1), ("Crane_TrackR", -1)):
        t = build_track(nm, side, mats)
        parent(t, base)

    car = build_carriage(mats)
    parent(car, base)

    # --- boom
    BOOM_LEN, PANELS, ANG = 11.0, 10, 55.0
    hinge_local = Vector((1.1, 0, 0.7))  # design space, relative to carriage
    bm = build_boom(mats, BOOM_LEN, PANELS)
    boom = finish("Crane_Boom", bm, [mats[YEL], mats[CHAR], mats[WORN]])
    boom.location = R(hinge_local)
    boom.rotation_euler = Euler((math.radians(ANG), 0, 0))
    parent(boom, car)
    # boom mesh runs along design +x -> Blender +y; rotate about Blender X raises it.

    hinge_w = Vector((1.1, 0, 1.4 + 0.7))
    a = math.radians(ANG)
    tip_w = hinge_w + Vector((BOOM_LEN * math.cos(a), 0, BOOM_LEN * math.sin(a)))  # design space
    tip_anchor = empty("Anchor_BoomTipCable", (0, BOOM_LEN, 0), boom)

    # --- ball
    ball_c = Vector((tip_w.x, 0, 3.8))
    bm, eye_top = build_ball(mats)
    ball_mats = [mats[STEEL]]
    ball = finish("Crane_WreckingBall", bm, ball_mats, loc=ball_c)
    parent(ball, root)
    eye_w = ball_c + Vector((0, 0, eye_top))
    ball_anchor = empty("Anchor_BallAttach", R(Vector((0, 0, eye_top))), ball)

    # --- cables
    cable = build_cable("Crane_SuspensionCable", tip_w, eye_w, 0.04, 6, mats[STEEL], root)
    apex_w = Vector((-1.7, 0, 1.4 + 4.2))
    for nm, s in (("Crane_BoomSupportCable_L", 1), ("Crane_BoomSupportCable_R", -1)):
        top = apex_w + Vector((0, s * 0.15, 0))
        bot = tip_w + Vector((0, s * 0.2, 0.0))
        build_cable(nm, top, bot, 0.07, 6, mats[CHAR], root)

    # selection cleanup, validate, save
    for ob in bpy.data.objects:
        ob.select_set(False)
    bpy.context.view_layer.update()

    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(FBX_DIR, exist_ok=True)
    # apply scale/rotation sanity: only boom/cables carry rotation (by design, pivots); scale must be 1
    for ob in bpy.data.objects:
        assert all(abs(s - 1) < 1e-9 for s in ob.scale), ob.name
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    for ob in bpy.data.objects:
        ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False,
        object_types={"MESH", "EMPTY"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        axis_forward="-Z",
        axis_up="Y",
        use_custom_props=False,
    )
    tris = sum(len(p.vertices) - 2 for ob in bpy.data.objects if ob.type == "MESH" for p in ob.data.polygons)
    print("TRIS", tris, "TIP", tip_w, "EYE", eye_w, "BALL", ball_c)


main()
