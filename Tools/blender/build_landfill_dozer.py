"""Author the low-poly landfill (waste-handler) bulldozer (~38 t, Cat D8T-WH / SU-blade style). A PROP, not a blockout.

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/build_landfill_dozer.py

Writes Tools/blender/LandfillDozer/LandfillDozer.blend and
DestructionPOC/Assets/Destruction/Models/LandfillDozer/LandfillDozer.fbx.

Same conventions as build_skid_steer.py / build_wheel_loader.py: geometry is authored in "design space"
(x = forward, y = left, z = up, metres) and mapped to Blender space by a +90 deg Z rotation (machine faces
Blender +Y). Every animated node is authored AT REST WITH ROTATION 0 (geometry baked in its rest pose, node
origin at its pivot) and rotates about Blender local X = the design lateral axis. Positive rotation_euler.x
turns design +x toward +z. The script also poses the rig headless and prints the verified numbers.
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "LandfillDozer")
BLEND_PATH = os.path.join(OUT_DIR, "LandfillDozer.blend")
FBX_DIR = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "LandfillDozer")
FBX_PATH = os.path.join(FBX_DIR, "LandfillDozer.fbx")

RZ = Matrix.Rotation(math.radians(90), 4, "Z")


def R(v):
    """Design-space point/offset -> Blender space."""
    return Vector((-v[1], v[0], v[2]))


def D(v):
    """Blender -> design."""
    return Vector((v.y, -v.x, v.z))


# ---------------------------------------------------------------- layout (design space, metres, degrees)
TRACK_Y = 1.62                      # track centre line (+-); shoes 0.66 wide -> 3.90 m over tracks
SHOE_W = 0.66
TRACK_IN, TRACK_OUT = TRACK_Y - SHOE_W / 2, TRACK_Y + SHOE_W / 2     # 1.29 / 1.95
SHOE_T = 0.06                       # grouser height (ring outer sits 0.06 above the ground)
RING_T = 0.14                       # ring (shoe + link) thickness
IDL_C, IDL_R = (1.65, 0.65 + SHOE_T), 0.65     # idler centre (x, z) and outer hull radius
SPR_C, SPR_R = (-2.05, 1.25), 0.62             # elevated drive sprocket centre and outer hull radius
HEEL_X = -1.55                      # rear end of the flat ground contact (front = idler bottom 1.65 -> 3.2 m flat)
HULL_HW = 1.22                      # hull half width between the track frames

PIV = (2.10, 0.80)                  # push-arm trunnion pin (x, z), y = 0 axis
FACE_X = 4.40                       # blade face plane (flat centre section)
EDGE_FRONT = 4.46                   # front of the bolt-on edge segments
BLADE_HW = 2.15                     # blade half width -> 4.30 m
FLAT_HW = 1.50                      # flat centre section half width; ends wrap forward
WRAP = 0.22                         # forward wrap of each end over the 0.65 m end section
FACE_TOP = 1.90                     # face height
RACK_TOP = 2.90
ARM_Y0, ARM_Y1 = 2.00, 2.14         # push-arm plate y range (outside the tracks, mirrored)
CH_PIN = (2.30, 1.05, 1.55)         # lift ram chassis pin (x, y, z), y mirrored
BL_PIN = (3.95, 1.05, 1.45)         # lift ram blade pin
MAX_LIFT = 38.0                     # deg
ROOF_Z = 3.50
CAB_FLOOR = 1.80
HOOD_TOP = 2.15
WIN_Z0 = 2.20

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


# ---------------------------------------------------------------- geometry helpers (as the skid steer / wheel loader)
def _mat(faces, mat):
    for f in faces:
        f.material_index = mat


def add_box(bm, lo, hi, mat):
    lo, hi = Vector(lo), Vector(hi)
    c, s = (lo + hi) / 2, hi - lo
    m = Matrix.Translation(c) @ Matrix.Diagonal((s.x, s.y, s.z, 1))
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    _mat({f for v in r["verts"] for f in v.link_faces}, mat)


def B(bm, xr, yr, zr, mat):
    """Box from (min, max) ranges, any order."""
    add_box(bm, (min(xr), min(yr), min(zr)), (max(xr), max(yr), max(zr)), mat)


def BS(bm, xr, yr, zr, mat):
    """Box at +y range plus its mirror at -y."""
    B(bm, xr, yr, zr, mat)
    B(bm, xr, (-yr[0], -yr[1]), zr, mat)


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


ORIGIN = {}          # object name -> design-space world origin (all nodes are authored at rest, rotation 0)


def finish(name, bm, loc=(0, 0, 0), rz=True, world=True):
    """world=True: geometry was authored in design world coordinates and is re-centred on `loc` (the node
    origin / pivot). rz=False: geometry is already in the parent's Blender-local space (ram halves)."""
    if rz:
        if world:
            bmesh.ops.translate(bm, vec=-Vector(loc), verts=bm.verts)
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
    if not name.startswith(("Col_", "Vol_")):
        for u in used:
            me.materials.append(MATS[u])
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    ob.location = R(loc) if rz else Vector(loc)
    bpy.context.scene.collection.objects.link(ob)
    if rz:
        ORIGIN[name] = Vector(loc)
    return ob


def parent(child, par):
    child.parent = par
    child.matrix_parent_inverse = Matrix.Identity(4)


