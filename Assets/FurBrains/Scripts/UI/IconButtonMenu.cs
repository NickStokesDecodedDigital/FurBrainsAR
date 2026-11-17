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

        /// <summary>One built button and when it can be used.</summary>
        struct Entry
        {
            public Button Button;
            public Graphic[] Graphics;
            public Func<bool> CanUse;
        }

        readonly List<Entry> m_Entries = new();
        float m_FittedWidth = -1f; // parent width the panel was last fitted to

        protected virtual void Start()
        {
            ApplyScale();
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
            // The item's name, if there's no icon yet: above the bottom label, or centred when there isn't one.
            if (icon == null)
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
            const float fontSize = 34f;
            var anchorMin = new Vector2(0f, 0f);
            var anchorMax = new Vector2(1f, 0.15f);

            if (m_LabelIcon == null)
            {
                AddLabel(button, "Label", text, fontSize, anchorMin, anchorMax);
                return;
            }

            var row = new GameObject("Label", typeof(RectTransform));
            row.transform.SetParent(button, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = anchorMin;
            rowRect.anchorMax = anchorMax;
            rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;

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
