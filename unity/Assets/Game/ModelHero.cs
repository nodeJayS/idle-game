#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Blender-authored hero models (scripted in art/*.py, exported to
    /// Assets/Resources/Models/&lt;defId&gt;.fbx). The FBX carries FLAT root-level rigid
    /// parts named "&lt;joint&gt;.&lt;part&gt;" with verts in character space; we build the same
    /// joint skeleton ChibiHero does and reparent each part by its name prefix
    /// (worldPositionStays, so FBX axis-conversion quirks can't misplace anything).
    /// ChibiAnimator then drives the joints unchanged. Returns null when no model
    /// asset exists for the def — SpawnView falls back to the code-built chibi.
    /// </summary>
    public static class ModelHero
    {
        // Joint layout — must match the BONES table in art/*.py (shared chibi skeleton).
        private const float Hip = 0.42f, TorsoH = 0.5f, HeadR = 0.27f;
        private const float ShoulderX = 0.28f, HipX = 0.13f, ArmLen = 0.42f;
        private const float ShoulderY = TorsoH * 0.80f;

        public static (GameObject root, ChibiAnimator anim)? Build(string defId)
        {
            var prefab = Resources.Load<GameObject>("Models/" + defId);
            if (prefab == null) return null;

            var fbx = Object.Instantiate(prefab);
            fbx.transform.position = Vector3.zero;

            var root = new GameObject(defId);
            var body = Joint("body", root.transform, new Vector3(0f, Hip, 0f));
            var joints = new Dictionary<string, Transform>
            {
                ["body"] = body,
                ["head"] = Joint("head", body, new Vector3(0f, TorsoH + HeadR * 0.85f, 0f)),
                // FBX handedness flips X, so the model's L parts import on +X; the
                // joints sit where the parts landed (mirror of ChibiHero's signs).
                ["armL"] = Joint("armL", body, new Vector3(ShoulderX, ShoulderY, 0f)),
                ["armR"] = Joint("armR", body, new Vector3(-ShoulderX, ShoulderY, 0f)),
                ["legL"] = Joint("legL", body, new Vector3(HipX, 0f, 0f)),
                ["legR"] = Joint("legR", body, new Vector3(-HipX, 0f, 0f)),
            };
            joints["hand"] = Joint("hand", joints["armR"], new Vector3(0f, -ArmLen, 0f));

            var parts = new List<Transform>();
            foreach (Transform c in fbx.transform) parts.Add(c);
            foreach (var p in parts)
            {
                int dot = p.name.IndexOf('.');
                var joint = dot > 0 && joints.TryGetValue(p.name.Substring(0, dot), out var j)
                    ? j : body;
                p.SetParent(joint, worldPositionStays: true);
                var col = p.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }
            Object.Destroy(fbx);

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                foreach (var m in r.materials) Bootstrap.MakeMatte(m);

            var anim = root.AddComponent<ChibiAnimator>();
            anim.Body = body;
            anim.Head = joints["head"];
            anim.ArmL = joints["armL"];
            anim.ArmR = joints["armR"];
            anim.LegL = joints["legL"];
            anim.LegR = joints["legR"];
            anim.Setup();
            return (root, anim);
        }

        private static Transform Joint(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
