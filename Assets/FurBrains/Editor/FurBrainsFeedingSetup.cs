using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// One-click scene setup for feeding and coins: Tools → FurBrains → Set Up Feeding In Open Scene.
    /// </summary>
    /// <remarks>
    /// Creates (or reuses, if they already exist):
    /// <list type="bullet">
    /// <item>Starter food assets in Assets/FurBrains/Food/ (edit their price and hunger in the Inspector).</item>
    /// <item>A "FurBrains Game" object holding PlayerWallet (coins) and FurBrainNeeds (hunger).</item>
    /// <item>A "Pet HUD" under the UI canvas, top right: coin count and hunger bar, driven by PetHud.</item>
    /// <item>A FoodMenu on the template's object-menu Content, so the create button opens the food menu.</item>
    /// <item>The FurBrainSummoner's re-summon prompt, pointed at the onboarding's "Prompt Tap to Place".</item>
    /// </list>
    /// Everything goes through Undo, so Ctrl+Z reverts it. Save the scene afterwards. Doing this with a tool instead of
    /// editing SampleScene.unity's YAML keeps the fileID/GUID references correct.
    /// </remarks>
    public static class FurBrainsFeedingSetup
    {
        const string k_FoodFolder = "Assets/FurBrains/Food";

        [MenuItem("Tools/FurBrains/Set Up Feeding In Open Scene")]
        static void SetUp()
        {
            Undo.SetCurrentGroupName("Set Up FurBrains Feeding");
            var undoGroup = Undo.GetCurrentGroup();

            var foods = LoadOrCreateFoods();

            var game = FindInScene("FurBrains Game");
            if (game == null)
            {
                game = new GameObject("FurBrains Game");
                Undo.RegisterCreatedObjectUndo(game, "Create FurBrains Game");
            }
            var wallet = GetOrAdd<PlayerWallet>(game);
            var needs = GetOrAdd<FurBrainNeeds>(game);

            var canvasObject = FindInScene("UI");
            var canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[FurBrains] No UI canvas found in the open scene.");
                return;
            }

            var hud = FindInScene("Pet HUD");
            if (hud == null)
                hud = CreateHud(canvas.transform);
            var hudComponent = GetOrAdd<PetHud>(hud);
            SetReference(hudComponent, "m_Wallet", wallet);
            SetReference(hudComponent, "m_Needs", needs);

            var summoner = Object.FindAnyObjectByType<FurBrainSummoner>(FindObjectsInactive.Include);
            if (summoner != null)
            {
                var prompt = FindInScene("Prompt Tap to Place");
                if (prompt != null)
                    SetReference(summoner, "m_SummonPrompt", prompt);
                else
                    Debug.LogWarning("[FurBrains] \"Prompt Tap to Place\" not found; set FurBrainSummoner's Summon Prompt by hand.");
            }
            else
            {
                Debug.LogWarning("[FurBrains] No FurBrainSummoner in the scene. Add one to the Object Spawner first.");
            }

            var content = FindMenuContent();
            if (content != null)
            {
                var menu = GetOrAdd<FoodMenu>(content);
                var so = new SerializedObject(menu);
                var list = so.FindProperty("m_Foods");
                list.arraySize = foods.Count;
                for (var i = 0; i < foods.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = foods[i];
                so.FindProperty("m_Wallet").objectReferenceValue = wallet;
                so.FindProperty("m_Needs").objectReferenceValue = needs;
                so.FindProperty("m_Summoner").objectReferenceValue = summoner;
                so.FindProperty("m_MenuManager").objectReferenceValue =
                    Object.FindAnyObjectByType<UnityEngine.XR.Templates.AR.ARTemplateMenuManager>(FindObjectsInactive.Include);
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("[FurBrains] Couldn't find the object menu's Content; the food menu wasn't set up.");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = hud;
            Debug.Log($"[FurBrains] Feeding set up with {foods.Count} foods. Save the scene (Ctrl+S).", hud);
        }

        // ---------- Foods ----------

        static List<FoodItem> LoadOrCreateFoods()
        {
            if (!AssetDatabase.IsValidFolder(k_FoodFolder))
                AssetDatabase.CreateFolder("Assets/FurBrains", "Food");

            var existing = AssetDatabase.FindAssets("t:FoodItem", new[] { k_FoodFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<FoodItem>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(f => f != null)
                .ToList();
            if (existing.Count > 0)
                return existing;

            // Starter set: pricier food restores more hunger (with a small bulk bonus).
            var starters = new (string name, int price, float hunger)[]
            {
                ("Berry", 5, 10f),
                ("Apple", 10, 22f),
                ("Sandwich", 20, 45f),
                ("Cake", 40, 100f),
            };
            var foods = new List<FoodItem>();
            foreach (var (name, price, hunger) in starters)
            {
                var food = ScriptableObject.CreateInstance<FoodItem>();
                food.EditorInitialize(name, price, hunger);
                AssetDatabase.CreateAsset(food, $"{k_FoodFolder}/{name}.asset");
                foods.Add(food);
            }
            AssetDatabase.SaveAssets();
            return foods;
        }

        // ---------- HUD ----------

        static GameObject CreateHud(Transform canvas)
        {
            var panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // Top-right corner, to the left of the template's options button.
            var hud = CreateUiObject("Pet HUD", canvas);
            var hudRect = (RectTransform)hud.transform;
            hudRect.anchorMin = hudRect.anchorMax = hudRect.pivot = new Vector2(1f, 1f);
            hudRect.anchoredPosition = new Vector2(-170f, -30f);
            hudRect.sizeDelta = new Vector2(230f, 100f);
            var backdrop = hud.AddComponent<Image>();
            backdrop.sprite = panelSprite;
            backdrop.type = Image.Type.Sliced;
            backdrop.color = new Color(0f, 0f, 0f, 0.35f); // keeps the text readable over the camera image
            backdrop.raycastTarget = false;

            // Row 1: coin icon + count.
            var coin = CreateUiObject("Coin Icon", hud.transform);
            Place(coin, new Vector2(0f, 1f), new Vector2(15f, -10f), new Vector2(40f, 40f));
            var coinImage = coin.AddComponent<Image>();
            coinImage.sprite = roundSprite;
            coinImage.color = new Color(1f, 0.8f, 0.2f);
            coinImage.raycastTarget = false;

            var coinsText = CreateUiObject("Coins Text", hud.transform);
            Place(coinsText, new Vector2(0f, 1f), new Vector2(65f, -5f), new Vector2(150f, 50f));
            var text = coinsText.AddComponent<TextMeshProUGUI>();
            text.text = "100";
            text.fontSize = 40f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.raycastTarget = false;

            // Row 2: hunger bar (dark background with a coloured fill that shrinks toward the left).
            var bar = CreateUiObject("Hunger Bar", hud.transform);
            Place(bar, new Vector2(0f, 1f), new Vector2(15f, -62f), new Vector2(200f, 26f));
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
            // PetHud sets this image's width from hunger (via its right anchor), so a plain image is enough.
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.45f, 0.85f, 0.4f);
            fillImage.raycastTarget = false;

            var hudComponent = Undo.AddComponent<PetHud>(hud);
            SetReference(hudComponent, "m_CoinsText", text);
            SetReference(hudComponent, "m_HungerFill", fillImage);
            return hud;
        }

        static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.layer = parent.gameObject.layer; // the UI layer
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Anchors and pivots a child at the given corner, with a pixel offset and size.</summary>
        static void Place(GameObject go, Vector2 corner, Vector2 offset, Vector2 size)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        // ---------- Helpers ----------

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        /// <summary>Sets a private [SerializeField] through Unity's serialization, as dragging it in the Inspector would.</summary>
        static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        /// <summary>Finds a GameObject by name in the open scene, including inactive ones.</summary>
        static GameObject FindInScene(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t.gameObject;
            return null;
        }

        /// <summary>The "Content" of the template's object menu scroll view.</summary>
        static GameObject FindMenuContent()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Content" && t.GetComponentsInParent<Transform>(true).Any(p => p.name == "Object Menu"))
                    return t.gameObject;
            return null;
        }
    }
}
