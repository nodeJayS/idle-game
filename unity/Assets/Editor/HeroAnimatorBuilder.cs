#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace IdleGame.EditorTools
{
    /// <summary>
    /// Per-hero animation wiring for the MS2 skinned pipeline. For every
    /// Resources/Models/&lt;defId&gt;_skinned.fbx this (a) defines the importer clips
    /// from the FBX takes (idle/run loop, everything else one-shot) and (b) builds
    /// &lt;defId&gt;Animator.overrideController remapping the shared HeroAnimator's
    /// states onto THAT hero's clips — the base controller references one hero's
    /// clips directly, so without the override every hero would play those.
    /// Run after each art/skinned_body.py bake (Tools menu or via MCP).
    /// </summary>
    public static class HeroAnimatorBuilder
    {
        private const string ModelsDir = "Assets/Resources/Models";
        private static readonly HashSet<string> Looping = new() { "idle", "run" };

        [MenuItem("Tools/Build Hero Animators")]
        public static void BuildAll()
        {
            foreach (var path in Directory.GetFiles(ModelsDir, "*_skinned.fbx"))
                Build(path.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
        }

        /// <summary>Take/clip name -> role: Blender exports takes as "Rig|idle".</summary>
        private static string Role(string clipName) =>
            clipName.Split('|').Last().ToLowerInvariant();

        private static void Build(string fbxPath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            var clips = importer.defaultClipAnimations; // one per take, full range
            foreach (var c in clips) c.loopTime = Looping.Contains(Role(c.takeName));
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var baseCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ModelsDir + "/HeroAnimator.controller");
            if (baseCtrl == null)
            {
                Debug.LogError("[HeroAnimatorBuilder] HeroAnimator.controller missing");
                return;
            }

            var byRole = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => Role(c.name))
                .ToDictionary(g => g.Key, g => g.First());

            string defId = Path.GetFileNameWithoutExtension(fbxPath);
            defId = defId.Substring(0, defId.Length - "_skinned".Length);
            string outPath = $"{ModelsDir}/{defId}Animator.overrideController";

            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(outPath);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(baseCtrl);
                AssetDatabase.CreateAsset(oc, outPath);
            }
            oc.runtimeAnimatorController = baseCtrl;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var baseClip in baseCtrl.animationClips.Distinct())
                if (byRole.TryGetValue(Role(baseClip.name), out var mine))
                    overrides.Add(new(baseClip, mine));
            oc.ApplyOverrides(overrides);
            EditorUtility.SetDirty(oc);
            Debug.Log($"[HeroAnimatorBuilder] {defId}: {overrides.Count} state clips bound.");
        }
    }
}
