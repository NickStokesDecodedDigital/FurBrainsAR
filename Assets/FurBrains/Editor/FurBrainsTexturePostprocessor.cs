using UnityEditor;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Applies phone-friendly import settings to every texture under Assets/FurBrains/.
    /// Runs automatically whenever Unity imports or reimports a texture in that folder,
    /// so artists can drop in full-resolution source images without blowing up app size or memory.
    /// </summary>
    /// <remarks>
    /// Lives in an "Editor" folder, so it's compiled into an editor-only assembly and never ships in the app.
    /// </remarks>
    public class FurBrainsTexturePostprocessor : AssetPostprocessor
    {
        const string k_Root = "Assets/FurBrains/";

        // Cap for normal / colour maps. 2048 keeps hero-character detail; drop to 1024 if memory is tight.
        const int k_MaxSize = 2048;

        // Bump this when changing the rules below: Unity then reimports affected textures automatically.
        public override uint GetVersion() => 3;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(k_Root))
                return;

            var importer = (TextureImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

            if (name.Contains("normal"))
            {
                // Tells Unity to treat the image as a normal map (correct encoding, no sRGB).
                importer.textureType = TextureImporterType.NormalMap;
            }
            else if (name.Contains("roughness") || name.Contains("metallic") || name.Contains("mask") || name.Contains("alpha"))
            {
                // Data textures store numbers, not colours, so they must not be gamma-corrected.
                importer.sRGBTexture = false;
            }

            importer.maxTextureSize = k_MaxSize;
            importer.mipmapEnabled = true;

            // UI pictures (Assets/FurBrains/UI/...): ready-to-use sprites with see-through backgrounds. Mipmaps
            // (smaller copies for distant 3D objects) aren't needed for UI drawn at a fixed size on screen.
            if (assetPath.StartsWith(k_Root + "UI/"))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
            }

            // Android: ASTC is the GPU-native compressed format on modern phones (~4-8x smaller in memory than RGBA32).
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = k_MaxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = (int)TextureCompressionQuality.Normal;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
