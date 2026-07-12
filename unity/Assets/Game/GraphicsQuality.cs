#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IdleGame.Game
{
    /// <summary>
    /// The 10.12(d) quality tier: applies the persisted weak-hardware levers (render scale,
    /// shadows, post) to the live URP pipeline + main camera. The benchmark shows the game is
    /// GPU-fill-bound on weak machines (~2ms/frame on a desktop at 720p, near-zero GC), so
    /// RENDER SCALE is the lever that matters; shadows/post are the secondary cuts. Called at
    /// boot (after the camera exists) and on every Settings change — idempotent, cheap.
    /// </summary>
    public static class GraphicsQuality
    {
        public static void Apply()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Settings.RenderScale;
                // Distance 0 disables the whole shadow pass (casters skip submission);
                // on = the 10.12c-trimmed distance, not the asset default.
                urp.shadowDistance = Settings.Shadows ? Bootstrap.ShadowDistance : 0f;
            }
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent(out UniversalAdditionalCameraData data))
                data.renderPostProcessing = Settings.PostFx;
        }
    }
}
