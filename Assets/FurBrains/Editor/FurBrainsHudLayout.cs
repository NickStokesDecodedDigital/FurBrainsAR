using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FurBrains.EditorTools.FurBrainsFeedingSetup; // FindInScene

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Puts the HUD and buttons in their standard places: Tools → FurBrains → Arrange HUD In Open Scene.
    /// The setup tools call <see cref="Apply"/> too, so a fresh setup ends up with the same layout.
    /// </summary>
    /// <remarks>
    /// <b>Anchors in one line:</b> a RectTransform's anchor is the point on its parent (0..1 in each direction) it is
    /// pinned to, and its anchored position is a pixel offset from there. Anchoring to (0, 1) keeps the coins at the
    /// top-left corner whatever the screen size; (0.5, 0) keeps the buttons around the bottom centre.
    /// The numbers here are the layout: change them and run the menu item again.
    /// </remarks>
    public static class FurBrainsHudLayout
    {
        // Gap between the food and minigame buttons, in canvas pixels. Its middle is the screen's centre.
        const float k_ButtonGap = 30f;

        // Height of the bottom row's centres above the screen's bottom edge, in canvas pixels.
        const float k_BottomRowY = 150f;

        // Name of the meter object added inside the food and minigame buttons.
        const string k_NeedFillName = "Need Fill";

        // The Options menu's added row and its icon.
        const string k_MuteRowName = "Mute Sounds Toggle";
        const string k_SoundIconPath = "Assets/FurBrains/UI/Icons/Icon_Sound.png";

        // The ring added around each round bottom button.
        const string k_BorderName = "Border";
        const string k_RingSpritePath = "Assets/FurBrains/UI/Icons/Button_Ring.png";

        // Size of the menus' round X (close) buttons, in canvas pixels.
        const float k_CloseButtonSize = 90f;

        // The display-only need meter shown beside the X inside the food and minigame menus, and its size in pixels.
        const string k_BadgeName = "Need Badge";
        const float k_BadgeSize = 120f;

        // The Play badge shown at the bottom centre during minigames (a child of the Minigame HUD).
        const string k_GameBadgeName = "Play Badge";

        [MenuItem("Tools/FurBrains/Arrange HUD In Open Scene")]
        static void Arrange()
        {
            Apply();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FurBrains] HUD arranged. Save the scene (Ctrl+S).");
        }

        /// <summary>Moves the HUD objects that exist in the open scene (missing ones are skipped).</summary>
        public static void Apply()
        {
            // Coins: top-left corner.
            Pin("Pet HUD", new Vector2(0f, 1f), new Vector2(30f, -30f), pivot: new Vector2(0f, 1f));

            // The minigame exit button moves to the top-right so it doesn't cover the coins. (The Options button
            // that normally sits there hides during a game.)
            Pin("Exit Button", new Vector2(1f, 1f), new Vector2(-30f, -30f), pivot: new Vector2(1f, 1f));

            // The food button was originally the template's "Create Button"; accept either name.
            var food = FindFirstInScene("Food Button", "Create Button");
            if (food != null)
            {
                var rect = (RectTransform)food.transform;
                // If the bottom buttons sit in a container (such as "Main"), make it fill the screen, so that
                // "bottom centre" and "top right" below mean the screen's edges on every phone size.
                StretchToFill(rect.parent as RectTransform);

                // Food and minigame buttons side by side, centred on the gap between them.
                var halfStep = (rect.rect.width * rect.localScale.x + k_ButtonGap) * 0.5f;
                Pin(food, new Vector2(0.5f, 0f), new Vector2(-halfStep, k_BottomRowY));
                Pin(FindInScene("Minigame Button"), new Vector2(0.5f, 0f), new Vector2(halfStep, k_BottomRowY));

                // Delete (retry) button: on the same row, right of the minigame button, with the same gap between
                // their edges as between the food and minigame buttons. Measured edge to edge (half of each
                // button's on-screen width plus the gap), because the delete button is smaller.
                var delete = FindInScene("Delete Button");
                if (delete != null)
                {
                    var deleteRect = (RectTransform)delete.transform;
                    var buttonHalf = rect.rect.width * rect.localScale.x * 0.5f;
                    var deleteHalf = deleteRect.rect.width * deleteRect.localScale.x * 0.5f;
                    var deleteX = halfStep + buttonHalf + k_ButtonGap + deleteHalf;
                    Pin(delete, new Vector2(0.5f, 0f), new Vector2(deleteX, k_BottomRowY));
                }
            }

            // The template's Options (⋯) button: top-right corner.
            Pin("Options Button", new Vector2(1f, 1f), new Vector2(-100f, -100f));

            StyleCloseButtons();

            // Need meters inside the activity buttons: hunger in the food button, play in the minigame button.
            AddNeedMeter(food, NeedMeter.NeedType.Hunger);
            AddNeedMeter(FindInScene("Minigame Button"), NeedMeter.NeedType.Play);
            RemoveOldHungerBar();

            // A white ring around each round bottom button (after the meters, so it can sit on top of the fill).
            AddBorder(food);
            AddBorder(FindInScene("Minigame Button"));
            AddBorder(FindInScene("Delete Button"));

            // The open menus cover the bottom row, so each menu gets a copy of its button's meter beside its X
            // (hunger left of the food menu's X, play right of the minigame menu's X: the same sides as the buttons).
            AddMenuNeedBadge(food, FindFirstInScene("Food Menu Animator", "Object Menu Animator"), -1f);
            AddMenuNeedBadge(FindInScene("Minigame Button"), FindInScene("Minigame Menu Animator"), 1f);

            // The bottom buttons hide during a game, so show the Play meter at the bottom centre while one runs.
            AddInGamePlayBadge(FindInScene("Minigame Button"));

            SetUpOptionsMenu();
        }

        /// <summary>
        /// Trims the template's Options (⋯) menu to what this app needs: "Visualize Surfaces" plus a new
        /// "Mute Sounds" row. The other template rows are switched off (not deleted), so they can be brought back by
        /// ticking them in the Inspector.
        /// </summary>
        /// <remarks>
        /// The menu ("Options Modal") is a column of 64-pixel rows anchored to its top. The mute row is a copy of the
        /// Visualize Surfaces row, so it looks the same; its copied click hookup (which toggled the planes) is
        /// replaced by a MuteSoundsToggle that adds its own listeners at runtime.
        /// </remarks>
        static void SetUpOptionsMenu()
        {
            var modal = FindInScene("Options Modal");
            if (modal == null)
                return;
            var modalTransform = modal.transform;

            // Hide the rows a children's app doesn't need (the AR debug tools and the template's hints/clear-all).
            foreach (var rowName in new[] { "Hints Button", "Remove Objects Button", "Debug Menu Toggle" })
            {
                var row = modalTransform.Find(rowName);
                if (row != null && row.gameObject.activeSelf)
                {
                    Undo.RecordObject(row.gameObject, "Set Up Options Menu");
                    row.gameObject.SetActive(false);
                }
            }

            var planeRow = modalTransform.Find("Debug Plane Toggle") as RectTransform;
            if (planeRow == null)
                return;
            Undo.RecordObject(planeRow, "Set Up Options Menu");
            planeRow.anchoredPosition = new Vector2(planeRow.anchoredPosition.x, 0f); // first row
            var rowHeight = planeRow.rect.height;

            var muteRow = modalTransform.Find(k_MuteRowName) as RectTransform;
            if (muteRow == null)
            {
                // Object.Instantiate copies a scene object with all its children and components (like the Editor's
                // Duplicate). Register it with Undo so Ctrl+Z removes it again.
                muteRow = (RectTransform)Object.Instantiate(planeRow.gameObject, modalTransform).transform;
                Undo.RegisterCreatedObjectUndo(muteRow.gameObject, "Set Up Options Menu");
                muteRow.name = k_MuteRowName;

                // Drop the copied click hookup (it would toggle the surfaces), the same trick IconButtonMenu uses.
                var button = muteRow.GetComponent<Button>();
                if (button != null)
                    button.onClick = new Button.ButtonClickedEvent();

                var label = muteRow.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (label != null)
                    label.text = "Mute Sounds";

                var icon = muteRow.Find("Icon");
                var sound = AssetDatabase.LoadAssetAtPath<Sprite>(k_SoundIconPath);
                if (icon != null && sound != null && icon.TryGetComponent<Image>(out var iconImage))
                {
                    iconImage.sprite = sound;
                    iconImage.preserveAspect = true;
                }

                var toggle = muteRow.gameObject.AddComponent<MuteSoundsToggle>();
                SetReference(toggle, "m_Button", button);
                SetReference(toggle, "m_Switch", muteRow.GetComponentInChildren<Slider>(true));
            }
            Undo.RecordObject(muteRow, "Set Up Options Menu");
            muteRow.anchoredPosition = new Vector2(muteRow.anchoredPosition.x, -rowHeight); // second row

            // Shrink the panel to the two rows that are left.
            var modalRect = (RectTransform)modalTransform;
            Undo.RecordObject(modalRect, "Set Up Options Menu");
            // The pivot is the point that stays put when the size changes. The template pivots the panel at its
            // vertical middle, which would pull its top edge down; move the pivot to the top edge first (keeping that
            // edge where it is), so the panel shrinks up toward the ⋯ button.
            if (!Mathf.Approximately(modalRect.pivot.y, 1f))
            {
                var top = modalRect.anchoredPosition.y + (1f - modalRect.pivot.y) * modalRect.rect.height;
                modalRect.pivot = new Vector2(modalRect.pivot.x, 1f);
                modalRect.anchoredPosition = new Vector2(modalRect.anchoredPosition.x, top);
            }
            modalRect.sizeDelta = new Vector2(modalRect.sizeDelta.x, rowHeight * 2f);
        }

        /// <summary>
        /// Adds a "Border" child to a round button: the ring sprite (UI/Icons/Button_Ring.png) stretched over the
        /// button's edge. It's placed just above the need fill (if any) and below the icon, so the ring frames the
        /// fill and the icon stays on top. Skipped if the button already has one; recolour it in the Inspector.
        /// </summary>
        static void AddBorder(GameObject button)
        {
            if (button == null || button.transform.Find(k_BorderName) != null)
                return;
            var ring = AssetDatabase.LoadAssetAtPath<Sprite>(k_RingSpritePath);
            if (ring == null)
            {
                Debug.LogWarning($"[FurBrains] {k_RingSpritePath} not found (or not imported as a Sprite); no button borders added.");
                return;
            }

            var border = CreateUiObject(k_BorderName, button.transform);
            var rect = (RectTransform)border.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // UI draws children in order: background (the button itself) → need fill → border → icon and label.
            var fill = button.transform.Find(k_NeedFillName);
            border.transform.SetSiblingIndex(fill != null ? fill.GetSiblingIndex() + 1 : 0);

            var image = border.AddComponent<Image>();
            image.sprite = ring;
            image.preserveAspect = true;
            image.color = new Color(1f, 1f, 1f, 0.95f);
            image.raycastTarget = false; // taps go through to the button
        }

        /// <summary>
        /// Adds a "Need Fill" child to a round button: an Image in the button's own shape that fills from the bottom
        /// (Filled, Vertical, Bottom), drawn behind the icon, with a NeedMeter setting its fill. Skipped if the button
        /// already has one, so re-running keeps any restyling done in the Inspector.
        /// </summary>
        static void AddNeedMeter(GameObject button, NeedMeter.NeedType need)
        {
            if (button == null || button.transform.Find(k_NeedFillName) != null)
                return;

            var fill = CreateUiObject(k_NeedFillName, button.transform);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // First child = drawn first, i.e. behind the icon and label (UI draws children in order).
            fill.transform.SetAsFirstSibling();

            var image = fill.AddComponent<Image>();
            var buttonImage = button.GetComponent<Image>();
            image.sprite = buttonImage != null ? buttonImage.sprite : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = (int)Image.OriginVertical.Bottom;
            image.fillAmount = 0.8f;
            image.preserveAspect = true;
            image.raycastTarget = false; // taps go through to the button itself

            var meter = fill.AddComponent<NeedMeter>();
            meter.need = need;
            SetReference(meter, "m_Fill", image);
        }

        /// <summary>
        /// Adds a "Need Badge" to a menu panel, in its bottom row beside its round X. An open menu covers the bottom
        /// row, so without it the player couldn't watch hunger rise while feeding. <paramref name="side"/> is -1 for
        /// left of the X, +1 for right. Skipped if the panel already has one, so Inspector tweaks are kept.
        /// </summary>
        static void AddMenuNeedBadge(GameObject sourceButton, GameObject menuAnimator, float side)
        {
            if (sourceButton == null || menuAnimator == null)
                return;
            var panel = menuAnimator.transform.Find("Object Menu");
            if (panel == null || panel.Find(k_BadgeName) != null)
                return;

            // In the X's row: the X's height, beside it with the usual gap between edges.
            var rowY = 80f;
            var cancel = panel.Find("Cancel Button") as RectTransform;
            if (cancel != null)
                rowY = cancel.anchoredPosition.y;
            var position = new Vector2(side * (k_CloseButtonSize * 0.5f + k_ButtonGap + k_BadgeSize * 0.5f), rowY);
            CreateNeedBadge(sourceButton, panel, k_BadgeName, position, k_BadgeSize);
        }

        /// <summary>
        /// Adds a Play badge at the bottom centre that MinigameHud shows only while a game runs (the bottom buttons
        /// hide during games, so the player could otherwise not see Play rising). Same size as the real button.
        /// Skipped if the HUD already has one.
        /// </summary>
        static void AddInGamePlayBadge(GameObject minigameButton)
        {
            var hud = Object.FindAnyObjectByType<MinigameHud>(FindObjectsInactive.Include);
            if (minigameButton == null || hud == null || hud.transform.Find(k_GameBadgeName) != null)
                return;

            var buttonRect = (RectTransform)minigameButton.transform;
            var size = buttonRect.rect.width * buttonRect.localScale.x; // on-screen size of the real button
            var badge = CreateNeedBadge(minigameButton, hud.transform, k_GameBadgeName,
                new Vector2(0f, k_BottomRowY), size);
            badge.SetActive(false); // MinigameHud shows it during games

            // Add it to the HUD's "Show While Playing" list (SerializedObject edits the saved field with Undo).
            var serialized = new SerializedObject(hud);
            var list = serialized.FindProperty("m_ShowWhilePlaying");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = badge;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// A display-only copy of a round bottom button (icon, Need Fill meter, ring) under <paramref name="parent"/>,
        /// anchored at its bottom centre with the given offset and on-screen size.
        /// </summary>
        /// <remarks>
        /// It's a copy, so it keeps any restyling of the real button, and its NeedMeter (which reads FurBrainNeeds every
        /// frame) makes it fill live. The Button, its scripts and its caption are removed and nothing on it catches
        /// taps, so taps fall through (e.g. they don't close a menu).
        /// </remarks>
        static GameObject CreateNeedBadge(GameObject sourceButton, Transform parent, string name, Vector2 position,
            float size)
        {
            var badge = Object.Instantiate(sourceButton, parent, false);
            badge.name = name;
            badge.SetActive(true);
            Undo.RegisterCreatedObjectUndo(badge, "Add Need Badge");

            // Scripts on the root go first (e.g. PopupMenu, which requires the Button), then the Button itself;
            // Images stay. Graphic is the base class of Image and text, Selectable of Button.
            foreach (var script in badge.GetComponents<MonoBehaviour>())
                if (!(script is Graphic) && !(script is Selectable))
                    Object.DestroyImmediate(script);
            foreach (var selectable in badge.GetComponents<Selectable>())
            {
                // The real button's dark background comes from its Button tint (Normal Color, near-black), applied
                // at runtime on top of a white circle sprite. Without the Button that tint is gone, so bake it into
                // the Image's own colour; otherwise the badge is a white circle and the white icon disappears.
                if (selectable.transition == Selectable.Transition.ColorTint && selectable.targetGraphic != null)
                    selectable.targetGraphic.color *= selectable.colors.normalColor * selectable.colors.colorMultiplier;
                Object.DestroyImmediate(selectable);
            }

            foreach (var text in badge.GetComponentsInChildren<TMPro.TMP_Text>(true))
                Object.DestroyImmediate(text.gameObject);
            foreach (var graphic in badge.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
                graphic.enabled = true; // PopupMenu hides the real button by disabling its graphics
            }

            var rect = (RectTransform)badge.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            var scale = rect.rect.width > 0f ? size / rect.rect.width : 1f;
            rect.localScale = new Vector3(scale, scale, 1f);
            rect.SetAsLastSibling(); // drawn on top of its parent's background
            return badge;
        }

        /// <summary>
        /// Removes the old top-left hunger bar (hunger is shown in the food button now). The coins stay.
        /// </summary>
        static void RemoveOldHungerBar()
        {
            var bar = FindInScene("Hunger Bar");
            if (bar == null)
                return;
            var hud = bar.GetComponentInParent<PetHud>(true);
            if (hud != null)
            {
                SetReference(hud, "m_HungerFill", null);
                SetReference(hud, "m_HungerBar", null);
            }
            Undo.DestroyObjectImmediate(bar);
            Debug.Log("[FurBrains] Removed the old hunger bar; hunger now shows in the food button.");
        }

        /// <summary>
        /// Turns the food and minigame menus' blue "Cancel" buttons into round X buttons, matching the minigame exit
        /// button: no reading needed. Only the look changes; the buttons stay where they are and keep their wiring
        /// (PopupMenu / the template close the menu when they're tapped).
        /// </summary>
        static void StyleCloseButtons()
        {
            var round = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            // Include inactive objects: closed menus are switched off. (Like FindObjectsOfType, but newer and faster.)
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button.name != "Cancel Button" || !button.gameObject.scene.IsValid())
                    continue;

                var rect = (RectTransform)button.transform;
                Undo.RecordObject(rect, "Style Close Buttons");
                rect.sizeDelta = new Vector2(k_CloseButtonSize, k_CloseButtonSize);

                if (button.TryGetComponent<Image>(out var image))
                {
                    Undo.RecordObject(image, "Style Close Buttons");
                    image.sprite = round;
                    image.type = Image.Type.Simple;
                    image.color = new Color(0f, 0f, 0f, 0.5f);
                }

                // The template's label is a legacy UI Text child.
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    Undo.RecordObject(label, "Style Close Buttons");
                    label.text = "X";
                    label.fontSize = 48;
                    label.fontStyle = FontStyle.Bold;
                    label.color = Color.white;
                    label.alignment = TextAnchor.MiddleCenter;
                    // The template's label box is only 160 × 30, and legacy Text draws nothing at all if the text
                    // doesn't fit (Truncate). Fill the button instead, and allow overflow just in case.
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    var labelRect = label.rectTransform;
                    Undo.RecordObject(labelRect, "Style Close Buttons");
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                }
            }
        }

        /// <summary>
        /// Makes a container RectTransform fill its parent (anchors at both corners, no offsets), unless it's the
        /// canvas itself or already does.
        /// </summary>
        static void StretchToFill(RectTransform container)
        {
            if (container == null || container.GetComponent<Canvas>() != null)
                return;
            if (container.anchorMin == Vector2.zero && container.anchorMax == Vector2.one &&
                container.offsetMin == Vector2.zero && container.offsetMax == Vector2.zero)
                return;
            Undo.RecordObject(container, "Arrange HUD");
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = container.offsetMax = Vector2.zero;
            container.pivot = new Vector2(0.5f, 0.5f);
            Debug.Log($"[FurBrains] Made \"{container.name}\" fill the screen so the buttons inside it anchor to the screen edges.", container);
        }

        /// <summary>Anchors an object at a point on its parent, with an offset. Keeps its size; pivot is optional.</summary>
        static void Pin(string objectName, Vector2 anchor, Vector2 position, Vector2? pivot = null) =>
            Pin(FindInScene(objectName), anchor, position, pivot);

        static void Pin(GameObject go, Vector2 anchor, Vector2 position, Vector2? pivot = null)
        {
            if (go == null)
                return;
            var rect = (RectTransform)go.transform;
            Undo.RecordObject(rect, "Arrange HUD");
            rect.anchorMin = rect.anchorMax = anchor;
            if (pivot.HasValue)
                rect.pivot = pivot.Value;
            rect.anchoredPosition = position;
        }
    }
}
