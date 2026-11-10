using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FurBrains
{
    /// <summary>
    /// Top-level behaviour of a placed FurBrain: waves when it appears and whenever the player taps it, and slowly
    /// turns to face the player.
    /// </summary>
    /// <remarks>
    /// Tap detection uses the XR Interaction Toolkit interactable on the same object (an XRSimpleInteractable, which
    /// can be selected but never moves the object). XRI doesn't have a separate "tap" event: touching the FurBrain
    /// <i>selects</i> it and lifting the finger <i>deselects</i> it, so we treat a short select as a tap.
    ///
    /// The Animator lives on the character model (a child object); this script only fires its "Wave" trigger.
    /// The Animator Controller decides what that means (see FurBrainsPetBuilder, which generates it).
    /// </remarks>
    [RequireComponent(typeof(XRBaseInteractable))]
    public class FurBrainPet : MonoBehaviour
    {
        /// <summary>Animator trigger parameter that starts the wave.</summary>
        public const string WaveTrigger = "Wave";

        [SerializeField]
        [Tooltip("Animator on the character model. Found in children automatically if left empty.")]
        Animator m_Animator;

        [SerializeField]
        [Tooltip("Face controller on the character model. Found in children automatically if left empty.")]
        FurBrainFace m_Face;

        [SerializeField]
        [Tooltip("Wave as soon as the FurBrain is placed.")]
        bool m_WaveOnSpawn = true;

        [SerializeField]
        [Tooltip("Longest touch, in seconds, that still counts as a tap rather than a drag.")]
        float m_MaxTapDuration = 0.35f;

        [SerializeField]
        [Tooltip("How long the happy face lasts after a wave, in seconds.")]
        float m_HappyFaceDuration = 2.5f;

        [SerializeField]
        [Tooltip("Slowly turn to face the player (the AR camera).")]
        bool m_FacePlayer = true;

        [SerializeField]
        [Tooltip("Turning speed in degrees per second.")]
        float m_TurnSpeed = 45f;

        [SerializeField]
        [Tooltip("Start turning once the player is more than this many degrees off to the side.")]
        float m_StartTurnAngle = 20f;

        [SerializeField]
        [Tooltip("Stop turning once facing the player within this many degrees.")]
        float m_StopTurnAngle = 2f;

        XRBaseInteractable m_Interactable;
        Camera m_Camera;
        float m_SelectStartTime;
        float m_ClearFaceTime = -1f;
        bool m_Turning;

        /// <summary>Animator on the character model.</summary>
        public Animator animator
        {
            get => m_Animator;
            set => m_Animator = value;
        }

        void Awake()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();
            if (m_Face == null)
                m_Face = GetComponentInChildren<FurBrainFace>();
        }

        void OnEnable()
        {
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void Start()
        {
            if (m_WaveOnSpawn)
                Wave();
        }

        void Update()
        {
            if (m_ClearFaceTime >= 0f && Time.time >= m_ClearFaceTime)
            {
                m_ClearFaceTime = -1f;
                if (m_Face != null)
                    m_Face.SetShape("Happy", 0f);
            }

            if (m_FacePlayer)
                TurnTowardPlayer();
        }

        /// <summary>
        /// Turns around the FurBrain's own up axis (the surface normal it was placed on) toward the camera.
        /// </summary>
        /// <remarks>
        /// Uses two thresholds (hysteresis) so it doesn't twitch with every small head movement: it only starts turning
        /// once the player is clearly off to the side, then keeps turning until it's facing them almost exactly.
        /// </remarks>
        void TurnTowardPlayer()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            var up = transform.up;
            var toPlayer = Vector3.ProjectOnPlane(m_Camera.transform.position - transform.position, up);
            if (toPlayer.sqrMagnitude < 1e-6f)
                return; // camera directly above: no meaningful direction

            var angle = Vector3.Angle(transform.forward, toPlayer);
            if (!m_Turning && angle > m_StartTurnAngle)
                m_Turning = true;
            else if (m_Turning && angle < m_StopTurnAngle)
                m_Turning = false;

            if (m_Turning)
            {
                var target = Quaternion.LookRotation(toPlayer, up);
                // RotateTowards moves at most this many degrees this frame: a constant speed, independent of frame rate.
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, m_TurnSpeed * Time.deltaTime);
            }
        }

        /// <summary>Play the wave animation with a happy face.</summary>
        public void Wave()
        {
            if (m_Animator != null)
                m_Animator.SetTrigger(WaveTrigger);

            if (m_Face != null && m_Face.SetShape("Happy", 1f))
                m_ClearFaceTime = Time.time + m_HappyFaceDuration;
        }

        /// <summary>React to being fed. Placeholder (wave + happy face) until there's an eating animation.</summary>
        public void Eat() => Wave();

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_SelectStartTime = Time.time;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (!args.isCanceled && Time.time - m_SelectStartTime <= m_MaxTapDuration)
                Wave();
        }
    }
}
