using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Templates.AR;

namespace FurBrains
{
    /// <summary>
    /// Turns the template's object menu (opened by the create button) into a food menu. One button per FoodItem,
    /// cheapest on the left. Tapping a food pays for it and feeds the FurBrain; the menu stays open (optional).
    /// </summary>
    /// <remarks>
    /// Button building, greying out and menu scaling come from <see cref="IconButtonMenu"/>. A food is greyed out when
    /// it can't be used: no FurBrain summoned, not enough coins, or already full (so a child never spends coins for
    /// nothing).
    /// </remarks>
    public class FoodMenu : IconButtonMenu
    {
        [SerializeField]
        [Tooltip("Foods to offer. Order doesn't matter: they're shown cheapest first.")]
        List<FoodItem> m_Foods = new();

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        PlayerWallet m_Wallet;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Used to find the summoned FurBrain. Found in the scene automatically if left empty.")]
        FurBrainSummoner m_Summoner;

        [SerializeField]
        [Tooltip("Used to close the menu after feeding. Found in the scene automatically if left empty.")]
        ARTemplateMenuManager m_MenuManager;

        [SerializeField]
        [Tooltip("Close the menu after buying a food. Off: the menu stays open so several foods can be bought in a row.")]
        bool m_CloseAfterBuying;

        /// <summary>Foods on offer.</summary>
        public List<FoodItem> foods => m_Foods;

        void Awake()
        {
            if (m_Wallet == null) m_Wallet = FindAnyObjectByType<PlayerWallet>();
            if (m_Needs == null) m_Needs = FindAnyObjectByType<FurBrainNeeds>();
            if (m_Summoner == null) m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_MenuManager == null) m_MenuManager = FindAnyObjectByType<ARTemplateMenuManager>(FindObjectsInactive.Include);
        }

        protected override void BuildItems()
        {
            foreach (var food in m_Foods.Where(f => f != null).OrderBy(f => f.price))
            {
                // Lambdas capture variables, but foreach gives each iteration its own 'food', so each button
                // buys its own food.
                AddButton($"Food ({food.name})", food.icon, food.displayName, food.price.ToString(),
                    () => Buy(food), () => CanBuy(food));
            }
        }

        bool CanBuy(FoodItem food) =>
            m_Wallet != null && m_Needs != null && m_Summoner != null &&
            m_Summoner.hasSummoned &&
            m_Wallet.CanAfford(food.price) &&
            !m_Needs.isFull;

        void Buy(FoodItem food)
        {
            if (!CanBuy(food) || !m_Wallet.TrySpend(food.price))
                return;

            m_Needs.Feed(food.hungerRestored);

            if (m_Summoner.summoned.TryGetComponent<FurBrainPet>(out var pet))
                pet.Eat();

            if (m_CloseAfterBuying && m_MenuManager != null)
                m_MenuManager.HideMenu();
        }
    }
}
