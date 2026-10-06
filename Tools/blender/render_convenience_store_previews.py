"""Render preview PNGs of the convenience-store lot. Ground, markings, lights and cameras are temporary."""
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ConvenienceStore")
OUT = os.path.join(HERE, "renders")
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "ConvenienceStore.blend"))
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.samples = 96
sc.cycles.use_denoising = True
sc.cycles.device = "CPU"
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "Standard"
sc.view_settings.exposure = -0.9


def mat(name, col, rough=0.9, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = col
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = emit
        b.inputs["Emission Strength"].default_value = 3
    return m


def plane(name, x0, x1, y0, y1, z, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    ob.data.materials.append(m)
    return ob


grass = mat("TMP_Grass", (0.13, 0.22, 0.07, 1))
asphalt = mat("TMP_Asphalt", (0.09, 0.09, 0.1, 1), 0.85)
paint = mat("TMP_Paint", (0.85, 0.85, 0.8, 1), 0.6)
road = mat("TMP_Road", (0.06, 0.06, 0.065, 1), 0.85)
yellow = mat("TMP_Yellow", (0.85, 0.65, 0.08, 1), 0.6)

plane("TMP_Grass", -90, 90, -90, 90, -0.02, grass)
plane("TMP_Lot", -25, 25, -20, 20, 0.0, asphalt)
plane("TMP_Road", -90, 90, -34, -20, 0.0, road)
for x in range(-80, 80, 8):
    plane("TMP_Dash", x, x + 4, -27.1, -26.9, 0.005, yellow)
# parking stall lines in front of the store
for x in (-15.5, -12.5, -9.5, -6.5, -3.5):
    plane("TMP_Stall", x - 0.05, x + 0.05, 0.3, 5.6, 0.005, paint)
for x in (14.5, 17.5, 20.5, 23.5):
    plane("TMP_Stall2", x - 0.05, x + 0.05, 2.3, 7.6, 0.005, paint)
# pump lane arrows / island apron
plane("TMP_Apron", 1.0, 15.0, -3.0, 7.0, 0.002, mat("TMP_Concrete", (0.4, 0.4, 0.38, 1)))

sun = bpy.data.lights.new("TMP_Sun", "SUN")
sun.energy = 3.5
sun.angle = math.radians(2.5)
so = bpy.data.objects.new("TMP_Sun", sun)
sc.collection.objects.link(so)
so.rotation_euler = (math.radians(48), math.radians(8), math.radians(-52))

world = bpy.data.worlds.new("TMP_W")
sc.world = world
world.use_nodes = True
nt = world.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
sky = nt.nodes.new("ShaderNodeTexSky")
bg = nt.nodes.new("ShaderNodeBackground")
out = nt.nodes.new("ShaderNodeOutputWorld")
sky.sun_disc = False
sky.sun_elevation = math.radians(40)
sky.sun_rotation = math.radians(150)
bg.inputs[1].default_value = 0.35
nt.links.new(sky.outputs[0], bg.inputs[0])
nt.links.new(bg.outputs[0], out.inputs[0])

# emissive light heads
for o in bpy.data.objects:
    if o.name.startswith("Light_Head"):
        o.data.materials.clear()
        o.data.materials.append(mat("TMP_Lamp", (1, 1, 0.8, 1), 0.3, (1, 0.95, 0.7, 1)))

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


shoot("01_aerial_three_quarter.png", (-40, -42, 30), (-2, 1, 0), lens=34)
shoot("02_store_front.png", (-10, -14, 3.2), (-10, 10, 2.0), lens=24)
shoot("03_pump_island.png", (17, -15, 4.0), (8, 2, 2.0), lens=28)
shoot("04_rear_side.png", (6, 34, 13), (-14, 13, 1.0), lens=30)
shoot("05_detail_storefront.png", (-10.0, 5.5, 1.9), (-12.5, 10, 1.8), lens=26)
if os.environ.get("QA"):
    shoot("qa_top.png", (0, 0, 80), (0, 0, 0), ortho=60)
    shoot("qa_front.png", (0, -80, 6), (0, 0, 6), ortho=60)
    shoot("qa_side.png", (80, 0, 6), (0, 0, 6), ortho=60)
