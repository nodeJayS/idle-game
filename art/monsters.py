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
    # zone 3 — Murkwater Swamp
    "toad":   (0.36, 0.48, 0.26),
    "toadbelly": (0.72, 0.74, 0.52),
    "toaddark": (0.24, 0.34, 0.18),
    "wisp":   (0.55, 0.95, 0.75),
    "wispcore": (0.85, 1.00, 0.90),
    "muck":   (0.22, 0.28, 0.18),
    "muckdark": (0.15, 0.20, 0.13),
    "sludge": (0.45, 0.55, 0.30),
    # zone 4 — Amber Dunes
    "chitin": (0.46, 0.30, 0.16),
    "chitindark": (0.30, 0.19, 0.11),
    "sand":   (0.82, 0.70, 0.46),
    "sandshadow": (0.60, 0.48, 0.30),
    # zone 5 — Frostpeak Tundra
    "ice":    (0.78, 0.88, 0.95),
    "icedeep": (0.52, 0.70, 0.88),
    "icecore": (0.70, 0.98, 1.00),
    "fur":    (0.86, 0.90, 0.95),
    "furdark": (0.46, 0.56, 0.70),
    # zone 6 — Ember Caldera
    "basalt": (0.22, 0.19, 0.18),
    "basaltdark": (0.14, 0.12, 0.11),
    "lava":   (1.00, 0.45, 0.12),
    "ash":    (0.45, 0.40, 0.38),
    # zone 7 — Gloom Hollow
    "shadow": (0.22, 0.20, 0.30),
    "shadowdeep": (0.13, 0.12, 0.19),
    "violet": (0.62, 0.42, 0.95),
    "batfur": (0.34, 0.30, 0.40),
}
# modest strengths: Unity's FBX import drops emission anyway (base colour carries
# the look there); these only need to READ in the eyeball renders, not blow out.
EMISSIVE = {"amber": 6.0, "ember": 8.0, "wisp": 0.8, "wispcore": 2.5, "sludge": 1.2,
            "icecore": 2.0, "lava": 3.0, "violet": 2.5}

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


