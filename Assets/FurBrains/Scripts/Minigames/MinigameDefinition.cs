using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// One entry in the minigame menu: what it's called, its icon, the prefab that runs it and its coin rewards.
    /// </summary>
    /// <remarks>
    /// Create one with right-click in the Project window → Create → FurBrains → Minigame, point Prefab at a prefab whose
    /// root has a <see cref="Minigame"/> component, and add the asset to the MinigameMenu's list. That's all the menu and
    /// the runner need: they never know which game they're running, only that it is a Minigame.
    /// </remarks>
    [CreateAssetMenu(menuName = "FurBrains/Minigame", fileName = "NewMinigame")]
    public class MinigameDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Name shown on the menu button when there's no icon.")]
        string m_DisplayName = "Minigame";

        [SerializeField]
        [Tooltip("Picture shown on the menu button (optional).")]
        Sprite m_Icon;

        [SerializeField]
        [Tooltip("Prefab created when the game starts. Its root must have a Minigame component.")]
        Minigame m_Prefab;

        [Header("Rewards")]
        [SerializeField, Min(0)]
        [Tooltip("Coins per point; can be a fraction (e.g. 0.5 = one coin per two points). The total is rounded up. " +
                 "Won-or-lost games count a win as 1 point, so for those this is the win reward.")]
        float m_CoinsPerPoint = 1f;

        [SerializeField, Min(0)]
        [Tooltip("Coins given even for a low score or a loss (no punishing fail states).")]
        int m_MinimumCoins = 2;

        /// <summary>Name shown on the menu button when there's no icon.</summary>
        public string displayName => m_DisplayName;

        /// <summary>Picture shown on the menu button.</summary>
        public Sprite icon => m_Icon;

        /// <summary>Prefab created when the game starts.</summary>
        public Minigame prefab => m_Prefab;

        /// <summary>
        /// Coins earned for a result: points × Coins Per Point, rounded up to a whole coin, but at least Minimum Coins.
        /// A won-or-lost game scores 1 point for a win and 0 for a loss, so one rule covers both kinds of game.
        /// </summary>
        public int CoinsFor(MinigameResult result) =>
            // CeilToInt rounds up (7 points × 0.5 = 3.5 → 4). Coins stay whole numbers; only the rate is a fraction.
            Mathf.Max(m_MinimumCoins, Mathf.CeilToInt(result.points * m_CoinsPerPoint));

#if UNITY_EDITOR
        /// <summary>Editor-only setup, used by the scene setup tool when it creates the starter games.</summary>
        public void EditorInitialize(string displayName, Minigame prefab, float coinsPerPoint = 1f, int minimumCoins = 2)
        {
            m_DisplayName = displayName;
            m_Prefab = prefab;
            m_CoinsPerPoint = coinsPerPoint;
            m_MinimumCoins = minimumCoins;
        }
#endif
    }
}
