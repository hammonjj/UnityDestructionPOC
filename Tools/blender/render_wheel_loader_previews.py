"""Render preview PNGs of the wheel loader. Ground, lights, cameras and any re-posing live only in this
session; the .blend is never saved.

    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/render_wheel_loader_previews.py [-- --extra]

Blender axes: the machine faces +Y; design y (left) is Blender -X. Colliders and Vol_Cavity are hidden.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "WheelLoader")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "WheelLoader.blend"))
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 800
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "Standard"
O = bpy.data.objects
for ob in O:
    if ob.name.startswith(("Col_", "Vol_")):
        ob.hide_render = True
        ob.hide_viewport = True

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 2, 0))
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


def pose(steer=0.0, lift=0.0, rotx=0.0):
    O["WL_FrontFrame"].rotation_euler = Euler((0, 0, math.radians(steer)))
    O["WL_LiftArm"].rotation_euler = Euler((math.radians(lift), 0, 0))
    O["WL_Bucket"].rotation_euler = Euler((math.radians(rotx), 0, 0))
    bpy.context.view_layer.update()


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


def orbit(tgt, az_deg, el_deg, dist):
    az, el = math.radians(az_deg), math.radians(el_deg)
    t = Vector(tgt)
    return t + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * dist


# Blender: machine faces +Y; design-left is -X, so az=-60 is the front-left quarter.
# 1) game-camera-style three-quarter overhead (37 deg), bucket raised and curled back, slight steer
pose(steer=12, lift=38, rotx=35)
tgt = (0, 2.2, 1.9)
shoot("preview_three_quarter.png", orbit(tgt, -60, 37, 13), tgt, lens=35)
# 2) side view (design left), rest pose, orthographic
pose()
shoot("preview_side.png", (-40, 1.3, 1.9), (0, 1.3, 1.9), ortho=11)
# 3) front view (looking back at the bucket), rest
shoot("preview_front.png", (0, 40, 1.8), (0, 0, 1.8), ortho=6.5)

if "--extra" in sys.argv:
    shoot("qa_top.png", (0, 1.3, 40), (0, 1.3, 0), ortho=11)
    pose(lift=64, rotx=45)
    shoot("qa_side_raised.png", (-40, 1.3, 2.5), (0, 1.3, 2.5), ortho=11)
    pose(lift=30, rotx=-55)
    shoot("qa_side_dump.png", (-40, 1.3, 2.5), (0, 1.3, 2.5), ortho=11)
    pose(steer=40)
    shoot("qa_steer_left_top.png", (0, 1.3, 40), (0, 1.3, 0), ortho=11)
    pose(steer=-40)
    shoot("qa_steer_right_top.png", (0, 1.3, 40), (0, 1.3, 0), ortho=11)
    pose()
    shoot("qa_rear_three_quarter.png", orbit((0, 0.5, 1.5), 140, 25, 15), (0, 0.5, 1.5), lens=35)
