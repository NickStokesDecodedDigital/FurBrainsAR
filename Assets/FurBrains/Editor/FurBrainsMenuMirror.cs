using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FurBrains.EditorTools.FurBrainsFeedingSetup; // FindFirstInScene

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Tools → FurBrains → Copy Food Menu Look To Minigame Menu: makes the minigame menu match the food menu's layout
    /// and styling, so you only design one menu by hand.
    /// </summary>
    /// <remarks>
    /// Both menus are copies of the same template, so their objects have the same names and nesting. The tool walks the
    /// food menu's tree and, for each object, finds the one at the same path in the minigame menu and copies:
    /// <list type="bullet">
    /// <item>position and size (RectTransform),</item>
    /// <item>look only: Image colour/sprite/type, Button colours, text font/size/alignment (never the text itself),</item>
    /// <item>whether it's switched on.</item>
    /// </list>
    /// Objects that exist only in the food menu (e.g. a title, the X button's icon) are duplicated into the minigame
    /// menu. Things that must stay different are left alone: each menu's need badge icon and meter, its scripts and
    /// their scene references, the menu buttons themselves (built at runtime), and whether the panel is open.
    /// Everything goes through Undo, so Ctrl+Z reverts it. Re-run it after changing the food menu again.
    /// </remarks>
    public static class FurBrainsMenuMirror
    {
        // Food-only objects whose copy gets a different name and text in the minigame menu (name, text).
        static readonly (string food, string minigame, string text)[] k_Renames =
        {
            ("Feed FurBrain", "Play FurBrain", "Play with Ziara"),
        };

        [MenuItem("Tools/FurBrains/Copy Food Menu Look To Minigame Menu")]
        static void Run()
        {
            var food = FindFirstInScene("Food Menu Animator", "Object Menu Animator");
            var minigame = FindFirstInScene("Minigame Menu Animator");
            if (food == null || minigame == null)
            {
                Debug.LogError("[FurBrains] Couldn't find the Food Menu Animator and Minigame Menu Animator in the open scene.");
                return;
            }

            // After Restyle Minigame Menu As Cards the two menus are deliberately different; copying would undo it.
            var minigameGrid = minigame.GetComponentInChildren<GridLayoutGroup>(true);
            if (minigameGrid != null)
            {
                Debug.LogWarning("[FurBrains] The minigame menu uses the card layout now, so the food menu's look isn't " +
                                 "copied onto it.");
                return;
            }

            Undo.SetCurrentGroupName("Copy Food Menu Look");

            // The open/close animation (e.g. the FurBrainsMenu controller that hides the panel fully).
            var srcAnimator = food.GetComponent<Animator>();
            var dstAnimator = minigame.GetComponent<Animator>();
            if (srcAnimator != null && dstAnimator != null)
            {
                Undo.RecordObject(dstAnimator, "Copy Food Menu Look");
                dstAnimator.runtimeAnimatorController = srcAnimator.runtimeAnimatorController;
            }

            Mirror(food.transform, minigame.transform);
            CopyMenuSettings(food, minigame);
            Debug.Log("[FurBrains] Copied the food menu's look to the minigame menu. Ctrl+Z undoes it.");
        }

        /// <summary>Copies src's look onto dst, then does the same for every child (recursion, like a tree walk in C++).</summary>
        static void Mirror(Transform src, Transform dst)
        {
            CopyLook(src, dst);

            // The buttons inside Content are made at runtime from the template button; only match the existing ones.
            var isContent = src.name == "Content";

            for (int i = 0; i < src.childCount; i++)
            {
                var srcChild = src.GetChild(i);
                var dstChild = FindChild(dst, MinigameName(srcChild.name));
                if (dstChild == null)
                {
                    if (isContent)
                        continue;
                    dstChild = Duplicate(srcChild, dst);
                }
                Mirror(srcChild, dstChild);
            }
        }

        static void CopyLook(Transform src, Transform dst)
        {
            // Whether the menu panel is open is the animator's business, so "Object Menu" keeps its own on/off state.
            if (src.name != "Object Menu" && dst.gameObject.activeSelf != src.gameObject.activeSelf)
            {
                Undo.RecordObject(dst.gameObject, "Copy Food Menu Look");
                dst.gameObject.SetActive(src.gameObject.activeSelf);
            }

            if (src is RectTransform s && dst is RectTransform d)
            {
                Undo.RecordObject(d, "Copy Food Menu Look");
                d.anchorMin = s.anchorMin;
                d.anchorMax = s.anchorMax;
                d.pivot = s.pivot;
                d.anchoredPosition = s.anchoredPosition;
                d.sizeDelta = s.sizeDelta;
                d.localScale = s.localScale;
                d.localRotation = s.localRotation;
            }

            var srcImage = src.GetComponent<Image>();
            var dstImage = dst.GetComponent<Image>();
            if (srcImage != null && dstImage != null)
            {
                Undo.RecordObject(dstImage, "Copy Food Menu Look");
                dstImage.color = srcImage.color;
                dstImage.type = srcImage.type;
                dstImage.pixelsPerUnitMultiplier = srcImage.pixelsPerUnitMultiplier;
                dstImage.preserveAspect = srcImage.preserveAspect;
                dstImage.fillCenter = srcImage.fillCenter;
                if (!KeepsOwnSprite(src))
                    dstImage.sprite = srcImage.sprite;
            }

            var srcButton = src.GetComponent<Button>();
            var dstButton = dst.GetComponent<Button>();
            if (srcButton != null && dstButton != null)
            {
                Undo.RecordObject(dstButton, "Copy Food Menu Look");
                dstButton.transition = srcButton.transition;
                dstButton.colors = srcButton.colors;
            }

            var srcText = src.GetComponent<TMP_Text>();
            var dstText = dst.GetComponent<TMP_Text>();
            if (srcText != null && dstText != null)
            {
                Undo.RecordObject(dstText, "Copy Food Menu Look");
                dstText.font = srcText.font;
                dstText.fontSize = srcText.fontSize;
                dstText.fontStyle = srcText.fontStyle;
                dstText.color = srcText.color;
                dstText.alignment = srcText.alignment;
                dstText.textWrappingMode = srcText.textWrappingMode;
            }
        }

        // Images whose picture differs per menu: the need badge's icon (food vs play) and the item icons (set at runtime).
        static bool KeepsOwnSprite(Transform t) =>
            t.name == "ObjectIcon" || (t.name == "Icon" && t.parent != null && t.parent.name == "Need Badge");

        static Transform Duplicate(Transform srcChild, Transform dstParent)
        {
            var copy = Object.Instantiate(srcChild.gameObject, dstParent, false);
            copy.name = MinigameName(srcChild.name);
            copy.transform.SetSiblingIndex(Mathf.Min(srcChild.GetSiblingIndex(), dstParent.childCount - 1));
            foreach (var rename in k_Renames)
                if (rename.food == srcChild.name && copy.TryGetComponent<TMP_Text>(out var text))
                    text.text = rename.text;
            Undo.RegisterCreatedObjectUndo(copy, "Copy Food Menu Look");
            return copy.transform;
        }

        static string MinigameName(string foodName)
        {
            foreach (var rename in k_Renames)
                if (rename.food == foodName)
                    return rename.minigame;
            return foodName;
        }

        static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);
            return null;
        }

        /// <summary>
        /// The menu scripts' size settings (Menu Scale, Bottom Label Raise). They're private fields, so they're set
        /// through SerializedObject: the same route the Inspector uses, which also records Undo.
        /// </summary>
        static void CopyMenuSettings(GameObject food, GameObject minigame)
        {
            var src = food.GetComponentInChildren<IconButtonMenu>(true);
            var dst = minigame.GetComponentInChildren<IconButtonMenu>(true);
            if (src == null || dst == null)
                return;

            var srcSo = new SerializedObject(src);
            var dstSo = new SerializedObject(dst);
            foreach (var field in new[] { "m_MenuScale", "m_BottomLabelRaise" })
                dstSo.FindProperty(field).floatValue = srcSo.FindProperty(field).floatValue;
            dstSo.ApplyModifiedProperties();
        }
    }
}
