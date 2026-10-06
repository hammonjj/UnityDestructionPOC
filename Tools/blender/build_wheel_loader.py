"""Author the low-poly articulated wheel loader (~18 t class, Cat 950 style) as a PROP (not a blockout).

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/build_wheel_loader.py

Writes Tools/blender/WheelLoader/WheelLoader.blend and
DestructionPOC/Assets/Destruction/Models/WheelLoader/WheelLoader.fbx.

Same conventions as build_excavator.py / build_wrecking_crane.py: geometry is authored in "design space"
(x = forward, y = left, z = up, metres) and mapped to Blender space by a +90 deg Z rotation (the machine
faces Blender +Y). Lift arm and bucket rotate about the design lateral axis = Blender local X
(positive rotation_euler.x turns design +x toward +z). Steering rotates WL_FrontFrame about Blender local Z.
The script also poses the rig headless and prints verified numbers (articulation, clearances, normals).
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "WheelLoader")
BLEND_PATH = os.path.join(OUT_DIR, "WheelLoader.blend")
FBX_DIR = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "WheelLoader")
FBX_PATH = os.path.join(FBX_DIR, "WheelLoader.fbx")

RZ = Matrix.Rotation(math.radians(90), 4, "Z")


def R(v):
    """Design-space point/offset -> Blender space."""
    return Vector((-v[1], v[0], v[2]))


def D(v):
    """Blender -> design."""
    return Vector((v.y, -v.x, v.z))


# ---------------------------------------------------------------- layout (design space, metres, degrees)
AXLE_X = 1.7                       # axles at +-1.7 from the articulation pivot
TIRE_R, TIRE_W = 0.85, 0.65
TIRE_Y = 1.5 - TIRE_W / 2          # 1.175: 3.0 m over tires
PIVOT = (1.3, 2.25)                # lift pivot pin (x, z) on the front frame
PIN_WORLD = (3.72, 1.45)           # bucket pivot pin (x, z) at rest
PIN = (PIN_WORLD[0] - PIVOT[0], PIN_WORLD[1] - PIVOT[1])   # arm-local (2.42, -0.95)
ARM_Y0, ARM_Y1 = 0.70, 0.86        # arm plate y range (mirrored)
LIFT_RAM_FRAME = (0.62, 0.97, 1.55)    # frame pin, FrontFrame-local (y mirrored)
LIFT_RAM_ARM = (1.1, 0.97, 0.10)       # arm pin, arm-local (y mirrored)
TILT_ARM = (1.3, 0.0, 0.50)            # arm-local
TILT_BUCKET = (-0.40, 0.0, 0.40)       # bucket-local
EDGE_Z = -PIN_WORLD[1]             # bucket-local z of the ground (-1.30)
FLOOR_TOP = EDGE_Z + 0.06
BACK_TOP = FLOOR_TOP + 1.10
LIFT_MAX = 64.0                    # deg, arm rotation limit used for reporting

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


# ---------------------------------------------------------------- geometry helpers (as the excavator)
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
    rot = {"z": Matrix.Identity(4),
           "x": Matrix.Rotation(math.radians(90), 4, "Y"),
           "y": Matrix.Rotation(math.radians(90), 4, "X")}[axis]
    m = Matrix.Translation(center) @ rot
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius,
                              depth=length, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def add_tube(bm, a, b, radius, segs, mat, radius2=None):
    a, b = Vector(a), Vector(b)
    d = b - a
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    m = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius,
                              radius2=radius if radius2 is None else radius2, depth=d.length, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def add_prism_xz(bm, pts, y0, y1, mat):
    a = [bm.verts.new((x, y0, z)) for x, z in pts]
    b = [bm.verts.new((x, y1, z)) for x, z in pts]
    faces = [bm.faces.new(a[::-1]), bm.faces.new(b)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    _mat(faces, mat)


def add_prism_xy(bm, pts, z0, z1, mat):
    a = [bm.verts.new((x, y, z0)) for x, y in pts]
    b = [bm.verts.new((x, y, z1)) for x, y in pts]
    faces = [bm.faces.new(a[::-1]), bm.faces.new(b)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    _mat(faces, mat)


def strip_outline(centre, half):
    left, right = [], []
    n = len(centre)
    for i in range(n):
        p = Vector(centre[i])
        t = (Vector(centre[min(i + 1, n - 1)]) - Vector(centre[max(i - 1, 0)])).normalized()
        nrm = Vector((-t.y, t.x))
        left.append(tuple(p + nrm * half[i]))
        right.append(tuple(p - nrm * half[i]))
    return left + right[::-1]


def finish(name, bm, loc=(0, 0, 0), rz=True, nomat=False):
    if rz:
        bmesh.ops.transform(bm, matrix=RZ, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    used = [] if nomat else sorted({f.material_index for f in bm.faces})
    remap = {u: i for i, u in enumerate(used)}
    for f in bm.faces:
        f.material_index = remap.get(f.material_index, 0)
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


def col_box(name, par, lo, hi, rot_pitch=0.0, wire=True):
    """Collider/volume helper: a cube mesh centred at its own origin, size baked, in the parent's space."""
    lo, hi = Vector(lo), Vector(hi)
    c, s = (lo + hi) / 2, hi - lo
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((s.x, s.y, s.z, 1)))
    ob = finish(name, bm, loc=c, nomat=True)
    ob.rotation_euler = Euler((math.radians(rot_pitch), 0, 0))
    ob.display_type = "WIRE"
    ob.hide_render = True
    parent(ob, par)
    return ob


