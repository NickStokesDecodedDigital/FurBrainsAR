using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Shared base for the template-style icon menus (food, minigames): builds one button per item by cloning a template
    /// button, labels it, greys it out when it can't be used, and scales the whole menu panel.
    /// </summary>
    /// <remarks>
    /// Sits on a menu's scroll-view "Content" object, whose HorizontalLayoutGroup lays the buttons out in child order.
    /// At start-up it hides the existing children (the template's shape buttons) and clones one of them per item, so new
    /// buttons keep the template's look. Each clone gets a fresh onClick event, dropping the scene-wired listeners it
    /// copied (the template's SetObjectToSpawn).
    ///
    /// Subclasses only say <i>what</i> to show: they implement <see cref="BuildItems"/> and call
    /// <see cref="AddButton"/> for each item. This is the template-method pattern: the base class owns the fixed steps
    /// and calls the subclass's override (a C# <c>abstract</c> method, like a C++ pure virtual) for the variable part.
    /// </remarks>
    public abstract class IconButtonMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Button to copy for each item. If empty, the first Button under this object is used.")]
        Button m_ButtonTemplate;

        [SerializeField]
        [Tooltip("The menu panel to resize. If empty, the ancestor named \"Object Menu\" is used.")]
        RectTransform m_MenuPanel;

        [SerializeField, Range(0.3f, 1f)]
        [Tooltip("Size of the whole menu (panel, buttons and cancel button) relative to the template's design.")]
        float m_MenuScale = 0.9f;

        [SerializeField]
        [Tooltip("Small icon shown just left of each button's bottom label, e.g. a coin beside the food prices. " +
                 "Empty = text only.")]
        Sprite m_LabelIcon;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("Raises each button's bottom label (e.g. the price), as a fraction of the button's height. " +
                 "0 = along the bottom edge.")]
        float m_BottomLabelRaise = 0f;

        [SerializeField, Min(8f)]
        [Tooltip("Font size of each button's bottom label (e.g. the price); the label icon scales with it.")]
        float m_BottomLabelSize = 34f;

        /// <summary>One built button and when it can be used.</summary>
        struct Entry
        {
            public Button Button;
            public Graphic[] Graphics;
            public Func<bool> CanUse;
        }

        readonly List<Entry> m_Entries = new();
        float m_FittedWidth = -1f; // parent width the panel was last fitted to
        GridLayoutGroup m_Grid;    // optional: lays the buttons out in rows and columns (the minigame cards)
        Vector2 m_GridFit = -Vector2.one;  // scroll area width and button count the cells were last sized for

        protected virtual void Start()
        {
            ApplyScale();
            m_Grid = GetComponent<GridLayoutGroup>();
            if (m_ButtonTemplate == null)
                m_ButtonTemplate = GetComponentInChildren<Button>(true);
            if (m_ButtonTemplate == null)
            {
                Debug.LogWarning($"[FurBrains] {GetType().Name} has no button to copy.", this);
                return;
            }

            // Hide the template's own buttons. Inactive children are ignored by the layout group.
            foreach (Transform child in transform)
                child.gameObject.SetActive(false);

            BuildItems();
        }

        /// <summary>Add one button per item with <see cref="AddButton"/>, in the order they should appear.</summary>
        protected abstract void BuildItems();

        /// <summary>
        /// Adds a button. <paramref name="onClick"/> runs when it's tapped; <paramref name="canUse"/> is checked every
        /// frame to grey it out. <paramref name="bottomLabel"/> (e.g. a price) is optional. Returns the new button,
        /// so a subclass can add more to it (FoodMenu adds drag-to-feed).
        /// </summary>
        protected Button AddButton(string objectName, Sprite icon, string displayName, string bottomLabel,
            Action onClick, Func<bool> canUse)
        {
            var button = Instantiate(m_ButtonTemplate, transform);
            button.gameObject.SetActive(true);
            button.name = objectName;

            // A new, empty event drops the persistent (scene-wired) listeners the clone copied from the template.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());

            var selectionBox = button.transform.Find("SelectionBox");
            if (selectionBox != null)
                selectionBox.gameObject.SetActive(false);

            var iconTransform = button.transform.Find("ObjectIcon");
            var image = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            if (image != null)
            {
                image.sprite = icon;
                image.enabled = icon != null;
            }

            var hasBottom = !string.IsNullOrEmpty(bottomLabel);
            if (hasBottom)
                AddBottomLabel(button.transform, bottomLabel);

            // If the template button has a text child called "Name" (the minigame cards do), it shows the item's
            // name wherever it was placed in the Inspector. Otherwise the name is only added when there's no icon:
            // above the bottom label, or centred when there isn't one.
            var nameTransform = button.transform.Find("Name");
            var nameText = nameTransform != null ? nameTransform.GetComponent<TMP_Text>() : null;
            if (nameText != null)
                nameText.text = displayName;
            else if (icon == null)
                AddLabel(button.transform, "Name", displayName, 28f, new Vector2(0f, hasBottom ? 0.35f : 0f), Vector2.one);

            m_Entries.Add(new Entry
            {
                Button = button,
                Graphics = button.GetComponentsInChildren<Graphic>(true),
                CanUse = canUse,
            });
            return button;
        }

        protected virtual void Update()
        {
            // Re-fit the panel if the screen width changed (e.g. the phone was rotated).
            if (m_MenuPanel != null && m_MenuPanel.parent is RectTransform parent && parent.rect.width != m_FittedWidth)
                ApplyScale();
            FitGrid();

            // A handful of buttons, so simply refresh every frame rather than tracking each reason separately.
            foreach (var entry in m_Entries)
            {
                var usable = entry.CanUse == null || entry.CanUse();
                if (entry.Button.interactable == usable)
                    continue;
                entry.Button.interactable = usable;
                foreach (var graphic in entry.Graphics)
                {
                    var c = graphic.color;
                    c.a = usable ? 1f : 0.4f;
                    graphic.color = c;
                }
            }
        }

        protected virtual void OnValidate()
        {
            // Lets you drag the Menu Scale slider in Play mode and see the result.
            if (Application.isPlaying)
                ApplyScale();
        }

        /// <summary>
        /// Sizes the whole menu panel with its scale rather than resizing every element: everything inside keeps
        /// its proportions. The panel's pivot is at its bottom centre, so it shrinks toward the bottom of the screen.
        /// (The menu's open/close animation only moves the panel up and down, so it doesn't fight this.)
        /// Scaling would also narrow the panel, so it's made wider by the same factor to keep its border at the
        /// screen's left and right edges.
        /// </summary>
        void ApplyScale()
        {
            if (m_MenuPanel == null)
            {
                foreach (var parent in GetComponentsInParent<RectTransform>(true))
                {
                    if (parent.name == "Object Menu")
                    {
                        m_MenuPanel = parent;
                        break;
                    }
                }
            }
            if (m_MenuPanel == null)
                return;

            m_MenuPanel.localScale = new Vector3(m_MenuScale, m_MenuScale, 1f);

            // The panel's anchors stretch it across its parent (the full screen width), and sizeDelta.x is extra width
            // on top of that. Scaled by s, the panel shows as s × (parentWidth + extra); for that to equal parentWidth,
            // extra = parentWidth × (1/s − 1). E.g. at 0.9 the panel is ~11% wider before scaling, so it fits exactly.
            if (m_MenuPanel.parent is RectTransform container)
            {
                m_FittedWidth = container.rect.width;
                var extra = m_FittedWidth * (1f / m_MenuScale - 1f);
                m_MenuPanel.sizeDelta = new Vector2(extra, m_MenuPanel.sizeDelta.y);
            }
        }

        /// <summary>
        /// Sizes the buttons of an optional GridLayoutGroup on this object so they fill the scroll area's width. The
        /// size comes from the grid's <b>Padding</b> and <b>Spacing</b> (set in the Inspector): whatever width is left
        /// after them is shared equally: (width − left/right padding − gaps) ÷ buttons across. A grid only takes a
        /// fixed cell size and phones differ in width, so this does that sum for it.
        /// <list type="bullet">
        /// <item>Fixed Column Count (the minigame cards, 2 columns): the columns fill the width; the cell height stays
        /// as set in the Inspector.</item>
        /// <item>Fixed Row Count (the food cards, 1 row): all the cards in the row fill the width and are square, so
        /// more padding or spacing makes them smaller (and more foods would make them smaller too).</item>
        /// </list>
        /// The scroll area's height doesn't affect the size. Runs again when the width or the number of buttons changes.
        /// </summary>
        void FitGrid()
        {
            if (m_Grid == null || m_Grid.constraint == GridLayoutGroup.Constraint.Flexible)
                return;
            if (transform.parent is not RectTransform area) // the scroll view's Viewport
                return;
            var width = area.rect.width;
            var items = m_Entries.Count;
            var fit = new Vector2(width, items);
            if (fit == m_GridFit || width <= 0f || items == 0)
                return;
            m_GridFit = fit;

            var count = Mathf.Max(1, m_Grid.constraintCount);
            if (m_Grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            {
                var cell = (width - m_Grid.padding.horizontal - m_Grid.spacing.x * (count - 1)) / count;
                m_Grid.cellSize = new Vector2(cell, m_Grid.cellSize.y);
            }
            else
            {
                var across = Mathf.CeilToInt(items / (float)count); // cards side by side in a row
                var side = (width - m_Grid.padding.horizontal - m_Grid.spacing.x * (across - 1)) / across;
                m_Grid.cellSize = new Vector2(side, side);
            }
        }

        /// <summary>
        /// The bottom label (e.g. a price), with <see cref="m_LabelIcon"/> just to its left when one is set.
        /// </summary>
        /// <remarks>
        /// Without an icon it's a plain centred text. With one, a "Label" row holds the icon and the text side by side:
        /// a HorizontalLayoutGroup lines its children up left to right and centres the pair, so "[coin] 5" and
        /// "[coin] 25" both stay centred whatever the number's width. The text reports its own width to the layout
        /// group (TextMeshPro does that automatically); the icon's size comes from its LayoutElement.
        /// </remarks>
        void AddBottomLabel(Transform button, string text)
        {
            var fontSize = m_BottomLabelSize;

            // The label is a strip across the button, m_BottomLabelRaise (a fraction of the button's height) up from
            // the bottom. Its height follows the font size, so a bigger label gets a taller strip: the row's layout
            // group never makes the coin taller than the strip it sits in.
            var stripHeight = fontSize * 1.2f;
            RectTransform strip;
            if (m_LabelIcon == null)
            {
                strip = AddLabel(button, "Label", text, fontSize, Vector2.zero, Vector2.one).rectTransform;
                PlaceStrip(strip, stripHeight);
                return;
            }

            var row = new GameObject("Label", typeof(RectTransform));
            row.transform.SetParent(button, false);
            strip = (RectTransform)row.transform;
            PlaceStrip(strip, stripHeight);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 4f;
            layout.childControlWidth = true;   // children get their preferred widths...
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false; // ...and aren't stretched to fill the row
            layout.childForceExpandHeight = false;

            // The coin: about the height of the digits.
            var icon = new GameObject("Label Icon", typeof(RectTransform));
            icon.transform.SetParent(row.transform, false);
            var image = icon.AddComponent<Image>();
            image.sprite = m_LabelIcon;
            image.preserveAspect = true;
            image.raycastTarget = false; // taps go to the button, not the coin
            var iconSize = icon.AddComponent<LayoutElement>();
            iconSize.preferredWidth = iconSize.preferredHeight = fontSize * 0.9f;

            var label = AddLabel(row.transform, "Label Text", text, fontSize, Vector2.zero, Vector2.one);
            label.textWrappingMode = TextWrappingModes.NoWrap; // keep the number on one line so it reports its real width
        }

        // Anchors the strip to the full width at m_BottomLabelRaise up the button, growing upward to the given height.
        void PlaceStrip(RectTransform strip, float height)
        {
            strip.anchorMin = new Vector2(0f, m_BottomLabelRaise);
            strip.anchorMax = new Vector2(1f, m_BottomLabelRaise);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = Vector2.zero;
            strip.sizeDelta = new Vector2(0f, height);
        }

        static TextMeshProUGUI AddLabel(Transform parent, string name, string text, float size, Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }
    }
}
