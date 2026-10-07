"""Author the fire-ravaged convenience-store lot: the same store after a fire, fenced off awaiting demolition.

Run headless from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/build_burned_store.py

Writes Tools/blender/BurnedStore/BurnedStore.blend and Tools/blender/BurnedStore/BurnedStore.fbx.
The FBX is not copied into the Unity project yet; nothing references this level.

Starts from the layout in build_convenience_store.py (Blender Z-up, metres, lot x -25..25, y -20..20).
The fire started in the back-of-house at the east end of the store, so damage grows from west to east:
  * Three roof bays over the east half burned through and fell into the store; the parapets they
    carried went with them. The east wall and the east end of the back wall burned down to stubs.
  * Every storefront pane blew out. Plywood boards cover the west openings and the door; the east
    openings are open, black holes. One shard is left in the west-most frame.
  * The east awning bay and its post came down on the sidewalk with the header brick above it.
    The store sign burned off the east half of the band.
  * Inside, an east shelf and a cooler toppled, and the fallen roof slabs lie on them and the floor.
  * The fire spread across the whole lot. The pump canopy lost its north-east bay and the fascia on
    it, a pump hood fell off, the pylon price panel burned away, timber fence panels burned down, the
    enclosure gate fell and the dumpster lid is gone. Car_A and Car_C burned where they were parked
    and sit on their rims; the other cars are gone.
  * A roll-off container, already half full of debris, stands in front of the store. It arrived after
    the fire, so it is the only thing on the lot that is not burnt.

Same blockout contract as the original: axis-aligned boxes, touching never overlapping, material from
the name suffix. Render-only dressing (soot on the ground, caution tape, the condemned notice text)
is added by render_burned_store_previews.py and is not part of the structure.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_DIR = os.path.join(REPO, "Tools", "blender", "BurnedStore")
BLEND_PATH = os.path.join(OUT_DIR, "BurnedStore.blend")
FBX_PATH = os.path.join(OUT_DIR, "BurnedStore.fbx")

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


# Roof bays (i along x, j along y) that burned through, and the parapets that sat on them.
ROOF_GONE = {(2, 1), (3, 1), (3, 2)}
PARAPET_GONE = {"Parapet_E1", "Parapet_E2", "Parapet_N3"}


def build_store():
    G = "Store"
    X0, X1, Y0, Y1 = -16.0, -4.0, 10.0, 18.0
    T, P, H = 0.3, 0.5, 4.0
    ix0, ix1 = X0 + P, X1 - P
    iy0, iy1 = Y0 + P, Y1 - P
    for ex, xs in (("W", (X0, ix0)), ("E", (ix1, X1))):
        for fy, ys in (("S", (Y0, iy0)), ("N", (iy1, Y1))):
            box(G, f"Pier_{ex}{fy}__concrete", xs[0], xs[1], ys[0], ys[1], 0, H)

    # storefront (south): bulkhead and mullions survive, the glass is gone
    dx0, dx1 = -11.0, -9.0
    for tag, (bx0, bx1) in (("L", (ix0, dx0)), ("R", (dx1, ix1))):
        for k, (a, b) in enumerate(splits(bx0, bx1, 3)):
            box(G, f"Wall_Bulkhead_{tag}{k}__brick", a, b, Y0, Y0 + T, 0, 0.8)
        for k in (1, 2):
            m = bx0 + (bx1 - bx0) * k / 3
            box(G, f"Post_Mullion_{tag}{k}__concrete", m - 0.15, m + 0.15, Y0, Y0 + T, 0.8, 3.0)
        for k, (a, b) in enumerate(splits(bx0, bx1, 2)):
            if (tag, k) == ("R", 1):
                continue  # east header fell onto the sidewalk
            box(G, f"Wall_Header_{tag}{k}__brick", a, b, Y0, Y0 + T, 3.0, H)
    box(G, "Lintel_Door_S__concrete", dx0, dx1, Y0, Y0 + T, 2.6, H)
    # one shard left in the west-most frame
    box(G, "Glass_Shard_L0__wood", ix0, -14.15, Y0 + 0.1, Y0 + 0.18, 0.8, 1.5)

    # plywood over the west openings, the door and the west window
    box(G, "Board_Front_L1__wood", -14.1, -12.4, Y0 - 0.05, Y0, 0.8, 3.0)
    box(G, "Board_Front_L2__wood", -12.4, -11.0, Y0 - 0.05, Y0, 0.8, 3.0)
    box(G, "Board_Door__wood", dx0, dx1, Y0 - 0.05, Y0, 0.15, 2.6)
    box(G, "Board_Front_R0__wood", dx1, -7.4, Y0 - 0.05, Y0, 0.8, 3.0)
    box(G, "Sign_Condemned__wood__noshatter", -10.45, -9.55, Y0 - 0.09, Y0 - 0.05, 1.2, 2.0)

    # back (north) wall: the east end burned down to a stub
    for tag, (a0, a1) in (("L", (ix0, dx0)), ("R", (dx1, ix1))):
        for k, (a, b) in enumerate(splits(a0, a1, 2)):
            top = 2.6 if (tag, k) == ("R", 1) else H
            box(G, f"Wall_Back_{tag}{k}__brick", a, b, Y1 - T, Y1, 0, top)
    box(G, "Lintel_Door_N__concrete", dx0, dx1, Y1 - T, Y1, 2.6, H)

    # west wall with a boarded window; east wall burned down from the north end
    for k, (a, b) in enumerate(((iy0, 12.5), (14.5, 16.0), (16.0, iy1))):
        box(G, f"Wall_W{k}__brick", X0, X0 + T, a, b, 0, H)
    box(G, "Sill_W__brick", X0, X0 + T, 12.5, 14.5, 0, 1.0)
    box(G, "Lintel_W__concrete", X0, X0 + T, 12.5, 14.5, 2.6, H)
    box(G, "Board_W__wood", X0 - 0.05, X0, 12.4, 14.6, 0.9, 2.7)
    for k, ((a, b), top) in enumerate(zip(splits(iy0, iy1, 3), (H, 2.9, 2.0))):
        box(G, f"Wall_E{k}__brick", X1 - T, X1, a, b, 0, top)

    # roof and parapet, minus what burned through
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        for j, (ya, yb) in enumerate(splits(Y0, Y1, 3)):
            if (i, j) not in ROOF_GONE:
                box(G, f"Roof_{i}{j}__concrete", xa, xb, ya, yb, H, H + 0.3)
    zt = H + 0.3
    parapets = []
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        parapets.append((f"Parapet_S{i}", xa, xb, Y0, Y0 + T))
        parapets.append((f"Parapet_N{i}", xa, xb, Y1 - T, Y1))
    for j, (ya, yb) in enumerate(splits(Y0 + T, Y1 - T, 3)):
        parapets.append((f"Parapet_W{j}", X0, X0 + T, ya, yb))
        parapets.append((f"Parapet_E{j}", X1 - T, X1, ya, yb))
    for n, xa, xb, ya, yb in parapets:
        if n not in PARAPET_GONE:
            box(G, f"{n}__brick", xa, xb, ya, yb, zt, zt + 0.5)

    # sidewalk and awning; the east bay and its post are down
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        box(G, f"Slab_Sidewalk{i}__concrete", xa, xb, 6.0, Y0, 0, 0.15)
    for i, (xa, xb) in enumerate(splits(X0, X1, 4)):
        if i == 3:
            continue
        box(G, f"Awning_{i}__concrete", xa, xb, 7.0, Y0, 3.4, 3.7)
        cx = (xa + xb) / 2
        box(G, f"Post_Awning{i}__concrete", cx - 0.15, cx + 0.15, 7.0, 7.3, 0.15, 3.4)
        if i < 2:
            box(G, f"Sign_StoreBand{i}__wood", xa, xb, 7.0, 7.2, 3.7, 4.3)
    box(G, "Awning_Fallen3__concrete", -6.8, -4.4, 7.4, 9.9, 0.15, 0.45)
    box(G, "Wall_HeaderFallen__brick", -6.4, -4.6, 8.0, 8.6, 0.45, 0.75)
    box(G, "Post_AwningFallen__concrete", -7.6, -4.35, 6.55, 6.85, 0.15, 0.45)
    box(G, "Rubble_Sidewalk0__brick", -8.4, -7.6, 8.6, 9.5, 0.15, 0.4)
    box(G, "Rubble_Sidewalk1__brick", -5.8, -5.0, 6.1, 6.5, 0.15, 0.35)

    # interior: the west half is charred but standing, the east half is under the roof
    for y in (13.0, 15.0):
        box(G, f"Shelf_{0 if y < 14 else 1}0__wood", -14.0, -11.0, y, y + 0.5, 0, 1.8)
    box(G, "Shelf_Toppled__wood", -9.0, -6.0, 12.6, 14.4, 0, 0.5)
    box(G, "Counter__wood", -8.6, -5.6, 11.0, 11.8, 0, 1.1)
    box(G, "Register_Fallen__wood", -6.2, -5.6, 12.0, 12.4, 0, 0.3)
    for i, x in enumerate((-15.2, -14.0)):
        box(G, f"Cooler_{i}__concrete", x, x + 1.2, 16.9, 17.7, 0, 2.1)
    box(G, "Cooler_3__concrete", -6.2, -5.0, 16.9, 17.7, 0, 2.1)
    box(G, "Cooler_Toppled__concrete", -7.4, -6.2, 14.8, 16.9, 0, 0.8)
    box(G, "Roof_Fallen0__concrete", -10.6, -7.6, 15.2, 16.8, 0, 0.3)
    box(G, "Roof_Fallen1__concrete", -8.8, -6.4, 12.7, 14.3, 0.5, 0.8)
    box(G, "Roof_Fallen2__concrete", -5.9, -4.6, 13.6, 16.2, 0, 0.3)
    box(G, "Rubble_Interior0__brick", -5.6, -4.7, 14.2, 15.6, 0.3, 0.7)
    box(G, "Rubble_Interior1__brick", -10.2, -9.0, 15.5, 16.4, 0.3, 0.6)
    # east wall brick that fell outward
    box(G, "Rubble_East0__brick", -4.0, -2.8, 14.5, 17.0, 0, 0.4)
    box(G, "Rubble_East1__brick", -3.6, -2.9, 15.0, 16.2, 0.4, 0.7)

    for i, x in enumerate((-15.0, -12.25, -9.5, -6.75, -4.5)):
        box(G, f"Bollard_Store{i}__concrete", x - 0.125, x + 0.125, 6.2, 6.45, 0.15, 1.15)
    box(G, "Bin_Store0__wood", -13.075, -12.525, 9.0, 9.55, 0.15, 1.05)
    box(G, "Bin_Store1__wood", -7.475, -6.925, 9.0, 9.55, 0.15, 1.05)


def build_pumps():
    G = "Pumps"
    for i, (xa, xb) in enumerate(splits(3.0, 13.0, 3)):
        box(G, f"Slab_PumpIsland{i}__concrete", xa, xb, 1.0, 3.0, 0, 0.2)
    box(G, "Pump_Body0__concrete", 5.55, 6.45, 1.7, 2.3, 0.2, 1.8)
    box(G, "Pump_Hood0__concrete", 5.5, 6.5, 1.65, 2.35, 1.8, 2.05)
    box(G, "Pump_Body1__concrete", 9.55, 10.45, 1.7, 2.3, 0.2, 1.8)
    box(G, "Pump_HoodFallen1__concrete", 10.6, 11.6, 1.15, 1.65, 0.2, 0.45)
    for i, (x, y) in enumerate(((3.3, 1.3), (12.7, 1.3), (3.3, 2.7), (12.7, 2.7))):
        box(G, f"Bollard_Pump{i}__concrete", x - 0.125, x + 0.125, y - 0.125, y + 0.125, 0.2, 1.1)
    # canopy: the north-east bay burned through and came down with its fascia
    zc = 5.0
    for i, (xa, xb) in enumerate(splits(2.0, 14.0, 3)):
        for j, (ya, yb) in enumerate(splits(-2.0, 6.0, 2)):
            if (i, j) != (2, 1):
                box(G, f"Slab_PumpCanopy{i}{j}__concrete", xa, xb, ya, yb, zc, zc + 0.4)
    for i, (x, y) in enumerate(((3.0, -1.2), (13.0, -1.2), (3.0, 5.2), (13.0, 5.2))):
        box(G, f"Column_Canopy{i}__concrete", x - 0.25, x + 0.25, y - 0.25, y + 0.25, 0, zc)
    zf = zc + 0.4
    for i, (xa, xb) in enumerate(splits(2.0, 14.0, 3)):
        if i != 1:  # the middle south fascia burned off
            box(G, f"Fascia_S{i}__wood", xa, xb, -2.0, -1.8, zf, zf + 0.6)
        if i != 2:
            box(G, f"Fascia_N{i}__wood", xa, xb, 5.8, 6.0, zf, zf + 0.6)
    for j, (ya, yb) in enumerate(splits(-1.8, 5.8, 2)):
        box(G, f"Fascia_W{j}__wood", 2.0, 2.2, ya, yb, zf, zf + 0.6)
        if j == 0:
            box(G, f"Fascia_E{j}__wood", 13.8, 14.0, ya, yb, zf, zf + 0.6)
    box(G, "Slab_PumpCanopyFallen__concrete", 9.6, 12.6, 3.2, 5.6, 0, 0.4)
    box(G, "Fascia_Fallen__wood", 10.0, 12.6, 5.7, 6.3, 0, 0.2)


def car(name, cx, cy, axis, burnt=False):
    G = "Cars"
    L, W = 4.2, 1.75
    # a burnt-out car has lost its tyres and sits low on its rims
    zw, zb, zc = (0.25, 0.75, 1.25) if burnt else (0.4, 0.95, 1.5)

    def cb(sub, u0, u1, v0, v1, z0, z1):
        if axis == "x":
            box(G, f"{name}_{sub}__wood", cx + u0, cx + u1, cy + v0, cy + v1, z0, z1)
        else:
            box(G, f"{name}_{sub}__wood", cx + v0, cx + v1, cy + u0, cy + u1, z0, z1)

    cb("Body", -L / 2, L / 2, -W / 2, W / 2, zw, zb)
    cb("Cabin", -1.3, 0.7, -0.775, 0.775, zb, zc)
    for i, (u, v) in enumerate(((-1.4, -1), (-1.4, 1), (1.4, -1), (1.4, 1))):
        v0, v1 = (-W / 2, -W / 2 + 0.25) if v < 0 else (W / 2 - 0.25, W / 2)
        cb(f"Wheel{i}", u - 0.35, u + 0.35, v0, v1, 0, zw)


def build_cars():
    car("Car_A", -14.0, 3.0, "y", burnt=True)
    car("Car_C", 20.0, 6.0, "y", burnt=True)
    for i, x in enumerate((-14.0, -11.0, -8.0, -5.0)):
        box("Cars", f"Slab_WheelStop{i}__concrete", x - 0.9, x + 0.9, 5.4, 5.65, 0, 0.12)


def build_demolition():
    G = "Demolition"
    # roll-off container west of the burnt car, open top, half full of debris
    x0, x1, y0, y1 = -17.9, -15.5, 0.2, 5.9
    box(G, "Slab_RollOffBase__concrete", x0, x1, y0, y1, 0, 0.25)
    box(G, "Wall_RollOffW__concrete", x0, x0 + 0.15, y0, y1, 0.25, 1.8)
    box(G, "Wall_RollOffE__concrete", x1 - 0.15, x1, y0, y1, 0.25, 1.8)
    box(G, "Wall_RollOffS__concrete", x0 + 0.15, x1 - 0.15, y0, y0 + 0.15, 0.25, 1.8)
    box(G, "Wall_RollOffN__concrete", x0 + 0.15, x1 - 0.15, y1 - 0.15, y1, 0.25, 1.8)
    box(G, "Debris_RollOff0__brick", x0 + 0.15, x1 - 0.15, 3.4, y1 - 0.15, 0.25, 1.1)
    box(G, "Debris_RollOff1__wood", x0 + 0.15, -16.4, 1.6, 3.4, 0.25, 0.8)
    box(G, "Debris_RollOff2__concrete", -17.5, -16.0, 3.8, 5.3, 1.1, 1.45)


def build_site():
    G = "Site"
    for i, (x, y, d) in enumerate(((-20, 2, 1), (-1, 6, 1), (20, 2, -1), (-1, -12, 1))):
        box(G, f"Slab_LightBase{i}__concrete", x - 0.4, x + 0.4, y - 0.4, y + 0.4, 0, 0.5)
        box(G, f"Post_Light{i}__wood", x - 0.125, x + 0.125, y - 0.125, y + 0.125, 0.5, 8.0)
        a, b = sorted((x - d * 0.125, x + d * 1.6))
        box(G, f"Beam_LightArm{i}__wood", a, b, y - 0.1, y + 0.1, 8.0, 8.2)
        a, b = sorted((x + d * 1.0, x + d * 1.6))
        box(G, f"Light_Head{i}__wood", a, b, y - 0.2, y + 0.2, 7.8, 8.0)

    px, py = -20.0, -16.0
    box(G, "Slab_PylonFooting__concrete", px - 1.3, px + 1.3, py - 0.45, py + 0.45, 0, 0.5)
    for i, s in enumerate((-1, 1)):
        a, b = sorted((px + s * 0.8, px + s * 1.2))
        box(G, f"Post_Pylon{i}__concrete", a, b, py - 0.2, py + 0.2, 0.5, 5.5)
    box(G, "Sign_PylonPrice__wood", px - 0.8, px + 0.8, py - 0.2, py + 0.2, 2.5, 3.6)  # upper panel burned away
    box(G, "Sign_PylonCabinet__wood", px - 1.2, px + 1.2, py - 0.3, py + 0.3, 5.5, 7.8)

    for k, (a, b) in enumerate(splits(-23.0, -19.0, 2)):
        box(G, f"Wall_EnclosureBack{k}__brick", a, b, 16.75, 17.0, 0, 1.8)
    for k, (a, b) in enumerate(splits(13.8, 16.75, 2)):
        box(G, f"Wall_EnclosureW{k}__brick", -23.0, -22.75, a, b, 0, 1.8)
        box(G, f"Wall_EnclosureE{k}__brick", -19.25, -19.0, a, b, 0, 1.8)
    box(G, "Post_GateW__concrete", -23.0, -22.7, 13.5, 13.8, 0, 2.0)
    box(G, "Post_GateE__concrete", -19.3, -19.0, 13.5, 13.8, 0, 2.0)
    box(G, "Gate_Leaf0__wood", -22.7, -21.0, 13.5, 13.6, 0, 1.8)
    box(G, "Gate_LeafFallen1__wood", -21.0, -19.3, 11.7, 13.5, 0, 0.1)  # burned off its hinges
    box(G, "Dumpster_Body__wood", -22.4, -20.2, 14.6, 16.4, 0, 1.2)  # lid burned off

    # plastic bins slumped in the heat
    for i, (x, y) in enumerate(((1.4, 2.0), (16.0, 2.0))):
        box(G, f"Bin_Lot{i}__wood", x - 0.3, x + 0.3, y - 0.3, y + 0.3, 0, 0.45)

    n = 0
    for a, b, c in ((-24.75, -14.0, 4), (-6.0, 10.0, 6), (18.0, 24.88, 3)):
        for xa, xb in splits(a, b, c):
            box(G, f"Slab_Curb{n}__concrete", xa, xb, -20.0, -19.8, 0, 0.15)
            n += 1

    for i, (xa, xb) in enumerate(splits(-25.0, 25.0, 10)):
        box(G, f"Wall_BoundaryN{i}__brick", xa, xb, 19.75, 20.0, 0, 1.8)
    for i, (ya, yb) in enumerate(splits(-20.0, 19.75, 8)):
        box(G, f"Wall_BoundaryW{i}__brick", -25.0, -24.75, ya, yb, 0, 1.8)
    # timber fence: some panels burned to stubs, two burned away entirely
    stub = {3: 0.7, 4: 0.4, 7: 0.9}
    for i, (ya, yb) in enumerate(splits(-20.0, 19.75, 10)):
        if i not in (5, 6):
            box(G, f"Panel_FenceE{i}__wood", 24.88, 25.0, ya, yb, 0, stub.get(i, 1.8))


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
        raise SystemExit("[burned] pieces interpenetrate:\n  " + "\n  ".join(bad[:30]))
    print(f"[burned] overlap check passed for {len(boxes)} pieces")


# ---------------------------------------------------------------- render materials

CHAR = (0.018, 0.016, 0.015, 1)

# (name prefix, base colour, roughness, metallic, soot level 0..1). First match wins.
RENDER_MATS = [
    ("Glass_", (0.2, 0.22, 0.22, 1), 0.1, 0.0, 0.7),
    ("Board_", (0.3, 0.21, 0.12, 1), 0.85, 0, 0.2),
    ("Sign_Condemned", (0.95, 0.92, 0.85, 1), 0.6, 0, 0.0),
    ("Sign_StoreBand", (0.6, 0.12, 0.1, 1), 0.6, 0, 0.75),
    ("Sign_Pylon", (0.7, 0.58, 0.18, 1), 0.6, 0, 0.8),
    ("Car_A", (0.32, 0.17, 0.09, 1), 0.85, 0.4, 0.8),
    ("Car_C", (0.3, 0.18, 0.1, 1), 0.85, 0.4, 0.75),
    ("Pump_Body", (0.6, 0.12, 0.1, 1), 0.6, 0.2, 0.85),
    ("Pump_Hood", (0.7, 0.7, 0.7, 1), 0.6, 0.2, 0.85),
    ("Fascia_", (0.7, 0.18, 0.13, 1), 0.6, 0, 0.8),
    ("Bollard_", (0.85, 0.7, 0.1, 1), 0.5, 0, 0.6),
    ("Bin_", (0.12, 0.22, 0.15, 1), 0.6, 0, 0.85),
    ("Dumpster", (0.1, 0.3, 0.16, 1), 0.7, 0.2, 0.85),
    ("Gate_", (0.35, 0.25, 0.16, 1), 0.8, 0, 0.9),
    ("Light_Head", (0.6, 0.6, 0.55, 1), 0.5, 0, 0.7),
    ("Post_Light", (0.25, 0.25, 0.27, 1), 0.6, 0.5, 0.6),
    ("Beam_LightArm", (0.25, 0.25, 0.27, 1), 0.6, 0.5, 0.6),
    ("Shelf", (0.55, 0.55, 0.57, 1), 0.7, 0.3, 0.9),
    ("Counter", (0.5, 0.38, 0.25, 1), 0.8, 0, 0.95),
    ("Register", (0.3, 0.3, 0.3, 1), 0.8, 0, 0.95),
    ("Cooler", (0.7, 0.78, 0.82, 1), 0.5, 0.2, 0.85),
    ("Panel_Fence", (0.4, 0.28, 0.17, 1), 0.85, 0, 0.9),
    ("Wall_RollOff", (0.12, 0.28, 0.55, 1), 0.6, 0.3, 0),
    ("Slab_RollOff", (0.12, 0.28, 0.55, 1), 0.6, 0.3, 0),
    ("Debris_RollOff0", (0.42, 0.26, 0.2, 1), 0.95, 0, 0.6),
    ("Debris_RollOff1", (0.35, 0.25, 0.16, 1), 0.95, 0, 0.8),
    ("Debris_RollOff2", (0.45, 0.45, 0.43, 1), 0.95, 0, 0.5),
    ("Roof_Fallen", (0.3, 0.3, 0.32, 1), 0.95, 0, 0.95),
    ("Roof", (0.26, 0.26, 0.27, 1), 0.9, 0, 0.8),
    ("Rubble", (0.5, 0.29, 0.22, 1), 0.95, 0, 0.75),
    ("Slab_PumpCanopyFallen", (0.5, 0.5, 0.5, 1), 0.95, 0, 0.95),
    ("Slab_PumpCanopy", (0.75, 0.75, 0.74, 1), 0.85, 0, 0.75),
    ("Column_Canopy", (0.75, 0.75, 0.74, 1), 0.85, 0, 0.7),
    ("Wall_", (0.58, 0.32, 0.25, 1), 0.85, 0, 0.7),
    ("Parapet", (0.58, 0.32, 0.25, 1), 0.85, 0, 0.85),
    ("Sill_", (0.58, 0.32, 0.25, 1), 0.85, 0, 0.7),
]
STORE_DEFAULT_CONCRETE = ("StoreConcrete", (0.6, 0.6, 0.58, 1), 0.9, 0, 0.6)


def burnt_material(key, col, rough, metal, soot):
    """Principled material whose colour is pulled toward char by noise, height and an east-west gradient.

    Soot climbs the walls (worse higher up, where smoke poured out of the openings) and the whole store
    is worse toward the east end, where the fire started. soot = 0 gives a plain material.
    """
    m = bpy.data.materials.new("M_" + key)
    m.use_nodes = True
    m.diffuse_color = tuple(c * (1 - 0.7 * soot) for c in col[:3]) + (1,)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if soot <= 0:
        b.inputs["Base Color"].default_value = col
        return m

    N = nt.nodes.new
    L = nt.links.new
    geo = N("ShaderNodeNewGeometry")
    sep = N("ShaderNodeSeparateXYZ")
    L(geo.outputs["Position"], sep.inputs[0])
    noise = N("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 2.2
    noise.inputs["Detail"].default_value = 8.0
    noise.inputs["Roughness"].default_value = 0.65
    L(geo.outputs["Position"], noise.inputs["Vector"])
    streak = N("ShaderNodeTexNoise")  # vertical streaks: stretch the noise along z
    streak.inputs["Scale"].default_value = 2.5
    streak.inputs["Detail"].default_value = 4.0
    mapping = N("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1.0, 1.0, 0.12)
    L(geo.outputs["Position"], mapping.inputs["Vector"])
    L(mapping.outputs["Vector"], streak.inputs["Vector"])

    def mrange(src, a, b_, c, d):
        r = N("ShaderNodeMapRange")
        r.clamp = True
        r.inputs["From Min"].default_value = a
        r.inputs["From Max"].default_value = b_
        r.inputs["To Min"].default_value = c
        r.inputs["To Max"].default_value = d
        L(src, r.inputs["Value"])
        return r.outputs["Result"]

    def calc(op, a, b_):
        n = N("ShaderNodeMath")
        n.operation = op
        for i, v in enumerate((a, b_)):
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    height = mrange(sep.outputs["Z"], 0.3, 4.8, 0.35, 1.0)
    east = mrange(sep.outputs["X"], -16.0, -4.0, 0.55, 1.0)
    blotch = mrange(noise.outputs["Fac"], 0.3, 0.7, -0.25, 0.25)
    streaks = mrange(streak.outputs["Fac"], 0.4, 0.6, -0.2, 0.2)
    amount = calc("MULTIPLY", calc("MULTIPLY", height, east), soot * 1.4)
    amount = calc("ADD", calc("ADD", amount, blotch), streaks)
    amount = mrange(amount, 0.0, 1.1, 0.0, 1.0)

    # base -> smoke-stained brown -> char, so the transition reads as staining, not camouflage
    stained = (col[0] * 0.32 + 0.03, col[1] * 0.28 + 0.02, col[2] * 0.25 + 0.01, 1)
    mix0 = mix_rgba(nt, mrange(amount, 0.0, 0.5, 0.0, 1.0), col, stained)
    mix = mix_rgba(nt, mrange(amount, 0.45, 0.9, 0.0, 1.0), mix0, CHAR)
    L(mix, b.inputs["Base Color"])
    rmix = N("ShaderNodeMix")
    rmix.inputs["A"].default_value = rough
    rmix.inputs["B"].default_value = 0.97
    L(amount, rmix.inputs["Factor"])
    L(rmix.outputs["Result"], b.inputs["Roughness"])
    return m


def mix_rgba(nt, factor, a, b):
    """Colour Mix node; a and b are RGBA tuples or output sockets. Returns the colour result socket."""
    n = nt.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    sock = {s.identifier: s for s in n.inputs}
    nt.links.new(factor, sock["Factor_Float"])
    for key, v in (("A_Color", a), ("B_Color", b)):
        if isinstance(v, tuple):
            sock[key].default_value = v
        else:
            nt.links.new(v, sock[key])
    return next(s for s in n.outputs if s.identifier == "Result_Color")


def assign_render_materials():
    cache = {}

    def get(spec):
        key = spec[0].strip("_")
        if key not in cache:
            cache[key] = burnt_material(key, *spec[1:])
        return cache[key]

    for o in PIECES:
        n = o.name
        in_store = o.users_collection[0].name == "Store"
        spec = next((s for s in RENDER_MATS if n.startswith(s[0])), None)
        if n.startswith("Car") and "_Wheel" in n:
            spec = ("BurntRim", (0.2, 0.18, 0.17, 1), 0.7, 0.6, 0.6)
        elif n.startswith("Car") and "_Cabin" in n:
            spec = ("BurntCabin", (0.1, 0.08, 0.07, 1), 0.9, 0.3, 0.9)
        if spec is None:
            if "__brick" in n:
                spec = ("Brick", (0.62, 0.34, 0.26, 1), 0.85, 0, 0.75)
            elif "__wood" in n:
                spec = ("Wood", (0.45, 0.32, 0.2, 1), 0.8, 0, 0.85)
            elif in_store:
                spec = STORE_DEFAULT_CONCRETE
            else:
                spec = ("Concrete", (0.58, 0.58, 0.56, 1), 0.9, 0, 0.7)
        o.data.materials.append(get(spec))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    build_store()
    build_pumps()
    build_cars()
    build_demolition()
    build_site()
    check_no_overlaps()

    os.makedirs(OUT_DIR, exist_ok=True)

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
    print(f"[burned] {len(PIECES)} pieces; extent x {hi[0]-lo[0]:.1f} y {hi[1]-lo[1]:.1f} z {hi[2]-lo[2]:.1f}; ground z {lo[2]:.3f}")
    for g, c in COLL.items():
        print(f"[burned]   {g}: {len(c.objects)}")


if __name__ == "__main__":
    main()
    sys.exit(0)
