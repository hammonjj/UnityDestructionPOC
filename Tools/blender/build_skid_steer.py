"""Author the low-poly wheeled skid-steer loader (~3.5 t, Cat 262 style). A PROP, not a blockout.

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/build_skid_steer.py

Writes Tools/blender/SkidSteer/SkidSteer.blend and
DestructionPOC/Assets/Destruction/Models/SkidSteer/SkidSteer.fbx.

Same conventions as build_excavator.py / build_wrecking_crane.py: geometry is authored in "design space"
(x = forward, y = left, z = up) and mapped to Blender space by a +90 deg Z rotation (machine faces
Blender +Y). All articulated nodes rotate about design y = Blender local X. Unlike the excavator, every
animated node is authored AT REST WITH ROTATION 0 (geometry baked in its rest pose), so the Unity code
applies signed deltas from identity.
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "SkidSteer")
BLEND_PATH = os.path.join(OUT_DIR, "SkidSteer.blend")
FBX_DIR = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "SkidSteer")
FBX_PATH = os.path.join(FBX_DIR, "SkidSteer.fbx")

RZ = Matrix.Rotation(math.radians(90), 4, "Z")


def R(v):
    """Design-space point/offset -> Blender space."""
    return Vector((-v[1], v[0], v[2]))


# ---------------------------------------------------------------- layout (design space, metres, degrees)
TIRE_R, TIRE_W = 0.425, 0.30
WHEEL_X = 0.60                      # axle at +-0.6 -> wheelbase 1.2
WHEEL_Y = 0.775                     # tire centre -> 1.85 over tires
BODY_HW = 0.58                      # body half width (inside the tyre inner face at 0.625)
PIVOT = (-0.65, 1.65)               # lift pivot pin (x, z) in root space, y = 0
PIN_REST = (1.33, 0.50)             # bucket pivot pin at rest (bucket on the ground)
ARM_Y0, ARM_Y1 = 0.645, 0.725       # lift arm plate y range (each side)
EAR_Y0, EAR_Y1 = 0.735, 0.795       # bucket ear plate y range (each side)
CHORD = (PIN_REST[0] - PIVOT[0], PIN_REST[1] - PIVOT[1])
ARM_L = math.hypot(*CHORD)                                # pivot pin -> bucket pin
REST_DEG = math.degrees(math.atan2(CHORD[1], CHORD[0]))   # chord elevation at rest (negative)
MAXH = 3.0                                                # max bucket pin height
MAX_DEG = math.degrees(math.asin((MAXH - PIVOT[1]) / ARM_L))   # chord elevation at max height
LIFT_RANGE = MAX_DEG - REST_DEG                           # SS_LiftArm rotation delta, rest -> max
LIFT_RAM_CH = (-0.90, 0.80, 0.90)   # chassis end of the lift ram (design, root space; mirrored in y)
LIFT_RAM_ARM = (1.20, -0.05)        # arm end, arm-chord frame
TILT_RAM_ARM = (1.40, 0.32)         # tilt ram base on the arm, arm-chord frame
TILT_TUBE = (1.50, 0.15)            # cross tube, arm-chord frame
TILT_RAM_BUCKET = (0.0, 0.36)       # tilt ram rod pin, bucket-local (x, z)
ROOF_Z = 2.10

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


# ---------------------------------------------------------------- geometry helpers (as in build_excavator.py)
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


def rot_xz(pts, deg):
    """Rotate (x, z) points about the origin; positive turns +x toward +z."""
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [(x * c - z * s, x * s + z * c) for x, z in pts]


def finish(name, bm, loc=(0, 0, 0), rz=True):
    if rz:
        bmesh.ops.transform(bm, matrix=RZ, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    used = sorted({f.material_index for f in bm.faces})
    remap = {u: i for i, u in enumerate(used)}
    for f in bm.faces:
        f.material_index = remap[f.material_index]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if not name.startswith(("Col_", "Vol_")):          # helper cubes carry no material
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


def collider(name, par, centre, size, pitch_deg=0.0):
    """Axis-aligned cube mesh (no material) in the parent's space; size is design (x, y, z)."""
    sx, sy, sz = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((sx, sy, sz, 1)))
    ob = finish(name, bm, loc=centre)
    ob.rotation_euler = Euler((math.radians(pitch_deg), 0, 0))
    ob.display_type = "WIRE"
    parent(ob, par)
    return ob


