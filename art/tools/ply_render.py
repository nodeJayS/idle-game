"""Render a PLY mesh (front / side / 3-4 views), auto-framed. Usage:
blender -b --python ply_render.py -- <in.ply> <outdir> <basename>
"""
import sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
ply, outdir, base = argv[0], argv[1], argv[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
try:
    bpy.ops.wm.ply_import(filepath=ply)
except AttributeError:
    bpy.ops.import_mesh.ply(filepath=ply)
obj = bpy.context.selected_objects[0] if bpy.context.selected_objects else bpy.context.active_object

mat = bpy.data.materials.new("clay")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.80, 0.62, 0.52, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
obj.data.materials.append(mat)

# frame from bounds
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
center = (lo + hi) / 2
size = max(hi - lo)
print("BBOX lo=%s hi=%s size=%.3f" % (tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi), size))

scene = bpy.context.scene
for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.render.resolution_x = scene.render.resolution_y = 900
try:
    scene.view_settings.view_transform = "Standard"
except TypeError:
    pass
world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.85, 0.87, 0.90, 1.0)
    bg.inputs[1].default_value = 0.5

target = bpy.data.objects.new("LookAt", None)
target.location = center
scene.collection.objects.link(target)

def tracked(o, loc):
    o.location = loc
    scene.collection.objects.link(o)
    o.constraints.new("TRACK_TO").target = target
    return o

key = bpy.data.lights.new("Key", "SUN")
key.energy = 2.5
tracked(bpy.data.objects.new("Key", key), center + Vector((size, -size, size * 1.5)))
fill = bpy.data.lights.new("Fill", "SUN")
fill.energy = 1.0
tracked(bpy.data.objects.new("Fill", fill), center + Vector((-size, -size * 0.5, size * 0.5)))

cam = bpy.data.cameras.new("Cam")
cam_obj = tracked(bpy.data.objects.new("Cam", cam), center)
scene.camera = cam_obj
d = size * 2.2
views = {
    "front": Vector((0, -d, 0)),
    "f34":   Vector((d * 0.7, -d * 0.7, d * 0.25)),
    "side":  Vector((d, 0, 0)),
}
for name, off in views.items():
    cam_obj.location = center + off
    scene.render.filepath = "%s/%s_%s.png" % (outdir, base, name)
    bpy.ops.render.render(write_still=True)
    print("rendered", scene.render.filepath)
