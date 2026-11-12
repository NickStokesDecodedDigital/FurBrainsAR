using System.Collections.Generic;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Tilts the FurBrain's neck and head up or down to look at the player's height, on top of whatever animation is
    /// playing. Like a "Look At" / "Transform (Modify) Bone" node in an Unreal Anim Blueprint.
    /// </summary>
    /// <remarks>
    /// Only the vertical angle is handled here: FurBrainPet already turns the whole body to face the player sideways.
    ///
    /// <b>Why LateUpdate:</b> each frame Unity runs Update on scripts, then the Animator poses the skeleton from the
    /// clips, then LateUpdate. Rotating the bones in LateUpdate adds our tilt on top of the animated pose; next frame
    /// the Animator overwrites the bones again, so the tilt never builds up.
    /// </remarks>
    public class FurBrainLookAt : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Bones that share the tilt, from the base of the neck up to the head (UE5 mannequin names).")]
        string[] m_BoneNames = { "neck_01", "neck_02", "head" };

        [SerializeField]
        [Tooltip("Most the head is tilted away from its animated pose, up or down, in degrees.")]
        float m_MaxTilt = 35f;

        [SerializeField]
        [Tooltip("How quickly the head follows. Higher = snappier.")]
        float m_Speed = 4f;

        [SerializeField]
        [Tooltip("Fine-tune in degrees if the eyes still don't quite meet the camera (negative = lower).")]
        float m_AngleOffset;

        readonly List<Transform> m_Bones = new();
        Camera m_Camera;
        float m_TargetPitch;   // smoothed angle to the camera, in degrees; positive = above
        Vector3 m_FacingInHead; // which way the face points, in the head bone's own axes

        void Awake()
        {
            // Find the bones by name anywhere under the pet (all parts share the Body's skeleton).
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var boneName in m_BoneNames)
            foreach (var t in all)
                if (t.name == boneName)
                {
                    m_Bones.Add(t);
                    break;
                }

            if (m_Bones.Count == 0)
            {
                Debug.LogWarning($"[FurBrains] FurBrainLookAt on {name} found none of its bones; it won't do anything.", this);
                return;
            }

            // Bones from Unreal have their own axis conventions, so instead of guessing which local axis is "the face",
            // record it now: before any animation plays, the model is in its rest pose, facing the pet's forward.
            // Inverse(rotation) * worldDirection converts a world direction into the bone's local axes.
            var head = m_Bones[m_Bones.Count - 1];
            m_FacingInHead = Quaternion.Inverse(head.rotation) * transform.forward;
        }

        void LateUpdate()
        {
            if (m_Bones.Count == 0)
                return;
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            // 1. Where we want to look: the angle from the head up (or down) to the camera, eased so it follows
            //    smoothly. 1 - e^(-speed·dt) gives the same feel at any frame rate.
            var head = m_Bones[m_Bones.Count - 1];
            var wantPitch = PitchOf(m_Camera.transform.position - head.position) + m_AngleOffset;
            m_TargetPitch = Mathf.Lerp(m_TargetPitch, wantPitch, 1f - Mathf.Exp(-m_Speed * Time.deltaTime));

            // 2. Where the face points right now, after the animation posed the head (clips can tilt it up or down).
            var currentPitch = PitchOf(head.rotation * m_FacingInHead);

            // 3. Turn by the difference, so the face ends up pointing at the camera whatever the animation did.
            var tilt = Mathf.Clamp(m_TargetPitch - currentPitch, -m_MaxTilt, m_MaxTilt);

            // Rotate around the body's right axis (negative angle = nose up). Each bone gets an equal share; bones are
            // parent → child, so tilting the neck also carries the head, and the head ends up at the full angle.
            var share = Quaternion.AngleAxis(-tilt / m_Bones.Count, transform.right);
            foreach (var bone in m_Bones)
                bone.rotation = share * bone.rotation;
        }

        /// <summary>
        /// Angle of a direction above (+) or below (−) the floor, in degrees: its height along the FurBrain's up axis
        /// against its length along the floor.
        /// </summary>
        float PitchOf(Vector3 direction)
        {
            var height = Vector3.Dot(direction, transform.up);
            var along = Vector3.ProjectOnPlane(direction, transform.up).magnitude;
            return Mathf.Atan2(height, along) * Mathf.Rad2Deg;
        }
    }
}
