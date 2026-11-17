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

        [SerializeField]
        [Tooltip("Blend shapes the FurBrain holds while the food menu is open (\"O\" = an eager open mouth). " +
                 "Empty = keep its normal mood face.")]
        string[] m_PetExpression = { "O" };

        FurBrainMood m_HeldMood; // the FurBrain whose face this menu is currently holding (null = none)

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
                var button = AddButton($"Food ({food.name})", food.icon, food.displayName, food.price.ToString(),
                    () => Buy(food), () => CanBuy(food));

                // Drag-to-feed: the same Buy and CanBuy as the tap, so both ways behave identically.
                button.gameObject.AddComponent<DragToPet>().Setup(food.icon, () => Buy(food), () => CanBuy(food), m_Summoner);
            }
        }

        bool CanBuy(FoodItem food) =>
            m_Wallet != null && m_Needs != null && m_Summoner != null &&
            m_Summoner.hasSummoned &&
            PetCanEat() &&
            m_Wallet.CanAfford(food.price) &&
            !m_Needs.isFull;

        // False while the FurBrain is still in its Feed Cooldown after the last food (the buttons grey out meanwhile).
        bool PetCanEat() =>
            !m_Summoner.summoned.TryGetComponent<FurBrainPet>(out var pet) || pet.canEat;

        void Buy(FoodItem food)
        {
            if (!CanBuy(food) || !m_Wallet.TrySpend(food.price))
                return;

            m_Needs.Feed(food.hungerRestored);

            if (m_Summoner.summoned.TryGetComponent<FurBrainPet>(out var pet))
                pet.Eat(food.crumbColor); // crumbs in the food's colour

            if (m_CloseAfterBuying && m_MenuManager != null)
                m_MenuManager.HideMenu();
        }

        // "override" replaces the base class's Update (it's virtual there); base.Update() still runs the base version
        // first (greying out buttons), like calling Base::Update() from a C++ override.
        protected override void Update()
        {
            base.Update();
            UpdatePetFace();
        }

        /// <summary>
        /// While the menu is open, the FurBrain holds an eager "O" face (through FurBrainMood, the face's owner).
        /// Closing the menu only slides it down (this component stays active), so each frame we compare
        /// "is the menu open?" with "are we holding the face?" and act on the two moments when they differ.
        /// </summary>
        void UpdatePetFace()
        {
            var menuOpen = m_MenuManager != null && m_MenuManager.isFoodMenuShown;
            var holdingFace = m_HeldMood != null;

            if (menuOpen && !holdingFace)
                StartHoldingFace();   // the menu has just opened
            else if (!menuOpen && holdingFace)
                StopHoldingFace();    // the menu has just closed
            // Otherwise nothing changed since last frame: nothing to do.
        }

        void StartHoldingFace()
        {
            if (m_PetExpression.Length == 0 || m_Summoner == null || !m_Summoner.hasSummoned)
                return; // no face to show, or no FurBrain to show it on

            // TryGetComponent fills m_HeldMood ("out", like a C++ reference parameter) and returns whether it found one.
            if (m_Summoner.summoned.TryGetComponent(out m_HeldMood))
                m_HeldMood.Hold(m_PetExpression);
        }

        void StopHoldingFace()
        {
            // Unity's != null is also false for a FurBrain that was deleted in the meantime, so this is safe.
            if (m_HeldMood != null)
                m_HeldMood.ReleaseHold();
            m_HeldMood = null;
        }

        // If this menu is switched off entirely (e.g. a minigame hides it), don't leave the FurBrain with the face.
        // (Deleting the FurBrain also closes the menu, through ARTemplateMenuManager, so that's covered above.)
        void OnDisable() => StopHoldingFace();
    }
}
