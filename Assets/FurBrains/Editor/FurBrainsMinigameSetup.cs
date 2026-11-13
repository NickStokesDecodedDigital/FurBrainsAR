using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FurBrains.EditorTools.FurBrainsFeedingSetup; // reuse FindInScene, GetOrAdd, SetReference, ...

namespace FurBrains.EditorTools
{
    /// <summary>
    /// One-click scene setup for minigames: Tools → FurBrains → Set Up Minigames In Open Scene.
    /// </summary>
    /// <remarks>
    /// Creates (or reuses, if they already exist):
    /// <list type="bullet">
    /// <item>The Beach Ball Rally, Bubble Pop, Cup Shuffle and Copy Ziara prefabs, materials and MinigameDefinitions, in
    /// Assets/FurBrains/Minigames/ (existing assets are kept, so Inspector tweaks survive re-running).</item>
    /// <item>A MinigameRunner on "FurBrains Game".</item>
    /// <item>A "Minigame HUD" under the UI canvas: exit button, live score and results panel.</item>
    /// <item>A "Minigame Button" (a copy of the food/create button, beside it) with a PopupMenu, opening a
    /// "Minigame Menu Animator" (a copy of the food menu) whose Content gets a MinigameMenu instead of the FoodMenu.</item>
    /// </list>
    /// Everything goes through Undo (Ctrl+Z reverts it). Save the scene afterwards.
    /// </remarks>
    public static class FurBrainsMinigameSetup
    {
        const string k_Folder = "Assets/FurBrains/Minigames/BeachBall";
        const string k_PrefabPath = k_Folder + "/BeachBallRally.prefab";
        const string k_DefinitionPath = k_Folder + "/BeachBall.asset";
        const string k_MaterialPath = k_Folder + "/M_BeachBall.mat";
        const string k_TexturePath = k_Folder + "/BeachBall_Stripes.png";

        [MenuItem("Tools/FurBrains/Set Up Minigames In Open Scene")]
        static void SetUp()
        {
            Undo.SetCurrentGroupName("Set Up FurBrains Minigames");
            var undoGroup = Undo.GetCurrentGroup();

            var definitions = new[]
            {
                LoadOrCreateBeachBall(), LoadOrCreateBubblePop(), LoadOrCreateCupShuffle(), LoadOrCreateCopyZiara(),
            };

            var game = FindInScene("FurBrains Game");
            if (game == null)
            {
                Debug.LogError("[FurBrains] No \"FurBrains Game\" object. Run Set Up Feeding In Open Scene first.");
                return;
            }
            var runner = GetOrAdd<MinigameRunner>(game);

            var canvasObject = FindInScene("UI");
            var canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[FurBrains] No UI canvas found in the open scene.");
                return;
            }

            var hud = FindInScene("Minigame HUD");
            if (hud == null)
                hud = CreateHud(canvas.transform);
            var hudComponent = hud.GetComponent<MinigameHud>();
            SetReference(hudComponent, "m_Runner", runner);
            if (new SerializedObject(hudComponent).FindProperty("m_TimeFill").objectReferenceValue == null)
                SetReference(hudComponent, "m_TimeFill", CreateTimeBar(hud.transform));
            var options = FindInScene("Options Button");
            if (options != null)
            {
                var so = new SerializedObject(hudComponent);
                var list = so.FindProperty("m_HideWhilePlaying");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = options;
                so.ApplyModifiedProperties();
            }

            if (!SetUpMenu(runner, definitions))
                return;

            FurBrainsHudLayout.Apply();
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = FindInScene("Minigame Button");
            Debug.Log("[FurBrains] Minigames set up. Save the scene (Ctrl+S).");
        }

        // ---------- Menu button + popup menu (copies of the food ones) ----------

