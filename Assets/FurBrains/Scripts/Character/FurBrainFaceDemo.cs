using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Debug aid: shows the summoned FurBrain's facial expression blend shapes, either cycling through them
    /// automatically or holding one you pick in the Inspector during Play mode. The current shape's name is shown at
    /// the top of the screen. Remove or disable it when you're done.
    /// </summary>
    /// <remarks>
    /// Put it on any object in the scene (for example "FurBrains Game"). The FurBrain is created at runtime by the
    /// spawner, so instead of a reference set in the Inspector this looks up the current FurBrainFace whenever it
    /// doesn't have one (e.g. after deleting and re-summoning).
    ///
    /// While it runs it switches off the FurBrain's FurBrainMood (which would otherwise keep setting the hunger
    /// expression), and switches it back on when disabled. The Inspector dropdown comes from FurBrainFaceDemoEditor
    /// (Assets/FurBrains/Editor/), a custom Inspector, because the list of shapes only exists at runtime.
    /// </remarks>
    public class FurBrainFaceDemo : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Step through the shapes automatically. Picking a shape in the Inspector switches this off.")]
        bool m_AutoCycle = true;

        [SerializeField]
        [Tooltip("Seconds each expression is held when cycling.")]
        float m_SecondsPerShape = 2f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Strength of the displayed shape.")]
        float m_Weight = 1f;

        [SerializeField]
        [Tooltip("Shapes to skip (blink and the A I U E O visemes are mouth shapes for talking, not emotions).")]
        List<string> m_Skip = new() { "Blink", "A", "I", "U", "E", "O" };

        [SerializeField, HideInInspector]
        string m_Selected;

        FurBrainFace m_Face;
        FurBrainMood m_Mood;
        string[] m_Shapes = System.Array.Empty<string>();
        float m_NextTime;
        GUIStyle m_Style;

        /// <summary>Shapes available on the current FurBrain (empty until one is summoned).</summary>
        public string[] shapes => m_Shapes;

        /// <summary>Shape being displayed. Set it to show a particular one.</summary>
        public string selected
        {
            get => m_Selected;
            set => m_Selected = value;
        }

        /// <summary>Whether shapes are being stepped through automatically.</summary>
        public bool autoCycle
        {
            get => m_AutoCycle;
            set => m_AutoCycle = value;
        }

        void Update()
        {
            if (m_Face == null && !FindFace())
                return;

            if (m_AutoCycle && m_Shapes.Length > 0 && Time.time >= m_NextTime)
            {
                var index = System.Array.IndexOf(m_Shapes, m_Selected);
                m_Selected = m_Shapes[(index + 1) % m_Shapes.Length];
                m_NextTime = Time.time + m_SecondsPerShape;
            }

            // Target every shape each frame: the selected one at the chosen weight, the rest at 0.
            // FurBrainFace eases toward these targets, so switching shapes blends smoothly.
            foreach (var shape in m_Shapes)
                m_Face.SetShape(shape, shape == m_Selected ? m_Weight : 0f);
        }

        bool FindFace()
        {
            m_Face = FindAnyObjectByType<FurBrainFace>();
            if (m_Face == null)
                return false;

            m_Shapes = m_Face.ShapeNames.Where(n => !m_Skip.Contains(n)).OrderBy(n => n).ToArray();
            m_NextTime = 0f;
            Debug.Log($"[FurBrains] Face shapes ({m_Shapes.Length}): {string.Join(", ", m_Shapes)}");

            // Pause the hunger mood so it doesn't fight the demo over the face.
            m_Mood = m_Face.GetComponentInParent<FurBrainMood>();
            if (m_Mood != null)
                m_Mood.enabled = false;
            return true;
        }

        void OnDisable()
        {
            if (m_Face != null)
                foreach (var shape in m_Shapes)
                    m_Face.SetShape(shape, 0f);
            if (m_Mood != null)
                m_Mood.enabled = true;
            m_Face = null;
            m_Mood = null;
        }

        // Immediate-mode debug label (IMGUI): fine for a temporary tool, no Canvas setup needed.
        void OnGUI()
        {
            if (m_Face == null || string.IsNullOrEmpty(m_Selected))
                return;
            m_Style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.04f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = Color.white },
            };
            var index = System.Array.IndexOf(m_Shapes, m_Selected);
            var text = $"{m_Selected}  ({index + 1}/{m_Shapes.Length})";
            GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, Screen.height * 0.1f), text, m_Style);
        }
    }
}
