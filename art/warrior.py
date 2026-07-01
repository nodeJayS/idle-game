# Chibi Warrior — scripted hero model, SMOOTH "toy" style: heroes are the only
# smooth/rounded thing in a faceted low-poly world, so they pop. Organic parts
# (head, hair, body, limbs) are smooth-shaded and beveled; metal (sword, shield
# boss) stays crisp.
#
#   blender -b --python art/warrior.py -- --renders <dir>
#   blender -b --python art/warrior.py -- --export <file.fbx>
#   ... --export <file.fbx> --skinned
#
# Conventions:
#  - Blender Z-up, character faces -Y; palette authored in sRGB and converted to
#    linear at material build (Blender Principled Base Color is linear).
#  - Export is FLAT: root-level meshes named "<joint>.<part>"; ModelHero.cs builds
#    the joint skeleton and reparents by prefix (see that file for why).

import sys
import bpy
import bmesh
from mathutils import Vector

# --- chibi metrics (shared skeleton — must match ModelHero.cs) -----------------
HIP = 0.42
TORSO_H = 0.50
HEAD_R = 0.28
SHOULDER_X = 0.28
SHOULDER_Z = HIP + TORSO_H * 0.80
ARM_LEN = 0.42
HIP_X = 0.13
HEAD_CZ = HIP + TORSO_H + 0.27 * 0.85

# --- palette (sRGB) -------------------------------------------------------------
COLORS = {
    "skin":  (0.96, 0.80, 0.64),
    "tunic": (0.15, 0.38, 0.88),
    "limb":  (0.11, 0.27, 0.66),
    "boot":  (0.40, 0.27, 0.14),
    "hair":  (0.46, 0.27, 0.11),
    "belt":  (0.25, 0.16, 0.09),
    "steel": (0.78, 0.81, 0.87),
    "dark":  (0.30, 0.31, 0.36),
    "wood":  (0.52, 0.34, 0.16),
    "eye":   (0.08, 0.07, 0.09),
    "white": (0.97, 0.97, 0.97),
}

_materials = {}

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def mat(name):
    if name in _materials:
        return _materials[name]
    c = tuple(srgb_to_linear(v) for v in COLORS[name])
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*c, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
    m.diffuse_color = (*c, 1.0)
    _materials[name] = m
    return m


PARTS = []

def register(obj, material, bone, smooth=True):
    obj.name = bone + "." + obj.name
    obj.data.materials.append(mat(material))
    for p in obj.data.polygons:
        p.use_smooth = smooth
    PARTS.append((obj, bone))
    return obj


def apply_mods(obj):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def box(name, size, loc, material, bone, taper_top=1.0, taper_bottom=1.0,
        bevel=0.03, seg=3, smooth=True):
    """Rounded slab: a tapered cube with beveled edges, smooth-shaded by default."""
    w, d, h = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= w
        v.co.y *= d
        v.co.z *= h
        s = taper_top if v.co.z > 0 else taper_bottom
        v.co.x *= s
        v.co.y *= s
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.location = loc
    bpy.context.scene.collection.objects.link(obj)
    if bevel > 0:
        b = obj.modifiers.new("Bevel", "BEVEL")
        b.width = min(bevel, min(w, d, h) * 0.45)
        b.segments = seg
        b.limit_method = "ANGLE"
        apply_mods(obj)
    return register(obj, material, bone, smooth)


def ball(name, radius, loc, material, bone, scale=(1, 1, 1), segs=24, rings=16,
         smooth=True):
    """Smooth UV sphere — round silhouette, unlike the faceted world icospheres."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings,
                                         radius=radius, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = name
    for v in obj.data.vertices:
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    obj.location = loc
    return register(obj, material, bone, smooth)


def build_hair(head_c):
    """Bowl cut with real thickness: trimmed sphere shell + solidify, smooth."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16,
                                         radius=HEAD_R * 1.09, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "hair"
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [v for v in bm.verts if v.co.z < min(0.055, -0.5 * v.co.y - 0.02)]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    s = obj.modifiers.new("Solid", "SOLIDIFY")
    s.thickness = 0.035
    s.offset = 1.0
    apply_mods(obj)
    obj.location = head_c + Vector((0, 0.015, 0.02))
    return register(obj, "hair", "head")


def build_sword():
    hand = Vector((SHOULDER_X, 0, SHOULDER_Z - ARM_LEN))
    box("grip", (0.05, 0.05, 0.14), hand + Vector((0, 0, -0.02)), "belt", "hand",
        bevel=0.012, seg=2, smooth=False)
    box("guard", (0.22, 0.06, 0.045), hand + Vector((0, 0, -0.10)), "dark", "hand",
        bevel=0.012, seg=2, smooth=False)
    box("blade", (0.075, 0.03, 0.50), hand + Vector((0, 0, -0.38)), "steel", "hand",
        bevel=0.01, seg=1, smooth=False)
    tip = box("tip", (0.075, 0.03, 0.09), hand + Vector((0, 0, -0.675)), "steel", "hand",
              taper_top=0.02, bevel=0, smooth=False)
    for v in tip.data.vertices:
        v.co.z = -v.co.z


def build_shield():
    """Round wooden buckler, smooth face, crisp steel boss."""
    c = Vector((-SHOULDER_X - 0.11, 0, SHOULDER_Z - ARM_LEN * 0.55))
    bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=0.16, depth=0.045,
                                        location=(0, 0, 0), rotation=(0, 1.5708, 0))
    obj = bpy.context.active_object
    obj.name = "shield"
    b = obj.modifiers.new("Bevel", "BEVEL")
    b.width = 0.015
    b.segments = 2
    apply_mods(obj)
    obj.location = c
    register(obj, "wood", "armL")
    ball("boss", 0.05, c + Vector((-0.025, 0, 0)), "steel", "armL", scale=(0.6, 1, 1))


