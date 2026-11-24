using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Draws a Toggle as a sliding on/off switch: the knob slides right and the track turns the "on" colour.
    /// </summary>
    /// <remarks>
    /// Unity's Toggle only knows how to fade a checkmark in and out, so it keeps the on/off state (<c>isOn</c>, and
    /// the tap handling) and this script just reads it every frame and moves the pieces, easing toward the target so
    /// the knob slides instead of jumping. The Toggle is on the whole row, so tapping anywhere on it flips the switch.
    /// </remarks>
    public class SwitchVisual : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The Toggle holding the on/off state. Found on this object if empty.")]
        Toggle m_Toggle;

        [SerializeField]
        [Tooltip("The switch's background pill.")]
        Image m_Track;

        [SerializeField]
        [Tooltip("The round knob, a child of the track anchored at its centre.")]
        RectTransform m_Knob;

        [SerializeField]
        [Tooltip("Track colour when on.")]
        Color m_OnColor = new Color32(78, 201, 176, 255);

        [SerializeField]
        [Tooltip("Track colour when off.")]
        Color m_OffColor = new Color32(77, 80, 140, 255);

        [SerializeField]
        [Tooltip("How fast the knob slides (higher = quicker).")]
        float m_Speed = 14f;

        float m_Position; // 0 = off (knob left) .. 1 = on (knob right)

        void Awake()
        {
            if (m_Toggle == null)
                m_Toggle = GetComponent<Toggle>();
        }

        void OnEnable()
        {
            // Jump straight to the current state when the panel opens, rather than sliding from the old one.
            m_Position = IsOn() ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            // Eases toward the target; × deltaTime keeps the speed the same at any frame rate.
            m_Position = Mathf.MoveTowards(m_Position, IsOn() ? 1f : 0f, m_Speed * Time.unscaledDeltaTime);
            Apply();
        }

        bool IsOn() => m_Toggle != null && m_Toggle.isOn;

        void Apply()
        {
            if (m_Track != null)
                m_Track.color = Color.Lerp(m_OffColor, m_OnColor, m_Position);
            if (m_Knob != null && m_Track != null)
            {
                // Travel = track width minus knob width, minus a small gap at each end; centred on the track.
                var track = m_Track.rectTransform.rect;
                var gap = (track.height - m_Knob.rect.height) * 0.5f;
                var travel = track.width - m_Knob.rect.width - gap * 2f;
                m_Knob.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, m_Position) * 0.5f, 0f);
            }
        }
    }
}
