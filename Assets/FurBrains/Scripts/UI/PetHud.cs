using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Top-right display: the player's coins and the FurBrain's hunger bar.
    /// </summary>
    /// <remarks>
    /// The HUD only <i>shows</i> values; PlayerWallet and FurBrainNeeds own them. It listens to their change events,
    /// so the UI and the game logic stay independent (a minigame can add coins without knowing the HUD exists).
    /// The UI objects themselves are created by Tools → FurBrains → Set Up Feeding In Open Scene, and can be restyled
    /// freely in the Scene view.
    /// </remarks>
    public class PetHud : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Coin balance to show. Found in the scene automatically if left empty.")]
        PlayerWallet m_Wallet;

        [SerializeField]
        [Tooltip("Hunger to show. Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Text that shows the coin balance.")]
        TMP_Text m_CoinsText;

        [SerializeField]
        [Tooltip("Bar fill image. Stretched across the bar background; its width is set from hunger.")]
        Image m_HungerFill;

        [SerializeField]
        [Tooltip("Bar colour from very hungry (left) to full (right).")]
        Gradient m_HungerColors = DefaultGradient();

        [SerializeField]
        [Tooltip("Object shown only while a FurBrain is summoned. If empty, the hunger fill's parent (the bar) is used.")]
        GameObject m_HungerBar;

        [SerializeField]
        [Tooltip("Tells the HUD whether a FurBrain is summoned. Found in the scene automatically if left empty.")]
        FurBrainSummoner m_Summoner;

        void Awake()
        {
            if (m_Summoner == null)
                m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_HungerBar == null && m_HungerFill != null && m_HungerFill.transform.parent != null)
                m_HungerBar = m_HungerFill.transform.parent.gameObject;
            if (m_Wallet == null)
                m_Wallet = FindAnyObjectByType<PlayerWallet>();
            if (m_Needs == null)
                m_Needs = FindAnyObjectByType<FurBrainNeeds>();
        }

        void OnEnable()
        {
            if (m_Wallet != null)
                m_Wallet.coinsChanged += OnCoinsChanged;
            if (m_Needs != null)
                m_Needs.hungerChanged += OnHungerChanged;
        }

        void OnDisable()
        {
            if (m_Wallet != null)
                m_Wallet.coinsChanged -= OnCoinsChanged;
            if (m_Needs != null)
                m_Needs.hungerChanged -= OnHungerChanged;
        }

        void Start()
        {
            // Events only fire on changes, so show the starting values once.
            if (m_Wallet != null)
                OnCoinsChanged(m_Wallet.coins);
            if (m_Needs != null)
                OnHungerChanged(m_Needs.hunger);
        }

        void Update()
        {
            // The hunger bar is about the FurBrain in front of you, so it only shows while one is summoned.
            // (Hunger itself keeps being tracked by FurBrainNeeds either way.)
            if (m_HungerBar == null)
                return;
            var show = m_Summoner != null && m_Summoner.hasSummoned;
            if (m_HungerBar.activeSelf != show)
                m_HungerBar.SetActive(show);
        }

        void OnCoinsChanged(int coins)
        {
            if (m_CoinsText != null)
                m_CoinsText.text = coins.ToString();
        }

        void OnHungerChanged(float hunger)
        {
            if (m_HungerFill == null)
                return;
            var t = m_Needs.hungerNormalized;
            // Size the fill by moving its right anchor: anchors are fractions of the parent (the bar background), so
            // anchorMax.x = 0.5 makes the fill end halfway along the bar. Unlike Image.fillAmount, this works with any
            // image, including one without a sprite (Filled images ignore fillAmount when they have no sprite).
            var rect = m_HungerFill.rectTransform;
            rect.anchorMax = new Vector2(t, rect.anchorMax.y);
            m_HungerFill.color = m_HungerColors.Evaluate(t);
        }

        static Gradient DefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.35f, 0.3f), 0f),   // red: very hungry
                    new GradientColorKey(new Color(1f, 0.75f, 0.25f), 0.4f),   // orange
                    new GradientColorKey(new Color(0.45f, 0.85f, 0.4f), 0.75f) // green: well fed
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