def build_character():
    # Soft rounded torso + belt.
    box("torso", (0.50, 0.36, TORSO_H), (0, 0, HIP + TORSO_H / 2), "tunic", "body",
        taper_top=0.85, bevel=0.07, seg=4)
    box("belt", (0.505, 0.365, 0.07), (0, 0, HIP + 0.08), "belt", "body", bevel=0.02, seg=2)

    # Smooth ball head, thick bowl-cut hair, eyes with glints.
    head_c = Vector((0, 0, HEAD_CZ))
    ball("head", HEAD_R, head_c, "skin", "head")
    build_hair(head_c)
    for sx in (-1, 1):
        ball("eye", 0.068, head_c + Vector((sx * 0.105, -0.235, -0.025)),
             "eye", "head", scale=(0.85, 0.32, 1.30), segs=16, rings=12)
        ball("glint", 0.016, head_c + Vector((sx * 0.088, -0.266, 0.008)),
             "white", "head", segs=12, rings=8)

    # Soft limbs, ball fists.
    for sx, bone in ((-1, "armL"), (1, "armR")):
        box("arm" + bone[-1], (0.15, 0.15, ARM_LEN - 0.07),
            (sx * SHOULDER_X, 0, SHOULDER_Z - (ARM_LEN - 0.07) / 2), "limb", bone,
            bevel=0.06, seg=4)
        ball("fist" + bone[-1], 0.075, (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN),
             "skin", bone, segs=16, rings=12)
    for sx, bone in ((-1, "legL"), (1, "legR")):
        box("leg" + bone[-1], (0.16, 0.16, 0.30), (sx * HIP_X, 0, HIP - 0.15),
            "limb", bone, bevel=0.06, seg=4)
        box("boot" + bone[-1], (0.19, 0.24, 0.13), (sx * HIP_X, -0.025, 0.065),
            "boot", bone, bevel=0.05, seg=4)

    build_sword()
    build_shield()


# --- joints (shared chibi skeleton) ----------------------------------------------
BONES = {
    "body": ((0, 0, HIP), (0, 0, HIP + TORSO_H), None),
    "head": ((0, 0, HIP + TORSO_H), (0, 0, HEAD_CZ + HEAD_R), "body"),
    "armL": ((-SHOULDER_X, 0, SHOULDER_Z), (-SHOULDER_X, 0, SHOULDER_Z - ARM_LEN), "body"),
    "armR": ((SHOULDER_X, 0, SHOULDER_Z), (SHOULDER_X, 0, SHOULDER_Z - ARM_LEN), "body"),
    "legL": ((-HIP_X, 0, HIP), (-HIP_X, 0, 0.02), "body"),
    "legR": ((HIP_X, 0, HIP), (HIP_X, 0, 0.02), "body"),
    "hand": ((SHOULDER_X, 0, SHOULDER_Z - ARM_LEN),
             (SHOULDER_X, 0, SHOULDER_Z - ARM_LEN - 0.10), "armR"),
}


def build_rig_and_skin():
    arm_data = bpy.data.armatures.new("WarriorRig")
    arm_obj = bpy.data.objects.new("WarriorRig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in BONES.items():
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for obj, bone in PARTS:
        vg = obj.vertex_groups.new(name=bone)
        vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in PARTS:
        obj.select_set(True)
    target = PARTS[0][0]
    target.name = "Warrior"
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()
    target.parent = arm_obj
    target.modifiers.new("Armature", "ARMATURE").object = arm_obj


# --- render / export ---------------------------------------------------------------

def setup_scene():
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

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.86, 0.88, 0.91, 1.0)
        bg.inputs[1].default_value = 0.45

    target = bpy.data.objects.new("LookAt", None)
    target.location = (0, 0, 0.72)
    scene.collection.objects.link(target)

    def tracked(obj, loc):
        obj.location = loc
        scene.collection.objects.link(obj)
        obj.constraints.new("TRACK_TO").target = target
        return obj

    key = bpy.data.lights.new("Key", "SUN")
    key.energy = 2.4
    tracked(bpy.data.objects.new("Key", key), (2.5, -2.0, 4.0))
    fill = bpy.data.lights.new("Fill", "SUN")
    fill.energy = 1.0
    tracked(bpy.data.objects.new("Fill", fill), (-3.0, -1.0, 2.0))

    cam = bpy.data.cameras.new("Cam")
    cam_obj = tracked(bpy.data.objects.new("Cam", cam), (1.55, -1.9, 1.35))
    scene.camera = cam_obj
    return cam_obj


def render_views(cam_obj, out_dir):
    views = {
        "warrior_f34": (1.55, -1.9, 1.35),
        "warrior_front": (0.0, -2.4, 1.0),
        "warrior_b34": (-1.55, 1.9, 1.35),
    }
    scene = bpy.context.scene
    for name, loc in views.items():
        cam_obj.location = loc
        scene.render.filepath = "%s/%s.png" % (out_dir, name)
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def export_fbx(path, skinned):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"} if skinned else {"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=not skinned,
        add_leaf_bones=False,
        bake_anim=False,
    )
    print("exported", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)

    build_character()
    skinned = "--skinned" in argv
    if skinned:
        build_rig_and_skin()

    if "--export" in argv:
        export_fbx(argv[argv.index("--export") + 1], skinned)
    if "--renders" in argv:
        cam = setup_scene()
        render_views(cam, argv[argv.index("--renders") + 1])


main()
