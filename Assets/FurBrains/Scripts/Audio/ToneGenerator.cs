using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Makes simple musical notes in code, so games can have sound without any audio files.
    /// </summary>
    /// <remarks>
    /// A <c>static class</c> can't be instantiated and only holds static functions: the C# version of a C++ namespace
    /// of free functions.
    ///
    /// <b>How digital sound works:</b> a sound is a list of numbers ("samples") between -1 and 1 giving the speaker
    /// position over time. At 44,100 samples per second (CD quality), a 0.5 s note is 22,050 numbers. A pure tone at
    /// frequency f is sin(2π · f · time); a quieter copy at 2 × f (the "second harmonic") makes it rounder, a bit like
    /// a marimba or xylophone.
    ///
    /// <b>The envelope</b> shapes the volume over time: a very short fade-in (attack), then a smooth fade-out (decay).
    /// Starting or stopping a wave suddenly makes the speaker jump, which you hear as a click.
    ///
    /// The returned AudioClip is an engine object: free it with <c>Destroy</c> when you're done (like C++
    /// <c>delete</c>). The garbage collector only frees the small C# wrapper, not the sound data.
    /// </remarks>
    public static class ToneGenerator
    {
        const int k_SampleRate = 44100;
        const float k_AttackSeconds = 0.005f;

        /// <summary>
        /// A soft note: <paramref name="frequency"/> in Hz (e.g. 440 = the A above middle C), lasting
        /// <paramref name="seconds"/>, at <paramref name="volume"/> (0..1).
        /// </summary>
        public static AudioClip Note(string name, float frequency, float seconds, float volume = 0.5f)
        {
            var sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * k_SampleRate));
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var time = (float)i / k_SampleRate;

                // Volume over time: ramp up over 5 ms, then die away exponentially (reaching about 1% at the end).
                var attack = Mathf.Clamp01(time / k_AttackSeconds);
                var decay = Mathf.Exp(-4.6f * time / seconds);

                var phase = 2f * Mathf.PI * frequency * time;
                var wave = Mathf.Sin(phase) + 0.3f * Mathf.Sin(2f * phase); // tone + quieter second harmonic

                samples[i] = wave / 1.3f * attack * decay * volume; // ÷ 1.3 keeps the sum within -1..1
            }

            // 1 channel (mono), not streamed: the whole note is held in memory, which is fine for short sounds.
            var clip = AudioClip.Create(name, sampleCount, 1, k_SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
