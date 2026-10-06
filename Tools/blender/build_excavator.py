"""Author the low-poly tracked excavator with four demolition attachments (a PROP, not a blockout).

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/build_excavator.py

Writes Tools/blender/Excavator/Excavator.blend and
DestructionPOC/Assets/Destruction/Models/Excavator/Excavator.fbx (machine + all four attachments).

Same conventions as build_wrecking_crane.py: geometry is authored in "design space"
(x = forward, y = left, z = up) and mapped to Blender space by a +90 deg Z rotation, so the machine
faces Blender +Y. Every articulated part rotates about the design lateral axis (design y), which is
Blender local X, so a "pitch" below is rotation_euler.x: positive pitch turns design +x toward +z.
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "Excavator")
BLEND_PATH = os.path.join(OUT_DIR, "Excavator.blend")
FBX_DIR = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "Excavator")
FBX_PATH = os.path.join(FBX_DIR, "Excavator.fbx")

RZ = Matrix.Rotation(math.radians(90), 4, "Z")


def R(v):
    """Design-space point/offset -> Blender space."""
    return Vector((-v[1], v[0], v[2]))


# ---------------------------------------------------------------- layout (design space, metres, degrees)
UPPER_Z = 1.10                      # top of slew ring = Exc_Upper origin height
TRACK_Y = 1.00                      # track centre lines at +-1.0 (0.6 wide -> 2.6 overall)
BOOM_FOOT = (1.15, 0.0, 0.50)       # relative to Exc_Upper
BOOM_LEN = 5.20                     # foot pin -> stick pin (chord of the banana)
BOOM_PITCH = 42.0                   # chord elevation at rest
STICK_LEN = 2.60                    # stick pin -> wrist pin
STICK_PITCH_REL = -110.0            # relative to boom -> world -68 deg (22 deg off vertical, leaning forward)
ATT_ROW_X, ATT_ROW_Z = 9.0, 3.0     # attachment display row (roots), spaced along design y
ATT_ROW_Y = {"Crusher": 3.6, "Shear": 1.2, "Breaker": -1.2, "Grapple": -3.6}

# ---------------------------------------------------------------- materials (identical to the crane)
MAT_DEFS = [
    ("CraneYellow", (0.95, 0.62, 0.04, 1), 0.0, 0.6),
    ("CraneWornYellow", (0.66, 0.38, 0.04, 1), 0.0, 0.7),
    ("CraneCharcoal", (0.075, 0.08, 0.09, 1), 0.2, 0.65),
    ("CraneCabGlass", (0.30, 0.40, 0.50, 1), 0.0, 0.25),
    ("CraneDarkSteel", (0.13, 0.14, 0.16, 1), 0.85, 0.4),
]
YEL, WORN, CHAR, GLASS, STEEL = range(5)
MATS = []


def make_materials():
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
        MATS.append(m)


# ---------------------------------------------------------------- geometry helpers
def _mat(faces, mat):
    for f in faces:
        f.material_index = mat


def add_box(bm, lo, hi, mat):
    lo, hi = Vector(lo), Vector(hi)
    c, s = (lo + hi) / 2, hi - lo
    m = Matrix.Translation(c) @ Matrix.Diagonal((s.x, s.y, s.z, 1))
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def add_cyl(bm, center, radius, length, axis, segs, mat):
    """Cylinder along axis 'x', 'y' or 'z' of whatever space the bmesh is in."""
    rot = {"z": Matrix.Identity(4),
           "x": Matrix.Rotation(math.radians(90), 4, "Y"),
           "y": Matrix.Rotation(math.radians(90), 4, "X")}[axis]
    m = Matrix.Translation(center) @ rot
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius,
                              depth=length, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def add_tube(bm, a, b, radius, segs, mat, radius2=None):
    """Cylinder (or cone if radius2 given) from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    m = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius,
                              radius2=radius if radius2 is None else radius2, depth=d.length, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def add_prism_xz(bm, pts, y0, y1, mat):
    """Closed prism: polygon (x, z) profile extruded across design y from y0 to y1."""
    a = [bm.verts.new((x, y0, z)) for x, z in pts]
    b = [bm.verts.new((x, y1, z)) for x, z in pts]
    faces = [bm.faces.new(a[::-1]), bm.faces.new(b)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    _mat(faces, mat)


def add_prism_xy(bm, pts, z0, z1, mat):
    """Closed prism: polygon (x, y) footprint extruded from z0 to z1."""
    a = [bm.verts.new((x, y, z0)) for x, y in pts]
    b = [bm.verts.new((x, y, z1)) for x, y in pts]
    faces = [bm.faces.new(a[::-1]), bm.faces.new(b)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    _mat(faces, mat)


def strip_outline(centre, half):
    """Polygon (x, z) outlining a tapered bar that follows a centre polyline."""
    left, right = [], []
    n = len(centre)
    for i in range(n):
        p = Vector(centre[i])
        t = (Vector(centre[min(i + 1, n - 1)]) - Vector(centre[max(i - 1, 0)])).normalized()
        nrm = Vector((-t.y, t.x))
        left.append(tuple(p + nrm * half[i]))
        right.append(tuple(p - nrm * half[i]))
    return left + right[::-1]


def rot_xz(pts, deg):
    """Rotate (x, z) points about the origin; positive turns +x toward +z (= positive pitch)."""
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [(x * c - z * s, x * s + z * c) for x, z in pts]


def finish(name, bm, loc=(0, 0, 0), rz=True):
    """Map to Blender space (unless rz=False), fix normals, keep only used material slots."""
    if rz:
        bmesh.ops.transform(bm, matrix=RZ, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])  # concave caps
    used = sorted({f.material_index for f in bm.faces})
    remap = {u: i for i, u in enumerate(used)}
    for f in bm.faces:
        f.material_index = remap[f.material_index]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for u in used:
        me.materials.append(MATS[u])
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    ob.location = R(loc) if rz else Vector(loc)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def parent(child, par):
    child.parent = par
    child.matrix_parent_inverse = Matrix.Identity(4)


def empty(name, loc, par, size=0.3, kind="PLAIN_AXES"):
    """loc is design space, relative to an un-rotated parent frame."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    e.location = R(loc)
    bpy.context.scene.collection.objects.link(e)
    if par is not None:
        parent(e, par)
    return e


def update():
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- undercarriage
def build_track(name, side):
    """Stadium-profile track (3.8 x 0.6 m), origin at ground level under the track centre."""
    bm = bmesh.new()
    cz, rad, half = 0.43, 0.38, 1.47
    pts = []
    n = 6
    for i in range(n + 1):
        a = math.radians(-90 + 180 * i / n)
        pts.append((half + rad * math.cos(a), cz + rad * math.sin(a)))
    for i in range(n + 1):
        a = math.radians(90 + 180 * i / n)
        pts.append((-half + rad * math.cos(a), cz + rad * math.sin(a)))
    add_prism_xz(bm, pts, -0.3, 0.3, CHAR)
    for i in range(len(pts)):  # cleats
        j = (i + 1) % len(pts)
        p0, p1 = Vector(pts[i]), Vector(pts[j])
        mid, d = (p0 + p1) / 2, p1 - p0
        ln = d.length
        if ln < 0.15:
            continue
        ang = math.atan2(d.y, d.x)
        m = (Matrix.Translation((mid.x, 0, mid.y)) @ Matrix.Rotation(-ang, 4, "Y")
             @ Matrix.Diagonal((ln * 0.5, 0.64, 0.1, 1)))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        _mat({f for v in r["verts"] for f in v.link_faces}, CHAR)
    # rollers, idler (front) and sprocket (rear) on the outer face
    yo = side * 0.33
    for x in (-0.95, -0.32, 0.32, 0.95):
        add_cyl(bm, (x, yo, 0.3), 0.17, 0.06, "y", 8, STEEL)
    add_cyl(bm, (half, yo, cz), 0.3, 0.07, "y", 10, STEEL)
    add_cyl(bm, (-half, yo, cz), 0.32, 0.07, "y", 10, STEEL)
    add_cyl(bm, (-half, side * 0.37, cz), 0.14, 0.05, "y", 6, YEL)  # final drive hub
    # track frame top plate, yellow, inside the loop (reads between the cleats from above)
    add_box(bm, (-1.2, -0.24, 0.62), (1.2, 0.24, 0.79), YEL)
    return finish(name, bm, loc=(0, side * TRACK_Y, 0))


def build_chassis():
    bm = bmesh.new()
    add_box(bm, (-1.25, -0.72, 0.42), (1.25, 0.72, 0.85), CHAR)          # car body
    add_box(bm, (-0.55, -0.8, 0.38), (0.55, 0.8, 0.78), CHAR)            # cross members to the tracks
    add_cyl(bm, (0, 0, (0.85 + UPPER_Z) / 2), 0.9, UPPER_Z - 0.85, "z", 12, STEEL)  # slew ring
    return finish("Exc_Chassis", bm)


# ---------------------------------------------------------------- upper structure
def build_upper():
    bm = bmesh.new()
    add_box(bm, (-1.3, -1.3, 0.0), (1.3, 1.3, 0.25), YEL)                # deck
    cw = [(-1.25, -1.3), (-1.25, 1.3), (-1.62, 1.3), (-1.84, 1.1), (-1.93, 0.6), (-1.96, 0.0),
          (-1.93, -0.6), (-1.84, -1.1), (-1.62, -1.3)]
    add_prism_xy(bm, cw, 0.0, 1.05, CHAR)                                # counterweight
    add_box(bm, (-1.3, -1.3, 0.25), (-0.15, 1.3, 1.0), YEL)              # engine hood
    for s in (-1, 1):                                                    # louvres
        for i in range(3):
            x = -1.15 + i * 0.32
            add_box(bm, (x, s * 1.3 - 0.015, 0.45), (x + 0.2, s * 1.3 + 0.015, 0.85), CHAR)
    for i in range(4):                                                   # top grille
        x = -1.1 + i * 0.22
        add_box(bm, (x, -0.2, 0.99), (x + 0.12, 0.9, 1.02), CHAR)
    add_cyl(bm, (-0.55, -0.85, 1.25), 0.08, 0.5, "z", 6, STEEL)          # exhaust stack
    # cab, left front
    add_box(bm, (0.0, 0.3, 0.25), (1.3, 1.3, 1.75), YEL)
    add_box(bm, (-0.05, 0.26, 1.75), (1.36, 1.34, 1.82), CHAR)           # roof
    add_box(bm, (1.3, 0.38, 0.72), (1.33, 1.22, 1.68), GLASS)            # front
    add_box(bm, (0.1, 1.3, 0.72), (1.2, 1.33, 1.68), GLASS)              # outer side (door)
    add_box(bm, (0.25, 0.27, 0.8), (1.2, 0.3, 1.68), GLASS)              # boom side
    add_box(bm, (-0.03, 0.4, 0.85), (0.0, 1.2, 1.68), GLASS)             # rear
    add_box(bm, (1.32, 0.36, 1.6), (1.42, 1.24, 1.66), CHAR)             # visor
    # cab steps on the left side
    add_box(bm, (0.35, 1.3, -0.05), (0.95, 1.48, 0.05), STEEL)
    add_box(bm, (0.35, 1.3, 0.12), (0.95, 1.42, 0.22), STEEL)
    # right side tool box + hydraulic tank
    add_box(bm, (-0.15, -1.3, 0.25), (1.3, -0.38, 0.85), YEL)
    add_box(bm, (-0.15, -1.25, 0.85), (0.5, -0.55, 1.15), YEL)
    add_box(bm, (0.6, -1.3 - 0.015, 0.4), (1.1, -1.3 + 0.015, 0.7), CHAR)  # tool box door
    # boom foot lugs and boom-ram lugs
    for s in (-1, 1):
        add_box(bm, (0.8, s * 0.30 - 0.04, 0.25), (1.45, s * 0.30 + 0.04, 0.78), STEEL)
        add_box(bm, (1.25, s * 0.36 - 0.07, 0.0), (1.62, s * 0.36 + 0.07, 0.22), STEEL)
    add_box(bm, (1.25, -0.43, -0.12), (1.62, 0.43, 0.02), YEL)           # front nose plate
    # painted chips
    add_box(bm, (-0.9, 1.3, 0.3), (-0.55, 1.315, 0.42), WORN)
    add_box(bm, (0.15, 1.3, 0.3), (0.55, 1.315, 0.4), WORN)
    add_box(bm, (-0.15, -1.315, 0.3), (0.3, -1.3, 0.42), WORN)
    add_box(bm, (1.3, -0.9, 0.3), (1.315, -0.6, 0.45), WORN)
    add_box(bm, (-1.0, 0.95, 1.0), (-0.7, 1.2, 1.015), WORN)
    return finish("Exc_Upper", bm, loc=(0, 0, UPPER_Z))


# ---------------------------------------------------------------- front linkage
BOOM_TOP = [(-0.32, 0.12), (-0.05, 0.34), (2.45, 0.98), (5.2, 0.27), (5.46, 0.02)]
BOOM_BOT = [(5.2, -0.24), (2.65, 0.32), (0.25, -0.30), (-0.2, -0.26)]
BOOM_RAM_END = (2.10, 0.36, 0.30)        # boom-local, mirrored in y
STICK_RAM_BASE = (2.30, 0.0, 1.12)       # boom-local, on a lug above the knee


def build_boom():
    bm = bmesh.new()
    add_prism_xz(bm, BOOM_TOP + BOOM_BOT, -0.24, 0.24, YEL)
    add_cyl(bm, (0, 0, 0), 0.15, 0.7, "y", 8, STEEL)                     # foot pin
    add_cyl(bm, (BOOM_LEN, 0, 0), 0.15, 0.62, "y", 8, STEEL)             # stick pin boss
    add_cyl(bm, (BOOM_RAM_END[0], 0, BOOM_RAM_END[2]), 0.1, 0.86, "y", 8, STEEL)  # boom-ram pin
    add_box(bm, (2.1, -0.12, 0.85), (2.5, 0.12, 1.2), STEEL)             # stick-ram lug
    add_cyl(bm, (STICK_RAM_BASE[0], 0, STICK_RAM_BASE[2]), 0.08, 0.34, "y", 8, STEEL)
    # painted chips
    add_box(bm, (1.0, 0.24, 0.05), (1.6, 0.255, 0.2), WORN)
    add_box(bm, (3.4, -0.255, 0.1), (3.9, -0.24, 0.22), WORN)
    return finish("Exc_Boom", bm)


STICK_PROFILE = [(-0.8, 0.05), (-0.55, 0.36), (0.3, 0.45), (2.4, 0.24), (2.75, 0.06),
                 (2.75, -0.1), (2.45, -0.18), (0.4, -0.3), (-0.55, -0.25), (-0.82, -0.1)]
STICK_RAM_END = (-0.70, 0.0, -0.10)      # stick-local, on the tail above the boom pin
ATT_RAM_BASE = (-0.05, 0.0, 0.58)
ATT_RAM_END = (2.15, 0.0, 0.38)


def build_stick():
    bm = bmesh.new()
    add_prism_xz(bm, STICK_PROFILE, -0.17, 0.17, YEL)
    add_cyl(bm, (0, 0, 0), 0.13, 0.5, "y", 8, STEEL)                     # boom/stick pin
    add_cyl(bm, (STICK_RAM_END[0], 0, STICK_RAM_END[2]), 0.08, 0.4, "y", 8, STEEL)
    add_cyl(bm, (STICK_LEN, 0, 0), 0.09, 0.44, "y", 8, STEEL)            # wrist (attachment) pin
    add_box(bm, (-0.2, -0.1, 0.4), (0.15, 0.1, 0.62), STEEL)             # attachment-ram lug
    add_cyl(bm, (ATT_RAM_BASE[0], 0, ATT_RAM_BASE[2]), 0.07, 0.3, "y", 8, STEEL)
    # attachment ram (static, both ends on the stick) + link plates down to the wrist
    add_tube(bm, ATT_RAM_BASE, (1.35, 0, 0.45), 0.1, 8, STEEL)
    add_tube(bm, (1.2, 0, 0.452), ATT_RAM_END, 0.055, 8, STEEL)
    for s in (-1, 1):
        add_tube(bm, (ATT_RAM_END[0], s * 0.2, ATT_RAM_END[2]), (2.5, s * 0.2, 0.12), 0.05, 6, STEEL)
    add_cyl(bm, (ATT_RAM_END[0], 0, ATT_RAM_END[2]), 0.06, 0.46, "y", 8, STEEL)
    add_box(bm, (0.7, 0.17, -0.1), (1.2, 0.185, 0.05), WORN)             # chip
    return finish("Exc_Stick", bm)


def build_ram_part(name, start_w, end_w, par, radius, frac, cap_r):
    """One half of a hydraulic ram, authored in `par`'s local frame, origin at its pin.

    start_w / end_w are Blender world points; the tube runs `frac` of the way from start to end.
    """
    inv = par.matrix_world.inverted()
    a, b = inv @ start_w, inv @ end_w
    d = b - a
    bm = bmesh.new()
    add_tube(bm, (0, 0, 0), d * frac, radius, 8, STEEL)
    add_cyl(bm, (0, 0, 0), cap_r, 0.16, "x", 8, STEEL)                   # pin eye (Blender X = lateral)
    ob = finish(name, bm, loc=a, rz=False)
    parent(ob, par)
    return ob


# ---------------------------------------------------------------- attachments
def add_mount(bm):
    """Coupler bracket common to every attachment: side plates straddling the stick, pins."""
    plate = [(-0.32, -0.24), (0.32, -0.24), (0.24, 0.13), (-0.24, 0.13)]
    for s in (-1, 1):
        add_prism_xz(bm, plate, s * 0.20, s * 0.27, YEL)
    add_box(bm, (-0.34, -0.29, -0.3), (0.34, 0.29, -0.2), YEL)            # top plate
    add_cyl(bm, (0, 0, 0), 0.07, 0.62, "y", 8, STEEL)                    # main pin (= origin)
    add_cyl(bm, (-0.22, 0, -0.06), 0.06, 0.62, "y", 8, STEEL)            # link pin


def jaw_mesh(name, outline, open_deg, y0, y1, teeth=(), tooth_mat=STEEL, extra=None):
    """Jaw authored CLOSED in its hinge frame (x, z), then baked open by open_deg of pitch."""
    bm = bmesh.new()
    add_prism_xz(bm, rot_xz(outline, open_deg), y0, y1, STEEL)
    for tri in teeth:
        add_prism_xz(bm, rot_xz(tri, open_deg), y0 + 0.03, y1 - 0.03, tooth_mat)
    add_cyl(bm, (0, 0, 0), 0.1, (y1 - y0) + 0.06, "y", 8, STEEL)          # hinge boss
    if extra:
        extra(bm, open_deg)
    return bm


def open_angle(tip, target_x):
    """Pitch that swings a closed tip (hinge frame) to x = target_x, staying below the hinge."""
    r = math.hypot(*tip)
    closed = math.degrees(math.atan2(tip[1], tip[0]))
    opened = -math.degrees(math.acos(max(-1.0, min(1.0, target_x / r))))
    return opened - closed


REPORT = {}


def build_crusher(root):
    bm = bmesh.new()
    add_mount(bm)
    body = [(-0.36, -0.3), (0.36, -0.3), (0.38, -0.62), (0.28, -0.84), (-0.28, -0.84), (-0.38, -0.62)]
    add_prism_xz(bm, body, -0.3, 0.3, YEL)
    add_box(bm, (-0.2, -0.31, -0.7), (0.2, 0.31, -0.42), CHAR)             # cylinder housing band
    add_box(bm, (0.38, -0.12, -0.6), (0.395, 0.12, -0.45), WORN)
    ob = finish("Att_Crusher_Body", bm)
    parent(ob, root)

    hx, hz = 0.16, -0.78
    outline = [(-0.08, 0.06), (0.0, 0.15), (0.2, 0.06), (0.28, -0.2), (0.23, -0.45), (0.09, -0.64),
               (-0.18, -0.72), (-0.1, -0.6), (-0.04, -0.44), (-0.03, -0.25), (-0.06, -0.05)]
    teeth = [[(-0.04, -0.2), (-0.13, -0.27), (-0.03, -0.32)],
             [(-0.04, -0.37), (-0.14, -0.43), (-0.05, -0.49)],
             [(-0.07, -0.52), (-0.16, -0.58), (-0.1, -0.62)]]
    tip = (-0.18, -0.72)                                                   # 2 cm past centre when closed
    ang = open_angle(tip, 0.60 - hx)                                       # tips 1.2 m apart when open
    for nm, s in (("Att_Crusher_JawA", 1), ("Att_Crusher_JawB", -1)):
        ol = [(s * x, z) for x, z in outline]
        th = [[(s * x, z) for x, z in t] for t in teeth]
        bm = jaw_mesh(nm, ol, s * ang, -0.22, 0.22, th)
        j = finish(nm, bm, loc=(s * hx, 0, hz))
        parent(j, root)
    empty("Anchor_Crusher_Bite", (0, 0, -1.18), root, 0.2)
    REPORT["crusher"] = dict(hinge=(hx, hz), open=ang)


def build_shear(root):
    hz = -0.80
    bm = bmesh.new()
    add_mount(bm)
    body = [(-0.32, -0.3), (0.32, -0.3), (0.32, -0.55), (0.22, -0.82), (-0.22, -0.82), (-0.34, -0.55)]
    add_prism_xz(bm, body, -0.26, 0.26, YEL)
    add_tube(bm, (-0.36, 0, -0.32), (-0.3, 0, -0.75), 0.07, 8, STEEL)      # external cylinder
    add_box(bm, (0.32, -0.1, -0.55), (0.335, 0.1, -0.4), WORN)
    # fixed lower jaw: two hooked plates with a slot between them for the moving blade
    fixed = [(0.18, 0.12), (0.26, -0.3), (0.22, -0.8), (0.1, -1.02), (-0.14, -1.06), (-0.17, -0.97),
             (-0.06, -0.9), (-0.06, -0.5), (-0.1, -0.1), (-0.12, 0.1)]
    fixed = [(x, z + hz) for x, z in fixed]
    for s in (-1, 1):
        add_prism_xz(bm, fixed, s * 0.105, s * 0.21, STEEL)
    add_prism_xz(bm, [(0.16, hz - 0.9), (0.1, hz - 1.02), (-0.14, hz - 1.06), (-0.17, hz - 0.97),
                      (-0.06, hz - 0.9)], -0.21, 0.21, STEEL)              # hook nose bridging the plates
    add_box(bm, (0.12, -0.21, hz - 0.6), (0.24, 0.21, hz - 0.3), STEEL)    # spine web
    add_cyl(bm, (0, 0, hz), 0.13, 0.5, "y", 10, STEEL)                    # pivot boss
    ob = finish("Att_Shear_Body", bm)
    parent(ob, root)

    # long scissor blade: straight cutting edge on +x, pointed tip, deep spine on -x
    blade = [(-0.16, 0.16), (0.05, 0.03), (0.1, -0.3), (0.09, -0.75), (0.02, -0.93), (-0.08, -0.86),
             (-0.24, -0.6), (-0.32, -0.3), (-0.3, -0.02)]
    ang = -40.0                                                            # opens toward -x
    bm = jaw_mesh("Att_Shear_Blade", blade, ang, -0.09, 0.09)
    b = finish("Att_Shear_Blade", bm, loc=(0, 0, hz))
    parent(b, root)
    empty("Anchor_Shear_Cut", (-0.08, 0, hz - 0.38), root, 0.2)
    REPORT["shear"] = dict(hinge=(0.0, hz), open=ang)


BIT_TOP = -1.85
BIT_LEN = 0.85
BIT_STROKE = 0.25


def build_breaker(root):
    bm = bmesh.new()
    add_mount(bm)
    hous = [(-0.26, -0.15), (-0.26, 0.15), (-0.2, 0.22), (0.2, 0.22), (0.26, 0.15), (0.26, -0.15),
            (0.2, -0.22), (-0.2, -0.22)]
    add_prism_xy(bm, hous, -1.95, -0.3, YEL)                               # housing
    add_box(bm, (-0.29, -0.24, -0.62), (0.29, 0.24, -0.42), CHAR)          # clamp bands
    add_box(bm, (-0.29, -0.24, -1.6), (0.29, 0.24, -1.4), CHAR)
    for s in (-1, 1):                                                      # side tie rods
        for x in (-0.12, 0.12):
            add_cyl(bm, (x, s * 0.235, -1.1), 0.035, 1.5, "z", 6, STEEL)
    add_cyl(bm, (0, 0, -2.02), 0.15, 0.14, "z", 10, STEEL)                  # front head / bushing
    add_cyl(bm, (0.27, 0, -0.9), 0.05, 0.25, "x", 6, STEEL)                # hose fitting
    add_box(bm, (0.26, -0.1, -1.0), (0.275, 0.12, -0.8), WORN)
    ob = finish("Att_Breaker_Body", bm)
    parent(ob, root)

    bm = bmesh.new()
    shaft = BIT_LEN - 0.2
    add_cyl(bm, (0, 0, -shaft / 2), 0.075, shaft, "z", 8, STEEL)
    add_tube(bm, (0, 0, -shaft), (0, 0, -BIT_LEN), 0.075, 8, STEEL, radius2=0.0)   # moil point
    add_cyl(bm, (0, 0, -0.04), 0.1, 0.08, "z", 8, STEEL)                   # retainer collar (inside)
    bit = finish("Att_Breaker_Bit", bm, loc=(0, 0, BIT_TOP))
    parent(bit, root)
    empty("Anchor_Breaker_Tip", (0, 0, -BIT_LEN), bit, 0.15)


def build_grapple(root):
    hx, hz = 0.16, -0.56
    bm = bmesh.new()
    add_mount(bm)
    add_cyl(bm, (0, 0, -0.36), 0.27, 0.12, "z", 10, STEEL)                 # rotator
    add_box(bm, (-0.24, -0.52, -0.62), (0.24, 0.52, -0.42), YEL)            # head beam
    add_box(bm, (-0.1, -0.53, -0.58), (0.1, -0.52, -0.46), WORN)
    for s in (-1, 1):
        add_cyl(bm, (0, s * 0.12, -0.5), 0.05, 0.3, "x", 6, STEEL)          # claw cylinders
    ob = finish("Att_Grapple_Body", bm)
    parent(ob, root)

    centre = [(0.0, 0.0), (0.2, -0.12), (0.32, -0.38), (0.28, -0.62), (0.1, -0.8), (-0.2, -0.86)]
    half = [0.09, 0.09, 0.085, 0.075, 0.06, 0.025]
    tip = centre[-1]
    ang = open_angle(tip, 0.80 - hx)                                       # 1.6 m span when open
    for nm, s, ys in (("Att_Grapple_ClawA", 1, (-0.22, 0.22)), ("Att_Grapple_ClawB", -1, (-0.44, 0.0, 0.44))):
        out = strip_outline([(s * x, z) for x, z in centre], half)
        bm = bmesh.new()
        for y in ys:
            add_prism_xz(bm, rot_xz(out, s * ang), y - 0.06, y + 0.06, STEEL)
        add_cyl(bm, (0, 0, 0), 0.07, 2 * max(ys) + 0.2, "y", 8, STEEL)    # hinge tube
        j = finish(nm, bm, loc=(s * hx, 0, hz))
        parent(j, root)
    empty("Anchor_Grapple_Grip", (0, 0, -1.05), root, 0.2)
    REPORT["grapple"] = dict(hinge=(hx, hz), open=ang)


# ---------------------------------------------------------------- main
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    make_materials()

    root = empty("Excavator_Root", (0, 0, 0), None, 1.0, "ARROWS")
    chassis = build_chassis()
    parent(chassis, root)
    for nm, side in (("Exc_TrackL", 1), ("Exc_TrackR", -1)):
        parent(build_track(nm, side), chassis)

    upper = build_upper()
    parent(upper, root)  # sibling of the chassis, per the Unity node contract

    boom = build_boom()
    boom.location = R(BOOM_FOOT)
    boom.rotation_euler = Euler((math.radians(BOOM_PITCH), 0, 0))
    parent(boom, upper)

    stick = build_stick()
    stick.location = R((BOOM_LEN, 0, 0))
    stick.rotation_euler = Euler((math.radians(STICK_PITCH_REL), 0, 0))
    parent(stick, boom)

    wrist = empty("Exc_Wrist", (STICK_LEN, 0, 0), stick, 0.4, "ARROWS")
    wrist.rotation_euler = Euler((-math.radians(BOOM_PITCH + STICK_PITCH_REL), 0, 0))

    empty("Anchor_Seat", (0.6, 0.8, 1.4), upper, 0.2)
    empty("Anchor_Door", (0.6, 1.9, -UPPER_Z), upper, 0.3)
    update()

    def W(ob, p):
        return ob.matrix_world @ R(p)

    # rams: barrel on the base part, rod on the moving part, each pointing at the other's pin
    for s, sfx in ((1, "L"), (-1, "R")):
        base = W(upper, (1.45, s * 0.36, 0.11))
        end = W(boom, (BOOM_RAM_END[0], s * BOOM_RAM_END[1], BOOM_RAM_END[2]))
        build_ram_part("Exc_BoomRam_" + sfx, base, end, upper, 0.11, 0.62, 0.12)
        build_ram_part("Exc_BoomRamRod_" + sfx, end, base, boom, 0.06, 0.55, 0.09)
    base, end = W(boom, STICK_RAM_BASE), W(stick, STICK_RAM_END)
    build_ram_part("Exc_StickRam", base, end, boom, 0.11, 0.6, 0.12)
    build_ram_part("Exc_StickRamRod", end, base, stick, 0.06, 0.55, 0.09)

    # attachments: separate roots, world rotation identity, side by side clear of the machine
    for nm, fn in (("Crusher", build_crusher), ("Shear", build_shear), ("Breaker", build_breaker),
                   ("Grapple", build_grapple)):
        r = empty("Att_" + nm, (ATT_ROW_X, ATT_ROW_Y[nm], ATT_ROW_Z), None, 0.4, "ARROWS")
        fn(r)

    for ob in bpy.data.objects:
        ob.select_set(False)
    update()

    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(FBX_DIR, exist_ok=True)
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

    def dz(v):  # Blender world -> design
        return (round(v.y, 3), round(-v.x, 3), round(v.z, 3))

    tris = sum(len(p.vertices) - 2 for ob in bpy.data.objects if ob.type == "MESH" for p in ob.data.polygons)
    print("TRIS", tris)
    for ob in sorted(bpy.data.objects, key=lambda o: o.name):
        t = sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == "MESH" else 0
        print("OBJ %-24s world(design)=%s tris=%d" % (ob.name, dz(ob.matrix_world.translation), t))
    print("REPORT", REPORT)


main()
