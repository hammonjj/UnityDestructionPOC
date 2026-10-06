"""Render preview PNGs of the excavator. Ground, lights, cameras and any re-posing live only in this
session; the .blend is never saved.

    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/render_excavator_previews.py [-- --extra]

Blender axes: the machine faces +Y; design y (left) is Blender -X.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Excavator")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "Excavator.blend"))
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 800
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "Standard"

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 5, 0))
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
O = bpy.data.objects
ATTS = ["Att_Crusher", "Att_Shear", "Att_Breaker", "Att_Grapple"]


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


def show_atts(visible):
    for a in ATTS:
        for ob in [O[a]] + list(O[a].children_recursive):
            ob.hide_render = a not in visible


def mount(att):
    """Drop an attachment onto Exc_Wrist by world pose (what Unity does)."""
    O[att].matrix_world = O["Exc_Wrist"].matrix_world.copy()
    bpy.context.view_layer.update()


home = {a: O[a].matrix_world.copy() for a in ATTS}

# 35 deg overhead three-quarter, crusher mounted
show_atts({"Att_Crusher"})
mount("Att_Crusher")
d = 14.5
el = math.radians(35)
az = math.radians(-60)                       # from front-left (machine faces +Y; design-left is -X)
tgt = Vector((0, 2.4, 2.0))
shoot("preview_three_quarter.png",
      tgt + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * d,
      tgt, lens=35)
# side ortho (design left side), machine only
show_atts(set())
shoot("preview_side.png", (-40, 2.5, 3.0), (0, 2.5, 3.0), ortho=11)

# all four attachments, laid out along design x for a clean side view
show_atts(set(ATTS))
for i, a in enumerate(ATTS):
    O[a].matrix_world = Matrix.Translation((0, 10 + i * 2.3, 3.0))
bpy.context.view_layer.update()
shoot("preview_attachments.png", (-14, 13.45, 3.6), (0, 13.45, 1.75), lens=50)

if "--extra" in sys.argv:
    for a in ATTS:
        O[a].matrix_world = home[a]
    show_atts(set())
    shoot("qa_front.png", (0, -40, 3.0), (0, 0, 3.0), ortho=9)
    shoot("qa_top.png", (0, 2.4, 40), (0, 2.4, 0), ortho=14)
    show_atts(set(ATTS))
    shoot("qa_attachments_front.png", (0, 40, 1.8), (0, 9, 1.8), ortho=10)
    for a in ATTS:
        show_atts({a})
        mount(a)
        shoot("qa_wrist_%s.png" % a[4:].lower(), (-9, 5.99, 2.7), (0, 5.99, 2.0), ortho=5)
        O[a].matrix_world = home[a]
