# Generic MS2 effect-NIF -> FBX baker (ROADMAP 10.11e ripped-FX spike).
# Imports every NiMesh part of an FX nif STATIC (bind pose; FX bone swirl
# anims are not carried — Unity spins/pulses the prefab instead), assigns
# the referenced DDS textures, ships them lowercase next to the FBX (Unity
# decodes DXT natively, same recipe as skinned_body's export), exports mesh-only
# FBX in game units (MS2 cm x0.01).
#
#   blender -b --python art/tools/fx_bake.py -- <effect.nif> <out.fbx> [--renders <dir>]
#
# Textures resolve next to the nif first, then the Effect textures dir,
# then the whole extract (same precedence as the hero pipeline).

import os
import shutil
import sys

import bpy
from mathutils import Vector

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.append(TOOLS_DIR)
from nif_import import load_nif  # noqa: E402

EXTRACT_ROOT = r"C:\Games\MapleStory2\Extracted"
FX_TEX_DIR = os.path.join(EXTRACT_ROOT, "Effect", "textures")


def find_texture(name, near):
    for root_dir in [near, FX_TEX_DIR, EXTRACT_ROOT]:
        for root, _dirs, files in os.walk(root_dir):
            for fn in files:
                if fn.lower() == name.lower():
                    return os.path.join(root, fn)
    print("  ! texture not found:", name)
    return None


def material_for(tex_name, alpha, near, cache={}):
    stem = os.path.splitext(tex_name or "untextured")[0].lower()
    if stem in cache:
        return cache[stem]
    m = bpy.data.materials.new(stem)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf and tex_name:
        path = find_texture(tex_name, near)
        if path:
            img = bpy.data.images.load(path)
            img["dds_name"] = stem + ".dds"
            tex = m.node_tree.nodes.new("ShaderNodeTexImage")
            tex.image = img
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
            if alpha:
                m.node_tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
                m.blend_method = "BLEND"
    cache[stem] = m
    return m


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    nif_path, fbx_path = argv[0], argv[1]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    near = os.path.dirname(nif_path)
    built = 0
    for md in load_nif(nif_path):
        name = md["name"] or "part"
        if name.lower().startswith(("bone", "tempendbone")):
            continue  # FX rig helper prisms, not visible geometry
        me = bpy.data.meshes.new(name)
        me.from_pydata([Vector(v) for v in md["verts"]], [], md["tris"])
        me.validate()
        if md["uvs"]:
            uvl = me.uv_layers.new()
            for loop in me.loops:
                u, v = md["uvs"][loop.vertex_index]
                uvl.data[loop.index].uv = (u, 1.0 - v)
        for p in me.polygons:
            p.use_smooth = True
        me.materials.append(material_for(md["texture"], md["alpha"], near))
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        built += 1
        print("  part %-20s %5dv %5dt tex=%s alpha=%s"
              % (name, len(md["verts"]), len(md["tris"]), md["texture"], md["alpha"]))
    if not built:
        sys.exit("no visible parts in " + nif_path)

    # MS2 cm -> game units, matching the hero pipeline
    for obj in bpy.data.objects:
        obj.scale = (0.01, 0.01, 0.01)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    out_dir = os.path.dirname(os.path.abspath(fbx_path))
    os.makedirs(out_dir, exist_ok=True)
    for img in bpy.data.images:
        if img.source == "FILE" and os.path.exists(img.filepath):
            dst = os.path.join(out_dir, img["dds_name"])
            shutil.copyfile(img.filepath, dst)
            print("texture ->", dst)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        path_mode="STRIP",
    )
    print("exported", fbx_path)

    if "--renders" in argv:
        out = argv[argv.index("--renders") + 1]
        scene = bpy.context.scene
        for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
            try:
                scene.render.engine = engine
                break
            except TypeError:
                continue
        scene.render.resolution_x = scene.render.resolution_y = 700
        world = bpy.data.worlds.new("World")
        scene.world = world
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.1, 0.12, 0.18, 1)
        cam = bpy.data.cameras.new("Cam")
        cam_obj = bpy.data.objects.new("Cam", cam)
        scene.collection.objects.link(cam_obj)
        scene.camera = cam_obj
        allv = [obj.matrix_world @ Vector(c) for obj in bpy.data.objects
                if obj.type == "MESH" for c in obj.bound_box]
        center = sum(allv, Vector()) / max(1, len(allv))
        size = max((v - center).length for v in allv) if allv else 1.0
        key = bpy.data.lights.new("Key", "SUN")
        key_obj = bpy.data.objects.new("Key", key)
        scene.collection.objects.link(key_obj)
        key_obj.rotation_euler = (0.8, 0.2, 0.5)
        for label, off in {"front": Vector((0, -3, 0.6)), "f34": Vector((2, -2.4, 1.0))}.items():
            cam_obj.location = center + off * size
            look = center - cam_obj.location
            cam_obj.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out, "fx_%s.png" % label)
            bpy.ops.render.render(write_still=True)
            print("rendered", scene.render.filepath)


main()
