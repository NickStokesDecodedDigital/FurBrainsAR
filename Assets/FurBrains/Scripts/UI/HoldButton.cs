using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace FurBrains
{
    /// <summary>
    /// A button that only fires after being held down for a few seconds: a simple grown-up gate, since young children
    /// tend to tap rather than hold. A fill grows across the button while it's held and shrinks back if let go early.
    /// </summary>
    /// <remarks>
    /// <b>How it hears the finger:</b> the EventSystem calls these interface methods on any UI object with a Graphic
    /// (here the button's Image) that's pressed: <c>IPointerDownHandler.OnPointerDown</c> etc. A C# interface is like
    /// a C++ abstract class with only pure virtual functions; implementing it is how the EventSystem finds us.
    /// Sliding the finger off the button counts as letting go.
    ///
    /// <c>onHeld</c> is a UnityEvent, the same kind of event as Button.onClick: listeners can be added in code or in
    /// the Inspector.
    /// </remarks>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField]
        [Tooltip("How long the button must be held, in seconds.")]
        float m_HoldSeconds = 3f;

        [SerializeField]
        [Tooltip("Shows the progress: its width grows from its height (a circle) to the full button. Anchored to the " +
                 "button's left edge. Hidden while not held.")]
        RectTransform m_Fill;

        [SerializeField]
        [Tooltip("Runs once the button has been held long enough.")]
        UnityEvent m_OnHeld = new UnityEvent();

        bool m_Holding;
        float m_Progress; // 0..1

        /// <summary>Runs once the button has been held long enough.</summary>
        public UnityEvent onHeld => m_OnHeld;

        public void OnPointerDown(PointerEventData eventData) => m_Holding = true;
        public void OnPointerUp(PointerEventData eventData) => m_Holding = false;
        public void OnPointerExit(PointerEventData eventData) => m_Holding = false;

        void OnDisable()
        {
            m_Holding = false;
            m_Progress = 0f;
            ShowProgress();
        }

        void Update()
        {
            // Fills over m_HoldSeconds while held; drains twice as fast when let go early.
            var rate = 1f / Mathf.Max(0.1f, m_HoldSeconds);
            m_Progress = Mathf.Clamp01(m_Progress + (m_Holding ? rate : -2f * rate) * Time.unscaledDeltaTime);

            if (m_Holding && m_Progress >= 1f)
            {
                m_Holding = false; // one go per press
                m_Progress = 0f;
                m_OnHeld.Invoke();
            }
            ShowProgress();
        }

        void ShowProgress()
        {
            if (m_Fill == null)
                return;
            var visible = m_Progress > 0f;
            if (m_Fill.gameObject.activeSelf != visible)
                m_Fill.gameObject.SetActive(visible);
            if (!visible)
                return;

            // Width from the button's height (a circle, so the rounded ends look right) up to its full width.
            var button = ((RectTransform)transform).rect;
            var width = Mathf.Lerp(button.height, button.width, m_Progress);
            m_Fill.sizeDelta = new Vector2(width, m_Fill.sizeDelta.y);
        }
    }
}
