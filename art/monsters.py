# Zone monsters — LOW-POLY FACETED scripted models (roadmap 4, the Tunic look).
# HARD ART RULE (2026-07-02): monsters/world/props are faceted flat-shaded like the
# world scenery; the MS2 pipeline is for HEROES ONLY. The smooth-chibi-hero vs
# faceted-world contrast IS the look.
#
#   blender -b --python art/monsters.py -- --monster bone_rattler --renders <dir>
#   blender -b --python art/monsters.py -- --monster bone_rattler --export <file.fbx>
#
# One monster per invocation (the scene is reset per run). ALWAYS eyeball
# --renders before --export.
#
# Conventions (same as the hero scripts):
#  - Blender Z-up, the monster faces -Y; palette authored in sRGB, converted to
#    linear at material build. Feet at z=0, true world scale (trash ~1.1u tall).
#  - Export: root-level MESH objects only; Unity's MonsterModel.cs instantiates
#    the FBX as one rigid group (no rig — motion is the view transform).

import sys
import bpy
import bmesh
from mathutils import Vector, noise

# --- palette (sRGB) -------------------------------------------------------------
COLORS = {
    "bone":   (0.93, 0.89, 0.80),
    "oldbone": (0.78, 0.72, 0.60),
    "socket": (0.10, 0.09, 0.08),
    "stone":  (0.55, 0.56, 0.58),
    "darkstone": (0.42, 0.43, 0.46),
    "moss":   (0.40, 0.50, 0.28),
    "amber":  (1.00, 0.65, 0.15),
    "iron":   (0.23, 0.24, 0.28),
    "trim":   (0.72, 0.58, 0.25),
    "ember":  (1.00, 0.35, 0.10),
    "cape":   (0.30, 0.12, 0.14),
}
EMISSIVE = {"amber": 6.0, "ember": 8.0}  # glow strength for the "eye" materials

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
        bsdf.inputs["Roughness"].default_value = 0.95
        if name in EMISSIVE:
            try:
                bsdf.inputs["Emission Color"].default_value = (*c, 1.0)
                bsdf.inputs["Emission Strength"].default_value = EMISSIVE[name]
            except KeyError:
                pass
    m.diffuse_color = (*c, 1.0)
    _materials[name] = m
    return m


PARTS = []


def register(obj, material):
    """FLAT shading always — facets are the style (opposite of the hero scripts)."""
    obj.data.materials.append(mat(material))
    for p in obj.data.polygons:
        p.use_smooth = False
    PARTS.append(obj)
    return obj


