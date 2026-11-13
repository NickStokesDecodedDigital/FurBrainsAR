using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Gives Ziara's body a furry look with textures only: draws thousands of combed, clumped fur strands that flow
    /// down her body, and bakes them into copies of her body colour and normal textures.
    /// </summary>
    /// <remarks>
    /// Steps (each is one function below):
    /// <list type="number">
    /// <item><b>Flow map</b> (<see cref="BuildFlowMap"/>): the body texture is cut into separate islands that face
    /// every which way, so "down" in the texture isn't "down" on the body. For each triangle of her rest-pose mesh we
    /// work out which texture direction points down her body (gravity, flattened onto the surface), and paint it
    /// into a small direction map. Strands then follow that map, so fur flows consistently across the islands.</item>
    /// <item><b>Strands</b> (<see cref="DrawStrands"/>): short curved hairs drawn along the flow into a "height"
    /// image (1 on a hair, 0 in the gaps). A low-frequency noise twists and shades nearby hairs together, which
    /// makes soft clumps (tufts). Each hair is darker at the root and lighter at the tip, like real fur.</item>
    /// <item><b>Colour</b> (<see cref="BakeColour"/>): the original colour × a factor from the height image that
    /// averages exactly 1 over the body, so her overall colour doesn't change.</item>
    /// <item><b>Normal</b> (<see cref="BakeNormal"/>): the slope of the height image tilts the original normal map,
    /// so each hair catches the light.</item>
    /// </list>
    /// Outputs (2048, the cap FurBrainsTexturePostprocessor applies anyway) are set on M_Ziara_Body:
    /// Rabbit_Body_BaseColor_Fur.png as Base Map and Rabbit_Body_Normal_Fur.png as Normal Map. The material is also
    /// switched to the FurBrains/FuzzyLit shader (URP Lit plus a soft fuzz rim at the outline).
    /// "Create Ziara Materials" restores the original textures and URP Lit, so re-run this tool after it.
    /// URP Lit's Detail Inputs were tried first and darkened the body, so they're not used.
    /// </remarks>
    public static class FurBrainsFurTextureMaker
    {
        const string k_Folder = "Assets/FurBrains/Models/Ziara";
        const string k_CharacterPrefab = k_Folder + "/Ziara.prefab";
        const string k_ColourSource = k_Folder + "/Rabbit_Body_BaseColor.PNG";
        const string k_NormalSource = k_Folder + "/Rabbit_Body_Normal.PNG";
        const string k_ColourOutput = k_Folder + "/Rabbit_Body_BaseColor_Fur.png";
        // "Normal" in the name makes FurBrainsTexturePostprocessor import it as a normal map.
        const string k_NormalOutput = k_Folder + "/Rabbit_Body_Normal_Fur.png";
        const string k_BodyMaterialPath = k_Folder + "/Materials/M_Ziara_Body.mat";
        const string k_FuzzyShader = "FurBrains/FuzzyLit";

        const int k_Size = 2048;      // output size in pixels
        const int k_FlowSize = 256;   // direction map size (directions change slowly, so this can be small)

        // Strands (sizes in output pixels). Tune these and re-run the tool.
        const float k_Density = 0.03f;      // strands per body pixel
        const float k_MinLength = 10f;
        const float k_MaxLength = 22f;
        const float k_Width = 1.1f;         // half-width at the root; hairs taper toward the tip
        const float k_StrandJitter = 12f;   // random twist per hair, in degrees
        const float k_ClumpSize = 28f;      // size of a tuft, in pixels
        const float k_ClumpTwist = 30f;     // how far a whole tuft can turn away from the flow, in degrees

        // Look.
        const float k_ColourContrast = 0.4f;   // brightness difference between hair tips and the gaps (0 = none)
        const float k_NormalStrength = 0.35f;  // how much hairs bend the lighting (0.9 looked too bumpy)
        const int k_NormalSoftness = 1;        // blur radius (pixels) of the hair bumps: higher = softer bumps

        [MenuItem("Tools/FurBrains/Add Fur Texture To Ziara")]
        static void Make()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterPrefab);
            if (material == null || prefab == null)
            {
                Debug.LogError($"[FurBrains] Need {k_CharacterPrefab} and {k_BodyMaterialPath} (run Create Ziara Materials first).");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("FurBrains fur", "Reading Ziara's mesh", 0f);
                if (!BuildFlowMap(prefab, out var flow, out var covered))
                    return;

                var height = DrawStrands(flow, covered);

                EditorUtility.DisplayProgressBar("FurBrains fur", "Baking colour", 0.8f);
                var colourSource = LoadPng(k_ColourSource);
                SavePng(k_ColourOutput, BakeColour(colourSource, height, covered));
                Object.DestroyImmediate(colourSource);

                EditorUtility.DisplayProgressBar("FurBrains fur", "Baking normals", 0.9f);
                var normalSource = LoadPng(k_NormalSource);
                SavePng(k_NormalOutput, BakeNormal(normalSource, height));
                Object.DestroyImmediate(normalSource);
            }
            finally
            {
                // Like a C++ RAII guard: the progress bar is cleared even if something above throws.
                EditorUtility.ClearProgressBar();
            }

            Undo.RecordObject(material, "Add Fur Texture");
            // A Material Variant always uses its parent's shader, so a variant can't switch to FuzzyLit. Detaching it
            // turns it back into a normal material and keeps all its current values.
            if (material.parent != null)
            {
                Debug.Log($"[FurBrains] {material.name} was a variant of {material.parent.name}; detached it so it can " +
                          "use its own shader.", material);
                material.parent = null;
            }
            // URP Lit + a soft fuzz rim (FuzzyLit.shader). It uses the same property names as URP Lit, so switching
            // keeps every texture and setting; the Fuzz values start at the shader's defaults.
            var fuzzyLit = Shader.Find(k_FuzzyShader);
            if (fuzzyLit != null)
                material.shader = fuzzyLit;
            else
                Debug.LogWarning($"[FurBrains] Shader {k_FuzzyShader} not found (check the Console for shader errors); " +
                                 "keeping the current shader.");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(k_ColourOutput));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(k_NormalOutput));
            material.EnableKeyword("_NORMALMAP");
            // Remove the earlier detail-layer attempt, if it's still on the material.
            material.SetTexture("_DetailAlbedoMap", null);
            material.SetTexture("_DetailNormalMap", null);
            material.SetTextureScale("_DetailAlbedoMap", Vector2.one);
            material.DisableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            Selection.activeObject = material;
            Debug.Log("[FurBrains] Baked fur into Rabbit_Body_BaseColor_Fur.png and Rabbit_Body_Normal_Fur.png " +
                      "and set them on M_Ziara_Body.");
        }

        // ---------------------------------------------------------------------------------------------------------
        // 1. Flow map

        /// <summary>
        /// For each texel of a k_FlowSize map: the texture-space direction that points down Ziara's body.
        /// <paramref name="covered"/> marks texels that belong to the body (not empty texture space).
        /// </summary>
        static bool BuildFlowMap(GameObject prefab, out Vector2[] flow, out bool[] covered)
        {
            flow = new Vector2[k_FlowSize * k_FlowSize];
            covered = new bool[k_FlowSize * k_FlowSize];
            var weight = new float[k_FlowSize * k_FlowSize];

            // Pose the body in a temporary, invisible scene (as FurBrainsFurMaskBaker does) to get world positions.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                SkinnedMeshRenderer body = null;
                foreach (var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.name == "Body")
                        body = r;
                if (body == null)
                {
                    Debug.LogError("[FurBrains] No renderer named \"Body\" in the character prefab.");
                    return false;
                }

                var baked = new Mesh();
                body.BakeMesh(baked);
                var positions = baked.vertices;
                var toWorld = body.transform.localToWorldMatrix;
                for (var i = 0; i < positions.Length; i++)
                    positions[i] = toWorld.MultiplyPoint3x4(positions[i]);
                var uv = baked.uv;
                var triangles = baked.triangles;
                Object.DestroyImmediate(baked);

                for (var t = 0; t < triangles.Length; t += 3)
                {
                    int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                    var e1 = positions[i1] - positions[i0];
                    var e2 = positions[i2] - positions[i0];
                    var normal = Vector3.Cross(e1, e2);
                    if (normal.sqrMagnitude < 1e-14f)
                        continue;
                    normal.Normalize();

                    // Gravity flattened onto the triangle: the way a hair lying on this surface would hang.
                    var down = Vector3.down - normal * Vector3.Dot(Vector3.down, normal);

                    // Write "down" as a mix of the two edges (down = a·e1 + b·e2, solved as a 2 × 2 system), then use
                    // the same mix of the edges' texture-space versions. That converts a 3D direction on the surface
                    // into the matching direction in the texture, whichever way this island is rotated or mirrored.
                    float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2);
                    float g1 = Vector3.Dot(down, e1), g2 = Vector3.Dot(down, e2);
                    var det = d11 * d22 - d12 * d12;
                    if (Mathf.Abs(det) < 1e-20f)
                        continue;
                    var a = (g1 * d22 - g2 * d12) / det;
                    var b = (g2 * d11 - g1 * d12) / det;
                    var dir = a * (uv[i1] - uv[i0]) + b * (uv[i2] - uv[i0]);
                    if (dir.sqrMagnitude < 1e-20f)
                        continue;
                    dir.Normalize();

                    // Steep sides (where gravity clearly points along the surface) count more than flat tops.
                    PaintTriangle(flow, weight, uv[i0], uv[i1], uv[i2], dir * down.magnitude, down.magnitude);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            // Each covered texel now holds a weighted sum of directions: turn it into one unit direction.
            for (var i = 0; i < flow.Length; i++)
            {
                covered[i] = weight[i] > 0f && flow[i].sqrMagnitude > 1e-20f;
                flow[i] = covered[i] ? flow[i].normalized : Vector2.zero;
                weight[i] = covered[i] ? 1f : 0f;
            }

            // Fill the empty texture space around each island with its neighbours' directions (so strands near an
            // island's edge keep their direction), then smooth so the flow bends gently instead of per triangle.
            for (var pass = 0; pass < 6; pass++)
                Dilate(flow, weight);
            for (var pass = 0; pass < 3; pass++)
                Blur(flow);
            for (var i = 0; i < flow.Length; i++)
                flow[i] = flow[i].sqrMagnitude > 1e-12f ? flow[i].normalized : Vector2.down;

            // Count one texel past each island's edge as body too, so the edge pixels get fur.
            var grown = (bool[])covered.Clone();
            for (var y = 0; y < k_FlowSize; y++)
                for (var x = 0; x < k_FlowSize; x++)
                    if (covered[y * k_FlowSize + x])
                        for (var oy = -1; oy <= 1; oy++)
                            for (var ox = -1; ox <= 1; ox++)
                                grown[Clamp(y + oy, k_FlowSize) * k_FlowSize + Clamp(x + ox, k_FlowSize)] = true;
            covered = grown;
            return true;
        }

        /// <summary>Adds <paramref name="value"/> to every flow texel inside the UV triangle (a, b, c).</summary>
        static void PaintTriangle(Vector2[] flow, float[] weight, Vector2 a, Vector2 b, Vector2 c, Vector2 value, float w)
        {
            a *= k_FlowSize; b *= k_FlowSize; c *= k_FlowSize;
            var area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 1e-12f)
                return;

            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            var maxX = Mathf.Min(k_FlowSize - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            var maxY = Mathf.Min(k_FlowSize - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            var paintedAny = false;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    // Barycentric test at the texel centre: all three weights ≥ 0 means inside the triangle.
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var wa = Cross(b - p, c - p) / area;
                    var wb = Cross(c - p, a - p) / area;
                    var wc = 1f - wa - wb;
                    if (wa < 0f || wb < 0f || wc < 0f)
                        continue;
                    flow[y * k_FlowSize + x] += value;
                    weight[y * k_FlowSize + x] += w;
                    paintedAny = true;
                }
            }

            // Triangles smaller than a texel would otherwise be missed: paint their centre's texel.
            if (!paintedAny)
            {
                var centre = (a + b + c) / 3f;
                var i = Clamp((int)centre.y, k_FlowSize) * k_FlowSize + Clamp((int)centre.x, k_FlowSize);
                flow[i] += value;
                weight[i] += w;
            }
        }

        /// <summary>Gives each empty texel the average direction of its non-empty neighbours.</summary>
        static void Dilate(Vector2[] flow, float[] weight)
        {
            var newFlow = (Vector2[])flow.Clone();
            var newWeight = (float[])weight.Clone();
            for (var y = 0; y < k_FlowSize; y++)
            {
                for (var x = 0; x < k_FlowSize; x++)
                {
                    var i = y * k_FlowSize + x;
                    if (weight[i] > 0f)
                        continue;
                    var sum = Vector2.zero;
                    for (var oy = -1; oy <= 1; oy++)
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            var j = Clamp(y + oy, k_FlowSize) * k_FlowSize + Clamp(x + ox, k_FlowSize);
                            if (weight[j] > 0f)
                                sum += flow[j];
                        }
                    if (sum.sqrMagnitude > 1e-20f)
                    {
                        newFlow[i] = sum.normalized;
                        newWeight[i] = 1f;
                    }
                }
            }
            System.Array.Copy(newFlow, flow, flow.Length);
            System.Array.Copy(newWeight, weight, weight.Length);
        }

        /// <summary>3 × 3 average of the (unnormalised) flow, which also averages in each texel's weight.</summary>
        static void Blur(Vector2[] flow)
        {
            var source = (Vector2[])flow.Clone();
            for (var y = 0; y < k_FlowSize; y++)
                for (var x = 0; x < k_FlowSize; x++)
                {
                    var sum = Vector2.zero;
                    for (var oy = -1; oy <= 1; oy++)
                        for (var ox = -1; ox <= 1; ox++)
                            sum += source[Clamp(y + oy, k_FlowSize) * k_FlowSize + Clamp(x + ox, k_FlowSize)];
                    flow[y * k_FlowSize + x] = sum / 9f;
                }
        }

        /// <summary>Flow direction at an output pixel position, smoothly blended between flow texels.</summary>
        static Vector2 SampleFlow(Vector2[] flow, float x, float y)
        {
            var fx = x / k_Size * k_FlowSize - 0.5f;
            var fy = y / k_Size * k_FlowSize - 0.5f;
            var x0 = Mathf.FloorToInt(fx);
            var y0 = Mathf.FloorToInt(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            var top = Vector2.Lerp(flow[Clamp(y0, k_FlowSize) * k_FlowSize + Clamp(x0, k_FlowSize)],
                                   flow[Clamp(y0, k_FlowSize) * k_FlowSize + Clamp(x0 + 1, k_FlowSize)], tx);
            var bottom = Vector2.Lerp(flow[Clamp(y0 + 1, k_FlowSize) * k_FlowSize + Clamp(x0, k_FlowSize)],
                                      flow[Clamp(y0 + 1, k_FlowSize) * k_FlowSize + Clamp(x0 + 1, k_FlowSize)], tx);
            var result = Vector2.Lerp(top, bottom, ty);
            return result.sqrMagnitude > 1e-12f ? result.normalized : Vector2.down;
        }

        static bool IsCovered(bool[] covered, float x, float y) =>
            covered[Clamp((int)(y / k_Size * k_FlowSize), k_FlowSize) * k_FlowSize +
                    Clamp((int)(x / k_Size * k_FlowSize), k_FlowSize)];

        // ---------------------------------------------------------------------------------------------------------
        // 2. Strands

        /// <summary>A k_Size × k_Size image of 0..1: bright on hairs (brightest at the tips), dark in the gaps.</summary>
        static float[] DrawStrands(Vector2[] flow, bool[] covered)
        {
            var height = new float[k_Size * k_Size];
            var random = new System.Random(12345); // fixed seed: the same fur every run
            var attempts = (int)(k_Size * k_Size * k_Density);

            for (var s = 0; s < attempts; s++)
            {
                if (s % 20000 == 0)
                    EditorUtility.DisplayProgressBar("FurBrains fur", "Drawing strands", 0.05f + 0.7f * s / attempts);

                var x = (float)random.NextDouble() * k_Size;
                var y = (float)random.NextDouble() * k_Size;
                if (!IsCovered(covered, x, y))
                    continue;

                // Clumps: a smooth noise (Perlin, the same noise used for terrain) sampled at the strand's root, so
                // neighbouring hairs get almost the same twist and shade and read as one tuft.
                var clumpX = x / k_ClumpSize;
                var clumpY = y / k_ClumpSize;
                var clumpTwist = (Mathf.PerlinNoise(clumpX + 31.7f, clumpY + 12.3f) - 0.5f) * 2f * k_ClumpTwist;
                var clumpShade = Mathf.Lerp(0.65f, 1f, Mathf.PerlinNoise(clumpX + 5.1f, clumpY + 77.9f));

                var twist = (clumpTwist + ((float)random.NextDouble() - 0.5f) * 2f * k_StrandJitter) * Mathf.Deg2Rad;
                var cos = Mathf.Cos(twist);
                var sin = Mathf.Sin(twist);
                var length = Mathf.Lerp(k_MinLength, k_MaxLength, (float)random.NextDouble());
                var brightness = Mathf.Lerp(0.7f, 1f, (float)random.NextDouble()) * clumpShade;

                // Walk along the flow (re-reading it at each step, so long hairs curve with it).
                var steps = Mathf.CeilToInt(length / 0.75f);
                for (var i = 0; i <= steps; i++)
                {
                    var t = i / (float)steps;
                    // Darker at the root, lighter at the tip; thinner toward the tip.
                    Stamp(height, x, y, brightness * Mathf.Lerp(0.35f, 1f, t), k_Width * Mathf.Lerp(1f, 0.5f, t));

                    var dir = SampleFlow(flow, x, y);
                    // Rotate the flow direction by this hair's twist (2D rotation, as in C++ maths code).
                    x += (dir.x * cos - dir.y * sin) * (length / steps);
                    y += (dir.x * sin + dir.y * cos) * (length / steps);
                }
            }
            return height;
        }

        /// <summary>Raises the height around (x, y) with a soft round falloff.</summary>
        static void Stamp(float[] height, float x, float y, float value, float width)
        {
            var r = Mathf.CeilToInt(width + 1f);
            var cx = Mathf.FloorToInt(x);
            var cy = Mathf.FloorToInt(y);
            for (var py = cy - r; py <= cy + r; py++)
            {
                if (py < 0 || py >= k_Size)
                    continue;
                for (var px = cx - r; px <= cx + r; px++)
                {
                    if (px < 0 || px >= k_Size)
                        continue;
                    var dx = px + 0.5f - x;
                    var dy = py + 0.5f - y;
                    var falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / width;
                    if (falloff <= 0f)
                        continue;
                    var index = py * k_Size + px;
                    // Max, not add: overlapping hairs shouldn't pile up into bright blobs.
                    height[index] = Mathf.Max(height[index], value * falloff);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // 3. Colour

        /// <summary>Original colour × a fur factor that averages exactly 1 over the body.</summary>
        static Color32[] BakeColour(Texture2D source, float[] height, bool[] covered)
        {
            // Average height over the body only (empty texture space would skew it).
            double sum = 0;
            var count = 0;
            for (var y = 0; y < k_Size; y++)
                for (var x = 0; x < k_Size; x++)
                    if (IsCovered(covered, x, y))
                    {
                        sum += height[y * k_Size + x];
                        count++;
                    }
            var mean = count > 0 ? (float)(sum / count) : 0f;

            // Colours in the PNG are sRGB; "averages to unchanged" must be done in linear (light) values.
            // Lookup tables avoid millions of Mathf.Pow calls (like a static const array in C++).
            var toLinear = new float[256];
            for (var i = 0; i < 256; i++)
                toLinear[i] = Mathf.GammaToLinearSpace(i / 255f);
            var toGamma = new byte[4096];
            for (var i = 0; i < 4096; i++)
                toGamma[i] = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(i / 4095f) * 255f);

            var src = source.GetPixels32();
            var srcSize = source.width;
            var scale = Mathf.Max(1, srcSize / k_Size); // 4096 source → 2048 output: average 2 × 2 blocks

            var pixels = new Color32[k_Size * k_Size];
            for (var y = 0; y < k_Size; y++)
            {
                for (var x = 0; x < k_Size; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var c = src[Mathf.Min(y * scale + sy, srcSize - 1) * srcSize + Mathf.Min(x * scale + sx, srcSize - 1)];
                            r += toLinear[c.r];
                            g += toLinear[c.g];
                            b += toLinear[c.b];
                        }
                    var n = scale * scale;

                    var factor = IsCovered(covered, x, y) ? 1f + (height[y * k_Size + x] - mean) * k_ColourContrast : 1f;
                    pixels[y * k_Size + x] = new Color32(
                        ToGamma(toGamma, r / n * factor),
                        ToGamma(toGamma, g / n * factor),
                        ToGamma(toGamma, b / n * factor),
                        255);
                }
            }
            return pixels;
        }

        static byte ToGamma(byte[] table, float linear) =>
            table[Mathf.Clamp(Mathf.RoundToInt(linear * 4095f), 0, 4095)];

        // ---------------------------------------------------------------------------------------------------------
        // 4. Normal

        /// <summary>The original normal map, tilted by the slope of the hairs.</summary>
        static Color32[] BakeNormal(Texture2D source, float[] height)
        {
            var src = source.GetPixels32();
            var srcSize = source.width;
            var scale = Mathf.Max(1, srcSize / k_Size);

            // Soften the hair edges first: sharp edges give sharp, ridge-like bumps in the lighting.
            height = BoxBlur(height, k_NormalSoftness);

            var pixels = new Color32[k_Size * k_Size];
            for (var y = 0; y < k_Size; y++)
            {
                for (var x = 0; x < k_Size; x++)
                {
                    // Original normal: average the source block, decoding 0..255 to -1..1.
                    var baseNormal = Vector3.zero;
                    for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var c = src[Mathf.Min(y * scale + sy, srcSize - 1) * srcSize + Mathf.Min(x * scale + sx, srcSize - 1)];
                            baseNormal += new Vector3(c.r / 127.5f - 1f, c.g / 127.5f - 1f, c.b / 127.5f - 1f);
                        }
                    baseNormal.Normalize();

                    // Slope of the hair image (neighbour differences). In a normal map, x follows the texture's
                    // u (right) and y its v (up), so the slope tilts the normal directly.
                    var slopeX = height[y * k_Size + Mathf.Min(x + 1, k_Size - 1)] - height[y * k_Size + Mathf.Max(x - 1, 0)];
                    var slopeY = height[Mathf.Min(y + 1, k_Size - 1) * k_Size + x] - height[Mathf.Max(y - 1, 0) * k_Size + x];

                    // Add the hair tilt to the original normal's tilt ("whiteout" blend), then re-normalise.
                    var normal = new Vector3(
                        baseNormal.x - slopeX * k_NormalStrength,
                        baseNormal.y - slopeY * k_NormalStrength,
                        baseNormal.z).normalized;

                    pixels[y * k_Size + x] = new Color32(
                        (byte)Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }
            return pixels;
        }

        /// <summary>
        /// Returns a blurred copy: each pixel becomes the average of the square of pixels within
        /// <paramref name="radius"/>. Done as two passes (across, then down), which gives the same result as the full
        /// square but is much faster.
        /// </summary>
        static float[] BoxBlur(float[] image, int radius)
        {
            if (radius <= 0)
                return image;
            var across = new float[image.Length];
            var result = new float[image.Length];
            var count = 2 * radius + 1;
            for (var y = 0; y < k_Size; y++)
                for (var x = 0; x < k_Size; x++)
                {
                    var sum = 0f;
                    for (var o = -radius; o <= radius; o++)
                        sum += image[y * k_Size + Clamp(x + o, k_Size)];
                    across[y * k_Size + x] = sum / count;
                }
            for (var y = 0; y < k_Size; y++)
                for (var x = 0; x < k_Size; x++)
                {
                    var sum = 0f;
                    for (var o = -radius; o <= radius; o++)
                        sum += across[Clamp(y + o, k_Size) * k_Size + x];
                    result[y * k_Size + x] = sum / count;
                }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers

        static Texture2D LoadPng(string path)
        {
            // Read the file directly, so the texture doesn't need "Read/Write" enabled in its import settings.
            // linear: true keeps the stored numbers as they are (we do any sRGB conversion ourselves).
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        static void SavePng(string path, Color32[] pixels)
        {
            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            // Textures made with new Texture2D live on the engine side and aren't garbage-collected: free them.
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static int Clamp(int i, int size) => i < 0 ? 0 : i >= size ? size - 1 : i;
    }
}
