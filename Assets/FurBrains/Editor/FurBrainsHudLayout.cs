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

        // Size of the menus' round X (close) buttons, in canvas pixels.
        const float k_CloseButtonSize = 90f;

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
            // Coins and hunger: top-left corner.
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
