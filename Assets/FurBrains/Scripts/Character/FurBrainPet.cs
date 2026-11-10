using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FurBrains
{
    /// <summary>
    /// Top-level behaviour of a placed FurBrain: waves when it appears and whenever the player taps it.
    /// </summary>
    /// <remarks>
    /// Tap detection reuses the XR Interaction Toolkit interactable that already handles move / rotate / scale.
    /// XRI doesn't have a separate "tap" event: touching the FurBrain <i>selects</i> it and lifting the finger
    /// <i>deselects</i> it, whether the player tapped or dragged. So we treat a select that was short and didn't move,
    /// rotate or scale the FurBrain as a tap.
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
        [Tooltip("How far (metres) the FurBrain may move during a touch for it to still count as a tap.")]
        float m_MaxTapMovement = 0.02f;

        [SerializeField]
        [Tooltip("How long the happy face lasts after a wave, in seconds.")]
        float m_HappyFaceDuration = 2.5f;

        XRBaseInteractable m_Interactable;
        float m_SelectStartTime;
        Pose m_SelectStartPose;
        Vector3 m_SelectStartScale;
        float m_ClearFaceTime = -1f;

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
        }

        /// <summary>Play the wave animation with a happy face.</summary>
        public void Wave()
        {
            if (m_Animator != null)
                m_Animator.SetTrigger(WaveTrigger);

            if (m_Face != null && m_Face.SetShape("Happy", 1f))
                m_ClearFaceTime = Time.time + m_HappyFaceDuration;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_SelectStartTime = Time.time;
            m_SelectStartPose = new Pose(transform.position, transform.rotation);
            m_SelectStartScale = transform.localScale;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled)
                return;

            var quick = Time.time - m_SelectStartTime <= m_MaxTapDuration;
            var stayedPut = Vector3.Distance(transform.position, m_SelectStartPose.position) <= m_MaxTapMovement
                && Quaternion.Angle(transform.rotation, m_SelectStartPose.rotation) <= 5f
                && (transform.localScale - m_SelectStartScale).sqrMagnitude < 1e-4f;

            if (quick && stayedPut)
                Wave();
        }
    }
}
