using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace FurBrains
{
    /// <summary>
    /// Allows only one summoned FurBrain at a time. Tap-to-place is switched off while one exists and comes back
    /// when it's gone (deleted with the delete button, or removed by "clear all").
    /// </summary>
    /// <remarks>
    /// The template's tap-to-place chain is: <c>ARInteractorSpawnTrigger</c> (detects a tap on a plane) → UnityEvent
    /// wired in the scene → <c>ObjectSpawner.SpawnObject</c>. Both live in the XRI sample folder, which we don't edit,
    /// so instead of changing them we simply disable the trigger component. A disabled MonoBehaviour gets no
    /// <c>Update()</c> calls, so it stops looking for taps.
    ///
    /// We learn about new FurBrains from the spawner's <c>objectSpawned</c> C# event (a multicast delegate: think a
    /// list of function pointers that <c>+=</c> adds to). We notice deletion by the reference becoming null: Unity's
    /// <c>==</c> on a destroyed object returns true for null, even though the C# wrapper object still exists.
    /// </remarks>
    public class FurBrainSummoner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Spawner that creates the FurBrain. Found in the scene automatically if left empty.")]
        ObjectSpawner m_Spawner;

        [SerializeField]
        [Tooltip("Tap-to-place trigger to switch off while a FurBrain exists. Found in the scene automatically if left empty.")]
        ARInteractorSpawnTrigger m_SpawnTrigger;

        [SerializeField]
        [Tooltip("Prompt to show when the FurBrain is deleted, inviting the player to summon it again " +
                 "(the onboarding's \"Prompt Tap to Place\"). Hidden again once the FurBrain is summoned.")]
        GameObject m_SummonPrompt;

        GameObject m_Summoned;
        bool m_HadSummoned;

        /// <summary>The currently summoned FurBrain, or null if there isn't one.</summary>
        public GameObject summoned => m_Summoned;

        /// <summary>True while a FurBrain is summoned. (Unity's == null also catches a destroyed one.)</summary>
        public bool hasSummoned => m_Summoned != null;

        /// <summary>Prompt shown after the FurBrain is deleted.</summary>
        public GameObject summonPrompt
        {
            get => m_SummonPrompt;
            set => m_SummonPrompt = value;
        }

        void Awake()
        {
            if (m_Spawner == null)
                m_Spawner = FindAnyObjectByType<ObjectSpawner>();
            if (m_SpawnTrigger == null)
                m_SpawnTrigger = FindAnyObjectByType<ARInteractorSpawnTrigger>();
            if (m_Spawner == null || m_SpawnTrigger == null)
                Debug.LogWarning("[FurBrains] FurBrainSummoner couldn't find the ObjectSpawner or ARInteractorSpawnTrigger.", this);
        }

        void OnEnable()
        {
            if (m_Spawner != null)
                m_Spawner.objectSpawned += OnObjectSpawned;
        }

        void OnDisable()
        {
            if (m_Spawner != null)
                m_Spawner.objectSpawned -= OnObjectSpawned;
        }

        void Update()
        {
            // Destroyed objects compare equal to null in Unity, so this notices deletion.
            var canSummon = m_Summoned == null;
            if (m_SpawnTrigger != null && m_SpawnTrigger.enabled != canSummon)
                m_SpawnTrigger.enabled = canSummon;

            // Just deleted: invite the player to summon again. (The first summon's prompt is the onboarding's job.)
            if (m_HadSummoned && canSummon)
            {
                m_HadSummoned = false;
                if (m_SummonPrompt != null)
                    m_SummonPrompt.SetActive(true);
            }
        }

        void OnObjectSpawned(GameObject spawned)
        {
            m_Summoned = spawned;
            m_HadSummoned = true;
            if (m_SpawnTrigger != null)
                m_SpawnTrigger.enabled = false;
            if (m_SummonPrompt != null)
                m_SummonPrompt.SetActive(false);
        }
    }
}
