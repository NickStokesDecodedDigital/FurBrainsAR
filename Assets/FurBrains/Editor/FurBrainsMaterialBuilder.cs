using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Creates URP materials for the Unreal-exported Ziara parts and assigns them to each FBX's material slots.
    /// </summary>
    /// <remarks>
    /// Unreal materials can't be exported, only their textures. This tool rebuilds simple URP Lit equivalents:
    /// <list type="bullet">
    /// <item>Base colour + normal map (+ emission where present).</item>
    /// <item>Unreal used separate "_Alpha" mask textures for cut-outs (eyelashes, armor trim). URP Lit reads cut-out
    /// alpha from the base colour's alpha channel, so we merge Color + Alpha into a new "_Cutout.png" first.</item>
    /// <item>Each material is hooked up through the FBX importer's "Remapped Materials" list, the same thing you'd do by
    /// hand in the Inspector's Materials tab, so the change survives reimports.</item>
    /// </list>
    /// Re-running the tool is safe: it overwrites the materials it created.
    /// </remarks>
    public static class FurBrainsMaterialBuilder
    {
        const string k_Folder = "Assets/FurBrains/Models/Ziara";

        /// <summary>One FBX material slot and the textures/settings that should fill it.</summary>
        struct Slot
        {
            public string Fbx, SlotName, Material, BaseColor, Alpha, Normal, Emission;
            public float Smoothness;
            public bool DoubleSided;
            public Color EmissionColor;
        }

        // The mapping is written out by hand from the texture and slot names the Unreal team used.
        static readonly Slot[] k_Slots =
        {
            new() { Fbx = "Body", SlotName = "Fbx Default Material 0", Material = "M_Ziara_Body",
                    BaseColor = "Rabbit_Body_BaseColor", Normal = "Rabbit_Body_Normal", Smoothness = 0.15f },
            // Slot 1 has no polygons, but give it the body material so nothing ever shows up pink.
            new() { Fbx = "Body", SlotName = "Fbx Default Material 1", Material = "M_Ziara_Body" },

            new() { Fbx = "Mouth", SlotName = "MI_Mouth", Material = "M_Ziara_Mouth",
                    BaseColor = "Mouth_Basecolor", Smoothness = 0.45f },

            // Despite its name, Eyebrows.FBX contains only the eyes: every polygon uses the "M_Eyes" slot and the
            // "M_Eyelash_Eyebrow" slot is empty. Unity draws an extra material slot over the last sub-mesh a second
            // time, so a different material there z-fights with the eyes (one eye showed the iris, the other didn't).
            // Giving both slots the eye material avoids that. (The Bakegroup lash textures aren't used by any part.)
            new() { Fbx = "Eyebrows", SlotName = "M_Eyes", Material = "M_Ziara_Eyes",
                    BaseColor = "Eyes", Smoothness = 0.85f },
            new() { Fbx = "Eyebrows", SlotName = "M_Eyelash_Eyebrow", Material = "M_Ziara_Eyes" },

            new() { Fbx = "Armor", SlotName = "M_Armor_", Material = "M_Ziara_Armor",
                    BaseColor = "Armor_1K_Color", Alpha = "Armor_1K_Alpha", Normal = "Armor_1K_NormalGL",
                    Emission = "Armor_1K_Emmision", EmissionColor = new Color(1f, 0.85f, 0.5f) * 2f, Smoothness = 0.5f },
            new() { Fbx = "Armor", SlotName = "M_Light", Material = "M_Ziara_ArmorLight",
                    EmissionColor = new Color(1f, 0.85f, 0.5f) * 3f, Smoothness = 0.5f },

            new() { Fbx = "Helmet_Armor", SlotName = "M_Helm", Material = "M_Ziara_Helmet",
                    BaseColor = "Helmet_1K_Color", Alpha = "Helmet_1K_Alpha", Normal = "Helmet_1K_NormalGL",
                    Smoothness = 0.5f },

            new() { Fbx = "Cape", SlotName = "M_Cape", Material = "M_Ziara_Cape",
                    BaseColor = "CapeColor", Normal = "CapeNormal", Smoothness = 0.2f, DoubleSided = true },
        };

        [MenuItem("Tools/FurBrains/Create Ziara Materials")]
        static void Build()
        {
            var materialFolder = $"{k_Folder}/Materials";
            if (!AssetDatabase.IsValidFolder(materialFolder))
                AssetDatabase.CreateFolder(k_Folder, "Materials");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[FurBrains] URP Lit shader not found. Is the Universal Render Pipeline set up?");
                return;
            }

            foreach (var slot in k_Slots)
            {
                var material = CreateOrUpdateMaterial(slot, shader, materialFolder);
                Remap(slot, material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FurBrains] Created/updated {k_Slots.Length} material slots in {materialFolder}. " +
                      "Rebuild the character prefab if you built it before materials existed.");
        }

        static Material CreateOrUpdateMaterial(Slot slot, Shader shader, string folder)
        {
            var path = $"{folder}/{slot.Material}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var isNew = material == null;
            if (isNew)
                material = new Material(shader);
            else if (material.shader != shader)
                material.shader = shader;

            // Several slots can share one material; only configure it from the slot that names textures.
            if (!isNew && slot.BaseColor == null && slot.EmissionColor == default)
                return material;

            var baseColorName = slot.Alpha != null ? MergeCutout(slot.BaseColor, slot.Alpha) : slot.BaseColor;
            var baseMap = LoadTexture(baseColorName);
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", slot.Smoothness);
            material.SetFloat("_Metallic", 0f);

            var normal = LoadTexture(slot.Normal);
            material.SetTexture("_BumpMap", normal);
            SetKeyword(material, "_NORMALMAP", normal != null);

            // Alpha clipping: pixels with alpha below the cutoff aren't drawn (cheap, no sorting issues).
            var cutout = slot.Alpha != null;
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            SetKeyword(material, "_ALPHATEST_ON", cutout);
            material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            material.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            // Draw both faces for thin geometry such as eyelash cards and the cape.
            material.SetFloat("_Cull", slot.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back);

            var emissive = slot.EmissionColor != default;
            material.SetTexture("_EmissionMap", LoadTexture(slot.Emission));
            material.SetColor("_EmissionColor", emissive ? slot.EmissionColor : Color.black);
            SetKeyword(material, "_EMISSION", emissive);
            material.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                                                        : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (isNew)
                AssetDatabase.CreateAsset(material, path);
            else
                EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Writes Color.rgb + Alpha.r into "{color}_Cutout.png" and returns the new texture's name.</summary>
        static string MergeCutout(string colorName, string alphaName)
        {
            var outName = $"{colorName}_Cutout";
            var outPath = $"{k_Folder}/{outName}.png";

            // Read the source PNG files directly: always readable and uncompressed, regardless of import settings.
            var color = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var alpha = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            color.LoadImage(File.ReadAllBytes(FindFile(colorName)));
            alpha.LoadImage(File.ReadAllBytes(FindFile(alphaName)));

            if (color.width != alpha.width || color.height != alpha.height)
            {
                Debug.LogWarning($"[FurBrains] {colorName} and {alphaName} are different sizes; resizing alpha to match.");
                alpha = Resize(alpha, color.width, color.height);
            }

            var c = color.GetPixels32();
            var a = alpha.GetPixels32();
            for (var i = 0; i < c.Length; i++)
                c[i].a = a[i].r;
            color.SetPixels32(c);

            File.WriteAllBytes(outPath, color.EncodeToPNG());
            Object.DestroyImmediate(color);
            Object.DestroyImmediate(alpha);
            AssetDatabase.ImportAsset(outPath);
            return outName;
        }

        static void Remap(Slot slot, Material material)
        {
            var fbxPath = $"{k_Folder}/{slot.Fbx}.FBX";
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter importer)
            {
                Debug.LogWarning($"[FurBrains] Couldn't find {fbxPath}; skipping slot {slot.SlotName}.");
                return;
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot.SlotName), material);
            importer.SaveAndReimport();
        }

        static Texture2D LoadTexture(string name) =>
            name == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(FindAssetPath(name));

        static string FindAssetPath(string name)
        {
            // Match the real on-disk spelling (e.g. ".PNG" vs ".png"): the AssetDatabase is case-sensitive about paths.
            foreach (var file in Directory.GetFiles(k_Folder))
            {
                if (file.EndsWith(".meta"))
                    continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(file), name, System.StringComparison.OrdinalIgnoreCase))
                    return file.Replace('\\', '/');
            }
            Debug.LogWarning($"[FurBrains] Texture '{name}' not found in {k_Folder}.");
            return null;
        }

        static string FindFile(string name) => FindAssetPath(name);

        static Texture2D Resize(Texture2D source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(source);
            return result;
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}
