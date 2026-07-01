# Chibi Valkyrie — scripted low-poly hero from a 2D reference (MS2-style hooded
# shield knight: leather hood + gold trim, teal plume, big layered anime eyes,
# blue kite shield, flowing back-locks). Original interpretation, not a replica.
#
# SHIPS AS THE WARRIOR: exported to Resources/Models/warrior_basic.fbx (visual
# upgrade over art/warrior.py's male chibi, which remains as an alternate model).
#
#   blender -b --python art/valkyrie.py -- --renders <dir>
#   blender -b --python art/valkyrie.py -- --export <file.fbx>
#
# Same conventions as art/warrior.py (see there): Z-up, faces -Y, sRGB palette,
# flat "<joint>.<part>" export onto the shared chibi skeleton (BONES table).

import sys
import bpy
import bmesh
from mathutils import Vector

# --- shared chibi skeleton metrics (must match ModelHero.cs) -------------------
HIP = 0.42
TORSO_H = 0.50
HEAD_R = 0.31          # bigger head than the warrior — MS2 proportions
SHOULDER_X = 0.28
SHOULDER_Z = HIP + TORSO_H * 0.80
ARM_LEN = 0.42
HIP_X = 0.13
HEAD_CZ = HIP + TORSO_H + 0.27 * 0.85   # head JOINT stays where the skeleton puts it

