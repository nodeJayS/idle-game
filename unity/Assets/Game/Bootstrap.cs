using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// M0 scene bootstrap. Builds the isometric scene in code on Play — no manual
    /// editor wiring needed. The renderer only READS game-core state (Save +
    /// CombatState); all logic stays in IdleGame.GameCore. Primitives are
    /// placeholders to be swapped for real art later (pixels or low-poly).
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Boot()
        {
            // --- game-core: a fresh save + the tier's combat lineup ---
            var cfg = GameConfig.Default();
            var save = Save.NewGame(12345u, cfg, 0);

            var party = new List<HeroInstance>();
            foreach (var id in save.Party)
                if (id != null)
                {
                    var hero = save.Heroes.Find(h => h.Id == id);
                    if (hero != null) party.Add(hero);
                }

            var combat = Combat.InitCombat(party, save.Progress.CurrentRiftTier, cfg, new Rng(save.RngSeed));

            // --- camera (iso-ish angle) ---
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(9f, 11f, -9f);
            cam.transform.rotation = Quaternion.Euler(38f, -45f, 0f);
            cam.backgroundColor = new Color(0.08f, 0.06f, 0.10f);

            // --- directional light ---
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Sun");
                light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.15f;

            // --- ground plane ---
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            Paint(ground, new Color(0.18f, 0.35f, 0.22f));

            // --- spawn combat entities as primitives ---
            // combat Pos is a 2D plane (X, Y) -> world (X, Z); Y is up.
            foreach (var e in combat.Entities)
            {
                bool isHero = e.Team == Team.Party;
                var type = (!isHero && e.IsBoss) ? PrimitiveType.Cube : PrimitiveType.Capsule;
                var go = GameObject.CreatePrimitive(type);
                go.name = e.Id;

                float scale = e.IsBoss ? 1.6f : 1f;
                float height = (type == PrimitiveType.Capsule ? 1f : 0.5f) * scale;
                go.transform.position = new Vector3((float)e.Pos.X, height, (float)e.Pos.Y);
                go.transform.localScale = new Vector3(0.7f * scale, 0.9f * scale, 0.7f * scale);

                Color color = isHero
                    ? new Color(0.36f, 0.55f, 0.85f)               // party = blue
                    : (e.IsBoss ? new Color(0.85f, 0.40f, 0.25f)   // boss = orange
                                : new Color(0.45f, 0.80f, 0.50f));  // mobs = green
                Paint(go, color);
            }

            Debug.Log($"[Bootstrap] M0 scene built: {combat.Entities.Count} entities, tier {combat.Tier}.");
        }

        private static void Paint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            renderer.sharedMaterial = mat;
        }
    }
}