def C(a, b):
    """Arm-chord frame (a along the chord, b perpendicular) -> arm-local rest design (x, z)."""
    return rot_xz([(a, b)], REST_DEG)[0]


# ---------------------------------------------------------------- wheels
def build_wheel(name, loc):
    bm = bmesh.new()
    add_cyl(bm, (0, 0, 0), 0.40, TIRE_W, "y", 16, CHAR)
    for i in range(12):   # chunky tread lugs; one always points straight down so the bottom is at z=0
        a = math.radians(i * 30)
        m = Matrix.Rotation(a, 4, "Y") @ Matrix.Translation((0, 0, 0.40)) @ Matrix.Diagonal((0.11, TIRE_W, 0.05, 1))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        _mat({f for v in r["verts"] for f in v.link_faces}, CHAR)
    add_cyl(bm, (0, 0, 0), 0.25, TIRE_W + 0.004, "y", 12, STEEL)          # rim
    add_cyl(bm, (0, 0, 0), 0.08, TIRE_W + 0.006, "y", 6, STEEL)           # hub nut
    return finish(name, bm, loc=loc)


# ---------------------------------------------------------------- chassis (body, cab cage, engine hood)
def build_chassis():
    bm = bmesh.new()
    add_box(bm, (-1.28, -BODY_HW, 0.30), (0.78, BODY_HW, 0.62), YEL)               # belly
    add_box(bm, (-1.00, -0.50, 0.25), (0.60, 0.50, 0.30), CHAR)                    # skid plate
    add_box(bm, (-1.32, -BODY_HW, 0.30), (-1.26, BODY_HW, 0.62), CHAR)             # rear bumper
    add_box(bm, (-0.45, -BODY_HW, 0.62), (0.78, BODY_HW, 0.67), YEL)               # cab floor
    for s in (-1, 1):                                                              # sills
        add_box(bm, (-0.45, min(s * 0.46, s * BODY_HW), 0.67), (0.78, max(s * 0.46, s * BODY_HW), 0.95), YEL)
    add_box(bm, (0.62, -BODY_HW, 0.67), (0.78, BODY_HW, 0.95), YEL)                # front wall
    add_box(bm, (0.78, -0.40, 0.45), (0.82, 0.40, 0.85), CHAR)                     # front grille plate
    # engine hood with curved rear
    prof = [(-0.45, 0.62), (-0.45, 1.30), (-0.85, 1.30)]
    arc = []
    for i in range(1, 6):
        a = math.radians(90 - 90 * i / 5)
        arc.append((-0.85 - 0.45 * math.cos(a), 0.75 + 0.55 * math.sin(a)))
    prof += arc + [(-1.30, 0.62)]
    add_prism_xz(bm, prof, -BODY_HW, BODY_HW, YEL)
    for (x, z) in arc[:-1]:                                                        # rear grille slats on the curve
        add_box(bm, (x - 0.045, -0.46, z - 0.025), (x + 0.0, 0.46, z + 0.025), CHAR)
    for x in (-0.80, -0.68, -0.56):                                                # top vents
        add_box(bm, (x, -0.38, 1.30), (x + 0.07, 0.38, 1.33), CHAR)
    add_cyl(bm, (-0.62, -0.42, 1.52), 0.04, 0.44, "z", 6, STEEL)                   # exhaust
    # arm towers + lift ram brackets
    tower = [(-1.00, 0.85), (-0.40, 0.85), (-0.42, 1.30), (-0.55, 1.80), (-0.82, 1.80), (-0.95, 1.25)]
    for s in (-1, 1):
        add_prism_xz(bm, tower, min(s * BODY_HW, s * (ARM_Y0 - 0.005)), max(s * BODY_HW, s * (ARM_Y0 - 0.005)), YEL)
        add_box(bm, (-0.96, min(s * 0.64, s * 0.86), 0.85), (-0.84, max(s * 0.64, s * 0.86), 1.00), YEL)
        add_cyl(bm, (LIFT_RAM_CH[0], s * LIFT_RAM_CH[1], LIFT_RAM_CH[2]), 0.045, 0.16, "y", 8, STEEL)
    # cab cage (ROPS): posts, roof guard, rails, glass
    for x in (-0.45, 0.55):
        for s in (-1, 1):
            add_box(bm, (x - 0.03, s * 0.52 - 0.03, 0.67), (x + 0.03, s * 0.52 + 0.03, ROOF_Z - 0.06), CHAR)
    for s in (-1, 1):                                                              # roof side rails
        add_box(bm, (-0.50, s * 0.52 - 0.03, ROOF_Z - 0.06), (0.68, s * 0.52 + 0.03, ROOF_Z), CHAR)
    for x in (-0.50, 0.62):                                                        # roof cross rails
        add_box(bm, (x - 0.0, -0.55, ROOF_Z - 0.06), (x + 0.06, 0.55, ROOF_Z), CHAR)
    for x in (0.0, 0.20, 0.40):                                                    # guard bars
        add_box(bm, (x, -0.52, ROOF_Z - 0.05), (x + 0.05, 0.52, ROOF_Z), CHAR)
    add_box(bm, (-0.45, -0.46, ROOF_Z - 0.045), (0.55, 0.46, ROOF_Z - 0.035), GLASS)   # skylight under the bars
    for s in (-1, 1):                                                              # lower side rails
        add_box(bm, (-0.45, s * 0.52 - 0.025, 1.05), (0.55, s * 0.52 + 0.025, 1.10), CHAR)
    add_box(bm, (0.52, -0.52, 1.05), (0.58, 0.52, 1.10), CHAR)                     # front lower bar
    add_box(bm, (-0.46, -0.46, 1.10), (-0.44, 0.46, 1.95), GLASS)                  # rear window
    for s in (-1, 1):                                                              # rear quarter windows
        add_box(bm, (-0.44, s * 0.52 - 0.01, 1.12), (-0.05, s * 0.52 + 0.01, 1.95), GLASS)
    # seat, lap bar, levers
    add_box(bm, (-0.35, -0.22, 0.67), (0.05, 0.22, 0.80), CHAR)
    add_box(bm, (-0.40, -0.22, 0.80), (-0.33, 0.22, 1.40), CHAR)
    add_cyl(bm, (0.14, 0, 1.12), 0.02, 0.92, "y", 6, STEEL)
    for s in (-1, 1):
        add_cyl(bm, (0.25, s * 0.30, 0.95), 0.02, 0.30, "z", 6, STEEL)
    # paint chips
    add_box(bm, (-1.1, BODY_HW, 0.70), (-0.8, BODY_HW + 0.012, 0.88), WORN)
    add_box(bm, (-0.2, -BODY_HW - 0.012, 0.70), (0.3, -BODY_HW, 0.82), WORN)
    add_box(bm, (-1.0, -0.3, 1.30), (-0.9, 0.3, 1.33), WORN)
    return finish("SS_Chassis", bm)


