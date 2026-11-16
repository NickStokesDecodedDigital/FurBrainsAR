using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// The Options menu's "Mute Sounds" row: tapping the row or its switch toggles <see cref="SoundSettings.muted"/>,
    /// and the switch shows the current setting.
    /// </summary>
    /// <remarks>
    /// The row is a copy of the template's "Visualize Surfaces" row (made by Tools → FurBrains → Arrange HUD), so it
    /// has the same two parts: a Button covering the whole row and a small on/off switch (the template's DebugSlider,
    /// a Slider whose value is 0 or 1 and which flips itself when tapped). Taps on the switch go to the switch, taps
    /// elsewhere on the row go to the Button, so both are listened to.
    ///
    /// Listeners are added in OnEnable and removed in OnDisable (the project's convention), so nothing is left
    /// pointing at this object once it's switched off or destroyed.
    /// </remarks>
    public class MuteSoundsToggle : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Button covering the row.")]
        Button m_Button;

        [SerializeField]
        [Tooltip("The row's on/off switch (value 1 = muted).")]
        Slider m_Switch;

        void OnEnable()
        {
            if (m_Button != null)
                m_Button.onClick.AddListener(OnRowTapped);
            if (m_Switch != null)
                m_Switch.onValueChanged.AddListener(OnSwitchChanged);
            ShowSetting();
        }

        void OnDisable()
        {
            if (m_Button != null)
                m_Button.onClick.RemoveListener(OnRowTapped);
            if (m_Switch != null)
                m_Switch.onValueChanged.RemoveListener(OnSwitchChanged);
        }

        // Tapped the row: flip the setting and move the switch to match.
        void OnRowTapped()
        {
            SoundSettings.muted = !SoundSettings.muted;
            ShowSetting();
        }

        // Tapped the switch itself: it has already flipped, so take the setting from it.
        void OnSwitchChanged(float value) => SoundSettings.muted = value > 0.5f;

        void ShowSetting()
        {
            // "WithoutNotify" moves the switch without raising onValueChanged, so this doesn't call OnSwitchChanged.
            if (m_Switch != null)
                m_Switch.SetValueWithoutNotify(SoundSettings.muted ? 1f : 0f);
        }
    }
}
