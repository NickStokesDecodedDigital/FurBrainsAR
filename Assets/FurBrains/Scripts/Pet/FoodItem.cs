using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// One food the player can buy for their FurBrain. Each food is its own asset file, edited in the Inspector.
    /// </summary>
    /// <remarks>
    /// A ScriptableObject is a data-only asset: like a <c>struct</c> saved to disk that the Editor can show and edit.
    /// Create new foods with right-click in the Project window → Create → FurBrains → Food Item, then add them to the
    /// FoodMenu's list. The menu sorts them by price, cheapest on the left.
    /// </remarks>
    [CreateAssetMenu(menuName = "FurBrains/Food Item", fileName = "NewFood")]
    public class FoodItem : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Name shown on the button when there's no icon.")]
        string m_DisplayName = "Food";

        [SerializeField]
        [Tooltip("Picture shown on the food button (optional).")]
        Sprite m_Icon;

        [SerializeField, Min(0)]
        [Tooltip("Cost in coins.")]
        int m_Price = 10;

        [SerializeField, Min(0)]
        [Tooltip("How much hunger this restores, in the same units as FurBrainNeeds' Max Hunger (100 by default).")]
        float m_HungerRestored = 20f;

        /// <summary>Name shown on the button when there's no icon.</summary>
        public string displayName => m_DisplayName;

        /// <summary>Picture shown on the food button.</summary>
        public Sprite icon => m_Icon;

        /// <summary>Cost in coins.</summary>
        public int price => m_Price;

        /// <summary>How much hunger this restores.</summary>
        public float hungerRestored => m_HungerRestored;

#if UNITY_EDITOR
        /// <summary>Editor-only setup, used by the scene setup tool when it creates the starter foods.</summary>
        public void EditorInitialize(string displayName, int price, float hungerRestored)
        {
            m_DisplayName = displayName;
            m_Price = price;
            m_HungerRestored = hungerRestored;
        }
#endif
    }
}