# ---------------------------------------------------------------- lift arm (rest pose baked, origin = pivot pin)
ARM_OUTLINE = []


def build_arm():
    bm = bmesh.new()
    centre = [(-0.25, 0.05), (0.30, 0.10), (1.00, 0.18), (1.70, 0.14), (ARM_L, 0.0)]
    half = [0.12, 0.13, 0.14, 0.12, 0.10]
    outline = rot_xz(strip_outline(centre, half), REST_DEG)
    ARM_OUTLINE[:] = outline
    for s in (-1, 1):
        y0, y1 = sorted((s * ARM_Y0, s * ARM_Y1))
        add_prism_xz(bm, outline, y0, y1, YEL)
        yc = s * (ARM_Y0 + ARM_Y1) / 2
        add_cyl(bm, (0, yc, 0), 0.10, 0.26, "y", 8, STEEL)                          # pivot boss/pin
        px, pz = C(*LIFT_RAM_ARM)
        add_box(bm, (px - 0.06, min(s * 0.725, s * 0.84), pz - 0.08), (px + 0.06, max(s * 0.725, s * 0.84), pz + 0.08), YEL)
        add_cyl(bm, (px, s * LIFT_RAM_CH[1], pz), 0.045, 0.16, "y", 8, STEEL)        # lift ram pin
        tx, tz = C(ARM_L, 0)
        add_cyl(bm, (tx, yc, tz), 0.10, 0.26, "y", 8, STEEL)                         # bucket pivot boss
    tx, tz = C(*TILT_TUBE)
    add_cyl(bm, (tx, 0, tz), 0.06, 2 * ARM_Y1 + 0.02, "y", 8, YEL)                   # cross tube
    bx, bz = C(*TILT_RAM_ARM)
    for s in (-1, 1):                                                               # tilt ram base lugs
        add_tube(bm, (tx, s * 0.08, tz), (bx, s * 0.08, bz), 0.035, 6, YEL)
    add_cyl(bm, (bx, 0, bz), 0.04, 0.26, "y", 8, STEEL)
    return finish("SS_LiftArm", bm, loc=(PIVOT[0], 0, PIVOT[1]))


