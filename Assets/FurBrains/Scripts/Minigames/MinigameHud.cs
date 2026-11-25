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
        [Tooltip("Optional background or border behind the live score, shown and hidden together with it.")]
        GameObject m_LiveScoreBackground;

        [SerializeField]
        [Tooltip("Fill image of the time bar, shown while a timed game runs. Its width shrinks as time runs out.")]
        Image m_TimeFill;

        [SerializeField]
        [Tooltip("Panel shown after a game.")]
        GameObject m_ResultsPanel;

        [SerializeField]
        [Tooltip("Full-screen Image just above the Results Panel in the Hierarchy (Raycast Target on). Shown and hidden " +
                 "with the results; it dims the game and catches taps, so the other buttons can't be pressed meanwhile. " +
                 "If left empty, one is made in code with the colour below.")]
        GameObject m_ResultsBackdrop;

        [SerializeField]
        [Tooltip("Colour of the backdrop made in code when Results Backdrop is empty. Alpha 0 = invisible but still " +
                 "blocking. Not used when you assign your own backdrop (set its Image colour instead).")]
        Color m_ResultsBackdropColor = new Color(0f, 0f, 0f, 0.4f);

        [SerializeField]
        [Tooltip("Results: \"Great Job!\" for a win or any points, otherwise \"Nice try!\".")]
        TMP_Text m_ResultText;

        [SerializeField]
        [Tooltip("Results: \"Score: N\". Hidden after a won-or-lost game, which has no score.")]
        TMP_Text m_ScoreText;

        [SerializeField]
        [Tooltip("Results: how much the Play meter went up during the game, \"Play Meter: +N%!\".")]
        TMP_Text m_PlayMeterText;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty. Used for the Play meter gain.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Results: coins earned.")]
        TMP_Text m_CoinsText;

        [SerializeField]
        [Tooltip("Closes the results panel.")]
        Button m_OkButton;

        [SerializeField]
        [Tooltip("Results: starts the same game again. Optional.")]
        Button m_PlayAgainButton;

        [SerializeField]
        [Tooltip("Results close by themselves after this many seconds.")]
        float m_AutoCloseSeconds = 4f;

        [SerializeField]
        [Tooltip("Other UI hidden while a game runs (e.g. the options button). The food, minigame and delete buttons " +
                 "already hide themselves.")]
        GameObject[] m_HideWhilePlaying = System.Array.Empty<GameObject>();

        [SerializeField]
        [Tooltip("UI shown only while a game runs (e.g. the Play meter badge at the bottom centre). Hidden otherwise.")]
        GameObject[] m_ShowWhilePlaying = System.Array.Empty<GameObject>();

        float m_CloseTime = -1f;
        bool m_WasPlaying;
        int m_ShownScore = int.MinValue;
        MinigameDefinition m_LastGame; // the game the results panel is showing, for Play Again
        float m_PlayAtStart;           // Play meter (0..1) when the current game started

        void Awake()
        {
            if (m_Runner == null)
                m_Runner = FindAnyObjectByType<MinigameRunner>();
            if (m_Needs == null)
                m_Needs = FindAnyObjectByType<FurBrainNeeds>();
            // Start hidden (not via SetPlaying, which would also re-show the Hide While Playing objects).
            if (m_ExitButton != null) m_ExitButton.gameObject.SetActive(false);
            SetLiveScoreVisible(false);
            if (m_TimeFill != null && m_TimeFill.transform.parent != null)
                m_TimeFill.transform.parent.gameObject.SetActive(false);
            if (m_ResultsPanel != null)
            {
                // Use the backdrop placed in the scene if there is one; otherwise make one.
                if (m_ResultsBackdrop == null)
                    CreateResultsBackdrop();
                m_ResultsBackdrop.SetActive(false);
                m_ResultsPanel.SetActive(false);
            }
            foreach (var go in m_ShowWhilePlaying)
                if (go != null)
                    go.SetActive(false);
        }

        void OnEnable()
        {
            // The results panel reacts to a moment (a game ending, with its result and coins), so it still uses
            // the runner's event; everything else below is read from the runner every frame.
            if (m_Runner != null)
                m_Runner.ended += OnEnded;
            if (m_ExitButton != null) m_ExitButton.onClick.AddListener(OnExit);
            if (m_OkButton != null) m_OkButton.onClick.AddListener(CloseResults);
            if (m_PlayAgainButton != null) m_PlayAgainButton.onClick.AddListener(PlayAgain);
        }

        void OnDisable()
        {
            if (m_Runner != null)
                m_Runner.ended -= OnEnded;
            if (m_ExitButton != null) m_ExitButton.onClick.RemoveListener(OnExit);
            if (m_OkButton != null) m_OkButton.onClick.RemoveListener(CloseResults);
            if (m_PlayAgainButton != null) m_PlayAgainButton.onClick.RemoveListener(PlayAgain);
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
                {
                    CloseResults();
                    // Remember the Play meter so the results can show how much this game added.
                    if (m_Needs != null)
                        m_PlayAtStart = m_Needs.playNormalized;
                }
                SetPlaying(playing);
                SetLiveScoreVisible(playing && game.resultKind == MinigameResultKind.Score);
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

            //if (m_CloseTime >= 0f && Time.time >= m_CloseTime)
            //    CloseResults();
        }

        void OnEnded(MinigameDefinition definition, MinigameResult result, int coins)
        {
            m_LastGame = definition;
            if (m_ResultsPanel == null)
                return;

            // isGood = a win or any points (the same test that makes Ziara celebrate).
            if (m_ResultText != null)
                m_ResultText.text = result.isGood ? "Great Job!" : "Nice try!";

            // Only score games have a score; a won-or-lost game hides the line.
            if (m_ScoreText != null)
            {
                var hasScore = result.kind == MinigameResultKind.Score;
                m_ScoreText.gameObject.SetActive(hasScore);
                if (hasScore)
                    m_ScoreText.text = $"Score: {result.score}";
            }

            // Whole percent of the meter gained since the game started (rounded down, never below 0).
            if (m_PlayMeterText != null && m_Needs != null)
            {
                var gain = Mathf.Max(0, Mathf.FloorToInt((m_Needs.playNormalized - m_PlayAtStart) * 100f + 0.001f));
                m_PlayMeterText.text = $"Play Meter: +{gain}%!";
            }

            if (m_CoinsText != null)
                m_CoinsText.text = $"+{coins}";

            m_ResultsPanel.SetActive(true);
            if (m_ResultsBackdrop != null)
                m_ResultsBackdrop.SetActive(true);
            m_CloseTime = Time.time + m_AutoCloseSeconds;
        }

        void SetPlaying(bool playing)
        {
            if (m_ExitButton != null)
                m_ExitButton.gameObject.SetActive(playing);
            if (!playing)
                SetLiveScoreVisible(false);
            foreach (var go in m_HideWhilePlaying)
                if (go != null)
                    go.SetActive(!playing);
            foreach (var go in m_ShowWhilePlaying)
                if (go != null)
                    go.SetActive(playing);
        }

        // The live score and its background (if any) always appear and disappear together.
        void SetLiveScoreVisible(bool visible)
        {
            if (m_LiveScoreText != null)
                m_LiveScoreText.gameObject.SetActive(visible);
            if (m_LiveScoreBackground != null)
                m_LiveScoreBackground.SetActive(visible);
        }

        void OnExit()
        {
            if (m_Runner != null)
                m_Runner.Quit();
        }

        void PlayAgain()
        {
            // Starting the game hides the results by itself (Update sees playing become true and calls CloseResults).
            // Play returns false if it can't start (e.g. the FurBrain was removed); then just close the results.
            if (m_Runner == null || !m_Runner.Play(m_LastGame))
                CloseResults();
        }

        void CloseResults()
        {
            m_CloseTime = -1f;
            if (m_ResultsPanel != null)
                m_ResultsPanel.SetActive(false);
            if (m_ResultsBackdrop != null)
                m_ResultsBackdrop.SetActive(false);
        }

        /// <summary>
        /// A full-screen Image just behind the results panel. UI is drawn in Hierarchy order and taps go to the
        /// top-most Graphic with Raycast Target on, so this sits above everything earlier in the canvas (Main's
        /// buttons, the menus) and swallows their taps, while the panel and its buttons, drawn after it, still work.
        /// Only used when no backdrop is assigned in the Inspector, so older scenes need no extra setup.
        /// </summary>
        void CreateResultsBackdrop()
        {
            var parent = m_ResultsPanel.transform.parent;
            m_ResultsBackdrop = new GameObject("Results Backdrop", typeof(RectTransform));
            m_ResultsBackdrop.transform.SetParent(parent, false);
            // Directly before the panel among its siblings, so it's drawn just under it.
            m_ResultsBackdrop.transform.SetSiblingIndex(m_ResultsPanel.transform.GetSiblingIndex());

            // Stretch over the whole parent. The parent (Minigame HUD) fills the screen, so this does too; the large
            // margin makes sure it still covers everything if the parent is smaller.
            var rect = (RectTransform)m_ResultsBackdrop.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-5000f, -5000f);
            rect.offsetMax = new Vector2(5000f, 5000f);

            var image = m_ResultsBackdrop.AddComponent<Image>();
            image.color = m_ResultsBackdropColor;
            image.raycastTarget = true; // this is what blocks the taps

            m_ResultsBackdrop.SetActive(false);
        }
    }
}
