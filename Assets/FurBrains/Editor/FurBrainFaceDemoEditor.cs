using System;
using UnityEditor;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Custom Inspector for FurBrainFaceDemo: in Play mode, adds a dropdown of the FurBrain's blend shapes plus
    /// previous / next buttons. Picking a shape switches off auto-cycling and holds that shape.
    /// </summary>
    /// <remarks>
    /// A custom Editor replaces how Unity draws a component in the Inspector. <c>DrawDefaultInspector()</c> draws the
    /// normal fields first; we then add our own controls underneath. The shape list only exists at runtime (it's read
    /// from the summoned FurBrain), so the dropdown only appears in Play mode once a FurBrain is summoned.
    /// </remarks>
    [CustomEditor(typeof(FurBrainFaceDemo))]
    public class FurBrainFaceDemoEditor : Editor
    {
        // Keep repainting during Play mode so the dropdown follows auto-cycling.
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var demo = (FurBrainFaceDemo)target;
            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode and summon the FurBrain to pick a shape here.", MessageType.Info);
                return;
            }

            var shapes = demo.shapes;
            if (shapes.Length == 0)
            {
                EditorGUILayout.HelpBox("Summon the FurBrain to see its shapes.", MessageType.Info);
                return;
            }

            var index = Mathf.Max(0, Array.IndexOf(shapes, demo.selected));
            var picked = EditorGUILayout.Popup("Show Shape", index, shapes);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Previous"))
                    picked = (index - 1 + shapes.Length) % shapes.Length;
                if (GUILayout.Button("Next ▶"))
                    picked = (index + 1) % shapes.Length;
            }

            if (picked != index || demo.selected != shapes[picked])
            {
                if (picked != index)
                    demo.autoCycle = false; // a manual pick means "hold this one"
                demo.selected = shapes[picked];
            }
        }
    }
}
