#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Scripted-Blender LOW-POLY monster models (art/monsters.py →
    /// Assets/Resources/Models/monsters/&lt;monsterId&gt;.fbx). Faceted flat-shaded Tunic
    /// style — the smooth MS2 pipeline is HEROES-ONLY (hard art rule 2026-07-02).
    /// One rigid group: no rig; motion is the view transform (slide/turn/lunge),
    /// exactly like the primitives it replaces. Returns null when a monster has no
    /// model yet — SpawnView falls back to the painted capsule/cube.
    /// </summary>
    public static class MonsterModel
    {
        public static (GameObject root, float height)? Build(string monsterId)
        {
            var prefab = Resources.Load<GameObject>("Models/monsters/" + monsterId);
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab);
            go.transform.position = Vector3.zero;
            float height = 0.8f; // health-bar anchor floor for very flat designs
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.materials) Bootstrap.MakeMatte(m);
                height = Mathf.Max(height, r.bounds.max.y);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return (go, height);
        }

        /// <summary>Rank/modifier tell on an authored-palette model: lean every material
        /// toward the highlight and add emission — the model equivalent of the primitives'
        /// Paint+Glow (which only touch a root renderer and no-op on model groups).</summary>
        public static void Tint(GameObject go, Color tint, float lean, Color emission)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    Color baseC = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                : m.HasProperty("_Color") ? m.color : Color.gray;
                    if (m.HasProperty("_BaseColor"))
                        m.SetColor("_BaseColor", Color.Lerp(baseC, tint, lean));
                    else if (m.HasProperty("_Color"))
                        m.color = Color.Lerp(baseC, tint, lean);
                    // scale the glow by the material's own brightness: a uniform emission
                    // swamps dark palettes (a shadow-black wraith reads mustard under a
                    // gold income-mod aura) while light materials can carry more of it
                    float luma = 0.3f * baseC.r + 0.6f * baseC.g + 0.1f * baseC.b;
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission * (0.15f + 0.85f * luma));
                }
        }
    }
}
