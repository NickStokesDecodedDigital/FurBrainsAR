using UnityEngine;
using UnityEngine.Audio;

namespace FurBrains
{
    /// <summary>
    /// The lines the FurBrain says on her own: hello once she's been summoned, then "I'm hungry" when the Hunger meter
    /// is low and "Let's play" when the Play meter is low, without nagging.
    /// </summary>
    /// <remarks>
    /// Sits on the pet root next to FurBrainPet and FurBrainSpeech (the pet builder adds it). It only decides
    /// <i>when</i> to speak; FurBrainSpeech plays the line and moves her mouth. (The tap voice line stays in
    /// FurBrainPet, as it's a reaction to a tap.)
    ///
    /// <b>Hello</b> is said when FurBrainPet's summon animation ends: Start works out that moment (start time + the
    /// clip's length) and Update checks the clock each frame.
    ///
    /// <b>Comments</b> use a second deadline (<c>m_NextCommentTime</c>), also checked every frame:
    /// <list type="bullet">
    /// <item>While she's <b>busy</b> (speaking, hello still to come, a minigame running, the food or minigame menu
    /// open), the deadline keeps being pushed to "now + Comment Pause", so she waits for some calm after each of
    /// those ends.</item>
    /// <item>After a comment, the deadline moves Comment Cooldown ahead, so she doesn't repeat herself.</item>
    /// </list>
    /// </remarks>
    [RequireComponent(typeof(FurBrainSpeech))]
    public class FurBrainComment : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional voice line (clip or Audio Random Container) said once FurBrainPet's summon animation has " +
                 "finished (straight away if there's no summon animation).")]
        AudioResource m_Hello;

        [SerializeField]
        [Tooltip("Voice line (clip or Audio Random Container) said when the Hunger meter is low, e.g. \"I'm hungry\". " +
                 "Empty = she never mentions food.")]
        AudioResource m_HungryLine;

        [SerializeField]
        [Tooltip("Voice line said when the Play meter is low, e.g. \"Let's play\". Empty = she never asks to play.")]
        AudioResource m_PlayLine;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Speak up when Hunger or Play is below this fraction of full (0.3 = below 30 %). " +
                 "If both are low, she mentions the lower one.")]
        float m_CommentBelow = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("Minimum seconds between two comments, so she doesn't nag.")]
        float m_CommentCooldown = 60f;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds of calm needed before a comment. Restarts whenever she's busy: speaking (hello, tap line), " +
                 "a minigame running, or the food/minigame menu open. So she never speaks the moment something ends.")]
        float m_CommentPause = 8f;

        FurBrainSpeech m_Speech;
        FurBrainPet m_Pet;
        FurBrainNeeds m_Needs;
        MinigameRunner m_Runner;
        UnityEngine.XR.Templates.AR.ARTemplateMenuManager m_MenuManager; // owns the food menu
        float m_HelloTime = -1f; // when to say the Hello line (Time.time); -1 = not waiting to
        float m_NextCommentTime; // Time.time from which a comment may be said

        void Awake()
        {
            m_Speech = GetComponent<FurBrainSpeech>(); // RequireComponent guarantees it's there
            m_Pet = GetComponent<FurBrainPet>();       // only asked about its summon animation, for hello's timing
            // The pet is spawned at runtime, so scene objects can't be dragged in; look them up instead.
            m_Needs = FindAnyObjectByType<FurBrainNeeds>(); // on the scene's FurBrains Game object
            m_Runner = FindAnyObjectByType<MinigameRunner>();
            // Include inactive objects, as PopupMenu does, in case the menu manager's object is switched off.
            m_MenuManager = FindAnyObjectByType<UnityEngine.XR.Templates.AR.ARTemplateMenuManager>(FindObjectsInactive.Include);
        }

        void Start()
        {
            // Say hello when the summon animation ends: clip.length is its duration in seconds. FurBrainPet starts
            // that animation in its own Start, this same frame.
            if (m_Hello != null)
            {
                var summon = m_Pet != null && m_Pet.playSummonAnimation ? m_Pet.summonAnimation : null;
                var animationSeconds = summon != null ? summon.length : 0f;
                m_HelloTime = Time.time + animationSeconds;
            }

            // No comment straight after being summoned. (While hello is still to come, Update keeps pushing this
            // back, so the first comment comes at least Comment Pause after hello.)
            m_NextCommentTime = Time.time + m_CommentPause;
        }

        void Update()
        {
            if (m_HelloTime >= 0f && Time.time >= m_HelloTime)
            {
                m_HelloTime = -1f; // only once
                m_Speech.Say(m_Hello);
            }

            if (m_Needs == null || (m_HungryLine == null && m_PlayLine == null))
                return;

            if (IsBusy())
            {
                m_NextCommentTime = Mathf.Max(m_NextCommentTime, Time.time + m_CommentPause);
                return;
            }

            if (Time.time < m_NextCommentTime)
                return;

            var line = PickLine();
            if (line == null)
                return; // nothing low enough to mention

            m_Speech.Say(line);
            m_NextCommentTime = Time.time + m_CommentCooldown;
        }

        /// <summary>Talking, hello still to come, in a minigame, or a menu is open.</summary>
        bool IsBusy() =>
            m_Speech.isSpeaking ||
            m_HelloTime >= 0f ||
            (m_Runner != null && m_Runner.isPlaying) ||
            (m_MenuManager != null && m_MenuManager.isFoodMenuShown) ||
            PopupMenu.anyOpen; // the minigame menu

        /// <summary>
        /// The line for the lowest need below Comment Below (skipping a need whose line is empty), or null if neither
        /// need is low.
        /// </summary>
        AudioResource PickLine()
        {
            var hunger = m_Needs.hungerNormalized;
            var play = m_Needs.playNormalized;
            var hungryLow = hunger < m_CommentBelow && m_HungryLine != null;
            var playLow = play < m_CommentBelow && m_PlayLine != null;

            if (hungryLow && (!playLow || hunger <= play))
                return m_HungryLine;
            if (playLow)
                return m_PlayLine;
            return null;
        }

        // Testing aid: right-click the component header on the spawned FurBrain in Play mode. Skips the timers and
        // the busy check, but still needs a low need (use FurBrainNeeds → Make Hungry / Make Bored first).
        [ContextMenu("Say Comment Now")]
        void SayCommentNow()
        {
            if (m_Needs == null || m_Speech == null)
                return;
            var line = PickLine();
            if (line == null)
            {
                Debug.Log("[FurBrains] No need is below Comment Below (or its line is empty), so there's nothing to say.", this);
                return;
            }
            m_Speech.Say(line);
            m_NextCommentTime = Time.time + m_CommentCooldown;
        }
    }
}
