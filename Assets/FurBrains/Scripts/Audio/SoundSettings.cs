using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// The game-wide "mute sounds" setting, saved on the device. Muting silences every sound in the app at once.
    /// </summary>
    /// <remarks>
    /// <b>How it mutes:</b> <c>AudioListener.volume</c> is Unity's master volume: everything heard through the
    /// listener (on the AR camera) is multiplied by it, so 0 silences all AudioSources without touching any of them.
    ///
    /// <b>Why a static class:</b> there's one setting for the whole app, and it must apply at start-up even if the
    /// Options menu (whose UI is switched off until opened) is never touched. A <c>static class</c> has no instances
    /// and no GameObject, like a C++ namespace with a file-scope variable.
    ///
    /// <c>[RuntimeInitializeOnLoadMethod]</c> asks Unity to call the method automatically when the game starts (in a
    /// build, and every time you press Play), a bit like a C++ static initialiser. It loads the saved choice.
    /// </remarks>
    public static class SoundSettings
    {
        /// <summary>PlayerPrefs key the setting is saved under.</summary>
        public const string SaveKey = "FurBrains.Muted";

        static bool s_Muted;

        /// <summary>Whether all sound is muted. Setting it applies and saves it straight away.</summary>
        public static bool muted
        {
            get => s_Muted;
            set
            {
                s_Muted = value;
                Apply();
                PlayerPrefs.SetInt(SaveKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadSaved()
        {
            s_Muted = PlayerPrefs.GetInt(SaveKey, 0) == 1;
            Apply();
        }

        static void Apply() => AudioListener.volume = s_Muted ? 0f : 1f;
    }
}
