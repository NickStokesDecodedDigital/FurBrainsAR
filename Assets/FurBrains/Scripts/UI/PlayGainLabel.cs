using TMPro;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Shows how much the Play meter has gone up since the current minigame started, e.g. "+5%", above the Play badge.
    /// Visible only while a game runs.
    /// </summary>
    /// <remarks>
    /// Follows the project's UI rule: it reads the game state every frame (whether the runner has a game, and the
    /// Play level from FurBrainNeeds) and only rebuilds the text when the shown number changes. Nothing has to tell
    /// it a game started: it notices the runner going from "no game" to "a game" and remembers the Play level then.
    ///
    /// The percent is of the whole meter (percentage points): from 40 % full to 45 % full shows "+5%".
    /// Put it on a TextMeshPro text. It hides and shows its own text, so it can sit anywhere.
    /// </remarks>
    [RequireComponent(typeof(TMP_Text))]
    public class PlayGainLabel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        MinigameRunner m_Runner;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        TMP_Text m_Text;
        bool m_WasPlaying;
        float m_PlayAtStart;           // Play (0..1) when the current game started
        int m_ShownPercent = int.MinValue;

        void Awake()
        {
            m_Text = GetComponent<TMP_Text>();
            if (m_Runner == null) m_Runner = FindAnyObjectByType<MinigameRunner>();
            if (m_Needs == null) m_Needs = FindAnyObjectByType<FurBrainNeeds>();
            m_Text.enabled = false;
        }

        void Update()
        {
            var playing = m_Runner != null && m_Runner.isPlaying && m_Needs != null;

            // A game just started: remember where the meter was.
            if (playing && !m_WasPlaying)
            {
                m_PlayAtStart = m_Needs.playNormalized;
                m_ShownPercent = int.MinValue; // force the text to be rebuilt
            }
            m_WasPlaying = playing;

            if (m_Text.enabled != playing)
                m_Text.enabled = playing;
            if (!playing)
                return;

            // Whole percent gained so far. FloorToInt so it only says +5% once the meter really is 5 points up; the
            // tiny extra stops float rounding (4.99999 for 5) from showing one less.
            var percent = Mathf.Max(0, Mathf.FloorToInt((m_Needs.playNormalized - m_PlayAtStart) * 100f + 0.001f));
            if (percent != m_ShownPercent)
            {
                m_ShownPercent = percent;
                m_Text.text = $"+{percent}%";
            }
        }
    }
}
