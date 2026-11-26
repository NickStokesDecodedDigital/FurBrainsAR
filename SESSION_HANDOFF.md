# FurBrainsAR: session handoff (2026-11-27)

A summary of the long Claude Code session that ran from late November 2026 up to 27 November, for carrying on from another computer. **CLAUDE.md** (in this repo) is the detailed technical reference and is kept up to date. This file covers the history, decisions, current state and next steps. Claude's private memory notes don't travel between computers, so their content is copied at the end.

---

## 1. Setting up the other computer

1. **Clone:** `git clone https://github.com/NickStokesDecodedDigital/FurBrainsAR` (branch `main`). Check that the latest work was pushed first (see section 5).
2. **Unity 6.3 LTS `6000.3.28f1`** via Unity Hub, with the **Android Build Support** module (SDK, NDK, OpenJDK). Open the project: the first import takes a while (the `Library/` folder is not in git).
3. **Packages** come from `Packages/manifest.json` automatically, including the git package `com.learntoflutter.flutter_embed_unity_6000_0` (needs git installed).
4. **Piper TTS** (test voice lines only), optional:
   - `pip install piper-tts`
   - `python -m piper.download_voices en_US-amy-medium`, run in the FurBrainsAR folder. The `.onnx` voice files are git-ignored, so download them again on the new computer.
   - Generate lines **from the FurBrainsAR folder** (Piper looks for the voice in the current folder, or use `--data-dir`):
     `python -m piper -m en_US-amy-medium --length-scale 2 -f Assets\FurBrains\Audio\Voice\Test\Vowels\Voice_X.wav -- "Words."`
5. **Flutter parent app** (only needed for embedding work):
   - Clone `github.com/3PM-Source/furbrains-parent-app` (private; needs access).
   - It needs **Flutter 3.44.6** (3.50 rejects their Gradle 8.10) and Windows **Developer Mode** (symlinks).
   - Export Unity into `FurBrainsParentApp/android/unityLibrary` (git-ignored, about 6 GB), then run `flutter run` from the parent app folder. That builds a debug APK in about 3 minutes and installs it on the plugged-in phone.
   - Without the export, the app builds normally and "Play in AR" shows a placeholder screen.
