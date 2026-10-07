"""Render preview PNGs of the fire-ravaged store lot.

Ground, soot and water stains, caution tape, the condemned notice text, lights and cameras are
temporary and live only in this render.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/render_burned_store_previews.py
"""
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "BurnedStore")
OUT = os.path.join(HERE, "renders")
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "BurnedStore.blend"))
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.samples = int(os.environ.get("SAMPLES", 128))
sc.cycles.use_denoising = True
try:
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "METAL"
    prefs.get_devices()
    for d in prefs.devices:
        d.use = True
    sc.cycles.device = "GPU"
except Exception:
    sc.cycles.device = "CPU"
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "AgX"
sc.view_settings.look = "AgX - Medium High Contrast"
sc.view_settings.exposure = 0.0


def mat(name, col, rough=0.9, emit=None, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = col
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = emit
        b.inputs["Emission Strength"].default_value = 3
    return m


def stain(name, col, rough, scale, lo, hi, seed_offset=0.0):
    """A decal material: colour where a noise mask is high, fully transparent elsewhere."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = col
    b.inputs["Roughness"].default_value = rough
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Location"].default_value = (seed_offset, seed_offset * 0.7, 0)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 6
    ramp = nt.nodes.new("ShaderNodeMapRange")
    ramp.clamp = True
    ramp.inputs["From Min"].default_value = lo
    ramp.inputs["From Max"].default_value = hi
    nt.links.new(geo.outputs["Position"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Value"])
    nt.links.new(ramp.outputs["Result"], b.inputs["Alpha"])
    return m


def plane(name, x0, x1, y0, y1, z, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    ob.data.materials.append(m)
    return ob


def vplane_x(name, x0, x1, y, z0, z1, m):
    """Vertical quad facing -y."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    ob.data.materials.append(m)
    return ob


# ------------------------------------------------------------------ ground
grass = mat("TMP_Grass", (0.11, 0.16, 0.06, 1))
asphalt = mat("TMP_Asphalt", (0.075, 0.075, 0.08, 1), 0.85)
paint = mat("TMP_Paint", (0.7, 0.7, 0.66, 1), 0.6)
road = mat("TMP_Road", (0.05, 0.05, 0.055, 1), 0.85)
yellow = mat("TMP_Yellow", (0.75, 0.58, 0.08, 1), 0.6)

plane("TMP_Grass", -90, 90, -90, 90, -0.02, grass)
plane("TMP_Lot", -25, 25, -20, 20, 0.0, asphalt)
plane("TMP_Road", -90, 90, -34, -20, 0.0, road)
for x in range(-80, 80, 8):
    plane("TMP_Dash", x, x + 4, -27.1, -26.9, 0.005, yellow)
for x in (-15.5, -12.5, -9.5, -6.5, -3.5):
    plane("TMP_Stall", x - 0.05, x + 0.05, 0.3, 5.3, 0.005, paint)
for x in (14.5, 17.5, 20.5, 23.5):
    plane("TMP_Stall2", x - 0.05, x + 0.05, 2.3, 7.6, 0.005, paint)
plane("TMP_Apron", 1.0, 15.0, -3.0, 7.0, 0.002, mat("TMP_Concrete", (0.36, 0.36, 0.34, 1)))

# fire aftermath on the ground: ash-black floor inside, soot outside, firefighting water puddles
ash_floor = stain("TMP_AshFloor", (0.02, 0.018, 0.017, 1), 0.98, 1.2, 0.05, 0.35)
plane("TMP_StoreFloorBase", -15.7, -4.3, 10.3, 17.7, 0.001, mat("TMP_BurntSlab", (0.07, 0.065, 0.06, 1), 0.95))
plane("TMP_StoreFloorAsh", -15.7, -4.3, 10.3, 17.7, 0.004, ash_floor)
soot = stain("TMP_Soot", (0.012, 0.011, 0.01, 1), 0.95, 0.6, 0.42, 0.62)
plane("TMP_SootSidewalk", -16.0, -4.0, 6.0, 10.0, 0.152, soot)
plane("TMP_SootLotFront", -17.5, -2.5, 0.2, 6.0, 0.006, stain("TMP_Soot2", (0.015, 0.014, 0.013, 1), 0.95, 0.5, 0.45, 0.65, 3.1))
plane("TMP_SootEast", -4.0, -2.1, 9.0, 19.5, 0.006, stain("TMP_Soot3", (0.015, 0.014, 0.013, 1), 0.95, 0.7, 0.4, 0.6, 7.3))
plane("TMP_SootCar", -15.6, -12.4, 0.4, 5.6, 0.008, stain("TMP_Soot4", (0.01, 0.01, 0.01, 1), 0.95, 1.1, 0.3, 0.5, 1.7))
# the fire crossed the whole lot: soot over the asphalt and pump apron, scorched grass at the edges
plane("TMP_SootLot", -25.0, 25.0, -20.0, 20.0, 0.004, stain("TMP_Soot5", (0.012, 0.011, 0.01, 1), 0.95, 0.15, 0.3, 0.75, 4.2))
plane("TMP_SootApron", 1.0, 15.0, -3.0, 7.0, 0.007, stain("TMP_Soot6", (0.012, 0.011, 0.01, 1), 0.95, 0.5, 0.3, 0.55, 9.4))
plane("TMP_Scorch", -35.0, 35.0, -20.0, 30.0, -0.015, stain("TMP_Scorch", (0.04, 0.03, 0.02, 1), 0.98, 0.15, 0.35, 0.55, 2.6))
water = stain("TMP_Water", (0.015, 0.016, 0.018, 1), 0.12, 0.4, 0.62, 0.65, 11.0)
plane("TMP_Puddles", -18.0, 16.0, -6.0, 8.0, 0.01, water)

# ------------------------------------------------------------------ dressing
# caution tape: diagonal black/yellow stripes
tape = bpy.data.materials.new("TMP_Tape")
tape.use_nodes = True
tn = tape.node_tree
tb = tn.nodes["Principled BSDF"]
tb.inputs["Roughness"].default_value = 0.35
wave = tn.nodes.new("ShaderNodeTexWave")
wave.wave_profile = "SAW"
wave.inputs["Scale"].default_value = 3.0
tramp = tn.nodes.new("ShaderNodeValToRGB")
tramp.color_ramp.interpolation = "CONSTANT"
tramp.color_ramp.elements[0].color = (0.9, 0.7, 0.02, 1)
tramp.color_ramp.elements[1].position = 0.7
tramp.color_ramp.elements[1].color = (0.01, 0.01, 0.01, 1)
tgeo = tn.nodes.new("ShaderNodeNewGeometry")
tn.links.new(tgeo.outputs["Position"], wave.inputs["Vector"])
tn.links.new(wave.outputs["Fac"], tramp.inputs["Fac"])
tn.links.new(tramp.outputs["Color"], tb.inputs["Base Color"])
vplane_x("TMP_TapeBollards", -15.0, -4.5, 6.32, 0.95, 1.03, tape)
vplane_x("TMP_TapeBollardsLow", -15.0, -4.5, 6.33, 0.55, 0.63, tape)

# condemned notice text on the placard by the door
notice = mat("TMP_NoticeRed", (0.55, 0.03, 0.02, 1), 0.5)
bpy.ops.object.text_add(location=(-10.0, 9.905, 1.83))
txt = bpy.context.active_object
txt.name = "TMP_NoticeText"
txt.data.body = "CONDEMNED\nUNSAFE\nDO NOT ENTER"
txt.data.align_x = "CENTER"
txt.data.size = 0.13
txt.data.extrude = 0.002
txt.rotation_euler = (math.radians(90), 0, 0)
txt.data.materials.append(notice)

# ------------------------------------------------------------------ light
sun = bpy.data.lights.new("TMP_Sun", "SUN")
sun.energy = 2.6
sun.angle = math.radians(6)
sun.color = (1.0, 0.9, 0.8)
so = bpy.data.objects.new("TMP_Sun", sun)
sc.collection.objects.link(so)
so.rotation_euler = (math.radians(58), math.radians(8), math.radians(-40))

world = bpy.data.worlds.new("TMP_W")
sc.world = world
world.use_nodes = True
wn = world.node_tree
for n in list(wn.nodes):
    wn.nodes.remove(n)
sky = wn.nodes.new("ShaderNodeTexSky")
haze = wn.nodes.new("ShaderNodeMix")
haze.data_type = "RGBA"
hz = {s.identifier: s for s in haze.inputs}
hz["Factor_Float"].default_value = 0.6
hz["B_Color"].default_value = (0.5, 0.48, 0.46, 1)
bg = wn.nodes.new("ShaderNodeBackground")
wout = wn.nodes.new("ShaderNodeOutputWorld")
sky.sun_disc = False
sky.sun_elevation = math.radians(32)
sky.sun_rotation = math.radians(140)
bg.inputs[1].default_value = 0.5
wn.links.new(sky.outputs[0], hz["A_Color"])
wn.links.new(next(s for s in haze.outputs if s.identifier == "Result_Color"), bg.inputs[0])
wn.links.new(bg.outputs[0], wout.inputs[0])

cam_d = bpy.data.cameras.new("TMP_Cam")
cam = bpy.data.objects.new("TMP_Cam", cam_d)
sc.collection.objects.link(cam)
sc.camera = cam


def shoot(name, loc, target, lens=28, ortho=None):
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    if ortho:
        cam_d.type = "ORTHO"
        cam_d.ortho_scale = ortho
    else:
        cam_d.type = "PERSP"
        cam_d.lens = lens
    sc.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    print("rendered", name)


only = os.environ.get("ONLY")
SHOTS = [
    ("01_aerial_three_quarter.png", (-44, -18, 26), (-8, 9, 0.5), 32),
    ("02_store_front.png", (-10, -14, 3.2), (-10, 10, 2.0), 26),
    ("03_east_collapse.png", (4, 16.5, 6), (-6, 14.5, 1.5), 28),
    ("04_roof_hole_aerial.png", (-1, 26, 17), (-9, 14, 0.5), 30),
    ("05_storefront_detail.png", (-8.6, 2.2, 1.8), (-11.2, 10, 1.9), 28),
    ("06_interior.png", (-5.2, 10.6, 1.7), (-10, 15.5, 0.8), 18),
    ("07_pump_island.png", (17, -15, 4.0), (8, 2, 2.0), 28),
]
for n, loc, tgt, lens in SHOTS:
    if not only or only in n:
        shoot(n, loc, tgt, lens=lens)
if os.environ.get("QA"):
    shoot("qa_top.png", (-10, 10, 60), (-10, 10, 0), ortho=26)
