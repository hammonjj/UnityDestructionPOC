"""Render preview PNGs of the skid-steer. Ground, lights, cameras and any re-posing live only in this
session; the .blend is never saved.

    /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/blender/render_skid_steer_previews.py

Blender axes: the machine faces +Y; design y (left) is Blender -X.
Posing: SS_LiftArm.rotation_euler.x = lift delta from rest (+ raises); SS_Bucket.rotation_euler.x
(+ curls back, - dumps; the handoff contract angle is the negative of this). Rams are re-aimed here the
way Unity is expected to: each half rotates + stretches along its pin-to-pin axis.
"""
import math
import os

import bpy
from mathutils import Euler, Matrix, Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "SkidSteer")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "SkidSteer.blend"))
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 800
sc.render.image_settings.file_format = "PNG"
sc.view_settings.view_transform = "Standard"
O = bpy.data.objects

for ob in O:  # colliders / cavity volume are hidden by Unity; hide them in the renders too
    if ob.name.startswith(("Col_", "Vol_")):
        ob.hide_render = True

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 1, 0))
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
bpy.context.view_layer.update()

RAMS = [("SS_LiftRam_L", "SS_LiftRamRod_L"), ("SS_LiftRam_R", "SS_LiftRamRod_R"), ("SS_TiltRam", "SS_TiltRamRod")]
rest = {}
for b, r in RAMS:
    rest[b] = (O[b].matrix_world.copy(), (O[r].matrix_world.translation - O[b].matrix_world.translation))
    rest[r] = (O[r].matrix_world.copy(), (O[b].matrix_world.translation - O[r].matrix_world.translation))


PARENTS = {n: O[n].parent for b, r in RAMS for n in (b, r)}
LOCS = {n: O[n].location.copy() for b, r in RAMS for n in (b, r)}


def apply_pose(lift, rot_x):
    for n, par in PARENTS.items():
        O[n].parent = par
        O[n].matrix_parent_inverse = Matrix.Identity(4)
        O[n].location = LOCS[n]
        O[n].rotation_euler = Euler((0, 0, 0))
        O[n].scale = (1, 1, 1)
    O["SS_LiftArm"].rotation_euler = Euler((math.radians(lift), 0, 0))
    O["SS_Bucket"].rotation_euler = Euler((math.radians(rot_x), 0, 0))
    bpy.context.view_layer.update()
    for b, r in RAMS:
        pb = O[b].parent.matrix_world @ O[b].location
        pr = O[r].parent.matrix_world @ O[r].location
        for name, own_p, other_p in ((b, pb, pr), (r, pr, pb)):
            m0, d0 = rest[name]
            d1 = other_p - own_p
            q = d0.normalized().rotation_difference(d1.normalized()).to_matrix().to_4x4()
            n = d0.normalized()
            s = d1.length / d0.length
            S = Matrix.Identity(4)
            for i in range(3):
                for j in range(3):
                    S[i][j] += (s - 1) * n[i] * n[j]
            O[name].parent = None
            O[name].matrix_world = Matrix.Translation(own_p) @ q @ S
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


def orbit(az_deg, el_deg, d, tgt):
    az, el = math.radians(az_deg), math.radians(el_deg)
    return tgt + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * d


# three-quarter overhead (37 deg elevation, like the game camera), bucket raised and curled back
apply_pose(35.0, 30.0)
tgt = Vector((0.2, 0.6, 1.5))
shoot("preview_three_quarter.png", orbit(-50, 37, 7.2, tgt), tgt, lens=35)
# same view at rest (bucket on the ground)
apply_pose(0.0, 0.0)
tgt = Vector((0, 0.4, 0.9))
shoot("preview_three_quarter_rest.png", orbit(-50, 37, 6.0, tgt), tgt, lens=35)
# side ortho (design left = Blender -X), rest and raised
shoot("preview_side.png", (-40, 0.4, 1.3), (0, 0.4, 1.3), ortho=5.6)
apply_pose(LIFT_MAX := 66.276, -55.0)
shoot("preview_side_raised_dump.png", (-40, 0.4, 1.9), (0, 0.4, 1.9), ortho=7.0)
apply_pose(0.0, 0.0)
# front ortho (looking back along -Y) and top
shoot("preview_front.png", (0, 40, 1.2), (0, 0, 1.2), ortho=3.6)
shoot("preview_top.png", (0, 0.4, 40), (0, 0.4, 0), ortho=5.6)
