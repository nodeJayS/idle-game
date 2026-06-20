# Open the downloaded base-character .blend, report its contents, and render a
# preview so we can see what we're working with before customizing.
import bpy, os, mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
BLEND = os.path.join(HERE, "base", "base-character.blend")
PREVIEW = os.path.join(HERE, "out", "inspect.png")
os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene

print("=== OBJECTS ===")
meshes = []
for o in bpy.data.objects:
    extra = ""
    if o.type == 'MESH':
        meshes.append(o)
        o.hide_render = False
        o.hide_viewport = False
        extra = f" verts={len(o.data.vertices)} polys={len(o.data.polygons)} mats={[m.name for m in o.data.materials]}"
    print(f"  [{o.type}] {o.name}{extra}")

# combined world-space bbox of the meshes
mn = [1e9, 1e9, 1e9]; mx = [-1e9, -1e9, -1e9]
for o in meshes:
    for corner in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(corner)
        for i in range(3):
            mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
center = [(mn[i] + mx[i]) / 2 for i in range(3)]
dims = [mx[i] - mn[i] for i in range(3)]
print("=== BBOX ===")
print("  center", [round(c, 3) for c in center], "dims", [round(d, 3) for d in dims])

reach = max(dims) if dims else 2.0

# camera framing the character from the front (-Y), looking at its centre
target = bpy.data.objects.new("Target", None)
scene.collection.objects.link(target)
target.location = (center[0], center[1], center[2])
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
cam.location = (center[0] + reach * 0.5, center[1] - reach * 2.4, center[2] + reach * 0.25)
con = cam.constraints.new(type='TRACK_TO')
con.target = target
con.track_axis = 'TRACK_NEGATIVE_Z'
con.up_axis = 'UP_Y'
scene.camera = cam

scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
sh.show_shadows = True
sh.background_type = 'VIEWPORT'
sh.background_color = (0.72, 0.74, 0.77)
scene.render.resolution_x = 620
scene.render.resolution_y = 800
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
print("PREVIEW:", PREVIEW)
