# Chibi Warrior — scripted low-poly hero model (Tunic / MapleStory-2 chibi style).
#
# This script IS the model source: run it headless to rebuild the mesh, rig and
# renders from nothing. No .blend file is authoritative.
#
#   blender -b --python art/warrior.py -- --renders <dir>        preview PNGs
#   blender -b --python art/warrior.py -- --export <file.fbx>    FBX for Unity
#   ... --export <file.fbx> --skinned                            armature + skin variant
#
# Conventions:
#  - Blender Z-up, character faces -Y (Blender "front"); FBX export converts for Unity.
#  - Proportions mirror Assets/Game/ChibiHero.cs (~1.4 units tall, head R 0.27) so the
#    imported model drops into the same camera/world scale as the code-built chibis.
#  - Default export is FLAT: one root-level mesh per rigid part, named
#    "<joint>.<part>" (e.g. armL.box, hand.blade), verts in character space.
#    ModelHero.cs builds the joint skeleton in Unity (same layout as ChibiHero.cs)
#    and reparents parts by name prefix — this sidesteps the FBX axis-conversion
#    rotations that a transform hierarchy would import with, which would break
#    ChibiAnimator's absolute localRotation writes. --skinned exports an
#    armature+skin variant for when a hero ever needs real (bendy) deformation.

import sys
import bpy
import bmesh
from mathutils import Vector

# --- chibi metrics (match ChibiHero.cs) --------------------------------------
HIP = 0.42          # top of the legs = body pivot
TORSO_H = 0.50
HEAD_R = 0.27
SHOULDER_X = 0.28
SHOULDER_Z = HIP + TORSO_H * 0.80
ARM_LEN = 0.42
HIP_X = 0.13
HEAD_CZ = HIP + TORSO_H + HEAD_R * 0.85   # head sphere centre ~1.15

# --- palette (warrior: blue tunic, brown boots — same as ChibiHero) -----------
COLORS = {
    "skin":  (0.95, 0.80, 0.66),
    "tunic": (0.20, 0.40, 0.78),
    "limb":  (0.17, 0.32, 0.62),
    "boot":  (0.30, 0.22, 0.15),
    "hair":  (0.36, 0.24, 0.14),
    "belt":  (0.16, 0.12, 0.09),
    "steel": (0.62, 0.65, 0.71),
    "dark":  (0.33, 0.33, 0.38),
    "wood":  (0.42, 0.29, 0.17),
    "eye":   (0.09, 0.08, 0.10),
}

_materials = {}

def mat(name):
    if name in _materials:
        return _materials[name]
    c = COLORS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*c, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
    m.diffuse_color = (*c, 1.0)
    _materials[name] = m
    return m


PARTS = []  # (object, bone_name)

def register(obj, material, bone):
    obj.name = bone + "." + obj.name   # joint prefix — ModelHero.cs parents by this
    obj.data.materials.append(mat(material))
    for p in obj.data.polygons:
        p.use_smooth = False
    PARTS.append((obj, bone))
    return obj


def box(name, size, loc, material, bone, taper_top=1.0):
    """Axis-aligned box; taper_top scales the top face in X/Y (frustum torso etc.)."""
    w, d, h = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= w
        v.co.y *= d
        v.co.z *= h
        if v.co.z > 0:
            v.co.x *= taper_top
            v.co.y *= taper_top
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.location = loc
    bpy.context.scene.collection.objects.link(obj)
    return register(obj, material, bone)


