# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FurBrainsAR is a mobile AR prototype built on Unity's **Mobile AR template** (Unity 6.3 LTS, `6000.3.28f1`). Its primary target is **Android (ARCore)**; ARKit is installed but iOS isn't currently built.

## Product context

**FurBrains** is a fluffy AI toy for children aged 3–11 that they can talk to. This app lets the player interact directly with a **digital FurBrain** in AR. It is **one app** that combines two things:

1. **AR pet game (the core loop):** a virtual pet that lives in the player's space (care, feeding, play, a growing relationship), in the genre of AR virtual-pet games.
2. **A broad minigame collection played with the FurBrain:** this is the key differentiator. Pet games in this genre usually offer only a small selection of minigames, while FurBrains should offer many. Architect minigames as **pluggable modules** sharing a common interface (start/end, results feeding back into the pet's state) so new ones are cheap to add.

Design implications:
- **The audience is young children.** Use large touch targets, minimal reading (icons, audio, animation), simple one-finger gestures, short sessions, and no fail states that punish.
- **Child privacy:** treat camera and microphone data and any voice or AI features with child-privacy rules in mind (e.g. COPPA, GDPR-K). Don't add analytics, ads, accounts or network calls without discussing it first.
- The FurBrain character is the toy's own IP. Mechanics can draw on the genre, but don't copy specific existing games' characters, art or content.

## Working with the user

- They have some Unity experience and are strongest in **C++**, and they're new to C#-heavy Unity work and to Claude Code. Explain C#/Unity concepts by comparison with C++ where it helps: GC vs. manual lifetime, `Destroy()` vs. `delete`, MonoBehaviour lifecycle vs. constructors, properties, events/delegates, `[SerializeField]`.
- They want to **understand how the project works**, not just get working code. For each change, briefly explain what was changed, why, and how it fits into the existing architecture (which components talk to which, and what's wired in the scene vs. in code). Point to the relevant files rather than treating code as a black box.
- Spell out any Editor steps that code changes depend on.

Key packages (see `Packages/manifest.json`): AR Foundation 6.5, ARCore/ARKit 6.5, XR Interaction Toolkit 3.5.1, Input System 1.20, URP 17.3, Android Logcat.

## Working with the Unity Editor

- The user normally has the project open in the Editor. Unity recompiles C# when the Editor regains focus, and compile errors show in the Editor **Console**. Ask the user to paste them.
- Scene and prefab wiring (adding components, assigning serialized references, UnityEvent hookups) is usually quickest done by the user in the Inspector. Give exact steps. Editing `.unity`/`.prefab` YAML directly is possible (serialization is Force Text) but fragile: references use fileID/GUID pairs from `.meta` files.
- Every asset has a `.meta` file holding its GUID. When creating, moving or renaming assets, keep the `.meta` alongside, or let Unity generate it. Never regenerate or delete a `.meta` for an existing asset, because that breaks references.
- Batch-mode commands (`Unity.exe -batchmode -projectPath ...`) fail while the Editor has the project open.
- Editor path: `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe`.
- adb (not on PATH): `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`. Device logs: `adb logcat -s Unity`, or the Android Logcat window in the Editor.

## Build & test

- Builds are made from the Editor (**File → Build Profiles → Android → Build / Build And Run**) into `Builds/Android_Build/`, which is git-ignored. There is no scripted build pipeline.
- Only `Assets/Scenes/SampleScene.unity` is in the build scene list.
- In-Editor Play mode uses **XR Simulation** (the Standalone loader), so AR features can be tested without a device. Real plane tracking, lighting and performance need an on-device build.
- There are no automated tests yet. Unity Test Framework is available through `com.unity.feature.development`.

## Architecture

The app is the template's `SampleScene`: tap a detected plane to place an object, then move, rotate or scale it.

- **XR Origin (AR Rig)** (`Assets/Samples/XR Interaction Toolkit/3.5.1/AR Starter Assets/Prefabs/`) contains the AR camera, the AR managers (planes, raycasts) and a screen-touch `XRRayInteractor` fed by XRI touchscreen gesture input. Placed objects are XRI interactables (the prefab variants in `Assets/MobileARTemplateAssets/Prefabs/`, such as `CubeVariant`), so move, rotate and scale come from XRI, not custom code.
- **Placement flow:** `ARInteractorSpawnTrigger` waits for a tap that doesn't select anything, raycasts against AR planes (ignoring taps over UI), and fires `objectSpawnTriggered(position, normal)`. In the scene, that event is wired to `ObjectSpawner.SpawnObject`, which instantiates from its `objectPrefabs` list (7 entries, set in the scene) and, with `spawnAsChildren`, parents spawned objects under the spawner. `spawnOptionIndex = -1` means random.
- **`ARTemplateMenuManager`** (`Assets/MobileARTemplateAssets/Scripts/`) owns the UI. The create menu's buttons call `SetObjectToSpawn(index)`, which sets the spawner index. It also handles deleting the focused object (via `XRInteractionGroup.focusInteractable`), "clear all" (destroys spawner children), plane-visualization toggles (adds `ARPlaneMeshVisualizerFader` to new planes) and the AR debug menu.
- **`GoalManager`** runs the onboarding prompt sequence (greeting → scan surfaces → tap to place → move/rotate/scale hints) using the `Prompt_*` prefabs in `Assets/MobileARTemplateAssets/UI/Prefabs/`.
- Input comes from the Input System via `XRI Default Input Actions.inputactions`. Scripts read it through XRI `XRInputValueReader`/`XRInputButtonReader` fields, not legacy `Input.*`.

## Code layout and conventions

- `Assets/Samples/XR Interaction Toolkit/3.5.1/` is **imported sample code** in its own assemblies (`ARStarterAssets.asmdef`, `StarterAssets.asmdef`). Avoid editing it in place, because re-importing the sample overwrites it. Copy a script or subclass it into project code instead.
- `Assets/MobileARTemplateAssets/` is template code compiled into `Assembly-CSharp` (namespace `UnityEngine.XR.Templates.AR`). It's fine to modify.
- Put new prototype code under its own folder (for example `Assets/FurBrains/`), not inside the sample or template folders.
- Code that references AR Foundation inside the XRI sample assemblies is wrapped in `#if AR_FOUNDATION_PRESENT`.
- Code style follows the template: `[SerializeField] T m_Field;` private fields with a `[Tooltip]` and a public property; subscribe in `OnEnable` and unsubscribe in `OnDisable`.

## Git

`Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`, `.utmp/` and IDE project files (`*.csproj`, `*.slnx`) are ignored. The remote is `origin` → `github.com/NickStokesDecodedDigital/FurBrainsAR`, branch `main`.
