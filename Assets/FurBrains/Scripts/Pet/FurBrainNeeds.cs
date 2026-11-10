using System;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// The FurBrain's needs. For now just hunger: a fullness value that slowly drops over time and goes back up when
    /// the FurBrain is fed. 0 = very hungry, <see cref="maxHunger"/> = full.
    /// </summary>
    /// <remarks>
    /// This lives in the scene, not on the spawned FurBrain, because it belongs to the player's pet rather than to one
    /// summoned instance: deleting and re-summoning the FurBrain keeps its hunger. Saved on the device like the coins.
    ///
    /// Hunger never causes anything bad (children's app: no punishing fail states). Later, low hunger can make the
    /// FurBrain look sad or ask for food.
    /// </remarks>
    public class FurBrainNeeds : MonoBehaviour
    {
        /// <summary>PlayerPrefs key the hunger is saved under.</summary>
        public const string SaveKey = "FurBrains.Hunger";
        const string k_SaveKey = SaveKey;

        [SerializeField, Min(1)]
        [Tooltip("Hunger value when completely full.")]
        float m_MaxHunger = 100f;

        [SerializeField, Min(0)]
        [Tooltip("Hunger a brand-new pet starts with. Only used when nothing is saved yet: " +
                 "use Tools → FurBrains → Clear Saved Progress to start fresh.")]
        float m_StartingHunger = 80f;

        [SerializeField, Min(0)]
        [Tooltip("How much hunger is lost per minute of play. 0.5 = full to empty in about 3 hours 20 minutes.")]
        float m_HungerLossPerMinute = 0.5f;

        [SerializeField]
        [Tooltip("Remember hunger between app launches.")]
        bool m_SaveProgress = true;

        float m_Hunger;

        /// <summary>Invoked with the new hunger value whenever it changes.</summary>
        public event Action<float> hungerChanged;

        /// <summary>Current hunger (0 = very hungry, maxHunger = full).</summary>
        public float hunger => m_Hunger;

        /// <summary>Hunger value when completely full.</summary>
        public float maxHunger => m_MaxHunger;

        /// <summary>Hunger as 0..1, handy for UI bars.</summary>
        public float hungerNormalized => m_Hunger / m_MaxHunger;

        /// <summary>True when the FurBrain can't eat any more.</summary>
        public bool isFull => m_Hunger >= m_MaxHunger - 0.01f;

        void Awake()
        {
            var start = Mathf.Min(m_StartingHunger, m_MaxHunger);
            m_Hunger = m_SaveProgress ? PlayerPrefs.GetFloat(k_SaveKey, start) : start;
        }

        void Update()
        {
            if (m_Hunger > 0f && m_HungerLossPerMinute > 0f)
                SetHunger(m_Hunger - m_HungerLossPerMinute / 60f * Time.deltaTime, save: false);
        }

        /// <summary>Restores hunger, up to full.</summary>
        public void Feed(float amount)
        {
            if (amount > 0f)
                SetHunger(m_Hunger + amount, save: true);
        }

        void SetHunger(float value, bool save)
        {
            m_Hunger = Mathf.Clamp(value, 0f, m_MaxHunger);
            hungerChanged?.Invoke(m_Hunger);
            if (save)
                Save();
        }

        void Save()
        {
            if (!m_SaveProgress)
                return;
            PlayerPrefs.SetFloat(k_SaveKey, m_Hunger);
            PlayerPrefs.Save();
        }

        // The slow drain isn't saved every frame; save when the app is backgrounded or closed instead.
        // (On phones, OnApplicationPause(true) is the reliable "about to stop" signal.)
        void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }

        void OnDisable() => Save();

        /// <summary>Testing aid: right-click the component header in the Inspector → Reset Hunger.</summary>
        [ContextMenu("Reset Hunger")]
        void ResetHunger() => SetHunger(Mathf.Min(m_StartingHunger, m_MaxHunger), save: true);

        /// <summary>Testing aid: make the FurBrain very hungry right away.</summary>
        [ContextMenu("Make Hungry")]
        void MakeHungry() => SetHunger(m_MaxHunger * 0.1f, save: true);
    }
}
