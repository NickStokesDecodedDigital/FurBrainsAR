using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Builds "ZiaraPet.prefab": the placeable, interactive version of Ziara that the AR ObjectSpawner creates.
    /// Also generates the Animator Controller it uses.
    /// </summary>
    /// <remarks>
    /// Structure of the generated prefab. The FurBrain can be tapped (and deleted via the template's delete button)
    /// but not moved, rotated or scaled by the player; FurBrainPet turns it to face the player itself.
    /// <code>
    /// ZiaraPet                 XRSimpleInteractable, CapsuleCollider, FurBrainPet
    /// └─ Model                scaled to toy size, rotated to face the root's +Z (the spawner points +Z at the
    ///    │                     camera), offset so the feet are at y = 0
    ///    └─ Ziara              nested Ziara.prefab (Animator, FurBrainFace, all mesh parts), left untouched
    /// </code>
    /// Ziara.prefab is <i>nested</i>, not copied, so rebuilding Ziara with the character builder flows through.
    /// Re-run this tool after changing it; it overwrites the generated assets in place (same GUIDs, so scene
    /// references such as the ObjectSpawner's prefab list stay valid).
    /// </remarks>
    public static class FurBrainsPetBuilder
    {
        const string k_CharacterPrefab = "Assets/FurBrains/Models/Ziara/Ziara.prefab";
        const string k_WaveClipFbx = "Assets/FurBrains/Animation/Clips/Waving_1__UE.FBX";
        // Placeholder idle until we pick one of the real idles in Animation/Clips/Idle/.
        const string k_IdleClipFbx = "Assets/FurBrains/Animation/Clips/Thinking_UE.FBX";

        const string k_ControllerPath = "Assets/FurBrains/Animation/FurBrainBasic.controller";
        const string k_FurMaterialPath = "Assets/FurBrains/Models/Ziara/Materials/M_Ziara_Fur.mat";
        const string k_MaskPath = "Assets/FurBrains/Animation/FurBrainBody.mask";
        const string k_PetPrefabPath = "Assets/FurBrains/Prefabs/ZiaraPet.prefab";

        // Height of the placed FurBrain in metres.
        const float k_TargetHeight = 0.3f;

        [MenuItem("Tools/FurBrains/Build Ziara Pet Prefab")]
        static void Build()
        {
            SetClipLooping(k_IdleClipFbx, true);
            SetClipLooping(k_WaveClipFbx, false);

            // Starting clips for the pet's animation fields; change them afterwards in the prefab's Inspector.
            var waveClip = LoadClip(k_WaveClipFbx);
            var idleClip = LoadClip(k_IdleClipFbx);
            if (waveClip == null || idleClip == null)
                return;

            var controller = BuildController();
            BuildPrefab(controller, idleClip, waveClip);
        }

        /// <summary>
        /// A clip-agnostic state machine. Idle (looping) is the default state; each action trigger (Summon, Tap, Feed)
        /// crossfades from any state into that action's state, which returns to Idle when its clip finishes.
        /// Triggering the same action again restarts it.
        /// </summary>
        /// <remarks>
        /// Every state plays an empty placeholder "slot" clip, stored inside the controller asset. FurBrainPet replaces
        /// the slots with the clips chosen in its Inspector fields (through an AnimatorOverrideController), so changing
        /// an animation never requires rebuilding this controller.
        /// </remarks>
        static AnimatorController BuildController()
        {
            EnsureFolder(Path.GetDirectoryName(k_ControllerPath));

            // Rebuild the contents of an existing controller rather than replacing the file, so its GUID survives.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(k_ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(k_ControllerPath);
            }
            else
            {
                foreach (var p in controller.parameters)
                    controller.RemoveParameter(p);
                var sm = controller.layers[0].stateMachine;
                foreach (var s in sm.states)
                    sm.RemoveState(s.state);
                foreach (var t in sm.anyStateTransitions)
                    sm.RemoveAnyStateTransition(t);
            }

            // The layers property hands back a copy of the array, so changes only stick once it's assigned back.
            var layers = controller.layers;
            layers[0].avatarMask = BuildBodyMask();
            controller.layers = layers;
            var stateMachine = controller.layers[0].stateMachine;

            var idle = stateMachine.AddState("Idle", new Vector3(300, 0));
            idle.motion = GetSlotClip(controller, FurBrainPet.IdleSlot);
            stateMachine.defaultState = idle;

            AddAction(controller, stateMachine, idle, "Summon", FurBrainPet.SummonTrigger, FurBrainPet.SummonSlot, 0);
            AddAction(controller, stateMachine, idle, "Tap", FurBrainPet.TapTrigger, FurBrainPet.TapSlot, 1);
            AddAction(controller, stateMachine, idle, "Feed", FurBrainPet.FeedTrigger, FurBrainPet.FeedSlot, 2);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>One play-once action: trigger → state (from any state) → back to Idle when the clip ends.</summary>
        static void AddAction(AnimatorController controller, AnimatorStateMachine stateMachine, AnimatorState idle,
            string stateName, string trigger, string slot, int row)
        {
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

            var state = stateMachine.AddState(stateName, new Vector3(600, row * 80));
            state.motion = GetSlotClip(controller, slot);

            var enter = stateMachine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
            enter.duration = 0.15f;
            enter.canTransitionToSelf = true;

            var exit = state.AddTransition(idle);
            exit.hasExitTime = true;
            exit.exitTime = 0.95f; // fraction of whichever clip ends up playing in this state
            exit.duration = 0.25f;
        }

        /// <summary>
        /// The empty placeholder clip for a slot, stored as a sub-asset inside the controller file (like the clips
        /// inside an FBX). Reused when the controller is rebuilt.
        /// </summary>
        static AnimationClip GetSlotClip(AnimatorController controller, string slotName)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == slotName);
            if (clip == null)
            {
                clip = new AnimationClip { name = slotName };
                AssetDatabase.AddObjectToAsset(clip, controller);
            }
            return clip;
        }

        /// <summary>
        /// An Avatar Mask that lets clips animate every bone except the top "root" bone (and the Animator's own object).
        /// </summary>
        /// <remarks>
        /// The Unreal animation exports key the "root" bone with Unreal's Z-up → Unity Y-up conversion baked in, while
        /// the character model already applies that conversion on its top object. Letting the clip drive "root" applies
        /// the -90° turn twice, which laid Ziara on her back. "root" carries no real motion in these clips (its moves are
        /// under 1 cm), so masking it out costs nothing. The mask is a whitelist of transform paths, relative to the
        /// Animator, built from the character prefab (all FurBrains share the skeleton, so it fits all of them).
        /// </remarks>
        static AvatarMask BuildBodyMask()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterPrefab);
            if (character == null)
                return null;

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(k_MaskPath);
            var isNew = mask == null;
            if (isNew)
                mask = new AvatarMask();

            var transforms = character.GetComponentsInChildren<Transform>(true);
            mask.transformCount = transforms.Length;
            for (var i = 0; i < transforms.Length; i++)
            {
                var path = AnimationUtility.CalculateTransformPath(transforms[i], character.transform);
                mask.SetTransformPath(i, path);
                mask.SetTransformActive(i, path != "" && path != "root");
            }

            if (isNew)
                AssetDatabase.CreateAsset(mask, k_MaskPath);
            else
                EditorUtility.SetDirty(mask);
            return mask;
        }

        static void BuildPrefab(RuntimeAnimatorController controller, AnimationClip idleClip, AnimationClip actionClip)
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterPrefab);
            if (characterPrefab == null)
            {
                Debug.LogError($"[FurBrains] {k_CharacterPrefab} not found. Build the character prefab first.");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(k_PetPrefabPath));
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("ZiaraPet");
                EditorSceneManager.MoveGameObjectToScene(root, scene);

                // Size / facing / ground offset go on a wrapper, never on the Animator's own object: an animation
                // clip with curves for its root would otherwise overwrite them every frame.
                var model = new GameObject("Model").transform;
                model.SetParent(root.transform, false);

                var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab, scene);
                character.transform.SetParent(model, false);
                FitCharacter(model);

                // Don't use ?? with Unity objects: Unity overrides == null, which ?? ignores.
                var animator = character.GetComponent<Animator>();
                if (animator == null)
                    animator = character.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                // Keep animating even if the FurBrain is briefly off-screen, so it never "freezes" mid-wave.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var furMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_FurMaterialPath);
                if (furMaterial != null)
                {
                    var fur = character.GetComponent<FurBrainFur>();
                    if (fur == null)
                        fur = character.AddComponent<FurBrainFur>();
                    fur.furMaterial = furMaterial;
                    // Fur is parked for now: the component is added switched off; tick it to try fur again.
                    fur.enabled = false;
                }
                else
                {
                    Debug.LogWarning($"[FurBrains] {k_FurMaterialPath} not found, so the pet has no fur. " +
                                     "Run Tools → FurBrains → Create Ziara Materials, then rebuild the pet.");
                }

                // Tap-only interaction. XRSimpleInteractable can be selected (a tap: used for waving) and focused
                // (what the template's delete button acts on), but unlike the template's XRGrabInteractable +
                // ARTransformer it never moves, rotates or scales the object. It only needs a collider to be hit by
                // the screen-touch ray; no Rigidbody is required.
                AddCollider(root, character);
                root.AddComponent<XRSimpleInteractable>();

                // Mood before pet: FurBrainPet looks up FurBrainMood in Awake (either order works, this reads clearer).
                root.AddComponent<FurBrainMood>();
                var pet = root.AddComponent<FurBrainPet>();
                pet.animator = animator;
                pet.idleAnimation = idleClip;
                pet.summonAnimation = actionClip;
                pet.tapAnimation = actionClip;
                pet.feedAnimation = actionClip;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_PetPrefabPath, out var ok);
                if (ok)
                {
                    Debug.Log($"[FurBrains] Built {k_PetPrefabPath} and {k_ControllerPath}.", prefab);
                    EditorGUIUtility.PingObject(prefab);
                    Selection.activeObject = prefab;
                }
                else
                {
                    Debug.LogError($"[FurBrains] Failed to save {k_PetPrefabPath}.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Scale to toy size, turn the face toward +Z, and stand the feet on y = 0.</summary>
        static void FitCharacter(Transform character)
        {
            // Facing: the eyes sit in front of the head, so (eye centre - body centre) points the way she faces.
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            var body = renderers.FirstOrDefault(r => r.name == "Body");
            var eyes = renderers.FirstOrDefault(r => r.name == "Eyebrows");
            if (body != null && eyes != null)
            {
                var facing = eyes.bounds.center - body.bounds.center;
                facing.y = 0f;
                if (facing.sqrMagnitude > 1e-6f)
                {
                    // Snap to the nearest 90 degrees: models are authored facing along an axis.
                    var yaw = Mathf.Round(Vector3.SignedAngle(facing, Vector3.forward, Vector3.up) / 90f) * 90f;
                    character.localRotation = Quaternion.Euler(0f, yaw, 0f) * character.localRotation;
                }
            }

            var bounds = WorldBounds(renderers);
            if (bounds.size.y > 0f)
                character.localScale *= k_TargetHeight / bounds.size.y;

            bounds = WorldBounds(renderers);
            character.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static void AddCollider(GameObject root, GameObject character)
        {
            var bounds = WorldBounds(character.GetComponentsInChildren<Renderer>(true));
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y axis
            capsule.height = bounds.size.y;
            // Narrower than the full bounds, which include Ziara's wide ears and arms.
            capsule.radius = Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f;
            capsule.center = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            var result = new Bounds();
            var first = true;
            foreach (var r in renderers)
            {
                if (first) { result = r.bounds; first = false; }
                else result.Encapsulate(r.bounds);
            }
            return result;
        }

        static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null)
                Debug.LogError($"[FurBrains] No animation clip found in {fbxPath}.");
            return clip;
        }

        /// <summary>Sets "Loop Time" on the FBX's clip(s) through the importer, as the Animation tab would.</summary>
        static void SetClipLooping(string fbxPath, bool loop)
        {
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter importer)
                return;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            if (clips.All(c => c.loopTime == loop) && importer.clipAnimations.Length > 0)
                return;
            foreach (var c in clips)
                c.loopTime = loop;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path))
                return;
            EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