def rock(name, radius, loc, material, scale=(1, 1, 1), seed=0, jitter=0.35, subdiv=1):
    """A faceted boulder: coarse icosphere displaced by smooth noise (the same
    recipe as the client's Scenery rocks, authored here so monsters match)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    off = Vector((seed * 13.7 + 5.1, seed * 7.3 + 9.2, seed * 3.9 + 2.4))
    for v in bm.verts:
        n = noise.noise(v.co * (1.5 / max(radius, 0.01)) + off)
        f = 1.0 + jitter * n
        v.co *= f
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.location = loc
    obj.scale = scale
    bpy.context.scene.collection.objects.link(obj)
    return register(obj, material)


def box(name, size, loc, material, taper_top=1.0, rot=(0, 0, 0)):
    """A crisp faceted slab (no bevel — hard edges read as Tunic)."""
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
    obj.rotation_euler = rot
    bpy.context.scene.collection.objects.link(obj)
    return register(obj, material)


def prism(name, outline, depth, loc, material, axis="x", rot=(0, 0, 0)):
    """Extruded polygon (flat, crisp): outline in the plane ⊥ to `axis`."""
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
    obj.rotation_euler = rot
    bpy.context.scene.collection.objects.link(obj)
    return register(obj, material)


# --- zone 2: Ruined Courtyard ---------------------------------------------------

def build_bone_rattler():
    """A hunched little bone-imp: oversized skull on a mound of old bones,
    stubby legs, one raised claw of bone shards. ~1.0u tall."""
    # bone mound body (hunched forward)
    rock("body", 0.30, (0, 0.02, 0.34), "oldbone", scale=(1.0, 0.85, 0.95), seed=3, jitter=0.25)
    # rib shards wrapped around the mound front (embedded, tips peeking out)
    box("rib1", (0.26, 0.04, 0.04), (0, -0.16, 0.42), "bone", rot=(0.35, 0.20, 0))
    box("rib2", (0.28, 0.04, 0.04), (0, -0.18, 0.34), "bone", rot=(0.30, -0.15, 0))
    box("rib3", (0.24, 0.04, 0.04), (0, -0.17, 0.26), "bone", rot=(0.35, 0.10, 0))
    # skull — big, slightly flattened, faceted
    rock("skull", 0.26, (0, -0.06, 0.76), "bone", scale=(1.0, 0.95, 0.85), seed=7, jitter=0.10, subdiv=2)
    # jaw
    box("jaw", (0.16, 0.12, 0.05), (0, -0.16, 0.60), "bone", taper_top=1.2)
    # eye sockets (dark, sunk into the face — faces -Y)
    rock("socketL", 0.065, (0.095, -0.26, 0.79), "socket", seed=1, jitter=0.05)
    rock("socketR", 0.065, (-0.095, -0.26, 0.79), "socket", seed=2, jitter=0.05)
    # stub legs
    box("legL", (0.09, 0.10, 0.17), (0.13, 0, 0.10), "oldbone", taper_top=0.7)
    box("legR", (0.09, 0.10, 0.17), (-0.13, 0, 0.10), "oldbone", taper_top=0.7)
    # raised claw arm — rooted IN the mound, shards overlapping the arm tip
    box("armL", (0.05, 0.05, 0.26), (0.24, -0.10, 0.50), "bone", rot=(0.35, 0, -0.55))
    box("clawL1", (0.04, 0.04, 0.13), (0.33, -0.17, 0.66), "bone", rot=(0.5, 0, -0.35))
    box("clawL2", (0.04, 0.04, 0.12), (0.36, -0.11, 0.64), "bone", rot=(0.25, 0, -0.75))
    # trailing arm bone, embedded at the mound's flank
    box("armR", (0.05, 0.05, 0.20), (-0.22, 0.04, 0.34), "oldbone", rot=(-0.2, 0, 0.5))


def build_stone_sentry():
    """A stacked-boulder golem: two rock feet, a massive mossy torso boulder,
    a head rock with one amber eye band, hanging arm boulders. ~1.35u tall."""
    rock("footL", 0.18, (0.20, 0, 0.14), "darkstone", scale=(1.0, 1.15, 0.75), seed=11)
    rock("footR", 0.18, (-0.20, 0, 0.14), "darkstone", scale=(1.0, 1.15, 0.75), seed=12)
    rock("torso", 0.42, (0, 0, 0.66), "stone", scale=(1.05, 0.9, 0.95), seed=13, jitter=0.30, subdiv=2)
    # moss caps on the torso + head tops (the Scenery rocks' moss language)
    rock("moss1", 0.30, (0.05, 0.03, 0.94), "moss", scale=(1.0, 0.85, 0.35), seed=14, jitter=0.25)
    rock("head", 0.24, (0, -0.02, 1.16), "stone", scale=(1.0, 0.95, 0.85), seed=15, jitter=0.22, subdiv=2)
    rock("moss2", 0.16, (0.03, 0.02, 1.32), "moss", scale=(1.0, 0.9, 0.4), seed=16, jitter=0.25)
    # one amber eye band rooted in the head rock but proud of its facets (faces -Y)
    box("eye", (0.16, 0.14, 0.055), (0, -0.20, 1.16), "amber")
    # hanging arm boulders
    rock("armL", 0.17, (0.48, 0.02, 0.52), "darkstone", scale=(0.9, 0.9, 1.5), seed=17, jitter=0.28)
    rock("armR", 0.17, (-0.48, 0.02, 0.52), "darkstone", scale=(0.9, 0.9, 1.5), seed=18, jitter=0.28)


def build_grave_knight():
    """BOSS — a hulking armored revenant: dark iron slab body, horned helm with an
    ember visor slit, slab greatsword, tattered cape. ~2.1u tall. Every part
    OVERLAPS its neighbor (box sizes are full extents — mind the spans)."""
    # legs planted on the ground (span 0..0.95, tucked under the torso)
    box("legL", (0.22, 0.26, 0.95), (0.25, 0, 0.48), "iron", taper_top=0.8)
    box("legR", (0.22, 0.26, 0.95), (-0.25, 0, 0.48), "iron", taper_top=0.8)
    # torso 0.91..1.65, wide flared shoulders; belt overlaps the waist
    box("torso", (0.46, 0.32, 0.74), (0, 0, 1.28), "iron", taper_top=1.45)
    box("belt", (0.42, 0.30, 0.10), (0, 0, 0.95), "trim")
    # pauldron chunks sunk onto the shoulder corners
    rock("pauldL", 0.24, (0.42, 0, 1.62), "iron", scale=(1.0, 0.9, 0.8), seed=21, jitter=0.20)
    rock("pauldR", 0.24, (-0.42, 0, 1.62), "iron", scale=(1.0, 0.9, 0.8), seed=22, jitter=0.20)
    # helm 1.63..2.09 overlapping the torso top; horns from the helm crown
    box("helm", (0.32, 0.30, 0.46), (0, 0, 1.86), "iron", taper_top=0.78)
    prism("hornL", [(0, 0), (0.18, 0.06), (0.34, 0.26), (0.13, 0.07)], 0.06,
          (0.14, 0, 2.00), "trim", axis="y")
    prism("hornR", [(0, 0), (-0.18, 0.06), (-0.34, 0.26), (-0.13, 0.07)], 0.06,
          (-0.14, 0, 2.00), "trim", axis="y")
    box("visor", (0.20, 0.05, 0.05), (0, -0.14, 1.88), "ember")
    # arms hanging from the pauldrons (0.90..1.58)
    box("armL", (0.15, 0.16, 0.68), (0.48, 0, 1.24), "iron", taper_top=0.85)
    box("armR", (0.15, 0.16, 0.68), (-0.48, 0, 1.24), "iron", taper_top=0.85)
    # slab greatsword at the right hand: grip in fist, guard below it, blade
    # running point-down THROUGH the guard to the ground line
    box("grip", (0.07, 0.07, 0.22), (-0.52, -0.14, 1.10), "iron")
    box("guard", (0.30, 0.09, 0.07), (-0.52, -0.14, 0.97), "trim")
    box("blade", (0.13, 0.05, 0.85), (-0.52, -0.14, 0.55), "stone", taper_top=1.25)
    # tattered cape hanging from the shoulders at the back (+Y)
    prism("cape", [(-0.40, 0), (0.40, 0), (0.32, -1.20), (0.12, -0.98), (-0.07, -1.26), (-0.34, -1.05)],
          0.04, (0, 0.22, 1.62), "cape", axis="y")


MONSTERS = {
    # zone 2 — Ruined Courtyard (trash, trash, boss)
    "bone_rattler": (build_bone_rattler, 0.55, 2.1),
    "stone_sentry": (build_stone_sentry, 0.70, 2.6),
    "grave_knight": (build_grave_knight, 1.10, 3.6),
}

# --- render / export -------------------------------------------------------------


def setup_scene(look_z, dist):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = scene.render.resolution_y = 700
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
    target.location = (0, 0, look_z)
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
    cam_obj = tracked(bpy.data.objects.new("Cam", cam), (dist * 0.63, -dist * 0.77, look_z + dist * 0.45))
    scene.camera = cam_obj
    return cam_obj, dist, look_z


def render_views(cam_obj, dist, look_z, name, out_dir):
    views = {
        "%s_f34" % name: (dist * 0.63, -dist * 0.77, look_z + dist * 0.45),
        "%s_front" % name: (0.0, -dist, look_z + dist * 0.20),
        "%s_b34" % name: (-dist * 0.63, dist * 0.77, look_z + dist * 0.45),
    }
    scene = bpy.context.scene
    for vname, loc in views.items():
        cam_obj.location = loc
        scene.render.filepath = "%s/%s.png" % (out_dir, vname)
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def export_fbx(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=False,
    )
    print("exported", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--monster" not in argv:
        print("usage: -- --monster <id> [--renders <dir>] [--export <file.fbx>]")
        print("known:", ", ".join(sorted(MONSTERS)))
        return
    name = argv[argv.index("--monster") + 1]
    build, look_z, dist = MONSTERS[name]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    build()

    if "--export" in argv:
        export_fbx(argv[argv.index("--export") + 1])
    if "--renders" in argv:
        cam, d, lz = setup_scene(look_z, dist)
        render_views(cam, d, lz, name, argv[argv.index("--renders") + 1])


main()