# ---------------------------------------------------------------- wheels
def build_wheel(name, parent_ob, loc):
    """Origin = axle centre; tire 1.7 m dia x 0.65 m wide, spins about local X (lateral)."""
    bm = bmesh.new()
    add_cyl(bm, (0, 0, 0), 0.81, TIRE_W, "y", 16, CHAR)
    n = 16
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        c = Vector((0.815 * math.cos(a), 0, 0.815 * math.sin(a)))
        m = Matrix.Translation(c) @ Matrix.Rotation(-a, 4, "Y") @ Matrix.Diagonal((0.07, TIRE_W - 0.05, 0.24, 1))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        _mat({f for v in r["verts"] for f in v.link_faces}, CHAR)
    add_cyl(bm, (0, 0, 0), 0.46, TIRE_W + 0.04, "y", 8, STEEL)       # rim
    add_cyl(bm, (0, 0, 0), 0.20, TIRE_W + 0.10, "y", 8, STEEL)       # hub cap
    ob = finish(name, bm, loc=loc)
    parent(ob, parent_ob)
    return ob


# ---------------------------------------------------------------- rear frame
def build_rear():
    bm = bmesh.new()
    add_box(bm, (-2.58, -0.8, 0.5), (-0.95, 0.8, 1.85), CHAR)                 # lower body between the tires
    hood = [(-2.6, 1.85), (-1.0, 1.85), (-1.0, 2.1), (-1.4, 2.5), (-2.6, 2.5)]
    add_prism_xz(bm, hood, -1.15, 1.15, YEL)                                  # engine hood
    cw = [(-2.58, -1.15), (-2.58, 1.15), (-2.75, 1.05), (-2.9, 0.55), (-2.92, 0.0),
          (-2.9, -0.55), (-2.75, -1.05)]
    add_prism_xy(bm, cw, 0.55, 2.4, CHAR)                                     # counterweight
    for s in (-1, 1):                                                         # side louvres
        for i in range(3):
            x = -2.3 + i * 0.4
            add_box(bm, (x, s * 1.15 - 0.015, 2.05), (x + 0.25, s * 1.15 + 0.015, 2.4), CHAR)
    for i in range(5):                                                        # rear grille
        z = 1.0 + i * 0.2
        add_box(bm, (-2.945, -0.45, z), (-2.915, 0.45, z + 0.1), CHAR)
    for s in (-1, 1):
        add_box(bm, (-2.83, s * 0.9 - 0.1, 1.9), (-2.76, s * 0.9 + 0.1, 2.05), GLASS)   # tail lights
    add_cyl(bm, (-1.4, 0.8, 2.85), 0.07, 0.7, "z", 6, STEEL)                  # exhaust stack
    add_cyl(bm, (-1.4, 0.8, 3.22), 0.1, 0.05, "z", 6, STEEL)
    add_cyl(bm, (-1.75, -0.8, 2.75), 0.13, 0.5, "z", 8, CHAR)                 # air cleaner
    for s in (-1, 1):                                                         # mudguards over the tires
        add_box(bm, (-2.5, s * 0.85 if s > 0 else -1.5, 1.76), (-0.95, 1.5 if s > 0 else -0.85, 1.82), YEL)
        add_box(bm, (-2.5, s * 0.85 if s > 0 else -1.5, 1.44), (-2.44, 1.5 if s > 0 else -0.85, 1.82), YEL)  # rear skirt
    add_cyl(bm, (-AXLE_X, 0, TIRE_R), 0.15, 2.0, "y", 8, STEEL)               # rear axle
    add_box(bm, (-0.95, -0.4, 0.45), (-0.1, 0.4, 0.7), CHAR)                  # hitch plate
    add_cyl(bm, (0, 0, 0.875), 0.18, 0.85, "z", 8, STEEL)                     # articulation pin
    add_box(bm, (-2.4, 0.8, 1.2), (-1.5, 0.84, 1.55), WORN)                   # chips
    add_box(bm, (-1.3, -0.84, 0.8), (-0.97, -0.8, 1.1), WORN)
    return finish("WL_RearFrame", bm)


