# Skinned MS2 female base body — full-fidelity import from the extracted client
# files (IP rule overruled by user 2026-07-01). Phase 0/1 of docs/ms2-port-plan.md:
# art/tools/nif_import.py parses f_body.nif directly (UVs, authored normals,
# REAL skin weights + bone palettes, per-part textures incl. the alpha face),
# the body goes on the 19-bone skeleton measured from the same NIF, and the
# idle/run/attack actions are rebuilt from art/motion/*.json (world-space deltas
# decoded from the MS2 .kf files by art/tools/kf_motion.py).
#
#   blender -b --python art/skinned_body.py -- --export <file.fbx>
#       [--renders <dir>] [--pose] [--animtest <dir>]
#
# Export also converts the referenced DDS textures to PNG next to the FBX so
# Unity's material import finds them by name.
#
# Conventions: built in MS2 cm (Z-up, faces -Y), scaled x0.01 + applied before
# clips/export so 133.5 cm -> 1.335 game units (matches the chibi height).

import json
import os
import sys
import bpy
from mathutils import Matrix, Quaternion, Vector

ART_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(ART_DIR, "tools"))
from nif_import import load_nif  # noqa: E402
from nif_skeleton import load_world_positions, load_world_transforms  # noqa: E402

EXTRACT_ROOT = r"C:\Games\MapleStory2\Extracted"
GENDERS = {
    "female": r"C:\Games\MapleStory2\Extracted\Character\female\f_body.nif",
    "male":   r"C:\Games\MapleStory2\Extracted\Character\male\m_body.nif",
}


def clip_roles():
    """Every decoded clip in the hero's motion dir becomes an FBX take,
    named by its role (idle/run/attack/attack2/bore/hit/death/...)."""
    import glob
    return sorted(os.path.splitext(os.path.basename(p))[0]
                  for p in glob.glob(os.path.join(MOTION_DIR, "*.json")))

# module state set by main() from --hero / --gender
NIF = GENDERS["female"]
MOTION_DIR = os.path.join(ART_DIR, "motion", "warrior_basic")
J = {}


def joints_from_nif(nif_path):
    """The 19-bone rig, measured straight from the body NIF (cm, world).
    Tails point at the child joint / measured landmark."""
    w = load_world_positions(nif_path)

    def p(n):
        return w["Bip01 " + n]

    J = {
        "pelvis": (p("Pelvis"), p("Spine"), None),
        "spine":  (p("Spine"), p("Spine1"), "pelvis"),
        "spine1": (p("Spine1"), p("Spine2"), "spine"),
        "spine2": (p("Spine2"), p("Neck"), "spine1"),
        "neck":   (p("Neck"), p("Head"), "spine2"),
        "head":   (p("Head"), p("HeadNub"), "neck"),
    }
    for s in ("L", "R"):
        t = s
        J["clav" + t] = (p(s + " Clavicle"), p(s + " UpperArm"), "spine2")
        J["uarm" + t] = (p(s + " UpperArm"), p(s + " Forearm"), "clav" + t)
        J["forearm" + t] = (p(s + " Forearm"), p(s + " Hand"), "uarm" + t)
        J["hand" + t] = (p(s + " Hand"), w["Weapon_Hand_%s_Point" % s], "forearm" + t)
        J["thigh" + t] = (p(s + " Thigh"), p(s + " Calf"), "pelvis")
        J["calf" + t] = (p(s + " Calf"), p(s + " Foot"), "thigh" + t)
        J["foot" + t] = (p(s + " Foot"), p(s + " Toe0"), "calf" + t)
    return J


