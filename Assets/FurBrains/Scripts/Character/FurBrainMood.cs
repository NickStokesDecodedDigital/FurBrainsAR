using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Decides the FurBrain's facial expression: a base mood from its hunger, plus short reactions (e.g. a happy face
    /// while waving) that temporarily take over.
    /// </summary>
    /// <remarks>
    /// <b>Why one owner for the face:</b> several systems want to set expressions (hunger, waving, later minigames).
    /// If each called FurBrainFace.SetShape directly they'd undo each other, e.g. the wave's "turn Happy off afterwards"
    /// would wipe a happy mood. So they all go through this component, which works out the single expression to show
    /// and sends it to FurBrainFace (which does the easing and blinking).
    ///
    /// <b>Moods</b> are bands of the hunger bar (in percent). The band with the highest Min Hunger % at or below the
    /// current hunger wins. Each band lists the blend shapes to turn on; an empty list is the plain, neutral face.
    ///
    /// Hunger lives in the scene (FurBrainNeeds), while this component is on the spawned FurBrain, so it finds
    /// FurBrainNeeds at runtime.
    /// </remarks>
    public class FurBrainMood : MonoBehaviour
    {
        /// <summary>One expression band of the hunger bar.</summary>
        [Serializable]
        public class MoodBand
        {
            [Tooltip("Just a label for the Inspector.")]
            public string name;

            [Range(0f, 100f)]
            [Tooltip("This mood applies from this hunger percentage upward (until the next band starts).")]
            public float minHungerPercent;

            [Tooltip("Blend shapes to show at full strength. Empty = neutral face.")]
            public string[] shapes = Array.Empty<string>();
        }

        [SerializeField]
        [Tooltip("Expression for each part of the hunger bar.")]
        List<MoodBand> m_Bands = new()
        {
            new MoodBand { name = "Sad", minHungerPercent = 0f, shapes = new[] { "Sadness", "SadEar" } },
            new MoodBand { name = "Neutral", minHungerPercent = 30f },
            new MoodBand { name = "Very happy", minHungerPercent = 60f, shapes = new[] { "Empowered" } },
        };

        [SerializeField]
        [Tooltip("Face controller. Found in children automatically if left empty.")]
        FurBrainFace m_Face;

        [SerializeField]
        [Tooltip("Hunger source. Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        // Every shape this component has ever turned on, so it can turn them off again when the expression changes.
        readonly HashSet<string> m_ManagedShapes = new();
        readonly HashSet<string> m_Wanted = new();

        string[] m_ReactionShapes;
        float m_ReactionEndTime = -1f;

        /// <summary>The hunger band currently shown (null if there's no hunger source).</summary>
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

        void Update()
        {
            if (m_Face == null)
                return;

            m_Wanted.Clear();
            if (Time.time < m_ReactionEndTime && m_ReactionShapes != null)
            {
                m_Wanted.UnionWith(m_ReactionShapes);
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
            var percent = m_Needs.hungerNormalized * 100f;
            return m_Bands.Where(b => b.minHungerPercent <= percent)
                          .OrderByDescending(b => b.minHungerPercent)
                          .FirstOrDefault();
        }
    }
}
