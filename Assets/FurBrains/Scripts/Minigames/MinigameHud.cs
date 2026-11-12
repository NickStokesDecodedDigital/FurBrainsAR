using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Minigame screen overlay: an exit button and a live score while a game runs, and a results panel afterwards.
    /// </summary>
    /// <remarks>
    /// Works for every game through the runner's events and the Minigame base class (it shows <c>currentScore</c>
    /// for games that count points). The UI objects are created by Tools → FurBrains → Set Up Minigames In Open Scene
    /// and can be restyled freely.
    /// </remarks>
    public class MinigameHud : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        MinigameRunner m_Runner;

        [SerializeField]
        [Tooltip("Shown while a game runs; ends the game early.")]
        Button m_ExitButton;

        [SerializeField]
        [Tooltip("Big live score shown while a game that counts points runs.")]
        TMP_Text m_LiveScoreText;

        [SerializeField]
        [Tooltip("Fill image of the time bar, shown while a timed game runs. Its width shrinks as time runs out.")]
        Image m_TimeFill;

        [SerializeField]
        [Tooltip("Panel shown after a game.")]
        GameObject m_ResultsPanel;

        [SerializeField]
        [Tooltip("Results: the score, or win / good try.")]
        TMP_Text m_ResultText;

        [SerializeField]
        [Tooltip("Results: coins earned.")]
        TMP_Text m_CoinsText;

        [SerializeField]
        [Tooltip("Closes the results panel.")]
        Button m_OkButton;

        [SerializeField]
        [Tooltip("Results close by themselves after this many seconds.")]
        float m_AutoCloseSeconds = 4f;

        [SerializeField]
        [Tooltip("Other UI hidden while a game runs (e.g. the options button). The food, minigame and delete buttons " +
                 "already hide themselves.")]
        GameObject[] m_HideWhilePlaying = System.Array.Empty<GameObject>();

        float m_CloseTime = -1f;
        bool m_WasPlaying;
        int m_ShownScore = int.MinValue;

        void Awake()
        {
            if (m_Runner == null)
                m_Runner = FindAnyObjectByType<MinigameRunner>();
            // Start hidden (not via SetPlaying, which would also re-show the Hide While Playing objects).
            if (m_ExitButton != null) m_ExitButton.gameObject.SetActive(false);
            if (m_LiveScoreText != null) m_LiveScoreText.gameObject.SetActive(false);
            if (m_TimeFill != null && m_TimeFill.transform.parent != null)
                m_TimeFill.transform.parent.gameObject.SetActive(false);
            if (m_ResultsPanel != null)
                m_ResultsPanel.SetActive(false);
        }

        void OnEnable()
        {
            // The results panel reacts to a moment (a game ending, with its result and coins), so it still uses
            // the runner's event; everything else below is read from the runner every frame.
            if (m_Runner != null)
                m_Runner.ended += OnEnded;
            if (m_ExitButton != null) m_ExitButton.onClick.AddListener(OnExit);
            if (m_OkButton != null) m_OkButton.onClick.AddListener(CloseResults);
        }

        void OnDisable()
        {
            if (m_Runner != null)
                m_Runner.ended -= OnEnded;
            if (m_ExitButton != null) m_ExitButton.onClick.RemoveListener(OnExit);
            if (m_OkButton != null) m_OkButton.onClick.RemoveListener(CloseResults);
        }

        void Update()
        {
            // Read the runner's state every frame (like a UMG binding) and update the UI when it changes.
            var game = m_Runner != null ? m_Runner.current : null;
            var playing = game != null;
            if (playing != m_WasPlaying)
            {
                m_WasPlaying = playing;
                if (playing)
                    CloseResults();
                SetPlaying(playing);
                if (m_LiveScoreText != null)
                    m_LiveScoreText.gameObject.SetActive(playing && game.resultKind == MinigameResultKind.Score);
                m_ShownScore = int.MinValue;
            }

            if (playing && m_LiveScoreText != null && game.currentScore != m_ShownScore)
            {
                m_ShownScore = game.currentScore;
                m_LiveScoreText.text = m_ShownScore.ToString();
            }

            // Time bar: the bar object is the fill's parent; shown only for timed games (timeRemaining >= 0).
            if (m_TimeFill != null && m_TimeFill.transform.parent != null)
            {
                var bar = m_TimeFill.transform.parent.gameObject;
                var timed = playing && game.timeRemaining >= 0f;
                if (bar.activeSelf != timed)
                    bar.SetActive(timed);
                if (timed)
                {
                    // Same trick as the hunger bar: the right anchor sets the fill's width as a fraction of the bar.
                    var rect = m_TimeFill.rectTransform;
                    rect.anchorMax = new Vector2(game.timeRemainingNormalized, rect.anchorMax.y);
                }
            }

            if (m_CloseTime >= 0f && Time.time >= m_CloseTime)
                CloseResults();
        }

        void OnEnded(MinigameDefinition definition, MinigameResult result, int coins)
        {
            if (m_ResultsPanel == null)
                return;

            if (m_ResultText != null)
                m_ResultText.text = result.kind == MinigameResultKind.Score
                    ? result.score.ToString()
                    : result.won ? "You win!" : "Good try!"; // placeholder until there are win / try-again icons
            if (m_CoinsText != null)
                m_CoinsText.text = $"+{coins}";

            m_ResultsPanel.SetActive(true);
            m_CloseTime = Time.time + m_AutoCloseSeconds;
        }

        void SetPlaying(bool playing)
        {
            if (m_ExitButton != null)
                m_ExitButton.gameObject.SetActive(playing);
            if (m_LiveScoreText != null && !playing)
                m_LiveScoreText.gameObject.SetActive(false);
            foreach (var go in m_HideWhilePlaying)
                if (go != null)
                    go.SetActive(!playing);
        }

        void OnExit()
        {
            if (m_Runner != null)
                m_Runner.Quit();
        }

        void CloseResults()
        {
            m_CloseTime = -1f;
            if (m_ResultsPanel != null)
                m_ResultsPanel.SetActive(false);
        }
    }
}