def map_bone(bip):
    """Bip01 name -> our rig bone; extras fold into the nearest kept ancestor."""
    base = {
        "Bip01 Pelvis": "pelvis", "Bip01 Spine": "spine", "Bip01 Spine1": "spine1",
        "Bip01 Spine2": "spine2", "Bip01 Neck": "neck", "Bip01 Head": "head",
        "Bip01 HeadNub": "head",
    }
    if bip in base:
        return base[bip]
    for tag, side in ((" L ", "L"), (" R ", "R")):
        if tag in bip:
            part = bip.split(tag, 1)[1]
            if part.startswith("Finger") or part == "Hand":
                return "hand" + side
            if part.startswith("ForeTwist") or part == "Forearm":
                return "forearm" + side
            if part.startswith("UpperTwist") or part == "UpperArm":
                return "uarm" + side
            if part == "Clavicle":
                return "clav" + side
            if part == "Thigh":
                return "thigh" + side
            if part == "Calf":
                return "calf" + side
            if part in ("Foot", "Toe0"):
                return "foot" + side
    if bip.startswith("Tail_Point"):
        return "head"           # hair tail helpers live on the head
    return "pelvis"             # NonAccum / Footsteps / Bip01 / unknown


# --- textures --------------------------------------------------------------------

_tex_cache = {}

def find_texture(name):
    if name in _tex_cache:
        return _tex_cache[name]
    for root, _dirs, files in os.walk(EXTRACT_ROOT):
        for fn in files:
            if fn.lower() == name.lower():
                _tex_cache[name] = os.path.join(root, fn)
                return _tex_cache[name]
    _tex_cache[name] = None
    print("  ! texture not found:", name)
    return None


_materials = {}
_tints = {}  # unity material name (lower) -> sRGB tint, written next to the FBX

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def material_for(tex_name, alpha, tint):
    key = (tex_name, alpha, tint)
    if key in _materials:
        return _materials[key]
    m = bpy.data.materials.new(os.path.splitext(tex_name or "skin")[0])
    if tint:
        _tints[m.name.lower()] = tint
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Roughness"].default_value = 0.9
        path = find_texture(tex_name) if tex_name else None
        if path:
            img = bpy.data.images.load(path)
            tex = m.node_tree.nodes.new("ShaderNodeTexImage")
            tex.image = img
            color_out = tex.outputs["Color"]
            if tint:
                # MS2 customization: grayscale skin texture x OverrideColor0
                mix = m.node_tree.nodes.new("ShaderNodeMix")
                mix.data_type = "RGBA"
                mix.blend_type = "MULTIPLY"
                mix.inputs["Factor"].default_value = 1.0
                m.node_tree.links.new(color_out, mix.inputs["A"])
                mix.inputs["B"].default_value = (
                    *[srgb_to_linear(c) for c in tint], 1.0)
                color_out = mix.outputs["Result"]
            m.node_tree.links.new(color_out, bsdf.inputs["Base Color"])
            if alpha:
                m.node_tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    if alpha:
        try:
            m.blend_method = "CLIP"
        except (AttributeError, TypeError):
            pass
    _materials[key] = m
    return m


# --- mesh build from NIFs ------------------------------------------------------------

def nearest_bone(center):
    """Closest J bone segment to a point — anchors static (unskinned) parts."""
    def seg_d(a, b):
        av, bv, cv = Vector(a), Vector(b), Vector(center)
        ab = bv - av
        t = max(0.0, min(1.0, (cv - av).dot(ab) / max(ab.length_squared, 1e-9)))
        return (cv - (av + ab * t)).length
    return min(J, key=lambda n: seg_d(J[n][0], J[n][1]))


