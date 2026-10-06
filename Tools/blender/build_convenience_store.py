"""Author the convenience-store lot demo level and export it for the Destruction Lab.

Run headless from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/build_convenience_store.py

Writes Tools/blender/ConvenienceStore/ConvenienceStore.blend and
DestructionPOC/Assets/Destruction/Models/ConvenienceStore.fbx.

Layout (Blender Z-up, metres). Lot x in [-25, 25], y in [-20, 20]; the street is the south (-y) edge.
  * Store 12 x 8 m at x -16..-4, y 10..18, storefront facing south, 4 m walls, flat roof with parapet.
  * Storefront glass sits between mullions on a brick bulkhead under a header; the door is a gap with a lintel.
  * Awning slab over the sidewalk in front of the store, carried by four posts.
  * Pump canopy 12 x 8 m at x 2..14, y -2..6, island with two pumps beneath it.
  * Four cars, four light poles, pylon sign, dumpster enclosure, bins, bollards, curbs, boundary wall and fence.

Only three destruction materials exist (concrete, wood, brick). Glass, vehicles, signs, poles and bins
therefore use __wood (light and weak); the render materials are assigned from name prefixes after export.
Everything is an axis-aligned box. Pieces touch, never overlap.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "ConvenienceStore")
BLEND_PATH = os.path.join(OUT_DIR, "ConvenienceStore.blend")
FBX_PATH = os.path.join(REPO, "DestructionPOC", "Assets", "Destruction", "Models", "ConvenienceStore.fbx")

PIECES = []
COLL = {}


def coll(name):
    if name not in COLL:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
        COLL[name] = c
    return COLL[name]


def box(group, name, x0, x1, y0, y1, z0, z1):
    cx, cy, cz = (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2
    sx, sy, sz = x1 - x0, y1 - y0, z1 - z0
    assert sx > 0.02 and sy > 0.02 and sz > 0.02, name
    h = (sx / 2, sy / 2, sz / 2)
    verts = [(a * h[0], b * h[1], c * h[2]) for a in (-1, 1) for b in (-1, 1) for c in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = (cx, cy, cz)
    coll(group).objects.link(obj)
    PIECES.append(obj)
    return obj


def splits(lo, hi, n):
    step = (hi - lo) / n
    return [(lo + i * step, lo + (i + 1) * step) for i in range(n)]


def build_store():
    G = "Store"
    X0, X1, Y0, Y1 = -16.0, -4.0, 10.0, 18.0
    T, P, H = 0.3, 0.5, 4.0
    ix0, ix1 = X0 + P, X1 - P
    iy0, iy1 = Y0 + P, Y1 - P
    # corner piers
    for ex, xs in (("W", (X0, ix0)), ("E", (ix1, X1))):
        for fy, ys in (("S", (Y0, iy0)), ("N", (iy1, Y1))):
            box(G, f"Pier_{ex}{fy}__concrete", xs[0], xs[1], ys[0], ys[1], 0, H)

    # storefront (south): bulkhead, glass between mullions, header, door gap with lintel
    dx0, dx1 = -11.0, -9.0
    for tag, (bx0, bx1) in (("L", (ix0, dx0)), ("R", (dx1, ix1))):
        for k, (a, b) in enumerate(splits(bx0, bx1, 3)):
            box(G, f"Wall_Bulkhead_{tag}{k}__brick", a, b, Y0, Y0 + T, 0, 0.8)
            ga = a if k == 0 else a + 0.15
            gb = b if k == 2 else b - 0.15
            box(G, f"Glass_Front_{tag}{k}__wood", ga, gb, Y0 + 0.1, Y0 + 0.18, 0.8, 3.0)
        for k in (1, 2):
            m = bx0 + (bx1 - bx0) * k / 3
            box(G, f"Post_Mullion_{tag}{k}__concrete", m - 0.15, m + 0.15, Y0, Y0 + T, 0.8, 3.0)
        for k, (a, b) in enumerate(splits(bx0, bx1, 2)):
            box(G, f"Wall_Header_{tag}{k}__brick", a, b, Y0, Y0 + T, 3.0, H)
    box(G, "Lintel_Door_S__concrete", dx0, dx1, Y0, Y0 + T, 2.6, H)

    # back (north) wall with service door
    for tag, (a0, a1) in (("L", (ix0, dx0)), ("R", (dx1, ix1))):
        for k, (a, b) in enumerate(splits(a0, a1, 2)):
            box(G, f"Wall_Back_{tag}{k}__brick", a, b, Y1 - T, Y1, 0, H)
    box(G, "Lintel_Door_N__concrete", dx0, dx1, Y1 - T, Y1, 2.6, H)

    # west wall with a window, east wall solid
    for k, (a, b) in enumerate(((iy0, 12.5), (14.5, 16.0), (16.0, iy1))):
        box(G, f"Wall_W{k}__brick", X0, X0 + T, a, b, 0, H)
    box(G, "Sill_W__brick", X0, X0 + T, 12.5, 14.5, 0, 1.0)
    box(G, "Lintel_W__concrete", X0, X0 + T, 12.5, 14.5, 2.6, H)
    for k, (a, b) in enumerate(splits(iy0, iy1, 3)):
        box(G, f"Wall_E{k}__brick", X1 - T, X1, a, b, 0, H)

    # roof and parapet
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        for j, (ya, yb) in enumerate(splits(Y0, Y1, 3)):
            box(G, f"Roof_{i}{j}__concrete", xa, xb, ya, yb, H, H + 0.3)
    zt = H + 0.3
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        box(G, f"Parapet_S{i}__brick", xa, xb, Y0, Y0 + T, zt, zt + 0.5)
        box(G, f"Parapet_N{i}__brick", xa, xb, Y1 - T, Y1, zt, zt + 0.5)
    for j, (ya, yb) in enumerate(splits(Y0 + T, Y1 - T, 3)):
        box(G, f"Parapet_W{j}__brick", X0, X0 + T, ya, yb, zt, zt + 0.5)
        box(G, f"Parapet_E{j}__brick", X1 - T, X1, ya, yb, zt, zt + 0.5)

    # sidewalk and awning
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        box(G, f"Slab_Sidewalk{i}__concrete", xa, xb, 6.0, Y0, 0, 0.15)
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        box(G, f"Awning_{i}__concrete", xa, xb, 7.0, Y0, 3.4, 3.7)
        cx = (xa + xb) / 2
        box(G, f"Post_Awning{i}__concrete", cx - 0.15, cx + 0.15, 7.0, 7.3, 0.15, 3.4)
        box(G, f"Sign_StoreBand{i}__wood", xa, xb, 7.0, 7.2, 3.7, 4.3)

    # interior
    for r, y in enumerate((13.0, 15.0)):
        for c, x in enumerate((-14.0, -9.0)):
            box(G, f"Shelf_{r}{c}__wood", x, x + 3.0, y, y + 0.5, 0, 1.8)
    box(G, "Counter__wood", -8.6, -5.6, 11.0, 11.8, 0, 1.1)
    box(G, "Register__wood", -7.4, -6.8, 11.2, 11.6, 1.1, 1.4)
    for i, x in enumerate((-15.2, -14.0, -7.4, -6.2)):
        box(G, f"Cooler_{i}__concrete", x, x + 1.2, 16.9, 17.7, 0, 2.1)
    # bollards guarding the awning line, bins at the door
    for i, x in enumerate((-15.0, -12.25, -9.5, -6.75, -4.5)):
        box(G, f"Bollard_Store{i}__concrete", x - 0.125, x + 0.125, 6.2, 6.45, 0.15, 1.15)
    for i, x in enumerate((-12.8, -7.2)):
        box(G, f"Bin_Store{i}__wood", x - 0.275, x + 0.275, 9.0, 9.55, 0.15, 1.05)


def build_pumps():
    G = "Pumps"
    for i, (xa, xb) in enumerate(splits(3.0, 13.0, 3)):
        box(G, f"Slab_PumpIsland{i}__concrete", xa, xb, 1.0, 3.0, 0, 0.2)
    for i, x in enumerate((6.0, 10.0)):
        box(G, f"Pump_Body{i}__concrete", x - 0.45, x + 0.45, 1.7, 2.3, 0.2, 1.8)
        box(G, f"Pump_Hood{i}__concrete", x - 0.5, x + 0.5, 1.65, 2.35, 1.8, 2.05)
    for i, (x, y) in enumerate(((3.3, 1.3), (12.7, 1.3), (3.3, 2.7), (12.7, 2.7))):
        box(G, f"Bollard_Pump{i}__concrete", x - 0.125, x + 0.125, y - 0.125, y + 0.125, 0.2, 1.1)
    # canopy
    zc = 5.0
    for i, (xa, xb) in enumerate(splits(2.0, 14.0, 3)):
        for j, (ya, yb) in enumerate(splits(-2.0, 6.0, 2)):
            box(G, f"Slab_PumpCanopy{i}{j}__concrete", xa, xb, ya, yb, zc, zc + 0.4)
    for i, (x, y) in enumerate(((3.0, -1.2), (13.0, -1.2), (3.0, 5.2), (13.0, 5.2))):
        box(G, f"Column_Canopy{i}__concrete", x - 0.25, x + 0.25, y - 0.25, y + 0.25, 0, zc)
    zf = zc + 0.4
    for i, (xa, xb) in enumerate(splits(2.0, 14.0, 3)):
        box(G, f"Fascia_S{i}__wood", xa, xb, -2.0, -1.8, zf, zf + 0.6)
        box(G, f"Fascia_N{i}__wood", xa, xb, 5.8, 6.0, zf, zf + 0.6)
    for j, (ya, yb) in enumerate(splits(-1.8, 5.8, 2)):
        box(G, f"Fascia_W{j}__wood", 2.0, 2.2, ya, yb, zf, zf + 0.6)
        box(G, f"Fascia_E{j}__wood", 13.8, 14.0, ya, yb, zf, zf + 0.6)


def car(name, cx, cy, axis):
    G = "Cars"
    L, W = 4.2, 1.75

    def cb(sub, u0, u1, v0, v1, z0, z1):
        if axis == "x":
            box(G, f"{name}_{sub}__wood", cx + u0, cx + u1, cy + v0, cy + v1, z0, z1)
        else:
            box(G, f"{name}_{sub}__wood", cx + v0, cx + v1, cy + u0, cy + u1, z0, z1)

    cb("Body", -L / 2, L / 2, -W / 2, W / 2, 0.4, 0.95)
    cb("Cabin", -1.3, 0.7, -0.775, 0.775, 0.95, 1.5)
    for i, (u, v) in enumerate(((-1.4, -1), (-1.4, 1), (1.4, -1), (1.4, 1))):
        v0, v1 = (-W / 2, -W / 2 + 0.25) if v < 0 else (W / 2 - 0.25, W / 2)
        cb(f"Wheel{i}", u - 0.35, u + 0.35, v0, v1, 0, 0.4)


def build_cars():
    car("Car_A", -14.0, 3.0, "y")
    car("Car_B", -8.0, 3.5, "y")
    car("Car_Pump", 8.0, -0.4, "x")
    car("Car_C", 20.0, 6.0, "y")
    for i, x in enumerate((-14.0, -11.0, -8.0, -5.0)):
        box("Cars", f"Slab_WheelStop{i}__concrete", x - 0.9, x + 0.9, 5.4, 5.65, 0, 0.12)


def build_site():
    G = "Site"
    # light poles
    for i, (x, y, d) in enumerate(((-20, 2, 1), (-1, 6, 1), (20, 2, -1), (-1, -12, 1))):
        box(G, f"Slab_LightBase{i}__concrete", x - 0.4, x + 0.4, y - 0.4, y + 0.4, 0, 0.5)
        box(G, f"Post_Light{i}__wood", x - 0.125, x + 0.125, y - 0.125, y + 0.125, 0.5, 8.0)
        a, b = sorted((x - d * 0.125, x + d * 1.6))
        box(G, f"Beam_LightArm{i}__wood", a, b, y - 0.1, y + 0.1, 8.0, 8.2)
        a, b = sorted((x + d * 1.0, x + d * 1.6))
        box(G, f"Light_Head{i}__wood", a, b, y - 0.2, y + 0.2, 7.8, 8.0)

    # pylon sign
    px, py = -20.0, -16.0
    box(G, "Slab_PylonFooting__concrete", px - 1.3, px + 1.3, py - 0.45, py + 0.45, 0, 0.5)
    for i, s in enumerate((-1, 1)):
        a, b = sorted((px + s * 0.8, px + s * 1.2))
        box(G, f"Post_Pylon{i}__concrete", a, b, py - 0.2, py + 0.2, 0.5, 5.5)
    box(G, "Sign_PylonPrice__wood", px - 0.8, px + 0.8, py - 0.2, py + 0.2, 2.5, 5.5)
    box(G, "Sign_PylonCabinet__wood", px - 1.2, px + 1.2, py - 0.3, py + 0.3, 5.5, 7.8)

    # dumpster enclosure at x -23..-19, y 13.5..17
    for k, (a, b) in enumerate(splits(-23.0, -19.0, 2)):
        box(G, f"Wall_EnclosureBack{k}__brick", a, b, 16.75, 17.0, 0, 1.8)
    for k, (a, b) in enumerate(splits(13.8, 16.75, 2)):
        box(G, f"Wall_EnclosureW{k}__brick", -23.0, -22.75, a, b, 0, 1.8)
        box(G, f"Wall_EnclosureE{k}__brick", -19.25, -19.0, a, b, 0, 1.8)
    box(G, "Post_GateW__concrete", -23.0, -22.7, 13.5, 13.8, 0, 2.0)
    box(G, "Post_GateE__concrete", -19.3, -19.0, 13.5, 13.8, 0, 2.0)
    box(G, "Gate_Leaf0__wood", -22.7, -21.0, 13.5, 13.6, 0, 1.8)
    box(G, "Gate_Leaf1__wood", -21.0, -19.3, 13.5, 13.6, 0, 1.8)
    box(G, "Dumpster_Body__wood", -22.4, -20.2, 14.6, 16.4, 0, 1.2)
    box(G, "Dumpster_Lid__wood", -22.4, -20.2, 14.6, 16.4, 1.2, 1.3)

    # loose bins
    for i, (x, y) in enumerate(((1.4, 2.0), (16.0, 2.0))):
        box(G, f"Bin_Lot{i}__wood", x - 0.275, x + 0.275, y - 0.275, y + 0.275, 0, 0.9)

    # south curb with two driveway gaps
    n = 0
    for a, b, c in ((-24.75, -14.0, 4), (-6.0, 10.0, 6), (18.0, 24.88, 3)):
        for xa, xb in splits(a, b, c):
            box(G, f"Slab_Curb{n}__concrete", xa, xb, -20.0, -19.8, 0, 0.15)
            n += 1

    # boundary: brick wall north and west, timber fence east
    for i, (xa, xb) in enumerate(splits(-25.0, 25.0, 10)):
        box(G, f"Wall_BoundaryN{i}__brick", xa, xb, 19.75, 20.0, 0, 1.8)
    for i, (ya, yb) in enumerate(splits(-20.0, 19.75, 8)):
        box(G, f"Wall_BoundaryW{i}__brick", -25.0, -24.75, ya, yb, 0, 1.8)
    for i, (ya, yb) in enumerate(splits(-20.0, 19.75, 10)):
        box(G, f"Panel_FenceE{i}__wood", 24.88, 25.0, ya, yb, 0, 1.8)


def check_no_overlaps(tol=1e-4):
    boxes = [(p.name, p.location, p.dimensions) for p in PIECES]
    bad = []
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            (na, ca, sa), (nb, cb, sb) = boxes[i], boxes[j]
            ov = [sa[k] / 2 + sb[k] / 2 - abs(ca[k] - cb[k]) for k in range(3)]
            if all(o > tol for o in ov):
                bad.append(f"{na} overlaps {nb} by {min(ov):.3f} m")
    if bad:
        raise SystemExit("[store] pieces interpenetrate:\n  " + "\n  ".join(bad[:30]))
    print(f"[store] overlap check passed for {len(boxes)} pieces")


RENDER_MATS = [
    ("Glass_", (0.45, 0.62, 0.7, 1), 0.05, 0.0, 0.6),
    ("Wall_Bulkhead", (0.55, 0.3, 0.24, 1), 0.8, 0, 0),
    ("Sign_Store", (0.85, 0.15, 0.12, 1), 0.5, 0, 0),
    ("Sign_Pylon", (0.9, 0.75, 0.15, 1), 0.5, 0, 0),
    ("Car_A", (0.7, 0.1, 0.1, 1), 0.35, 0.3, 0),
    ("Car_B", (0.15, 0.3, 0.6, 1), 0.35, 0.3, 0),
    ("Car_Pump", (0.85, 0.85, 0.82, 1), 0.35, 0.3, 0),
    ("Car_C", (0.15, 0.4, 0.2, 1), 0.35, 0.3, 0),
    ("Pump_Body", (0.8, 0.12, 0.1, 1), 0.4, 0.2, 0),
    ("Pump_Hood", (0.9, 0.9, 0.9, 1), 0.4, 0.2, 0),
    ("Fascia_", (0.85, 0.15, 0.12, 1), 0.5, 0, 0),
    ("Bollard_", (0.9, 0.75, 0.1, 1), 0.5, 0, 0),
    ("Bin_", (0.15, 0.3, 0.2, 1), 0.6, 0, 0),
    ("Dumpster", (0.1, 0.35, 0.18, 1), 0.6, 0, 0),
    ("Gate_", (0.4, 0.28, 0.18, 1), 0.7, 0, 0),
    ("Light_Head", (0.95, 0.95, 0.8, 1), 0.4, 0, 0),
    ("Post_Light", (0.25, 0.25, 0.27, 1), 0.5, 0.5, 0),
    ("Beam_LightArm", (0.25, 0.25, 0.27, 1), 0.5, 0.5, 0),
    ("Shelf", (0.7, 0.7, 0.72, 1), 0.6, 0, 0),
    ("Counter", (0.6, 0.45, 0.3, 1), 0.6, 0, 0),
    ("Cooler", (0.75, 0.85, 0.9, 1), 0.4, 0.2, 0),
    ("Panel_Fence", (0.5, 0.36, 0.22, 1), 0.8, 0, 0),
    ("Roof", (0.3, 0.3, 0.32, 1), 0.9, 0, 0),
    ("Wall_", (0.62, 0.34, 0.26, 1), 0.85, 0, 0),
    ("Parapet", (0.62, 0.34, 0.26, 1), 0.85, 0, 0),
    ("Wheel", (0.05, 0.05, 0.05, 1), 0.9, 0, 0),
]


def assign_render_materials():
    cache = {}

    def get(key, col, rough, metal, trans):
        if key in cache:
            return cache[key]
        m = bpy.data.materials.new("M_" + key.strip("_"))
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = col
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
        if trans:
            b.inputs["Transmission Weight"].default_value = trans
        m.diffuse_color = col
        cache[key] = m
        return m

    for o in PIECES:
        n = o.name
        spec = None
        for s in RENDER_MATS:
            if n.startswith(s[0]) or (s[0].startswith("Car") is False and s[0] in n and s[0] in ("Wheel",)):
                spec = s
                break
        if spec is None:
            if "__brick" in n:
                spec = ("Brick", (0.62, 0.34, 0.26, 1), 0.85, 0, 0)
            elif "__wood" in n:
                spec = ("Wood", (0.55, 0.4, 0.26, 1), 0.7, 0, 0)
            else:
                spec = ("Concrete", (0.62, 0.62, 0.6, 1), 0.9, 0, 0)
        # car wheels
        if n.startswith("Car") and "_Wheel" in n:
            spec = ("Wheel", (0.05, 0.05, 0.05, 1), 0.9, 0, 0)
        elif n.startswith("Car") and "_Cabin" in n:
            spec = ("Cabin", (0.08, 0.12, 0.16, 1), 0.15, 0.0, 0)
        o.data.materials.append(get(*spec[:1] and (spec[0], spec[1], spec[2], spec[3], spec[4])))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    build_store()
    build_pumps()
    build_cars()
    build_site()
    check_no_overlaps()

    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    for p in PIECES:
        p.select_set(True)
    bpy.context.view_layer.objects.active = PIECES[0]
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        apply_scale_options="FBX_SCALE_NONE", bake_space_transform=False, object_types={"MESH"},
        mesh_smooth_type="FACE", add_leaf_bones=False, axis_forward="-Z", axis_up="Y",
        path_mode="STRIP",
    )
    assign_render_materials()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for p in PIECES:
        for c in p.bound_box:
            w = p.matrix_world @ Vector(c)
            for k in range(3):
                lo[k] = min(lo[k], w[k])
                hi[k] = max(hi[k], w[k])
    print(f"[store] {len(PIECES)} pieces; extent x {hi[0]-lo[0]:.1f} y {hi[1]-lo[1]:.1f} z {hi[2]-lo[2]:.1f}; ground z {lo[2]:.3f}")
    for g, c in COLL.items():
        print(f"[store]   {g}: {len(c.objects)}")


if __name__ == "__main__":
    main()
    sys.exit(0)
