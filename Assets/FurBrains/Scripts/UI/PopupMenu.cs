using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// A menu button that opens and closes a slide-up menu, like the template's create/food button does. Used for the
    /// minigame menu (a copy of the template's object menu).
    /// </summary>
    /// <remarks>
    /// The menu's slide animation is driven by the same Animator controller as the template's menu: a bool parameter
    /// "Show". Only one popup is open at a time: opening this one closes the food menu, and the food menu closes this
    /// one when it opens (ARTemplateMenuManager calls <see cref="HideAll"/>).
    ///
    /// The button is only shown while a FurBrain is summoned and no minigame is running.
    /// </remarks>
    [RequireComponent(typeof(Button))]
    public class PopupMenu : MonoBehaviour
    {
        static readonly int k_Show = Animator.StringToHash("Show");

        // Every enabled PopupMenu, so HideAll can reach them (a static list is shared by all instances, like a C++
        // static member).
        static readonly List<PopupMenu> s_All = new();

        [SerializeField]
        [Tooltip("Animator on the menu (the copy of \"Object Menu Animator\").")]
        Animator m_MenuAnimator;

        [SerializeField]
        [Tooltip("The menu panel. Activated when opening; taps outside it close the menu.")]
        RectTransform m_MenuPanel;

        [SerializeField]
        [Tooltip("Button in the menu that closes it.")]
        Button m_CancelButton;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainSummoner m_Summoner;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        MinigameRunner m_Runner;

        [SerializeField]
        [Tooltip("The food menu manager, closed when this menu opens. Found in the scene automatically if left empty.")]
        UnityEngine.XR.Templates.AR.ARTemplateMenuManager m_FoodMenu;

        Button m_Button;
        Image m_ButtonImage;
        bool m_Open;

        /// <summary>Whether the menu is open.</summary>
        public bool isOpen => m_Open;

        /// <summary>Closes every popup menu (called when the food menu opens).</summary>
        public static void HideAll()
        {
            foreach (var popup in s_All)
                popup.Hide();
        }

        void Awake()
        {
            m_Button = GetComponent<Button>();
            m_ButtonImage = GetComponent<Image>();
            if (m_Summoner == null) m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_Runner == null) m_Runner = FindAnyObjectByType<MinigameRunner>();
            if (m_FoodMenu == null)
                m_FoodMenu = FindAnyObjectByType<UnityEngine.XR.Templates.AR.ARTemplateMenuManager>(FindObjectsInactive.Include);
        }

        void OnEnable()
        {
            s_All.Add(this);
            m_Button.onClick.AddListener(Toggle);
            if (m_CancelButton != null)
                m_CancelButton.onClick.AddListener(Hide);
        }

        void OnDisable()
        {
            s_All.Remove(this);
            m_Button.onClick.RemoveListener(Toggle);
            if (m_CancelButton != null)
                m_CancelButton.onClick.RemoveListener(Hide);
        }

        void Start() => Hide();

        void Update()
        {
            // Show the button only when it's useful. We hide its graphics and disable clicking rather than
            // deactivating the GameObject, because a deactivated object gets no Update calls to switch back on.
            var available = m_Summoner != null && m_Summoner.hasSummoned && (m_Runner == null || !m_Runner.isPlaying);
            SetButtonVisible(available);
            if (!available && m_Open)
                Hide();

            // Close when the player presses anywhere outside the menu (and not on this button, which toggles).
            var pointer = Pointer.current;
            if (m_Open && pointer != null && pointer.press.wasPressedThisFrame)
            {
                var position = pointer.position.ReadValue();
                if (!Contains(m_MenuPanel, position) && !Contains((RectTransform)transform, position))
                    Hide();
            }
        }

        void SetButtonVisible(bool visible)
        {
            if (m_Button.interactable == visible)
                return;
            m_Button.interactable = visible;
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                graphic.enabled = visible;
        }

        // Screen-space overlay canvas: no camera needed for the point-in-rectangle test.
        static bool Contains(RectTransform rect, Vector2 screenPoint) =>
            rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

        void Toggle()
        {
            if (m_Open) Hide();
            else Show();
        }

        /// <summary>Opens the menu (closing the food menu).</summary>
        public void Show()
        {
            if (m_FoodMenu != null)
                m_FoodMenu.HideMenu();
            m_Open = true;
            if (m_MenuPanel != null)
                m_MenuPanel.gameObject.SetActive(true);
            if (m_MenuAnimator != null)
                m_MenuAnimator.SetBool(k_Show, true);
        }

        /// <summary>Closes the menu.</summary>
        public void Hide()
        {
            m_Open = false;
            if (m_MenuAnimator != null && m_MenuAnimator.isActiveAndEnabled)
                m_MenuAnimator.SetBool(k_Show, false);
        }
    }
}
