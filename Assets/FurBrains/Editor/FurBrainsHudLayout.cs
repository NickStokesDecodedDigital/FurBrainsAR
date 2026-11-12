using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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

            // Food and minigame buttons side by side, centred on the gap between them.
            var food = FindInScene("Create Button");
            if (food != null)
            {
                var rect = (RectTransform)food.transform;
                var halfStep = (rect.rect.width * rect.localScale.x + k_ButtonGap) * 0.5f;
                Pin("Create Button", new Vector2(0.5f, 0f), new Vector2(-halfStep, rect.anchoredPosition.y));
                Pin("Minigame Button", new Vector2(0.5f, 0f), new Vector2(halfStep, rect.anchoredPosition.y));

                // Delete button: on the same row, at the right edge (the same inset as the Options button).
                Pin("Delete Button", new Vector2(1f, 0f), new Vector2(-100f, rect.anchoredPosition.y));
            }
        }

        /// <summary>Anchors an object at a point on its parent, with an offset. Keeps its size; pivot is optional.</summary>
        static void Pin(string objectName, Vector2 anchor, Vector2 position, Vector2? pivot = null)
        {
            var go = FindInScene(objectName);
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
