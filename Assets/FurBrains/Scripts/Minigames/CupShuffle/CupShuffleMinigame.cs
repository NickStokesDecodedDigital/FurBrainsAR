using System.Collections.Generic;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Cup Shuffle: a treat is hidden under one of three cups in front of the FurBrain, the cups are shuffled, and the
    /// player taps the cup they think hides it. A won-or-lost game: picking right wins; picking wrong still shows where
    /// the treat was and pays the small consolation reward.
    /// </summary>
    /// <remarks>
    /// Small state machine (an enum + switch, like Beach Ball):
    /// Show (treat visible) → Cover (cup lowers) → Shuffle (pairs of cups swap places) → Pick (wait for a tap)
    /// → Reveal (lift the chosen cup, and the right one if different) → finish.
    ///
    /// The cups are laid out from this game object's transform, which the runner places at the FurBrain's feet facing
    /// the same way as the FurBrain (toward the player): forward = toward the player, right = sideways.
    /// </remarks>
    public class CupShuffleMinigame : Minigame
    {
        enum Phase { Show, Cover, Shuffle, Pick, Reveal }

        [SerializeField]
        [Tooltip("Hidden template cup (a child of this prefab) that the cups are copied from.")]
        GameObject m_CupTemplate;

        [SerializeField]
        [Tooltip("The treat hidden under a cup (a child of this prefab).")]
        Transform m_Treat;

        [Header("Layout")]
        [SerializeField, Min(2)]
        [Tooltip("Number of cups.")]
        int m_CupCount = 3;

        [SerializeField]
        [Tooltip("Distance between neighbouring cups, in metres.")]
        float m_Spacing = 0.15f;

        [SerializeField]
        [Tooltip("How far in front of the FurBrain (toward the player) the cups stand, in metres.")]
        float m_DistanceInFront = 0.25f;

        [SerializeField]
        [Tooltip("How high a cup lifts to show what's underneath, in metres.")]
        float m_LiftHeight = 0.1f;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds the treat is shown before it's covered.")]
        float m_ShowTime = 1.5f;

        [SerializeField]
        [Tooltip("Seconds to lift or lower a cup.")]
        float m_LiftTime = 0.4f;

        [SerializeField]
        [Tooltip("Number of swaps.")]
        int m_Swaps = 5;

        [SerializeField]
        [Tooltip("Seconds per swap. Lower = harder.")]
        float m_SwapTime = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the answer is shown before the results.")]
        float m_RevealTime = 1.5f;

        [Header("Tapping")]
        [SerializeField]
        [Tooltip("Taps count if within this many times the cup's on-screen radius of its centre.")]
        float m_TapRadiusMultiplier = 1.3f;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("...or within this fraction of the screen width, whichever is bigger.")]
        float m_MinTapRadiusFraction = 0.06f;

        readonly List<Transform> m_Cups = new();
        int[] m_SlotOfCup;   // which slot (0 = left) each cup is standing in
        int m_TreatCup;      // which cup hides the treat
        float m_CupHalfHeight;

        Phase m_Phase;
        float m_PhaseStart;
        int m_SwapsDone;
        int m_SwapA, m_SwapB; // the two cups swapping right now
        int m_PickedCup;

        // Up to two cups can be lifted at once (the picked one and, if different, the one hiding the treat).
        int m_LiftedA = -1, m_LiftedB = -1;
        float m_LiftAmount; // metres

        public override MinigameResultKind resultKind => MinigameResultKind.Outcome;

        protected override void OnBegin()
        {
            if (m_CupTemplate == null || m_Treat == null)
            {
                Debug.LogError("[FurBrains] Cup Shuffle is missing its cup template or treat.", this);
                FinishWithOutcome(false);
                return;
            }

            // Cylinders are 2 units tall at scale 1, so half the height is the cup's Y scale.
            m_CupHalfHeight = m_CupTemplate.transform.localScale.y;
            m_CupTemplate.SetActive(false);

            m_SlotOfCup = new int[m_CupCount];
            for (var i = 0; i < m_CupCount; i++)
            {
                var cup = Instantiate(m_CupTemplate, transform);
                cup.name = $"Cup {i + 1}";
                cup.SetActive(true);
                m_Cups.Add(cup.transform);
                m_SlotOfCup[i] = i;
            }

            m_TreatCup = m_CupCount / 2; // starts under the middle cup, so the child sees it clearly
            StartPhase(Phase.Show);
        }

        void Update()
        {
            if (!isRunning)
                return;

            var elapsed = Time.time - m_PhaseStart;
            switch (m_Phase)
            {
                case Phase.Show:
                    PlaceAllCups(liftedCup: m_TreatCup, lift: 1f);
                    if (elapsed >= m_ShowTime)
                        StartPhase(Phase.Cover);
                    break;

                case Phase.Cover:
                    PlaceAllCups(liftedCup: m_TreatCup, lift: 1f - Mathf.Clamp01(elapsed / m_LiftTime));
                    if (elapsed >= m_LiftTime)
                        StartNextSwap();
                    break;

                case Phase.Shuffle:
                    UpdateSwap(Mathf.Clamp01(elapsed / m_SwapTime));
                    if (elapsed >= m_SwapTime)
                    {
                        // Finish this swap: the two cups trade slots.
                        (m_SlotOfCup[m_SwapA], m_SlotOfCup[m_SwapB]) = (m_SlotOfCup[m_SwapB], m_SlotOfCup[m_SwapA]);
                        m_SwapsDone++;
                        StartNextSwap();
                    }
                    break;

                case Phase.Pick:
                    PlaceAllCups(liftedCup: -1, lift: 0f);
                    CheckPick();
                    break;

                case Phase.Reveal:
                    var lift = Mathf.Clamp01(elapsed / m_LiftTime);
                    PlaceAllCups(m_PickedCup, lift, alsoLifted: m_TreatCup);
                    if (elapsed >= m_LiftTime + m_RevealTime)
                        FinishWithOutcome(m_PickedCup == m_TreatCup);
                    break;
            }

            // The treat sits on the floor under its cup (so it follows the cup around while shuffling).
            var cupBase = m_Cups[m_TreatCup].position - transform.up * (m_CupHalfHeight + LiftOf(m_TreatCup));
            m_Treat.position = cupBase + transform.up * (m_Treat.lossyScale.y * 0.5f);
        }

        void StartPhase(Phase phase)
        {
            m_Phase = phase;
            m_PhaseStart = Time.time;
        }

        void StartNextSwap()
        {
            if (m_SwapsDone >= m_Swaps)
            {
                StartPhase(Phase.Pick);
                // The FurBrain gives an encouraging gesture: "your turn!"
                if (context.pet != null)
                    context.pet.OnTapped();
                return;
            }

            // Two different random cups.
            m_SwapA = Random.Range(0, m_CupCount);
            m_SwapB = (m_SwapA + Random.Range(1, m_CupCount)) % m_CupCount;
            StartPhase(Phase.Shuffle);
        }

        /// <summary>Moves the two swapping cups around each other on half-circles, so they don't pass through each other.</summary>
        void UpdateSwap(float t)
        {
            var from = SlotPosition(m_SlotOfCup[m_SwapA]);
            var to = SlotPosition(m_SlotOfCup[m_SwapB]);
            var middle = (from + to) * 0.5f;
            var radius = Vector3.Distance(from, to) * 0.5f;

            // Smooth start and stop. Angle goes 0 → 180°: cos moves the cup across, sin swings it forward or back.
            var angle = Mathf.SmoothStep(0f, 1f, t) * Mathf.PI;
            var across = (from - middle) * Mathf.Cos(angle);
            var swing = transform.forward * (Mathf.Sin(angle) * radius);
            m_Cups[m_SwapA].position = middle + across + swing; // cup A passes in front...
            m_Cups[m_SwapB].position = middle - across - swing; // ...cup B behind

            for (var i = 0; i < m_CupCount; i++)
                if (i != m_SwapA && i != m_SwapB)
                    m_Cups[i].position = SlotPosition(m_SlotOfCup[i]);
        }

        void CheckPick()
        {
            if (!TryGetTap(out var tap))
                return;
            var radius = m_CupTemplate.transform.localScale.x * 0.5f;
            for (var i = 0; i < m_CupCount; i++)
            {
                if (!IsTapNear(tap, m_Cups[i].position, radius, m_TapRadiusMultiplier, m_MinTapRadiusFraction))
                    continue;
                m_PickedCup = i;
                StartPhase(Phase.Reveal);
                return;
            }
        }

        // ---------- Positions ----------

        /// <summary>Resting position of a slot: a row across in front of the FurBrain, standing on the floor.</summary>
        Vector3 SlotPosition(int slot)
        {
            var offset = (slot - (m_CupCount - 1) * 0.5f) * m_Spacing; // centred: -1, 0, 1 × spacing for 3 cups
            return transform.position
                   + transform.forward * m_DistanceInFront
                   + transform.right * offset
                   + transform.up * m_CupHalfHeight;
        }

        /// <summary>How far a cup is lifted right now, in metres (0 unless it's one of the lifted cups).</summary>
        float LiftOf(int cup) => cup == m_LiftedA || cup == m_LiftedB ? m_LiftAmount : 0f;

        /// <summary>Puts every cup in its slot, with up to two cups lifted by <paramref name="lift"/> (0..1).</summary>
        void PlaceAllCups(int liftedCup, float lift, int alsoLifted = -1)
        {
            m_LiftedA = liftedCup;
            m_LiftedB = alsoLifted;
            m_LiftAmount = Mathf.SmoothStep(0f, 1f, lift) * m_LiftHeight;
            for (var i = 0; i < m_CupCount; i++)
                m_Cups[i].position = SlotPosition(m_SlotOfCup[i]) + transform.up * LiftOf(i);
        }
    }
}