        static bool SetUpMenu(MinigameRunner runner, MinigameDefinition[] definitions)
        {
            // The food button and menu may have been renamed from the template's names; accept either.
            var createButton = FindFirstInScene("Food Button", "Create Button");
            var foodMenu = FindFirstInScene("Food Menu Animator", "Object Menu Animator");
            if (createButton == null || foodMenu == null)
            {
                Debug.LogError("[FurBrains] Couldn't find the food button (\"Food Button\"/\"Create Button\") or the food " +
                               "menu (\"Food Menu Animator\"/\"Object Menu Animator\").");
                return false;
            }

            // The menu: a copy of the food menu with its FoodMenu swapped for a MinigameMenu.
            var menu = FindInScene("Minigame Menu Animator");
            if (menu == null)
            {
                menu = Duplicate(foodMenu, "Minigame Menu Animator");
                var content = FindChild(menu.transform, "Content");
                var food = content != null ? content.GetComponent<FoodMenu>() : null;
                if (food != null)
                    Undo.DestroyObjectImmediate(food);
            }
            var menuContent = FindChild(menu.transform, "Content");
            var menuPanel = FindChild(menu.transform, "Object Menu");
            var cancel = FindChild(menu.transform, "Cancel Button");
            if (cancel != null)
                ClearClicks(cancel.GetComponent<Button>());

            // The button: a copy of the food button, placed to its right, tinted so the two can be told apart.
            var button = FindInScene("Minigame Button");
            if (button == null)
            {
                button = Duplicate(createButton, "Minigame Button"); // FurBrainsHudLayout.Apply positions it
                var icon = FindChild(button.transform, "Icon");
                if (icon != null && icon.TryGetComponent<Image>(out var iconImage))
                    iconImage.color = new Color(0.55f, 0.85f, 1f);
                button.SetActive(true);
            }
            ClearClicks(button.GetComponent<Button>());

            var popup = GetOrAdd<PopupMenu>(button);
            SetReference(popup, "m_MenuAnimator", menu.GetComponent<Animator>());
            SetReference(popup, "m_MenuPanel", menuPanel != null ? menuPanel.GetComponent<RectTransform>() : null);
            SetReference(popup, "m_CancelButton", cancel != null ? cancel.GetComponent<Button>() : null);
            SetReference(popup, "m_Runner", runner);

            if (menuContent == null)
            {
                Debug.LogWarning("[FurBrains] The minigame menu copy has no \"Content\"; add a MinigameMenu by hand.");
                return true;
            }
            var minigameMenu = GetOrAdd<MinigameMenu>(menuContent.gameObject);
            var menuSo = new SerializedObject(minigameMenu);
            // Add any game that isn't on the menu yet (existing entries and their order are kept).
            var games = menuSo.FindProperty("m_Games");
            foreach (var definition in definitions)
            {
                var listed = false;
                for (var i = 0; i < games.arraySize; i++)
                    listed |= games.GetArrayElementAtIndex(i).objectReferenceValue == definition;
                if (listed)
                    continue;
                games.arraySize++;
                games.GetArrayElementAtIndex(games.arraySize - 1).objectReferenceValue = definition;
            }
            menuSo.FindProperty("m_Runner").objectReferenceValue = runner;
            menuSo.FindProperty("m_Popup").objectReferenceValue = popup;
            menuSo.ApplyModifiedProperties();
            return true;
        }

        static GameObject Duplicate(GameObject source, string name)
        {
            var copy = Object.Instantiate(source, source.transform.parent);
            copy.name = name;
            copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            Undo.RegisterCreatedObjectUndo(copy, $"Create {name}");
            return copy;
        }

        /// <summary>Removes scene-wired (persistent) onClick listeners copied from the template.</summary>
        static void ClearClicks(Button button)
        {
            if (button == null)
                return;
            Undo.RecordObject(button, "Clear button clicks");
            button.onClick = new Button.ButtonClickedEvent();
            EditorUtility.SetDirty(button);
        }

