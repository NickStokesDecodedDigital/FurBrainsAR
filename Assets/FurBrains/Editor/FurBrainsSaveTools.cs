using UnityEditor;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Testing aid: Tools → FurBrains → Clear Saved Progress deletes the saved coins and hunger, so the next Play
    /// session starts like a brand-new player (Starting Coins / Starting Hunger apply again).
    /// </summary>
    /// <remarks>
    /// In the Editor, PlayerPrefs live in the Windows registry under this project's company/product name; on a phone
    /// they're in the app's private storage (cleared by uninstalling or clearing the app's data).
    /// </remarks>
    public static class FurBrainsSaveTools
    {
        [MenuItem("Tools/FurBrains/Clear Saved Progress")]
        static void ClearSavedProgress()
        {
            if (Application.isPlaying)
            {
                // The running game would just save its current values again on exit.
                Debug.LogWarning("[FurBrains] Stop Play mode first, then clear saved progress.");
                return;
            }

            PlayerPrefs.DeleteKey(PlayerWallet.SaveKey);
            PlayerPrefs.DeleteKey(FurBrainNeeds.SaveKey);
            PlayerPrefs.Save();
            Debug.Log("[FurBrains] Cleared saved coins and hunger. The next Play session starts fresh.");
        }
    }
}
