using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Templates.AR;
using static FurBrains.EditorTools.FurBrainsFeedingSetup; // FindInScene

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Tools → FurBrains → Build Settings Panel In Open Scene: builds the Settings sheet (sound and floor-dot
    /// switches, Move Ziara / Scan again, a hold-to-exit box for grown-ups) and makes the Options (⋯) button open it.
    /// </summary>
    /// <remarks>
    /// <b>Layout groups:</b> instead of placing every object by hand, the sheet stacks its children with a
    /// VerticalLayoutGroup (top to bottom, each at its LayoutElement's preferred height) and a ContentSizeFitter
    /// sizes the sheet to fit them. Rows and buttons do the same sideways with HorizontalLayoutGroups. Adding a row
    /// later is just adding a child with a LayoutElement: everything below moves down by itself.
    ///
    /// <b>Rounded corners:</b> every rounded box is the same small circle sprite (UI/Shapes/Shape_Round.png) drawn
    /// Sliced; the Image's Pixels Per Unit Multiplier sets the corner radius (radius = 32 / multiplier).
    ///
    /// It only builds the panel if there isn't one yet, so your Inspector changes are never overwritten. To rebuild
    /// from scratch, delete "Settings Panel" and run it again. The old template "Options Modal" is left in place
    /// (switched off) because the menu manager still uses its floor-dots switch internally.
    /// Sizes are canvas units (the UI canvas is about 580 units wide on a portrait phone); colours match the mock-up.
    /// </remarks>
    public static class FurBrainsSettingsBuilder
    {
        const string k_PanelName = "Settings Panel";
        const string k_RoundSprite = "Assets/FurBrains/UI/Shapes/Shape_Round.png";
        const string k_IconFolder = "Assets/FurBrains/UI/Icons/";

        // Colours from the mock-up.
        static readonly Color32 k_SheetColor = new(37, 39, 70, 255);
        static readonly Color32 k_CardColor = new(49, 52, 99, 255);
        static readonly Color32 k_DividerColor = new(62, 65, 118, 255);
        static readonly Color32 k_HandleColor = new(90, 94, 160, 255);
        static readonly Color32 k_OutlineColor = new(72, 76, 135, 255);
        static readonly Color32 k_MutedText = new(185, 188, 220, 255);
        static readonly Color32 k_DarkText = new(30, 32, 55, 255);
        static readonly Color32 k_HoldFillColor = new(78, 201, 176, 110);

        // Sizes (canvas units).
        const float k_Padding = 24f;     // sheet edges
        const float k_Spacing = 18f;     // between the sheet's sections
        const float k_Overhang = 40f;    // the sheet runs this far below the screen, hiding its bottom corners
        const float k_RowHeight = 87f;
        const float k_ButtonHeight = 80f;
        const float k_TextSize = 26f;

        static Sprite s_Round;

        [MenuItem("Tools/FurBrains/Build Settings Panel In Open Scene")]
        static void Build()
        {
            var menuManager = Object.FindAnyObjectByType<ARTemplateMenuManager>(FindObjectsInactive.Include);
            var oldModal = FindInScene("Options Modal");
            if (menuManager == null || oldModal == null)
            {
                Debug.LogError("[FurBrains] Couldn't find the ARTemplateMenuManager and the \"Options Modal\" in the open scene.");
                return;
            }
            if (FindInScene(k_PanelName) != null)
            {
                Debug.LogWarning($"[FurBrains] \"{k_PanelName}\" already exists. Delete it first to rebuild it.");
                return;
            }
            s_Round = AssetDatabase.LoadAssetAtPath<Sprite>(k_RoundSprite);
            if (s_Round == null || s_Round.border == Vector4.zero)
                Debug.LogWarning($"[FurBrains] {k_RoundSprite} is missing or has no 9-slice border; corners won't be round. " +
                                 "Right-click it → Reimport, then rebuild.");

            // The panel: a full-screen container (no Image, so taps outside the sheet still reach the AR view and close
            // it, like the old Options menu) placed just after the old one, so it draws above Main.
            var canvas = oldModal.transform.parent;
            var root = NewUi(k_PanelName, canvas);
            root.transform.SetSiblingIndex(oldModal.transform.GetSiblingIndex() + 1);
            Stretch(root);

            // The sheet: anchored to the bottom, as wide as the screen, as tall as its contents.
            var sheet = NewUi("Sheet", root.transform);
            var sheetRect = (RectTransform)sheet.transform;
            sheetRect.anchorMin = new Vector2(0f, 0f);
            sheetRect.anchorMax = new Vector2(1f, 0f);
            sheetRect.pivot = new Vector2(0.5f, 0f);
            sheetRect.anchoredPosition = new Vector2(0f, -k_Overhang);
            sheetRect.sizeDelta = Vector2.zero;
            Rounded(sheet, k_SheetColor, 32f);
            var column = Column(sheet, new RectOffset((int)k_Padding, (int)k_Padding, 14, (int)(k_Padding + k_Overhang)), k_Spacing);
            column.childAlignment = TextAnchor.UpperCenter;
            sheet.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Grab handle (decoration).
            var handleRow = NewUi("Handle", sheet.transform);
            Height(handleRow, 6f);
            var handle = NewUi("Bar", handleRow.transform);
            Place(handle, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 6f));
            Rounded(handle, k_HandleColor, 3f);

            // Header: title + round X.
            var header = NewUi("Header", sheet.transform);
            Height(header, 63f);
            var title = Text(header.transform, "Title", "Settings", 32f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(title.gameObject, right: 80f);
            var close = NewUi("Close Button", header.transform);
            Place(close, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(63f, 63f), pivot: new Vector2(1f, 0.5f));
            Rounded(close, k_CardColor, 31.5f);
            var closeButton = close.AddComponent<Button>();
            Icon(close.transform, "Icon_Close_X", 24f, Vector2.zero, new Vector2(0.5f, 0.5f));

            // Card of switch rows.
            var card = NewUi("Switches", sheet.transform);
            Rounded(card, k_CardColor, 16f);
            Column(card, new RectOffset(), 0f);
            var sound = SwitchRow(card.transform, "Sound Effects Row", "Icon_Sound", "Sound effects");
            var divider = NewUi("Divider", card.transform);
            Height(divider, 2f);
            AddImage(divider, k_DividerColor);
            var floor = SwitchRow(card.transform, "Floor Dots Row", "Icon_FloorDots", "Show floor dots");

            // Move / Scan buttons side by side.
            var buttons = NewUi("Buttons", sheet.transform);
            Height(buttons, k_ButtonHeight);
            var row = buttons.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            var move = IconTextButton(buttons.transform, "Move Button", "Icon_Plus", "Move Ziara");
            var scan = IconTextButton(buttons.transform, "Scan Button", "Icon_Retry_Refresh", "Scan again");

            // Grown-ups box: an outline (a lighter rounded box with the sheet colour inset inside it).
            var outline = NewUi("Grown-ups", sheet.transform);
            Rounded(outline, k_OutlineColor, 20f);
            Column(outline, new RectOffset(3, 3, 3, 3), 0f);
            var inner = NewUi("Inner", outline.transform);
            Rounded(inner, k_SheetColor, 18f);
            var innerColumn = Column(inner, new RectOffset(20, 20, 18, 18), 14f);
            innerColumn.childAlignment = TextAnchor.UpperCenter;

            var label = NewUi("Label", inner.transform);
            Height(label, 24f);
            var labelRow = label.AddComponent<HorizontalLayoutGroup>();
            labelRow.childAlignment = TextAnchor.MiddleLeft;
            labelRow.spacing = 10f;
            labelRow.childControlWidth = labelRow.childControlHeight = true;
            labelRow.childForceExpandWidth = labelRow.childForceExpandHeight = false;
            var lockIcon = Icon(label.transform, "Icon_Lock", 20f, Vector2.zero, new Vector2(0.5f, 0.5f));
            lockIcon.color = k_MutedText;
            Size(lockIcon.gameObject, 20f, 20f);
            var labelText = Text(label.transform, "Text", "FOR GROWN-UPS", 19f, k_MutedText, TextAlignmentOptions.MidlineLeft);
            labelText.characterSpacing = 4f;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;

            var exit = NewUi("Exit Hold Button", inner.transform);
            Height(exit, 76f);
            Rounded(exit, Color.white, 20f);
            var hold = exit.AddComponent<HoldButton>();
            var fill = NewUi("Fill", exit.transform);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = new Vector2(76f, 0f);
            Rounded(fill, k_HoldFillColor, 20f).raycastTarget = false;
            fill.SetActive(false);
            var exitText = Text(exit.transform, "Text", "Hold 3 seconds to exit to app", 25f, k_DarkText, TextAlignmentOptions.Center);
            Stretch(exitText.gameObject);
            SetRef(hold, "m_Fill", fillRect);

            var caption = Text(inner.transform, "Caption", "Play-time limits and purchases live in the parent area",
                19f, k_MutedText, TextAlignmentOptions.Center);
            caption.fontStyle = FontStyles.Normal; // no LayoutElement: the text reports its own height, so it can wrap

            // Wire the panel script.
            var panel = root.AddComponent<SettingsPanel>();
            SetRef(panel, "m_CloseButton", closeButton);
            SetRef(panel, "m_SoundToggle", sound);
            SetRef(panel, "m_FloorDotsToggle", floor);
            SetRef(panel, "m_MoveButton", move);
            SetRef(panel, "m_ScanButton", scan);
            SetRef(panel, "m_ExitHoldButton", hold);
            SetRef(panel, "m_MenuManager", menuManager);

            root.SetActive(false); // opened by the Options button
            Undo.RegisterCreatedObjectUndo(root, "Build Settings Panel");

            // The Options button calls the menu manager's ShowHideModal, which opens its "Modal Menu": point it here.
            var so = new SerializedObject(menuManager);
            so.FindProperty("m_ModalMenu").objectReferenceValue = root;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[FurBrains] Built the Settings Panel and connected it to the Options button. Save the scene to keep it.");
        }

        // ---------- pieces ----------

        /// <summary>A row with an icon, a label and a sliding switch. The Toggle covers the whole row.</summary>
        static Toggle SwitchRow(Transform parent, string name, string icon, string text)
        {
            var row = NewUi(name, parent);
            Height(row, k_RowHeight);
            AddImage(row, new Color(0f, 0f, 0f, 0f)); // invisible, but catches taps for the Toggle

            Icon(row.transform, icon, 30f, new Vector2(k_Padding + 15f, 0f), new Vector2(0f, 0.5f));
            var label = Text(row.transform, "Label", text, k_TextSize, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(label.gameObject, left: k_Padding + 30f + 18f, right: k_Padding + 80f + 10f);

            var track = NewUi("Switch", row.transform);
            Place(track, new Vector2(1f, 0.5f), new Vector2(-k_Padding, 0f), new Vector2(80f, 48f), pivot: new Vector2(1f, 0.5f));
            var trackImage = Rounded(track, Color.white, 24f);
            trackImage.raycastTarget = false;
            var knob = NewUi("Knob", track.transform);
            Place(knob, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            var knobImage = AddImage(knob, Color.white);
            knobImage.sprite = s_Round;
            knobImage.raycastTarget = false;

            var toggle = row.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.graphic = null; // no checkmark: SwitchVisual draws the state
            var visual = row.AddComponent<SwitchVisual>();
            SetRef(visual, "m_Toggle", toggle);
            SetRef(visual, "m_Track", trackImage);
            SetRef(visual, "m_Knob", (RectTransform)knob.transform);
            return toggle;
        }

        /// <summary>A card-coloured button with an icon and a label, centred together.</summary>
        static Button IconTextButton(Transform parent, string name, string icon, string text)
        {
            var go = NewUi(name, parent);
            var image = Rounded(go, k_CardColor, 16f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var content = NewUi("Content", go.transform);
            Stretch(content);
            var row = content.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 10f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var iconImage = Icon(content.transform, icon, 28f, Vector2.zero, new Vector2(0.5f, 0.5f));
            Size(iconImage.gameObject, 28f, 28f);
            var label = Text(content.transform, "Label", text, k_TextSize, Color.white, TextAlignmentOptions.Midline);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        // ---------- helpers ----------

        static GameObject NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        static Image AddImage(GameObject go, Color color)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>A rounded box: the circle sprite drawn Sliced, with the given corner radius.</summary>
        static Image Rounded(GameObject go, Color color, float radius)
        {
            var image = AddImage(go, color);
            image.sprite = s_Round;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 32f / radius;
            return image;
        }

        static Image Icon(Transform parent, string file, float size, Vector2 position, Vector2 anchor)
        {
            var go = NewUi("Icon", parent);
            Place(go, anchor, position, new Vector2(size, size));
            var image = AddImage(go, Color.white);
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(k_IconFolder + file + ".png");
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (image.sprite == null)
                Debug.LogWarning($"[FurBrains] Icon {file}.png not found in {k_IconFolder}.");
            return image;
        }

        static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment)
        {
            var go = NewUi(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        static VerticalLayoutGroup Column(GameObject go, RectOffset padding, float spacing)
        {
            var column = go.AddComponent<VerticalLayoutGroup>();
            column.padding = padding;
            column.spacing = spacing;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            return column;
        }

        // Fixed height inside a layout group (the group sets the width).
        static void Height(GameObject go, float height) => go.AddComponent<LayoutElement>().preferredHeight = height;

        static void Size(GameObject go, float width, float height)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
        }

        static void Place(GameObject go, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // Fill the parent, with optional insets from its left and right edges.
        static void Stretch(GameObject go, float left = 0f, float right = 0f)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        // Private [SerializeField]s are set the way the Inspector does it.
        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
