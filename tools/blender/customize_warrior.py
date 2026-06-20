# Customize the downloaded low-poly base character into the Warrior: colour the
# single-material body by region using the rig's vertex groups (head/hands->skin,
# torso+upper arms->shirt, legs/hips->pants, feet->boots). Renders a preview.
import bpy, os, mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
BLEND = os.path.join(HERE, "base", "base-character.blend")  # pristine source (never written)
PREVIEW = os.path.join(HERE, "out", "warrior_preview.png")
os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene

obj = bpy.data.objects.get("Cube")
assert obj and obj.type == 'MESH', "character mesh 'Cube' not found"

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Roughness"].default_value = 0.85
    m.diffuse_color = (*rgb, 1.0)
    return m

SKIN, SHIRT, PANTS, BOOT = 0, 1, 2, 3
# Brighter, livelier hero palette: vivid blue tunic + warm gold-tan trousers
# (complementary), saddle-brown boots, warm skin.
mats = [mat("Skin", (1.00, 0.82, 0.64)), mat("Shirt", (0.18, 0.58, 1.00)),
        mat("Pants", (0.93, 0.69, 0.24)), mat("Boot", (0.58, 0.36, 0.18))]
obj.data.materials.clear()
for m in mats:
    obj.data.materials.append(m)

vg = [g.name for g in obj.vertex_groups]
print("VGROUPS:", len(vg), vg[:40])

def region(gname):
    n = gname.lower()
    if any(k in n for k in ("foot", "toe", "heel")): return BOOT
    if any(k in n for k in ("thigh", "shin", "leg", "hip", "pelvis", "knee", "butt")): return PANTS
    if any(k in n for k in ("forearm", "hand", "finger", "palm", "wrist")): return SKIN
    if any(k in n for k in ("spine", "chest", "breast", "shoulder", "upper_arm", "clavicle", "collar", "torso", "arm")): return SHIRT
    if any(k in n for k in ("head", "face", "neck", "jaw")): return SKIN
    return SKIN

# dominant region per vertex, then majority vote per polygon
me = obj.data
zs = [(obj.matrix_world @ v.co).z for v in me.vertices]
zmin, zmax = min(zs), max(zs); H = max(1e-6, zmax - zmin)

def vert_region(v):
    if v.groups:
        g = max(v.groups, key=lambda gv: gv.weight)
        if g.weight > 0:
            return region(vg[g.group])
    # fallback by height if unweighted
    t = ((obj.matrix_world @ v.co).z - zmin) / H
    return BOOT if t < 0.07 else PANTS if t < 0.50 else SHIRT if t < 0.80 else SKIN

vreg = [vert_region(v) for v in me.vertices]
for p in me.polygons:
    counts = {}
    for vi in p.vertices:
        counts[vreg[vi]] = counts.get(vreg[vi], 0) + 1
    p.material_index = max(counts, key=counts.get)

# ---- clean render: only the character mesh visible -------------------------
for o in bpy.data.objects:
    o.hide_render = (o is not obj)

mn = [1e9]*3; mx = [-1e9]*3
for c in obj.bound_box:
    w = obj.matrix_world @ mathutils.Vector(c)
    for i in range(3):
        mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
center = [(mn[i]+mx[i])/2 for i in range(3)]
reach = mx[2]-mn[2]

target = bpy.data.objects.new("T", None); scene.collection.objects.link(target)
target.location = center
cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data)
scene.collection.objects.link(cam)
cam.location = (center[0] + reach*0.30, center[1] - reach*1.9, center[2] + reach*0.12)
con = cam.constraints.new(type='TRACK_TO'); con.target = target
con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'
scene.camera = cam

scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = True
sh.background_type = 'VIEWPORT'; sh.background_color = (0.72, 0.74, 0.77)
scene.render.resolution_x = 600; scene.render.resolution_y = 800
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
print("PREVIEW:", PREVIEW)
