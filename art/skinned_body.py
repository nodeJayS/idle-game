# Skinned MS2 female base body — imports the extracted f_body mesh directly
# (IP rule overruled by user 2026-07-01: real mesh, local extract at
# C:\Games\MapleStory2\Extracted) and rigs it on the 19-bone skeleton measured
# from f_body.nif (art/tools/nif_skeleton.py). This is the base "person";
# equipment layers on later.
#
# Clips (idle/run/attack) are rebuilt from art/motion/*.json — world-space
# rotation deltas decoded from the MS2 .kf files by art/tools/kf_motion.py —
# applied per frame as pose-bone matrix_basis, then baked into FBX actions.
#
#   blender -b --python art/skinned_body.py -- --export <file.fbx>
#       [--renders <dir>] [--pose] [--animtest <dir>]
#
# Conventions: built in MS2 cm (Z-up, faces -Y), scaled x0.01 + applied before
# clips/export so 133.5 cm -> 1.335 game units (matches the chibi height).

import json
import os
import sys
import bpy
from mathutils import Matrix, Quaternion, Vector

MOTION_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "motion")
CLIPS = ("idle", "run", "attack")

BLEND = r"C:\Games\MapleStory2\Extracted\f_body.blend"
MESH_NAME = "MS2_f_body"

# --- measured Bip01 joints (cm, world; from nif_skeleton.py on f_body.nif) -----
# name: (head, tail, parent). Tails point at the child joint (or the measured
# nub/weapon/toe landmark for leaf bones).
J = {
    "pelvis":   ((0, -2.0, 45.3),    (0, -2.5, 48.9),    None),
    "spine":    ((0, -2.5, 48.9),    (0, -3.2, 54.7),    "pelvis"),
    "spine1":   ((0, -3.2, 54.7),    (0, -3.4, 60.4),    "spine"),
    "spine2":   ((0, -3.4, 60.4),    (0, -0.35, 77.3),   "spine1"),
    "neck":     ((0, -0.35, 77.3),   (0, -0.03, 81.4),   "spine2"),
    "head":     ((0, -0.03, 81.4),   (0, -0.08, 133.0),  "neck"),

    "clavL":    ((3.19, 0.05, 75.5),  (9.95, 0.24, 73.3),  "spine2"),
    "uarmL":    ((9.95, 0.24, 73.3),  (19.68, 0.68, 61.2), "clavL"),
    "forearmL": ((19.68, 0.68, 61.2), (28.0, 1.04, 50.9),  "uarmL"),
    "handL":    ((28.0, 1.04, 50.9),  (34.3, 1.5, 41.2),   "forearmL"),

    "clavR":    ((-3.19, 0.05, 75.5),  (-9.95, 0.24, 73.3),  "spine2"),
    "uarmR":    ((-9.95, 0.24, 73.3),  (-19.68, 0.68, 61.2), "clavR"),
    "forearmR": ((-19.68, 0.68, 61.2), (-28.0, 1.04, 50.9),  "uarmR"),
    "handR":    ((-28.0, 1.04, 50.9),  (-34.3, 1.5, 41.2),   "forearmR"),

    "thighL":   ((7.76, -2.0, 45.3),  (9.01, 0.69, 23.4),  "pelvis"),
    "calfL":    ((9.01, 0.69, 23.4),  (9.85, 2.92, 8.61),  "thighL"),
    "footL":    ((9.85, 2.92, 8.61),  (9.97, -3.22, 0.0),  "calfL"),

    "thighR":   ((-7.76, -2.0, 45.3), (-9.01, 0.69, 23.4), "pelvis"),
    "calfR":    ((-9.01, 0.69, 23.4), (-9.85, 2.92, 8.61), "thighR"),
    "footR":    ((-9.85, 2.92, 8.61), (-9.97, -3.22, 0.0), "calfR"),
}

