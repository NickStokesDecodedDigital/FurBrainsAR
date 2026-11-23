using System;
using System.Globalization;
using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Passes messages between this game and a Flutter app that embeds it (the FurBrains parent app), through the
    /// flutter_embed_unity plugin. Lives on the scene's "FurBrains Game" object.
    /// </summary>
    /// <remarks>
    /// Both directions are plain strings, on the device only (no network):
    /// <list type="bullet">
    /// <item><b>Unity → Flutter:</b> JSON like <c>{"type":"needs","coins":120,"food":0.3,"fun":0.75}</c>, sent with
    /// the plugin's <c>SendToFlutter.Send</c>. Every message carries the current coins and needs (food = hunger,
    /// fun = play, both 0..1). Types: "ready" once at start, "coins" when the balance changes, "needs" when food or
    /// fun has changed by at least 1 % (and when the app is paused), "exit" when the player presses the exit button.
    /// </item>
    /// <item><b>Flutter → Unity:</b> Flutter calls e.g. <c>sendToUnity("FurBrains Game", "SetNeeds",
    /// '{"food":0.3,"fun":0.75}')</c>, which runs the public method of that name below. Unity finds it by name at
    /// runtime (like looking up a function pointer in a table by its string name), so these methods must stay
    /// public, take one string, and keep their names.</item>
    /// </list>
    /// In the Editor, <c>SendToFlutter.Send</c> only logs to the Console. In a normal APK built straight from Unity
    /// (no Flutter app around it) the plugin's Java side is missing and the call throws, so the first failure switches
    /// sending off and the game carries on as usual.
    /// </remarks>
    public class FlutterBridge : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        PlayerWallet m_Wallet;

        [SerializeField]
        [Tooltip("Found in the scene automatically if left empty.")]
        FurBrainNeeds m_Needs;

        // Change (as a 0..1 fraction) that triggers a "needs" message. The slow drain crosses it about every two
        // minutes; feeding and minigames cross it straight away.
        const float k_NeedsChangeToSend = 0.01f;

        // True once we know there's no Flutter app to talk to (static: one answer for the whole app run).
        static bool s_NoHost;

        // The food and fun values in the last message sent, to notice when they've changed enough to send again.
        float m_SentFood = -1f;
        float m_SentFun = -1f;

        /// <summary>
        /// The shape of every message sent to Flutter. JsonUtility turns its public fields into JSON
        /// (e.g. {"type":"coins","coins":120,"food":0.3,"fun":0.75}). [Serializable] marks it as convertible, like a
        /// POD struct you'd memcpy.
        /// </summary>
        [Serializable]
        class Message
        {
            public string type;
            public int coins;
            public float food;
            public float fun;
        }

        /// <summary>What Flutter sends to <see cref="SetNeeds"/>: food and fun as 0..1.</summary>
        [Serializable]
        class NeedsMessage
        {
            public float food;
            public float fun;
        }

        /// <summary>True when the game runs inside a Flutter app (as far as we know: false once a send has failed).</summary>
        public static bool hasHost => !s_NoHost;

        void Awake()
        {
            if (m_Wallet == null) m_Wallet = FindAnyObjectByType<PlayerWallet>();
            if (m_Needs == null) m_Needs = FindAnyObjectByType<FurBrainNeeds>();
        }

        void OnEnable()
        {
            if (m_Wallet != null)
                m_Wallet.coinsChanged += OnCoinsChanged;
        }

        void OnDisable()
        {
            if (m_Wallet != null)
                m_Wallet.coinsChanged -= OnCoinsChanged;
        }

        // Start runs after every object's Awake, so the wallet and needs have loaded their saved values by now.
        void Start() => Send("ready");

        // FurBrainNeeds has no change event (its values drain a little every frame), so check here instead.
        void Update()
        {
            if (m_Needs == null || s_NoHost)
                return;
            if (Mathf.Abs(m_Needs.hungerNormalized - m_SentFood) >= k_NeedsChangeToSend ||
                Mathf.Abs(m_Needs.playNormalized - m_SentFun) >= k_NeedsChangeToSend)
                Send("needs");
        }

        // When the app goes to the background (or Flutter hides the game), make sure Flutter has the latest values.
        void OnApplicationPause(bool paused)
        {
            if (paused)
                Send("needs");
        }

        void OnCoinsChanged(int coins) => Send("coins");

        /// <summary>Called from Flutter: gives the player coins. The amount arrives as text, e.g. "10".</summary>
        public void AddCoins(string amount)
        {
            // InvariantCulture: parse numbers the same way whatever language the phone is set to.
            if (m_Wallet != null && int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                m_Wallet.Add(value);
            else
                Debug.LogWarning($"[FurBrains] FlutterBridge.AddCoins: couldn't use \"{amount}\".", this);
        }

        /// <summary>
        /// Called from Flutter when the game opens: sets the FurBrain's needs to the parent app's food and fun values
        /// for the child who is playing, as JSON like <c>{"food":0.3,"fun":0.75}</c>.
        /// </summary>
        public void SetNeeds(string json)
        {
            if (m_Needs == null)
                return;
            try
            {
                var needs = JsonUtility.FromJson<NeedsMessage>(json);
                m_Needs.SetLevels(needs.food, needs.fun);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FurBrains] FlutterBridge.SetNeeds: couldn't use \"{json}\" ({e.Message}).", this);
            }
        }

        /// <summary>
        /// Leaves the game and goes back to the parent app. Hook the exit button's On Click () up to this.
        /// Sends the latest needs first so the parent app shows them straight away. Does nothing without a Flutter app.
        /// </summary>
        public void ExitToHost()
        {
            if (s_NoHost)
            {
                Debug.Log("[FurBrains] Exit pressed, but there's no Flutter app to go back to.", this);
                return;
            }
            Send("needs");
            Send("exit");
        }

        void Send(string type)
        {
            if (s_NoHost)
                return;

            var message = new Message
            {
                type = type,
                coins = m_Wallet != null ? m_Wallet.coins : 0,
                food = m_Needs != null ? m_Needs.hungerNormalized : 0f,
                fun = m_Needs != null ? m_Needs.playNormalized : 0f,
            };
            m_SentFood = message.food;
            m_SentFun = message.fun;

            try
            {
                SendToFlutter.Send(JsonUtility.ToJson(message));
            }
            catch (Exception e)
            {
                // Not running inside a Flutter app (e.g. a normal APK build): stop trying.
                s_NoHost = true;
                Debug.Log($"[FurBrains] No Flutter host, messages to Flutter are off ({e.GetType().Name}).");
            }
        }

        // Testing aids: right-click the component header in the Inspector during Play mode.
        [ContextMenu("Test: Add 10 Coins (as if from Flutter)")]
        void TestAddCoins() => AddCoins("10");

        [ContextMenu("Test: Set Needs to 20% food, 90% fun (as if from Flutter)")]
        void TestSetNeeds() => SetNeeds("{\"food\":0.2,\"fun\":0.9}");
    }
}
