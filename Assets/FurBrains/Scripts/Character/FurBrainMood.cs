using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace FurBrains
{
    /// <summary>
    /// Decides the FurBrain's facial expression: a base mood from its happiness, plus short reactions (e.g. a happy face
    /// while waving) that temporarily take over.
    /// </summary>
    /// <remarks>
    /// <b>Why one owner for the face:</b> several systems want to set expressions (hunger, waving, later minigames).
    /// If each called FurBrainFace.SetShape directly they'd undo each other, e.g. the wave's "turn Happy off afterwards"
    /// would wipe a happy mood. So they all go through this component, which works out the single expression to show
    /// and sends it to FurBrainFace (which does the easing and blinking).
    ///
    /// <b>Moods</b> are bands of happiness (in percent), where happiness is the average of the Hunger and Play needs
    /// (FurBrainNeeds.happinessNormalized). The band with the highest Min Happiness % at or below the current happiness
    /// wins. Each band lists the blend shapes to turn on; an empty list is the plain, neutral face.
    ///
    /// The needs live in the scene (FurBrainNeeds), while this component is on the spawned FurBrain, so it finds
    /// FurBrainNeeds at runtime.
    /// </remarks>
    public class FurBrainMood : MonoBehaviour
    {
        /// <summary>One expression band of happiness.</summary>
        [Serializable]
        public class MoodBand
        {
            [Tooltip("Just a label for the Inspector.")]
            public string name;

            // FormerlySerializedAs: this field used to be called minHungerPercent. The attribute tells Unity to read
            // the old name from existing prefabs, so values already set in the Inspector aren't lost by the rename.
            [Range(0f, 100f)]
            [FormerlySerializedAs("minHungerPercent")]
            [Tooltip("This mood applies from this happiness percentage upward (until the next band starts).")]
            public float minHappinessPercent;

            [Tooltip("Blend shapes to show at full strength. Empty = neutral face.")]
            public string[] shapes = Array.Empty<string>();
        }

        [SerializeField]
        [Tooltip("Expression for each band of happiness (the average of hunger and play).")]
        List<MoodBand> m_Bands = new()
        {
            new MoodBand { name = "Sad", minHappinessPercent = 0f, shapes = new[] { "Sadness", "SadEar" } },
            new MoodBand { name = "Neutral", minHappinessPercent = 30f },
            new MoodBand { name = "Very happy", minHappinessPercent = 60f, shapes = new[] { "Empowered" } },
        };

        [SerializeField]
        [Tooltip("Face controller. Found in children automatically if left empty.")]
        FurBrainFace m_Face;

        [SerializeField]
        [Tooltip("The needs (happiness comes from hunger and play). Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        // Every shape this component has ever turned on, so it can turn them off again when the expression changes.
        readonly HashSet<string> m_ManagedShapes = new();
        readonly HashSet<string> m_Wanted = new();

        string[] m_ReactionShapes;
        float m_ReactionEndTime = -1f;
        string[] m_HeldShapes; // shown instead of the mood until released (null = nothing held)

        /// <summary>The happiness band currently shown (null if there's no needs source).</summary>
        public MoodBand currentBand { get; private set; }

        void Awake()
        {
            if (m_Face == null)
                m_Face = GetComponentInChildren<FurBrainFace>();
            if (m_Needs == null)
                m_Needs = FindAnyObjectByType<FurBrainNeeds>();

            foreach (var band in m_Bands)
                m_ManagedShapes.UnionWith(band.shapes);
        }

        /// <summary>Shows these shapes for a while instead of the mood (e.g. Happy while waving).</summary>
        public void React(float seconds, params string[] shapes)
        {
            m_ReactionShapes = shapes;
            m_ReactionEndTime = Time.time + seconds;
            m_ManagedShapes.UnionWith(shapes);
        }

        /// <summary>
        /// Shows these shapes instead of the mood until <see cref="ReleaseHold"/> is called, e.g. an "O" mouth for
        /// the whole of a bubble-blowing game. Short reactions (<see cref="React"/>) still play over it, then the
        /// held face comes back.
        /// </summary>
        public void Hold(params string[] shapes)
        {
            m_HeldShapes = shapes;
            m_ManagedShapes.UnionWith(shapes);
        }

        /// <summary>Stops holding a face; the mood takes over again.</summary>
        public void ReleaseHold() => m_HeldShapes = null;

        void Update()
        {
            if (m_Face == null)
                return;

            // Three layers, highest priority first: a short reaction, then a held face, then the happiness mood.
            m_Wanted.Clear();
            if (Time.time < m_ReactionEndTime && m_ReactionShapes != null)
            {
                m_Wanted.UnionWith(m_ReactionShapes);
            }
            else if (m_HeldShapes != null)
            {
                m_Wanted.UnionWith(m_HeldShapes);
            }
            else
            {
                currentBand = PickBand();
                if (currentBand != null)
                    m_Wanted.UnionWith(currentBand.shapes);
            }

            // Setting the same target every frame is cheap: FurBrainFace just stores it and eases toward it.
            foreach (var shape in m_ManagedShapes)
                m_Face.SetShape(shape, m_Wanted.Contains(shape) ? 1f : 0f);
        }

        MoodBand PickBand()
        {
            if (m_Needs == null)
                return null;
            var percent = m_Needs.happinessNormalized * 100f;
            return m_Bands.Where(b => b.minHappinessPercent <= percent)
                          .OrderByDescending(b => b.minHappinessPercent)
                          .FirstOrDefault();
        }
    }
}