# ---------------------------------------------------------------- front frame
def build_front():
    bm = bmesh.new()
    add_box(bm, (-0.55, -0.5, 0.72), (2.05, 0.5, 1.28), CHAR)                 # frame body
    add_box(bm, (0.9, -0.5, 1.28), (2.05, 0.5, 1.42), YEL)                    # deck
    add_box(bm, (1.1, -0.7, 1.28), (1.6, 0.7, 1.42), YEL)                     # tower bridge
    tower = [(1.1, 1.28), (1.6, 1.28), (1.55, 2.1), (1.3, 2.45), (1.05, 2.1)]
    for s in (-1, 1):                                                         # lift towers
        add_prism_xz(bm, tower, *sorted((s * 0.56, s * 0.69)), YEL)
    add_cyl(bm, (PIVOT[0], 0, PIVOT[1]), 0.09, 1.8, "y", 8, STEEL)            # lift pivot pin
    # cab
    add_box(bm, (-0.4, -0.78, 1.28), (0.9, 0.78, 3.2), YEL)
    add_box(bm, (-0.5, -0.88, 3.2), (1.0, 0.88, 3.3), CHAR)                   # roof
    add_box(bm, (0.9, -0.62, 1.95), (0.93, 0.62, 3.1), GLASS)                 # windscreen
    add_box(bm, (-0.43, -0.62, 1.95), (-0.4, 0.62, 3.1), GLASS)               # rear
    for s in (-1, 1):
        lo, hi = sorted((s * 0.78, s * 0.81))
        add_box(bm, (-0.25, lo, 1.95), (0.75, hi, 3.1), GLASS)                # side windows
    add_box(bm, (0.9, -0.66, 3.1), (1.05, 0.66, 3.2), CHAR)                   # visor
    for s in (-1, 1):
        add_box(bm, (0.95, s * 0.4 - 0.1, 3.3), (1.0, s * 0.4 + 0.1, 3.38), GLASS)   # roof work lights
    add_cyl(bm, (-0.25, 0.5, 3.4), 0.05, 0.2, "z", 6, STEEL)                  # beacon
    add_box(bm, (-0.3, -0.5, 3.3), (0.4, 0.2, 3.4), CHAR)                     # A/C unit
    for s in (-1, 1):
        add_box(bm, (2.05, s * 0.3 - 0.1, 0.95), (2.09, s * 0.3 + 0.1, 1.15), GLASS)    # headlights
    # steps + stiles on the left (design +y)
    for z in (0.62, 1.0, 1.38):
        add_box(bm, (-0.22, 0.78, z), (0.3, 1.04, z + 0.05), STEEL)
    for x in (-0.22, 0.25):
        add_box(bm, (x, 1.0, 0.55), (x + 0.05, 1.04, 1.43), STEEL)
    add_box(bm, (0.3, 0.8, 1.45), (0.35, 0.85, 2.4), STEEL)                   # grab rail
    add_box(bm, (0.35, 0.78, 1.28), (0.8, 0.8, 1.9), WORN)                    # door scuff
    # lift ram brackets + pins
    for s in (-1, 1):
        lo, hi = sorted((s * 0.5, s * 1.02))
        add_box(bm, (0.45, lo, 1.35), (0.78, hi, 1.7), YEL)
        add_cyl(bm, (LIFT_RAM_FRAME[0], s * LIFT_RAM_FRAME[1], LIFT_RAM_FRAME[2]), 0.06, 0.3, "y", 6, STEEL)
    add_cyl(bm, (AXLE_X, 0, TIRE_R), 0.15, 2.0, "y", 8, STEEL)                # front axle
    for s in (-1, 1):                                                         # mudguards (clear of the arms)
        lo, hi = sorted((s * 1.12, s * 1.5))
        add_box(bm, (0.9, lo, 1.76), (2.5, hi, 1.82), YEL)
        add_box(bm, (2.44, lo, 1.44), (2.5, hi, 1.82), YEL)                   # front skirt
    add_box(bm, (2.05, -0.4, 0.55), (2.35, 0.4, 0.85), CHAR)                  # nose guard / bumper
    return finish("WL_FrontFrame", bm)


# ---------------------------------------------------------------- lift arm
ARM_CENTRE = [(0.0, 0.0), (1.0, 0.2), (1.75, 0.12), PIN]
ARM_HALF = [0.24, 0.26, 0.22, 0.14]
CROSS = (1.3, 0.18)


