using System;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// The player's coins. Spent on food; minigames will add to it later.
    /// </summary>
    /// <remarks>
    /// Other scripts react to changes through the <see cref="coinsChanged"/> event rather than checking every frame
    /// (C# <c>event Action&lt;int&gt;</c> is a list of callbacks, like <c>std::vector&lt;std::function&lt;void(int)&gt;&gt;</c>
    /// that only this class may invoke).
    ///
    /// The balance is saved on the device with PlayerPrefs (Unity's small local key/value store), so it survives
    /// restarting the app. Nothing leaves the device.
    /// </remarks>
    public class PlayerWallet : MonoBehaviour
    {
        /// <summary>PlayerPrefs key the balance is saved under.</summary>
        public const string SaveKey = "FurBrains.Coins";
        const string k_SaveKey = SaveKey;

        [SerializeField, Min(0)]
        [Tooltip("Coins a brand-new player starts with. Only used when nothing is saved yet: " +
                 "use Tools → FurBrains → Clear Saved Progress to start fresh.")]
        int m_StartingCoins = 100;

        [SerializeField]
        [Tooltip("Remember the balance between app launches.")]
        bool m_SaveProgress = true;

        int m_Coins;

        /// <summary>Invoked with the new balance whenever it changes.</summary>
        public event Action<int> coinsChanged;

        /// <summary>Current balance.</summary>
        public int coins => m_Coins;

        void Awake()
        {
            m_Coins = m_SaveProgress ? PlayerPrefs.GetInt(k_SaveKey, m_StartingCoins) : m_StartingCoins;
        }

        /// <summary>Whether the player has at least this many coins.</summary>
        public bool CanAfford(int amount) => m_Coins >= amount;

        /// <summary>Takes the coins if the player can afford it. Returns false (and changes nothing) otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || !CanAfford(amount))
                return false;
            SetCoins(m_Coins - amount);
            return true;
        }

        /// <summary>Gives the player coins (for minigame rewards and the like).</summary>
        public void Add(int amount)
        {
            if (amount > 0)
                SetCoins(m_Coins + amount);
        }

        void SetCoins(int value)
        {
            m_Coins = Mathf.Max(0, value);
            coinsChanged?.Invoke(m_Coins);
            Save();
        }

        void Save()
        {
            if (!m_SaveProgress)
                return;
            PlayerPrefs.SetInt(k_SaveKey, m_Coins);
            PlayerPrefs.Save();
        }

        /// <summary>Testing aid: right-click the component header in the Inspector → Reset Coins.</summary>
        [ContextMenu("Reset Coins")]
        void ResetCoins() => SetCoins(m_StartingCoins);
    }
}