# ---------------------------------------------------------------- bucket (origin = bucket pivot pin)
EDGE_X, FLOOR_Z, TOP_Z = 0.97, -0.50, 0.10      # bucket-local: cutting edge tip x, ground z, back top z
BACK_X = 0.12                                   # outer face of the back plate
BUCKET_POLY = []


def build_bucket():
    bm = bmesh.new()
    hw = 0.925                                    # inside face of side walls
    centre_prof = [(0.88, -0.50), (0.30, -0.50), (0.12, -0.32), (0.12, 0.10), (0.17, 0.10), (0.17, -0.299),
                   (0.321, -0.45), (0.88, -0.45)]
    BUCKET_POLY[:] = centre_prof
    add_prism_xz(bm, centre_prof, -(hw + 0.025), hw + 0.025, YEL)                   # floor + chamfer + back
    wall = [(0.97, -0.50), (0.30, -0.50), (0.12, -0.32), (0.12, 0.10), (0.45, 0.10), (0.97, -0.12)]
    for s in (-1, 1):
        y0, y1 = sorted((s * hw, s * (hw + 0.05)))
        add_prism_xz(bm, wall, y0, y1, YEL)
    add_prism_xz(bm, [(0.80, -0.50), (0.97, -0.50), (0.88, -0.43), (0.80, -0.43)], -0.97, 0.97, STEEL)  # edge
    for y in (-0.40, 0.40, -0.85, 0.85):                                             # rear ribs
        add_box(bm, (0.07, y - 0.03, -0.30), (0.12, y + 0.03, 0.08), YEL)
    add_box(bm, (0.08, -hw, 0.06), (0.17, hw, 0.10), YEL)                            # top back rail
    ear = [(0.14, -0.22), (0.14, 0.12), (0.0, 0.13), (-0.10, 0.0), (-0.10, -0.05), (0.0, -0.15)]
    for s in (-1, 1):
        y0, y1 = sorted((s * EAR_Y0, s * EAR_Y1))
        add_prism_xz(bm, ear, y0, y1, YEL)
        add_cyl(bm, (0, s * 0.68, 0), 0.055, 0.26, "y", 8, STEEL)                    # pivot pin
    # centre bracket for the tilt ram
    add_prism_xz(bm, [(0.17, -0.20), (0.17, 0.30), (0.05, 0.44), (-0.06, 0.38), (-0.04, 0.20), (0.10, -0.05)],
                 -0.09, 0.09, YEL)
    add_cyl(bm, (TILT_RAM_BUCKET[0], 0, TILT_RAM_BUCKET[1]), 0.04, 0.26, "y", 8, STEEL)
    return finish("SS_Bucket", bm)


