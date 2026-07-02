# Chibi Warrior — scripted hero model, SMOOTH "toy" style with REAL MapleStory 2
# proportions (measured from the extracted f_body mesh — see
# docs/ms2-hero-pipeline-plan.md): head ~42% of total height and WIDER than
# body+arms; slim tapered torso; the silhouette mass comes from GEAR (hood +
# wings, layered pauldrons, skirt flare, armored boots, big kite shield).
# Design = the hooded shield-knight from art/valkyrie.py, rebuilt smooth.
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

# Visual head radius — bigger than the joint constant (geometry is free; only
# BONES must stay fixed). Head sphere = 0.62 wide, ~42% of the ~1.45 height.
HEAD_VR = 0.30

# --- palette (sRGB) -------------------------------------------------------------
COLORS = {
    "skin":  (0.99, 0.85, 0.74),
    "hair":  (0.50, 0.29, 0.12),
    "hood":  (0.35, 0.19, 0.12),
    "cloth": (0.58, 0.36, 0.20),
    "tunic": (0.15, 0.38, 0.88),
    "gold":  (0.93, 0.71, 0.18),
    "teal":  (0.25, 0.78, 0.92),
    "boot":  (0.30, 0.20, 0.13),
    "belt":  (0.22, 0.15, 0.10),
    "steel": (0.80, 0.83, 0.88),
    "dark":  (0.20, 0.15, 0.12),
    "blue":  (0.16, 0.33, 0.82),
    "white": (0.97, 0.97, 0.97),
    "iris":  (0.13, 0.45, 0.95),
    "pupil": (0.06, 0.06, 0.09),
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


def prism(name, outline, depth, loc, material, bone, axis="x"):
    """Extruded polygon (flat-shaded, crisp): outline is [(a, b), ...] in the
    plane perpendicular to `axis`; depth extrudes along the axis."""
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
    return register(obj, material, bone, smooth=False)


# --- head: face, layered anime eyes, sculpted hair, hood + wings ----------------

def build_eyes(head_c):
    """Layered MS2 eyes: sclera -> iris -> pupil -> two highlights."""
    for sx in (-1, 1):
        e = head_c + Vector((sx * 0.115, 0, -0.02))
        ball("sclera", 0.085, e + Vector((0, -0.268, 0)),
             "white", "head", scale=(0.80, 0.25, 1.25), segs=16, rings=12)
        ball("iris", 0.060, e + Vector((0, -0.285, -0.004)),
             "iris", "head", scale=(0.85, 0.25, 1.20), segs=16, rings=12)
        ball("pupil", 0.033, e + Vector((0, -0.298, -0.008)),
             "pupil", "head", scale=(0.85, 0.25, 1.15), segs=12, rings=8)
        ball("glint", 0.016, e + Vector((sx * -0.020, -0.308, 0.035)),
             "white", "head", segs=12, rings=8)
        ball("glint2", 0.009, e + Vector((sx * 0.016, -0.310, -0.030)),
             "white", "head", segs=12, rings=8)


def build_hair(head_c):
    """Sculpted clumps, not a bowl shell: fringe under the hood + side/back locks."""
    for i, (fx, fz) in enumerate(((-0.14, 0.09), (0.0, 0.12), (0.14, 0.09))):
        ball("fringe%d" % i, 0.115, head_c + Vector((fx, -0.255, fz)),
             "hair", "head", scale=(0.60, 0.38, 0.50))
    for sx in (-1, 1):
        ball("sidelock", 0.095, head_c + Vector((sx * 0.285, -0.09, -0.10)),
             "hair", "head", scale=(0.42, 0.60, 1.45))
    for sx in (-1, 1):
        box("backlock", (0.085, 0.06, 0.38), head_c + Vector((sx * 0.12, 0.29, -0.36)),
            "hair", "head", taper_bottom=0.45, bevel=0.02, seg=2)


def build_hood(head_c):
    """Cowl shell around the big head + swept horn-wings (the MS2 knight cap)."""
    r = HEAD_VR * 1.12
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16,
                                         radius=r, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "hood"
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [v for v in bm.verts
              if v.co.y < -r * 0.20 and abs(v.co.x) < r * 0.70
              and v.co.z < r * 0.45]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    s = obj.modifiers.new("Solid", "SOLIDIFY")
    s.thickness = 0.03
    s.offset = 1.0
    apply_mods(obj)
    obj.location = head_c + Vector((0, 0.01, 0.02))
    register(obj, "hood", "head")
    # gold browband tucked against the face at the hood's opening
    box("browband", (0.52, 0.05, 0.055), head_c + Vector((0, -0.26, 0.17)),
        "gold", "head", bevel=0.02, seg=2)
    # swept horn-wings off the sides of the hood, tilted outward
    wing = [(-0.02, -0.02), (0.10, 0.02), (0.24, 0.16), (0.34, 0.36),
            (0.26, 0.34), (0.12, 0.16), (0.00, 0.08)]
    for sx in (-1, 1):
        w = prism("hoodwing", wing, 0.045, head_c + Vector((sx * 0.30, 0.02, 0.06)),
                  "hood", "head")
        w.rotation_euler = (0, sx * 0.35, 0)


def build_plume(head_c):
    """Teal feather arcing up the right side of the hood."""
    base = head_c + Vector((0.22, 0.05, 0.30))
    steps = ((Vector((0.00, 0.00, 0.02)), 0.080, 1.5),
             (Vector((0.030, 0.035, 0.14)), 0.070, 1.7),
             (Vector((0.055, 0.080, 0.26)), 0.055, 1.9))
    for i, (off, r, zs) in enumerate(steps):
        ball("plume%d" % i, r, base + off, "teal", "head",
             scale=(0.30, 0.75, zs), segs=16, rings=12)
    ball("plumesocket", 0.05, base + Vector((0, 0, -0.03)), "gold", "head",
         segs=12, rings=8)


# --- gear -------------------------------------------------------------------------

def build_sword():
    hand = Vector((SHOULDER_X, 0, SHOULDER_Z - ARM_LEN))
    box("grip", (0.05, 0.05, 0.14), hand + Vector((0, 0, -0.02)), "belt", "hand",
        bevel=0.012, seg=2, smooth=False)
    box("guard", (0.23, 0.06, 0.05), hand + Vector((0, 0, -0.10)), "gold", "hand",
        bevel=0.012, seg=2, smooth=False)
    box("blade", (0.085, 0.032, 0.46), hand + Vector((0, 0, -0.36)), "steel", "hand",
        bevel=0.01, seg=1, smooth=False)
    tip = box("tip", (0.085, 0.032, 0.09), hand + Vector((0, 0, -0.635)), "steel", "hand",
              taper_top=0.02, bevel=0, smooth=False)
    for v in tip.data.vertices:
        v.co.z = -v.co.z


def build_shield():
    """Big blue kite shield (half the body) with a gold rim + boss, left forearm."""
    c = Vector((-SHOULDER_X - 0.10, 0, SHOULDER_Z - ARM_LEN * 0.55))
    k = 1.3
    kite = [(a * k, b * k) for (a, b) in
            [(-0.15, 0.13), (-0.165, 0.03), (-0.11, -0.10), (0.0, -0.235),
             (0.11, -0.10), (0.165, 0.03), (0.15, 0.13), (0.0, 0.165)]]
    rim = [(a * 1.16, b * 1.16) for (a, b) in kite]
    prism("shieldrim", rim, 0.045, c, "gold", "armL")
    prism("shieldface", kite, 0.062, c, "blue", "armL")
    ball("shieldboss", 0.06, c + Vector((-0.035, 0, 0.02)), "gold", "armL",
         scale=(0.6, 1, 1), segs=16, rings=12)


# --- character ---------------------------------------------------------------------

def build_character():
    # Slim tapered torso (MS2 hourglass — the head dwarfs it): blue tunic chest
    # narrowing to the waist, belt, then a cloth skirt flaring at the hem.
    box("chest", (0.32, 0.24, 0.26), (0, 0, 0.78), "tunic", "body",
        taper_bottom=0.80, bevel=0.05, seg=4)
    box("chesttrim", (0.27, 0.205, 0.05), (0, 0, 0.645), "gold", "body",
        bevel=0.015, seg=2)
    box("belt", (0.265, 0.20, 0.055), (0, 0, 0.60), "belt", "body", bevel=0.015, seg=2)
    box("skirt", (0.27, 0.21, 0.20), (0, 0, 0.51), "cloth", "body",
        taper_top=0.92, taper_bottom=1.50, bevel=0.03, seg=3)

    # The head: ~42% of total height, wider than body+arms (per measured f_body).
    head_c = Vector((0, 0, HEAD_CZ))
    ball("head", HEAD_VR, head_c, "skin", "head", scale=(1.05, 0.95, 1.0))
    build_eyes(head_c)
    build_hair(head_c)
    build_hood(head_c)
    build_plume(head_c)

    # Thin arms tight to the body; layered pauldrons carry the shoulder mass.
    for sx, bone in ((-1, "armL"), (1, "armR")):
        box("arm" + bone[-1], (0.085, 0.085, 0.28),
            (sx * SHOULDER_X, 0, SHOULDER_Z - 0.155), "tunic", bone,
            bevel=0.035, seg=3)
        ball("pauldron" + bone[-1], 0.10, (sx * 0.24, 0, SHOULDER_Z + 0.01),
             "steel", bone, scale=(0.90, 0.85, 0.55))
        ball("pauldron2" + bone[-1], 0.085, (sx * 0.265, 0, SHOULDER_Z - 0.045),
             "steel", bone, scale=(0.80, 0.75, 0.50))
        box("cuff" + bone[-1], (0.125, 0.125, 0.09),
            (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN + 0.085), "gold", bone,
            bevel=0.025, seg=2)
        ball("fist" + bone[-1], 0.065, (sx * SHOULDER_X, 0, SHOULDER_Z - ARM_LEN),
             "skin", bone, segs=16, rings=12)

    # Short legs, chunky armored boots with gold-trimmed cuffs.
    for sx, bone in ((-1, "legL"), (1, "legR")):
        box("leg" + bone[-1], (0.13, 0.13, 0.24), (sx * HIP_X, 0, 0.32),
            "belt", bone, bevel=0.04, seg=3)
        box("boot" + bone[-1], (0.17, 0.24, 0.15), (sx * HIP_X, -0.02, 0.085),
            "boot", bone, bevel=0.045, seg=4)
        box("bootcuff" + bone[-1], (0.19, 0.19, 0.07), (sx * HIP_X, 0.005, 0.185),
            "boot", bone, bevel=0.025, seg=3)
        box("boottrim" + bone[-1], (0.20, 0.20, 0.035), (sx * HIP_X, 0.005, 0.215),
            "gold", bone, bevel=0.012, seg=2)

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