def empty(name, loc_world, par, size=0.3, kind="PLAIN_AXES"):
    """Empty at a design WORLD position; stored relative to the (rest, unrotated) parent."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    base = ORIGIN.get(par.name, Vector((0, 0, 0))) if par is not None else Vector((0, 0, 0))
    e.location = R(Vector(loc_world) - base)
    bpy.context.scene.collection.objects.link(e)
    if par is not None:
        parent(e, par)
    ORIGIN[name] = Vector(loc_world)
    return e


def update():
    bpy.context.view_layer.update()


def collider(name, par, lo, hi, pitch_deg=0.0):
    """Cube helper, design WORLD bounds lo..hi. Size baked into the mesh, origin at its centre."""
    lo, hi = Vector(lo), Vector(hi)
    c, s = (lo + hi) / 2, hi - lo
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((s.x, s.y, s.z, 1)))
    bmesh.ops.transform(bm, matrix=RZ, verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location = R(c - ORIGIN.get(par.name, Vector((0, 0, 0))))
    ob.rotation_euler = Euler((math.radians(pitch_deg), 0, 0))
    ob.display_type = "WIRE"
    ob.hide_render = True
    bpy.context.scene.collection.objects.link(ob)
    parent(ob, par)
    return ob


# ---------------------------------------------------------------- track outline (convex hull of idler + sprocket + heel)
def convex_hull(pts):
    pts = sorted(set(pts))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 1e-9:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 1e-9:
            up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]        # CCW


def circle_pts(c, r, step=15):
    return [(c[0] + r * math.cos(math.radians(a)), c[1] + r * math.sin(math.radians(a))) for a in range(0, 360, step)]


def hull_edge_normals(poly):
    n = len(poly)
    out = []
    for i in range(n):
        a, b = Vector(poly[i]), Vector(poly[(i + 1) % n])
        t = (b - a).normalized()
        out.append(Vector((t.y, -t.x)))      # outward for CCW
    return out


def offset_inward(poly, d):
    n = len(poly)
    norms = hull_edge_normals(poly)
    res = []
    for i in range(n):
        n1, n2 = norms[i - 1], norms[i]
        m = (n1 + n2)
        m = m / (1 + n1.dot(n2))
        res.append((poly[i][0] - m.x * d, poly[i][1] - m.y * d))
    return res


TRACK_OUTER = convex_hull(circle_pts(IDL_C, IDL_R) + circle_pts(SPR_C, SPR_R) + [(HEEL_X, SHOE_T)])
TRACK_INNER = offset_inward(TRACK_OUTER, RING_T)


def upper_z(poly, x):
    """Highest z of a convex polygon at x."""
    best = None
    n = len(poly)
    for i in range(n):
        (x1, z1), (x2, z2) = poly[i], poly[(i + 1) % n]
        if (x1 - x) * (x2 - x) <= 0 and x1 != x2:
            z = z1 + (z2 - z1) * (x - x1) / (x2 - x1)
            best = z if best is None else max(best, z)
    return best


def build_track(name, s):
    bm = bmesh.new()
    y0, y1 = s * TRACK_Y - SHOE_W / 2, s * TRACK_Y + SHOE_W / 2
    o0 = [bm.verts.new((x, y0, z)) for x, z in TRACK_OUTER]
    o1 = [bm.verts.new((x, y1, z)) for x, z in TRACK_OUTER]
    i0 = [bm.verts.new((x, y0, z)) for x, z in TRACK_INNER]
    i1 = [bm.verts.new((x, y1, z)) for x, z in TRACK_INNER]
    n = len(TRACK_OUTER)
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces += [bm.faces.new((o0[i], o0[j], o1[j], o1[i])), bm.faces.new((i0[i], i0[j], i1[j], i1[i])),
                  bm.faces.new((o0[i], o0[j], i0[j], i0[i])), bm.faces.new((o1[i], o1[j], i1[j], i1[i]))]
    _mat(faces, CHAR)
    # grousers: walk the outer perimeter every ~0.30 m
    norms = hull_edge_normals(TRACK_OUTER)
    seg_len = [(Vector(TRACK_OUTER[(i + 1) % n]) - Vector(TRACK_OUTER[i])).length for i in range(n)]
    perim = sum(seg_len)
    count = int(perim / 0.30)
    step = perim / count
    for k in range(count):
        d = (k + 0.5) * step
        i = 0
        while d > seg_len[i]:
            d -= seg_len[i]
            i += 1
        a, b = Vector(TRACK_OUTER[i]), Vector(TRACK_OUTER[(i + 1) % n])
        t = (b - a).normalized()
        p = a + t * d
        nr = norms[i]
        # basis: x = tangent, y = lateral, z = outward normal
        basis = Matrix(((t.x, 0, nr.x), (0, 1, 0), (t.y, 0, nr.y)))
        c = Vector((p.x + nr.x * 0.03, s * TRACK_Y, p.y + nr.y * 0.03))
        m = Matrix.Translation(c) @ basis.to_4x4() @ Matrix.Diagonal((0.07, SHOE_W + 0.02, SHOE_T, 1))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        low = min(v.co.z for v in r["verts"])
        if low < 0:                                  # keep every grouser at or above the ground plane
            for v in r["verts"]:
                v.co.z -= low
        _mat({f for v in r["verts"] for f in v.link_faces}, STEEL)
    return finish(name, bm, loc=(0, s * TRACK_Y, 0))


def build_sprocket(name, s):
    bm = bmesh.new()
    c = (SPR_C[0], s * TRACK_Y, SPR_C[1])
    add_cyl(bm, c, 0.36, 0.22, "y", 12, STEEL)
    add_cyl(bm, c, 0.17, 0.36, "y", 8, CHAR)
    for i in range(12):
        a = math.radians(i * 30)
        m = Matrix.Translation(c) @ Matrix.Rotation(a, 4, "Y") @ Matrix.Translation((0, 0, 0.40)) @ \
            Matrix.Diagonal((0.15, 0.20, 0.12, 1))
        r = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        _mat({f for v in r["verts"] for f in v.link_faces}, STEEL)
    return finish(name, bm, loc=c)


def build_idler(name, s):
    bm = bmesh.new()
    c = (IDL_C[0], s * TRACK_Y, IDL_C[1])
    add_cyl(bm, c, 0.46, 0.20, "y", 12, STEEL)
    for dy in (-0.10, 0.10):
        add_cyl(bm, (c[0], c[1] + dy, c[2]), 0.50, 0.05, "y", 12, CHAR)
    add_cyl(bm, c, 0.18, 0.36, "y", 8, CHAR)
    return finish(name, bm, loc=c)


# ---------------------------------------------------------------- chassis
def build_chassis():
    bm = bmesh.new()
    # --- belly guards (fully enclosed) + deck
    B(bm, (-2.15, 2.20), (-HULL_HW, HULL_HW), (0.45, 0.95), CHAR)                   # belly box
    B(bm, (-1.90, 1.95), (-1.00, 1.00), (0.40, 0.45), STEEL)                        # skid plate
    B(bm, (-2.15, 2.20), (-HULL_HW, HULL_HW), (0.95, 1.30), YEL)                    # deck
    B(bm, (2.20, 2.50), (-0.88, 0.88), (0.55, 1.10), CHAR)                          # front bumper / push bar
    B(bm, (2.50, 2.56), (-0.60, 0.60), (0.70, 0.95), STEEL)                         # tow lug plate
    BS(bm, (2.00, 2.50), (0.90, HULL_HW), (0.95, 1.30), YEL)                        # frame horns
    # --- engine hood + radiator guard
    hood = [(0.35, 1.30), (0.35, HOOD_TOP), (1.55, HOOD_TOP), (2.02, 1.95), (2.20, 1.78), (2.20, 1.30)]
    add_prism_xz(bm, hood, -0.85, 0.85, YEL)
    B(bm, (2.20, 2.26), (-0.80, 0.80), (1.30, 1.85), CHAR)                          # grille backing
    for k in range(9):                                                              # grille bars
        y = -0.72 + k * 0.18
        B(bm, (2.26, 2.31), (y - 0.025, y + 0.025), (1.34, 1.82), STEEL)
    B(bm, (2.26, 2.33), (-0.80, 0.80), (1.28, 1.34), CHAR)                          # grille frame
    B(bm, (2.26, 2.33), (-0.80, 0.80), (1.82, 1.88), CHAR)
    BS(bm, (2.26, 2.33), (0.75, 0.81), (1.30, 1.86), CHAR)
    for x in (0.55, 0.75, 0.95, 1.15):                                              # hood top vents
        B(bm, (x, x + 0.10), (-0.55, 0.55), (HOOD_TOP, HOOD_TOP + 0.03), CHAR)
    BS(bm, (0.45, 1.95), (0.85, 0.90), (1.45, 1.95), CHAR)                          # side mesh screens
    for y in (-0.72, 0.72):                                                         # headlights
        B(bm, (2.14, 2.22), (y - 0.12, y + 0.12), (1.90, 2.02), GLASS)
    B(bm, (0.40, 1.10), (-0.25, 0.25), (HOOD_TOP + 0.03, HOOD_TOP + 0.04), WORN)    # hood paint chip
    # --- exhaust stack, high mounted on the right of the hood, ahead of the cab
    add_cyl(bm, (0.70, -0.55, 2.65), 0.14, 0.80, "z", 8, CHAR)                      # muffler
    add_cyl(bm, (0.70, -0.55, 3.20), 0.07, 0.80, "z", 6, STEEL)                     # pipe, top 3.60
    B(bm, (0.64, 0.78), (-0.62, -0.48), (3.60, 3.64), STEEL)                        # rain cap
    # --- rear: counterweight + tow hitch
    B(bm, (-3.15, -2.15), (-1.15, 1.15), (0.75, 1.30), CHAR)
    B(bm, (-3.15, -2.15), (-1.15, 1.15), (1.30, 1.95), YEL)
    B(bm, (-3.17, -3.15), (-0.90, 0.90), (1.50, 1.85), CHAR)                        # rear grille plate
    B(bm, (-3.55, -3.15), (-0.35, 0.35), (0.80, 1.10), STEEL)                       # drawbar
    add_cyl(bm, (-3.40, 0, 1.05), 0.07, 0.40, "z", 6, STEEL)                        # hitch pin
    B(bm, (-3.15, -2.15), (0.30, 0.50), (1.95, 1.99), WORN)
    B(bm, (-2.15, -1.55), (-0.60, 0.60), (1.30, 1.55), YEL)                         # rear engine deck cover
    # --- cab: floor/riser, walls, ROPS posts, guarded glass, roof
    B(bm, (-1.55, 0.15), (-0.90, 0.90), (1.30, CAB_FLOOR), YEL)                     # riser / floor
    BS(bm, (-1.55, 0.15), (0.84, 0.90), (CAB_FLOOR, WIN_Z0), YEL)                   # side walls
    B(bm, (0.09, 0.15), (-0.90, 0.90), (CAB_FLOOR, WIN_Z0), YEL)                    # front wall
    B(bm, (-1.55, -1.49), (-0.90, 0.90), (CAB_FLOOR, WIN_Z0), YEL)                  # rear wall
    for x in (0.10, -1.52):                                                         # ROPS posts
        BS(bm, (x - 0.05, x + 0.05), (0.88, 0.98), (CAB_FLOOR, ROOF_Z - 0.10), CHAR)
    for y in (-0.88, 0.88):                                                         # centre posts under roof
        pass
    B(bm, (-1.70, 0.30), (-1.02, 1.02), (ROOF_Z - 0.10, ROOF_Z), CHAR)              # ROPS roof plate
    B(bm, (-1.60, 0.20), (-0.90, 0.90), (ROOF_Z - 0.16, ROOF_Z - 0.10), YEL)        # roof liner
    BS(bm, (0.05, 0.14), (0.80, 0.84), (WIN_Z0, ROOF_Z - 0.16), YEL)                # front corner posts (inner)
    B(bm, (0.10, 0.13), (-0.80, 0.80), (WIN_Z0 + 0.02, ROOF_Z - 0.16), GLASS)       # front glass
    B(bm, (-1.52, -1.49), (-0.80, 0.80), (WIN_Z0 + 0.02, ROOF_Z - 0.16), GLASS)     # rear glass
    BS(bm, (-1.45, 0.00), (0.86, 0.89), (WIN_Z0 + 0.02, ROOF_Z - 0.16), GLASS)      # side glass
    for z in (2.55, 2.90, 3.25):                                                    # front window guard bars
        B(bm, (0.15, 0.20), (-0.88, 0.88), (z, z + 0.04), CHAR)
    for y in (-0.5, 0.0, 0.5):
        B(bm, (0.15, 0.20), (y - 0.02, y + 0.02), (WIN_Z0, ROOF_Z - 0.10), CHAR)
    for z in (2.55, 3.00):                                                          # side guard bars
        BS(bm, (-1.50, 0.12), (0.90, 0.95), (z, z + 0.04), CHAR)
    BS(bm, (-0.70, -0.66), (0.90, 0.95), (WIN_Z0, ROOF_Z - 0.10), CHAR)
    for z in (2.55, 3.00):                                                          # rear guard bars
        B(bm, (-1.58, -1.53), (-0.88, 0.88), (z, z + 0.04), CHAR)
    B(bm, (-1.0, -0.2), (0.86, 0.91), (CAB_FLOOR + 0.1, WIN_Z0 - 0.05), WORN)       # left door panel (chipped)
    B(bm, (-1.0, -0.2), (-0.91, -0.86), (CAB_FLOOR + 0.1, WIN_Z0 - 0.05), WORN)
    for y in (-0.55, 0.55):                                                         # roof work lights
        B(bm, (0.12, 0.30), (y - 0.12, y + 0.12), (ROOF_Z, ROOF_Z + 0.10), GLASS)
    # seat + controls (inside)
    B(bm, (-1.15, -0.70), (-0.25, 0.25), (CAB_FLOOR, CAB_FLOOR + 0.30), CHAR)
    B(bm, (-1.20, -1.12), (-0.25, 0.25), (CAB_FLOOR + 0.30, CAB_FLOOR + 0.90), CHAR)
    BS(bm, (-0.55, -0.50), (0.25, 0.30), (CAB_FLOOR, CAB_FLOOR + 0.55), STEEL)
    # --- left access: steps, walkway over the track, grab rail
    B(bm, (-1.00, -0.10), (0.90, 2.55), (1.77, CAB_FLOOR), YEL)                     # walkway plate
    for z in (0.52, 1.14):
        B(bm, (-1.00, -0.30), (2.10, 2.55), (z, z + 0.05), STEEL)
    for x in (-0.98, -0.32):
        B(bm, (x - 0.025, x + 0.025), (2.50, 2.55), (0.50, 2.55), STEEL)
    B(bm, (-1.0, -0.30), (2.50, 2.55), (2.50, 2.55), STEEL)
    # --- track frames, rollers, carrier rollers, trunnion pads
    for s in (-1, 1):
        yc = s * TRACK_Y
        B(bm, (-1.50, 1.70), (yc - 0.17, yc + 0.17), (0.42, 0.80), CHAR)            # frame beam
        B(bm, (-0.2, 0.55), (yc - 0.21, yc + 0.21), (0.80, 0.90), STEEL)            # pivot housing
        for i in range(7):                                                          # track rollers
            add_cyl(bm, (-1.35 + i * 0.45, yc, 0.14 + 0.13), 0.13, 0.46, "y", 8, STEEL)
        for x in (-0.60, 0.65):                                                     # carrier rollers on posts
            zc = upper_z(TRACK_INNER, x) - 0.09
            B(bm, (x - 0.05, x + 0.05), (yc - 0.05, yc + 0.05), (0.88, zc), CHAR)
            add_cyl(bm, (x, yc, zc), 0.09, 0.30, "y", 8, STEEL)
        # frame cross links to the hull, final-drive housing at the sprocket, idler yoke
        B(bm, (-2.15, -1.55), (s * TRACK_IN, s * HULL_HW), (0.60, 1.30), CHAR)
        B(bm, (SPR_C[0] - 0.12, SPR_C[0] + 0.12), (s * TRACK_IN + 0.01 * s, s * (TRACK_IN + 0.05)), (SPR_C[1] - 0.2, SPR_C[1] + 0.2), CHAR)
        B(bm, (1.55, 1.75), (yc - 0.17, yc + 0.17), (IDL_C[1] - 0.08, IDL_C[1] + 0.08), CHAR)
        # trunnion pad outside the track (pin sits at y 1.96..2.22)
        B(bm, (PIV[0] - 0.22, PIV[0] + 0.22), (s * (TRACK_OUT + 0.005), s * (ARM_Y0 - 0.005)), (PIV[1] - 0.22, PIV[1] + 0.22), YEL)
        # lift-ram chassis clevis
        for dy in (0.89, 1.21):         # clevis plates (y 0.87..0.91 and 1.19..1.23)
            B(bm, (2.14, 2.46), (s * (dy - 0.02), s * (dy + 0.02)), (1.30, 1.78), YEL)
        add_cyl(bm, (CH_PIN[0], s * CH_PIN[1], CH_PIN[2]), 0.05, 0.36, "y", 8, STEEL)
    # paint chips
    B(bm, (0.5, 1.4), (HULL_HW, HULL_HW + 0.012), (1.0, 1.25), WORN)
    B(bm, (-1.8, -0.4), (-HULL_HW - 0.012, -HULL_HW), (1.0, 1.25), WORN)
    return finish("DZ_Chassis", bm)


# ---------------------------------------------------------------- blade (blade + push arms + trash rack, one rigid node)
def build_blade():
    bm = bmesh.new()
    # mouldboard, flat centre: lower wear strip + upper plate with a forward top lip
    add_prism_xz(bm, [(4.30, 0.30), (FACE_X, 0.30), (FACE_X, 0.90), (4.30, 0.90)], -FLAT_HW, FLAT_HW, WORN)
    add_prism_xz(bm, [(4.30, 0.90), (FACE_X, 0.90), (FACE_X, 1.72), (4.47, FACE_TOP), (4.30, FACE_TOP)],
                 -FLAT_HW, FLAT_HW, YEL)
    # wrapped ends (slight forward wrap)
    for s in (-1, 1):
        wp = [(4.30, FLAT_HW), (FACE_X, FLAT_HW), (FACE_X + WRAP, BLADE_HW), (4.30, BLADE_HW)]
        wp = [(x, s * y) for x, y in wp]
        add_prism_xy(bm, wp, 0.30, FACE_TOP, YEL)
        ep = [(4.30, FLAT_HW), (4.42, FLAT_HW), (4.42 + WRAP, BLADE_HW), (4.30, BLADE_HW)]
        add_prism_xy(bm, [(x, s * y) for x, y in ep], 0.0, 0.30, CHAR)
        # wrap end bolt-on segment
        def face(y):
            return 4.42 + WRAP / (BLADE_HW - FLAT_HW) * (y - FLAT_HW)
        y0, y1 = FLAT_HW + 0.03, BLADE_HW - 0.03
        sp = [(face(y0), y0), (face(y0) + 0.04, y0), (face(y1) + 0.04, y1), (face(y1), y1)]
        add_prism_xy(bm, [(x, s * y) for x, y in sp], 0.0, 0.24, STEEL)
    # cutting edge plate (flat part) and 5 bolt-on segments
    add_box(bm, (4.30, -FLAT_HW, 0.0), (4.42, FLAT_HW, 0.30), CHAR)
    seg_w = (2 * FLAT_HW - 0.06) / 5
    for k in range(5):
        ya = -FLAT_HW + 0.03 + k * seg_w
        add_box(bm, (4.42, ya + 0.015, 0.0), (EDGE_FRONT, ya + seg_w - 0.015, 0.24), STEEL)
        for f in (0.2, 0.5, 0.8):                                                   # bolt heads
            yb = ya + seg_w * f
            add_box(bm, (EDGE_FRONT, yb - 0.025, 0.14), (EDGE_FRONT + 0.02, yb + 0.025, 0.19), CHAR)
    # top lip rail
    add_box(bm, (4.28, -BLADE_HW, 1.84), (4.48, BLADE_HW, FACE_TOP), YEL)
    # back structure: top girder, mid girder, cross beam, ribs
    add_box(bm, (3.95, -2.00, 1.70), (4.30, 2.00, 1.84), YEL)
    add_box(bm, (4.00, -2.00, 1.05), (4.30, 2.00, 1.25), YEL)
    add_box(bm, (3.90, -2.00, 0.55), (4.30, 2.00, 1.00), CHAR)
    for y in (-1.55, -0.60, 0.0, 0.60, 1.55):
        add_box(bm, (4.18, y - 0.05, 0.30), (4.30, y + 0.05, 1.70), YEL)
    # lift-ram clevis on the back + blade pins
    for s in (-1, 1):
        for dy in (0.87, 1.19):         # clevis plates, 0.28 m gap around the ram (cap dia 0.18)
            add_box(bm, (3.88, min(s * dy, s * (dy + 0.04)), 1.28), (4.30, max(s * dy, s * (dy + 0.04)), 1.62), YEL)
        add_cyl(bm, (BL_PIN[0], s * BL_PIN[1], BL_PIN[2]), 0.05, 0.36, "y", 8, STEEL)
    # push arms (C-frame arms outside the tracks) + trunnion bosses
    arm = [(1.78, 0.50), (2.40, 0.42), (3.60, 0.46), (4.30, 0.46), (4.30, 1.10), (3.60, 1.12), (2.40, 1.18), (1.78, 1.10)]
    for s in (-1, 1):
        y0, y1 = sorted((s * ARM_Y0, s * ARM_Y1))
        add_prism_xz(bm, arm, y0, y1, YEL)
        add_cyl(bm, (PIV[0], s * 2.08, PIV[1]), 0.16, 0.16, "y", 10, STEEL)      # y 2.00..2.16, touches the chassis pad
    # trash rack: vertical bars, 2 horizontal bars, side posts, back braces
    for i in range(14):
        y = -1.95 + 0.30 * i
        add_box(bm, (4.28, y - 0.035, 1.84), (4.36, y + 0.035, RACK_TOP), STEEL)
    for z in (2.30, RACK_TOP - 0.10):
        add_box(bm, (4.26, -2.13, z), (4.40, 2.13, z + 0.10), STEEL)
    for s in (-1, 1):
        add_box(bm, (4.24, s * 2.08 - 0.06, 1.84), (4.40, s * 2.08 + 0.06, RACK_TOP), STEEL)
        for yb in (2.08, 1.30):
            add_tube(bm, (4.31, s * yb, RACK_TOP - 0.05), (4.02, s * yb, 1.80), 0.045, 6, STEEL)
    return finish("DZ_Blade", bm, loc=(PIV[0], 0, PIV[1]))


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


# ---------------------------------------------------------------- kinematics helpers (design 2D, about the trunnion)
def rot_about_piv(p, deg):
    """Design (x, z) point rotated about the trunnion by `deg` (positive: +x toward +z)."""
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    dx, dz = p[0] - PIV[0], p[1] - PIV[1]
    return (PIV[0] + dx * c - dz * s, PIV[1] + dx * s + dz * c)


def ram_len(deg):
    bx, bz = rot_about_piv((BL_PIN[0], BL_PIN[2]), deg)
    return math.hypot(bx - CH_PIN[0], bz - CH_PIN[2])


def edge_height(deg):
    return rot_about_piv((EDGE_FRONT, 0.0), deg)[1]


# ---------------------------------------------------------------- main
def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    make_materials()
    O = bpy.data.objects

    root = bpy.data.objects.new("LandfillDozer_Root", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 1.0
    sc.collection.objects.link(root)
    ORIGIN["LandfillDozer_Root"] = Vector((0, 0, 0))

    chassis = build_chassis()
    parent(chassis, root)
    blade = build_blade()
    parent(blade, root)

    for s, sfx in ((1, "L"), (-1, "R")):
        for nm, fn in (("DZ_Track_", build_track), ("DZ_Sprocket_", build_sprocket), ("DZ_Idler_", build_idler)):
            parent(fn(nm + sfx, s), chassis)

    # anchors
    empty("Anchor_BladeEdge", (EDGE_FRONT, 0, 0), blade, 0.15)
    empty("Anchor_Seat", (-0.85, 0, 2.95), chassis, 0.2)
    empty("Anchor_Door", (-0.65, 2.75, 0), chassis, 0.3)
    empty("Anchor_Exit_L", (0.0, 2.85, 0), chassis, 0.3)
    empty("Anchor_Exit_R", (0.0, -2.85, 0), chassis, 0.3)
    empty("Anchor_CameraFocus", (EDGE_FRONT + 4.0, 0, 0), chassis, 0.3)

    # colliders (design world bounds)
    collider("Col_Chassis", chassis, (-2.15, -HULL_HW, 0.45), (2.56, HULL_HW, 1.30))
    collider("Col_Hood", chassis, (0.35, -0.85, 1.30), (2.33, 0.85, HOOD_TOP))
    collider("Col_Rear", chassis, (-3.55, -1.15, 0.75), (-2.15, 1.15, 1.95))
    collider("Col_Cab", chassis, (-1.70, -1.02, 1.30), (0.30, 1.02, ROOF_Z))
    for s, sfx in ((1, "L"), (-1, "R")):
        collider("Col_Track_" + sfx, chassis, (SPR_C[0] - SPR_R, min(s * TRACK_IN, s * TRACK_OUT), 0.0),
                 (IDL_C[0] + IDL_R, max(s * TRACK_IN, s * TRACK_OUT), SPR_C[1] + SPR_R + 0.02))
    collider("Col_Blade_Face", blade, (4.28, -BLADE_HW, 0.30), (4.42, BLADE_HW, FACE_TOP))
    collider("Col_Blade_Rack", blade, (4.26, -BLADE_HW, FACE_TOP), (4.40, BLADE_HW, RACK_TOP))
    collider("Col_Blade_Edge", blade, (4.42, -BLADE_HW, 0.0), (4.50, BLADE_HW, 0.14))
    for s, sfx in ((1, "L"), (-1, "R")):
        collider("Col_Arm_" + sfx, blade, (1.78, min(s * ARM_Y0, s * ARM_Y1), 0.46),
                 (4.30, max(s * ARM_Y0, s * ARM_Y1), 1.12))
    update()

    # lift rams: barrel on the chassis (origin = chassis pin), rod on the blade (origin = blade pin)
    lmax = max(ram_len(a * MAX_LIFT / 76) for a in range(77))
    lrest = ram_len(0)
    frac = 0.62 * lmax / lrest
    for s, sfx in ((1, "L"), (-1, "R")):
        base_w = Vector(R((CH_PIN[0], s * CH_PIN[1], CH_PIN[2])))
        end_w = Vector(R((BL_PIN[0], s * BL_PIN[1], BL_PIN[2])))
        build_ram_part("DZ_LiftRam_" + sfx, base_w, end_w, chassis, 0.07, frac, 0.09)
        build_ram_part("DZ_LiftRamRod_" + sfx, end_w, base_w, blade, 0.045, frac, 0.07)
    # the rod halves share the blade as parent; their orientation at rest points back at the barrel pin
    for ob in O:
        ob.select_set(False)
    update()

    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(FBX_DIR, exist_ok=True)
    for ob in O:
        assert all(abs(v - 1) < 1e-9 for v in ob.scale), ob.name
        assert all(abs(v) < 1e-9 for v in ob.rotation_euler) or ob.name.startswith("Col_"), ob.name
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    for ob in O:
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
    names = {ob.name: (ob.parent.name if ob.parent else None, [m.name for m in ob.data.materials] if ob.type == "MESH" else [])
             for ob in O}
    verify(root, chassis, blade, lmax, lrest, frac)
    reimport_check(names)


# ---------------------------------------------------------------- numeric verification
def world_bvh(ob):
    me = ob.data
    vs = [ob.matrix_world @ v.co for v in me.vertices]
    return BVHTree.FromPolygons(vs, [tuple(p.vertices) for p in me.polygons])


def verts_world(ob):
    return [ob.matrix_world @ v.co for v in ob.data.vertices]


def dz(v):
    return (round(v.y, 3), round(-v.x, 3), round(v.z, 3))


REST_DIRS = {}


def pose_blade(O, deg):
    """Lift the blade and point barrel and rod halves at each other's pins (what Unity does each frame)."""
    blade = O["DZ_Blade"]
    blade.rotation_euler = Euler((math.radians(deg), 0, 0))
    update()
    for sfx in ("L", "R"):
        bar, rod = O["DZ_LiftRam_" + sfx], O["DZ_LiftRamRod_" + sfx]
        bar.rotation_mode = rod.rotation_mode = "QUATERNION"
        bar.rotation_quaternion = (1, 0, 0, 0)
        rod.rotation_quaternion = (1, 0, 0, 0)
        update()
        bar_w, rod_w = bar.matrix_world.translation.copy(), rod.matrix_world.translation.copy()
        rest_b, rest_r = REST_DIRS[sfx]
        bar.rotation_quaternion = rest_b.rotation_difference(rod_w - bar_w)
        local = blade.matrix_world.to_3x3().inverted() @ (bar_w - rod_w)
        rod.rotation_quaternion = rest_r.rotation_difference(local)
        update()


