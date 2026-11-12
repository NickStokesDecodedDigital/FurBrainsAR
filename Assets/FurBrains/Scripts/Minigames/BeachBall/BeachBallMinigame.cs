using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Beach Ball Rally: the player and the FurBrain hit a beach ball back and forth. Tap the ball as it comes at you
    /// to send it back; each of your hits adds one to the rally. Let it get past you (or hit the floor) and the game
    /// ends with the rally as the score.
    /// </summary>
    /// <remarks>
    /// The ball follows scripted arcs instead of physics, so its path is predictable and fair for young children.
    /// Each arc is fixed when the ball is launched (it doesn't chase the phone). Toward the player it's aimed just
    /// below the camera and simply keeps flying along the same curve; once it's behind the camera, the player missed.
    /// It can only be hit when it's close (Hit Distance). Difficulty ramps gently: flights get quicker as the rally grows.
    ///
    /// Small state machine (an enum + switch, like you'd write in C++):
    /// ToPlayer → (hit) → ToPet → (FurBrain hits it) → ToPlayer ...; a miss during ToPlayer ends the game.
    /// </remarks>
    public class BeachBallMinigame : Minigame
    {
        enum Phase { ToPlayer, ToPet }

        [SerializeField]
        [Tooltip("The ball object (a child of this prefab).")]
        Transform m_Ball;

        [Header("Feel")]
        [SerializeField]
        [Tooltip("Seconds for the ball to reach the player at the start of a rally.")]
        float m_StartFlightTime = 1.8f;

        [SerializeField]
        [Tooltip("Fastest flight time, reached as the rally grows.")]
        float m_MinFlightTime = 1.0f;

        [SerializeField]
        [Tooltip("Rally length at which the fastest flight time is reached.")]
        int m_RallyForMaxSpeed = 15;

        [SerializeField]
        [Tooltip("Height of each arc above the straight line, in metres.")]
        float m_ArcHeight = 0.25f;

        [Header("Player side")]
        [SerializeField]
        [Tooltip("The ball is aimed this far below the camera, in metres (roughly chest height when holding a phone).")]
        float m_AimBelowCamera = 0.1f;

        [SerializeField]
        [Tooltip("The ball can only be hit when it's closer to the camera than this, in metres.")]
        float m_HitDistance = 0.6f;

        [SerializeField]
        [Tooltip("Taps count if within this many times the ball's on-screen radius of its centre.")]
        float m_TapRadiusMultiplier = 1.5f;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("...or within this fraction of the screen width, whichever is bigger (generous for small hands).")]
        float m_MinTapRadiusFraction = 0.1f;

        [Header("FurBrain side")]
        [SerializeField]
        [Tooltip("Where the FurBrain hits the ball, as a fraction of its height (1 = top of its head).")]
        float m_PetHeadHeight = 0.9f;

        Phase m_Phase;
        Vector3 m_From;
        Vector3 m_To;
        float m_PhaseStart;
        float m_PhaseDuration;
        int m_Rally;

        public override MinigameResultKind resultKind => MinigameResultKind.Score;

        protected override void OnBegin()
        {
            m_Rally = 0;
            currentScore = 0;
            m_Ball.position = PetPoint();
            // A short pause, then the FurBrain serves.
            Launch(Phase.ToPlayer, PetPoint(), PlayerTarget(), FlightTime() + 0.5f);
        }

        void Update()
        {
            if (!isRunning)
                return;

            // Unclamped: past t = 1 the ball keeps following the same curve (and the arc term turns downward).
            var t = (Time.time - m_PhaseStart) / m_PhaseDuration;
            switch (m_Phase)
            {
                case Phase.ToPlayer:
                    m_Ball.position = Arc(m_From, m_To, t);
                    if (TryHit())
                        break;
                    // A miss: it got past the player, or landed on the floor first. The game ends with the rally.
                    if (IsBehindCamera() || IsOnFloor())
                        FinishWithScore(m_Rally);
                    break;

                case Phase.ToPet:
                    m_Ball.position = Arc(m_From, m_To, Mathf.Min(t, 1f));
                    if (t >= 1f)
                    {
                        // The FurBrain hits it back with its head.
                        if (context.pet != null)
                            context.pet.OnTapped();
                        Launch(Phase.ToPlayer, m_To, PlayerTarget(), FlightTime());
                    }
                    break;
            }

            // A lazy spin so it reads as a beach ball.
            m_Ball.Rotate(Vector3.right, 180f * Time.deltaTime, Space.Self);
        }

        /// <summary>If the player tapped the ball while it's close enough, send it back to the FurBrain.</summary>
        bool TryHit()
        {
            var cam = context.camera.transform;
            if (Vector3.Distance(m_Ball.position, cam.position) > m_HitDistance)
                return false; // still too far away
            // WasTapped (from the Minigame base class) checks for a tap near the ball on screen, ignoring UI taps.
            if (!WasTapped(m_Ball, m_Ball.lossyScale.x * 0.5f, m_TapRadiusMultiplier, m_MinTapRadiusFraction))
                return false;

            m_Rally++;
            currentScore = m_Rally;
            Launch(Phase.ToPet, m_Ball.position, PetPoint(), FlightTime());
            return true;
        }

        // Behind the plane through the camera facing its view direction (dot product < 0), i.e. the ball got past.
        bool IsBehindCamera()
        {
            var cam = context.camera.transform;
            return Vector3.Dot(m_Ball.position - cam.position, cam.forward) < 0f;
        }

        // The FurBrain stands on the detected plane, so its feet give the floor height (plus the ball's radius).
        bool IsOnFloor() => m_Ball.position.y <= context.petTransform.position.y + m_Ball.lossyScale.y * 0.5f;

        void Launch(Phase phase, Vector3 from, Vector3 to, float duration)
        {
            m_Phase = phase;
            m_From = from;
            m_To = to;
            m_PhaseStart = Time.time;
            m_PhaseDuration = Mathf.Max(0.01f, duration);
        }

        /// <summary>
        /// A parabola from a to b peaking m_ArcHeight above the midpoint: lerp plus 4·t·(1−t)·height. LerpUnclamped
        /// lets t go past 1, continuing the same curve beyond b (downward, since the arc term goes negative).
        /// </summary>
        Vector3 Arc(Vector3 a, Vector3 b, float t) =>
            Vector3.LerpUnclamped(a, b, t) + Vector3.up * (4f * t * (1f - t) * m_ArcHeight);

        // 0 at the start of a rally, 1 at m_RallyForMaxSpeed.
        float Ramp => Mathf.Clamp01(m_Rally / (float)Mathf.Max(1, m_RallyForMaxSpeed));
        float FlightTime() => Mathf.Lerp(m_StartFlightTime, m_MinFlightTime, Ramp);

        /// <summary>Where the ball is thrown at: just below the camera, fixed at launch.</summary>
        Vector3 PlayerTarget() => context.camera.transform.position - Vector3.up * m_AimBelowCamera;

        // At head height, slightly in front of the FurBrain (the base class's PetPoint does the maths).
        Vector3 PetPoint() => PetPoint(m_PetHeadHeight);
    }
}