def sphere(name, radius, loc, material, bone, subdiv=2, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=radius, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = name
    for v in obj.data.vertices:
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    obj.location = loc
    return register(obj, material, bone)


def build_hair(head_c):
    """Skullcap: an icosphere shell trimmed along a tilted plane — high fringe in
    front (clear of the eyes), low nape at the back."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=HEAD_R * 1.10, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "hair"
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [v for v in bm.verts if v.co.z < min(0.06, -0.5 * v.co.y - 0.02)]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.location = head_c + Vector((0, 0.015, 0.02))
    return register(obj, "hair", "head")


def build_sword():
    """Blade points down out of the fist, like the code-built chibi."""
    hand = Vector((SHOULDER_X, 0, SHOULDER_Z - ARM_LEN))
    box("grip", (0.05, 0.05, 0.14), hand + Vector((0, 0, -0.02)), "belt", "hand")
    box("guard", (0.22, 0.06, 0.045), hand + Vector((0, 0, -0.10)), "dark", "hand")
    box("blade", (0.075, 0.03, 0.50), hand + Vector((0, 0, -0.38)), "steel", "hand")
    # pointed tip: a tiny pyramid (fully tapered box) flipped downwards
    tip = box("tip", (0.075, 0.03, 0.09), hand + Vector((0, 0, -0.675)), "steel", "hand",
              taper_top=0.02)
    for v in tip.data.vertices:      # flip so the point faces down
        v.co.z = -v.co.z


def build_shield():
    """Round wooden buckler with a steel boss, strapped to the left forearm."""
    c = Vector((-SHOULDER_X - 0.10, 0, SHOULDER_Z - ARM_LEN * 0.55))
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.15, depth=0.04,
                                        location=(0, 0, 0), rotation=(0, 1.5708, 0))
    obj = bpy.context.active_object
    obj.name = "shield"
    obj.location = c
    register(obj, "wood", "armL")
    sphere("boss", 0.05, c + Vector((-0.025, 0, 0)), "steel", "armL", subdiv=1,
           scale=(0.6, 1, 1))


def build_character():
    # Torso: plump frustum (narrower shoulders = chibi silhouette), belt at the waist.
    box("torso", (0.50, 0.36, TORSO_H), (0, 0, HIP + TORSO_H / 2), "tunic", "body",
        taper_top=0.85)
    box("belt", (0.53, 0.39, 0.06), (0, 0, HIP + 0.05), "belt", "body")

    # Big faceted head + hair + eyes (face towards -Y).
    head_c = Vector((0, 0, HEAD_CZ))
    sphere("head", HEAD_R, head_c, "skin", "head")
    build_hair(head_c)
    for sx in (-1, 1):
        sphere("eye", 0.055, head_c + Vector((sx * 0.105, -0.225, -0.03)),
               "eye", "head", subdiv=2, scale=(0.8, 0.3, 1.35))

    # Arms hang from the shoulders; skin-ball fists at the wrists.
    for sx, bone in ((-1, "armL"), (1, "armR")):
        box("arm" + bone[-1], (0.15, 0.15, ARM_LEN - 0.07),
            (sx * SHOULDER_X, 0, SHOULDER_Z - (ARM_LEN - 0.07) / 2), "limb", bone)
        sphere("fist" + bone[-1], 0.075, (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN),
               "skin", bone, subdiv=1)

    # Legs + chunky boots with a toe.
    for sx, bone in ((-1, "legL"), (1, "legR")):
        box("leg" + bone[-1], (0.16, 0.16, 0.30), (sx * HIP_X, 0, HIP - 0.15), "limb", bone)
        box("boot" + bone[-1], (0.19, 0.24, 0.13), (sx * HIP_X, -0.025, 0.065), "boot", bone)

    build_sword()
    build_shield()


# --- joints ---------------------------------------------------------------------
# Names + positions match the joints ChibiAnimator drives on the code-built puppet.
# Used as empty positions (default export) or bone head/tails (--skinned).
BONES = {
    # name:     (head, tail, parent)
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

    # Rigid skin: each part's verts weighted 100% to its bone, then join to one mesh.
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
    return arm_obj, target


# --- render / export ------------------------------------------------------------

def setup_scene():
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = scene.render.resolution_y = 900
    try:  # AgX (the default) washes out flat low-poly colours; Standard keeps them true
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
        "warrior_f34": (1.55, -1.9, 1.35),    # front three-quarter
        "warrior_front": (0.0, -2.4, 1.0),
        "warrior_b34": (-1.55, 1.9, 1.35),    # back three-quarter
    }
    scene = bpy.context.scene
    for name, loc in views.items():
        cam_obj.location = loc
        scene.render.filepath = f"{out_dir}/{name}.png"
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def export_fbx(path, skinned):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"} if skinned else {"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",   # avoids the 100x scale factor in Unity
        bake_space_transform=not skinned,        # bakes Z-up->Y-up into the vert data
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

    if "--export" in argv:  # export BEFORE render setup adds camera/light objects
        export_fbx(argv[argv.index("--export") + 1], skinned)
    if "--renders" in argv:
        cam = setup_scene()
        render_views(cam, argv[argv.index("--renders") + 1])


main()