def verify(root, chassis, blade, lmax, lrest, frac):
    O = bpy.data.objects
    tris = sum(tri_count(ob) for ob in O if ob.type == "MESH")
    vis = sum(tri_count(ob) for ob in O if ob.type == "MESH" and not ob.name.startswith("Col_"))
    print("TRIS total", tris, "visible", vis)
    for ob in sorted(O, key=lambda o: o.name):
        print("OBJ %-22s parent=%-18s world(design)=%s tris=%d" %
              (ob.name, ob.parent.name if ob.parent else "-", dz(ob.matrix_world.translation),
               tri_count(ob) if ob.type == "MESH" else 0))

    def bbox(objs):
        pts = [p for ob in objs for p in verts_world(ob)]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        return lo, hi
    vis_objs = [ob for ob in O if ob.type == "MESH" and not ob.name.startswith("Col_")]
    lo, hi = bbox(vis_objs)
    print("BBOX design: x %.3f..%.3f (len %.3f)  y %.3f..%.3f (w %.3f)  z %.3f..%.3f" %
          (lo.y, hi.y, hi.y - lo.y, -hi.x, -lo.x, hi.x - lo.x, lo.z, hi.z))
    tl, th = bbox([O["DZ_Track_L"], O["DZ_Track_R"]])
    print("TRACKS: over-track width %.3f (y +-%.3f)  length x %.3f..%.3f (%.3f)  lowest z %.4f  top z %.3f" %
          (th.x - tl.x, th.x, tl.y, th.y, th.y - tl.y, tl.z, th.z))
    cl, ch = bbox([chassis])
    print("CHASSIS z %.3f..%.3f  cab roof z %.3f  exhaust top z %.3f" % (cl.z, ch.z, ROOF_Z + 0.10, 3.64))
    print("CONTACT: flat ground contact length = idler-bottom - heel = %.3f m" % (IDL_C[0] - HEEL_X))
    bl, bh = bbox([blade])
    print("BLADE bbox design: x %.3f..%.3f  y %.3f..%.3f (w %.3f)  z %.4f..%.3f" %
          (bl.y, bh.y, -bh.x, -bl.x, bh.x - bl.x, bl.z, bh.z))
    print("BLADE face plane x=%.3f (flat centre |y|<%.2f); wrap to x=%.3f at |y|=%.2f; face height %.2f; rack top z %.3f"
          % (FACE_X, FLAT_HW, FACE_X + WRAP, BLADE_HW, FACE_TOP, RACK_TOP))
    # --- blade solid check
    bm = bmesh.new()
    bm.from_mesh(blade.data)
    bm.verts.ensure_lookup_table()
    print("BLADE signed volume %.4f (positive = outward normals)" % bm.calc_volume(signed=True))
    seen, islands = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, isl = [f], []
        seen.add(f)
        while stack:
            cur = stack.pop()
            isl.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf not in seen:
                        seen.add(nf)
                        stack.append(nf)
        islands.append(isl)
    badisl = 0
    for isl in islands:
        v = 0.0
        for fc in isl:
            vs = [x.co for x in fc.verts]
            v += vs[0].dot(vs[1].cross(vs[2])) / 6.0
        if v <= 0:
            badisl += 1
    print("BLADE loose islands %d, islands with non-positive volume %d" % (len(islands), badisl))
    bm.free()
    bvh_blade = world_bvh(blade)
    random.seed(7)
    good = bad = 0
    centre = Vector((0, 4.2, 1.3))
    for _ in range(600):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1)))
        if d.length < 0.2:
            continue
        d.normalize()
        origin = centre + d * 12
        # aim at a random point inside the blade box to hit many parts
        tgt = Vector((random.uniform(-2.1, 2.1) * -1, random.uniform(3.95, 4.6), random.uniform(0.0, 2.9)))
        dirv = (tgt - origin).normalized()
        hit = bvh_blade.ray_cast(origin, dirv)
        if hit[0] is not None:
            if hit[1].dot(dirv) < 0:
                good += 1
            else:
                bad += 1
    print("BLADE ray test (first hit normal faces the ray origin): good=%d bad=%d" % (good, bad))

    # --- rest directions for the rams, then pose sweep
    update()
    for sfx in ("L", "R"):
        bar, rod = O["DZ_LiftRam_" + sfx], O["DZ_LiftRamRod_" + sfx]
        bar_w, rod_w = bar.matrix_world.translation.copy(), rod.matrix_world.translation.copy()
        REST_DIRS[sfx] = ((rod_w - bar_w).normalized(), (bar_w - rod_w).normalized())
    static = {n: world_bvh(O[n]) for n in ("DZ_Chassis", "DZ_Track_L", "DZ_Track_R", "DZ_Sprocket_L", "DZ_Sprocket_R",
                                          "DZ_Idler_L", "DZ_Idler_R")}
    print("---- lift sweep: DZ_Blade rotation_euler.x = lift (positive = blade tip UP)")
    hits_any = {}
    ram_min_clear = 9.0
    ram_sample_clear = 9.0
    halves_ok = True
    lens = []
    for i in range(0, 77):
        deg = i * MAX_LIFT / 76
        pose_blade(O, deg)
        bvh_b = world_bvh(blade)
        for n, bv in static.items():
            if bvh_b.overlap(bv):
                hits_any.setdefault(n, []).append(round(deg, 1))
        zmin = min(p.z for p in verts_world(blade))
        ram_min_clear = min(ram_min_clear, zmin)
        for sfx in ("L", "R"):
            bar, rod = O["DZ_LiftRam_" + sfx], O["DZ_LiftRamRod_" + sfx]
            a, b = bar.matrix_world.translation, rod.matrix_world.translation
            L = (b - a).length
            lens.append(L)
            hb = frac * lrest
            if 2 * hb < L + 0.3:
                halves_ok = False
            # samples along the pin-to-pin line, away from the clevises
            for k in range(1, 20):
                t = 0.15 / L + (1 - 0.30 / L) * k / 19
                p = a + (b - a) * t
                for n in ("DZ_Chassis", "DZ_Track_L", "DZ_Track_R"):
                    r = static[n].find_nearest(p)
                    if r[0] is not None:
                        ram_sample_clear = min(ram_sample_clear, r[3] - 0.07)
                r = bvh_b.find_nearest(p)
                if r[0] is not None:
                    ram_sample_clear = min(ram_sample_clear, r[3] - 0.07)
    print("BLADE lowest z over the sweep: %.5f (>= 0 means never below ground)" % ram_min_clear)
    print("BLADE-vs-static mesh overlaps over 0..%.0f deg (76 poses): %s" % (MAX_LIFT, hits_any or "none"))
    print("RAM axis clearance to chassis/tracks/blade (axis dist - 0.07 radius, clevis ends excluded): %.3f m" % ram_sample_clear)
    print("RAM length rest %.3f  min %.3f  max %.3f  half length each %.3f (0.62 x longest %.3f); halves overlap at every pose: %s"
          % (lrest, min(lens), max(lens), frac * lrest, lmax, halves_ok))
    # arms / rack vs hood, cab, exhaust, stack are all in the DZ_Chassis BVH above
    for deg in (0, 10, 20, 30, MAX_LIFT):
        pose_blade(O, deg)
        e = O["Anchor_BladeEdge"].matrix_world.translation
        print("POSE lift=%5.1f deg  edge(design)=%s  analytic edge z %.3f  rack top-front z %.3f  ram len %.3f"
              % (deg, dz(e), edge_height(deg), rot_about_piv((4.36, RACK_TOP), deg)[1], ram_len(deg)))
    print("EDGE HEIGHT at max lift %.3f m (lift %.1f deg)" % (edge_height(MAX_LIFT), MAX_LIFT))
    pose_blade(O, 0)

    # --- sprocket direction: which sign of rotation_euler.x drives the machine forward
    spr = O["DZ_Sprocket_L"]
    res = {}
    for sign in (+1, -1):
        spr.rotation_euler = Euler((math.radians(sign * 10), 0, 0))
        update()
        # a tooth point at the sprocket bottom (design): centre + (0, 0, -0.46)
        local = Vector(R((0.0, 0.0, -0.46)))
        p0 = spr.matrix_world.translation + local
        p1 = spr.matrix_world @ local
        res[sign] = D(p1 - p0).x
    spr.rotation_euler = Euler((0, 0, 0))
    update()
    print("SPROCKET: bottom tooth moves design x %+.3f for +10 deg, %+.3f for -10 deg. Track bottom runs BACKWARD when the "
          "machine drives forward, so forward = %s rotation_euler.x" %
          (res[1], res[-1], "NEGATIVE" if res[-1] < 0 else "POSITIVE"))
    print("SPROCKET tip radius 0.46 (pitch ~0.40), hull radius %.2f, axle (design) %s; IDLER axle %s radius 0.50" %
          (SPR_R, dz(O["DZ_Sprocket_L"].matrix_world.translation), dz(O["DZ_Idler_L"].matrix_world.translation)))
    grz = [p.z for ob in (O["DZ_Track_L"], O["DZ_Track_R"]) for p in verts_world(ob)]
    print("TRACK lowest vertex z %.4f" % min(grz))
    print("WALKWAY check: track outer top at x=-1.0 is z %.3f (walkway bottom 1.77)" % upper_z(TRACK_OUTER, -1.0))


def reimport_check(names):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX_PATH, axis_forward="-Z", axis_up="Y")
    got = {ob.name: (ob.parent.name if ob.parent else None, [m.name for m in ob.data.materials] if ob.type == "MESH" else [])
           for ob in bpy.data.objects}
    miss = sorted(set(names) - set(got))
    extra = sorted(set(got) - set(names))
    par_bad = [n for n in names if n in got and names[n][0] != got[n][0]]
    mat_bad = [n for n in names if n in got and not n.startswith("Col_") and [m.split(".")[0] for m in got[n][1]] != names[n][1]]
    print("FBX REIMPORT: %d objects (expected %d); missing %s extra %s parent mismatches %s material mismatches %s" %
          (len(got), len(names), miss, extra, par_bad, mat_bad))


main()
