using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Builds a single character prefab from several part FBX files (Body, Mouth, Armor, ...) that were exported
    /// from Unreal with the same skeleton. This is Unity's equivalent of Unreal's "Leader Pose" setup.
    /// </summary>
    /// <remarks>
    /// How it works: each part FBX imports with its own copy of the skeleton. A SkinnedMeshRenderer deforms its mesh
    /// using an array of bone Transforms (think: <c>std::vector&lt;Transform*&gt; bones</c>). We keep the Body's skeleton,
    /// re-point every other part's bone array at the Body's bones (matched by name), move the renderers under the Body
    /// and throw the duplicate skeletons away. Result: one skeleton, one Animator, all parts follow it.
    ///
    /// Usage: select all part FBX files in the Project window, right-click → FurBrains → Build Character Prefab.
    /// The prefab is saved next to the FBX files, named after their folder.
    /// </remarks>
    public static class FurBrainsCharacterBuilder
    {
        const string k_MenuPath = "Assets/FurBrains/Build Character Prefab From Selected Parts";

        [MenuItem(k_MenuPath, true)]
        static bool ValidateBuild() => GetSelectedModels().Count > 0;

        [MenuItem(k_MenuPath)]
        static void Build()
        {
            var parts = GetSelectedModels();
            // The part named "Body" provides the skeleton; fall back to the first selected part.
            var basePart = parts.FirstOrDefault(p => p.name.ToLowerInvariant().Contains("body")) ?? parts[0];

            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(basePart))?.Replace('\\', '/');
            var characterName = Path.GetFileName(folder);
            var prefabPath = $"{folder}/{characterName}.prefab";

            // Work in an invisible preview scene so the user's open scene isn't touched.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = InstantiateUnpacked(basePart, scene);
                root.name = characterName;

                var bonesByName = new Dictionary<string, Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    bonesByName.TryAdd(t.name, t);

                var missingBones = new HashSet<string>();
                foreach (var part in parts)
                {
                    if (part == basePart)
                        continue;

                    var instance = InstantiateUnpacked(part, scene);
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var bones = renderer.bones;
                        for (var i = 0; i < bones.Length; i++)
                        {
                            if (bones[i] == null)
                                continue;
                            if (bonesByName.TryGetValue(bones[i].name, out var shared))
                                bones[i] = shared;
                            else
                                missingBones.Add(bones[i].name);
                        }
                        renderer.bones = bones;

                        if (renderer.rootBone != null && bonesByName.TryGetValue(renderer.rootBone.name, out var sharedRoot))
                            renderer.rootBone = sharedRoot;

                        // Keep its local offset (identical between parts, since every FBX has the same root setup).
                        renderer.transform.SetParent(root.transform, false);
                        renderer.gameObject.name = part.name;
                    }

                    // Everything left in the instance is the duplicate skeleton.
                    Object.DestroyImmediate(instance);
                }

                // Unity draws any material slots beyond the mesh's sub-mesh count over the last sub-mesh again.
                // That's wasted GPU work at best, and z-fighting at worst (e.g. Ziara's eyes), so drop the extras.
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var subMeshCount = renderer.sharedMesh != null ? renderer.sharedMesh.subMeshCount : 0;
                    var materials = renderer.sharedMaterials;
                    if (subMeshCount > 0 && materials.Length > subMeshCount)
                        renderer.sharedMaterials = materials.Take(subMeshCount).ToArray();
                }

                if (missingBones.Count > 0)
                    Debug.LogWarning($"[FurBrains] {characterName}: these bones weren't found in {basePart.name}'s skeleton, " +
                        $"so those vertices won't follow animation: {string.Join(", ", missingBones)}");

                if (root.GetComponent<FurBrains.FurBrainFace>() == null)
                    root.AddComponent<FurBrains.FurBrainFace>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (success)
                {
                    Debug.Log($"[FurBrains] Built {prefabPath} from {parts.Count} parts (skeleton from {basePart.name}).", prefab);
                    EditorGUIUtility.PingObject(prefab);
                    Selection.activeObject = prefab;
                }
                else
                {
                    Debug.LogError($"[FurBrains] Failed to save {prefabPath}.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject InstantiateUnpacked(GameObject model, UnityEngine.SceneManagement.Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        static List<GameObject> GetSelectedModels() =>
            Selection.objects.OfType<GameObject>()
                .Where(g => PrefabUtility.GetPrefabAssetType(g) == PrefabAssetType.Model)
                .ToList();
    }
}
