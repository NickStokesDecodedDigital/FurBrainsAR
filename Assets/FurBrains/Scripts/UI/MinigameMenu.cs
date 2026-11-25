using System.Collections.Generic;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// The minigame menu: one free button per MinigameDefinition, in list order. Tapping one starts that game.
    /// </summary>
    /// <remarks>
    /// Button building, greying out and menu scaling come from <see cref="IconButtonMenu"/>, the same as the food menu.
    /// Games are greyed out when there's no FurBrain summoned or a game is already running.
    /// </remarks>
    public class MinigameMenu : IconButtonMenu
    {
        [SerializeField]
        [Tooltip("Minigames to offer, left to right.")]
        List<MinigameDefinition> m_Games = new();

        [SerializeField]
        [Tooltip("Starts the games. Found in the scene automatically if left empty.")]
        MinigameRunner m_Runner;

        [SerializeField]
        [Tooltip("The popup this menu lives in, closed when a game starts.")]
        PopupMenu m_Popup;

        /// <summary>Minigames on offer.</summary>
        public List<MinigameDefinition> games => m_Games;

        void Awake()
        {
            if (m_Runner == null)
                m_Runner = FindAnyObjectByType<MinigameRunner>();
        }

        protected override void BuildItems()
        {
            foreach (var game in m_Games)
            {
                if (game == null)
                    continue;
                var button = AddButton($"Minigame ({game.name})", game.icon, game.displayName, null,
                    () => StartGame(game), () => m_Runner != null && m_Runner.canPlay);

                // Each game's card has its own colour, on the card's own background Image. (Not button.image: that's
                // the Button's Target Graphic, which in the template was the icon.)
                if (game.cardColor.a > 0f && button.TryGetComponent<UnityEngine.UI.Image>(out var card))
                    card.color = game.cardColor;
            }
        }

        void StartGame(MinigameDefinition game)
        {
            if (m_Runner != null && m_Runner.Play(game) && m_Popup != null)
                m_Popup.Hide();
        }
    }
}
