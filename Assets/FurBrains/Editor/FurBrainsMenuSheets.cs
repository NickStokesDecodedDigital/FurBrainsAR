using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static FurBrains.EditorTools.FurBrainsFeedingSetup; // FindInScene, FindFirstInScene

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Restyles the food and minigame menus as bottom sheets: a header (the need badge, title, subtitle and X), a grid
    /// of cards, and a footer line.
    /// <list type="bullet">
    /// <item>Tools → FurBrains → Restyle Food Menu As Sheet: one row of square food cards (big icon, coin + price),
    /// spread evenly across the screen, scrolling sideways when there are more than fit.</item>
    /// <item>Tools → FurBrains → Restyle Minigame Menu As Cards: two columns of coloured game cards (icon top-left,
    /// name bottom-left), scrolling down.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Both menus are copies of the template's menu, so one set of steps works for both; only the texts, the grid's
    /// shape and the card's insides differ. Each menu is edited in place (with Undo), so its scripts and wiring stay:
    /// the animator still opens and closes it, and FoodMenu / MinigameMenu on "Content" still clone "Button Template"
    /// once per item.
    /// <list type="bullet">
    /// <item><b>Content</b>: its HorizontalLayoutGroup becomes a GridLayoutGroup. At runtime IconButtonMenu sizes the
    /// cards to fill the screen's width, using the grid's Padding and Spacing (see its FitGrid).</item>
    /// <item><b>Button Template</b> becomes a rounded card (the minigame card also gets a "Name" text, which
    /// IconButtonMenu fills with each game's name). Its Button now highlights the card itself (the template pointed
    /// it at the icon).</item>
    /// <item>The panel gets the rounded sheet look (its bottom corners sit below the screen, like the Settings sheet)
    /// and the badge, title and X move into a header row. The badge keeps its look.</item>
    /// </list>
    /// Safe to run again: it repositions things and only creates the pieces that are missing.
    /// </remarks>
    public static class FurBrainsMenuSheets
    {
        const string k_RoundSprite = "Assets/FurBrains/UI/Shapes/Shape_Round.png";
        const string k_IconFolder = "Assets/FurBrains/UI/Icons/";

        // Sizes in canvas units (the canvas is about 580 wide on a portrait phone).
        const float k_Padding = 24f;
        const float k_Overhang = 40f;     // sheet runs this far below the screen, hiding its bottom corners
        const float k_HeaderHeight = 170f;
        const float k_FooterHeight = 70f;

        // Food: one row of square cards that fill the width (126 units each for four on a phone). The scroll area is
        // this tall so they fit; their size comes from the grid's Padding and Spacing at runtime.
        const float k_FoodRowHeight = 126f;
        const float k_FoodGap = 10f;

        // Minigames: two columns, rows of this height.
        const float k_GameCardHeight = 190f;
        const float k_GameGap = 16f;
        const int k_GameVisibleRows = 2;

        static readonly Color32 k_HandleColor = new(90, 94, 160, 255);
        static readonly Color32 k_MutedText = new(185, 188, 220, 255);
        static readonly Color32 k_FoodSubtitleColor = new(255, 170, 80, 255);
        static readonly Color32 k_GameSubtitleColor = new(110, 222, 200, 255);
        static readonly Color32 k_FoodCardColor = new(49, 52, 99, 255);
        static readonly Color32 k_GameTemplateCardColor = new(112, 48, 224, 255);

        // Starter look per game, by asset name: icon file and card colour (from the mock-up).
        static readonly (string asset, string icon, Color32 color)[] k_GameLooks =
        {
            ("BeachBall", "Icon_Game_BeachBall", new Color32(199, 73, 18, 255)),
            ("BubblePop", "Icon_Game_BubblePop", new Color32(45, 122, 110, 255)),
            ("CupShuffle", "Icon_Game_CupShuffle", new Color32(112, 48, 224, 255)),
            ("CopyZiara", "Icon_Game_CopyZiara", new Color32(188, 32, 104, 255)),
        };

        static Sprite s_Round;

        // ================= Food =================

        [MenuItem("Tools/FurBrains/Restyle Food Menu As Sheet")]
        static void RestyleFoodMenu()
        {
            var animator = FindFirstInScene("Food Menu Animator", "Object Menu Animator");
            var panel = animator != null ? FindChild(animator.transform, "Object Menu") : null;
            var menu = panel != null ? panel.GetComponentInChildren<FoodMenu>(true) : null;
            if (menu == null)
            {
                Debug.LogError("[FurBrains] Couldn't find Food Menu Animator → Object Menu with a FoodMenu inside.");
                return;
            }
            Begin("Restyle Food Menu As Sheet");

            var gridArea = k_FoodRowHeight;
            StyleSheet(panel, "Feed FurBrain", "Feed Ziara", "Drag a treat to Ziara", k_FoodSubtitleColor,
                "Let go on Ziara to feed her", gridArea);

            // One row, scrolling sideways. Content runs the scroll area's full height and grows to the right.
            var content = menu.transform;
            SetUpScrollArea(content, horizontal: true);
            var contentRect = (RectTransform)content;
            Undo.RecordObject(contentRect, "Restyle");
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var grid = UseGrid(content, GridLayoutGroup.Constraint.FixedRowCount, 1, k_FoodGap,
                new Vector2(k_FoodRowHeight, k_FoodRowHeight)); // square; resized at runtime to fill the width
            grid.startAxis = GridLayoutGroup.Axis.Vertical; // fill down the (single) row, then move right
            grid.childAlignment = TextAnchor.MiddleLeft;
            var fitter = GetOrAdd<ContentSizeFitter>(content);
            Undo.RecordObject(fitter, "Restyle");
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // The card: a big icon filling most of it, and IconButtonMenu's coin + price row along the bottom.
            // No name: the picture says what the food is.
            var template = content.GetComponentInChildren<Button>(true);
            if (template != null)
            {
                var card = StyleCardBase(template, k_FoodCardColor, 18f);
                Stretch(FindChild(card, "ObjectIcon"), new Vector2(0.12f, 0.26f), new Vector2(0.88f, 0.94f));
                var oldName = FindChild(card, "Name"); // added by an earlier run of this tool
                if (oldName != null)
                    Undo.DestroyObjectImmediate(oldName.gameObject);
            }

            var menuSo = new SerializedObject(menu);
            menuSo.FindProperty("m_MenuScale").floatValue = 1f;
            menuSo.FindProperty("m_BottomLabelRaise").floatValue = 0.05f;
            menuSo.FindProperty("m_BottomLabelSize").floatValue = 20f;
            menuSo.ApplyModifiedProperties();

            End(panel);
        }

        // ================= Minigames =================

        [MenuItem("Tools/FurBrains/Restyle Minigame Menu As Cards")]
        static void RestyleMinigameMenu()
        {
            var animator = FindInScene("Minigame Menu Animator");
            var panel = animator != null ? FindChild(animator.transform, "Object Menu") : null;
            var menu = panel != null ? panel.GetComponentInChildren<MinigameMenu>(true) : null;
            if (menu == null)
            {
                Debug.LogError("[FurBrains] Couldn't find Minigame Menu Animator → Object Menu with a MinigameMenu inside.");
                return;
            }
            Begin("Restyle Minigame Menu As Cards");

            var gridArea = k_GameVisibleRows * k_GameCardHeight + (k_GameVisibleRows - 1) * k_GameGap;
            StyleSheet(panel, "Play FurBrain", "Play with Ziara", "Every game fills the Play meter", k_GameSubtitleColor,
                "The meter rises while you play, not just at the end", gridArea);

            // Two columns, scrolling down. Content spans the scroll area's width and grows downward.
            var content = menu.transform;
            SetUpScrollArea(content, horizontal: false);
            var contentRect = (RectTransform)content;
            Undo.RecordObject(contentRect, "Restyle");
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var grid = UseGrid(content, GridLayoutGroup.Constraint.FixedColumnCount, 2, k_GameGap,
                new Vector2(259f, k_GameCardHeight)); // width resized at runtime to fill the scroll area
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            var fitter = GetOrAdd<ContentSizeFitter>(content);
            Undo.RecordObject(fitter, "Restyle");
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The card: icon top-left, name bottom-left. Each game's Card Color tints it at runtime.
            var template = content.GetComponentInChildren<Button>(true);
            if (template != null)
            {
                var card = StyleCardBase(template, k_GameTemplateCardColor, 22f);
                var icon = FindChild(card, "ObjectIcon");
                if (icon != null)
                {
                    var iconRect = (RectTransform)icon;
                    Undo.RecordObject(iconRect, "Restyle");
                    iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
                    iconRect.pivot = new Vector2(0f, 1f);
                    iconRect.anchoredPosition = new Vector2(22f, -22f);
                    iconRect.sizeDelta = new Vector2(64f, 64f);
                }
                var name = NameText(card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(22f, 20f),
                    new Vector2(-12f, 60f), TextAlignmentOptions.MidlineLeft);
                name.fontSize = 30f;
            }

            var menuSo = new SerializedObject(menu);
            menuSo.FindProperty("m_MenuScale").floatValue = 1f;
            menuSo.ApplyModifiedProperties();

            GiveGamesStarterLooks(menu);
            End(panel);
        }

        // ================= shared steps =================

        static void Begin(string undoName)
        {
            s_Round = AssetDatabase.LoadAssetAtPath<Sprite>(k_RoundSprite);
            Undo.SetCurrentGroupName(undoName);
        }

        static void End(Transform panel)
        {
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("[FurBrains] Restyled the menu. Ctrl+Z undoes it; save the scene to keep it.");
        }

        /// <summary>
        /// The sheet itself: rounded panel sized to fit header + grid area + footer, grab handle, the header row
        /// (badge left, title and subtitle, X right) and the footer line. The scroll view fills the gap between.
        /// </summary>
        static void StyleSheet(Transform panel, string titleObject, string title, string subtitle, Color subtitleColor,
            string footer, float gridAreaHeight)
        {
            var sheetHeight = k_HeaderHeight + gridAreaHeight + 16f + k_FooterHeight + k_Overhang;
            var panelRect = (RectTransform)panel;
            Undo.RecordObject(panelRect, "Restyle");
            panelRect.anchoredPosition = new Vector2(0f, -k_Overhang);
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, sheetHeight);
            if (panel.TryGetComponent<Image>(out var panelImage))
                MakeRounded(panelImage, 32f); // keeps its colour

            var handle = FindOrCreate(panel, "Handle");
            Place(handle, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(56f, 6f));
            MakeRounded(GetOrAdd<Image>(handle), 3f).color = k_HandleColor;

            // Header. The badge keeps its size and scale; its on-screen size is its width × scale.
            var badge = FindChild(panel, "Need Badge");
            var badgeRight = k_Padding;
            if (badge != null)
            {
                var shown = ((RectTransform)badge).rect.width * badge.localScale.x;
                Place(badge, new Vector2(0f, 1f), new Vector2(k_Padding + shown * 0.5f, -90f), null);
                badgeRight = k_Padding + shown;
            }
            var close = FindChild(panel, "Cancel Button");
            var closeWidth = 90f;
            if (close != null)
            {
                closeWidth = ((RectTransform)close).rect.width;
                Place(close, new Vector2(1f, 1f), new Vector2(-k_Padding - closeWidth * 0.5f, -90f), null);
            }

            var left = badgeRight + 18f;
            var right = k_Padding + closeWidth + 12f;
            var titleTransform = FindOrCreate(panel, titleObject);
            TopStrip(titleTransform, left, right, 48f, 44f);
            StyleText(GetOrAdd<TextMeshProUGUI>(titleTransform), title, 34f, Color.white, TextAlignmentOptions.MidlineLeft, true);

            var subtitleTransform = FindOrCreate(panel, "Subtitle");
            TopStrip(subtitleTransform, left, right, 94f, 30f);
            StyleText(GetOrAdd<TextMeshProUGUI>(subtitleTransform), subtitle, 21f, subtitleColor,
                TextAlignmentOptions.MidlineLeft, true);

            var footerTransform = FindOrCreate(panel, "Footer");
            var footerRect = (RectTransform)footerTransform;
            Undo.RecordObject(footerRect, "Restyle");
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.offsetMin = new Vector2(k_Padding, k_Overhang + 16f);
            footerRect.offsetMax = new Vector2(-k_Padding, k_Overhang + 16f + 40f);
            var footerText = GetOrAdd<TextMeshProUGUI>(footerTransform);
            StyleText(footerText, footer, 20f, k_MutedText, TextAlignmentOptions.Center, false);
            footerText.fontStyle = FontStyles.Normal;
        }

        /// <summary>The scroll view fills the space between header and footer; the viewport fills the scroll view.</summary>
        static void SetUpScrollArea(Transform content, bool horizontal)
        {
            var viewport = content.parent;
            var scrollView = viewport != null ? viewport.parent : null;
            if (scrollView == null)
                return;

            var scrollRect = (RectTransform)scrollView;
            Undo.RecordObject(scrollRect, "Restyle");
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.pivot = new Vector2(0.5f, 1f);
            scrollRect.offsetMin = new Vector2(k_Padding, k_Overhang + k_FooterHeight);
            scrollRect.offsetMax = new Vector2(-k_Padding, -k_HeaderHeight);
            if (scrollView.TryGetComponent<ScrollRect>(out var scroll))
            {
                Undo.RecordObject(scroll, "Restyle");
                scroll.horizontal = horizontal;
                scroll.vertical = !horizontal;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }

            var viewportRect = (RectTransform)viewport;
            Undo.RecordObject(viewportRect, "Restyle");
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
        }

        /// <summary>Swaps Content's row layout for a grid. A GameObject can only have one layout group.</summary>
        static GridLayoutGroup UseGrid(Transform content, GridLayoutGroup.Constraint constraint, int count, float gap,
            Vector2 cellSize)
        {
            if (content.TryGetComponent<HorizontalLayoutGroup>(out var rowLayout))
                Undo.DestroyObjectImmediate(rowLayout);
            var grid = GetOrAdd<GridLayoutGroup>(content);
            Undo.RecordObject(grid, "Restyle");
            grid.constraint = constraint;
            grid.constraintCount = count;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.spacing = new Vector2(gap, gap);
            grid.cellSize = cellSize;
            grid.padding = new RectOffset();
            return grid;
        }

        /// <summary>
        /// What both cards share: rounded background, the Button highlighting the card itself (the template pointed
        /// it at the icon), a white icon, and no selection box.
        /// </summary>
        static Transform StyleCardBase(Button template, Color color, float radius)
        {
            var card = template.transform;
            if (card.TryGetComponent<Image>(out var image))
            {
                MakeRounded(image, radius).color = color;
                Undo.RecordObject(template, "Restyle");
                template.targetGraphic = image;
            }
            var icon = FindChild(card, "ObjectIcon");
            if (icon != null && icon.TryGetComponent<Image>(out var iconImage))
            {
                Undo.RecordObject(iconImage, "Restyle");
                iconImage.preserveAspect = true;
                iconImage.color = Color.white;
            }
            var selection = FindChild(card, "SelectionBox");
            if (selection != null)
            {
                Undo.RecordObject(selection.gameObject, "Restyle");
                selection.gameObject.SetActive(false);
            }
            return card;
        }

        /// <summary>The card's "Name" text (IconButtonMenu fills it with each item's name), placed by anchors + offsets.</summary>
        static TextMeshProUGUI NameText(Transform card, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin,
            Vector2 offsetMax, TextAlignmentOptions alignment)
        {
            var name = FindOrCreate(card, "Name");
            var rect = (RectTransform)name;
            Undo.RecordObject(rect, "Restyle");
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 0f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var text = GetOrAdd<TextMeshProUGUI>(name);
            StyleText(text, "Name", 24f, Color.white, alignment, true);
            text.overflowMode = TextOverflowModes.Ellipsis; // a long name ends in "…" rather than spilling out
            return text;
        }

        static void GiveGamesStarterLooks(MinigameMenu menu)
        {
            foreach (var game in menu.games)
            {
                if (game == null)
                    continue;
                foreach (var look in k_GameLooks)
                {
                    if (game.name != look.asset)
                        continue;
                    var so = new SerializedObject(game); // records Undo and marks the asset to be saved
                    var icon = so.FindProperty("m_Icon");
                    if (icon.objectReferenceValue == null)
                        icon.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(k_IconFolder + look.icon + ".png");
                    var color = so.FindProperty("m_CardColor");
                    if (color.colorValue.a <= 0f)
                        color.colorValue = look.color;
                    so.ApplyModifiedProperties();
                }
            }
            AssetDatabase.SaveAssets();
        }

        // ================= helpers =================

        static Image MakeRounded(Image image, float radius)
        {
            Undo.RecordObject(image, "Restyle");
            image.sprite = s_Round;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 32f / radius;
            return image;
        }

        static void StyleText(TextMeshProUGUI text, string value, float size, Color color, TextAlignmentOptions alignment,
            bool oneLine)
        {
            Undo.RecordObject(text, "Restyle");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = oneLine ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.raycastTarget = false;
        }

        // A strip along the top of the parent: left/right insets, distance from the top, height.
        static void TopStrip(Transform t, float left, float right, float top, float height)
        {
            var rect = (RectTransform)t;
            Undo.RecordObject(rect, "Restyle");
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        // Fill a fraction of the parent (anchors as fractions), with no extra offsets.
        static void Stretch(Transform t, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (t == null)
                return;
            var rect = (RectTransform)t;
            Undo.RecordObject(rect, "Restyle");
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void Place(Transform t, Vector2 anchor, Vector2 position, Vector2? size)
        {
            var rect = (RectTransform)t;
            Undo.RecordObject(rect, "Restyle");
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            if (size.HasValue)
                rect.sizeDelta = size.Value;
        }

        static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);
            return null;
        }

        static Transform FindOrCreate(Transform parent, string name)
        {
            var found = FindChild(parent, name);
            if (found != null)
                return found;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Restyle");
            return go.transform;
        }

        static T GetOrAdd<T>(Transform t) where T : Component =>
            t.TryGetComponent<T>(out var existing) ? existing : Undo.AddComponent<T>(t.gameObject);
    }
}