        static Transform FindChild(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        // ---------- HUD ----------

        static GameObject CreateHud(Transform canvas)
        {
            var panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var hud = CreateUiObject("Minigame HUD", canvas);
            var hudRect = (RectTransform)hud.transform;
            hudRect.anchorMin = Vector2.zero;
            hudRect.anchorMax = Vector2.one;
            hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;

            // Exit button, top right (the coins are top left).
            var exit = CreateUiObject("Exit Button", hud.transform);
            Place(exit, new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(80f, 80f));
            var exitImage = exit.AddComponent<Image>();
            exitImage.sprite = roundSprite;
            exitImage.color = new Color(0f, 0f, 0f, 0.5f);
            var exitButton = exit.AddComponent<Button>();
            AddText(exit.transform, "X", "X", 48f, Vector2.zero, Vector2.one);

            // Live score, top centre.
            var live = CreateUiObject("Live Score", hud.transform);
            Place(live, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(300f, 100f));
            var liveText = live.AddComponent<TextMeshProUGUI>();
            Style(liveText, "0", 80f);

            // Results panel, centre.
            var panel = CreateUiObject("Results Panel", hud.transform);
            Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340f, 300f));
            ((RectTransform)panel.transform).pivot = new Vector2(0.5f, 0.5f);
            var panelImage = panel.AddComponent<Image>();
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0f, 0f, 0f, 0.6f);

            var result = AddText(panel.transform, "Result Text", "0", 80f, new Vector2(0f, 0.62f), Vector2.one);

            var coin = CreateUiObject("Coin Icon", panel.transform);
            var coinRect = (RectTransform)coin.transform;
            coinRect.anchorMin = coinRect.anchorMax = new Vector2(0.5f, 0.5f);
            coinRect.anchoredPosition = new Vector2(-60f, 0f);
            coinRect.sizeDelta = new Vector2(50f, 50f);
            var coinImage = coin.AddComponent<Image>();
            coinImage.sprite = roundSprite;
            coinImage.color = new Color(1f, 0.8f, 0.2f);
            coinImage.raycastTarget = false;
            var coins = AddText(panel.transform, "Coins Text", "+0", 56f, new Vector2(0.45f, 0.35f), new Vector2(1f, 0.65f));
            coins.alignment = TextAlignmentOptions.MidlineLeft;

            var ok = CreateUiObject("OK Button", panel.transform);
            var okRect = (RectTransform)ok.transform;
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 0f);
            okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(0f, 20f);
            okRect.sizeDelta = new Vector2(200f, 70f);
            var okImage = ok.AddComponent<Image>();
            okImage.sprite = panelSprite;
            okImage.type = Image.Type.Sliced;
            okImage.color = new Color(0.45f, 0.85f, 0.4f);
            var okButton = ok.AddComponent<Button>();
            AddText(ok.transform, "Text", "OK", 40f, Vector2.zero, Vector2.one);

            var component = Undo.AddComponent<MinigameHud>(hud);
            SetReference(component, "m_ExitButton", exitButton);
            SetReference(component, "m_LiveScoreText", liveText);
            SetReference(component, "m_ResultsPanel", panel);
            SetReference(component, "m_ResultText", result);
            SetReference(component, "m_CoinsText", coins);
            SetReference(component, "m_OkButton", okButton);
            return hud;
        }

        /// <summary>A bar under the live score that empties as a timed game's time runs out. Returns its fill.</summary>
        static Image CreateTimeBar(Transform hud)
        {
            var panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var bar = CreateUiObject("Time Bar", hud);
            Place(bar, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(300f, 22f));
            ((RectTransform)bar.transform).pivot = new Vector2(0.5f, 1f);
            var barImage = bar.AddComponent<Image>();
            barImage.sprite = panelSprite;
            barImage.type = Image.Type.Sliced;
            barImage.color = new Color(0f, 0f, 0f, 0.5f);
            barImage.raycastTarget = false;

            var fill = CreateUiObject("Fill", bar.transform);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.55f, 0.85f, 1f);
            fillImage.raycastTarget = false;

            bar.SetActive(false); // MinigameHud shows it during timed games
            return fillImage;
        }

        static TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = CreateUiObject(name, parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.AddComponent<TextMeshProUGUI>();
            Style(label, text, size);
            return label;
        }

        static void Style(TextMeshProUGUI label, string text, float size)
        {
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        // ---------- Beach ball assets ----------

        static MinigameDefinition LoadOrCreateBeachBall()
        {
            EnsureFolder(k_Folder);

            var prefab = AssetDatabase.LoadAssetAtPath<BeachBallMinigame>(k_PrefabPath);
            if (prefab == null)
                prefab = CreateBeachBallPrefab();

            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(k_DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MinigameDefinition>();
                definition.EditorInitialize("Beach Ball", prefab);
                AssetDatabase.CreateAsset(definition, k_DefinitionPath);
                AssetDatabase.SaveAssets();
            }
            return definition;
        }

        static BeachBallMinigame CreateBeachBallPrefab()
        {
            var root = new GameObject("BeachBallRally");
            try
            {
                var game = root.AddComponent<BeachBallMinigame>();

                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Ball";
                Object.DestroyImmediate(ball.GetComponent<Collider>()); // taps are tested on screen, no physics needed
                ball.transform.SetParent(root.transform, false);
                ball.transform.localScale = Vector3.one * 0.12f;
                ball.GetComponent<MeshRenderer>().sharedMaterial = CreateBallMaterial();

                SetReference(game, "m_Ball", ball.transform);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                return saved.GetComponent<BeachBallMinigame>();
            }
            finally
            {
                // The temporary object was only needed to build the prefab asset.
                Object.DestroyImmediate(root);
            }
        }

        static Material CreateBallMaterial()
        {
            // Six vertical colour stripes: Unity's sphere UVs wrap around horizontally, so these become the
            // classic beach-ball segments.
            var colors = new[]
            {
                new Color(0.95f, 0.25f, 0.25f), Color.white, new Color(0.25f, 0.55f, 0.95f),
                new Color(1f, 0.85f, 0.2f), Color.white, new Color(0.35f, 0.8f, 0.35f),
            };
            const int width = 192, height = 4;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                texture.SetPixel(x, y, colors[x * colors.Length / width]);
            File.WriteAllBytes(k_TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(k_TexturePath);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath));
            material.SetFloat("_Smoothness", 0.6f);
            AssetDatabase.CreateAsset(material, k_MaterialPath);
            return material;
        }

        // ---------- Bubble Pop assets ----------

        const string k_BubbleFolder = "Assets/FurBrains/Minigames/BubblePop";

        static MinigameDefinition LoadOrCreateBubblePop()
        {
            EnsureFolder(k_BubbleFolder);
            var prefabPath = k_BubbleFolder + "/BubblePop.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<BubblePopMinigame>(prefabPath);
            if (prefab == null)
            {
                var root = new GameObject("BubblePop");
                try
                {
                    var game = root.AddComponent<BubblePopMinigame>();
                    var bubble = CreateShape(PrimitiveType.Sphere, "Bubble", root.transform, Vector3.one * 0.08f,
                        CreateMaterial(k_BubbleFolder + "/M_Bubble.mat", new Color(0.7f, 0.9f, 1f, 0.35f), 0.95f, true));
                    bubble.SetActive(false); // a template: the game copies it
                    SetReference(game, "m_BubbleTemplate", bubble);
                    SetReference(game, "m_GoldMaterial",
                        CreateMaterial(k_BubbleFolder + "/M_BubbleGold.mat", new Color(1f, 0.8f, 0.2f, 0.6f), 0.95f, true));
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath).GetComponent<BubblePopMinigame>();
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            return LoadOrCreateDefinition(k_BubbleFolder + "/BubblePop.asset", "Bubble Pop", prefab, 1, 2);
        }

        // ---------- Cup Shuffle assets ----------

        const string k_CupFolder = "Assets/FurBrains/Minigames/CupShuffle";

        static MinigameDefinition LoadOrCreateCupShuffle()
        {
            EnsureFolder(k_CupFolder);
            var prefabPath = k_CupFolder + "/CupShuffle.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<CupShuffleMinigame>(prefabPath);
            if (prefab == null)
            {
                var root = new GameObject("CupShuffle");
                try
                {
                    var game = root.AddComponent<CupShuffleMinigame>();
                    // A cylinder is 2 units tall at scale 1: Y scale 0.05 makes a 10 cm tall, 8 cm wide cup.
                    var cup = CreateShape(PrimitiveType.Cylinder, "Cup", root.transform, new Vector3(0.08f, 0.05f, 0.08f),
                        CreateMaterial(k_CupFolder + "/M_Cup.mat", new Color(0.95f, 0.55f, 0.75f), 0.4f, false));
                    cup.SetActive(false); // a template: the game copies it
                    var treat = CreateShape(PrimitiveType.Sphere, "Treat", root.transform, Vector3.one * 0.04f,
                        CreateMaterial(k_CupFolder + "/M_Treat.mat", new Color(0.9f, 0.15f, 0.25f), 0.7f, false));
                    SetReference(game, "m_CupTemplate", cup);
                    SetReference(game, "m_Treat", treat.transform);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath).GetComponent<CupShuffleMinigame>();
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            // Won or lost: a win counts as 1 point, so 10 coins per point = 10 for a win; a loss pays the minimum, 3.
            return LoadOrCreateDefinition(k_CupFolder + "/CupShuffle.asset", "Cup Shuffle", prefab, 10, 3);
        }

        // ---------- Copy Ziara assets ----------

        const string k_CopyFolder = "Assets/FurBrains/Minigames/CopyZiara";

        static MinigameDefinition LoadOrCreateCopyZiara()
        {
            EnsureFolder(k_CopyFolder);
            var prefabPath = k_CopyFolder + "/CopyZiara.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<CopyZiaraMinigame>(prefabPath);
            if (prefab == null)
            {
                var root = new GameObject("CopyZiara");
                try
                {
                    // [RequireComponent(typeof(AudioSource))] on the game adds the AudioSource along with it.
                    var game = root.AddComponent<CopyZiaraMinigame>();
                    var audio = root.GetComponent<AudioSource>();
                    audio.playOnAwake = false;
                    audio.spatialBlend = 0f; // 2D: same volume wherever the phone is, so it's always clear

                    // The pads' colour is set per pad at runtime; this material only needs emission switched on.
                    // If no material asset uses the emission shader variant, a build can leave it out, and lighting
                    // the pads at runtime would then do nothing.
                    var material = CreateMaterial(k_CopyFolder + "/M_Pad.mat", Color.white, 0.3f, false);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.black);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    EditorUtility.SetDirty(material);

                    // A cylinder is 2 units tall: Y scale 0.01 makes a 2 cm thick disc. Its width is set at runtime
                    // from the FurBrain's height.
                    var pad = CreateShape(PrimitiveType.Cylinder, "Pad", root.transform, new Vector3(1f, 0.01f, 1f), material);
                    pad.SetActive(false); // a template: the game copies it
                    SetReference(game, "m_PadTemplate", pad);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath).GetComponent<CopyZiaraMinigame>();
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            // Score = longest sequence copied: 1 coin per step, at least 2.
            return LoadOrCreateDefinition(k_CopyFolder + "/CopyZiara.asset", "Copy Ziara", prefab, 1, 2);
        }

        // ---------- Shared asset helpers ----------

        static MinigameDefinition LoadOrCreateDefinition(string path, string displayName, Minigame prefab,
            float coinsPerPoint, int minimumCoins)
        {
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(path);
            if (definition != null)
                return definition; // keep any values already tuned in the Inspector
            definition = ScriptableObject.CreateInstance<MinigameDefinition>();
            definition.EditorInitialize(displayName, prefab, coinsPerPoint, minimumCoins);
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            return definition;
        }

        /// <summary>A primitive shape without a collider (minigame taps are tested on screen, not with physics).</summary>
        static GameObject CreateShape(PrimitiveType type, string name, Transform parent, Vector3 scale, Material material)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            Object.DestroyImmediate(shape.GetComponent<Collider>());
            shape.transform.SetParent(parent, false);
            shape.transform.localScale = scale;
            shape.GetComponent<MeshRenderer>().sharedMaterial = material;
            return shape;
        }

        /// <summary>Creates (or loads) a URP Lit material asset, optionally see-through.</summary>
        static Material CreateMaterial(string path, Color color, float smoothness, bool transparent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (transparent)
            {
                // What ticking Surface Type → Transparent in the material Inspector does.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f); // alpha blending
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
