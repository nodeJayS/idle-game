using UnityEditor;

namespace IdleGame.GameEditor
{
    /// <summary>
    /// Imports the Mixamo hero FBXs (under Resources/Characters/Mixamo) as <b>Humanoid</b>
    /// so their clips share an avatar and retarget onto the same rig — which is what lets
    /// the Walk/Attack clips play on the Idle model. Runs automatically on (re)import; no
    /// manual inspector tweaking needed (the scene is built in code).
    /// </summary>
    public sealed class MixamoImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("Resources/Characters/Mixamo/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
        }
    }
}
