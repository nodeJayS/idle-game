#nullable enable
using System.Collections;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// First-use warmup (10.12c2): kills the mid-play pop-in/hitches a weak GPU shows the
    /// first time something lazily built gets rendered. Two causes, two passes:
    ///
    /// 1. LAZY TEXTURE/CACHE BAKES — GroundDetail styles bake on first zone use (a campaign
    ///    zone crossing has NO loading screen — stage advances re-dress in place, only the
    ///    tower/crypt transitions run LoadingScreen), and the FxKit sprite/material/mesh
    ///    caches build on the first skill or projectile. All of them are permanent caches,
    ///    so forcing them now just moves the cost to boot.
    ///
    /// 2. SHADER-VARIANT COMPILES — URP compiles a program the first time a material+keyword
    ///    combo is DRAWN. TunicSurface warms itself behind the main menu (the world renders
    ///    under it) and DungeonLit warms under the crypt LoadingScreen hold, but FxAdditive
    ///    (first halo), Lit+_EMISSION (first crystal FX) and SdfBlendShell (first SDF-family
    ///    monster) all first render mid-combat. We warm them by actually RENDERING each once:
    ///    a hidden rig of quads far below the world, drawn by a throwaway camera into a tiny
    ///    RenderTexture for two frames. Chosen over ShaderWarmupUtility/ShaderWarmup because
    ///    the experimental API needs the exact keyword set + vertex layout per variant to be
    ///    effective (URP's multi_compile space makes that list fragile) and on DX12/Vulkan a
    ///    real draw is the only warm that reliably builds the full PSO. A draw is a draw —
    ///    the RT target keeps it invisible.
    ///
    /// Runs ONCE from Bootstrap.Boot, behind the main menu (session start has no loading
    /// screen — the menu is the cover). Total cost: ~tens of ms of bakes + two 64px camera
    /// frames; the rig, camera and RT are destroyed when done. Additive — no baker changed.
    /// </summary>
    public sealed class Prewarm : MonoBehaviour
    {
        public static void Run() => new GameObject("Prewarm").AddComponent<Prewarm>();

        private void Start() => StartCoroutine(WarmThenDispose());

        private IEnumerator WarmThenDispose()
        {
            // ---- pass 1: force every lazy permanent cache to exist now ----
            foreach (GroundDetail.Style style in System.Enum.GetValues(typeof(GroundDetail.Style)))
                GroundDetail.Get(style); // all five zone detail maps (~85 KB each, kept forever anyway)
            FxKit.SoftDot();             // the shared 64px glow sprite
            UiKit.CircleSprite();        // avatar/handle circle (TopBar would bake it at session start)

            // ---- pass 2: render one instance of each cold shader/variant ----
            var rig = new GameObject("PrewarmRig");
            rig.transform.position = new Vector3(0f, -500f, 0f); // far below the world; only our camera looks

            // FxAdditive — the halo shader; the tint is the ice-halo color so the material
            // cache entry itself is also the one the first real projectile will fetch.
            Quad(rig, 0, FxKit.AdditiveMat(new Color(0.25f, 0.5f, 1f, 0.2f)));
            // URP Lit + _EMISSION — a DIFFERENT variant from the plain Lit already on screen;
            // warmed on the ice-shard mesh/material pair the first real cast would build.
            MeshPart(rig, 1, FxKit.Crystal(0.07f, 0.24f, 0.14f),
                     FxKit.CrystalMat(new Color(0.16f, 0.42f, 0.85f), new Color(0.05f, 0.2f, 0.7f) * 0.5f));
            // SDF blend-shell + DungeonLit: a bare material on a quad is enough — variant
            // compilation happens at draw submission, whatever the fragments evaluate to.
            // These two materials are throwaways (unlike the FxKit ones above, which are the
            // real cache entries), so they're destroyed with the rig.
            var throwaway = new System.Collections.Generic.List<Material>(2);
            ShaderQuad(rig, 2, "IdleGame/SdfBlendShell", throwaway);
            ShaderQuad(rig, 3, "IdleGame/DungeonLit", throwaway);

            var camGo = new GameObject("PrewarmCam");
            camGo.transform.position = rig.transform.position + new Vector3(1.5f, 0f, -4f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            var rt = RenderTexture.GetTemporary(64, 64, 24);
            cam.targetTexture = rt; // never touches the screen

            yield return null;
            yield return null; // two rendered frames — submission compiles the PSOs

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(camGo);
            Destroy(rig);
            foreach (var m in throwaway) Destroy(m); // rig destruction doesn't destroy materials
            Destroy(gameObject); // one-shot: nothing of the warmup survives
        }

        // ---- rig pieces (spaced along +x so every quad is in the camera's view) ----

        private static void Quad(GameObject rig, int slot, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.name = "warm:" + mat.shader.name;
            go.transform.SetParent(rig.transform, false);
            go.transform.localPosition = new Vector3(slot * 1.2f, 0f, 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void MeshPart(GameObject rig, int slot, Mesh mesh, Material mat)
        {
            var go = new GameObject("warm:" + mat.shader.name);
            go.transform.SetParent(rig.transform, false);
            go.transform.localPosition = new Vector3(slot * 1.2f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void ShaderQuad(GameObject rig, int slot, string shaderName,
                                       System.Collections.Generic.List<Material> throwaway)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) return; // shader stripped/missing — nothing to warm
            var m = new Material(sh);
            throwaway.Add(m);
            Quad(rig, slot, m);
        }
    }
}