# ---------------------------------------------------------------- rams
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


def point_in_poly(p, poly):
    x, z = p
    inside = False
    n = len(poly)
    for i in range(n):
        x1, z1 = poly[i]
        x2, z2 = poly[(i + 1) % n]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1) + x1:
            inside = not inside
    return inside


def edge_samples(poly, step=0.04):
    pts = []
    n = len(poly)
    for i in range(n):
        a, b = Vector(poly[i]), Vector(poly[(i + 1) % n])
        k = max(1, int((b - a).length / step))
        pts += [tuple(a + (b - a) * t / k) for t in range(k)]
    return pts


# ---------------------------------------------------------------- main
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    make_materials()

    root = empty("SkidSteer_Root", (0, 0, 0), None, 1.0, "ARROWS")
    chassis = build_chassis()
    parent(chassis, root)

    wheels = {}
    for nm, (sx, sy) in (("FL", (1, 1)), ("FR", (1, -1)), ("RL", (-1, 1)), ("RR", (-1, -1))):
        w = build_wheel("SS_Wheel_" + nm, (sx * WHEEL_X, sy * WHEEL_Y, TIRE_R))
        parent(w, chassis)
        wheels[nm] = w

    arm = build_arm()
    parent(arm, root)
    bucket = build_bucket()
    bucket.location = R((PIN_REST[0] - PIVOT[0], 0, PIN_REST[1] - PIVOT[1]))
    parent(bucket, arm)

    # anchors
    empty("Anchor_Seat", (-0.15, 0.0, 1.52), chassis, 0.2)
    empty("Anchor_Door", (0.40, 1.35, 0.0), chassis, 0.3)
    empty("Anchor_Exit_L", (0.0, 1.45, 0.0), chassis, 0.3)
    empty("Anchor_Exit_R", (0.0, -1.45, 0.0), chassis, 0.3)
    empty("Anchor_CameraFocus", (WHEEL_X + 2.0, 0.0, 0.0), root, 0.3)
    empty("Anchor_BucketEdge", (EDGE_X, 0, FLOOR_Z), bucket, 0.15)
    empty("Anchor_BucketCavity", (0.55, 0, -0.20), bucket, 0.2)

    # colliders
    collider("Col_Body", chassis, (-0.25, 0, 0.46), (2.06, 1.16, 0.32))
    collider("Col_Hood", chassis, (-0.875, 0, 0.96), (0.85, 1.16, 0.68))
    collider("Col_Cab", chassis, (0.09, 0, 2.07), (1.18, 1.16, 0.06))     # overhead guard slab only (posts stay open)
    for nm, (sx, sy) in (("FL", (1, 1)), ("FR", (1, -1)), ("RL", (-1, 1)), ("RR", (-1, -1))):
        collider("Col_Wheel_" + nm, chassis, (sx * WHEEL_X, sy * WHEEL_Y, TIRE_R), (0.85, TIRE_W, 0.85))
    for s, sfx in ((1, "L"), (-1, "R")):
        mx, mz = C(ARM_L / 2, 0.05)
        collider("Col_Arm_" + sfx, arm, (mx, s * (ARM_Y0 + ARM_Y1) / 2, mz), (ARM_L, 0.08, 0.26), REST_DEG)
    collider("Col_Bucket_Floor", bucket, (0.635, 0, -0.475), (0.67, 1.85, 0.05))
    collider("Col_Bucket_Back", bucket, (0.145, 0, -0.10), (0.05, 1.85, 0.40))
    collider("Col_Bucket_SideL", bucket, (0.545, 0.95, -0.20), (0.85, 0.05, 0.60))
    collider("Col_Bucket_SideR", bucket, (0.545, -0.95, -0.20), (0.85, 0.05, 0.60))
    collider("Col_Bucket_Edge", bucket, (0.885, 0, -0.465), (0.17, 1.94, 0.07))
    collider("Vol_Cavity", bucket, (0.57, 0, -0.175), (0.80, 1.85, 0.55))
    update()

    def W(ob, p):
        return ob.matrix_world @ R(p)

    # rams: barrel on the base part, rod on the moving part; each half points at the other's pin at rest
    for s, sfx in ((1, "L"), (-1, "R")):
        base_w = W(chassis, (LIFT_RAM_CH[0], s * LIFT_RAM_CH[1], LIFT_RAM_CH[2]))
        px, pz = C(*LIFT_RAM_ARM)
        end_w = W(arm, (px, s * LIFT_RAM_CH[1], pz))
        build_ram_part("SS_LiftRam_" + sfx, base_w, end_w, chassis, 0.06, 0.60, 0.075)
        build_ram_part("SS_LiftRamRod_" + sfx, end_w, base_w, arm, 0.035, 0.55, 0.06)
    bx, bz = C(*TILT_RAM_ARM)
    base_w = W(arm, (bx, 0, bz))
    end_w = W(bucket, (TILT_RAM_BUCKET[0], 0, TILT_RAM_BUCKET[1]))
    build_ram_part("SS_TiltRam", base_w, end_w, arm, 0.055, 0.60, 0.07)
    build_ram_part("SS_TiltRamRod", end_w, base_w, bucket, 0.035, 0.55, 0.06)

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

    report(root, chassis, arm, bucket, wheels)


