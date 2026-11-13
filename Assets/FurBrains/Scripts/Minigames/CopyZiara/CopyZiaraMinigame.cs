using System.Collections.Generic;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Copy Ziara: a memory game. Coloured pads lie on the floor in front of the FurBrain. It plays a sequence (each
    /// pad lights up with its own note) and the player taps the same sequence back. Each round adds one more step.
    /// A wrong tap gently shows the right pad and ends the game; the score is the longest sequence copied.
    /// </summary>
    /// <remarks>
    /// Small state machine (an enum + switch, like Cup Shuffle):
    /// Intro → Show (the FurBrain plays the sequence) → Repeat (wait for the player's taps)
    /// → Correct (a short cheer, add a step, back to Show) or Mistake (show the right pad, then finish).
    ///
    /// The pads are laid out from this game object's transform, which the runner places at the FurBrain's feet facing
    /// the player. Sizes and distances are fractions of the FurBrain's height (<see cref="Minigame.petHeight"/>), so
    /// the layout fits whatever size the FurBrain is.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    public class CopyZiaraMinigame : Minigame
    {
        enum Phase { Intro, Show, Repeat, Correct, Mistake }

        [SerializeField]
        [Tooltip("Hidden template pad (a child of this prefab) that the pads are copied from.")]
        GameObject m_PadTemplate;

        [SerializeField]
        [Tooltip("One colour per pad. The number of pads is the shorter of this list and Pad Notes.")]
        Color[] m_PadColors =
        {
            new(0.95f, 0.3f, 0.3f),  // red
            new(1f, 0.85f, 0.2f),    // yellow
            new(0.35f, 0.85f, 0.4f), // green
            new(0.3f, 0.55f, 1f),    // blue
        };

        [SerializeField]
        [Tooltip("One note per pad, in Hz. The default C, E, G and high C always sound nice together.")]
        float[] m_PadNotes = { 523.25f, 659.25f, 783.99f, 1046.5f };

        [Header("Layout (fractions of the FurBrain's height)")]
        [SerializeField]
        [Tooltip("How far in front of the FurBrain the pads are.")]
        float m_Distance = 0.6f;

        [SerializeField]
        [Tooltip("Pad diameter.")]
        float m_PadSize = 0.25f;

        [SerializeField, Range(30f, 180f)]
        [Tooltip("How wide the arc of pads is, in degrees.")]
        float m_ArcDegrees = 100f;

        [Header("Game")]
        [SerializeField, Min(1)]
        [Tooltip("Length of the first sequence.")]
        int m_StartLength = 2;

        [SerializeField, Min(1)]
        [Tooltip("The game ends (well done!) after copying a sequence this long.")]
        int m_MaxLength = 10;

        [Header("Timing (seconds)")]
        [SerializeField]
        [Tooltip("Pause before the first sequence.")]
        float m_IntroTime = 1f;

        [SerializeField]
        [Tooltip("How long each pad lights up in the first round.")]
        float m_LightTime = 0.45f;

        [SerializeField]
        [Tooltip("Gap between pads while the sequence plays.")]
        float m_GapTime = 0.2f;

        [SerializeField, Range(0.5f, 1f)]
        [Tooltip("Each round plays this much faster (0.93 = 7% quicker).")]
        float m_SpeedUp = 0.93f;

        [SerializeField]
        [Tooltip("Pads never light for less than this.")]
        float m_MinLightTime = 0.25f;

        [Header("Tapping")]
        [SerializeField]
        [Tooltip("Taps count if within this many times the pad's on-screen radius of its centre.")]
        float m_TapRadiusMultiplier = 1.2f;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("...or within this fraction of the screen width, whichever is bigger.")]
        float m_MinTapRadiusFraction = 0.06f;

        const float k_NoteSeconds = 0.6f;
        const float k_TapLightTime = 0.3f;
        const float k_LiftHeight = 0.02f;  // metres a lit pad rises
        const float k_LitScale = 1.15f;    // how much a lit pad grows
        const float k_LeadIn = 0.3f;       // pause at the start of each Show
        const float k_CheerTime = 1f;      // Correct phase length
        const float k_MistakeTime = 1.8f;  // Mistake phase length
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        readonly List<Transform> m_Pads = new();
        readonly List<int> m_Sequence = new();  // pad indices, in order
        Material[] m_PadMaterials;  // one copy per pad (made at runtime, destroyed in OnDestroy)
        Vector3[] m_PadRest;        // each pad's resting local position
        Vector3 m_PadScale;
        AudioClip[] m_Notes;
        AudioClip m_Boop;
        AudioSource m_Audio;

        Phase m_Phase;
        float m_PhaseStart;
        float m_CurrentLightTime;
        int m_ShowStep;      // next sequence step to play in Show
        int m_InputIndex;    // next step the player must tap in Repeat
        int m_MistakePad;    // the pad that should have been tapped
        int m_MistakeFlashes;

        int m_LitPad = -1;
        float m_LitUntil;

        public override MinigameResultKind resultKind => MinigameResultKind.Score;

        protected override void OnBegin()
        {
            currentScore = 0;
            m_Audio = GetComponent<AudioSource>();
            if (m_PadTemplate == null)
            {
                Debug.LogWarning("[FurBrains] Copy Ziara has no pad template.", this);
                FinishWithScore(0);
                return;
            }
            m_PadTemplate.SetActive(false);
            CreatePads();

            m_CurrentLightTime = m_LightTime;
            for (var i = 0; i < m_StartLength; i++)
                AddStep();

            if (context.pet != null)
                context.pet.OnTapped(); // "watch me!"
            SetPhase(Phase.Intro);
        }

        void CreatePads()
        {
            var count = Mathf.Min(m_PadColors.Length, m_PadNotes.Length);
            var diameter = petHeight * m_PadSize;
            var radius = petHeight * m_Distance;
            m_PadScale = new Vector3(diameter, m_PadTemplate.transform.localScale.y, diameter);
            m_PadMaterials = new Material[count];
            m_PadRest = new Vector3[count];
            m_Notes = new AudioClip[count];

            var templateRenderer = m_PadTemplate.GetComponent<Renderer>();
            for (var i = 0; i < count; i++)
            {
                // Spread the pads evenly across the arc, left to right as the player sees them. Angle 0 is straight
                // toward the player (the game's forward), so sin gives the sideways offset and cos the forward one.
                var t = count > 1 ? i / (count - 1f) : 0.5f;
                var angle = Mathf.Lerp(m_ArcDegrees * 0.5f, -m_ArcDegrees * 0.5f, t) * Mathf.Deg2Rad;
                // A cylinder is 2 units tall, so its half-height is its Y scale: raise it by that to sit on the floor.
                m_PadRest[i] = new Vector3(Mathf.Sin(angle) * radius, m_PadScale.y, Mathf.Cos(angle) * radius);

                var pad = Instantiate(m_PadTemplate, transform);
                pad.name = $"Pad {i}";
                pad.SetActive(true);
                pad.transform.localPosition = m_PadRest[i];
                pad.transform.localRotation = Quaternion.identity;
                pad.transform.localScale = m_PadScale;
                m_Pads.Add(pad.transform);

                // Each pad needs its own material so it can light up alone. new Material(original) copies it,
                // keyword settings included (the template material has emission switched on, glowing black).
                if (templateRenderer != null)
                {
                    m_PadMaterials[i] = new Material(templateRenderer.sharedMaterial) { name = $"Pad {i}" };
                    m_PadMaterials[i].SetColor("_BaseColor", m_PadColors[i]);
                    m_PadMaterials[i].SetColor(k_EmissionColor, Color.black);
                    pad.GetComponent<Renderer>().sharedMaterial = m_PadMaterials[i];
                }

                m_Notes[i] = ToneGenerator.Note($"Pad Note {i}", m_PadNotes[i], k_NoteSeconds);
            }
            m_Boop = ToneGenerator.Note("Boop", 196f, 0.4f, 0.4f); // a low, soft G: "not that one"
        }

        void AddStep() => m_Sequence.Add(Random.Range(0, m_Pads.Count));

        void SetPhase(Phase phase)
        {
            m_Phase = phase;
            m_PhaseStart = Time.time;
        }

        void Update()
        {
            if (!isRunning)
                return;

            if (m_LitPad >= 0 && Time.time >= m_LitUntil)
                UnlightPad();

            var elapsed = Time.time - m_PhaseStart;
            switch (m_Phase)
            {
                case Phase.Intro:
                    if (elapsed >= m_IntroTime)
                        StartShow();
                    break;

                case Phase.Show:
                    // Step k starts at leadIn + k × (light + gap).
                    var stepTime = m_CurrentLightTime + m_GapTime;
                    if (m_ShowStep < m_Sequence.Count && elapsed >= k_LeadIn + m_ShowStep * stepTime)
                    {
                        LightPad(m_Sequence[m_ShowStep], m_CurrentLightTime);
                        m_ShowStep++;
                    }
                    else if (m_ShowStep >= m_Sequence.Count && elapsed >= k_LeadIn + m_Sequence.Count * stepTime)
                    {
                        m_InputIndex = 0;
                        SetPhase(Phase.Repeat);
                    }
                    break;

                case Phase.Repeat:
                    CheckTap();
                    break;

                case Phase.Correct:
                    if (elapsed >= k_CheerTime)
                    {
                        if (m_Sequence.Count >= m_MaxLength)
                        {
                            FinishWithScore(currentScore); // copied the longest sequence: the runner celebrates
                            return;
                        }
                        AddStep();
                        m_CurrentLightTime = Mathf.Max(m_MinLightTime, m_CurrentLightTime * m_SpeedUp);
                        StartShow();
                    }
                    break;

                case Phase.Mistake:
                    // Flash the right pad twice (with its note), then end.
                    if (m_MistakeFlashes < 2 && elapsed >= 0.4f + m_MistakeFlashes * 0.6f)
                    {
                        LightPad(m_MistakePad, 0.35f);
                        m_MistakeFlashes++;
                    }
                    if (elapsed >= k_MistakeTime)
                        FinishWithScore(currentScore);
                    break;
            }
        }

        void StartShow()
        {
            m_ShowStep = 0;
            SetPhase(Phase.Show);
        }

        void CheckTap()
        {
            if (!TryGetTap(out var tap))
                return;

            var pad = PadUnderTap(tap);
            if (pad < 0)
                return; // tapped empty space: ignore it

            if (pad == m_Sequence[m_InputIndex])
            {
                LightPad(pad, k_TapLightTime);
                m_InputIndex++;
                if (m_InputIndex == m_Sequence.Count)
                {
                    currentScore = m_Sequence.Count;
                    if (context.pet != null)
                        context.pet.OnTapped(); // the FurBrain is pleased
                    SetPhase(Phase.Correct);
                }
            }
            else
            {
                m_Audio.PlayOneShot(m_Boop);
                m_MistakePad = m_Sequence[m_InputIndex];
                m_MistakeFlashes = 0;
                SetPhase(Phase.Mistake);
            }
        }

        /// <summary>The pad nearest the tap on screen, among those close enough to count; -1 if none.</summary>
        int PadUnderTap(Vector2 tap)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            var radius = m_PadScale.x * 0.5f;
            for (var i = 0; i < m_Pads.Count; i++)
            {
                var position = m_Pads[i].position;
                if (!IsTapNear(tap, position, radius, m_TapRadiusMultiplier, m_MinTapRadiusFraction))
                    continue;
                var distance = Vector2.Distance(tap, context.camera.WorldToScreenPoint(position));
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Lights a pad (glow, grow, lift) and plays its note, for <paramref name="seconds"/>.</summary>
        void LightPad(int index, float seconds)
        {
            UnlightPad();
            m_LitPad = index;
            m_LitUntil = Time.time + seconds;

            var pad = m_Pads[index];
            pad.localPosition = m_PadRest[index] + Vector3.up * k_LiftHeight;
            pad.localScale = new Vector3(m_PadScale.x * k_LitScale, m_PadScale.y, m_PadScale.z * k_LitScale);
            if (m_PadMaterials[index] != null)
                m_PadMaterials[index].SetColor(k_EmissionColor, m_PadColors[index] * 1.5f);
            m_Audio.PlayOneShot(m_Notes[index]);
        }

        void UnlightPad()
        {
            if (m_LitPad < 0)
                return;
            var pad = m_Pads[m_LitPad];
            pad.localPosition = m_PadRest[m_LitPad];
            pad.localScale = m_PadScale;
            if (m_PadMaterials[m_LitPad] != null)
                m_PadMaterials[m_LitPad].SetColor(k_EmissionColor, Color.black);
            m_LitPad = -1;
        }

        void OnDestroy()
        {
            // Materials and clips made with new / AudioClip.Create are engine objects that the garbage collector
            // won't free: destroy them explicitly, like C++ delete. (The pads themselves are children of this game
            // object, so they're destroyed along with it.)
            if (m_PadMaterials != null)
                foreach (var material in m_PadMaterials)
                    if (material != null)
                        Destroy(material);
            if (m_Notes != null)
                foreach (var note in m_Notes)
                    if (note != null)
                        Destroy(note);
            if (m_Boop != null)
                Destroy(m_Boop);
        }
    }
}
