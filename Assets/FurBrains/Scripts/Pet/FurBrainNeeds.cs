using System;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// The FurBrain's needs: <b>Hunger</b> (filled by feeding) and <b>Play</b> (filled by playing minigames). Both
    /// slowly drop over time. 0 = very hungry / very bored, max = full. <b>Happiness</b> is the average of the two.
    /// </summary>
    /// <remarks>
    /// This lives in the scene, not on the spawned FurBrain, because it belongs to the player's pet rather than to one
    /// summoned instance: deleting and re-summoning the FurBrain keeps its needs. Saved on the device like the coins.
    ///
    /// Needs never cause anything bad (children's app: no punishing fail states). Low happiness only changes the
    /// FurBrain's face (FurBrainMood), and the meters inside the food and play buttons (NeedMeter) show each need.
    /// </remarks>
    public class FurBrainNeeds : MonoBehaviour
    {
        /// <summary>
        /// One need: a value between 0 and <see cref="max"/> that drains over time and can be topped up.
        /// </summary>
        /// <remarks>
        /// A small class used twice (hunger and play) instead of copy-pasting every field and function for each.
        /// It's a class (a reference type in C#, so FurBrainNeeds changes its one copy in place), like a C++ class
        /// with member functions. <c>[Serializable]</c> lets Unity save its public fields and show them in the
        /// Inspector as a foldout inside FurBrainNeeds.
        /// </remarks>
        [Serializable]
        public class Need
        {
            [Min(1)] // Min prevents the user from entering a value below x in the inspector
            [Tooltip("Value when completely full.")]
            public float max = 100f;

            [Min(0)]
            [Tooltip("Value a brand-new pet starts with. Only used when nothing is saved yet: " +
                     "use Tools → FurBrains → Clear Saved Progress to start fresh.")]
            public float starting = 80f;

            [Min(0)]
            [Tooltip("How much is lost per minute of play. 0.5 = full to empty in about 3 hours 20 minutes.")]
            public float lossPerMinute = 0.5f;

            /// <summary>Current value (not saved in the scene: it's runtime state, loaded from the device).</summary>
            [NonSerialized] public float value;

            /// <summary>The value as 0..1, handy for meters.</summary>
            public float normalized => value / max;

            /// <summary>Loads the saved value, or starts at <see cref="starting"/>.</summary>
            public void Load(string key, bool useSave)
            {
                var start = Mathf.Min(starting, max);
                value = useSave ? PlayerPrefs.GetFloat(key, start) : start;
            }

            /// <summary>Sets the value, kept within 0..max.</summary>
            public void Set(float newValue) => value = Mathf.Clamp(newValue, 0f, max);

            /// <summary>The slow drop over time.</summary>
            public void Drain(float deltaTime)
            {
                if (value > 0f && lossPerMinute > 0f)
                    Set(value - lossPerMinute / 60f * deltaTime);
            }

            public void Save(string key) => PlayerPrefs.SetFloat(key, value);
        }

        /// <summary>PlayerPrefs key the hunger is saved under.</summary>
        public const string SaveKey = "FurBrains.Hunger";

        /// <summary>PlayerPrefs key the play need is saved under.</summary>
        public const string PlaySaveKey = "FurBrains.Play";

        [SerializeField]
        [Tooltip("Hunger: filled by feeding.")]
        Need m_Hunger = new();

        [SerializeField]
        [Tooltip("Play: filled by playing minigames.")]
        Need m_Play = new();

        [SerializeField]
        [Tooltip("Remember the needs between app launches.")]
        bool m_SaveProgress = true;

        /// <summary>Current hunger (0 = very hungry, maxHunger = full).</summary>
        public float hunger => m_Hunger.value;

        /// <summary>Hunger value when completely full.</summary>
        public float maxHunger => m_Hunger.max;

        /// <summary>Hunger as 0..1.</summary>
        public float hungerNormalized => m_Hunger.normalized;

        /// <summary>True when the FurBrain can't eat any more.</summary>
        public bool isFull => m_Hunger.value >= m_Hunger.max - 0.01f;

        /// <summary>Current play value (0 = very bored, maxPlay = fully played with).</summary>
        public float play => m_Play.value;

        /// <summary>Play value when completely full.</summary>
        public float maxPlay => m_Play.max;

        /// <summary>Play as 0..1.</summary>
        public float playNormalized => m_Play.normalized;

        /// <summary>How happy the FurBrain is, 0..1: the average of hunger and play.</summary>
        public float happinessNormalized => (hungerNormalized + playNormalized) * 0.5f;

        void Awake()
        {
            m_Hunger.Load(SaveKey, m_SaveProgress);
            m_Play.Load(PlaySaveKey, m_SaveProgress);
        }

        void Update()
        {
            m_Hunger.Drain(Time.deltaTime);
            m_Play.Drain(Time.deltaTime);
        }

        /// <summary>Restores hunger, up to full.</summary>
        public void Feed(float amount)
        {
            if (amount <= 0f)
                return;
            m_Hunger.Set(m_Hunger.value + amount);
            Save();
        }

        /// <summary>Restores play, up to full (MinigameRunner calls it every frame while a minigame is played).</summary>
        /// <param name="amount">How much to add.</param>
        /// <param name="save">Save to the device straight away. Pass false when adding a little every frame (saving
        /// every frame would be slow), and call <see cref="Save"/> when done.</param>
        public void AddPlay(float amount, bool save = true)
        {
            if (amount <= 0f)
                return;
            m_Play.Set(m_Play.value + amount);
            if (save)
                Save();
        }

        /// <summary>
        /// Sets both needs at once, as 0..1 fractions of full (FlutterBridge uses it to load the parent app's
        /// food and fun values for the child who is playing). Saved straight away.
        /// </summary>
        public void SetLevels(float hungerNormalized, float playNormalized)
        {
            m_Hunger.Set(Mathf.Clamp01(hungerNormalized) * m_Hunger.max);
            m_Play.Set(Mathf.Clamp01(playNormalized) * m_Play.max);
            Save();
        }

        /// <summary>Writes both needs to the device.</summary>
        public void Save()
        {
            if (!m_SaveProgress)
                return;
            m_Hunger.Save(SaveKey);
            m_Play.Save(PlaySaveKey);
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

        // Testing aids: right-click the component header in the Inspector (in Play mode).

        [ContextMenu("Reset Needs")]
        void ResetNeeds()
        {
            m_Hunger.Set(Mathf.Min(m_Hunger.starting, m_Hunger.max));
            m_Play.Set(Mathf.Min(m_Play.starting, m_Play.max));
            Save();
        }

        [ContextMenu("Make Hungry")]
        void MakeHungry()
        {
            m_Hunger.Set(m_Hunger.max * 0.1f);
            Save();
        }

        [ContextMenu("Make Bored")]
        void MakeBored()
        {
            m_Play.Set(m_Play.max * 0.1f);
            Save();
        }
    }
}
