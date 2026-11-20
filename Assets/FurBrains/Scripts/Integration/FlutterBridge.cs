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
    /// <item><b>Unity → Flutter:</b> JSON like <c>{"type":"coins","coins":120}</c>, sent with the plugin's
    /// <c>SendToFlutter.Send</c>. "ready" is sent once at start; "coins" whenever the balance changes.</item>
    /// <item><b>Flutter → Unity:</b> Flutter calls <c>sendToUnity("FurBrains Game", "AddCoins", "10")</c>, which runs
    /// the public method of that name below. Unity finds it by name at runtime (like looking up a function pointer
    /// in a table by its string name), so it must stay public, take one string, and keep its name.</item>
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

        // True once we know there's no Flutter app to talk to (static: one answer for the whole app run).
        static bool s_NoHost;

        /// <summary>
        /// The shape of every message sent to Flutter. JsonUtility turns its public fields into JSON
        /// (e.g. {"type":"coins","coins":120}). [Serializable] marks it as convertible, like a POD struct you'd memcpy.
        /// </summary>
        [Serializable]
        class Message
        {
            public string type;
            public int coins;
        }

        void Awake()
        {
            if (m_Wallet == null) m_Wallet = FindAnyObjectByType<PlayerWallet>();
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

        // Start runs after every object's Awake, so the wallet has loaded its saved balance by now.
        void Start() => Send("ready");

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

        void Send(string type)
        {
            if (s_NoHost)
                return;

            var json = JsonUtility.ToJson(new Message { type = type, coins = m_Wallet != null ? m_Wallet.coins : 0 });
            try
            {
                SendToFlutter.Send(json);
            }
            catch (Exception e)
            {
                // Not running inside a Flutter app (e.g. a normal APK build): stop trying.
                s_NoHost = true;
                Debug.Log($"[FurBrains] No Flutter host, messages to Flutter are off ({e.GetType().Name}).");
            }
        }

        // Testing aid: right-click the component header in the Inspector during Play mode.
        [ContextMenu("Test: Add 10 Coins (as if from Flutter)")]
        void TestAddCoins() => AddCoins("10");
    }
}