# --- palette (sRGB — converted to linear at material build) --------------------
COLORS = {
    "skin":    (0.99, 0.86, 0.76),
    "hair":    (0.55, 0.30, 0.11),
    "hood":    (0.35, 0.19, 0.12),
    "leather": (0.30, 0.20, 0.13),
    "cloth":   (0.58, 0.36, 0.20),
    "gold":    (0.93, 0.71, 0.18),
    "blue":    (0.16, 0.33, 0.82),
    "teal":    (0.25, 0.78, 0.92),
    "white":   (0.97, 0.97, 0.97),
    "irisblue": (0.13, 0.45, 0.95),
    "pupil":   (0.06, 0.06, 0.09),
    "steel":   (0.80, 0.83, 0.88),
    "dark":    (0.20, 0.15, 0.12),
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

def register(obj, material, bone):
    obj.name = bone + "." + obj.name
    obj.data.materials.append(mat(material))
    for p in obj.data.polygons:
        p.use_smooth = False
    PARTS.append((obj, bone))
    return obj


def box(name, size, loc, material, bone, taper_top=1.0, taper_bottom=1.0):
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


def prism(name, outline, depth, loc, material, bone, axis="x"):
    """Extruded polygon (flat-shaded): outline is [(a, b), ...] in the plane
    perpendicular to `axis`; depth extrudes along the axis."""
    n = len(outline)
    verts = []
    for off in (-depth / 2.0, depth / 2.0):
        for (a, b) in outline:
            verts.append(Vector((off, a, b)) if axis == "x" else Vector((a, off, b)))
    faces = [list(range(n - 1, -1, -1)), list(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append([i, j, n + j, n + i])
    me = bpy.data.meshes.new(name)
    me.from_pydata([v[:] for v in verts], [], faces)
    me.update()
    obj = bpy.data.objects.new(name, me)
    obj.location = loc
    bpy.context.scene.collection.objects.link(obj)
    return register(obj, material, bone)


# --- character ------------------------------------------------------------------

def build_hood(head_c):
    """A cowl: sphere shell around the head with a ragged low-poly face window."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=HEAD_R * 1.14, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "hoodshell"
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    r = HEAD_R * 1.14
    doomed = [v for v in bm.verts
              if v.co.y < -r * 0.35 and abs(v.co.x) < r * 0.62
              and -r * 0.62 < v.co.z < r * 0.55]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.location = head_c + Vector((0, 0.02, 0.03))
    register(obj, "hood", "head")
    # gold browband hugging the hood across the forehead, just above the eyes
    box("browband", (0.48, 0.08, 0.055), head_c + Vector((0, -0.24, 0.16)), "gold", "head")


def build_eyes(head_c):
    """Layered MS2 eyes: white sclera -> blue iris -> pupil -> highlight."""
    surf = -HEAD_R  # eye stack pushes progressively out of the face
    for sx in (-1, 1):
        e = Vector((sx * 0.105, 0, -0.01))
        sphere("sclera", 0.078, head_c + e + Vector((0, surf + 0.055, 0)),
               "white", "head", subdiv=2, scale=(0.80, 0.30, 1.20))
        sphere("iris", 0.055, head_c + e + Vector((0, surf + 0.045, -0.005)),
               "irisblue", "head", subdiv=2, scale=(0.85, 0.30, 1.15))
        sphere("pupil", 0.030, head_c + e + Vector((0, surf + 0.035, -0.010)),
               "pupil", "head", subdiv=1, scale=(0.85, 0.30, 1.10))
        sphere("glint", 0.014, head_c + e + Vector((sx * -0.018, surf + 0.026, 0.030)),
               "white", "head", subdiv=1)


def build_hair(head_c):
    # fringe peeking out under the browband
    sphere("fringe", 0.15, head_c + Vector((0, -0.235, 0.09)), "hair", "head",
           subdiv=2, scale=(1.30, 0.40, 0.40))
    # side locks framing the face at the cheeks
    for sx in (-1, 1):
        sphere("sidelock", 0.09, head_c + Vector((sx * 0.25, -0.14, -0.19)),
               "hair", "head", subdiv=2, scale=(0.50, 0.65, 1.75))
    # two long back locks flowing down to the hips
    for sx in (-1, 1):
        box("backlock", (0.09, 0.07, 0.55), head_c + Vector((sx * 0.14, 0.26, -0.42)),
            "hair", "head", taper_bottom=0.45)


def build_plume(head_c):
    """Teal feather arcing up the right side of the hood."""
    base = head_c + Vector((0.25, 0.06, 0.20))
    steps = ((Vector((0.00, 0.00, 0.02)), 0.085, 1.5),
             (Vector((0.035, 0.035, 0.15)), 0.075, 1.7),
             (Vector((0.06, 0.08, 0.28)), 0.060, 1.9))
    for i, (off, r, zs) in enumerate(steps):
        sphere("plume%d" % i, r, base + off, "teal", "head", subdiv=2,
               scale=(0.30, 0.75, zs))
    sphere("plumesocket", 0.055, base + Vector((0, 0, -0.04)), "gold", "head", subdiv=1)


def build_shield():
    """Blue kite shield with a gold rim + boss, on the left forearm."""
    c = Vector((-SHOULDER_X - 0.12, 0, SHOULDER_Z - ARM_LEN * 0.58))
    k = 1.3  # MS2 shields are HUGE — half the body
    kite = [(a * k, b * k) for (a, b) in
            [(-0.15, 0.13), (-0.165, 0.03), (-0.11, -0.10), (0.0, -0.235),
             (0.11, -0.10), (0.165, 0.03), (0.15, 0.13), (0.0, 0.165)]]
    rim = [(a * 1.16, b * 1.16) for (a, b) in kite]
    prism("shieldrim", rim, 0.045, c, "gold", "armL")
    prism("shieldface", kite, 0.062, c, "blue", "armL")
    sphere("shieldboss", 0.06, c + Vector((-0.035, 0, 0.02)), "gold", "armL",
           subdiv=1, scale=(0.6, 1, 1))


def build_sword():
    hand = Vector((SHOULDER_X, 0, SHOULDER_Z - ARM_LEN))
    box("grip", (0.05, 0.05, 0.14), hand + Vector((0, 0, -0.02)), "dark", "hand")
    box("guard", (0.22, 0.06, 0.05), hand + Vector((0, 0, -0.10)), "gold", "hand")
    box("blade", (0.075, 0.03, 0.46), hand + Vector((0, 0, -0.36)), "steel", "hand")
    tip = box("tip", (0.075, 0.03, 0.09), hand + Vector((0, 0, -0.635)), "steel", "hand",
              taper_top=0.02)
    for v in tip.data.vertices:
        v.co.z = -v.co.z


def build_character():
    # Torso: leather chest over a cloth skirt that flares at the hem.
    box("chest", (0.46, 0.34, 0.30), (0, 0, HIP + 0.35), "leather", "body", taper_top=0.88)
    box("chesttrim", (0.48, 0.36, 0.05), (0, 0, HIP + 0.22), "gold", "body")
    box("skirt", (0.41, 0.31, 0.22), (0, 0, HIP + 0.08), "cloth", "body", taper_bottom=1.30)
    box("belt", (0.50, 0.38, 0.05), (0, 0, HIP + 0.18), "dark", "body")

    head_c = Vector((0, 0, HEAD_CZ))
    sphere("face", HEAD_R, head_c, "skin", "head")
    build_eyes(head_c)
    build_hair(head_c)
    build_hood(head_c)
    build_plume(head_c)

    # Arms: leather with gold pauldrons + gauntlet cuffs, skin fists.
    for sx, bone in ((-1, "armL"), (1, "armR")):
        box("arm" + bone[-1], (0.14, 0.14, ARM_LEN - 0.07),
            (sx * SHOULDER_X, 0, SHOULDER_Z - (ARM_LEN - 0.07) / 2), "leather", bone)
        sphere("pauldron" + bone[-1], 0.115, (sx * SHOULDER_X, 0, SHOULDER_Z + 0.02),
               "gold", bone, subdiv=2, scale=(1, 0.9, 0.72))
        box("cuff" + bone[-1], (0.17, 0.17, 0.09),
            (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN + 0.10), "gold", bone)
        sphere("fist" + bone[-1], 0.07, (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN),
               "skin", bone, subdiv=1)

    # Legs: dark leggings, armored boots with gold trim.
    for sx, bone in ((-1, "legL"), (1, "legR")):
        box("leg" + bone[-1], (0.15, 0.15, 0.30), (sx * HIP_X, 0, HIP - 0.15), "dark", bone)
        box("boot" + bone[-1], (0.18, 0.23, 0.13), (sx * HIP_X, -0.02, 0.065), "leather", bone)
        box("boottrim" + bone[-1], (0.19, 0.16, 0.05), (sx * HIP_X, 0.01, 0.125), "gold", bone)

    build_sword()
    build_shield()


# --- joints (shared chibi skeleton — same as art/warrior.py) --------------------
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
    arm_data = bpy.data.armatures.new("ValkyrieRig")
    arm_obj = bpy.data.objects.new("ValkyrieRig", arm_data)
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
    target.name = "Valkyrie"
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()
    target.parent = arm_obj
    target.modifiers.new("Armature", "ARMATURE").object = arm_obj


# --- render / export --------------------------------------------------------------

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
    target.location = (0, 0, 0.78)
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
        "valk_f34": (1.5, -1.85, 1.4),
        "valk_front": (0.0, -2.35, 1.05),
        "valk_b34": (-1.5, 1.85, 1.4),
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