# ---------------------------------------------------------------- numeric verification (after export)
def dz(v):
    return (round(v.y, 3), round(-v.x, 3), round(v.z, 3))


def report(root, chassis, arm, bucket, wheels):
    O = bpy.data.objects
    tris = sum(len(p.vertices) - 2 for ob in O if ob.type == "MESH" for p in ob.data.polygons)
    vis = sum(len(p.vertices) - 2 for ob in O if ob.type == "MESH" and not ob.name.startswith(("Col_", "Vol_"))
              for p in ob.data.polygons)
    print("TRIS total", tris, "visible", vis)
    for ob in sorted(O, key=lambda o: o.name):
        t = sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == "MESH" else 0
        print("OBJ %-24s parent=%-14s world(design)=%s tris=%d" %
              (ob.name, ob.parent.name if ob.parent else "-", dz(ob.matrix_world.translation), t))
    print("ARM_L %.4f REST_DEG %.3f MAX_DEG %.3f LIFT_RANGE %.3f" % (ARM_L, REST_DEG, MAX_DEG, LIFT_RANGE))

    def bbox(objs):
        pts = [ob.matrix_world @ Vector(c) for ob in objs for c in ob.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        return lo, hi
    vis_objs = [ob for ob in O if ob.type == "MESH" and not ob.name.startswith(("Col_", "Vol_", "SS_LiftRam", "SS_TiltRam"))]
    lo, hi = bbox(vis_objs)
    print("BBOX design: x %.3f..%.3f (len %.3f)  y %.3f..%.3f (w %.3f)  z %.3f..%.3f" %
          (lo.y, hi.y, hi.y - lo.y, -hi.x, -lo.x, hi.x - lo.x, lo.z, hi.z))
    wl, wh = bbox(list(wheels.values()))
    print("WHEELS over-tire width %.3f  tire bottom z %.4f  top %.3f" % (wh.x - wl.x, wl.z, wh.z))
    cl = [O[n] for n in ("SS_Chassis",)]
    print("CAB roof top z %.3f" % bbox(cl)[1].z)
    bl, bh = bbox([bucket])
    print("BUCKET width %.3f depth(x) %.3f height %.3f; edge tip x(design) %.3f" % (bh.x - bl.x, bh.y - bl.y, bh.z - bl.z, bh.y))

    # ---- normals / volume of the bucket
    me = bucket.data
    bm = bmesh.new()
    bm.from_mesh(me)
    print("BUCKET signed volume %.4f (positive = outward normals)" % bm.calc_volume(signed=True))
    bm.free()

    def chk(label, centroid_pred, normal_pred):
        bad = good = 0
        for p in me.polygons:
            c = p.center
            if centroid_pred(c):
                if normal_pred(p.normal):
                    good += 1
                else:
                    bad += 1
        print("NORMALS %-26s good=%d bad=%d" % (label, good, bad))
    # Blender coords: design x -> Y, design y -> -X
    chk("floor top faces +z", lambda c: abs(c.z + 0.45) < 1e-4 and abs(c.y - 0.6) < 0.2 and abs(c.x) < 0.8,
        lambda n: n.z > 0.9)
    chk("back inner faces +x(design)", lambda c: abs(c.y - 0.17) < 1e-4 and abs(c.x) < 0.8,
        lambda n: n.y > 0.9)
    chk("left wall inner faces", lambda c: abs(c.x + 0.925) < 1e-4 and c.y > 0.3, lambda n: n.x > 0.9)
    chk("right wall inner faces", lambda c: abs(c.x - 0.925) < 1e-4 and c.y > 0.3, lambda n: n.x < -0.9)

    # ---- posing helpers (design angles: a = rotation_euler.x, +x toward +z)
    def pose(lift, bx):
        arm.rotation_euler = Euler((math.radians(lift), 0, 0))
        bucket.rotation_euler = Euler((math.radians(bx), 0, 0))
        update()

    print("---- poses: lift = SS_LiftArm rotation_euler.x delta from rest; bucket = rotation_euler.x"
          " (contract angle = -rotation_euler.x)")
    for lift, bx in ((0, 0), (0, 45), (0, -55), (LIFT_RANGE, 0), (LIFT_RANGE, -55), (LIFT_RANGE, 45), (30, 45)):
        pose(lift, bx)
        print("POSE lift=%6.2f bucket_rotX=%6.1f  pin=%s edge=%s lip_hinge(back top)=%s" %
              (lift, bx, dz(bucket.matrix_world.translation),
               dz(O["Anchor_BucketEdge"].matrix_world.translation),
               dz(bucket.matrix_world @ R((BACK_X, 0, TOP_Z)))))
    pose(LIFT_RANGE, -55)
    print("MAX height pin z %.3f ; dumped hinge (back-top) z %.3f ; dumped cutting edge z %.3f" %
          (bucket.matrix_world.translation.z, (bucket.matrix_world @ R((BACK_X, 0, TOP_Z))).z,
           O["Anchor_BucketEdge"].matrix_world.translation.z))

    # ---- bucket body vs arm outline (arm frame == bucket parent frame, so pure 2D in design)
    pts = edge_samples(BUCKET_POLY)
    outl = ARM_OUTLINE

    def arm_clip(bx_deg):
        hit = 0
        for p in pts:
            x, z = rot_xz([p], bx_deg)[0]
            if point_in_poly((x, z), [(ox - 0.0, oz) for ox, oz in outl_rel]):
                hit += 1
        return hit
    # arm outline relative to the bucket pin (bucket origin)
    pinrel = (PIN_REST[0] - PIVOT[0], PIN_REST[1] - PIVOT[1])
    outl_rel = [(ox - pinrel[0], oz - pinrel[1]) for ox, oz in outl]
    free = [b for b in range(-150, 151) if arm_clip(b) == 0]
    # contiguous free interval containing 0
    lo_b = hi_b = 0
    while lo_b - 1 in free:
        lo_b -= 1
    while hi_b + 1 in free:
        hi_b += 1
    print("BUCKET-vs-ARM free rotation_euler.x range: %d .. %d (contract angle %d .. %d)" % (lo_b, hi_b, -hi_b, -lo_b))

    # ---- bucket vs chassis (cab frame rect, wheels, hood/body), over lift x bucket grid
    def world_pts(lift, bx):
        out = []
        for p in pts:
            q = rot_xz([p], bx)[0]                                  # about the pin
            r_ = rot_xz([(pinrel[0] + q[0], pinrel[1] + q[1])], lift)[0]   # about the arm pivot
            out.append((PIVOT[0] + r_[0], PIVOT[1] + r_[1]))
        return out

    def hits_chassis(wp):
        for x, z in wp:
            if -0.52 < x < 0.68 and 0.62 < z < ROOF_Z + 0.02:
                return "cab"
            if -1.34 < x < 0.82 and 0.25 < z < 0.67:
                return "body"
            if -1.30 < x < -0.45 and z < 1.33:
                return "hood"
            for wx in (WHEEL_X, -WHEEL_X):
                if math.hypot(x - wx, z - TIRE_R) < TIRE_R:
                    return "wheel"
            if z < 0.0:
                return "ground"
        return None
    worst = {}
    for lift in [i * LIFT_RANGE / 22 for i in range(23)]:
        for bx_ in (-55, -45, 0, 45, 55):
            if bx_ < lo_b or bx_ > hi_b:
                continue
            h = hits_chassis(world_pts(lift, bx_))
            if h:
                worst.setdefault(h, []).append((round(lift, 1), bx_))
    print("BUCKET-vs-CHASSIS/GROUND hits (lift, rotX):", {k: v[:6] for k, v in worst.items()} or "none")

    for bxd in (-45, -55):
        l = 0.0
        while l < LIFT_RANGE and hits_chassis(world_pts(l, bxd)):
            l += 0.25
        print("MIN LIFT (deg above rest) for rotX %d (contract dump +%d) to clear ground/chassis: %.2f" % (bxd, -bxd, l))

    # ---- arm outline vs wheels / ground over the lift range
    mind = 9
    for lift in [i * LIFT_RANGE / 22 for i in range(23)]:
        for p in edge_samples(outl):
            x, z = rot_xz([p], lift)[0]
            x += PIVOT[0]
            z += PIVOT[1]
            for wx in (WHEEL_X, -WHEEL_X):
                mind = min(mind, math.hypot(x - wx, z - TIRE_R) - TIRE_R)
    print("ARM min clearance to tyre circles over range: %.3f m (y ranges overlap: arm %.3f..%.3f vs tyre inner 0.625)" %
          (mind, ARM_Y0, ARM_Y1))

    # ---- ram lengths across the lift range (with the bucket at a few tilts)
    for s, sfx in (("L", "L"),):
        for lift in (0, LIFT_RANGE / 2, LIFT_RANGE):
            pose(lift, 0)
            b = O["SS_LiftRam_L"].matrix_world.translation
            r = O["SS_LiftRamRod_L"].matrix_world.translation
            print("LIFT RAM length at lift %.1f: %.3f" % (lift, (r - b).length))
    for lift, bx_ in ((0, 0), (0, 45), (0, -55), (LIFT_RANGE, 0)):
        pose(lift, bx_)
        b = O["SS_TiltRam"].matrix_world.translation
        r = O["SS_TiltRamRod"].matrix_world.translation
        print("TILT RAM length lift %.1f rotX %d: %.3f" % (lift, bx_, (r - b).length))
    pose(0, 0)


main()
