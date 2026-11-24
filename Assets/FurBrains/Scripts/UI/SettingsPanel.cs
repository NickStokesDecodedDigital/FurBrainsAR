using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Templates.AR;

namespace FurBrains
{
    /// <summary>
    /// The Settings sheet opened by the Options (⋯) button: sound and floor-dot switches, Move Ziara / Scan again,
    /// and a hold-to-exit button for grown-ups.
    /// </summary>
    /// <remarks>
    /// Built by Tools → FurBrains → Build Settings Panel In Open Scene, which also makes it the menu manager's
    /// "Modal Menu", so the Options button and tapping outside open and close it exactly like the template's old
    /// Options menu. This script only connects the controls to the systems that own each setting:
    /// <list type="bullet">
    /// <item>Sound effects → <see cref="SoundSettings.muted"/> (on = not muted).</item>
    /// <item>Show floor dots → <see cref="ARTemplateMenuManager.SetPlanesVisible"/>.</item>
    /// <item>Move Ziara → <see cref="FurBrainSummoner.Remove"/>, which brings back tap-to-place.</item>
    /// <item>Scan again → also resets AR tracking (forgets the found surfaces) and restarts the scan prompt.</item>
    /// <item>Hold to exit → <see cref="FlutterBridge.ExitToHost"/> (does nothing outside the parent app).</item>
    /// </list>
    /// Everything left empty is found in the scene at start-up.
    /// </remarks>
    public class SettingsPanel : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField, Tooltip("The round X button.")] Button m_CloseButton;
        [SerializeField, Tooltip("Sound effects row (on = sound on).")] Toggle m_SoundToggle;
        [SerializeField, Tooltip("Show floor dots row.")] Toggle m_FloorDotsToggle;
        [SerializeField, Tooltip("Move Ziara button.")] Button m_MoveButton;
        [SerializeField, Tooltip("Scan again button.")] Button m_ScanButton;
        [SerializeField, Tooltip("Hold-to-exit button in the grown-ups box.")] HoldButton m_ExitHoldButton;

        [Header("Systems (found automatically if empty)")]
        [SerializeField] ARTemplateMenuManager m_MenuManager;
        [SerializeField] FurBrainSummoner m_Summoner;
        [SerializeField] GoalManager m_GoalManager;
        [SerializeField] FlutterBridge m_Bridge;
        [SerializeField] ARSession m_Session;

        void Awake()
        {
            // The panel starts switched off, so Awake runs the first time it opens: still before any button works.
            if (m_MenuManager == null) m_MenuManager = FindAnyObjectByType<ARTemplateMenuManager>();
            if (m_Summoner == null) m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_GoalManager == null) m_GoalManager = FindAnyObjectByType<GoalManager>();
            if (m_Bridge == null) m_Bridge = FindAnyObjectByType<FlutterBridge>();
            if (m_Session == null) m_Session = FindAnyObjectByType<ARSession>();
        }

        void OnEnable()
        {
            // Show the current settings each time the panel opens. "WithoutNotify" sets a Toggle without raising
            // onValueChanged, so this doesn't count as the player changing anything.
            if (m_SoundToggle != null)
                m_SoundToggle.SetIsOnWithoutNotify(!SoundSettings.muted);
            if (m_FloorDotsToggle != null && m_MenuManager != null)
                m_FloorDotsToggle.SetIsOnWithoutNotify(m_MenuManager.planesVisible);

            if (m_CloseButton != null) m_CloseButton.onClick.AddListener(Close);
            if (m_SoundToggle != null) m_SoundToggle.onValueChanged.AddListener(OnSoundChanged);
            if (m_FloorDotsToggle != null) m_FloorDotsToggle.onValueChanged.AddListener(OnFloorDotsChanged);
            if (m_MoveButton != null) m_MoveButton.onClick.AddListener(MoveFurBrain);
            if (m_ScanButton != null) m_ScanButton.onClick.AddListener(ScanAgain);
            if (m_ExitHoldButton != null) m_ExitHoldButton.onHeld.AddListener(ExitToApp);
        }

        void OnDisable()
        {
            if (m_CloseButton != null) m_CloseButton.onClick.RemoveListener(Close);
            if (m_SoundToggle != null) m_SoundToggle.onValueChanged.RemoveListener(OnSoundChanged);
            if (m_FloorDotsToggle != null) m_FloorDotsToggle.onValueChanged.RemoveListener(OnFloorDotsChanged);
            if (m_MoveButton != null) m_MoveButton.onClick.RemoveListener(MoveFurBrain);
            if (m_ScanButton != null) m_ScanButton.onClick.RemoveListener(ScanAgain);
            if (m_ExitHoldButton != null) m_ExitHoldButton.onHeld.RemoveListener(ExitToApp);
        }

        void OnSoundChanged(bool on) => SoundSettings.muted = !on;

        void OnFloorDotsChanged(bool on)
        {
            if (m_MenuManager != null)
                m_MenuManager.SetPlanesVisible(on);
        }

        void MoveFurBrain()
        {
            Close();
            if (m_Summoner != null)
                m_Summoner.Remove(); // tap-to-place comes back, with its prompt and the floor dots
        }

        void ScanAgain()
        {
            Close();
            if (m_Summoner != null)
                m_Summoner.Remove(showSummonPrompt: false); // the onboarding shows its own prompts below
            // Reset forgets everything AR has tracked (the surfaces found so far) and starts tracking afresh.
            if (m_Session != null)
                m_Session.Reset();
            if (m_GoalManager != null)
                m_GoalManager.RestartScanning(); // "scan surfaces", then "tap to place" once one is found
        }

        void ExitToApp()
        {
            Close();
            if (m_Bridge != null)
                m_Bridge.ExitToHost();
        }

        // Closes through the menu manager so its own "is the options menu open" bookkeeping stays right.
        void Close()
        {
            if (gameObject.activeSelf && m_MenuManager != null)
                m_MenuManager.ShowHideModal();
            else
                gameObject.SetActive(false);
        }
    }
}
