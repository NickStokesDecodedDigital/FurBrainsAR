using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Bakes a fur mask texture so fur never pokes through clothing, eyes or mouth.
    /// </summary>
    /// <remarks>
    /// For every Body vertex we measure the distance to the closest point on any "blocker" part (armour, helmet, cape,
    /// eyes, mouth), in the character's rest pose. The mask stores how much of the fur length fits in that gap:
    /// <c>mask = clamp01((distance - margin) / furLength)</c>. The shader multiplies the fur length by the mask, so fur
    /// ends just below the clothing, fades out toward clothing edges and grows full-length on open skin.
    ///
    /// The per-vertex values are then painted into a texture in UV space (a small software rasteriser: for each
    /// triangle, fill the pixels it covers in the UV layout, interpolating the corner values), and the result is
    /// assigned to the fur material's Fur Mask slot.
    ///
    /// The mask depends on the fur length, so re-run this after changing Fur Length on the material. The PNG can also be
    /// touched up by hand in an image editor (black = no fur, white = full length), but re-baking overwrites it.
    /// </remarks>
    public static class FurBrainsFurMaskBaker
    {
        const string k_CharacterPrefab = "Assets/FurBrains/Models/Ziara/Ziara.prefab";
        const string k_FurMaterial = "Assets/FurBrains/Models/Ziara/Materials/M_Ziara_Fur.mat";
        const string k_MaskPath = "Assets/FurBrains/Models/Ziara/Ziara_FurMask.png";

        // Parts whose surface fur must stay under. Names are the renderer names in the character prefab.
        static readonly HashSet<string> k_Blockers = new() { "Armor", "Helmet_Armor", "Cape", "Eyebrows", "Mouth" };

        // Gap (model units = metres at full size) kept between fur tips and clothing.
        const float k_Margin = 0.003f;
        const int k_Size = 1024;

        [MenuItem("Tools/FurBrains/Bake Ziara Fur Mask")]
        static void Bake()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterPrefab);
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_FurMaterial);
            if (prefab == null || material == null)
            {
                Debug.LogError($"[FurBrains] Need {k_CharacterPrefab} and {k_FurMaterial} (run Create Ziara Materials first).");
                return;
            }
            var furLength = Mathf.Max(material.GetFloat("_FurLength"), 1e-4f);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

                // Bake every part in its rest pose and bring it into one shared (world) space.
                Mesh body = null;
                Vector3[] bodyPositions = null;
                var blockers = new TriangleGrid(furLength + k_Margin);
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var isBody = renderer.name == "Body";
                    if (!isBody && !k_Blockers.Contains(renderer.name))
                        continue;

                    var baked = new Mesh();
                    renderer.BakeMesh(baked);
                    var positions = baked.vertices;
                    var toWorld = renderer.transform.localToWorldMatrix;
                    for (var i = 0; i < positions.Length; i++)
                        positions[i] = toWorld.MultiplyPoint3x4(positions[i]);

                    if (isBody)
                    {
                        body = baked;
                        bodyPositions = positions;
                        continue;
                    }

                    var tris = baked.triangles;
                    for (var t = 0; t < tris.Length; t += 3)
                        blockers.Add(positions[tris[t]], positions[tris[t + 1]], positions[tris[t + 2]]);
                    Object.DestroyImmediate(baked);
                }

                if (body == null)
                {
                    Debug.LogError("[FurBrains] No renderer named \"Body\" in the character prefab.");
                    return;
                }

                // 1. Per-vertex mask from the gap to the nearest blocker.
                var vertexMask = new float[bodyPositions.Length];
                int bald = 0, shortened = 0;
                for (var i = 0; i < bodyPositions.Length; i++)
                {
                    var distance = blockers.Distance(bodyPositions[i]);
                    vertexMask[i] = Mathf.Clamp01((distance - k_Margin) / furLength);
                    if (vertexMask[i] <= 0f) bald++;
                    else if (vertexMask[i] < 1f) shortened++;
                }

                // 2. Paint it into UV space and save.
                var pixels = Rasterize(body.uv, body.triangles, vertexMask);
                Object.DestroyImmediate(body);
                SavePng(pixels);

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_MaskPath);
                material.SetTexture("_FurMask", texture);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                Debug.Log($"[FurBrains] Baked {k_MaskPath} for fur length {furLength}: {bald} of {vertexMask.Length} body " +
                          $"vertices have no fur, {shortened} have shortened fur. Re-bake after changing Fur Length.", texture);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static float[] Rasterize(Vector2[] uv, int[] triangles, float[] vertexValue)
        {
            var pixels = new float[k_Size * k_Size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = -1f; // "not covered by any triangle"

            for (var t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                Vector2 a = uv[i0] * k_Size, b = uv[i1] * k_Size, c = uv[i2] * k_Size;
                var area = Cross(b - a, c - a);
                if (Mathf.Abs(area) < 1e-8f)
                    continue;

                var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                var maxX = Mathf.Min(k_Size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                var maxY = Mathf.Min(k_Size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    // Barycentric coordinates of the pixel centre: how much each corner contributes.
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var w0 = Cross(b - p, c - p) / area;
                    var w1 = Cross(c - p, a - p) / area;
                    var w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f)
                        continue;
                    var value = w0 * vertexValue[i0] + w1 * vertexValue[i1] + w2 * vertexValue[i2];
                    var index = y * k_Size + x;
                    // Where UV islands overlap, the smaller (safer) value wins.
                    pixels[index] = pixels[index] < 0f ? value : Mathf.Min(pixels[index], value);
                }
            }

            // Grow each UV island outward a few pixels, so texture filtering at island edges never blends in
            // the empty background (which would show as seams).
            for (var pass = 0; pass < 8; pass++)
            {
                var next = (float[])pixels.Clone();
                for (var y = 0; y < k_Size; y++)
                for (var x = 0; x < k_Size; x++)
                {
                    var index = y * k_Size + x;
                    if (pixels[index] >= 0f)
                        continue;
                    float sum = 0f; var count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= k_Size || ny >= k_Size)
                            continue;
                        var v = pixels[ny * k_Size + nx];
                        if (v >= 0f) { sum += v; count++; }
                    }
                    if (count > 0)
                        next[index] = sum / count;
                }
                pixels = next;
            }

            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i] < 0f)
                    pixels[i] = 1f;
            return pixels;
        }

        static void SavePng(float[] pixels)
        {
            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
            var colors = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var v = (byte)Mathf.RoundToInt(Mathf.Clamp01(pixels[i]) * 255f);
                colors[i] = new Color32(v, v, v, 255);
            }
            texture.SetPixels32(colors);
            File.WriteAllBytes(k_MaskPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            // Imported as linear data (not colour) by FurBrainsTexturePostprocessor, because the name contains "mask".
            AssetDatabase.ImportAsset(k_MaskPath);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>
        /// Triangles bucketed into a 3D grid of cells, so a distance query only tests the triangles near the point
        /// (like a spatial hash in C++: <c>std::unordered_map&lt;cell, std::vector&lt;int&gt;&gt;</c>).
        /// </summary>
        class TriangleGrid
        {
            readonly float m_Range;
            readonly Dictionary<Vector3Int, List<int>> m_Cells = new();
            readonly List<Vector3> m_Corners = new();

            /// <param name="range">Largest distance that matters; anything further is reported as this.</param>
            public TriangleGrid(float range) => m_Range = range;

            Vector3Int Cell(Vector3 p) => Vector3Int.FloorToInt(p / m_Range);

            public void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                var index = m_Corners.Count / 3;
                m_Corners.Add(a); m_Corners.Add(b); m_Corners.Add(c);
                var min = Cell(Vector3.Min(a, Vector3.Min(b, c)));
                var max = Cell(Vector3.Max(a, Vector3.Max(b, c)));
                for (var x = min.x; x <= max.x; x++)
                for (var y = min.y; y <= max.y; y++)
                for (var z = min.z; z <= max.z; z++)
                {
                    var key = new Vector3Int(x, y, z);
                    if (!m_Cells.TryGetValue(key, out var list))
                        m_Cells[key] = list = new List<int>();
                    list.Add(index);
                }
            }

            /// <summary>Distance from p to the nearest triangle, capped at the grid's range.</summary>
            public float Distance(Vector3 p)
            {
                var best = m_Range * m_Range;
                var min = Cell(p - Vector3.one * m_Range);
                var max = Cell(p + Vector3.one * m_Range);
                for (var x = min.x; x <= max.x; x++)
                for (var y = min.y; y <= max.y; y++)
                for (var z = min.z; z <= max.z; z++)
                {
                    if (!m_Cells.TryGetValue(new Vector3Int(x, y, z), out var list))
                        continue;
                    foreach (var t in list)
                    {
                        var q = ClosestPointOnTriangle(p, m_Corners[t * 3], m_Corners[t * 3 + 1], m_Corners[t * 3 + 2]);
                        best = Mathf.Min(best, (p - q).sqrMagnitude);
                    }
                }
                return Mathf.Sqrt(best);
            }

            // From "Real-Time Collision Detection" (Ericson), section 5.1.5.
            static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = p - a;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return a;

                var bp = p - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return b;

                var vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

                var cp = p - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return c;

                var vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

                var va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                    return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

                var denom = 1f / (va + vb + vc);
                float v = vb * denom, w = vc * denom;
                return a + ab * v + ac * w;
            }
        }
    }
}
