using System.IO;
using UnityEditor;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Tools → FurBrains → Set Up Eat Effect: the crumbs that fly out when the FurBrain eats.
    /// </summary>
    /// <remarks>
    /// Does three things, each skipped if already done (so tweaks made in the Inspector are kept):
    /// <list type="number">
    /// <item>Creates the crumb material, Assets/FurBrains/Effects/M_Crumbs.mat: URP's Particles/Unlit shader, which
    /// multiplies by each particle's colour (Lit materials ignore it), with Unity's soft round particle texture.</item>
    /// <item>Adds an "Eat Burst" particle system to ZiaraPet.prefab and links it to FurBrainPet's Eat Burst field.
    /// The pet builder calls <see cref="CreateEatBurst"/> too, so rebuilding the pet keeps the effect.</item>
    /// <item>Gives every Food Item whose Crumb Color is still clear the main colour of its icon.</item>
    /// </list>
    /// </remarks>
    public static class FurBrainsEatEffect
    {
        const string k_Folder = "Assets/FurBrains/Effects";
        const string k_MaterialPath = k_Folder + "/M_Crumbs.mat";
        const string k_PetPrefabPath = "Assets/FurBrains/Prefabs/ZiaraPet.prefab";

        [MenuItem("Tools/FurBrains/Set Up Eat Effect")]
        static void SetUp()
        {
            AddToPetPrefab();
            var coloured = ColourFoodsFromIcons();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FurBrains] Eat effect set up; gave {coloured} food(s) a crumb colour from their icon.");
        }

        static void AddToPetPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_PetPrefabPath) == null)
            {
                Debug.LogWarning($"[FurBrains] {k_PetPrefabPath} not found. Build the pet prefab first.");
                return;
            }
            // Open the prefab file behind the scenes (like Prefab Mode), change it, save it back (same GUID).
            var root = PrefabUtility.LoadPrefabContents(k_PetPrefabPath);
            try
            {
                var pet = root.GetComponent<FurBrainPet>();
                if (pet == null || pet.eatBurst != null)
                    return; // no pet script, or the effect is already there
                pet.eatBurst = CreateEatBurst(root.transform, LoadOrCreateMaterial());
                PrefabUtility.SaveAsPrefabAsset(root, k_PetPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// A particle system for crumbs: a quick upward spray of small soft dots that fall and shrink away. It never
        /// emits on its own; FurBrainPet moves it to the mouth, colours it and calls Emit when it eats.
        /// </summary>
        /// <remarks>
        /// Particle settings are grouped into "modules" (main, emission, shape...). Each property such as
        /// <c>ps.main</c> returns a small struct that writes straight through to the system, which is why it's put in
        /// a local variable and then changed. They're the same settings as in the Particle System Inspector.
        /// </remarks>
        public static ParticleSystem CreateEatBurst(Transform parent, Material material)
        {
            var go = new GameObject("Eat Burst");
            go.transform.SetParent(parent, false);
            // Particles fly along the emitter's forward (+Z) axis: tilt it up and slightly forward (toward the player).
            go.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);   // seconds, random in this range
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);      // metres per second
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);   // metres
            main.gravityModifier = 1.2f;                                       // crumbs fall
            main.simulationSpace = ParticleSystemSimulationSpace.World;        // crumbs stay put if the pet turns
            main.maxParticles = 100;

            var emission = ps.emission;
            emission.enabled = false; // only FurBrainPet's Emit call makes crumbs

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; // a spray, 35° either side of the emitter's direction
            shape.angle = 35f;
            shape.radius = 0.02f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f)); // shrink to nothing

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard; // flat dots that always face the camera
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>The crumb material (created the first time).</summary>
        public static Material LoadOrCreateMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets/FurBrains", "Effects");

            // Particles/Unlit multiplies by each particle's colour, which is how the crumbs take the food's colour.
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            // See-through, so the soft round texture's edges blend (what Surface Type → Transparent does).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, k_MaterialPath);
            return material;
        }

        /// <summary>Sets the crumb colour of every food whose colour is still clear. Returns how many were set.</summary>
        static int ColourFoodsFromIcons()
        {
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:FoodItem"))
            {
                var food = AssetDatabase.LoadAssetAtPath<FoodItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (food == null || food.icon == null || food.crumbColor.a > 0f)
                    continue;
                food.EditorSetCrumbColor(MainColour(food.icon));
                EditorUtility.SetDirty(food);
                count++;
            }
            return count;
        }

        /// <summary>
        /// The average colour of a sprite's visible, non-dark pixels: the dark outline and the transparent background
        /// are skipped, so a red apple gives red rather than a muddy dark red.
        /// </summary>
        static Color MainColour(Sprite sprite)
        {
            // Read the PNG file directly, so the texture doesn't need "Read/Write" enabled in its import settings.
            var path = AssetDatabase.GetAssetPath(sprite.texture);
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(texture);
                return Color.white;
            }

            var pixels = texture.GetPixels();
            var sum = Color.clear;
            var n = 0;
            foreach (var p in pixels)
            {
                if (p.a < 0.5f || p.maxColorComponent < 0.3f)
                    continue; // transparent background, or the dark outline
                sum += p;
                n++;
            }
            // A texture made with new Texture2D isn't freed by the garbage collector on the engine side: free it.
            Object.DestroyImmediate(texture);
            if (n == 0)
                return Color.white;
            var average = sum / n;
            average.a = 1f;
            return average;
        }
    }
}
