# Customize the downloaded low-poly base character into the Warrior. Colours the
# single-material body by region using the rig's vertex groups, adds a hair cap
# (top of the head), a waist belt, and a sword in the right hand. Renders a
# preview. Never writes the source .blend (open -> edit in memory -> render).
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

# Brighter, livelier hero palette.
SKIN, SHIRT, PANTS, BOOT, HAIR, BELT = range(6)
mats = [mat("Skin",  (1.00, 0.82, 0.64)), mat("Shirt", (0.18, 0.58, 1.00)),
        mat("Pants", (0.93, 0.69, 0.24)), mat("Boot",  (0.45, 0.28, 0.15)),
        mat("Hair",  (0.40, 0.24, 0.12)), mat("Belt",  (0.32, 0.20, 0.11))]
obj.data.materials.clear()
for m in mats:
    obj.data.materials.append(m)

vg = [g.name for g in obj.vertex_groups]

def region(gname):
    n = gname.lower()
    if any(k in n for k in ("foot", "toe", "heel")): return BOOT
    if any(k in n for k in ("thigh", "shin", "leg", "hip", "pelvis", "knee", "butt")): return PANTS
    if any(k in n for k in ("forearm", "hand", "finger", "palm", "wrist", "pinky")): return SKIN
    if any(k in n for k in ("spine", "chest", "breast", "shoulder", "upper_arm", "clavicle", "collar", "torso", "arm")): return SHIRT
    if any(k in n for k in ("head", "face", "neck", "jaw")): return SKIN
    return SKIN

me = obj.data
M = obj.matrix_world

# dominant vertex group (name) per vertex
vdom = []
for v in me.vertices:
    if v.groups:
        g = max(v.groups, key=lambda gv: gv.weight)
        vdom.append(vg[g.group] if g.weight > 0 else "")
    else:
        vdom.append("")
vreg = [region(n) if n else SKIN for n in vdom]

# head Z range (for the hair cap) and waist Z (for the belt)
head_z = [(M @ me.vertices[i].co).z for i in range(len(me.vertices)) if "head" in vdom[i].lower()]
hair_z = (min(head_z) + 0.50 * (max(head_z) - min(head_z))) if head_z else 1e9
shirt_lo = [ (M @ me.vertices[i].co).z for i in range(len(me.vertices)) if vreg[i] == SHIRT and abs((M @ me.vertices[i].co).x) < 0.18 ]
pants_hi = [ (M @ me.vertices[i].co).z for i in range(len(me.vertices)) if vreg[i] == PANTS ]
waist = (min(shirt_lo) + max(pants_hi)) / 2 if shirt_lo and pants_hi else -1e9
Hz = max((M @ v.co).z for v in me.vertices) - min((M @ v.co).z for v in me.vertices)

for p in me.polygons:
    cz = (M @ p.center).z
    cx = (M @ p.center).x
    counts = {}
    for vi in p.vertices:
        counts[vreg[vi]] = counts.get(vreg[vi], 0) + 1
    r = max(counts, key=counts.get)
    head_face = sum(1 for vi in p.vertices if "head" in vdom[vi].lower()) > len(p.vertices) / 2
    if head_face and cz > hair_z:
        r = HAIR
    elif r in (SHIRT, PANTS) and abs(cz - waist) < 0.028 * Hz and abs(cx) < 0.22:
        r = BELT
    p.material_index = r

# ---- sword in the right hand ----------------------------------------------
hand_pts = [M @ me.vertices[i].co for i in range(len(me.vertices))
            if "hand.r" in vdom[i].lower() or ("hand" in vdom[i].lower() and (M @ me.vertices[i].co).x > 0)]
hand_c = (sum(hand_pts, mathutils.Vector((0, 0, 0))) / len(hand_pts)) if hand_pts else mathutils.Vector((0.6, 0, 1.2))

BLADE = mat("Blade", (0.82, 0.85, 0.90)); GUARDM = mat("Guard", (0.50, 0.40, 0.18)); GRIPM = mat("Grip", (0.28, 0.16, 0.09))
sword = []
def wbox(name, offset, dims, material):
    c = hand_c + mathutils.Vector(offset)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=c)
    o = bpy.context.active_object
    o.name = name; o.scale = dims
    o.data.materials.clear(); o.data.materials.append(material)
    sword.append(o); return o

# blade hangs downward (-Z) from the hand; crossguard across X
wbox("Pommel", (0, 0, 0.09), (0.05, 0.05, 0.05), GUARDM)
wbox("Grip",   (0, 0, 0.00), (0.04, 0.04, 0.16), GRIPM)
wbox("Guard",  (0, 0, -0.10), (0.24, 0.06, 0.05), GUARDM)
wbox("Blade",  (0, 0, -0.52), (0.06, 0.025, 0.78), BLADE)

# ---- clean render: character mesh + sword only -----------------------------
show = {obj, *sword}
for o in bpy.data.objects:
    o.hide_render = o not in show

mn = [1e9]*3; mx = [-1e9]*3
for s in show:
    for c in s.bound_box:
        w = s.matrix_world @ mathutils.Vector(c)
        for i in range(3):
            mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
center = [(mn[i]+mx[i])/2 for i in range(3)]
reach = max(mx[i]-mn[i] for i in range(3))

target = bpy.data.objects.new("T", None); scene.collection.objects.link(target); target.location = center
cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data); scene.collection.objects.link(cam)
cam.location = (center[0] + reach*0.28, center[1] - reach*1.7, center[2] + reach*0.10)
con = cam.constraints.new(type='TRACK_TO'); con.target = target
con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'
scene.camera = cam

scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = True
sh.background_type = 'VIEWPORT'; sh.background_color = (0.72, 0.74, 0.77)
scene.render.resolution_x = 640; scene.render.resolution_y = 820
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
print("PREVIEW:", PREVIEW)

# ---- export a static, Unity-oriented FBX (mesh + sword) --------------------
FBX = os.path.normpath(os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "Characters", "Warrior.fbx"))
os.makedirs(os.path.dirname(FBX), exist_ok=True)
for o in bpy.data.objects:
    o.select_set(o in show)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'MESH'},
                         use_mesh_modifiers=True, apply_unit_scale=True, bake_space_transform=True,
                         mesh_smooth_type='FACE', path_mode='COPY')
print("FBX:", FBX)

# ---- weapon-less body in T-pose, for Mixamo auto-rigging --------------------
MIX = os.path.join(HERE, "out", "mixamo", "Warrior_base.fbx")
os.makedirs(os.path.dirname(MIX), exist_ok=True)
for o in bpy.data.objects:
    o.select_set(o is obj)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=MIX, use_selection=True, object_types={'MESH'},
                         use_mesh_modifiers=True, apply_unit_scale=True,
                         mesh_smooth_type='FACE', path_mode='COPY')
print("MIXAMO:", MIX)