def build_parts(nif_path, hide=frozenset(), rigid_bone=None, xform=None):
    """Build Blender objects for a NIF's parts. hide = part names to skip;
    rigid_bone + xform (world pos, rot3x3) = hand-space attachment mode."""
    parts, names = [], set()
    for md in load_nif(nif_path):
        names.add(md["name"])
        if md["name"] in hide:
            print("  hide %-10s (replaced by gear)" % md["name"])
            continue
        verts = md["verts"]
        normals = md["normals"]
        if xform is not None:
            # hand-space item: place at the weapon point's world transform
            t, R = xform
            verts = [(R[0] * v[0] + R[1] * v[1] + R[2] * v[2] + t[0],
                      R[3] * v[0] + R[4] * v[1] + R[5] * v[2] + t[1],
                      R[6] * v[0] + R[7] * v[1] + R[8] * v[2] + t[2]) for v in verts]
            if normals:
                normals = [(R[0] * n[0] + R[1] * n[1] + R[2] * n[2],
                            R[3] * n[0] + R[4] * n[1] + R[5] * n[2],
                            R[6] * n[0] + R[7] * n[1] + R[8] * n[2]) for n in normals]

        me = bpy.data.meshes.new(md["name"])
        me.from_pydata([Vector(v) for v in verts], [], md["tris"])
        me.validate()
        if md["uvs"]:
            uvl = me.uv_layers.new()
            for loop in me.loops:
                u, v = md["uvs"][loop.vertex_index]
                uvl.data[loop.index].uv = (u, 1.0 - v)  # DX -> GL v-flip
        for p in me.polygons:
            p.use_smooth = True
        if normals:
            try:
                me.normals_split_custom_set_from_vertices(
                    [Vector(n).normalized() for n in normals])
            except (AttributeError, RuntimeError) as e:
                print("  ! custom normals failed on %s: %s" % (md["name"], e))
        me.materials.append(material_for(md["texture"], md["alpha"], md["tint"]))

        obj = bpy.data.objects.new(md["name"], me)
        bpy.context.scene.collection.objects.link(obj)

        # REAL weights: BLENDINDICES -> BONE_PALETTE -> modifier bone list -> our rig
        groups = {}
        def vg(bone):
            if bone not in groups:
                groups[bone] = obj.vertex_groups.new(name=bone)
            return groups[bone]

        if rigid_bone is not None:
            vg(rigid_bone).add(list(range(len(me.vertices))), 1.0, "REPLACE")
        elif md["skinned"] and md["blend_indices"]:
            pal = md["bone_palette"] or list(range(len(md["bone_names"])))
            bnames = md["bone_names"]
            for vi, bi in enumerate(md["blend_indices"]):
                bw = md["blend_weights"][vi] if md["blend_weights"] else (1.0,)
                w = list(bw[:3]) + [max(0.0, 1.0 - sum(bw[:3]))] if len(bw) == 3 else list(bw)
                for k in range(min(4, len(bi))):
                    if w[k] <= 0.001:
                        continue
                    slot = pal[bi[k]] if bi[k] < len(pal) else bi[k]
                    bip = bnames[slot] if slot < len(bnames) else None
                    if bip:
                        vg(map_bone(bip)).add([vi], w[k], "ADD")
        else:
            # static model-space part (scalp, hair, hood) — anchor to nearest bone
            c = [sum(v[d] for v in verts) / len(verts) for d in range(3)]
            vg(nearest_bone(c)).add(list(range(len(me.vertices))), 1.0, "REPLACE")
        parts.append(obj)
        print("  part %-12s %4dv tex=%s alpha=%s" %
              (md["name"], len(me.vertices), md["texture"], md["alpha"]))
    return parts, names


def build_armature():
    arm_data = bpy.data.armatures.new("Rig")
    arm_obj = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in J.items():
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent:
            b.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def attach(parts, arm):
    for obj in parts:
        obj.parent = arm
        obj.modifiers.new("Armature", "ARMATURE").object = arm


