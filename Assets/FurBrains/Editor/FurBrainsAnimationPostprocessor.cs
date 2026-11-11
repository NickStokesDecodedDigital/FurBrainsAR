using UnityEditor;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Applies the FurBrains animation import settings to every FBX under Assets/FurBrains/Animation/Clips/,
    /// automatically on import, so nobody has to remember them for each new file.
    /// </summary>
    /// <remarks>
    /// Like the texture and model postprocessors, this is an <c>AssetPostprocessor</c>: Unity calls its
    /// <c>OnPreprocess…</c> methods before importing each asset, and we adjust the importer settings for this import.
    /// Settings forced for every clip FBX:
    /// <list type="bullet">
    /// <item>No cameras or lights (Unreal exports include them; they'd be useless objects inside the asset).</item>
    /// <item>No materials (animation-only files have no meshes to put them on).</item>
    /// <item>Generic rig (Humanoid would drop the FurBrains ear and cape bones).</item>
    /// </list>
    /// Clips that should loop (everything in Idle/ and Talking/, plus walk and run) get Loop Time switched on, but only
    /// while the file still uses Unity's default clip settings: once you edit a clip in the Animation tab (or a tool such
    /// as the pet builder does), your settings are kept.
    /// </remarks>
    public class FurBrainsAnimationPostprocessor : AssetPostprocessor
    {
        const string k_ClipsFolder = "Assets/FurBrains/Animation/Clips/";

        // Bump when changing the rules so Unity reimports the clips.
        public override uint GetVersion() => 1;

        static bool IsClip(string path) => path.StartsWith(k_ClipsFolder);

        static bool ShouldLoop(string path)
        {
            var relative = path.Substring(k_ClipsFolder.Length);
            return relative.StartsWith("Idle/") || relative.StartsWith("Talking/") ||
                   relative.Contains("_Walk_") || relative.Contains("_Run_");
        }

        void OnPreprocessModel()
        {
            if (!IsClip(assetPath))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
        }

        void OnPreprocessAnimation()
        {
            if (!IsClip(assetPath) || !ShouldLoop(assetPath))
                return;
            var importer = (ModelImporter)assetImporter;
            if (importer.clipAnimations.Length > 0)
                return; // the clip settings were customised: leave them alone

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
                clip.loopTime = true;
            importer.clipAnimations = clips;
        }
    }
}
