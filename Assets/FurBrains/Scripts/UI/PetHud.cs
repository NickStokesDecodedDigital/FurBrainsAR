using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Top-right display: the player's coins and the FurBrain's hunger bar.
    /// </summary>
    /// <remarks>
    /// The HUD only <i>shows</i> values; PlayerWallet and FurBrainNeeds own them. It reads them every frame through
    /// references (the HUD knows about the wallet, never the other way round), so the game logic stays independent of
    /// the UI: a minigame can add coins without knowing the HUD exists.
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

        // Last coin value written to the text, so it's only rebuilt when the number actually changes.
        int m_ShownCoins = int.MinValue;

        // Reads the current values every frame, like a UMG property binding in Unreal: the HUD holds references to
        // the wallet and needs (its "cast to the player") and simply asks them what to show.
        void Update()
        {
            if (m_Wallet != null && m_CoinsText != null && m_Wallet.coins != m_ShownCoins)
            {
                // ToString makes a new string (garbage for the GC to collect later), so skip it when nothing changed.
                m_ShownCoins = m_Wallet.coins;
                m_CoinsText.text = m_ShownCoins.ToString();
            }

            // The hunger bar is about the FurBrain in front of you, so it only shows while one is summoned.
            // (Hunger itself keeps being tracked by FurBrainNeeds either way.)
            var show = m_Summoner != null && m_Summoner.hasSummoned;
            if (m_HungerBar != null && m_HungerBar.activeSelf != show)
                m_HungerBar.SetActive(show);

            if (show)
                ShowHunger();
        }

        void ShowHunger()
        {
            if (m_HungerFill == null || m_Needs == null)
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