SKIN_SRGB = (0.99, 0.85, 0.74)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def import_body():
    bpy.ops.wm.append(
        filepath=BLEND + "/Object/" + MESH_NAME,
        directory=BLEND + "/Object/",
        filename=MESH_NAME)
    obj = bpy.data.objects[MESH_NAME]
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)  # blend stores a 0.01 object scale; mesh data is raw cm
    print("body dimensions (cm expected):", tuple(round(d, 1) for d in obj.dimensions))
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    obj.data.materials.clear()
    c = tuple(srgb_to_linear(v) for v in SKIN_SRGB)
    m = bpy.data.materials.new("skin")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*c, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
    m.diffuse_color = (*c, 1.0)
    obj.data.materials.append(m)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


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


def skin(body, arm):
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    # heat-map misses disconnected shells — rigidly bind leftovers to the
    # nearest bone segment so nothing is left behind at the origin when posed
    def seg_dist(p, a, b):
        ab = Vector(b) - Vector(a)
        t = max(0.0, min(1.0, (p - Vector(a)).dot(ab) / ab.length_squared))
        return (p - (Vector(a) + ab * t)).length

    fixed = 0
    for v in body.data.vertices:
        if any(g.weight > 0.01 for g in v.groups):
            continue
        best = min(J, key=lambda n: seg_dist(v.co, J[n][0], J[n][1]))
        vg = body.vertex_groups.get(best) or body.vertex_groups.new(name=best)
        vg.add([v.index], 1.0, "REPLACE")
        fixed += 1
    print("skin: %d verts heat-mapped, %d rigid-bound to nearest bone"
          % (len(body.data.vertices) - fixed, fixed))


def scale_to_game_units(body):
    h = body.dimensions.z
    print("body height before scale: %.3f" % h)
    if h < 10:  # already in game units — don't shrink twice
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.transform.resize(value=(0.01, 0.01, 0.01))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    print("body height after scale: %.3f" % body.dimensions.z)


def build_clips(arm):
    """One Blender action per decoded MS2 clip. The JSON carries per-frame
    world-space rotation deltas (armature space == MS2 world space here), so:
        M_pose(bone) = T(head_pose) @ Rdelta @ R_rest
    and matrix_basis = rest_relative form of that — computed analytically so
    no depsgraph updates are needed inside the frame loop."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    scene = bpy.context.scene
    scene.render.fps = 30

    rest_local = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    parent_of = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}

    arm.animation_data_create()
    for clip in CLIPS:
        with open(os.path.join(MOTION_DIR, clip + ".json")) as fh:
            data = json.load(fh)
        act = bpy.data.actions.new(clip)
        arm.animation_data.action = act
        frames = data["frames"]
        for fr in range(frames):
            pose_mat = {}  # armature-space pose matrix per bone this frame
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
                        # in-place: keep sway(x) + bob(z), drop forward drift(y)
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
        # NLA strip so the FBX exporter emits every action as its own take
        track = arm.animation_data.nla_tracks.new()
        track.name = clip
        track.strips.new(clip, 0, act)
        track.mute = True
        print("clip %-7s %d frames (%.2fs)" % (clip, frames, data["duration"]))
    arm.animation_data.action = None
    bpy.ops.object.mode_set(mode="OBJECT")


def render_anim_frames(arm, out_dir):
    """Contact-sheet renders: 4 frames per clip, f34 view — the eyeball check."""
    scene = bpy.context.scene
    for clip in CLIPS:
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


def export_fbx(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=False,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,   # one take per action: idle/run/attack
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
    target.location = (0, 0, 0.67)
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
             "body_side": (2.4, 0.0, 1.0)}
    for name, loc in views.items():
        cam_obj.location = loc
        scene.render.filepath = "%s/%s.png" % (out_dir, name)
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)

    body = import_body()
    arm = build_armature()
    skin(body, arm)
    scale_to_game_units(body)

    if "--pose" in argv:
        # deformation sanity check: swing limbs and let the render show tearing
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

    build_clips(arm)

    if "--export" in argv:
        export_fbx(argv[argv.index("--export") + 1])
    if "--renders" in argv or "--animtest" in argv:
        cam = setup_scene()
        if "--renders" in argv:
            render_views(cam, argv[argv.index("--renders") + 1])
        if "--animtest" in argv:
            render_anim_frames(arm, argv[argv.index("--animtest") + 1])


main()
