using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FurBrains
{
    /// <summary>
    /// Starts and ends minigames and feeds their results back into the pet game (coins, Ziara's reaction).
    /// Lives on the scene's "FurBrains Game" object.
    /// </summary>
    /// <remarks>
    /// The runner only deals with the <see cref="Minigame"/> base class, so it works for every game: it creates the
    /// definition's prefab at the FurBrain, calls <c>Begin</c>, waits for the game's <c>finished</c> event, pays out
    /// coins by the definition's rule, and destroys the game. UI listens to <see cref="started"/> / <see cref="ended"/>
    /// (MinigameHud shows the exit button and results; the menus hide while a game runs).
    /// </remarks>
    public class MinigameRunner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainSummoner m_Summoner;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        PlayerWallet m_Wallet;

        [SerializeField]
        [Tooltip("The FurBrain's needs (Play goes up after a game). Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        [SerializeField]
        [Tooltip("Played when any minigame ends (as the results appear). An Audio Clip, or an Audio Random " +
                 "Container for variety.")]
        AudioResource m_CompleteSound; // AudioResource is the base class of both AudioClip and AudioRandomContainer

        [SerializeField, Min(0)]
        [Tooltip("How much the FurBrain's Play need rises per second while any minigame is being played (in the " +
                 "same units as Furbrain Needs → Play → Max, 100 by default). 1.5 = a 30-second game adds 45.")]
        float m_PlayPerSecond = 1.5f;

        // Plays the complete sound. It lives here (on the always-active FurBrains Game object), not on the game,
        // because the game object is destroyed the moment it finishes, which would cut its sounds off.
        AudioSource m_Audio;

        MinigameDefinition m_Definition;
        Minigame m_Current;

        /// <summary>Raised when a game starts.</summary>
        public event Action<MinigameDefinition, Minigame> started;

        /// <summary>Raised when a game ends, with its result and the coins it paid.</summary>
        public event Action<MinigameDefinition, MinigameResult, int> ended;

        /// <summary>Whether a minigame is being played.</summary>
        public bool isPlaying => m_Current != null;

        /// <summary>The game being played, or null.</summary>
        public Minigame current => m_Current;

        /// <summary>Whether a game can start right now (a FurBrain is summoned and nothing else is playing).</summary>
        public bool canPlay => !isPlaying && m_Summoner != null && m_Summoner.hasSummoned;

        void Awake()
        {
            if (m_Summoner == null) m_Summoner = FindAnyObjectByType<FurBrainSummoner>();
            if (m_Wallet == null) m_Wallet = FindAnyObjectByType<PlayerWallet>();
            if (m_Needs == null) m_Needs = FindAnyObjectByType<FurBrainNeeds>();

            // FurBrains Game may not have an AudioSource yet: add one if it's missing.
            m_Audio = GetComponent<AudioSource>();
            if (m_Audio == null)
            {
                m_Audio = gameObject.AddComponent<AudioSource>();
                m_Audio.playOnAwake = false;
                m_Audio.spatialBlend = 0f; // 2D: the same volume wherever the phone is
            }
        }

        /// <summary>Starts a minigame. Returns false if it can't start right now.</summary>
        public bool Play(MinigameDefinition definition)
        {
            if (!canPlay || definition == null || definition.prefab == null)
                return false;

            var petObject = m_Summoner.summoned;
            var petTransform = petObject.transform;

            m_Definition = definition;
            // Instantiate with a component reference returns the clone's matching component (like a typed copy).
            m_Current = Instantiate(definition.prefab, petTransform.position, petTransform.rotation);
            m_Current.name = definition.name;
            m_Current.finished += OnFinished;

            started?.Invoke(definition, m_Current);
            m_Current.Begin(new MinigameContext
            {
                pet = petObject.GetComponent<FurBrainPet>(),
                petTransform = petTransform,
                camera = Camera.main,
            });
            return true;
        }

        /// <summary>Ends the current game early (the exit button). The game decides what that's worth.</summary>
        public void Quit()
        {
            if (m_Current != null)
                m_Current.Cancel();
        }

        void Update()
        {
            // If the FurBrain disappears mid-game (shouldn't happen: the delete button is hidden), stop cleanly.
            if (m_Current != null && (m_Summoner == null || !m_Summoner.hasSummoned))
                Quit();

            // Playing together fills the Play need, steadily, for as long as a game runs (whatever the result:
            // no punishing fail states). × Time.deltaTime turns "per second" into "this frame's share", so the rate
            // is the same at any frame rate. Not saved every frame: saved once when the game ends.
            if (isPlaying && m_Needs != null)
                m_Needs.AddPlay(m_PlayPerSecond * Time.deltaTime, save: false);
        }

        void OnFinished(MinigameResult result)
        {
            var definition = m_Definition;
            var game = m_Current;
            game.finished -= OnFinished;
            m_Current = null;
            m_Definition = null;

            var coins = definition.CoinsFor(result);
            if (m_Wallet != null)
                m_Wallet.Add(coins);

            // Play went up during the game (see Update); save it now that the game is over.
            if (m_Needs != null)
                m_Needs.Save();

            PlayCompleteSound();

            // Ziara reacts: a celebration for a good result, a friendly gesture otherwise (never a sad punishment).
            if (m_Summoner != null && m_Summoner.hasSummoned &&
                m_Summoner.summoned.TryGetComponent<FurBrainPet>(out var pet))
            {
                if (result.isGood)
                    pet.Celebrate();
                else
                    pet.OnTapped();
            }

            // Destroy is deferred to the end of the frame (unlike C++ delete), so the game finishing its own Update
            // after calling Finish is safe.
            Destroy(game.gameObject);
            ended?.Invoke(definition, result, coins);
        }

        void PlayCompleteSound()
        {
            // "is" checks the real type and gives a typed variable if it matches (like a C++ dynamic_cast + null check).
            if (m_CompleteSound is AudioClip clip)
            {
                m_Audio.PlayOneShot(clip);
            }
            else if (m_CompleteSound != null)
            {
                // A container (PlayOneShot only accepts clips): set it as the source's resource and Play. This
                // AudioSource only plays the complete sound, so restarting it never cuts anything else off.
                m_Audio.resource = m_CompleteSound;
                m_Audio.Play();
            }
        }

        // Testing aids: right-click the component header in the Inspector during a game.
        [ContextMenu("Test: End Current Game As Win")]
        void TestWin() { if (m_Current != null) m_Current.ForceEnd(MinigameResult.FromOutcome(true)); }

        [ContextMenu("Test: End Current Game As Loss")]
        void TestLoss() { if (m_Current != null) m_Current.ForceEnd(MinigameResult.FromOutcome(false)); }
    }
}