def build_arm():
    bm = bmesh.new()
    outline = strip_outline(ARM_CENTRE, ARM_HALF)
    for s in (-1, 1):
        add_prism_xz(bm, outline, *sorted((s * ARM_Y0, s * ARM_Y1)), YEL)
        add_cyl(bm, (0, s * 0.78, 0), 0.16, 0.16, "y", 8, STEEL)               # pivot boss
        add_cyl(bm, (PIN[0], s * 0.78, PIN[1]), 0.14, 0.16, "y", 8, STEEL)     # bucket-pin boss
        lo, hi = sorted((s * ARM_Y1, s * 1.02))
        add_box(bm, (LIFT_RAM_ARM[0] - 0.12, lo, LIFT_RAM_ARM[2] - 0.12),
                (LIFT_RAM_ARM[0] + 0.12, hi, LIFT_RAM_ARM[2] + 0.12), YEL)     # lift-ram lug
        add_cyl(bm, (LIFT_RAM_ARM[0], s * LIFT_RAM_ARM[1], LIFT_RAM_ARM[2]), 0.06, 0.3, "y", 6, STEEL)
        lo, hi = sorted((s * ARM_Y1 - 0.005 * s, s * ARM_Y1 + 0.015 * s))
        add_box(bm, (0.9, lo, 0.18), (1.6, hi, 0.3), WORN)
    add_cyl(bm, (CROSS[0], 0, CROSS[1]), 0.13, 2 * ARM_Y1, "y", 8, STEEL)      # cross tube
    add_box(bm, (TILT_ARM[0] - 0.1, -0.1, CROSS[1]), (TILT_ARM[0] + 0.1, 0.1, TILT_ARM[2]), YEL)   # tilt-ram lug
    add_cyl(bm, (TILT_ARM[0], 0, TILT_ARM[2]), 0.06, 0.3, "y", 6, STEEL)
    return finish("WL_LiftArm", bm)


# ---------------------------------------------------------------- bucket (origin = pivot pin)
BACK_C = [(-0.05, EDGE_Z + 0.03), (-0.38, -0.95), (-0.46, -0.45), (-0.40, BACK_TOP)]
BACK_HALF = 0.035
SIDE_TOP_BACK = FLOOR_TOP + 0.80
SIDE_TOP_FRONT = FLOOR_TOP + 0.20
EDGE_X = 1.78
BUCKET_Y = 1.6
WALL = 0.06
cav_lo = (-0.40, -(BUCKET_Y - WALL), FLOOR_TOP)
cav_hi = (EDGE_X - 0.23, BUCKET_Y - WALL, SIDE_TOP_BACK)   # to the edge-plate lip (x 1.55)


def build_bucket():
    bm = bmesh.new()
    yi = BUCKET_Y - WALL
    add_box(bm, (-0.05, -yi - 0.03, EDGE_Z), (1.45, yi + 0.03, FLOOR_TOP), WORN)          # floor (0.06 thick)
    back = strip_outline(BACK_C, [BACK_HALF] * 4)
    add_prism_xz(bm, back, -BUCKET_Y, BUCKET_Y, WORN)                                    # curved back (3 segments)
    for yc in (-1.0, 0.0, 1.0):                                                          # rear ribs
        rib = strip_outline([(x - 0.09, z) for x, z in BACK_C[1:]], [0.03] * 3)
        add_prism_xz(bm, rib, yc - 0.05, yc + 0.05, YEL)
    side = [(-0.50, EDGE_Z), (1.50, EDGE_Z), (1.50, SIDE_TOP_FRONT), (-0.42, SIDE_TOP_BACK), (-0.50, SIDE_TOP_BACK - 0.2)]
    for s in (-1, 1):
        add_prism_xz(bm, side, *sorted((s * yi, s * BUCKET_Y)), YEL)                    # side walls
    edge = [(1.30, EDGE_Z), (1.62, EDGE_Z), (1.52, EDGE_Z + 0.10), (1.30, EDGE_Z + 0.10)]
    add_prism_xz(bm, edge, -BUCKET_Y, BUCKET_Y, STEEL)                                   # cutting edge plate
    for i in range(7):                                                                   # teeth
        yc = -1.35 + i * 0.45
        add_prism_xz(bm, [(1.50, EDGE_Z), (EDGE_X, EDGE_Z), (1.50, EDGE_Z + 0.16)], yc - 0.09, yc + 0.09, STEEL)
    ear = [(-0.55, -0.9), (-0.52, -0.2), (-0.25, 0.18), (0.12, 0.2), (0.22, 0.0), (0.12, -0.2), (-0.1, -0.25), (-0.1, -0.9)]
    for s in (-1, 1):
        add_prism_xz(bm, ear, *sorted((s * 0.88, s * 1.0)), YEL)                         # pin ears outside the arms
    add_cyl(bm, (0, 0, 0), 0.09, 2.1, "y", 8, STEEL)                                     # bucket pivot pin
    lug = [(-0.55, -0.2), (-0.55, 0.35), (-0.4, 0.52), (-0.25, 0.35), (-0.25, -0.2)]
    add_prism_xz(bm, lug, -0.15, 0.15, YEL)                                              # tilt-ram lug
    add_cyl(bm, (TILT_BUCKET[0], 0, TILT_BUCKET[2]), 0.06, 0.4, "y", 6, STEEL)
    for s in (-1, 1):                                                                    # side wear strips (wear mat)
        lo, hi = sorted((s * (BUCKET_Y - 0.04), s * (BUCKET_Y - 0.002)))
        add_box(bm, (0.2, lo, EDGE_Z + 0.02), (1.3, hi, EDGE_Z + 0.12), WORN)
    return finish("WL_Bucket", bm)


