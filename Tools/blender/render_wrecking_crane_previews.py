"""Render preview PNGs of the crane. Temporary ground/lights/cameras live only in this session."""
import math
import os
import sys

import bpy
from mathutils import Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "WreckingCrane")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "WreckingCrane.blend"))
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 800
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "Standard"

# temporary ground
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 5, 0))
gnd = bpy.context.object
gnd.name = "TMP_Ground"
gm = bpy.data.materials.new("TMP_G")
gm.diffuse_color = (0.42, 0.40, 0.36, 1)
gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.40, 0.36, 1)
gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
gnd.data.materials.append(gm)

sun = bpy.data.lights.new("TMP_Sun", "SUN")
sun.energy = 4
sun.angle = math.radians(20)
so = bpy.data.objects.new("TMP_Sun", sun)
sc.collection.objects.link(so)
so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
fill = bpy.data.lights.new("TMP_Fill", "SUN")
fill.energy = 1.5
fo = bpy.data.objects.new("TMP_Fill", fill)
sc.collection.objects.link(fo)
fo.rotation_euler = (math.radians(60), 0, math.radians(150))

sc.world = bpy.data.worlds.new("TMP_W")
sc.world.use_nodes = True
bg = sc.world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.55, 0.65, 0.78, 1)
bg.inputs[1].default_value = 1.0

cam_d = bpy.data.cameras.new("TMP_Cam")
cam = bpy.data.objects.new("TMP_Cam", cam_d)
sc.collection.objects.link(cam)
sc.camera = cam


def shoot(name, loc, target, lens=35, ortho=None):
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if ortho:
        cam_d.type = "ORTHO"
        cam_d.ortho_scale = ortho
    else:
        cam_d.type = "PERSP"
        cam_d.lens = lens
    sc.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)


shoot("preview_three_quarter.png", (-19, -9, 9), (0, 5, 5), lens=32)
shoot("preview_side.png", (40, 3.5, 6), (0, 3.5, 6), ortho=22)
shoot("preview_ball_closeup.png", (-4.5, 3.5, 5.0), (0, 7.41, 3.8), lens=50)
if "--extra" in sys.argv:
    shoot("qa_front.png", (0, -40, 6), (0, 0, 6), ortho=22)
    shoot("qa_top.png", (0, 3.5, 40), (0, 3.5, 0), ortho=22)
