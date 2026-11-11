using System.Collections.Generic;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Controls a FurBrain's facial blend shapes by name, across every mesh part of the character.
    /// </summary>
    /// <remarks>
    /// FurBrains characters are split into parts (Body, Mouth, Eyebrows, Helmet, ...) and the same shape name can exist
    /// on several of them: e.g. "Happy" is on both Body and Mouth, "Calm" is on Body, Mouth, Eyebrows and Helmet.
    /// Setting a shape here sets it on every part that has it, so the face stays in sync.
    ///
    /// Weights are 0..1 (Unity's API uses 0..100 internally). Shapes ease toward their target rather than snapping.
    /// Applied in LateUpdate, i.e. after the Animator, so code-driven expressions win over animation clips.
    /// </remarks>
    public class FurBrainFace : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("How quickly shapes move toward their target weight (weight units per second).")]
        float m_BlendSpeed = 6f;

        [Header("Blinking")]
        [SerializeField]
        [Tooltip("Blink automatically at random intervals. Makes the character look alive.")]
        bool m_AutoBlink = true;

        [SerializeField]
        [Tooltip("Random time range between blinks, in seconds.")]
        Vector2 m_BlinkInterval = new Vector2(2f, 6f);

        [SerializeField]
        [Tooltip("How long one blink takes (close + open), in seconds.")]
        float m_BlinkDuration = 0.18f;

        const string k_BlinkShape = "Blink";

        // name -> every (renderer, shape index) pair that has a shape with that name.
        // Names are matched ignoring upper/lower case, so "empowered" finds "Empowered" (names typed in the Inspector).
        readonly Dictionary<string, List<(SkinnedMeshRenderer renderer, int index)>> m_Shapes = new(System.StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> m_Current = new(System.StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> m_Target = new(System.StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> m_WarnedUnknown = new();

        float m_NextBlinkTime;
        float m_BlinkStartTime = -1f;

        /// <summary>All blend shape names found on this character.</summary>
        public IReadOnlyCollection<string> ShapeNames => m_Shapes.Keys;

        void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                    continue;

                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var shapeName = mesh.GetBlendShapeName(i);
                    if (!m_Shapes.TryGetValue(shapeName, out var list))
                    {
                        list = new List<(SkinnedMeshRenderer, int)>();
                        m_Shapes[shapeName] = list;
                        m_Current[shapeName] = 0f;
                        m_Target[shapeName] = 0f;
                    }
                    list.Add((renderer, i));
                }
            }

            ScheduleNextBlink();
        }

        /// <summary>Ease a shape toward <paramref name="weight"/> (0..1). Returns false if no part has that shape.</summary>
        public bool SetShape(string shapeName, float weight)
        {
            if (string.IsNullOrEmpty(shapeName) || !m_Shapes.ContainsKey(shapeName))
            {
                // Say so once per name, rather than silently doing nothing (e.g. a typo in an Inspector field).
                if (weight > 0f && shapeName != null && m_WarnedUnknown.Add(shapeName))
                    Debug.LogWarning($"[FurBrains] {name} has no blend shape called \"{shapeName}\". " +
                                     $"Available: {string.Join(", ", m_Shapes.Keys)}", this);
                return false;
            }
            m_Target[shapeName] = Mathf.Clamp01(weight);
            return true;
        }

        /// <summary>Set a shape immediately, with no easing.</summary>
        public bool SetShapeImmediate(string shapeName, float weight)
        {
            if (!SetShape(shapeName, weight))
                return false;
            m_Current[shapeName] = m_Target[shapeName];
            return true;
        }

        /// <summary>Ease every shape back to neutral (except blinking, which keeps running).</summary>
        public void ClearAll()
        {
            foreach (var shapeName in m_Shapes.Keys)
                m_Target[shapeName] = 0f;
        }

        /// <summary>Trigger a blink now.</summary>
        public void Blink() => m_BlinkStartTime = Time.time;

        void LateUpdate()
        {
            if (m_AutoBlink && Time.time >= m_NextBlinkTime)
            {
                Blink();
                ScheduleNextBlink();
            }

            var step = m_BlendSpeed * Time.deltaTime;
            foreach (var (shapeName, targets) in m_Shapes)
            {
                var weight = m_Current[shapeName] = Mathf.MoveTowards(m_Current[shapeName], m_Target[shapeName], step);

                if (shapeName == k_BlinkShape && m_BlinkStartTime >= 0f)
                    weight = Mathf.Max(weight, BlinkWeight());

                foreach (var (renderer, index) in targets)
                    renderer.SetBlendShapeWeight(index, weight * 100f);
            }
        }

        // 0 -> 1 -> 0 over the blink duration (a triangle wave).
        float BlinkWeight()
        {
            var t = (Time.time - m_BlinkStartTime) / m_BlinkDuration;
            if (t >= 1f)
            {
                m_BlinkStartTime = -1f;
                return 0f;
            }
            return 1f - Mathf.Abs(t * 2f - 1f);
        }

        void ScheduleNextBlink() => m_NextBlinkTime = Time.time + Random.Range(m_BlinkInterval.x, m_BlinkInterval.y);
    }
}