6. **adb** comes with Unity: `...\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.
7. The paths in CLAUDE.md (`C:\Users\Decoded\...`) are this computer's. Adjust them if the new one differs.

---

## 2. How Nick likes to work (from Claude's memory notes)

- **Nick** is strongest in **C++**, has some Unity experience, and is new to C#-heavy Unity work and to Claude Code.
- **Explain every change:** what changed, why, and how it fits the architecture (which components talk to which, what's wired in the scene vs. in code). Use C++ comparisons for C#/Unity concepts.
- **Keep code simple:** plain core OOP. **Ask before adding design patterns** (events/observer, state machines, DI, pooling, coroutines…).
- **Spell out Editor steps.** Scene and prefab wiring is usually done by Nick in the Inspector.
- **Commit and push only when asked.** `Assets/Resources.meta` is always left out of commits.
- **Child privacy (ages 3–11):** no analytics, ads, accounts or network calls without discussing it first.

---

## 3. What was done in this session (chronological)

### Results screen and UI (earlier)
- Results screen texts:
  - "Great Job!" / "Nice try!";
  - "Score: N" (hidden for won/lost games);
  - "Play Meter: +N%!";
  - coins.
- `PlayGainLabel` shows "+N%" during games.
- A **Results Backdrop**, a full-screen Image just above the Results Panel with Raycast Target on, blocks taps on the other buttons while results are shown. It's now a serialized slot on `MinigameHud`; if left empty, it's created in code.
- Menus restyled as bottom sheets, and a settings sheet added (see CLAUDE.md).
- Nick fixed a results screen layout bug in the scene himself (commit `7f7b2e5`).

### Parent app (Flutter) integration
- Explored how the parent app talks to the **FurBrains server**:
  - one REST API at `api.furbrains.ai/v1`, used through a Dio client with Bearer token refresh;
  - toys are provisioned over BLE, after which they talk to the server directly.
- **Unity ↔ Flutter bridge latency** is fine for small JSON messages, such as needs, coins, or "user said X, wait for a reply". It's unsuitable for streaming audio. For live voice conversations, Unity should talk to the server directly with a short-lived token from the parent app, not relay audio through Flutter.
- Merged their `main` into the **`UnityGameEmbed`** branch, built and tested it on the Galaxy S7, and pushed.
- **Unity made optional:**
  - Gradle includes `:unityLibrary` only if the export exists.
  - `MainActivity` answers `isAvailable` on channel `com.furbrains.app/unity_game`.
  - `ArScreen` shows the game or the placeholder.
  - A stand-in Java class (`android/app/src/noUnity/`) stops a start-up crash when the export is missing.
- Tested both ways on the phone. **Merged into their `main` via PR #42** (merge commit `72a0a5e`).
- Checked:
  - `flutter analyze` is clean;
  - all 803 `flutter test` tests pass;
  - the web build works.
- Not tested: Bluetooth toy pairing after the `MainActivity` change, and iOS.
- **Export size explained:** about 5.9 GB in total, mostly Gradle and IL2CPP build caches; the game itself is much smaller. It's too big for GitHub. Options if teammates need it later: GitHub Releases, a Maven package, or Git LFS.
- **How a teammate tests the game:**
  1. Pull FurBrainsAR.
  2. Open it in Unity 6000.3.28f1.
  3. Export into `android/unityLibrary`.
  4. Run `flutter run`.

### AI voice and lip sync (this week's main work)
- **Agreed voice roadmap** (in order):
  1. Lip sync on pre-recorded lines (**mostly done**).
  2. In-game comments: hello on summon (**done**), "I'm hungry" / "let's play" when meters are low (**next**).
  3. Maybe runtime speech (TTS on device or from a server).
  4. Last: talk to the FurBrain server / the brain chosen in the parent app. **Needs the child-privacy and consent discussion first.**
- **The Unreal game's speech** (`C:\FurbrainsGame\Furbrains 5.5`), as a reference for step 4:
  - runtime OpenAI Realtime audio streamed through their own Node server;
  - client-side A I U E O lip sync (`URealtimeLipsyncComponent`, plus the unused `ThinkraLipsync`).
- **Piper** was installed for free test voices. The production voice should be the official FurBrains voice from the toy team; check Piper voice licences before shipping anything.
- **`FurBrainSpeech`** (`Assets/FurBrains/Scripts/Character/`, on the pet root, added by the pet builder):
  - `Say(AudioResource)` plays a line on its own AudioSource.
  - Mouth opening = loudness (RMS of `GetOutputData`).
  - Mouth shape = vowel detection:
    - **v1** used FFT peaks. U, E and O came out mostly wrong.
    - **v2 (current)** uses **LPC formant tracking**: downsample to about 11 kHz, pre-emphasis and Hamming window, autocorrelation, a 10-coefficient LPC filter (Levinson-Durbin), then F1/F2 = the first two hills of the filter's frequency curve.
  - Each `Vowels` entry (shape, F1, F2, **Strength**) is scored by distance. Shares are summed per shape and sharpened with **Winner Focus** (default 3), then weight = opening × share × strength.
  - Live readout fields (`Live F1/F2/Vowel`) for tuning, on the **spawned** Ziara, not the prefab asset.
- **Default vowel table** (measured from the Piper amy clips):

  | Entry | Shape | F1 / F2 (Hz) | Strength |
  |---|---|---|---|
  | ah | A | 910 / 1400 | 1 |
  | ee | I | 390 / 2900 | 1 |
  | oo | O | 390 / 2000 | 1.25 |
  | eh | E | 690 / 2030 | 1 |
  | oh | O | 570 / 1120 | 1.25 |
  | oh start ("go", "no") | O | 740 / 1680 | 1.25 |

  The "oh start" entry is needed because American "oh" glides from 740/1680 to 570/1120; without it, the start was read as E.
- **Findings:**
  - Ziara's own **`U` blend shape is flat/smiling** (Japanese-style う), so English "oo" uses `O`.
  - American "oo" is fronted (F2 about 2000).
  - Blend shapes add, so mood shapes such as `Empowered` can fight the mouth shapes.
  - Offline accuracy (share of loud frames given the right shape): A 91 %, I 75 %, oo 90 %, E 68 %, oh 95 %, go/no 95 %.
  - Nick's verdict: **"serviceable for now"**.
- **Testing helpers:**
  - `FurBrainSpeech` → ⋮ → **Say Test Line**.
  - `FurBrainPet` → **Tap Voice Line**, said when the player taps her (not when minigames call `OnTapped()`).
  - **Fur Brain Face Demo** on `FurBrains Game` (tick it on) previews every blend shape. Remove `A I U E O` from its Skip list to see the visemes.
- **Hello on summon:** `FurBrainPet` → **Hello** slot, said once the summon animation ends (a timer: Start time + clip length, checked in `Update`).
- **Gotcha:** Unity overwrote direct edits to `ZiaraPet.prefab` when the prefab was open and saved in the Editor. Close Prefab mode without saving before Claude edits the YAML, or make the change in the Inspector.

---

## 4. Commits this session (FurBrainsAR `main`)
- `c6b265d` Restyle food and minigame menus as sheets, add results texts and Play gain label
- `7f7b2e5` Fix results screen layout, add Piper test voice lines, update parent app notes
- `c33f3b3` Add FurBrainSpeech: voice lines with loudness and vowel lip sync

## 5. ⚠️ Not committed yet (as of writing this file)
- `FurBrainSpeech.cs`: LPC detection, Strength, Winner Focus, the sixth O entry.
- `FurBrainPet.cs`: Tap Voice Line and Hello.
- `ZiaraPet.prefab`: new vowel values.
- Test clips moved into `Audio/Voice/Test/Vowels/`, plus the new `Voice_O2.wav` ("Go. No. So. Oh. Toe.").
- CLAUDE.md updates.
- This file.

**Commit and push these before switching computers**, or they won't be on the other one.

---

## 6. Next steps / open items
- **In-game comments:** "I'm hungry" / "let's play" when Hunger/Play are low, with a cooldown so she doesn't nag. This reuses `FurBrainSpeech.Say`.
- **Optional lip sync improvements:**
  - turn mouth-affecting mood shapes (Empowered/Happy) down while she speaks;
  - a live readout of the final shape weights;
  - try **uLipSync** (MIT, calibrated per voice) on a branch if the own detector isn't good enough;
  - retune the vowel table when the official voice arrives.
- **Ask the character team** whether the `U` blend shape is meant to look flat.
- **The FurBrainsAR GitHub repo is public:** company character assets are visible to anyone. Raise it with the boss (making it private is in GitHub Settings).
- **Parent app:**
  - test BLE pairing and iOS;
  - decide which coins are authoritative (Unity's local coins vs their server-side Spark Coins);
  - game tiles don't pick a specific minigame yet;
  - decide how teammates get the Unity export;
  - the PR #42 description's "#21" link points at the wrong issue (harmless);
  - Nick's local parent-app `main` needs a `git pull`.
- **Runtime TTS** and then the **FurBrain server** (roadmap steps 3–4), after the privacy discussion.

---

## 7. Claude memory notes (copy for the new computer)
These lived in Claude's local memory and won't transfer by themselves. On the new computer, Claude reads CLAUDE.md automatically. Point it at this file, or ask it to save these as memories:
- **User background:** Nick, strongest in C++, some Unity, new to Claude Code. Wants to understand how the project works, not just working code.
- **Product:** FurBrains is a talking AI plush toy for children aged 3–11. The app is one AR pet game plus many minigames; the breadth of minigames is the differentiator. Design for young children and child privacy, and keep mechanics original.
- **Keep code simple:** core OOP. Ask before design patterns, and propose them as a question.
- **Explain changes:** what, why, and how it fits, with C++ comparisons. End each change with a short "how it works" and the files involved.
- **Voice roadmap:** see section 3. The current status is that step 1 is serviceable and hello on summon is done; next come the low-meter comments.
