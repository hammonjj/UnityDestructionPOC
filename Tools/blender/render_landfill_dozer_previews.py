"""Render preview PNGs of the landfill dozer. Ground, lights, cameras and any re-posing live only in this
session; the .blend is never saved.

    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/render_landfill_dozer_previews.py [-- --extra]

Blender axes: the machine faces +Y; design y (left) is Blender -X. Colliders are hidden.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "LandfillDozer")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "LandfillDozer.blend"))
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

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 3, 0))
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
cam_d.clip_end = 200

# rest directions of the ram halves (both are identity-rotated at load)
bpy.context.view_layer.update()
REST = {}
for sfx in ("L", "R"):
    bar, rod = O["DZ_LiftRam_" + sfx], O["DZ_LiftRamRod_" + sfx]
    d = (rod.matrix_world.translation - bar.matrix_world.translation).normalized()
    REST[sfx] = (d, -d)


def pose(lift=0.0):
    """DZ_Blade rotation_euler.x = lift (positive = tip up); the ram halves are re-aimed at each other."""
    blade = O["DZ_Blade"]
    blade.rotation_euler = Euler((math.radians(lift), 0, 0))
    bpy.context.view_layer.update()
    for sfx in ("L", "R"):
        bar, rod = O["DZ_LiftRam_" + sfx], O["DZ_LiftRamRod_" + sfx]
        bar.rotation_mode = rod.rotation_mode = "QUATERNION"
        bar.rotation_quaternion = rod.rotation_quaternion = (1, 0, 0, 0)
        bpy.context.view_layer.update()
        a, b = bar.matrix_world.translation.copy(), rod.matrix_world.translation.copy()
        bar.rotation_quaternion = REST[sfx][0].rotation_difference(b - a)
        rod.rotation_quaternion = REST[sfx][1].rotation_difference(blade.matrix_world.to_3x3().inverted() @ (a - b))
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
# Design -> Blender target: design (x, y, z) -> (-y, x, z).
# 1) game-camera-style three-quarter overhead (35 deg), blade slightly raised
pose(lift=8)
tgt = (0, 1.4, 1.6)
shoot("preview_three_quarter.png", orbit(tgt, -55, 30, 17), tgt, lens=35)
# 2) side view (design left), blade raised, orthographic
pose(lift=30)
shoot("preview_side.png", (-40, 0.6, 2.2), (0, 0.6, 2.2), ortho=11.5)
# 3) front view (looking back at the blade), rest
pose(0)
shoot("preview_front.png", (0, 40, 1.9), (0, 0, 1.9), ortho=8.0)

if "--extra" in sys.argv:
    shoot("qa_top.png", (0, 0.6, 40), (0, 0.6, 0), ortho=15)
    shoot("qa_side_rest.png", (-40, 0.6, 2.2), (0, 0.6, 2.2), ortho=11.5)
    shoot("qa_rear_three_quarter.png", orbit((0, -0.5, 1.8), 140, 25, 15), (0, -0.5, 1.8), lens=35)
    pose(38)
    shoot("qa_side_max.png", (-40, 0.6, 2.4), (0, 0.6, 2.4), ortho=11.5)
    pose(0)
