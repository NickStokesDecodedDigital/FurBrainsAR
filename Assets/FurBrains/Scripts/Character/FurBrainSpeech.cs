using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FurBrains
{
    /// <summary>
    /// Makes the FurBrain speak a voice line and moves her mouth with it: loudness sets how open the mouth is,
    /// and a simple vowel detector picks which mouth shape (A I U E O) to use.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Say"/> with a clip (or an Audio Random Container). The line plays on this component's own
    /// AudioSource, so what's measured each frame is the voice only, not crumbs or summon sounds.
    ///
    /// <b>How open:</b> every frame, read the last few milliseconds of audio the source just played
    /// (<c>GetOutputData</c>), take its loudness (RMS: the square root of the average squared sample) and map it to
    /// 0..1. Loud = open, quiet = closed.
    ///
    /// <b>Which vowel:</b> every vowel makes the throat and mouth resonate at two main frequencies, called formants.
    /// F1 (roughly 250–1000 Hz) rises the more open the mouth is ("ah" high, "ee"/"oo" low). F2 (roughly 800–3000 Hz)
    /// rises the further forward the tongue is ("ee" high, "oo"/"oh" low). So:
    /// <list type="number">
    /// <item><c>GetSpectrumData</c> splits the audio into 1024 frequency bins (a Fourier transform, done by Unity).</item>
    /// <item>The spectrum is smoothed (averaged over neighbouring bins) so the voice's individual harmonics blur into
    /// broad hills; the formants are the tops of those hills.</item>
    /// <item>F1 = the highest point between 250 and 1000 Hz; F2 = the highest point above F1, up to 3000 Hz.</item>
    /// <item>Each vowel in <c>Vowels</c> has a typical F1/F2. The closer the measured pair is to a vowel's, the more
    /// of that vowel's shape is used (weights always add up to 1, so it blends between neighbours).</item>
    /// </list>
    /// It's deliberately simple: it gets the broad differences (open "ah", wide "ee", round "oo") and may mix up
    /// neighbours like E/I or O/U. Tune the vowel frequencies with the live readout at the bottom of the Inspector.
    ///
    /// The mouth goes through FurBrainFace like every other expression, so it adds on top of the mood (e.g. Empowered)
    /// instead of fighting it, and it's applied after the Animator.
    /// </remarks>
    public class FurBrainSpeech : MonoBehaviour
    {
        /// <summary>One vowel: its blend shape and the formant frequencies it typically has.</summary>
        [Serializable]
        public class Vowel
        {
            [Tooltip("Blend shape for this vowel.")]
            public string shape;

            [Tooltip("Typical first formant in Hz. Higher = more open mouth (\"ah\" ~850, \"ee\" ~330).")]
            public float f1;

            [Tooltip("Typical second formant in Hz. Higher = tongue further forward (\"ee\" ~2700, \"oo\" ~1000).")]
            public float f2;

            public Vowel() { }

            public Vowel(string shape, float f1, float f2)
            {
                this.shape = shape;
                this.f1 = f1;
                this.f2 = f2;
            }
        }

        [SerializeField]
        [Tooltip("Found in the children automatically if left empty.")]
        FurBrainFace m_Face;

        [Header("Mouth opening (loudness)")]
        [SerializeField]
        [Tooltip("How far the mouth may open (0..1). Below 1 looks less like shouting.")]
        [Range(0f, 1f)]
        float m_MaxOpen = 0.8f;

        [SerializeField]
        [Tooltip("Loudness (RMS) that counts as fully open. Lower = the mouth opens wider for the same audio. " +
                 "Speech files are usually around 0.05–0.2.")]
        float m_LoudLevel = 0.15f;

        [SerializeField]
        [Tooltip("Loudness below this counts as silence, so breaths and hiss don't move the mouth.")]
        float m_QuietLevel = 0.01f;

        [SerializeField]
        [Tooltip("How fast the mouth opens (per second). High = snappy.")]
        float m_OpenSpeed = 25f;

        [SerializeField]
        [Tooltip("How fast the mouth closes (per second). A bit slower than opening looks natural.")]
        float m_CloseSpeed = 12f;

        [Header("Mouth shape (vowels)")]
        [SerializeField]
        [Tooltip("Off = loudness only, always using Mouth Shape below (the simple version, for comparison).")]
        bool m_DetectVowels = true;

        [SerializeField]
        [Tooltip("Shape used when Detect Vowels is off. Ziara's visemes are A I U E O; A is the widest open.")]
        string m_MouthShape = "A";

        [SerializeField]
        [Tooltip("Ziara's vowel shapes and their typical formants (a young woman's voice). Adjust F1/F2 to match " +
                 "what the live readout shows while she says that vowel.")]
        Vowel[] m_Vowels =
        {
            new Vowel("A", 850f, 1300f), // "ah"
            new Vowel("I", 330f, 2700f), // "ee"
            new Vowel("U", 380f, 1000f), // "oo"
            new Vowel("E", 600f, 2300f), // "eh"
            new Vowel("O", 550f, 950f),  // "oh"
        };

        [SerializeField]
        [Tooltip("How far (Hz) F1 may be from a vowel's and still count as a close match. Bigger = softer blending.")]
        float m_F1Tolerance = 200f;

        [SerializeField]
        [Tooltip("How far (Hz) F2 may be from a vowel's and still count as a close match. Bigger = softer blending.")]
        float m_F2Tolerance = 500f;

        [SerializeField]
        [Tooltip("How quickly the mouth changes from one vowel shape to the next (per second). Lower = less flicker.")]
        float m_VowelChangeSpeed = 12f;

        [SerializeField]
        [Tooltip("How widely the spectrum is averaged (Hz) before looking for formants, to blur the voice's " +
                 "individual harmonics. Raise it if F1/F2 jump around a lot.")]
        float m_SmoothingHz = 150f;

        [Header("Audio")]
        [SerializeField]
        [Tooltip("0 = heard the same everywhere (2D, easiest for children), 1 = comes from the FurBrain's position (3D).")]
        [Range(0f, 1f)]
        float m_SpatialBlend = 0f;

        [Header("Testing")]
        [SerializeField]
        [Tooltip("Played by the component's ⋮ menu → Say Test Line, in Play mode.")]
        AudioResource m_TestLine;

        [SerializeField]
        [Tooltip("Written by the script while she talks (Play mode): the measured formants and the strongest vowel. " +
                 "Use them to tune the Vowels list. Editing them does nothing.")]
        float m_LiveF1, m_LiveF2;

        [SerializeField]
        [Tooltip("Written by the script: the vowel currently weighted most.")]
        string m_LiveVowel;

        // Formant search ranges (Hz). F2 must be at least k_MinGap above F1, so the two peaks are different hills.
        const float k_MinF1 = 250f, k_MaxF1 = 1000f;
        const float k_MinF2 = 800f, k_MaxF2 = 3000f;
        const float k_MinGap = 300f;

        AudioSource m_Voice;
        readonly float[] m_Samples = new float[256];   // about 5 ms of audio at 48 kHz; reused every frame (no garbage)
        readonly float[] m_Spectrum = new float[1024]; // GetSpectrumData needs a power of two
        readonly float[] m_Smoothed = new float[1024];
        float m_Open;                                  // current mouth opening, 0..1
        float[] m_VowelTarget;                         // wanted share of each vowel (adds up to 1)
        float[] m_VowelWeight;                         // current share, easing toward the target

        /// <summary>True while a line is playing.</summary>
        public bool isSpeaking => m_Voice != null && m_Voice.isPlaying;

        /// <summary>Current mouth opening, 0..1 (e.g. for a debug display).</summary>
        public float mouthOpen => m_Open;

        void Awake()
        {
            if (m_Face == null)
                m_Face = GetComponentInChildren<FurBrainFace>();

            // A separate AudioSource just for the voice, so the analysis measures only speech.
            m_Voice = gameObject.AddComponent<AudioSource>();
            m_Voice.playOnAwake = false;
            m_Voice.spatialBlend = m_SpatialBlend;

            SayTestLine();
        }

        /// <summary>
        /// Speak a line: an AudioClip, or an Audio Random Container (a random pick from several takes). Starting a
        /// new line stops the one playing, so she never talks over herself.
        /// </summary>
        public void Say(AudioResource line)
        {
            if (line == null || m_Voice == null)
                return;
            m_Voice.Stop();
            m_Voice.resource = line;
            m_Voice.Play();
        }

        /// <summary>Stop talking now; the mouth closes smoothly.</summary>
        public void StopSpeaking()
        {
            if (m_Voice != null)
                m_Voice.Stop();
        }

        [ContextMenu("Say Test Line")]
        void SayTestLine() => Say(m_TestLine);

        void Update()
        {
            MakeVowelArrays();

            var loudness = isSpeaking ? MouthFromLoudness() : 0f;

            // Only look for a vowel while there's sound; in silence the mouth closes keeping its last shape.
            if (m_DetectVowels && loudness > 0f)
                DetectVowel();

            // Ease the opening: open fast, close a little slower.
            var speed = loudness > m_Open ? m_OpenSpeed : m_CloseSpeed;
            var wasOpen = m_Open > 0f;
            m_Open = Mathf.MoveTowards(m_Open, loudness, speed * Time.deltaTime);

            // Ease each vowel's share toward its target, so the shape doesn't flicker from frame to frame.
            for (var i = 0; i < m_VowelWeight.Length; i++)
                m_VowelWeight[i] = Mathf.MoveTowards(m_VowelWeight[i], m_VowelTarget[i], m_VowelChangeSpeed * Time.deltaTime);

            // Only touch the shapes while talking or closing, so the mouth is left alone the rest of the time.
            if (m_Face != null && (wasOpen || m_Open > 0f))
                ApplyMouth();
        }

        // Final weight of each shape = how open × that vowel's share. They add up, like all blend shapes.
        // SetShapeImmediate skips FurBrainFace's own easing (too slow for syllables); ours above replaces it.
        void ApplyMouth()
        {
            var open = m_Open * m_MaxOpen;
            if (!m_DetectVowels)
            {
                m_Face.SetShapeImmediate(m_MouthShape, open);
                return;
            }
            for (var i = 0; i < m_Vowels.Length; i++)
                m_Face.SetShapeImmediate(m_Vowels[i].shape, open * m_VowelWeight[i]);
        }

        // Loudness of the audio just played, mapped to 0..1 between the quiet and loud levels.
        float MouthFromLoudness()
        {
            m_Voice.GetOutputData(m_Samples, 0); // channel 0 (left, or the only channel for mono voice files)
            var sum = 0f;
            foreach (var s in m_Samples)
                sum += s * s;
            var rms = Mathf.Sqrt(sum / m_Samples.Length);
            return Mathf.InverseLerp(m_QuietLevel, m_LoudLevel, rms); // clamps to 0..1
        }

        // Measures F1/F2 and sets each vowel's target share from how close it is to them.
        void DetectVowel()
        {
            // 1. Spectrum: m_Spectrum[i] = strength of frequencies around i × binHz.
            //    Bins cover 0 Hz up to half the sample rate (the highest frequency digital audio can hold).
            m_Voice.GetSpectrumData(m_Spectrum, 0, FFTWindow.BlackmanHarris);
            var binHz = AudioSettings.outputSampleRate * 0.5f / m_Spectrum.Length;

            // 2. Smooth: each bin becomes the average of its neighbours within ± m_SmoothingHz. Only the bins up to
            //    the top of the F2 range are needed (about 130 of the 1024 at 48 kHz).
            var radius = Mathf.Max(1, Mathf.RoundToInt(m_SmoothingHz / binHz));
            var lastBin = Mathf.Min(m_Spectrum.Length - 1, Mathf.CeilToInt(k_MaxF2 / binHz));
            for (var i = 0; i <= lastBin; i++)
            {
                var from = Mathf.Max(0, i - radius);
                var to = Mathf.Min(m_Spectrum.Length - 1, i + radius);
                var sum = 0f;
                for (var j = from; j <= to; j++)
                    sum += m_Spectrum[j];
                m_Smoothed[i] = sum / (to - from + 1);
            }

            // 3. The two hilltops.
            var f1 = PeakFrequency(k_MinF1, k_MaxF1, binHz, false);
            // Higher frequencies are naturally much quieter in a voice, so for F2 the search boosts each bin in
            // proportion to its frequency; otherwise the slope down from F1 would win over the real F2 hill.
            var f2 = PeakFrequency(Mathf.Max(k_MinF2, f1 + k_MinGap), k_MaxF2, binHz, true);
            m_LiveF1 = f1;
            m_LiveF2 = f2;

            // 4. Closeness to each vowel: distance measured in "tolerances", turned into a score with exp(-d²)
            //    (1 = exact match, falling smoothly towards 0), then scaled so the shares add up to 1.
            var total = 0f;
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                var d1 = (f1 - m_Vowels[i].f1) / m_F1Tolerance;
                var d2 = (f2 - m_Vowels[i].f2) / m_F2Tolerance;
                m_VowelTarget[i] = Mathf.Exp(-(d1 * d1 + d2 * d2));
                total += m_VowelTarget[i];
            }
            if (total < 1e-6f)
                return; // nothing close at all (odd sound): keep the previous targets

            var best = 0;
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                m_VowelTarget[i] /= total;
                if (m_VowelTarget[i] > m_VowelTarget[best])
                    best = i;
            }
            m_LiveVowel = m_Vowels.Length > 0 ? m_Vowels[best].shape : "";
        }

        // Frequency (Hz) of the strongest smoothed bin between fromHz and toHz.
        float PeakFrequency(float fromHz, float toHz, float binHz, bool boostHighs)
        {
            var first = Mathf.Max(1, Mathf.RoundToInt(fromHz / binHz));
            var last = Mathf.Min(m_Smoothed.Length - 1, Mathf.RoundToInt(toHz / binHz));
            var bestBin = first;
            var bestValue = -1f;
            for (var i = first; i <= last; i++)
            {
                var value = boostHighs ? m_Smoothed[i] * i : m_Smoothed[i];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestBin = i;
                }
            }
            return bestBin * binHz;
        }

        // The vowel arrays match the Vowels list; (re)made if its length changes, e.g. edited in Play mode.
        // A fresh start is all "A", the most open shape, until the first vowel is measured.
        void MakeVowelArrays()
        {
            if (m_VowelWeight != null && m_VowelWeight.Length == m_Vowels.Length)
                return;
            m_VowelTarget = new float[m_Vowels.Length];
            m_VowelWeight = new float[m_Vowels.Length];
            if (m_Vowels.Length > 0)
                m_VowelTarget[0] = m_VowelWeight[0] = 1f;
        }
    }
}
