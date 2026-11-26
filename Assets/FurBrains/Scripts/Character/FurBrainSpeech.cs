using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FurBrains
{
    /// <summary>
    /// Makes the FurBrain speak a voice line and moves her mouth with it: loudness sets how open the mouth is,
    /// and a vowel detector picks which mouth shape (A I U E O) to use.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Say"/> with a clip (or an Audio Random Container). The line plays on this component's own
    /// AudioSource, so what's measured each frame is the voice only, not crumbs or summon sounds.
    ///
    /// <b>How open:</b> every frame, read the last ~20 ms of audio the source just played (<c>GetOutputData</c>),
    /// take its loudness (RMS: the square root of the average squared sample) and map it to 0..1.
    ///
    /// <b>Which vowel:</b> every vowel makes the throat and mouth resonate at two main frequencies, called formants.
    /// F1 rises the more open the mouth is ("ah" high, "ee"/"oo" low); F2 rises the further forward the tongue is
    /// ("ee" high, "oh" low). The formants are found with <b>LPC</b> (linear predictive coding), the standard method
    /// in speech tools:
    /// <list type="number">
    /// <item>Reduce the audio to ~11 kHz (averaging groups of samples): speech formants are all below 4 kHz.</item>
    /// <item>LPC finds the simple filter (10 numbers) that best predicts each sample from the 10 before it. That
    /// filter has the same resonances as the speaker's mouth and throat, but not the voice's pitch, so its frequency
    /// curve is a clean, smooth outline of the vowel: one hill per formant.</item>
    /// <item>F1 = the first hill above 250 Hz; F2 = the next hill.</item>
    /// <item>Each entry in <c>Vowels</c> has a typical F1/F2. The closer the measured pair is to an entry's, the
    /// more of its shape is used (shares always add up to 1, so it blends between neighbours).</item>
    /// </list>
    /// (The first version smoothed Unity's FFT spectrum and took its highest points instead. With a high voice, it
    /// often caught the wrong hill and mixed up oo/eh/oh with ee.)
    ///
    /// The mouth goes through FurBrainFace like every other expression, so it adds on top of the mood (e.g. Empowered)
    /// instead of fighting it, and it's applied after the Animator.
    /// </remarks>
    public class FurBrainSpeech : MonoBehaviour
    {
        /// <summary>One vowel sound: the blend shape to use and the formant frequencies it typically has.</summary>
        [Serializable]
        public class Vowel
        {
            [Tooltip("Blend shape for this vowel sound. Several sounds may share one (e.g. \"oo\" and \"oh\" both O, " +
                     "because Ziara's own U shape is flat rather than round).")]
            public string shape;

            [Tooltip("Typical first formant in Hz. Higher = more open mouth (\"ah\" ~900, \"ee\" ~400).")]
            public float f1;

            [Tooltip("Typical second formant in Hz. Higher = tongue further forward (\"ee\" ~2900, \"oh\" ~1100).")]
            public float f2;

            [Tooltip("Multiplies this vowel's shape weight. Above 1 for shapes that only read clearly near full " +
                     "strength (e.g. O, whose rounding is subtle at half weight). The total is capped at 1.")]
            public float strength = 1f;

            public Vowel() { }

            public Vowel(string shape, float f1, float f2, float strength = 1f)
            {
                this.shape = shape;
                this.f1 = f1;
                this.f2 = f2;
                this.strength = strength;
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
        [Tooltip("Vowel sounds and their typical formants, measured from the Piper test voice (en_US-amy) with this " +
                 "detector: ah, ee, oo, eh, oh. Retune for a new voice: play a line full of one vowel and copy " +
                 "what the live readout shows into that entry.")]
        Vowel[] m_Vowels =
        {
            new Vowel("A", 910f, 1400f), // "ah"  car, father
            new Vowel("I", 390f, 2900f), // "ee"  see, tree
            new Vowel("O", 390f, 2000f, 1.25f), // "oo" food, moon (American "oo" is far forward, hence the high F2)
            new Vowel("E", 690f, 2030f),        // "eh" bed, red
            new Vowel("O", 570f, 1120f, 1.25f), // "oh" door, more (1.25 × Max Open 0.8 = full O when loud)
            new Vowel("O", 740f, 1680f, 1.25f), // start of "oh" in go, no: American "oh" begins here, glides to the one above
        };

        [SerializeField]
        [Tooltip("How far (Hz) F1 may be from a vowel's and still count as a close match. Bigger = softer blending.")]
        float m_F1Tolerance = 150f;

        [SerializeField]
        [Tooltip("How far (Hz) F2 may be from a vowel's and still count as a close match. Bigger = softer blending.")]
        float m_F2Tolerance = 400f;

        [SerializeField]
        [Tooltip("Winner takes more: each shape's share is raised to this power before the shares are rescaled to " +
                 "add up to 1. 1 = plain blend; 2–3 = the leading shape gets most of the mouth, so neighbours (a bit " +
                 "of A or E in an O) don't water it down. Never changes which shape leads.")]
        [Range(1f, 5f)]
        float m_WinnerFocus = 3f;

        [SerializeField]
        [Tooltip("How quickly the mouth changes from one vowel shape to the next (per second). Lower = less flicker.")]
        float m_VowelChangeSpeed = 12f;

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
        [Tooltip("Written by the script while she talks (Play mode): the measured formants. Use them to tune the " +
                 "Vowels list. Editing them does nothing.")]
        float m_LiveF1, m_LiveF2;

        [SerializeField]
        [Tooltip("Written by the script: the Vowels entry currently weighted most (its number in the list and shape).")]
        string m_LiveVowel;

        const int k_LpcOrder = 10;            // filter length: 10 is right for speech at ~11 kHz (2 per formant + 2)
        const int k_FrameLength = 256;        // samples analysed after reducing to ~11 kHz: about 23 ms
        const float k_AnalysisRate = 11025f;  // the rate the audio is reduced to
        const float k_PreEmphasis = 0.95f;    // see DetectVowel step 2
        const float k_MinF1 = 250f;           // hills below this are the voice's pitch, not a formant
        const float k_MinF2 = 700f;
        const float k_MinGap = 150f;          // F2 must be at least this far above F1
        const float k_MaxHz = 3600f, k_StepHz = 10f; // the filter's curve is checked every 10 Hz up to here

        AudioSource m_Voice;
        float[] m_Raw;                        // ~21 ms of audio at the output rate (e.g. 1024 samples at 48 kHz)
        readonly float[] m_Frame = new float[k_FrameLength];
        readonly float[] m_Autocorrelation = new float[k_LpcOrder + 1];
        readonly float[] m_Lpc = new float[k_LpcOrder + 1];
        readonly float[] m_LpcTemp = new float[k_LpcOrder + 1];
        int m_Decimation;                     // how many output samples are averaged into one analysis sample
        float m_FrameRate;                    // sample rate of m_Frame

        float m_Open;                         // current mouth opening, 0..1
        float[] m_VowelTarget;                // wanted share of each vowel (adds up to 1)
        float[] m_VowelWeight;                // current share, easing toward the target
        float[] m_ShapeShare;                 // per shape, stored at the first entry using it (see ApplyMouth)
        float[] m_ShapeStrength;
        float[] m_ShapeFocused;

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

            // Unity mixes at the device's rate (usually 48 kHz). Averaging every m_Decimation samples gives ~11 kHz.
            m_Decimation = Mathf.Max(1, Mathf.RoundToInt(AudioSettings.outputSampleRate / k_AnalysisRate));
            m_FrameRate = (float)AudioSettings.outputSampleRate / m_Decimation;
            m_Raw = new float[k_FrameLength * m_Decimation];

            //SayTestLine();
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

            var loudness = 0f;
            if (isSpeaking)
            {
                m_Voice.GetOutputData(m_Raw, 0); // channel 0 (left, or the only channel for mono voice files)
                loudness = MouthFromLoudness();
            }

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

        // Sets each mouth shape's weight. Several Vowels entries can use the same shape (oo, oh and the start of oh
        // are all O), so their shares are first added up per shape. Each shape's total is kept at the first entry
        // that uses it (its "group leader"), so no extra list of shape names is needed.
        // SetShapeImmediate skips FurBrainFace's own easing (too slow for syllables); ours replaces it.
        void ApplyMouth()
        {
            var open = m_Open * m_MaxOpen;
            if (!m_DetectVowels)
            {
                m_Face.SetShapeImmediate(m_MouthShape, open);
                return;
            }

            // 1. Each shape's total share, and its share-weighted strength.
            Array.Clear(m_ShapeShare, 0, m_ShapeShare.Length);
            Array.Clear(m_ShapeStrength, 0, m_ShapeStrength.Length);
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                var leader = GroupLeader(i);
                m_ShapeShare[leader] += m_VowelWeight[i];
                m_ShapeStrength[leader] += m_VowelWeight[i] * m_Vowels[i].strength;
            }

            // 2. Winner takes more: raise every shape's share to the power Winner Focus, then rescale so they add
            //    up to 1 again. E.g. O 0.6 / E 0.3 / A 0.1 cubed and rescaled becomes O 0.88 / E 0.11 / A 0.004:
            //    the order stays the same, but the leader dominates.
            var total = 0f;
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                m_ShapeFocused[i] = GroupLeader(i) == i ? Mathf.Pow(m_ShapeShare[i], m_WinnerFocus) : 0f;
                total += m_ShapeFocused[i];
            }

            // 3. Weight = how open × focused share × strength (the strengths of the entries that made up the share,
            //    averaged by how much each contributed). FurBrainFace clamps the result to 0..1.
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                if (GroupLeader(i) != i)
                    continue;
                var focused = total > 0f ? m_ShapeFocused[i] / total : 0f;
                var strength = m_ShapeShare[i] > 0f ? m_ShapeStrength[i] / m_ShapeShare[i] : 1f;
                m_Face.SetShapeImmediate(m_Vowels[i].shape, open * focused * strength);
            }
        }

        // Index of the first Vowels entry that uses the same shape as entry i (i itself if it's the first).
        int GroupLeader(int i)
        {
            for (var j = 0; j < i; j++)
                if (m_Vowels[j].shape == m_Vowels[i].shape)
                    return j;
            return i;
        }

        // Loudness of the audio just read, mapped to 0..1 between the quiet and loud levels.
        float MouthFromLoudness()
        {
            var sum = 0f;
            foreach (var s in m_Raw)
                sum += s * s;
            var rms = Mathf.Sqrt(sum / m_Raw.Length);
            return Mathf.InverseLerp(m_QuietLevel, m_LoudLevel, rms); // clamps to 0..1
        }

        // Measures F1/F2 from m_Raw and sets each vowel's target share from how close it is to them.
        void DetectVowel()
        {
            // 1. Reduce to ~11 kHz: each analysis sample is the average of m_Decimation output samples.
            for (var i = 0; i < k_FrameLength; i++)
            {
                var sum = 0f;
                for (var j = 0; j < m_Decimation; j++)
                    sum += m_Raw[i * m_Decimation + j];
                m_Frame[i] = sum / m_Decimation;
            }

            // 2. Pre-emphasis (keep mostly the change from one sample to the next): voices are much quieter at high
            //    frequencies, and this evens that out so F2 isn't drowned by F1. Then a Hamming window (fade the
            //    ends to 0) so the cut-off edges of the snippet don't add false frequencies. Done backwards so each
            //    step still reads the original previous sample.
            for (var i = k_FrameLength - 1; i > 0; i--)
                m_Frame[i] -= k_PreEmphasis * m_Frame[i - 1];
            for (var i = 0; i < k_FrameLength; i++)
                m_Frame[i] *= 0.54f - 0.46f * Mathf.Cos(2f * Mathf.PI * i / (k_FrameLength - 1));

            // 3. Autocorrelation: r[lag] = how similar the snippet is to itself shifted by 'lag' samples.
            for (var lag = 0; lag <= k_LpcOrder; lag++)
            {
                var sum = 0f;
                for (var i = lag; i < k_FrameLength; i++)
                    sum += m_Frame[i] * m_Frame[i - lag];
                m_Autocorrelation[lag] = sum;
            }
            m_Autocorrelation[0] *= 1.0001f; // a tiny bit of extra "noise" keeps the next step numerically stable
            if (m_Autocorrelation[0] <= 0f)
                return;

            // 4. LPC filter from the autocorrelation (the Levinson-Durbin method: builds the best 1-number predictor,
            //    then improves it to 2 numbers, 3, ... up to k_LpcOrder).
            if (!SolveLpc())
                return;

            // 5. Walk the filter's frequency curve and take its first two hilltops above k_MinF1.
            if (!FindFormants(out var f1, out var f2))
                return;
            m_LiveF1 = f1;
            m_LiveF2 = f2;

            // 6. Closeness to each vowel: distance measured in "tolerances", turned into a score with exp(-d²)
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
            {
                // Nothing close at all (an odd sound): put everything back to the previous shares.
                Array.Copy(m_VowelWeight, m_VowelTarget, m_VowelTarget.Length);
                return;
            }

            var best = 0;
            for (var i = 0; i < m_Vowels.Length; i++)
            {
                m_VowelTarget[i] /= total;
                if (m_VowelTarget[i] > m_VowelTarget[best])
                    best = i;
            }
            m_LiveVowel = m_Vowels.Length > 0 ? $"{best}: {m_Vowels[best].shape}" : "";
        }

        // Levinson-Durbin: fills m_Lpc with a[0] = 1, a[1..order] such that each sample ≈ -(a[1]·previous sample +
        // a[2]·the one before + ...). Returns false if the maths broke down (e.g. pure silence).
        bool SolveLpc()
        {
            var r = m_Autocorrelation;
            var a = m_Lpc;
            Array.Clear(a, 0, a.Length);
            a[0] = 1f;
            var error = r[0]; // how wrong the current predictor still is

            for (var i = 1; i <= k_LpcOrder; i++)
            {
                var acc = r[i];
                for (var j = 1; j < i; j++)
                    acc += a[j] * r[i - j];
                var k = -acc / error; // how much the new, i-th number helps

                for (var j = 1; j < i; j++)
                    m_LpcTemp[j] = a[j] + k * a[i - j];
                for (var j = 1; j < i; j++)
                    a[j] = m_LpcTemp[j];
                a[i] = k;

                error *= 1f - k * k;
                if (error <= 0f)
                    return false;
            }
            return true;
        }

        // The filter's strength at frequency w is 1 / |a[0] + a[1]·e^(-iw) + a[2]·e^(-2iw) + ...|²; its local maxima
        // (hills) are the formants.
        bool FindFormants(out float f1, out float f2)
        {
            f1 = f2 = 0f;
            float before = 0f, previous = 0f;
            var count = 0;
            for (var hz = 150f; hz <= k_MaxHz; hz += k_StepHz, count++)
            {
                var w = 2f * Mathf.PI * hz / m_FrameRate;
                float re = 0f, im = 0f;
                for (var k = 0; k <= k_LpcOrder; k++)
                {
                    re += m_Lpc[k] * Mathf.Cos(w * k);
                    im += m_Lpc[k] * Mathf.Sin(w * k);
                }
                var strength = 1f / (re * re + im * im);

                // The previous point is a hilltop if it's higher than both of its neighbours.
                if (count >= 2 && previous > before && previous >= strength)
                {
                    var peak = hz - k_StepHz;
                    if (f1 == 0f)
                    {
                        if (peak >= k_MinF1)
                            f1 = peak;
                    }
                    else if (peak > f1 + k_MinGap && peak >= k_MinF2)
                    {
                        f2 = peak;
                        return true;
                    }
                }
                before = previous;
                previous = strength;
            }
            return false; // no clear second hill (e.g. a consonant): keep the previous vowel
        }

        // The vowel arrays match the Vowels list; (re)made if its length changes, e.g. edited in Play mode.
        // A fresh start is all "A", the most open shape, until the first vowel is measured.
        void MakeVowelArrays()
        {
            if (m_VowelWeight != null && m_VowelWeight.Length == m_Vowels.Length)
                return;
            m_VowelTarget = new float[m_Vowels.Length];
            m_VowelWeight = new float[m_Vowels.Length];
            m_ShapeShare = new float[m_Vowels.Length];
            m_ShapeStrength = new float[m_Vowels.Length];
            m_ShapeFocused = new float[m_Vowels.Length];
            if (m_Vowels.Length > 0)
                m_VowelTarget[0] = m_VowelWeight[0] = 1f;
        }
    }
}
