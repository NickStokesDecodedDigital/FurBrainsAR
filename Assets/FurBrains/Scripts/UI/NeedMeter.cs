using UnityEngine;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Shows one of the FurBrain's needs as a coloured fill inside a round button: the circle fills from the bottom up
    /// like a glass, red when low and green when full. Hunger sits in the food button, Play in the minigame button.
    /// </summary>
    /// <remarks>
    /// The fill is a UI Image set to <b>Filled → Vertical → Bottom</b>: Unity only draws the bottom
    /// <c>fillAmount</c> fraction of the sprite, so 0.5 shows the lower half of the circle. It uses the button's own
    /// circle sprite so the fill has exactly the button's shape (a Filled image needs a sprite to fill).
    ///
    /// Like the other HUD scripts it only <i>shows</i> a value: FurBrainNeeds owns it, and this reads it every frame
    /// (FurBrainNeeds never knows the meter exists). Tools → FurBrains → Arrange HUD In Open Scene creates the fill
    /// objects and adds this component.
    /// </remarks>
    public class NeedMeter : MonoBehaviour
    {
        /// <summary>Which need to show.</summary>
        public enum NeedType { Hunger, Play }

        [SerializeField]
        [Tooltip("Which need this meter shows.")]
        NeedType m_Need;

        [SerializeField]
        [Tooltip("The needs to show. Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Fill image (Image Type: Filled, Fill Method: Vertical, Fill Origin: Bottom). " +
                 "If empty, the Image on this object is used.")]
        Image m_Fill;

        [SerializeField]
        [Tooltip("Fill colour from empty (left) to full (right).")]
        Gradient m_Colors = DefaultGradient();

        float m_ShownValue = -1f; // last value drawn, so the image is only updated when it changes

        /// <summary>Which need this meter shows.</summary>
        public NeedType need
        {
            get => m_Need;
            set => m_Need = value;
        }

        void Awake()
        {
            if (m_Needs == null)
                m_Needs = FindAnyObjectByType<FurBrainNeeds>();
            if (m_Fill == null)
                m_Fill = GetComponent<Image>();
        }

        void Update()
        {
            if (m_Needs == null || m_Fill == null)
                return;

            // A switch on the enum picks the value: simple, and adding a need later means one more case here.
            var value = m_Need switch
            {
                NeedType.Hunger => m_Needs.hungerNormalized,
                NeedType.Play => m_Needs.playNormalized,
                _ => 0f,
            };

            // The needs drain a tiny amount every frame; only redraw when the change is visible (0.1%).
            if (Mathf.Abs(value - m_ShownValue) < 0.001f)
                return;
            m_ShownValue = value;
            m_Fill.fillAmount = value;
            m_Fill.color = m_Colors.Evaluate(value);
        }

        static Gradient DefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.35f, 0.3f), 0f),   // red: very low
                    new GradientColorKey(new Color(1f, 0.75f, 0.25f), 0.4f),   // orange
                    new GradientColorKey(new Color(0.45f, 0.85f, 0.4f), 0.75f) // green: well looked after
                },
                // Slightly see-through, so the button's dark circle shows through a little.
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 1f) });
            return gradient;
        }
    }
}
