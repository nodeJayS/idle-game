# Customize the downloaded low-poly base character into the Fire Wizard (Magician).
# Crimson robe (body + a flared robe skirt over the legs), hood, gold sash, bare
# hands, and a fire-orb staff in the right hand. Renders a preview. Never writes
# the source .blend (open -> edit in memory -> render).
import bpy, os, mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
BLEND = os.path.join(HERE, "base", "base-character.blend")  # pristine source (never written)
PREVIEW = os.path.join(HERE, "out", "mage_preview.png")
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

# Fire-wizard palette.
SKIN, ROBE, BOOT, HOOD, SASH = range(5)
mats = [mat("Skin", (1.00, 0.82, 0.64)), mat("Robe", (0.66, 0.13, 0.11)),
        mat("Boot", (0.30, 0.20, 0.12)), mat("Hood", (0.45, 0.09, 0.08)),
        mat("Sash", (0.96, 0.62, 0.16))]
obj.data.materials.clear()
for m in mats:
    obj.data.materials.append(m)

vg = [g.name for g in obj.vertex_groups]

def region(gname):
    n = gname.lower()
    if any(k in n for k in ("foot", "toe", "heel")): return BOOT
    if any(k in n for k in ("hand", "finger", "palm", "wrist", "pinky")): return SKIN   # bare hands
    if any(k in n for k in ("head", "face", "neck", "jaw")): return SKIN                 # face (hood added below)
    # everything else (torso, arms incl. forearms=long sleeves, legs/hips) = robe
    return ROBE

me = obj.data
M = obj.matrix_world

vdom = []
for v in me.vertices:
    if v.groups:
        g = max(v.groups, key=lambda gv: gv.weight)
        vdom.append(vg[g.group] if g.weight > 0 else "")
    else:
        vdom.append("")
vreg = [region(n) if n else ROBE for n in vdom]

zmin = min((M @ v.co).z for v in me.vertices)
zmax = max((M @ v.co).z for v in me.vertices)
Hz = zmax - zmin
head_z = [(M @ me.vertices[i].co).z for i in range(len(me.vertices)) if "head" in vdom[i].lower()]
hood_z = (min(head_z) + 0.42 * (max(head_z) - min(head_z))) if head_z else 1e9
waist = zmin + 0.52 * Hz

for p in me.polygons:
    cz = (M @ p.center).z
    cx = (M @ p.center).x
    counts = {}
    for vi in p.vertices:
        counts[vreg[vi]] = counts.get(vreg[vi], 0) + 1
    r = max(counts, key=counts.get)
    head_face = sum(1 for vi in p.vertices if "head" in vdom[vi].lower()) > len(p.vertices) / 2
    if head_face and cz > hood_z:
        r = HOOD
    elif r == ROBE and abs(cz - waist) < 0.030 * Hz and abs(cx) < 0.22:
        r = SASH
    p.material_index = r

extras = []   # meshes to add (robe skirt + staff)
def add_active(name, material):
    o = bpy.context.active_object
    o.name = name
    o.data.materials.clear(); o.data.materials.append(material)
    extras.append(o); return o

# flared robe skirt (cone) over the legs: wide at the hem, narrow at the waist
bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.36, radius2=0.17,
                                depth=(waist - (zmin + 0.06 * Hz)),
                                location=(0, 0, (waist + zmin + 0.06 * Hz) / 2))
skirt = add_active("RobeSkirt", mats[ROBE])
skirt.scale = (1.0, 0.85, 1.0)  # slightly flatter front-to-back

# fire-orb staff in the right hand (held upright)
hand_pts = [M @ me.vertices[i].co for i in range(len(me.vertices))
            if "hand.r" in vdom[i].lower() or ("hand" in vdom[i].lower() and (M @ me.vertices[i].co).x > 0)]
hand_c = (sum(hand_pts, mathutils.Vector((0, 0, 0))) / len(hand_pts)) if hand_pts else mathutils.Vector((0.6, 0, 1.2))
WOOD = mat("Staff", (0.34, 0.22, 0.12)); ORB = mat("FireOrb", (1.00, 0.45, 0.10)); ORBG = mat("OrbGlow", (1.00, 0.78, 0.30))

bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.028, depth=1.30,
                                    location=hand_c + mathutils.Vector((0, 0, 0.40)))
add_active("StaffPole", WOOD)
bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=9, radius=0.085,
                                     location=hand_c + mathutils.Vector((0, 0, 1.06)))
orb = add_active("FireOrb", ORB)
for pp in orb.data.polygons: pp.use_smooth = True

# ---- clean render: character + extras only ---------------------------------
show = {obj, *extras}
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

# ---- export a static, Unity-oriented FBX (mesh + robe skirt + staff) -------
FBX = os.path.normpath(os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "Characters", "Mage.fbx"))
os.makedirs(os.path.dirname(FBX), exist_ok=True)
for o in bpy.data.objects:
    o.select_set(o in show)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'MESH'},
                         use_mesh_modifiers=True, apply_unit_scale=True, bake_space_transform=True,
                         mesh_smooth_type='FACE', path_mode='COPY')
print("FBX:", FBX)
