using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace FurBrains
{
    /// <summary>
    /// Bubble Pop: the FurBrain blows bubbles that drift toward the player and float upward. Tap them to pop them
    /// before they float away. Golden bubbles are worth more. The game lasts a fixed time, so nobody gets knocked out;
    /// the score is how many points were popped.
    /// </summary>
    /// <remarks>
    /// Bubbles are copies of a hidden template child ("Bubble"), made with Instantiate and removed with Destroy. They
    /// are children of this game object, so when the runner destroys the game, any bubbles left go with it.
    /// </remarks>
    public class BubblePopMinigame : Minigame
    {
        /// <summary>One bubble in flight. A plain class: just data, updated by the game.</summary>
        class Bubble
        {
            public Transform transform;
            public Vector3 velocity;
            public float bornAt;
            public float radius;
            public bool golden;
            public float wobbleOffset; // so bubbles don't all wobble in step
        }

        [SerializeField]
        [Tooltip("Hidden template bubble (a child of this prefab) that each new bubble is copied from.")]
        GameObject m_BubbleTemplate;

        [SerializeField]
        [Tooltip("Material for golden bubbles.")]
        Material m_GoldMaterial;

        [Header("Game")]
        [SerializeField]
        [Tooltip("Length of the game in seconds.")]
        float m_Duration = 30f;

        [SerializeField]
        [Tooltip("Seconds between new bubbles.")]
        float m_SpawnInterval = 0.7f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Chance that a new bubble is golden.")]
        float m_GoldenChance = 0.1f;

        [SerializeField]
        [Tooltip("Points for a golden bubble (a normal one is 1).")]
        int m_GoldenPoints = 3;

        [Header("Bubbles")]
        [SerializeField]
        [Tooltip("Smallest and largest bubble size (diameter), in metres.")]
        Vector2 m_SizeRange = new(0.06f, 0.09f);

        [SerializeField]
        [Tooltip("Speed toward the player, in metres per second.")]
        float m_DriftSpeed = 0.12f;

        [SerializeField]
        [Tooltip("How far bubbles spread sideways, as a fraction of the drift speed.")]
        float m_Spread = 0.6f;

        [SerializeField]
        [Tooltip("How far bubbles spread up and down, as a fraction of the drift speed (added to the rise).")]
        float m_VerticalSpread = 0.5f;

        [SerializeField]
        [Tooltip("Upward float speed, in metres per second.")]
        float m_RiseSpeed = 0.05f;

        [SerializeField]
        [Tooltip("A bubble pops by itself after this many seconds (no penalty).")]
        float m_Lifetime = 6f;

        [SerializeField]
        [Tooltip("Taps count if within this many times the bubble's on-screen radius of its centre.")]
        float m_TapRadiusMultiplier = 1.5f;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("...or within this fraction of the screen width, whichever is bigger.")]
        float m_MinTapRadiusFraction = 0.06f;

        [SerializeField]
        [Tooltip("Where bubbles come from, as a fraction of the FurBrain's height.")]
        float m_SpawnHeight = 0.7f;

        [SerializeField]
        [Tooltip("Blend shapes the FurBrain holds for the whole game (\"O\" = a round mouth, as if blowing bubbles). " +
                 "Empty = keep its normal mood face.")]
        string[] m_PetExpression = { "O" };

        FurBrainMood m_PetMood; // the FurBrain's face owner, so the held face can be released when the game ends

        [Header("Sound")]
        [SerializeField]
        [Tooltip("Played when the player pops a bubble. An Audio Clip, or an Audio Random Container for variety " +
                 "(random clip, pitch and volume each time).")]
        AudioResource m_PopSound; // AudioResource is the base class of both AudioClip and AudioRandomContainer

        [SerializeField]
        [Tooltip("Played instead of Pop Sound when a golden bubble is popped (e.g. a sparkly chime). " +
                 "Empty = golden bubbles use Pop Sound too.")]
        AudioResource m_GoldenPopSound;

        AudioSource m_Audio;

        [Header("Pop effect")]
        [SerializeField, Min(0)]
        [Tooltip("Droplets that fly out when the player pops a bubble (half as many when one pops by itself).")]
        int m_PopDroplets = 10;


        // The particle systems' Start Size and Shape → Radius are set in their Inspectors for an average-sized
        // bubble; Burst scales them for bigger or smaller bubbles. These hold the Inspector values, read once at start.
        ParticleSystem.MinMaxCurve m_PopSize, m_GoldSize;
        float m_PopRadius, m_GoldRadius;
        float m_AverageBubbleRadius;

        [SerializeField]
        [Tooltip("Particle system (a child of this prefab) that sprays droplets in the bubble material. " +
                 "Created by Tools → FurBrains → Add Bubble Pop Effects.")]
        ParticleSystem m_PopBurst;

        [SerializeField]
        [Tooltip("Particle system (a child of this prefab) that sprays gold droplets for golden bubbles. " +
                 "If empty, golden bubbles use Pop Burst.")]
        ParticleSystem m_GoldBurst;

        // List<T> is C#'s std::vector<T>. Bubbles are classes, so the list holds references (like Bubble*).
        readonly List<Bubble> m_Bubbles = new();
        float m_StartTime;
        float m_NextSpawnTime;

        public override MinigameResultKind resultKind => MinigameResultKind.Score;

        protected override void OnBegin()
        {
            // The prefab may not have an AudioSource yet: add one if it's missing.
            m_Audio = GetComponent<AudioSource>();
            if (m_Audio == null)
                m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;

            currentScore = 0;
            m_StartTime = Time.time;
            m_NextSpawnTime = Time.time + 0.5f; // a moment to get ready
            timeRemaining = m_Duration;
            timeRemainingNormalized = 1f;
            if (m_BubbleTemplate != null)
                m_BubbleTemplate.SetActive(false);

            // Remember the sizes set in the particle systems' Inspectors (Burst scales them, rather than replacing them).
            m_AverageBubbleRadius = (m_SizeRange.x + m_SizeRange.y) * 0.25f; // average diameter ÷ 2
            if (m_PopBurst != null)
            {
                m_PopSize = m_PopBurst.main.startSize;
                m_PopRadius = m_PopBurst.shape.radius;
            }
            if (m_GoldBurst != null)
            {
                m_GoldSize = m_GoldBurst.main.startSize;
                m_GoldRadius = m_GoldBurst.shape.radius;
            }

            // The FurBrain blows the bubbles: hold an "O" mouth for the whole game. Faces always go through
            // FurBrainMood (the face's single owner), never FurBrainFace directly, so moods and reactions don't clash.
            if (context.pet != null && m_PetExpression.Length > 0 && context.pet.TryGetComponent(out m_PetMood))
                m_PetMood.Hold(m_PetExpression);
        }

        // The runner destroys the game when it ends (finished or quit), so this is the one place to undo the face.
        void OnDestroy()
        {
            // Unity's == null is also true for a FurBrain destroyed in the meantime, so this is safe either way.
            if (m_PetMood != null)
                m_PetMood.ReleaseHold();
        }

        void Update()
        {
            if (!isRunning)
                return;

            // Timer: when it runs out, the game ends with the points collected.
            timeRemaining = Mathf.Max(0f, m_Duration - (Time.time - m_StartTime));
            timeRemainingNormalized = m_Duration > 0f ? timeRemaining / m_Duration : 0f;
            if (timeRemaining <= 0f)
            {
                FinishWithScore(currentScore);
                return;
            }

            if (Time.time >= m_NextSpawnTime)
            {
                m_NextSpawnTime = Time.time + m_SpawnInterval;
                SpawnBubble();
            }

            MoveBubbles();
            CheckTap();
        }

        void SpawnBubble()
        {
            if (m_BubbleTemplate == null)
                return;

            var pet = context.petTransform;
            var size = Random.Range(m_SizeRange.x, m_SizeRange.y);
            var golden = Random.value < m_GoldenChance;

            // Instantiate(original, parent) copies the template as a child of this game.
            var go = Instantiate(m_BubbleTemplate, transform);
            go.SetActive(true);
            go.transform.position = PetPoint(m_SpawnHeight);
            go.transform.localScale = Vector3.one * size;
            if (golden && m_GoldMaterial != null && go.TryGetComponent<Renderer>(out var bubbleRenderer))
                bubbleRenderer.sharedMaterial = m_GoldMaterial;

            // Toward the player (the FurBrain faces them), plus a random spread sideways and up/down, so the bubbles
            // fan out in a cone rather than a flat sheet. The rise is added on top so they still float up overall.
            var sideways = Random.Range(-m_Spread, m_Spread) * m_DriftSpeed;
            var vertical = Random.Range(-m_VerticalSpread, m_VerticalSpread) * m_DriftSpeed;
            var velocity = pet.forward * m_DriftSpeed + pet.right * sideways + pet.up * (m_RiseSpeed + vertical);

            m_Bubbles.Add(new Bubble
            {
                transform = go.transform,
                velocity = velocity,
                bornAt = Time.time,
                radius = size * 0.5f,
                golden = golden,
                wobbleOffset = Random.Range(0f, 10f),
            });
        }

        void MoveBubbles()
        {
            var cam = context.camera.transform;
            // Loop backwards so removing an item doesn't skip the next one (same as erasing from a vector in C++).
            for (var i = m_Bubbles.Count - 1; i >= 0; i--)
            {
                var bubble = m_Bubbles[i];
                var age = Time.time - bubble.bornAt;

                // Drift, plus a gentle side-to-side wobble.
                var wobble = cam.right * (Mathf.Sin((age + bubble.wobbleOffset) * 3f) * 0.03f);
                bubble.transform.position += (bubble.velocity + wobble) * Time.deltaTime;

                // Floated away: too old (pops by itself, with a smaller burst), or got past the player (just goes).
                var behind = Vector3.Dot(bubble.transform.position - cam.position, cam.forward) < 0f;
                if (age > m_Lifetime && !behind)
                {
                    Burst(bubble, m_PopDroplets / 2);
                    RemoveBubble(i);
                }
                else if (behind)
                {
                    RemoveBubble(i);
                }
            }
        }

        void CheckTap()
        {
            if (!TryGetTap(out var tap))
                return;

            // One tap pops at most one bubble: the nearest to the camera that the tap is on (it's the one in front).
            var best = -1;
            var bestDistance = float.MaxValue;
            var cam = context.camera.transform.position;
            for (var i = 0; i < m_Bubbles.Count; i++)
            {
                var bubble = m_Bubbles[i];
                if (!IsTapNear(tap, bubble.transform.position, bubble.radius, m_TapRadiusMultiplier, m_MinTapRadiusFraction))
                    continue;
                var distance = Vector3.Distance(bubble.transform.position, cam);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            if (best < 0)
                return;

            var popped = m_Bubbles[best];
            currentScore += popped.golden ? m_GoldenPoints : 1;
            // Golden bubbles use their own sound if one is set. (Not "m_GoldenPopSound ?? m_PopSound": C#'s ?? skips
            // Unity's own null check, and in the Editor an empty object field can hold a placeholder that ?? treats
            // as set. Unity's == / != handles it correctly.)
            var sound = popped.golden && m_GoldenPopSound != null ? m_GoldenPopSound : m_PopSound;
            PlayPop(sound);
            Burst(popped, m_PopDroplets);
            RemoveBubble(best);
        }

        /// <summary>Sprays droplets from a bubble's position, sized to the bubble.</summary>
        void Burst(Bubble bubble, int count)
        {
            // The particle systems are saved in the prefab (made by the editor tool), so this only positions,
            // sizes and fires them. Golden bubbles fall back to the normal burst if there's no gold one.
            var useGold = bubble.golden && m_GoldBurst != null;
            var ps = useGold ? m_GoldBurst : m_PopBurst;
            if (ps == null || count <= 0)
                return;
            ps.transform.position = bubble.transform.position;

            // The Inspector values are for an average bubble: scale them by how big this bubble is compared to that
            // (a bubble 20% bigger than average gets droplets and a spray area 20% bigger).
            var scale = m_AverageBubbleRadius > 0f ? bubble.radius / m_AverageBubbleRadius : 1f;
            var main = ps.main;
            main.startSize = Scaled(useGold ? m_GoldSize : m_PopSize, scale);
            var shape = ps.shape;
            shape.radius = (useGold ? m_GoldRadius : m_PopRadius) * scale;
            ps.Emit(count); // creates the droplets immediately, using the settings above
        }

        /// <summary>
        /// A copy of a particle value scaled by <paramref name="factor"/>. MinMaxCurve can be a single number, a random
        /// range or a curve, so all three parts are scaled. (It's a struct, so this changes a copy, not the original.)
        /// </summary>
        static ParticleSystem.MinMaxCurve Scaled(ParticleSystem.MinMaxCurve value, float factor)
        {
            value.constantMin *= factor;     // lower end of a random range
            value.constantMax *= factor;     // a single number, or the upper end of a random range
            value.curveMultiplier *= factor; // curve modes
            return value;
        }

        void PlayPop(AudioResource sound)
        {
            // "is" checks the real type and gives a typed variable if it matches (like a C++ dynamic_cast + null check).
            if (sound is AudioClip clip)
            {
                m_Audio.PlayOneShot(clip); // quick pops overlap
            }
            else if (sound != null)
            {
                // A container (PlayOneShot only accepts clips): set it as the source's resource and Play; it picks the
                // clip, pitch and volume itself. Play() restarts the source, so a very quick second pop cuts off the
                // tail of the first, which is barely noticeable with short pop sounds.
                m_Audio.resource = sound;
                m_Audio.Play();
            }
        }

        void RemoveBubble(int index)
        {
            var bubble = m_Bubbles[index];
            m_Bubbles.RemoveAt(index);
            // Destroy happens at the end of the frame (not immediately like C++ delete).
            Destroy(bubble.transform.gameObject);
        }
    }
}
