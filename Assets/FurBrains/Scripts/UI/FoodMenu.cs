using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Templates.AR;

namespace FurBrains
{
    /// <summary>
    /// Turns the template's object menu (opened by the create button) into a food menu. One button per FoodItem,
    /// cheapest on the left. Tapping a food pays for it and feeds the FurBrain; the menu stays open (optional).
    /// </summary>
    /// <remarks>
    /// Sits on the menu's scroll-view "Content" object, whose HorizontalLayoutGroup lays the buttons out in child order.
    /// At start-up it hides the template's shape buttons and clones one of them per food, so the food buttons keep the
    /// template's look. The template's button onClick events (wired in the scene to SetObjectToSpawn) are replaced with
    /// a fresh event on each clone, so they no longer change what gets spawned.
    ///
    /// A food button is greyed out when it can't be used: no FurBrain summoned, not enough coins, or already full
    /// (so a child never spends coins for nothing).
    /// </remarks>
    public class FoodMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Foods to offer. Order doesn't matter: they're shown cheapest first.")]
        List<FoodItem> m_Foods = new();

        [SerializeField]
        [Tooltip("Button to copy for each food. If empty, the first Button under this object is used.")]
        Button m_ButtonTemplate;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        PlayerWallet m_Wallet;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Used to find the summoned FurBrain. Found in the scene automatically if left empty.")]
        FurBrainSummoner m_Summoner;

        [SerializeField]
        [Tooltip("Used to close the menu after feeding. Found in the scene automatically if left empty.")]
        ARTemplateMenuManager m_MenuManager;

        [SerializeField]
        [Tooltip("Close the menu after buying a food. Off: the menu stays open so several foods can be bought in a row.")]
        bool m_CloseAfterBuying;

        [SerializeField]
        [Tooltip("The menu panel to resize. If empty, the ancestor named \"Object Menu\" is used.")]
        RectTransform m_MenuPanel;

        [SerializeField, Range(0.3f, 1f)]
        [Tooltip("Size of the whole menu (panel, food buttons and cancel button) relative to the template's design.")]
        float m_MenuScale = 0.6f;

        /// <summary>One built button and what it sells.</summary>
        struct Entry
        {
            public FoodItem Food;
            public Button Button;
            public Graphic[] Graphics;
        }

        readonly List<Entry> m_Entries = new();

        /// <summary>Foods on offer.</summary>
        public List<FoodItem> foods => m_Foods;

        void Awake()
        {
            if (m_Wallet == null) m_Wallet = FindAnyObjectByType<PlayerWallet>();
            if (m_Needs == null) m_Needs = FindAnyObjectByType<FurBrainNeeds>();
            if (m_Summoner == null) m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_MenuManager == null) m_MenuManager = FindAnyObjectByType<ARTemplateMenuManager>(FindObjectsInactive.Include);
        }

        void Start()
        {
            ApplyScale();
            BuildButtons();
        }

        void OnValidate()
        {
            // Lets you drag the Menu Scale slider in Play mode and see the result.
            if (Application.isPlaying)
                ApplyScale();
        }

        /// <summary>
        /// Shrinks the whole menu panel with its scale rather than resizing every element: everything inside keeps
        /// its proportions. The panel's pivot is at its bottom centre, so it shrinks toward the bottom of the screen.
        /// (The menu's open/close animation only moves the panel up and down, so it doesn't fight this.)
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
            if (m_MenuPanel != null)
                m_MenuPanel.localScale = new Vector3(m_MenuScale, m_MenuScale, 1f);
        }

        void BuildButtons()
        {
            if (m_ButtonTemplate == null)
                m_ButtonTemplate = GetComponentInChildren<Button>(true);
            if (m_ButtonTemplate == null)
            {
                Debug.LogWarning("[FurBrains] FoodMenu has no button to copy.", this);
                return;
            }

            // Hide the template's own buttons (shapes). Inactive children are ignored by the layout group.
            foreach (Transform child in transform)
                child.gameObject.SetActive(false);

            foreach (var food in m_Foods.Where(f => f != null).OrderBy(f => f.price))
            {
                var button = Instantiate(m_ButtonTemplate, transform);
                button.gameObject.SetActive(true);
                button.name = $"Food ({food.name})";

                // A new, empty event drops the persistent (scene-wired) listeners the clone copied from the template.
                button.onClick = new Button.ButtonClickedEvent();
                // C# lambdas capture variables by reference, but foreach gives each iteration its own 'food',
                // so each button feeds its own food.
                button.onClick.AddListener(() => Buy(food));

                SetUpVisuals(button, food);
                m_Entries.Add(new Entry
                {
                    Food = food,
                    Button = button,
                    Graphics = button.GetComponentsInChildren<Graphic>(true),
                });
            }
        }

        static void SetUpVisuals(Button button, FoodItem food)
        {
            var selectionBox = button.transform.Find("SelectionBox");
            if (selectionBox != null)
                selectionBox.gameObject.SetActive(false);

            var iconTransform = button.transform.Find("ObjectIcon");
            var icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            if (icon != null)
            {
                icon.sprite = food.icon;
                icon.enabled = food.icon != null;
            }

            // Price along the bottom; the food's name in the middle if it has no icon yet.
            AddLabel(button.transform, "Price", food.price.ToString(), 34f, new Vector2(0f, 0f), new Vector2(1f, 0.35f));
            if (food.icon == null)
                AddLabel(button.transform, "Name", food.displayName, 28f, new Vector2(0f, 0.35f), new Vector2(1f, 1f));
        }

        static void AddLabel(Transform parent, string name, string text, float size, Vector2 anchorMin, Vector2 anchorMax)
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
        }

        void Update()
        {
            // A handful of buttons, so simply refresh every frame rather than tracking each reason separately.
            foreach (var entry in m_Entries)
            {
                var usable = CanBuy(entry.Food);
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

        bool CanBuy(FoodItem food) =>
            m_Wallet != null && m_Needs != null && m_Summoner != null &&
            m_Summoner.summoned != null &&
            m_Wallet.CanAfford(food.price) &&
            !m_Needs.isFull;

        void Buy(FoodItem food)
        {
            if (!CanBuy(food) || !m_Wallet.TrySpend(food.price))
                return;

            m_Needs.Feed(food.hungerRestored);

            if (m_Summoner.summoned.TryGetComponent<FurBrainPet>(out var pet))
                pet.Eat();

            if (m_CloseAfterBuying && m_MenuManager != null)
                m_MenuManager.HideMenu();
        }
    }
}