def rock(name, radius, loc, material, scale=(1, 1, 1), seed=0, jitter=0.35, subdiv=1,
         taper=0.0):
    """A faceted boulder: coarse icosphere displaced by smooth noise (the same
    recipe as the client's Scenery rocks, authored here so monsters match).
    taper > 0 pinches the top toward a point (teardrop/flame silhouettes)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    off = Vector((seed * 13.7 + 5.1, seed * 7.3 + 9.2, seed * 3.9 + 2.4))
    for v in bm.verts:
        n = noise.noise(v.co * (1.5 / max(radius, 0.01)) + off)
        f = 1.0 + jitter * n
        v.co *= f
        if taper > 0 and v.co.z > 0:
            s = 1.0 - taper * (v.co.z / (radius * (1.0 + jitter)))
            v.co.x *= max(s, 0.05)
            v.co.y *= max(s, 0.05)
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


# --- zone 3: Murkwater Swamp ------------------------------------------------------

def build_bog_toad():
    """A squat swamp toad: wide flattened body, pale throat, bulging eyes on top,
    splayed forefeet, a couple of muck warts. ~0.7u tall, wide footprint."""
    # body: broad flattened mound, haunches high at the back
    rock("body", 0.38, (0, 0.03, 0.34), "toad", scale=(1.15, 1.0, 0.75), seed=31, jitter=0.18, subdiv=2)
    # pale throat/belly bulge at the front (-Y), slightly lower
    rock("throat", 0.24, (0, -0.24, 0.26), "toadbelly", scale=(1.1, 0.8, 0.75), seed=32, jitter=0.12)
    # haunch lumps
    rock("haunchL", 0.17, (0.34, 0.16, 0.26), "toaddark", scale=(0.9, 1.1, 0.9), seed=33, jitter=0.2)
    rock("haunchR", 0.17, (-0.34, 0.16, 0.26), "toaddark", scale=(0.9, 1.1, 0.9), seed=34, jitter=0.2)
    # bulging eyes on top, looking forward
    rock("eyeballL", 0.10, (0.15, -0.20, 0.62), "toaddark", seed=35, jitter=0.08)
    rock("eyeballR", 0.10, (-0.15, -0.20, 0.62), "toaddark", seed=36, jitter=0.08)
    rock("pupilL", 0.05, (0.16, -0.27, 0.64), "amber", seed=37, jitter=0.05)
    rock("pupilR", 0.05, (-0.16, -0.27, 0.64), "amber", seed=38, jitter=0.05)
    # splayed forefeet
    box("footL", (0.16, 0.20, 0.07), (0.26, -0.30, 0.05), "toaddark", rot=(0, 0, 0.35))
    box("footR", (0.16, 0.20, 0.07), (-0.26, -0.30, 0.05), "toaddark", rot=(0, 0, -0.35))
    # muck warts
    rock("wart1", 0.06, (0.18, 0.10, 0.58), "moss", seed=39, jitter=0.1)
    rock("wart2", 0.05, (-0.12, 0.22, 0.55), "moss", seed=40, jitter=0.1)


def build_marsh_wisp():
    """A hovering swamp spirit: a faceted teardrop shell around a blazing core,
    two trailing flame-wisps. Authored hovering (no feet) — bottom at ~0.25u."""
    # flame-teardrop body: wide base pinching to a tip
    rock("shell", 0.28, (0, 0, 0.60), "wisp", scale=(1.0, 1.0, 1.55), seed=41, jitter=0.14,
         subdiv=2, taper=0.85)
    # blazing core peeking through the front (-Y)
    rock("core", 0.14, (0, -0.16, 0.54), "wispcore", seed=42, jitter=0.10)
    # dark hollow "eyes" on the shell front, above the core
    rock("eyeL", 0.06, (0.10, -0.25, 0.70), "socket", seed=43, jitter=0.05)
    rock("eyeR", 0.06, (-0.10, -0.25, 0.70), "socket", seed=44, jitter=0.05)
    # trailing flame-wisps behind/below
    rock("tail1", 0.11, (0.18, 0.16, 0.36), "wisp", scale=(1.0, 1.0, 1.6), seed=45, jitter=0.2, taper=0.7)
    rock("tail2", 0.09, (-0.16, 0.20, 0.30), "wisp", scale=(1.0, 1.0, 1.5), seed=46, jitter=0.2, taper=0.7)


def build_bog_horror():
    """BOSS — a heaving mound of swamp muck: layered sludge body, moss shoulders,
    two hulking tendril-arms planted like pillars, a row of glowing sludge eyes
    over a gaping dark maw. ~1.9u tall, very wide."""
    # layered muck mound
    rock("base", 0.62, (0, 0, 0.44), "muckdark", scale=(1.25, 1.05, 0.75), seed=51, jitter=0.22, subdiv=2)
    rock("mid", 0.52, (0, 0.04, 0.95), "muck", scale=(1.1, 0.95, 0.85), seed=52, jitter=0.25, subdiv=2)
    rock("crown", 0.34, (0, 0.06, 1.52), "muck", scale=(1.0, 0.9, 0.8), seed=53, jitter=0.28)
    # moss cladding on the shoulders/crown
    rock("moss1", 0.36, (0.16, 0.10, 1.30), "moss", scale=(1.0, 0.8, 0.4), seed=54, jitter=0.3)
    rock("moss2", 0.28, (-0.22, 0.06, 1.42), "moss", scale=(1.0, 0.85, 0.4), seed=55, jitter=0.3)
    # pillar tendril-arms planted into the ground ahead
    rock("armL", 0.20, (0.66, -0.22, 0.62), "muck", scale=(0.85, 0.85, 1.9), seed=56, jitter=0.25)
    rock("armR", 0.20, (-0.66, -0.22, 0.62), "muck", scale=(0.85, 0.85, 1.9), seed=57, jitter=0.25)
    rock("fistL", 0.24, (0.68, -0.26, 0.16), "muckdark", scale=(1.1, 1.1, 0.7), seed=58, jitter=0.25)
    rock("fistR", 0.24, (-0.68, -0.26, 0.16), "muckdark", scale=(1.1, 1.1, 0.7), seed=59, jitter=0.25)
    # glowing sludge eyes (uneven row, front -Y, PROUD of the mid rock) over a dark maw
    rock("eye1", 0.09, (0.17, -0.50, 1.28), "sludge", seed=60, jitter=0.08)
    rock("eye2", 0.11, (0, -0.54, 1.37), "sludge", seed=61, jitter=0.08)
    rock("eye3", 0.08, (-0.18, -0.50, 1.25), "sludge", seed=62, jitter=0.08)
    rock("maw", 0.20, (0, -0.44, 0.98), "socket", scale=(1.3, 0.6, 0.8), seed=63, jitter=0.15)
    # dripping sludge blobs
    rock("drip1", 0.09, (0.34, -0.34, 0.62), "sludge", scale=(1.0, 1.0, 1.6), seed=64, jitter=0.2)
    rock("drip2", 0.07, (-0.30, -0.36, 0.56), "sludge", scale=(1.0, 1.0, 1.5), seed=65, jitter=0.2)


# --- zone 4: Amber Dunes ----------------------------------------------------------

def build_dust_scarab():
    """A faceted dune beetle: domed bronze shell with a gold seam band, stubby
    horned head, three leg slabs per side. ~0.55u tall, scuttles low."""
    # domed shell (longer than wide, squashed)
    rock("shell", 0.30, (0, 0.04, 0.30), "chitin", scale=(1.05, 1.25, 0.70), seed=71, jitter=0.14, subdiv=2)
    # dark centre seam ridge + gold band across the shell's front lip (conformal, thin)
    box("seam", (0.04, 0.52, 0.04), (0, 0.08, 0.50), "chitindark")
    box("band", (0.40, 0.05, 0.04), (0, -0.27, 0.32), "trim")
    # head knob with a slim up-curved horn and amber eye dots (faces -Y)
    rock("head", 0.13, (0, -0.36, 0.20), "chitindark", seed=72, jitter=0.12)
    box("horn", (0.035, 0.035, 0.15), (0, -0.44, 0.30), "bone", rot=(-0.75, 0, 0))
    rock("eyeL", 0.035, (0.07, -0.46, 0.22), "amber", seed=73, jitter=0.05)
    rock("eyeR", 0.035, (-0.07, -0.46, 0.22), "amber", seed=74, jitter=0.05)
    # three leg slabs per side, rooted under the shell rim, splayed gently
    for i, (y, rz) in enumerate([(-0.14, 0.35), (0.02, 0.50), (0.18, 0.65)]):
        box("legL%d" % i, (0.18, 0.05, 0.05), (0.22, y, 0.10), "chitindark", rot=(0, 0, rz))
        box("legR%d" % i, (0.18, 0.05, 0.05), (-0.22, y, 0.10), "chitindark", rot=(0, 0, -rz))


def build_dune_stalker():
    """A hooded sand-serpent risen from a coil: stacked coil base, tapering neck,
    flared hood flaps around an amber-eyed head with fangs. ~1.35u tall."""
    # coiled base (two squashed loops)
    rock("coil1", 0.34, (0, 0, 0.16), "sandshadow", scale=(1.25, 1.25, 0.50), seed=81, jitter=0.18, subdiv=2)
    rock("coil2", 0.26, (0.04, 0.04, 0.40), "sand", scale=(1.15, 1.15, 0.50), seed=82, jitter=0.18)
    # rising neck, pinched toward the top
    rock("neck", 0.15, (0, 0.02, 0.88), "sand", scale=(1.0, 1.0, 2.3), seed=83, jitter=0.12, taper=0.25)
    # hood: two flattened lobes hugging the head sides (rocks sit organically where
    # flat prism flaps kept reading as detached boards)
    rock("hoodL", 0.11, (0.14, 0.03, 1.26), "sandshadow", scale=(0.55, 1.15, 1.65), seed=87, jitter=0.12)
    rock("hoodR", 0.11, (-0.14, 0.03, 1.26), "sandshadow", scale=(0.55, 1.15, 1.65), seed=88, jitter=0.12)
    # head leaning forward with amber eyes + bone fangs (faces -Y)
    rock("head", 0.15, (0, -0.07, 1.30), "sand", scale=(1.0, 1.25, 0.9), seed=84, jitter=0.12)
    rock("eyeL", 0.045, (0.08, -0.22, 1.34), "amber", seed=85, jitter=0.05)
    rock("eyeR", 0.045, (-0.08, -0.22, 1.34), "amber", seed=86, jitter=0.05)
    box("fangL", (0.03, 0.03, 0.09), (0.06, -0.22, 1.18), "bone")
    box("fangR", (0.03, 0.03, 0.09), (-0.06, -0.22, 1.18), "bone")


def build_dune_wurm():
    """BOSS — a great worm bursting from the sand: a leaning arc of fat segments
    out of a sand mound, ending in a round maw ringed with bone teeth. ~2.4u."""
    # burst mound at the base
    rock("mound", 0.50, (0, 0, 0.10), "sandshadow", scale=(1.5, 1.5, 0.35), seed=91, jitter=0.25, subdiv=2)
    # segment arc, leaning forward (-Y) as it rises
    rock("seg1", 0.50, (0, 0.02, 0.55), "sandshadow", scale=(1.05, 1.05, 0.95), seed=92, jitter=0.16, subdiv=2)
    rock("seg2", 0.44, (0, -0.06, 1.20), "sand", scale=(1.0, 1.0, 0.95), seed=93, jitter=0.16, subdiv=2)
    rock("seg3", 0.38, (0, -0.20, 1.78), "sandshadow", scale=(1.0, 1.0, 0.90), seed=94, jitter=0.16)
    # head segment with the open maw facing forward-down at the party
    rock("head", 0.34, (0, -0.36, 2.18), "sand", scale=(1.05, 1.0, 0.9), seed=95, jitter=0.14, subdiv=2)
    rock("maw", 0.20, (0, -0.62, 2.12), "socket", scale=(1.05, 0.55, 1.05), seed=96, jitter=0.10)
    # ring of bone teeth around the maw rim
    import math as _m
    for i in range(6):
        a = i / 6.0 * 2.0 * _m.pi
        x, z = _m.cos(a) * 0.24, _m.sin(a) * 0.24
        box("tooth%d" % i, (0.055, 0.10, 0.055), (x, -0.60, 2.12 + z), "bone",
            rot=(0, a, 0))
    # a couple of sand chunks mid-air around the burst
    rock("chunk1", 0.10, (0.55, -0.15, 0.35), "sand", seed=97, jitter=0.3)
    rock("chunk2", 0.08, (-0.48, 0.20, 0.28), "sand", seed=98, jitter=0.3)


# --- zone 5: Frostpeak Tundra -------------------------------------------------------

def build_ice_sprite():
    """A hovering shard-imp: one tall faceted crystal with glowing eyes, two
    splayed shard arms, a ring of small shards below. Bottom at ~0.15u."""
    rock("body", 0.22, (0, 0, 0.55), "ice", scale=(1.0, 0.9, 1.9), seed=101, jitter=0.12,
         subdiv=2, taper=0.65)
    rock("eyeL", 0.045, (0.08, -0.17, 0.68), "icecore", seed=102, jitter=0.05)
    rock("eyeR", 0.045, (-0.08, -0.17, 0.68), "icecore", seed=103, jitter=0.05)
    # shard arms angled outward
    box("armL", (0.06, 0.06, 0.30), (0.26, 0, 0.52), "icedeep", taper_top=0.3, rot=(0, -0.5, 0))
    box("armR", (0.06, 0.06, 0.30), (-0.26, 0, 0.52), "icedeep", taper_top=0.3, rot=(0, 0.5, 0))
    # orbiting shard ring near the base
    box("shard1", (0.05, 0.05, 0.16), (0.20, -0.12, 0.22), "icedeep", taper_top=0.2, rot=(0.2, 0, 0.3))
    box("shard2", (0.05, 0.05, 0.14), (-0.22, -0.06, 0.20), "icedeep", taper_top=0.2, rot=(-0.1, 0, -0.35))
    box("shard3", (0.05, 0.05, 0.15), (0.02, 0.22, 0.21), "icedeep", taper_top=0.2, rot=(0.3, 0, 0.05))


def build_frost_wolf():
    """A lean tundra wolf: long low body, dark back mane, snouted head with
    pricked ears and ice-blue eyes, four legs, a swept tail. ~0.85u tall."""
    rock("body", 0.26, (0, 0.06, 0.48), "fur", scale=(0.95, 1.75, 0.95), seed=111, jitter=0.12, subdiv=2)
    rock("mane", 0.20, (0, 0.14, 0.68), "furdark", scale=(0.95, 1.30, 0.60), seed=112, jitter=0.18)
    # head + snout + nose (faces -Y)
    rock("head", 0.16, (0, -0.44, 0.66), "fur", scale=(1.0, 1.1, 1.0), seed=113, jitter=0.10)
    box("snout", (0.11, 0.18, 0.09), (0, -0.60, 0.60), "furdark")
    box("nose", (0.05, 0.04, 0.04), (0, -0.70, 0.62), "socket")
    # pricked ears
    box("earL", (0.05, 0.04, 0.12), (0.09, -0.38, 0.84), "furdark", taper_top=0.25)
    box("earR", (0.05, 0.04, 0.12), (-0.09, -0.38, 0.84), "furdark", taper_top=0.25)
    rock("eyeL", 0.035, (0.09, -0.55, 0.70), "icecore", seed=114, jitter=0.05)
    rock("eyeR", 0.035, (-0.09, -0.55, 0.70), "icecore", seed=115, jitter=0.05)
    # four legs planted on the ground
    for name, x, y in (("legFL", 0.14, -0.26), ("legFR", -0.14, -0.26), ("legBL", 0.14, 0.34), ("legBR", -0.14, 0.34)):
        box(name, (0.08, 0.08, 0.42), (x, y, 0.21), "fur", taper_top=0.8)
    # swept tail
    rock("tail", 0.09, (0, 0.52, 0.62), "furdark", scale=(0.8, 1.9, 0.8), seed=116, jitter=0.15)


def build_glacier_golem():
    """BOSS — a glacial boulder golem: ice-rock feet/torso/head like the Stone
    Sentry's language but blue glacial ice, crystal spikes on the shoulders and
    back, a blazing cyan eye band. ~2.3u tall."""
    rock("footL", 0.24, (0.28, 0, 0.20), "icedeep", scale=(1.0, 1.15, 0.75), seed=121, jitter=0.2)
    rock("footR", 0.24, (-0.28, 0, 0.20), "icedeep", scale=(1.0, 1.15, 0.75), seed=122, jitter=0.2)
    rock("torso", 0.58, (0, 0, 1.02), "ice", scale=(1.1, 0.9, 1.0), seed=123, jitter=0.26, subdiv=2)
    rock("head", 0.30, (0, -0.04, 1.78), "ice", scale=(1.0, 0.95, 0.85), seed=124, jitter=0.2, subdiv=2)
    box("eye", (0.22, 0.20, 0.07), (0, -0.28, 1.78), "icecore") # deep enough to stay proud of the facets
    # hanging arm boulders
    rock("armL", 0.22, (0.70, 0.02, 0.78), "icedeep", scale=(0.9, 0.9, 1.6), seed=125, jitter=0.24)
    rock("armR", 0.22, (-0.70, 0.02, 0.78), "icedeep", scale=(0.9, 0.9, 1.6), seed=126, jitter=0.24)
    # crystal spikes rooted in the shoulders, tilted so the tips clear the crown
    box("spikeL", (0.11, 0.11, 0.50), (0.40, 0.06, 1.56), "icecore", taper_top=0.12, rot=(0, 0, -0.45))
    box("spikeR", (0.11, 0.11, 0.50), (-0.40, 0.06, 1.56), "icecore", taper_top=0.12, rot=(0, 0, 0.45))
    box("spikeB1", (0.09, 0.09, 0.36), (0.10, 0.32, 1.44), "icedeep", taper_top=0.15, rot=(0.45, 0, 0.1))
    box("spikeB2", (0.08, 0.08, 0.30), (-0.14, 0.34, 1.30), "icedeep", taper_top=0.15, rot=(0.5, 0, -0.1))


# --- zone 6: Ember Caldera ----------------------------------------------------------

def build_magma_imp():
    """A squat basalt imp: dark cracked body with a lava seam glowing through,
    horned head, ember eyes, stubby limbs. ~0.85u tall."""
    rock("body", 0.24, (0, 0, 0.40), "basalt", scale=(1.05, 0.95, 1.05), seed=131, jitter=0.20, subdiv=2)
    # lava seam glowing through the chest (faces -Y)
    rock("seam", 0.10, (0, -0.18, 0.40), "lava", scale=(1.2, 0.6, 1.6), seed=132, jitter=0.15)
    rock("head", 0.16, (0, -0.02, 0.74), "basalt", seed=133, jitter=0.16)
    # swept-back horns
    box("hornL", (0.05, 0.05, 0.16), (0.10, 0.04, 0.90), "basaltdark", taper_top=0.2, rot=(0.5, 0, -0.35))
    box("hornR", (0.05, 0.05, 0.16), (-0.10, 0.04, 0.90), "basaltdark", taper_top=0.2, rot=(0.5, 0, 0.35))
    rock("eyeL", 0.04, (0.07, -0.15, 0.78), "lava", seed=134, jitter=0.05)
    rock("eyeR", 0.04, (-0.07, -0.15, 0.78), "lava", seed=135, jitter=0.05)
    # stubby limbs
    box("armL", (0.07, 0.07, 0.22), (0.26, -0.04, 0.36), "basaltdark", rot=(0.2, 0, -0.4))
    box("armR", (0.07, 0.07, 0.22), (-0.26, -0.04, 0.36), "basaltdark", rot=(0.2, 0, 0.4))
    box("legL", (0.09, 0.10, 0.16), (0.12, 0, 0.09), "basaltdark", taper_top=0.75)
    box("legR", (0.09, 0.10, 0.16), (-0.12, 0, 0.09), "basaltdark", taper_top=0.75)


def build_cinder_hound():
    """A charcoal hound on the frost-wolf chassis: basalt body, a GLOWING lava
    ridge for a mane, ember eyes, ash snout, lava-tipped tail. ~0.85u tall."""
    rock("body", 0.26, (0, 0.06, 0.48), "basalt", scale=(0.95, 1.75, 0.95), seed=141, jitter=0.14, subdiv=2)
    rock("mane", 0.19, (0, 0.12, 0.70), "lava", scale=(0.75, 1.35, 0.55), seed=142, jitter=0.20)
    rock("head", 0.16, (0, -0.44, 0.66), "basalt", scale=(1.0, 1.1, 1.0), seed=143, jitter=0.12)
    box("snout", (0.11, 0.18, 0.09), (0, -0.60, 0.60), "ash")
    box("nose", (0.05, 0.04, 0.04), (0, -0.70, 0.62), "socket")
    box("earL", (0.05, 0.04, 0.12), (0.09, -0.38, 0.84), "basaltdark", taper_top=0.25)
    box("earR", (0.05, 0.04, 0.12), (-0.09, -0.38, 0.84), "basaltdark", taper_top=0.25)
    rock("eyeL", 0.035, (0.09, -0.55, 0.70), "ember", seed=144, jitter=0.05)
    rock("eyeR", 0.035, (-0.09, -0.55, 0.70), "ember", seed=145, jitter=0.05)
    for name, x, y in (("legFL", 0.14, -0.26), ("legFR", -0.14, -0.26), ("legBL", 0.14, 0.34), ("legBR", -0.14, 0.34)):
        box(name, (0.08, 0.08, 0.42), (x, y, 0.21), "basaltdark", taper_top=0.8)
    rock("tail", 0.08, (0, 0.52, 0.64), "basaltdark", scale=(0.8, 1.8, 0.8), seed=146, jitter=0.15)
    rock("tailtip", 0.06, (0, 0.66, 0.68), "lava", seed=147, jitter=0.1)


def build_ash_tyrant():
    """BOSS — a basalt demon-lord: cracked boulder bulk over planted legs, lava
    veins burning through the chest and shoulders, great trim horns, ember
    eyes. ~2.3u tall."""
    rock("footL", 0.24, (0.28, 0, 0.20), "basaltdark", scale=(1.0, 1.15, 0.75), seed=151, jitter=0.2)
    rock("footR", 0.24, (-0.28, 0, 0.20), "basaltdark", scale=(1.0, 1.15, 0.75), seed=152, jitter=0.2)
    rock("torso", 0.56, (0, 0, 1.00), "basalt", scale=(1.15, 0.9, 1.0), seed=153, jitter=0.24, subdiv=2)
    # lava veins burning through the chest + shoulder cracks (front -Y)
    rock("vein1", 0.16, (0.10, -0.42, 1.10), "lava", scale=(1.4, 0.5, 1.8), seed=154, jitter=0.15)
    rock("vein2", 0.10, (-0.20, -0.40, 0.86), "lava", scale=(1.3, 0.5, 1.5), seed=155, jitter=0.15)
    rock("head", 0.28, (0, -0.04, 1.74), "basalt", scale=(1.0, 0.95, 0.85), seed=156, jitter=0.18, subdiv=2)
    # great horns sweeping up-outward
    prism("hornL", [(0, 0), (0.22, 0.08), (0.42, 0.34), (0.16, 0.09)], 0.07,
          (0.16, 0, 1.88), "trim", axis="y")
    prism("hornR", [(0, 0), (-0.22, 0.08), (-0.42, 0.34), (-0.16, 0.09)], 0.07,
          (-0.16, 0, 1.88), "trim", axis="y")
    rock("eyeL", 0.06, (0.10, -0.27, 1.78), "ember", seed=157, jitter=0.05)
    rock("eyeR", 0.06, (-0.10, -0.27, 1.78), "ember", seed=158, jitter=0.05)
    # heavy arms with lava-cracked fists
    rock("armL", 0.21, (0.68, 0.02, 0.86), "basaltdark", scale=(0.9, 0.9, 1.7), seed=159, jitter=0.22)
    rock("armR", 0.21, (-0.68, 0.02, 0.86), "basaltdark", scale=(0.9, 0.9, 1.7), seed=160, jitter=0.22)
    rock("fistL", 0.16, (0.69, -0.02, 0.44), "lava", seed=161, jitter=0.15)
    rock("fistR", 0.16, (-0.69, -0.02, 0.44), "lava", seed=162, jitter=0.15)


# --- zone 7: Gloom Hollow -------------------------------------------------------------

def build_cave_bat():
    """A hovering cave bat: plump furry body, big membrane wings, tall ears,
    violet eyes, tiny fangs. Authored hovering — bottom at ~0.35u."""
    rock("body", 0.17, (0, 0, 0.60), "batfur", scale=(1.0, 0.95, 1.15), seed=171, jitter=0.14, subdiv=2)
    # tall ears
    box("earL", (0.06, 0.05, 0.16), (0.08, 0.02, 0.80), "shadowdeep", taper_top=0.2, rot=(0, 0, -0.15))
    box("earR", (0.06, 0.05, 0.16), (-0.08, 0.02, 0.80), "shadowdeep", taper_top=0.2, rot=(0, 0, 0.15))
    rock("eyeL", 0.04, (0.07, -0.15, 0.64), "violet", seed=172, jitter=0.05)
    rock("eyeR", 0.04, (-0.07, -0.15, 0.64), "violet", seed=173, jitter=0.05)
    box("fangL", (0.025, 0.025, 0.06), (0.04, -0.15, 0.50), "bone")
    box("fangR", (0.025, 0.025, 0.06), (-0.04, -0.15, 0.50), "bone")
    # big membrane wings: scalloped flat prisms swept up-outward
    prism("wingL", [(0, -0.10), (0.34, -0.24), (0.52, -0.08), (0.44, 0.10), (0.20, 0.16)], 0.03,
          (0.12, 0.04, 0.62), "shadowdeep", axis="y", rot=(0, 0, 0.35))
    prism("wingR", [(0, -0.10), (-0.34, -0.24), (-0.52, -0.08), (-0.44, 0.10), (-0.20, 0.16)], 0.03,
          (-0.12, 0.04, 0.62), "shadowdeep", axis="y", rot=(0, 0, -0.35))


def build_gloom_shade():
    """A floating wraith: a shadow-robe mass tapering to a wisp below, a deep
    hood with two burning violet eyes, thin reaching arms. Bottom ~0.25u."""
    # robe mass pinching upward + a lower wisp tail
    rock("robe", 0.26, (0, 0, 0.72), "shadow", scale=(1.0, 0.9, 1.6), seed=181, jitter=0.16, subdiv=2, taper=0.35)
    rock("wispt", 0.14, (0.02, 0.04, 0.32), "shadowdeep", scale=(0.9, 0.9, 1.6), seed=182, jitter=0.2, taper=-0.0)
    # deep hood: dark shell + a void face + violet eyes (faces -Y)
    rock("hood", 0.17, (0, 0.01, 1.14), "shadowdeep", scale=(1.05, 1.05, 1.1), seed=183, jitter=0.14)
    rock("face", 0.11, (0, -0.10, 1.12), "socket", seed=184, jitter=0.08)
    rock("eyeL", 0.04, (0.06, -0.19, 1.16), "violet", seed=185, jitter=0.05)
    rock("eyeR", 0.04, (-0.06, -0.19, 1.16), "violet", seed=186, jitter=0.05)
    # thin reaching arms
    box("armL", (0.05, 0.05, 0.30), (0.24, -0.14, 0.78), "shadow", taper_top=0.4, rot=(0.5, 0, -0.5))
    box("armR", (0.05, 0.05, 0.26), (-0.24, -0.10, 0.72), "shadow", taper_top=0.4, rot=(0.4, 0, 0.5))


def build_nightmare_maw():
    """BOSS — a vast floating void-head: a shadow sphere that is mostly MOUTH —
    a gaping dark maw ringed with bone teeth, three burning violet eyes above,
    horn fins swept back, a wisp tail below. Hovers, ~2.0u tall."""
    rock("head", 0.55, (0, 0, 1.15), "shadowdeep", scale=(1.05, 1.0, 0.95), seed=191, jitter=0.16, subdiv=2)
    # the maw: a huge void crater on the front (-Y)
    rock("maw", 0.34, (0, -0.42, 1.05), "socket", scale=(1.1, 0.55, 1.0), seed=192, jitter=0.10, subdiv=2)
    # teeth ring around the maw rim
    import math as _m
    for i in range(8):
        a = i / 8.0 * 2.0 * _m.pi
        x, z = _m.cos(a) * 0.40, _m.sin(a) * 0.34
        box("tooth%d" % i, (0.06, 0.12, 0.06), (x, -0.46, 1.05 + z), "bone", rot=(0, a, 0))
    # three burning eyes above the maw, uneven
    rock("eye1", 0.07, (0.16, -0.44, 1.52), "violet", seed=193, jitter=0.05)
    rock("eye2", 0.09, (0, -0.48, 1.60), "violet", seed=194, jitter=0.05)
    rock("eye3", 0.06, (-0.17, -0.43, 1.50), "violet", seed=195, jitter=0.05)
    # swept horn fins + a wisp tail below
    prism("finL", [(0, 0), (0.20, 0.10), (0.44, 0.34), (0.14, 0.10)], 0.06,
          (0.38, 0.24, 1.55), "shadow", axis="y")
    prism("finR", [(0, 0), (-0.20, 0.10), (-0.44, 0.34), (-0.14, 0.10)], 0.06,
          (-0.38, 0.24, 1.55), "shadow", axis="y")
    rock("tail", 0.20, (0, 0.10, 0.42), "shadow", scale=(0.9, 0.9, 1.8), seed=196, jitter=0.2, taper=0.5)


MONSTERS = {
    # zone 2 — Ruined Courtyard (trash, trash, boss)
    "bone_rattler": (build_bone_rattler, 0.55, 2.1),
    "stone_sentry": (build_stone_sentry, 0.70, 2.6),
    "grave_knight": (build_grave_knight, 1.10, 3.6),
    # zone 3 — Murkwater Swamp
    "bog_toad": (build_bog_toad, 0.40, 2.2),
    "marsh_wisp": (build_marsh_wisp, 0.60, 2.0),
    "bog_horror": (build_bog_horror, 0.95, 3.8),
    # zone 4 — Amber Dunes
    "dust_scarab": (build_dust_scarab, 0.35, 2.0),
    "dune_stalker": (build_dune_stalker, 0.70, 2.6),
    "dune_wurm": (build_dune_wurm, 1.20, 4.2),
    # zone 5 — Frostpeak Tundra
    "ice_sprite": (build_ice_sprite, 0.55, 2.0),
    "frost_wolf": (build_frost_wolf, 0.50, 2.4),
    "glacier_golem": (build_glacier_golem, 1.15, 4.0),
    # zone 6 — Ember Caldera
    "magma_imp": (build_magma_imp, 0.50, 2.0),
    "cinder_hound": (build_cinder_hound, 0.50, 2.4),
    "ash_tyrant": (build_ash_tyrant, 1.15, 4.0),
    # zone 7 — Gloom Hollow
    "cave_bat": (build_cave_bat, 0.60, 2.0),
    "gloom_shade": (build_gloom_shade, 0.75, 2.4),
    "nightmare_maw": (build_nightmare_maw, 1.15, 4.0),
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