def build_ram_part(name, start_w, end_w, par, radius, frac, cap_r):
    inv = par.matrix_world.inverted()
    a, b = inv @ start_w, inv @ end_w
    d = b - a
    bm = bmesh.new()
    add_tube(bm, (0, 0, 0), d * frac, radius, 8, STEEL)
    add_cyl(bm, (0, 0, 0), cap_r, 0.16, "x", 8, STEEL)
    ob = finish(name, bm, loc=a, rz=False)
    parent(ob, par)
    return ob


# ---------------------------------------------------------------- kinematics helpers for ram sizing
def rot2(p, deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return (p[0] * c - p[1] * s, p[0] * s + p[1] * c)


def add2(a, b):
    return (a[0] + b[0], a[1] + b[1])


def lift_ram_range():
    F = (LIFT_RAM_FRAME[0], LIFT_RAM_FRAME[2])
    Ls = [math.dist(F, add2(PIVOT, rot2((LIFT_RAM_ARM[0], LIFT_RAM_ARM[2]), l))) for l in range(0, int(LIFT_MAX) + 1, 2)]
    return min(Ls), max(Ls)


def tilt_ram_range():
    Ls = []
    for l in range(0, int(LIFT_MAX) + 1, 4):
        T = add2(PIVOT, rot2((TILT_ARM[0], TILT_ARM[2]), l))
        pw = add2(PIVOT, rot2(PIN, l))
        for t in range(-60, 51, 5):
            B = add2(pw, rot2((TILT_BUCKET[0], TILT_BUCKET[2]), l + t))
            Ls.append(math.dist(T, B))
    return min(Ls), max(Ls)


# ---------------------------------------------------------------- verification helpers
def tri_bvh(obs, face_filter=None):
    verts, tris = [], []
    for ob in obs:
        me = ob.data
        mw = ob.matrix_world
        base = len(verts)
        verts.extend([mw @ v.co for v in me.vertices])
        for p in me.polygons:
            if face_filter and not face_filter(ob, p):
                continue
            vs = list(p.vertices)
            for i in range(1, len(vs) - 1):
                tris.append((base + vs[0], base + vs[i], base + vs[i + 1]))
    return BVHTree.FromPolygons(verts, tris, epsilon=0.0)


def signed_volume(ob):
    me = ob.data
    v = 0.0
    for p in me.polygons:
        vs = [me.vertices[i].co for i in p.vertices]
        for i in range(1, len(vs) - 1):
            v += vs[0].dot(vs[i].cross(vs[i + 1])) / 6.0
    return v


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    make_materials()
    O = bpy.data.objects

    root = empty("WheelLoader_Root", (0, 0, 0), None, 1.0, "ARROWS")
    rear = build_rear()
    rear.name = "WL_RearFrame"
    parent(rear, root)
    front = build_front()
    parent(front, root)

    for nm, par, x, y in (("WL_Wheel_RL", rear, -AXLE_X, TIRE_Y), ("WL_Wheel_RR", rear, -AXLE_X, -TIRE_Y),
                          ("WL_Wheel_FL", front, AXLE_X, TIRE_Y), ("WL_Wheel_FR", front, AXLE_X, -TIRE_Y)):
        build_wheel(nm, par, (x, y, TIRE_R))

    arm = build_arm()
    arm.location = R((PIVOT[0], 0, PIVOT[1]))
    parent(arm, front)
    bucket = build_bucket()
    bucket.location = R((PIN[0], 0, PIN[1]))
    parent(bucket, arm)
    update()

    def W(ob, p):
        return ob.matrix_world @ R(p)

    # rams: barrel on the base part, rod on the moving part, each pointing at the other's pin
    lmin, lmax = lift_ram_range()
    tmin, tmax = tilt_ram_range()
    for s, sfx in ((1, "L"), (-1, "R")):
        base = W(front, (LIFT_RAM_FRAME[0], s * LIFT_RAM_FRAME[1], LIFT_RAM_FRAME[2]))
        end = W(arm, (LIFT_RAM_ARM[0], s * LIFT_RAM_ARM[1], LIFT_RAM_ARM[2]))
        Lr = (end - base).length
        build_ram_part("WL_LiftRam_" + sfx, base, end, front, 0.09, 0.62 * lmax / Lr, 0.10)
        build_ram_part("WL_LiftRamRod_" + sfx, end, base, arm, 0.055, 0.62 * lmax / Lr, 0.08)
    base, end = W(arm, TILT_ARM), W(bucket, TILT_BUCKET)
    Lr = (end - base).length
    build_ram_part("WL_TiltRam", base, end, arm, 0.08, 0.62 * tmax / Lr, 0.09)
    build_ram_part("WL_TiltRamRod", end, base, bucket, 0.05, 0.62 * tmax / Lr, 0.08)

    # anchors
    empty("Anchor_Seat", (0.15, 0.0, 2.65), front, 0.2)
    empty("Anchor_Door", (0.05, 1.40, 0.0), front, 0.3)
    empty("Anchor_Exit_L", (0.0, 2.3, 0.0), front, 0.3)
    empty("Anchor_Exit_R", (0.0, -2.3, 0.0), front, 0.3)
    empty("Anchor_CameraFocus", (AXLE_X + 3.0, 0.0, 0.0), front, 0.4)
    empty("Anchor_BucketEdge", (EDGE_X, 0.0, EDGE_Z), bucket, 0.2)
    empty("Anchor_BucketCavity", ((cav_lo[0] + cav_hi[0]) / 2, 0.0, FLOOR_TOP + 0.25), bucket, 0.2)

    # colliders (axis-aligned cubes in the parent's space; hidden by Unity)
    col_box("Col_Rear", rear, (-2.92, -0.8, 0.5), (-0.95, 0.8, 1.85))
    col_box("Col_RearHood", rear, (-2.92, -1.15, 1.85), (-1.0, 1.15, 2.5))
    col_box("Col_Front", front, (-0.55, -0.5, 0.72), (2.09, 0.5, 1.42))
    col_box("Col_Cab", front, (-0.4, -0.88, 1.42), (1.05, 0.88, 3.4))
    for nm, par, x, y in (("Col_Wheel_RL", rear, -AXLE_X, TIRE_Y), ("Col_Wheel_RR", rear, -AXLE_X, -TIRE_Y),
                          ("Col_Wheel_FL", front, AXLE_X, TIRE_Y), ("Col_Wheel_FR", front, AXLE_X, -TIRE_Y)):
        col_box(nm, par, (x - TIRE_R, y - TIRE_W / 2, 0.0), (x + TIRE_R, y + TIRE_W / 2, 2 * TIRE_R))
    ang = math.degrees(math.atan2(PIN[1], PIN[0]))
    chord = math.hypot(*PIN)
    mid = (PIN[0] / 2 + 0.0, 0.0, PIN[1] / 2 + 0.12)
    for nm, s in (("Col_Arm_L", 1), ("Col_Arm_R", -1)):
        c = col_box(nm, arm, (0, 0, 0), (chord, 0.16, 0.5), rot_pitch=ang)
        c.location = R((PIN[0] / 2, s * 0.78, PIN[1] / 2 + 0.1))
    t = WALL + 0.04
    col_box("Col_Bucket_Floor", bucket, (-0.45, -BUCKET_Y, EDGE_Z), (1.50, BUCKET_Y, FLOOR_TOP))
    col_box("Col_Bucket_Back", bucket, (-0.50, -BUCKET_Y, EDGE_Z), (-0.40, BUCKET_Y, BACK_TOP))
    col_box("Col_Bucket_SideL", bucket, (-0.50, BUCKET_Y - WALL, EDGE_Z), (1.50, BUCKET_Y, SIDE_TOP_BACK))
    col_box("Col_Bucket_SideR", bucket, (-0.50, -BUCKET_Y, EDGE_Z), (1.50, -(BUCKET_Y - WALL), SIDE_TOP_BACK))
    col_box("Col_Bucket_Edge", bucket, (1.30, -BUCKET_Y, EDGE_Z), (1.62, BUCKET_Y, EDGE_Z + 0.10))
    col_box("Vol_Cavity", bucket, cav_lo, cav_hi)

    for ob in bpy.data.objects:
        ob.select_set(False)
    update()

    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(FBX_DIR, exist_ok=True)
    for ob in bpy.data.objects:
        assert all(abs(s - 1) < 1e-9 for s in ob.scale), ob.name

    verify(O, root, rear, front, arm, bucket)

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
    print("WROTE", BLEND_PATH, FBX_PATH)


def pose(O, steer=0.0, lift=0.0, rotx=0.0):
    O["WL_FrontFrame"].rotation_euler = Euler((0, 0, math.radians(steer)))
    O["WL_LiftArm"].rotation_euler = Euler((math.radians(lift), 0, 0))
    O["WL_Bucket"].rotation_euler = Euler((math.radians(rotx), 0, 0))
    update()


def verify(O, root, rear, front, arm, bucket):
    P = lambda ob: tuple(round(c, 3) for c in D(ob.matrix_world.translation))
    pose(O)
    print("=== TRIS")
    tot = 0
    for ob in sorted(O, key=lambda o: o.name):
        if ob.type == "MESH":
            t = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            if not ob.name.startswith(("Col_", "Vol_")):
                tot += t
            print("  %-22s tris=%5d  origin(design)=%s" % (ob.name, t, P(ob)))
        else:
            print("  %-22s empty        origin(design)=%s" % (ob.name, P(ob)))
    print("TRIS_VISIBLE", tot)

    # bounding box of the visible machine
    vis = [o for o in O if o.type == "MESH" and not o.name.startswith(("Col_", "Vol_"))]
    pts = [D(o.matrix_world @ v.co) for o in vis for v in o.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    print("BBOX_DESIGN lo=%s hi=%s size=%s" % ([round(x, 3) for x in lo], [round(x, 3) for x in hi],
                                              [round(h - l, 3) for l, h in zip(lo, hi)]))
    wz = [(o.name, round(min((o.matrix_world @ v.co).z for v in o.data.vertices), 4)) for o in vis if o.name.startswith("WL_Wheel")]
    print("TIRE_MIN_Z", wz)
    ww = [round((max(D(o.matrix_world @ v.co).y for v in o.data.vertices)), 3) for o in vis if o.name == "WL_Wheel_FL"]
    # cutting edge
    print("EDGE_ANCHOR", P(O["Anchor_BucketEdge"]), "PIN", P(bucket))
    cr = [D(bucket.matrix_world @ v.co).z for v in bucket.data.vertices]
    print("BUCKET_MINZ_REST", round(min(cr), 4))

    # lift range
    lmax_ok = None
    for lift in (0, 20, 40, 55, 60, LIFT_MAX):
        pose(O, lift=lift)
        print("LIFT %5.1f  pin=%s  edge=%s" % (lift, P(bucket), P(O["Anchor_BucketEdge"])))
    # exact lift for pin height 4.0
    sol = math.degrees(math.asin((4.0 - PIVOT[1]) / math.hypot(*PIN))) - math.degrees(math.atan2(PIN[1], PIN[0]))
    print("LIFT_FOR_PIN_Z_4.0 = %.2f deg" % sol)
    pose(O, lift=sol)
    print("  check pin z =", round(P(bucket)[2], 3))
    pose(O)

    # steering
    for st in (-40, 0, 40):
        pose(O, steer=st)
        print("STEER %+d  FL=%s FR=%s  edge=%s" % (st, P(O["WL_Wheel_FL"]), P(O["WL_Wheel_FR"]), P(O["Anchor_BucketEdge"])))
    pose(O)

    # ---- clip tests
    def bucket_plates(ob, p):
        # drop the pin cylinder faces (radius < 0.12 about the pin axis, bucket-local x/z)
        pts = [ob.data.vertices[i].co for i in p.vertices]
        return not all(math.hypot(q.y, q.z) < 0.125 for q in pts)

    # pin faces: Blender local (x=-y_design, y=x_design, z)  -> pin axis along Blender X; radial = (y, z)
    arm_wheels = None
    frame_obs = [front, O["WL_Wheel_FL"], O["WL_Wheel_FR"]]

    print("=== BUCKET CLIP SWEEP, 1 deg steps (rotx = Blender rotation_euler.x deg; brief pitch = -rotx; dump = brief +)")
    rows = {}
    lifts = list(range(0, int(LIFT_MAX) + 1, 8)) + [int(LIFT_MAX)]
    for lift in lifts:
        arm_t = tri_bvh([arm])
        res = []
        for rx in range(-90, 111):
            pose(O, 0, lift, rx)
            tb = tri_bvh([bucket], bucket_plates)
            hit_arm = len(tb.overlap(tri_bvh([arm]))) > 0
            hit_fr = len(tb.overlap(tri_bvh([front, O["WL_Wheel_FL"], O["WL_Wheel_FR"]]))) > 0
            hit_g = min((bucket.matrix_world @ v.co).z for v in bucket.data.vertices) < -0.005
            res.append((rx, hit_arm, hit_fr, hit_g))
        rows[lift] = res

    def contiguous(res, test):
        """Largest contiguous rotx interval around rotx=0 (or nearest) where test(row) is True."""
        ok = {r[0] for r in res if test(r)}
        if 0 not in ok:
            return None
        lo = hi = 0
        while lo - 1 in ok:
            lo -= 1
        while hi + 1 in ok:
            hi += 1
        return lo, hi

    arm_lims, fr_lims = [], []
    for lift in lifts:
        a_ = contiguous(rows[lift], lambda r: not r[1])
        f_ = contiguous(rows[lift], lambda r: not r[1] and not r[2])
        g_ = [r[0] for r in rows[lift] if not r[1] and not r[2] and not r[3]]
        print("  lift %2d: arm-clip-free rotx %s (brief %s) | arm+frame-free %s | also above-ground: %s..%s" % (
            lift, a_, (-a_[1], -a_[0]) if a_ else None, f_, min(g_) if g_ else None, max(g_) if g_ else None))
        arm_lims.append(a_)
        fr_lims.append(f_)
    print("  ARM-CLIP-FREE across ALL lifts: rotx %d..%d  => brief pitch %d (curl) .. +%d (dump)" % (
        max(a[0] for a in arm_lims), min(a[1] for a in arm_lims), -min(a[1] for a in arm_lims), -max(a[0] for a in arm_lims)))
    print("  ARM+FRAME-CLIP-FREE across ALL lifts: rotx %d..%d" % (max(f[0] for f in fr_lims), min(f[1] for f in fr_lims)))
    pose(O)

    # arm vs frame
    pose(O)
    bad = []

    for lift in range(0, int(LIFT_MAX) + 1, 4):
        pose(O, 0, lift, 0)
        a = tri_bvh([arm], lambda ob, p: all(math.hypot((ob.data.vertices[i].co.y), (ob.data.vertices[i].co.z)) > 0 for i in p.vertices) and not all(
            math.hypot(ob.data.vertices[i].co.y - 0, ob.data.vertices[i].co.z) < 0.3 for i in p.vertices))
        f = tri_bvh([front, O["WL_Wheel_FL"], O["WL_Wheel_FR"]], lambda ob, p: not (ob is front and all(
            math.hypot(ob.data.vertices[i].co.y - PIVOT[0], ob.data.vertices[i].co.z - PIVOT[1]) < 0.25 for i in p.vertices)))
        if a.overlap(f):
            bad.append(lift)
    print("ARM_vs_FRONT clips at lift:", bad)

    # rear vs front group over steering
    badsteer = []
    for st in range(-40, 41, 5):
        pose(O, st, 0, 0)
        r = tri_bvh([rear, O["WL_Wheel_RL"], O["WL_Wheel_RR"]], lambda ob, p: not (ob is rear and all(
            math.hypot(ob.data.vertices[i].co.x, ob.data.vertices[i].co.y) < 0.3 for i in p.vertices)))
        fr = tri_bvh([front, arm, bucket, O["WL_Wheel_FL"], O["WL_Wheel_FR"]])
        if r.overlap(fr):
            badsteer.append(st)
    print("REAR_vs_FRONT clips at steer:", badsteer)
    pose(O)

    # ram lengths
    print("LIFT_RAM length range %.3f..%.3f   TILT_RAM length range %.3f..%.3f" % (*lift_ram_range(), *tilt_ram_range()))

    # normals: signed volume of every visible mesh, ray tests into the bucket cavity
    print("=== NORMALS")
    neg = [(o.name, round(signed_volume(o), 4)) for o in O if o.type == "MESH" and signed_volume(o) <= 0]
    print("non-positive signed volume:", neg)
    pose(O)
    bm_ = bucket.data
    cx = (-0.40 + EDGE_X - 0.23) / 2
    org = R((0.5, 0.3, FLOOR_TOP + 0.3))
    for nm, d_design in (("down", (0, 0, -1)), ("back", (-1, 0, 0)), ("left", (0, 1, 0)), ("right", (0, -1, 0)), ("front", (1, 0, 0))):
        d = R(d_design)
        hit, loc, nrm, idx = bucket.ray_cast(org, d)
        if hit:
            print("  ray %-5s hit at design %s  n.dir=%.2f  (%s)" % (nm, tuple(round(c, 3) for c in D(loc)), nrm.dot(d), "OK inward-facing" if nrm.dot(d) < 0 else "FLIPPED"))
        else:
            print("  ray %-5s no hit (open side)" % nm)

    # cavity volume (true, back-profile and tapered walls)
    inner = [(x + BACK_HALF, z) for x, z in BACK_C]

    def back_x(z):
        for (x0, z0), (x1, z1) in zip(inner, inner[1:]):
            if z0 <= z <= z1:
                return x0 + (x1 - x0) * (z - z0) / (z1 - z0)
        return inner[-1][0]

    vol, n = 0.0, 0.05
    xs = [(-0.5 + n * (i + 0.5)) for i in range(int(2.3 / n))]
    zs = [FLOOR_TOP + n * (j + 0.5) for j in range(int(1.2 / n))]
    for x in xs:
        top = SIDE_TOP_BACK + (SIDE_TOP_FRONT - SIDE_TOP_BACK) * (x - (-0.42)) / (1.50 - (-0.42))
        for z in zs:
            if x > back_x(z) and x < 1.55 and z < top and z < BACK_TOP:
                vol += n * n * n * (2 * (BUCKET_Y - WALL)) / n
    print("CAVITY_VOLUME_TRUE_m3 ~ %.2f ; Vol_Cavity box = %.2f m3" % (
        vol, (cav_hi[0] - cav_lo[0]) * (cav_hi[1] - cav_lo[1]) * 2 * 0 + (cav_hi[0] - cav_lo[0]) * 2 * (BUCKET_Y - WALL) * (cav_hi[2] - cav_lo[2])))
    print("CAV_BOUNDS_BUCKET_LOCAL_DESIGN lo=%s hi=%s" % (cav_lo, cav_hi))
    print("HIER:")
    for ob in sorted(O, key=lambda o: o.name):
        print("  %-22s parent=%s rot=%s" % (ob.name, ob.parent.name if ob.parent else None,
                                            tuple(round(math.degrees(a), 2) for a in ob.rotation_euler)))
    pose(O)


main()