def scale_to_game_units(parts):
    hi = max(max((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)
             for obj in parts)
    if hi < 10:
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.transform.resize(value=(0.01, 0.01, 0.01))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    print("scaled to game units")


# --- clips from decoded MS2 motion --------------------------------------------------

def build_clips(arm):
    """One Blender action per decoded MS2 clip. The JSON carries per-frame
    world-space rotation deltas (armature space == MS2 world space here):
        M_pose(bone) = T(head_pose) @ Rdelta @ R_rest
    matrix_basis computed analytically — no depsgraph updates in the loop."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    bpy.context.scene.render.fps = 30

    rest_local = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    parent_of = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}

    arm.animation_data_create()
    for clip in clip_roles():
        with open(os.path.join(MOTION_DIR, clip + ".json")) as fh:
            data = json.load(fh)
        act = bpy.data.actions.new(clip)
        arm.animation_data.action = act
        frames = data["frames"]
        for fr in range(frames):
            pose_mat = {}
            for name in J:  # dict order = parents before children
                pb = arm.pose.bones[name]
                rl = rest_local[name]
                dq = data["bones"].get(name, {}).get("dq")
                q = Quaternion(dq[fr]) if dq else Quaternion((1, 0, 0, 0))
                r_target = q.to_matrix() @ rl.to_3x3()

                pname = parent_of[name]
                if pname is None:
                    head = rl.to_translation()
                    if name == "pelvis" and data.get("pelvis_dt"):
                        dt = data["pelvis_dt"][fr]
                        head = head + Vector((dt[0], 0.0, dt[2]))
                    m = r_target.to_4x4()
                    m.translation = head
                    basis = rl.inverted() @ m
                else:
                    rel_rest = rest_local[pname].inverted() @ rl
                    head = pose_mat[pname] @ rel_rest.to_translation()
                    m = r_target.to_4x4()
                    m.translation = head
                    basis = rel_rest.inverted() @ pose_mat[pname].inverted() @ m
                pose_mat[name] = m

                pb.matrix_basis = basis
                pb.keyframe_insert("rotation_quaternion", frame=fr)
                if pname is None:
                    pb.keyframe_insert("location", frame=fr)
        act.use_fake_user = True
        print("clip %-7s %d frames (%.2fs)" % (clip, frames, data["duration"]))
    arm.animation_data.action = None
    for pb in arm.pose.bones:  # back to rest — keyframing left the last pose set
        pb.matrix_basis = Matrix.Identity(4)
    bpy.ops.object.mode_set(mode="OBJECT")


# --- render / export ----------------------------------------------------------------

def export_textures_dds(out_dir):
    """Unity decodes DXT natively — ship the DDS as-is, lowercase names so
    SkinnedHero's Resources.Load lookup is build-safe. (Never mutate the
    Blender images here: a failed in-place PNG re-encode breaks rendering.)"""
    import shutil
    for img in bpy.data.images:
        if img.source != "FILE" or not os.path.exists(img.filepath):
            continue
        dst = os.path.join(out_dir, os.path.basename(img.filepath).lower())
        shutil.copyfile(img.filepath, dst)
        print("texture ->", dst)


def export_tints(path):
    """Per-material sRGB tints (MS2 OverrideColor0) for SkinnedHero to apply —
    plain 'name r g b' lines, one per tinted material."""
    if not _tints:
        return
    txt = os.path.splitext(path)[0] + "_tints.txt"
    with open(txt, "w") as fh:
        for name, (r, g, b) in sorted(_tints.items()):
            fh.write("%s %.4f %.4f %.4f\n" % (name, r, g, b))
    print("tints ->", txt)


def export_skills(path, hero):
    """GameCore skill id -> (Skill state slot, MS2 sound set) — 'id slot sound'
    lines SkinnedHero reads to route TriggerSkill to the right clip + sound."""
    skills = (hero or {}).get("skills")
    if not skills:
        return
    txt = os.path.splitext(path)[0] + "_skills.txt"
    with open(txt, "w") as fh:
        for sid, b in sorted(skills.items()):
            fh.write("%s %d %s\n" % (sid, b["slot"], b["sound"]))
    print("skills ->", txt)


def export_fbx(path, hero=None):
    export_textures_dds(os.path.dirname(os.path.abspath(path)))
    export_tints(path)
    export_skills(path, hero)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=False,
        add_leaf_bones=False,
        path_mode="STRIP",   # Unity resolves textures by name from the same folder
        bake_anim=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_simplify_factor=0.0,
    )
    print("exported", path)


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
    scene = bpy.context.scene
    views = {"body_front": (0.0, -2.4, 1.0), "body_f34": (1.55, -1.9, 1.35),
             "body_side": (2.4, 0.0, 1.0),
             "body_face": (0.35, -1.35, 1.25)}
    for name, loc in views.items():
        cam_obj.location = loc
        scene.render.filepath = "%s/%s.png" % (out_dir, name)
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def render_anim_frames(arm, out_dir):
    scene = bpy.context.scene
    for clip in clip_roles():
        act = bpy.data.actions[clip]
        arm.animation_data.action = act
        span = int(act.frame_range[1])
        for i, frac in enumerate((0.0, 0.25, 0.5, 0.75)):
            fr = int(span * frac)
            scene.frame_set(fr)
            scene.render.filepath = "%s/%s_%d_f%02d.png" % (out_dir, clip, i, fr)
            bpy.ops.render.render(write_still=True)
            print("rendered", scene.render.filepath)
    arm.animation_data.action = None


def main():
    global NIF, MOTION_DIR, J
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    hero = None
    if "--hero" in argv:
        with open(argv[argv.index("--hero") + 1]) as fh:
            hero = json.load(fh)
        gender = hero["gender"]
    else:
        gender = argv[argv.index("--gender") + 1] if "--gender" in argv else "female"
    NIF = GENDERS[gender]
    # clips are PER HERO (three heroes = three clip sets); bare --gender test
    # builds fall back to the gender dir (male smoke clips live there)
    MOTION_DIR = os.path.join(ART_DIR, "motion", hero["id"] if hero else gender)
    J = joints_from_nif(NIF)
    print("gender=%s nif=%s hero=%s" % (gender, os.path.basename(NIF),
                                        hero["id"] if hero else "-"))

    bpy.ops.wm.read_factory_settings(use_empty=True)

    parts = []
    provided = set()
    if hero:
        for rel in hero.get("items", []):
            objs, names = build_parts(os.path.join(EXTRACT_ROOT, rel))
            parts += objs
            provided |= names
    # body: gear replaces same-named parts; worn slots hide the underwear
    hide = set(provided)
    if any(n.startswith("CL") for n in provided):
        hide.add("CL_Bra")
    if any(n.startswith("PA") for n in provided):
        hide.add("PA_Panty")
    body_objs, _ = build_parts(NIF, hide=hide)
    parts += body_objs
    if hero:
        weapon_points = load_world_transforms(NIF)
        for att in hero.get("attach", []):
            objs, _ = build_parts(os.path.join(EXTRACT_ROOT, att["nif"]),
                                  rigid_bone=att["bone"],
                                  xform=weapon_points[att["point"]])
            parts += objs

    arm = build_armature()
    attach(parts, arm)
    scale_to_game_units(parts)
    build_clips(arm)

    if "--pose" in argv:
        import math
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="POSE")
        arm.pose.bones["uarmL"].rotation_mode = "XYZ"
        arm.pose.bones["uarmL"].rotation_euler = (0, 0, math.radians(60))
        arm.pose.bones["thighR"].rotation_mode = "XYZ"
        arm.pose.bones["thighR"].rotation_euler = (math.radians(-40), 0, 0)
        arm.pose.bones["head"].rotation_mode = "XYZ"
        arm.pose.bones["head"].rotation_euler = (0, 0, math.radians(20))
        bpy.ops.object.mode_set(mode="OBJECT")

    if "--export" in argv:
        export_fbx(argv[argv.index("--export") + 1], hero)
    if "--renders" in argv or "--animtest" in argv:
        cam = setup_scene()
        if "--renders" in argv:
            render_views(cam, argv[argv.index("--renders") + 1])
        if "--animtest" in argv:
            render_anim_frames(arm, argv[argv.index("--animtest") + 1])


main()
