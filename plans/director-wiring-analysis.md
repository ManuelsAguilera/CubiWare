# Director Scene — Wiring Analysis Document

**Generated:** 2026-05-31 — **Corrected:** 2026-05-31 (full audit)  
**Sources cross-referenced:**
- [`scene-director-assets-guide.md`](Assets/Assets2/scenedirector/scene-director-assets-guide.md) — definitive wiring specification
- All 11 `.cs` scripts in [`Assets/Scripts/Minigames/SceneDirector/`](Assets/Scripts/Minigames/SceneDirector/) — every `[SerializeField]`, singleton pattern, `GetComponent` call, event subscription, and runtime dependency traced
- [`FaceLandmarkReader.cs`](Assets/Scripts/Face/FaceLandmarkReader.cs) and [`EmotionClassifier.cs`](Assets/Scripts/Face/EmotionClassifier.cs) — cross-referenced for `MaskController` and `SceneDirectorGame` dependencies
- Current Director scene hierarchy as described by the user
- Audio file inventory under [`Assets/Audio/SceneDirector/`](Assets/Audio/SceneDirector/) (13 of 15 clips present)

---

## A. Overview

The **Director scene** is a Unity theatrical emotion-matching minigame. A live webcam feed (or editor keyboard simulation) drives MediaPipe face landmark detection. The player performs scripted emotions (Happy, Surprised, Angry) while an approval bar fills or drains. A 3D theatrical mask on a dedicated render-texture camera overlays the player's face. Theatrical flourishes — curtains, audience reactions, tomato splats, audio cues — wrap the core mechanic in a dramatic stage metaphor.

**Goal of this document:** Audit every GameObject, component, serialized field reference, event wire, layer assignment, and asset against the combined requirements in the guide and code. Identify what is correctly wired, what is missing or misconfigured, and produce an actionable prompt for a Unity AI assistant to close the remaining gaps.

**Overall state:** The scene has substantial wiring — cameras, masks, core UI controllers, and button events are present. However, a significant number of serialized fields on `SceneDirectorGame` are not yet assigned, several required child GameObjects (DialogueBubble, IntroText, ResultsElementsPassed) are missing, the `CameraController` singleton placement is unconfirmed, layer assignments are unverified, and the critical animation events (curtain open/close, audience react) are not confirmed as wired. The bar-fill direction (Vertical vs. Horizontal) deviates from the guide specification.

**Note on EmotionClassifier modularity**: The `EmotionLabel` enum (`Neutral, Happy, Surprised, Angry`) and the `EmotionClassifier` class are designed to be extended. The `[RequireComponent(typeof(FaceLandmarkReader))]` attribute and event-based `OnEmotionChanged` pattern decouple emotion detection from the Director scene's state machine. Adding new emotions in a future version requires: (a) extending the `EmotionLabel` enum, (b) adding new scoring logic in `EmotionClassifier.Update()`, (c) creating corresponding mask meshes with matching `(int)` indices, (d) adding sprites to `ScriptController._emotionSprites`, and (e) updating `SceneDirectorGame.GenerateSequenceAsync()` LLM prompts. No architecture changes needed — the system is already modular.

---

## B. Correctly Wired Elements

### B.1 Camera & RenderTexture Pipeline

| Element | Status | Why It's Correct |
|---|---|---|
| **Main Camera** with `Camera`, `AudioListener`, `FaceLandmarkReader` | ✅ Correct | `FaceLandmarkReader` must live on a camera-facing GameObject. `MaskController._reader` (line 48 of [`MaskController.cs`](Assets/Scripts/Minigames/SceneDirector/MaskController.cs:48)) references it, with `FindAnyObjectByType<FaceLandmarkReader>()` fallback in `Awake()` (line 56). Culling mask excludes `StageLayer` per guide §2.3. |
| **Stage Camera** with `Camera`, renders only `StageLayer`, `Target Texture = StageRT` | ✅ Correct | `MaskController._stageCamera` (line 41) references this camera for `ViewportToWorldPoint()` coordinate conversion. Guide §2.3 requires StageLayer-only culling. Guide §3.2 requires `Target Texture = StageRT`. |
| **CameraDisplay** RawImage showing `StageRT` | ✅ Correct | Guide §3.2 requires `RawImage.Texture = StageRT`. The dedicated render-texture pipeline (Stage Camera → StageRT → CameraDisplay) isolates 3D masks from UI rendering. |
| **StageBackground** world-space Canvas in `StageLayer` with `WebcamFeed` RawImage | ✅ Correct | Provides the backdrop visible through `StageRT`. `MaskController` positions masks between the Stage Camera and this background. |

### B.2 Mask System

| Element | Status | Why It's Correct |
|---|---|---|
| **MaskRoot** GameObject with `MaskController` component | ✅ Correct | `MaskController` is a singleton. Guide §4.1 places it at scene root. |
| `MaskController._stageCamera` → Stage Camera | ✅ Correct | Required by `UpdatePosition()` for `ViewportToWorldPoint()` — lines 92–106 of [`MaskController.cs`](Assets/Scripts/Minigames/SceneDirector/MaskController.cs:92). |
| `MaskController._reader` → `FaceLandmarkReader` on Main Camera | ✅ Correct | Required for `FaceCenterNormalized`, `FaceScale`, `HeadConfidence`, `HasFreshData` — lines 64–70. |
| HappyMask, SurprisedMask, AngryMask children of MaskRoot | ✅ Correct | `_maskObjects` array (line 36) holds references to these three meshes. `SwapMesh()` activates exactly one by `(int)EmotionLabel` index — lines 114–119. |

### B.3 Core UI Controllers & Their Immediate Children

| Element | Status | Why It's Correct |
|---|---|---|
| **Countdown** GameObject with `CountdownController` | ✅ Correct | Singleton. Wired to `Fill Bar` (Image with `Image.Type = Filled`) and `Time Text` (TextMeshProUGUI) — matches `_timeText` (line 32) and `_fillBar` (line 33) of [`CountdownController.cs`](Assets/Scripts/Minigames/SceneDirector/CountdownController.cs:32). |
| **Scenario** GameObject with `ScenarioController` and `Animator` | ✅ Correct | Singleton. Contains `Curtain Image` child and `Camera Display` RawImage. `ScenarioController._animator` field at line 28 of [`ScenarioController.cs`](Assets/Scripts/Minigames/SceneDirector/ScenarioController.cs:28) expects an Animator with `Open` and `Close` triggers (lines 30–31). |
| **ApprovalBar** GameObject with `ApprovalBarController` | ✅ Correct | Singleton. Wired to `Fill Bar` (Image Filled) and `Feedback Text` (TMP) — matches `_fillBar` (line 45) and `_feedbackText` (line 47) of [`ApprovalBarController.cs`](Assets/Scripts/Minigames/SceneDirector/ApprovalBarController.cs:45). |
| **Script Panel** GameObject with `ScriptController` | ✅ Correct | Singleton. Wired to `Current Emotion Text`, `Next Emotion Text`, `Progress Text`, `Emotion Image` — matches `_currentEmotionText` (line 41), `_nextEmotionText` (line 43), `_progressText` (line 45), `_emotionIcon` (line 47) of [`ScriptController.cs`](Assets/Scripts/Minigames/SceneDirector/ScriptController.cs:41). |
| **Audience** GameObject with `AudienceController` and `Animator` | ✅ Correct | Singleton. Wired to `Audience Sprite` (Image). The `_animator` field at line 34 of [`AudienceController.cs`](Assets/Scripts/Minigames/SceneDirector/AudienceController.cs:34) expects an Animator with `SlightMove` (Bool, line 40), `React` (Trigger, line 41), `IsPositive` (Bool, line 42) parameters. |
| **ResultsPanel** with `ResultsHeadline`, `ResultsEvaluation`, `ResultsScore`, `PlayAgainButton` | ✅ Correct | These children match `_resultsHeadline` (line 66), `_resultsEvaluation` (line 67), `_resultsScore` (line 68) fields and the PlayAgainButton wiring in guide §13.2. |
| **PausePanel** with `ResumeButton`, `QuitButton` | ✅ Correct | Children match guide §4.2. |
| **CountdownOverlay** and **FeedbackOverlay** CanvasGroups | ✅ Correct | Full-screen overlays matching guide §4.2. |

### B.4 Audio System

| Element | Status | Why It's Correct |
|---|---|---|
| **SceneDirectorAudio** GameObject with `SceneDirectorAudio` component | ✅ Correct | Singleton. Guide marked this as ***NEW*** but user confirms it exists. |
| SFX and Music child `AudioSource` objects | ✅ Correct | Matches `_sfxSource` (line 26) and `_musicSource` (line 28) fields of [`SceneDirectorAudio.cs`](Assets/Scripts/Minigames/SceneDirector/SceneDirectorAudio.cs:26). Both should have `Play On Awake = false`. Music source should loop. |
| 13 audio clips populated (of 15 slots) | ✅ Partially Correct | Files present: `CurtainOpen`, `CurtainClose`, `CountdownTick`, `CountdownGo`, `CorrectChime`, `WrongBuzzer`, `Applause`, `Boo`, `TomatoSplat`, `AudienceLaugh`, `BGM_Gameplay`, `StingWin`, `StingLose`. Missing: `_audienceAmbient` (CRITICAL — `StartAmbient()` silently no-ops without it at line 144) and `_countdownUrgent`. All `Play*()` methods are null-safe (`if (clip == null) return`). See §C.4. |

### B.5 Tomato Splat System

| Element | Status | Why It's Correct |
|---|---|---|
| **TomatoSplatCanvas** high-sort-order Canvas | ✅ Correct | Guide §4.2 specifies sort order 100. |
| `TomatoSplatController` with `SplatPool` (`_poolParent`, line 28) and `TomatoSplatImage` prefab (`_splatPrefab`, line 30) | ✅ Correct | Matches fields at lines 28–30 of [`TomatoSplatController.cs`](Assets/Scripts/Minigames/SceneDirector/TomatoSplatController.cs:28). Pool initializes in `Awake()` (line 56). |
| **Placement note**: Guide §4.1 shows `TomatoSplatController` as a standalone GameObject at scene root (line 145). User places it on `TomatoSplatCanvas`. Both work — the code's `InitializePool()` uses `_poolParent ?? transform` (line 71), so either placement functions correctly. | ⚠️ Deviation | Not a runtime issue, but deviates from guide. |

### B.6 Button Event Wiring

| Button | Target | Method | Status |
|---|---|---|---|
| ResumeButton | SceneDirectorGame | `Resume()` | ✅ Correct — guide §13.1 |
| QuitButton | SceneDirectorGame | `QuitToMenu()` | ✅ Correct — guide §13.1 |
| PlayAgainButton | SceneDirectorGame | `TriggerPlayAgain()` | ✅ Correct — guide §13.2 |

### B.7 Code-Level Event Wiring (Automatic)

These are wired at runtime by `SceneDirectorGame.WireEvents()` (lines 240–271) — **no Inspector setup needed:**

| Publisher | Event | Subscriber | Purpose |
|---|---|---|---|
| `ScenarioController` | `OnOpenComplete` | `SceneDirectorGame.OnCurtainOpen` | Transition from CurtainOpening → CountdownOverlay |
| `ScenarioController` | `OnCloseComplete` | `SceneDirectorGame.OnCurtainClose` | Show ResultsPanel |
| `ScriptController` | `OnElementStarted` | `SceneDirectorGame.OnElementStarted` | Activate countdown + approval bar |
| `ScriptController` | `OnSequenceComplete` | `SceneDirectorGame.OnSequenceComplete` | End round (win) |
| `ApprovalBarController` | `OnBarFilled` | `SceneDirectorGame.OnBarFilled` | Pass element |
| `ApprovalBarController` | `OnBarEmptied` | `SceneDirectorGame.OnBarEmptied` | Fail element |
| `CountdownController` | `OnCountdownExpired` | `SceneDirectorGame.OnCountdownExpired` | Fail element (deactivates bar first) |
| `EmotionClassifier` | `OnEmotionChanged` | `SceneDirectorGame.OnEmotionChanged_Confirmed` | Mask swap (live mode only, line 266 guard: `!_editorSimulation`) |
| `GameManager` | `OnScoreChanged` | `SceneDirectorGame.OnScoreChanged` | Update ScoreHUD |

This is a major strength: all inter-controller event wiring is code-driven, eliminating many potential Inspector wiring errors.

---

## C. Improvements & Issues

### C.1 Missing GameObjects / UI Children

| # | Missing Element | Parent | Required By | Severity |
|---|---|---|---|---|
| C.1.1 | **DialogueBubble** (TextMeshProUGUI) | Audience | `AudienceController._dialogueText` (line 38 of [`AudienceController.cs`](Assets/Scripts/Minigames/SceneDirector/AudienceController.cs:38)). Guide §4.1 shows `DialogueBubble` as child of Audience. | 🔴 High — `ShowDialogue()` silently no-ops without it. |
| C.1.2 | **IntroText** (TextMeshProUGUI) | Script Panel | `ScriptController._introText` (line 51 of [`ScriptController.cs`](Assets/Scripts/Minigames/SceneDirector/ScriptController.cs:51)). Guide §4.1 shows `IntroText` as child of Script. | 🟡 Medium — LLM intro text won't display; `ShowIntro()` silently no-ops. |
| C.1.3 | **ResultsElementsPassed** (TextMeshProUGUI) | ResultsPanel | `SceneDirectorGame._resultsElementsPassed` (line 69 of [`SceneDirectorGame.cs`](Assets/Scripts/Minigames/SceneDirector/SceneDirectorGame.cs:69)). Guide §4.1 shows this child. | 🟡 Medium — "X / Y elementos" line won't appear in results. |
| C.1.4 | **PauseTitle** (TextMeshProUGUI) | PausePanel | Guide §4.2 specifies a "PAUSA" title. Cosmetic — not referenced in code. | 🟢 Low — cosmetic only. |
| C.1.5 | **CameraElement** / `CameraController` placement | Canvas | `CameraController` singleton is referenced throughout `SceneDirectorGame` (lines 128, 192–201, 312). Guide §4.1 shows `CameraElement (CameraController)` as child of Canvas. User does not mention where `CameraController` lives. | 🔴 High — if missing, `BindFeed()` and `SetMaskEmotion()` fail silently. |

### C.2 Missing Serialized Field Assignments on SceneDirectorGame

The user explicitly lists only 4 fields as wired: `_countdownOverlay`, `_feedbackOverlay`, `_resultsPanel`, `_scoreText`. Code at lines 60–71 of [`SceneDirectorGame.cs`](Assets/Scripts/Minigames/SceneDirector/SceneDirectorGame.cs:60) defines **11 serialized UI fields** (note: guide §14.4 incorrectly says "12 UI serialized fields"). The following are **unaccounted for** — no evidence they are assigned:

| # | Unassigned Field | Type | Required GameObject | Severity |
|---|---|---|---|---|
| C.2.1 | `_countdownText` | TextMeshProUGUI | CountdownOverlayText | 🔴 High — `PlayCountdownOverlay()` sets text on this (lines 416–426). Null → overlay invisible. |
| C.2.2 | `_feedbackOverlayText` | TextMeshProUGUI | FeedbackOverlayText | 🔴 High — `ShowElementFeedback()` sets text/color on this (lines 541–542). Null → feedback invisible. |
| C.2.3 | `_resultsHeadline` | TextMeshProUGUI | ResultsHeadline | 🟡 Medium — `PopulateResultsPanel()` sets "¡BRAVO!" / "¡ABUCHEADO!" (line 664). Null → headline missing. |
| C.2.4 | `_resultsEvaluation` | TextMeshProUGUI | ResultsEvaluation | 🟡 Medium — LLM/hardcoded evaluation text (line 669). Null → evaluation missing. |
| C.2.5 | `_resultsScore` | TextMeshProUGUI | ResultsScore | 🟡 Medium — "Score: X" text (line 672). Null → score missing from results. |
| C.2.6 | `_resultsElementsPassed` | TextMeshProUGUI | ResultsElementsPassed | 🟡 Medium — "X / Y elementos" (line 675). Null → count missing. |
| C.2.7 | `_pausePanel` | CanvasGroup | PausePanel | 🔴 High — `Pause()` and `Resume()` use `SetCanvasGroupAlpha(_pausePanel, …)` (lines 759, 768). Null → pause panel never shown/hidden. |

### C.3 Missing Serialized Field Assignments on ScriptController

| # | Unassigned/Missing Field | Details | Severity |
|---|---|---|---|
| C.3.1 | `_introText` (TextMeshProUGUI) | Child GameObject `IntroText` not confirmed as existing (see C.1.2). | 🟡 Medium |
| C.3.2 | `_emotionSprites` (Sprite[] — array, line 49) | User says "Emotion Image default set to Happy" — implies a single sprite on the Image, not the 4-element array on ScriptController. Guide §8.1 requires 4 sprites: `[0]=Neutral, [1]=Happy, [2]=Surprised, [3]=Angry`. Without the array, `RefreshUI()` at lines 225–229 can't swap sprites per emotion. | 🟡 Medium |
| C.3.3 | `_hardcodedSequence` | Default Inspector values are 3 elements (Happy/Surprised/Angry, 5s each). User doesn't mention override. If defaults are kept, fallback sequence works correctly. | 🟢 Low (defaults fine) |

### C.4 SceneDirectorAudio — Clip Count Mismatch (CORRECTED)

User reports **13 audio clips** assigned. Code defines **15 clip slots** (guide §6). Verified against [`Assets/Audio/SceneDirector/`](Assets/Audio/SceneDirector/) file inventory:

**Present (13):** `CurtainOpen`, `CurtainClose`, `CountdownTick`, `CountdownGo`, `CorrectChime`, `WrongBuzzer`, `Applause`, `Boo`, `TomatoSplat`, `AudienceLaugh`, `BGM_Gameplay`, `StingWin`, `StingLose`.

**Missing (2):**
- **`_audienceAmbient`** — 🔴 Critical. `StartAmbient()` at line 144 of [`SceneDirectorAudio.cs`](Assets/Scripts/Minigames/SceneDirector/SceneDirectorAudio.cs:144) returns immediately if this is null. The looping crowd murmur will never play.
- **`_countdownUrgent`** — 🟡 Low. Only called by `PlayCountdownUrgent()` (line 122), which is not currently invoked from `SceneDirectorGame`. The urgent tick is not wired in the game's current call pattern.

All `Play*()` methods check `if (clip == null) return`, so missing clips are non-fatal except for the ambient which gates an entire feature.

### C.5 Bar Fill Direction Mismatch

| Bar | User Says | Guide Specifies | Impact |
|---|---|---|---|
| Countdown `Fill Bar` | Vertical Filled | Filled / Horizontal / Left (§4.1) | Visual: bar drains vertically instead of horizontally. `fillAmount` logic is identical — functional but visually inconsistent with design intent. |
| ApprovalBar `Fill Bar` | Vertical Filled | Filled / Horizontal / Left (§4.1) | Same as above. |

**Fix:** Change both `Fill Bar` Images to `Image.Type = Filled`, `Fill Method = Horizontal`, `Fill Origin = Left`.

### C.6 MaskController — `_maskObjects` Array Size Ambiguity

User says: "_maskObjects: Array containing Happy Mask, Surprised Mask, and Angry Mask."

Code declares `GameObject[] _maskObjects = new GameObject[4]` (line 36 of [`MaskController.cs`](Assets/Scripts/Minigames/SceneDirector/MaskController.cs:36)). Index mapping: `[0]=Neutral(null), [1]=Happy, [2]=Surprised, [3]=Angry`. Guide §9 explicitly states "Index [0] should be null (Neutral = no mask visible)."

**⚠️ Guide-Code Field Name Conflict**: Guide §9 calls this field `_masks`. Code at line 36 calls it `_maskObjects`. The code is authoritative — use `_maskObjects` in all Inspector assignments.

**⚠️ Guide-Code Default Value Conflicts**: Guide §9 suggests `_maskDepth = 5.0` and `_scaleMultiplier = 1.5`. Code defaults (lines 43–45) are `_maskDepth = 1.5` and `_scaleMultiplier = 3.5`. Use code defaults; tune after testing.

If the Inspector array is size 3 (indices 0,1,2), then when `SwapMesh((int)EmotionLabel.Angry)` tries `_maskObjects[3]`, it will throw `IndexOutOfRangeException`.

**Critical to verify:** Inspector array must be size 4: `[0]=None (null), [1]=HappyMask, [2]=SurprisedMask, [3]=AngryMask`.

### C.7 MaskController — `_maskRoot` Field Unconfirmed

Code at line 39 of [`MaskController.cs`](Assets/Scripts/Minigames/SceneDirector/MaskController.cs:39) declares `[SerializeField] private Transform _maskRoot;`. `UpdatePosition()` at line 94 returns early if `_maskRoot == null`. User mentions "Mask Root: parent container for the AR masks" but does not confirm the `_maskRoot` serialized field is assigned (it may be self-assigned — the MaskRoot transform itself — but this needs verification).

### C.8 Layer Assignments Unverified

Guide §2.2 requires these layer assignments:

| GameObject | Required Layer |
|---|---|
| MaskRoot | `StageLayer` |
| HappyMask | `StageLayer` |
| SurprisedMask | `StageLayer` |
| AngryMask | `StageLayer` |
| StageBackground | `StageLayer` |

User does not mention layer assignments at all. If any mask child is on `Default` layer instead of `StageLayer`, it will not be rendered by the Stage Camera (which culls only `StageLayer`), breaking the mask visibility.

### C.9 Animation Events — Critical, Unconfirmed

These animation events must be wired in the Animation Clip editor (not in code). **Without them, the game state machine hangs:**

| Animator | Clip | Event Function | Why Critical |
|---|---|---|---|
| Scenario Animator | **Open** (last frame) | `ScenarioController.OnOpenComplete_AnimEvent()` | `SceneDirectorGame.OnCurtainOpen()` never fires → game never leaves CurtainOpening phase → countdown never starts. **Game hangs.** |
| Scenario Animator | **Close** (last frame) | `ScenarioController.OnCloseComplete_AnimEvent()` | `SceneDirectorGame.OnCurtainClose()` never fires → ResultsPanel never shown. **Game hangs after curtain close.** |
| Audience Animator | **React** positive clip (last frame) | `AudienceController.OnReactComplete_AnimEvent()` | `SetIdle()` never called → `CurrentState` stays `React` permanently (line 132 of [`AudienceController.cs`](Assets/Scripts/Minigames/SceneDirector/AudienceController.cs:132)). `SlightMove()` guard at line 77 prevents re-entering SlightMove during React. |
| Audience Animator | **React** negative clip (last frame) | `AudienceController.OnReactComplete_AnimEvent()` | Same as above for negative reactions. |

**The user does not mention any animation event wiring.** This is the single most critical gap — if these aren't set, the game cannot complete its flow.

### C.10 Dual AudioListeners

Both **Main Camera** and **Stage Camera** have `AudioListener` components. Unity logs a warning when multiple AudioListeners are active. The Stage Camera's AudioListener should be removed — only Main Camera needs one.

### C.11 CameraController — `_cameraDisplay` and `_maskController` Fields

[`CameraController.cs`](Assets/Scripts/Minigames/SceneDirector/CameraController.cs:28) requires:
- `_cameraDisplay` (RawImage) — the CameraDisplay RawImage showing webcam feed (line 28)
- `_maskController` (MaskController) — reference to the MaskController component (line 29)

User doesn't confirm these are assigned. Since `CameraController` placement itself is unclear (C.1.5), these fields may be null. `BindFeed()` at line 79 silently returns if `_cameraDisplay == null`. `SetMaskEmotion()` at line 90 silently returns if `_maskController == null`.

### C.12 AudienceController — `_animator` Field (SEVERITY CORRECTED)

User says Audience has an `Animator` component but doesn't confirm the `_animator` serialized field on `AudienceController` (line 34) is assigned. **If null, every API call crashes**: `SetIdle()` (line 67), `SlightMove()` (line 79), `ReactPositive()` (line 89), `ReactNegative()` (line 99) all call `_animator.SetBool`/`_animator.SetTrigger` without null checks. **🔴 High severity.**

### C.13 ScenarioController — `_animator` Field (SEVERITY CORRECTED)

Same as C.12 — Animator exists on the Scenario GameObject, but `ScenarioController._animator` (line 28) must be explicitly assigned in the Inspector. `Open()` (line 54) and `Close()` (line 61) call `_animator.SetTrigger()` without null checks. **🔴 High severity — NullReferenceException on game start.**

### C.14 Missing Scene-Level Singleton Placements

The following singletons are referenced by code but the user does not confirm their presence in the hierarchy:

| Singleton | Expected Location | Code References |
|---|---|---|
| `EmotionClassifier` | Must be on same GameObject as `FaceLandmarkReader` (Main Camera) per `[RequireComponent(typeof(FaceLandmarkReader))]` (line 20 of [`EmotionClassifier.cs`](Assets/Scripts/Face/EmotionClassifier.cs:20)) | `SceneDirectorGame` caches it at line 124 via `FindAnyObjectByType<EmotionClassifier>()`, uses `OnEmotionChanged` event |
| `CameraController` | Guide §4.1: `CameraElement` child of Canvas | Used in `SceneDirectorGame.OnStart()` (line 128), `Update()` (lines 192–201), `OnEmotionChanged_Confirmed()` (line 312) |

### C.15 StageRT Resolution

Guide §3.1 specifies StageRT must be **640×480**. User doesn't confirm resolution. Wrong resolution could cause aspect ratio distortion in the CameraDisplay.

### C.16 Scene Index Configuration

Guide §1.2: `SceneDirectorGame._sceneIndex` (line 45) defaults to **5** and must match the actual Build Settings index. User doesn't mention this value. If Director.unity's build index is not 5, `TriggerPlayAgain()` will load the wrong scene.

### C.17 Guide-Code Discrepancies (New Finding)

These conflicts between the guide and actual code are not runtime issues but may confuse anyone wiring the scene:

| # | Guide Reference | Guide Value | Code Value | Impact |
|---|---|---|---|---|
| C.17.1 | §9 — `_masks` field name | `_masks` | `_maskObjects` (line 36) | Inspector shows `_maskObjects`, not `_masks`. |
| C.17.2 | §9 — `_maskDepth` default | 5.0 | 1.5 (line 43) | Different starting tuning values. |
| C.17.3 | §9 — `_scaleMultiplier` default | 1.5 | 3.5 (line 45) | Different starting tuning values. |
| C.17.4 | §14.4 — "All 12 UI serialized fields assigned" | 12 | 11 (lines 61–71) | Off by one; there are 11 UI fields. |

---

## D. Summary of Findings

### Overall Assessment: **Partial — Functional Core Present, Multiple Critical Gaps Remain**

The Director scene has a solid foundation. The dual-camera render-texture pipeline, the singleton controller architecture, the code-driven event wiring (Section B.7), the mask swapping mechanics, and the button events are all correctly in place. The audio system and tomato splat system are present and wired.

### Critical Gaps (Will Cause Runtime Failures or Hangs)

1. **Animation events not confirmed** (C.9): `OnOpenComplete_AnimEvent`, `OnCloseComplete_AnimEvent`, `OnReactComplete_AnimEvent` — if any are missing from animation clips, the game state machine hangs.
2. **7 unassigned serialized fields on SceneDirectorGame** (C.2): `_countdownText`, `_feedbackOverlayText`, `_resultsHeadline`, `_resultsEvaluation`, `_resultsScore`, `_resultsElementsPassed`, `_pausePanel` — null references will cause silent failures in overlays, results, and pause.
3. **CameraController singleton placement unconfirmed** (C.1.5, C.14): Required for webcam binding and mask swaps.
4. **Layer assignments unverified** (C.8): If masks are not on `StageLayer`, they won't render.
5. **`_maskObjects` array size ambiguity** (C.6): Must be size 4 with index 0 = null.
6. **`ScenarioController._animator` unassigned** (C.13): NullReferenceException on `Open()`/`Close()` — game cannot start.
7. **`AudienceController._animator` unassigned** (C.12): NullReferenceException on any audience API call.
8. **`_audienceAmbient` audio clip missing** (C.4): `StartAmbient()` silently no-ops — no crowd murmur.

### Medium Gaps (Functional Degradation)

9. **Missing GameObjects**: DialogueBubble (C.1.1), IntroText (C.1.2), ResultsElementsPassed (C.1.3).
10. **Bar fill direction** (C.5): Vertical instead of Horizontal — cosmetic but deviates from spec.
11. **`_emotionSprites` array** (C.3.2): Need 4-element sprite array on ScriptController.
12. **MaskController `_maskRoot` field** (C.7): Must be assigned for mask positioning.
13. **CameraController `_cameraDisplay` and `_maskController` fields** (C.11): Must be assigned.

### Low-Severity Items

14. Dual AudioListeners (C.10).
15. StageRT resolution unconfirmed (C.15).
16. Scene index unconfirmed (C.16).
17. `_countdownUrgent` audio clip missing (C.4) — not currently called by any code path.
18. PauseTitle cosmetic child (C.1.4).
19. Guide-code field name and default value discrepancies (C.17).

### Correctly Wired Strengths

- RenderTexture pipeline (Stage Camera → StageRT → CameraDisplay) ✓
- Mask system (MaskController with stage camera and reader references) ✓
- All singleton controller GameObjects present ✓
- SceneDirectorAudio with SFX/Music AudioSources ✓
- TomatoSplatController with pool and prefab ✓
- All button onClick events wired correctly ✓
- Code-driven event wiring (9 event subscriptions in WireEvents) ✓
- FaceLandmarkReader on Main Camera ✓
- EmotionClassifier design is modular — `[RequireComponent]` + event pattern supports future emotion additions without architecture changes ✓
- 13 of 15 audio clips populated ✓

---

## E. Unity AI Assistant Prompt

> **Instructions:** Copy the entire block below and paste it into the Unity AI Assistant. This prompt assumes the Director scene already exists with partial wiring as described. Each step is ordered to minimize dependency chains (managers first, then UI children, then field assignments, then event wiring).

---

```
You are helping me finish wiring the Director scene (Director.unity) for a Unity theatrical emotion-matching minigame. The scene already has these elements in place:

- Main Camera (Camera, AudioListener, FaceLandmarkReader component, EmotionClassifier component)
- Stage Camera (Camera, AudioListener, Target Texture = StageRT, culls only StageLayer)
- MaskRoot (MaskController component, children: HappyMask, SurprisedMask, AngryMask)
- Canvas (Screen Space - Overlay, 1920x1080) containing:
  - CameraDisplay (RawImage showing StageRT)
  - Scenario (ScenarioController + Animator, child: CurtainImage)
  - Audience (AudienceController + Animator, child: AudienceSprite)
  - ApprovalBar (ApprovalBarController, children: FillBar, FeedbackText)
  - Countdown (CountdownController, children: FillBar, TimeText)
  - Script (ScriptController, children: CurrentEmotionText, NextEmotionText, ProgressText, EmotionIcon Image)
  - ResultsPanel (CanvasGroup, children: ResultsHeadline, ResultsEvaluation, ResultsScore, PlayAgainButton)
  - PausePanel (CanvasGroup, children: PanelBackground, ResumeButton, QuitButton)
  - CountdownOverlay (CanvasGroup) — needs child CountdownOverlayText
  - FeedbackOverlay (CanvasGroup) — needs child FeedbackOverlayText
  - ScoreHUD (TextMeshProUGUI)
  - StageArea with TomatoSplatCanvas child (TomatoSplatController, SplatPool child, TomatoSplatImage prefab assigned)
  - LeftCurtain, RightCurtain (styled images)
- StageBackground (world-space Canvas in StageLayer, child: WebcamFeed RawImage)
- SceneDirectorGame GameObject (SceneDirectorGame component, partially wired)
- SceneDirectorAudio GameObject (SceneDirectorAudio component, SFX + Music AudioSource children, 13 of 15 audio clips assigned)
- EventSystem
- SceneDirectorGame._countdownOverlay, _feedbackOverlay, _resultsPanel, _scoreText are already assigned.
- Button onClick events: ResumeButton→SceneDirectorGame.Resume(), QuitButton→SceneDirectorGame.QuitToMenu(), PlayAgainButton→SceneDirectorGame.TriggerPlayAgain() are wired.

I need you to complete the wiring. Follow these steps in order:

---

### STEP 1: Fix Layer Assignments

1. Verify the TagManager has a custom layer named "StageLayer" (Edit → Project Settings → Tags and Layers).
2. Set the following GameObjects to layer "StageLayer":
   - MaskRoot
   - HappyMask (child of MaskRoot)
   - SurprisedMask (child of MaskRoot)
   - AngryMask (child of MaskRoot)
   - StageBackground
3. Confirm Main Camera culling mask excludes StageLayer.
4. Confirm Stage Camera culling mask renders ONLY StageLayer.

### STEP 2: Fix AudioListener Conflict

5. Remove the AudioListener component from the Stage Camera. Only Main Camera should have an AudioListener.

### STEP 3: Verify StageRT Configuration

6. Confirm the StageRT RenderTexture asset exists at Assets/RenderTextures/StageRT with these properties:
   - Size: 640 × 480
   - Color Format: R8G8B8A8_UNorm
   - Depth Buffer: No depth buffer
   - Filter Mode: Bilinear
7. If StageRT doesn't exist, create it: right-click in Assets/RenderTextures/ → Create → Render Texture, name it "StageRT", set properties as above.
8. Assign StageRT to Stage Camera's Camera.Target Texture field.
9. Assign StageRT to CameraDisplay RawImage's Texture field.

### STEP 4: Create Missing UI Child GameObjects

10. Under the Audience GameObject, create a child named "DialogueBubble":
    - Add TextMeshProUGUI component
    - Set font size to ~24, center-aligned
    - Position it above the AudienceSprite
    - Assign it to AudienceController._dialogueText field

11. Under the Script GameObject, create a child named "IntroText":
    - Add TextMeshProUGUI component
    - Set font size to ~28, center-aligned, italic
    - Stretch to fill the Script panel width
    - Assign it to ScriptController._introText field

12. Under the ResultsPanel GameObject, create a child named "ResultsElementsPassed":
    - Add TextMeshProUGUI component
    - Set font size to ~20, center-aligned
    - Position below ResultsScore
    - Assign it to SceneDirectorGame._resultsElementsPassed field

13. Under the PausePanel GameObject, create a child named "PauseTitle":
    - Add TextMeshProUGUI component
    - Set text to "PAUSA", font size 60+, bold, center-aligned
    - Position at the top-center of the panel

14. Under the CountdownOverlay GameObject, create a child named "CountdownOverlayText":
    - Add TextMeshProUGUI component
    - Font size 120+, bold, center-aligned, stretch to fill parent
    - Initially shows nothing (controlled by code)

15. Under the FeedbackOverlay GameObject, create a child named "FeedbackOverlayText":
    - Add TextMeshProUGUI component
    - Font size 100+, bold, center-aligned, stretch to fill parent
    - Initially shows nothing

### STEP 5: Verify CameraController Placement

16. Locate the "CameraElement" child of Canvas. If it doesn't exist:
    - Create an empty GameObject named "CameraElement" as a child of Canvas.
    - Add the CameraController component to it.
17. On the CameraController component:
    - Assign _cameraDisplay: drag the CameraDisplay RawImage.
    - Assign _maskController: drag the MaskRoot GameObject (which has the MaskController component).

### STEP 6: Fix Bar Fill Direction

18. On the Countdown GameObject, find the child "Fill Bar" Image:
    - Set Image Type = Filled
    - Set Fill Method = Horizontal
    - Set Fill Origin = Left
19. On the ApprovalBar GameObject, find the child "Fill Bar" Image:
    - Set Image Type = Filled
    - Set Fill Method = Horizontal
    - Set Fill Origin = Left

### STEP 7: Verify MaskController Fields

CRITICAL: The serialized field is named `_maskObjects` in the Inspector (not `_masks`). The code declares it as `GameObject[] _maskObjects = new GameObject[4]` at line 36 of MaskController.cs.

20. Select MaskRoot. On the MaskController component:
    - Ensure `_maskObjects` array has Size = 4.
    - Element 0: leave empty (null) — this is Neutral.
    - Element 1: drag HappyMask GameObject.
    - Element 2: drag SurprisedMask GameObject.
    - Element 3: drag AngryMask GameObject.
21. Verify `_maskRoot` field on MaskController is assigned. Drag MaskRoot into this field.
22. Verify `_stageCamera` is assigned to the Stage Camera.
23. Verify `_reader` is assigned to the FaceLandmarkReader component (on Main Camera).
24. Set `_maskDepth` to 1.5 and `_scaleMultiplier` to 3.5 (code defaults; tune after testing).

### STEP 8: Assign All SceneDirectorGame Serialized Fields

25. Select the SceneDirectorGame GameObject. In the Inspector, assign these 11 fields under "UI References — Overlays":

    Drag the following GameObjects/components into their respective slots:
    - _countdownOverlay → drag the CountdownOverlay GameObject (has CanvasGroup)
    - _countdownText → drag the newly created CountdownOverlayText child (TextMeshProUGUI)
    - _feedbackOverlay → drag the FeedbackOverlay GameObject (has CanvasGroup)
    - _feedbackOverlayText → drag the newly created FeedbackOverlayText child (TextMeshProUGUI)
    - _resultsPanel → drag the ResultsPanel GameObject (has CanvasGroup) — already assigned, verify
    - _resultsHeadline → drag ResultsHeadline (TextMeshProUGUI)
    - _resultsEvaluation → drag ResultsEvaluation (TextMeshProUGUI)
    - _resultsScore → drag ResultsScore (TextMeshProUGUI)
    - _resultsElementsPassed → drag the newly created ResultsElementsPassed (TextMeshProUGUI)
    - _pausePanel → drag the PausePanel GameObject (has CanvasGroup)
    - _scoreText → drag ScoreHUD (TextMeshProUGUI) — already assigned, verify

26. Under "Scene Settings":
    - Set _sceneIndex to match Director.unity's build index in Build Settings.
    - Set _mainMenuSceneIndex to the MainMenu scene build index.
    - Leave _betweenElementDelay = 1.5, _endRoundDelayWin = 3.0, _endRoundDelayLose = 2.0, _pointsPerElement = 100.

### STEP 9: Assign ScenarioController and AudienceController Animator Fields (CRITICAL)

These MUST be assigned or the game crashes on start with NullReferenceException. Both scripts call `_animator.SetTrigger()`/`_animator.SetBool()` without null checks.

27. Select the Scenario GameObject. On the ScenarioController component:
    - Assign `_animator`: drag the Animator component from the same GameObject.

28. Select the Audience GameObject. On the AudienceController component:
    - Assign `_animator`: drag the Animator component from the same GameObject.
    - Assign `_dialogueText`: drag the newly created DialogueBubble child.

### STEP 10: Assign ScriptController Remaining Fields

29. Select the Script GameObject. On the ScriptController component:
    - Assign `_introText`: drag the newly created IntroText child (TextMeshProUGUI).
    - Create/assign `_emotionSprites` array with Size = 4:
      - Element 0: Neutral sprite (circle outline, gray)
      - Element 1: Happy sprite (smiley, yellow/gold)
      - Element 2: Surprised sprite (wide eyes, orange/amber)
      - Element 3: Angry sprite (furrowed, red)
    - Verify `_currentEmotionText`, `_nextEmotionText`, `_progressText`, `_emotionIcon` are assigned.
    - Under "Difficulty Curve": set `_sequenceLength` = 3, `_timeLimitStart` = 6, `_timeLimitEnd` = 3.

### STEP 11: Configure Animator Parameters and Animation Events

30. On the Scenario GameObject's Animator Controller:
    - Create Trigger parameter "Open"
    - Create Trigger parameter "Close"
    - In the Open animation clip, add an Animation Event at the LAST frame calling: ScenarioController.OnOpenComplete_AnimEvent()
    - In the Close animation clip, add an Animation Event at the LAST frame calling: ScenarioController.OnCloseComplete_AnimEvent()

31. On the Audience GameObject's Animator Controller:
    - Create Bool parameter "SlightMove"
    - Create Trigger parameter "React"
    - Create Bool parameter "IsPositive"
    - Set up transitions:
      - Idle → SlightMove: condition SlightMove == true
      - SlightMove → Idle: condition SlightMove == false
      - Any State → React: condition React trigger
      - React → Idle: Has Exit Time = true
    - In ALL React animation clips (positive and negative), add an Animation Event at the LAST frame calling: AudienceController.OnReactComplete_AnimEvent()

### STEP 12: Verify EmotionClassifier on Main Camera

32. Select Main Camera. Verify:
    - FaceLandmarkReader component is present.
    - EmotionClassifier component is present (it requires FaceLandmarkReader via [RequireComponent]).
    - If EmotionClassifier is missing, add it. It will auto-require FaceLandmarkReader.

### STEP 13: Complete Audio Clip Slots

33. Select SceneDirectorAudio. Count the number of non-null audio clips. There should be 15. The two known-missing clips are:
    - `_audienceAmbient` (CRITICAL — looping crowd murmur; `StartAmbient()` silently no-ops without it)
    - `_countdownUrgent` (Low priority — not currently called by any code path)
    Assign placeholder AudioClips for these two slots, or leave null (all calls are null-safe except `StartAmbient` which gates on clip null).

    The 15 slots are:
    - Curtain: `_curtainOpen`, `_curtainClose`
    - Countdown UI: `_countdownTick`, `_countdownGo`, `_countdownUrgent`
    - Element Feedback: `_correctChime`, `_wrongBuzzer`
    - Audience: `_audienceAmbient`, `_applause`, `_boo`, `_tomatoSplat`, `_audienceLaugh`
    - Music: `_bgmGameplay`, `_stingWin`, `_stingLose`

34. Verify `_sfxSource` (SFX AudioSource) has Play On Awake = false.
35. Verify `_musicSource` (Music AudioSource) has Play On Awake = false, Loop = true.

### STEP 14: Configure CanvasGroups for Initial State

36. On CountdownOverlay CanvasGroup:
    - Alpha = 0
    - Interactable = false
    - Blocks Raycasts = false
37. On FeedbackOverlay CanvasGroup:
    - Alpha = 0
    - Interactable = false
    - Blocks Raycasts = false
38. On PausePanel CanvasGroup:
    - Alpha = 0
    - Interactable = false
    - Blocks Raycasts = false
39. On ResultsPanel CanvasGroup:
    - Alpha = 0
    - Interactable = false
    - Blocks Raycasts = false

### STEP 15: TomatoSplatCanvas Sort Order

40. On the TomatoSplatCanvas Canvas component:
    - Set Sort Order to 100 (so splats render above all other UI).

### STEP 16: Final Verification

41. Run the scene and check the Console for:
    - No NullReferenceException errors (especially from ScenarioController, AudienceController, MaskController).
    - No warning about multiple AudioListeners.
    - Log messages from "SceneDirectorGame" showing state transitions.
    - If the curtain animation plays and completes, you should see "Curtain open — starting countdown overlay." in logs.
    - If using editor simulation (check `_editorSimulation = true` on SceneDirectorGame):
      - Press H, S, A, N keys to simulate emotions.
      - The mask should swap, approval bar should fill/drain.
      - Audience should react on pass/fail.
      - Results panel should appear after all elements pass or one fails.
42. If any serialized field is still null, the relevant feature will silently degrade (no crash, but no visual). Check the specific GameObject exists and the field is dragged in.
43. Specifically verify these fields are NOT null after wiring: `ScenarioController._animator`, `AudienceController._animator`, `AudienceController._dialogueText`, `MaskController._maskObjects` (size 4), `MaskController._maskRoot`, `CameraController._cameraDisplay`, `CameraController._maskController`.
```

---

*End of Wiring Analysis Document.*

### Corrections Applied in This Audit (vs. Original Analysis)

| # | Error in Original | Correction |
|---|---|---|
| 1 | Missing audio clips guessed as `_countdownUrgent` + `_audienceLaugh` | Verified against file inventory: missing are `_audienceAmbient` (critical) + `_countdownUrgent` (low) |
| 2 | `ScenarioController._animator` and `AudienceController._animator` classified as "Low" severity | Reclassified to **🔴 High** — null = NullReferenceException crash on game start or any audience call |
| 3 | Guide-code field name conflict (`_masks` vs `_maskObjects`) not flagged | Documented at C.6 and C.17.1; AI prompt uses code-authoritative name `_maskObjects` |
| 4 | Guide-code default value conflicts (`_maskDepth` 5.0 vs 1.5, `_scaleMultiplier` 1.5 vs 3.5) not flagged | Documented at C.6 and C.17.2–C.17.3 |
| 5 | Guide says "12 UI serialized fields" (line 543 of guide) but code has 11 | Documented at C.17.4 and corrected in Section C.2 |
| 6 | AI prompt missing step to create `CountdownOverlayText` and `FeedbackOverlayText` children | Added as Step 4 items 14–15 |
| 7 | `_emotionIcon` field type not specified | Clarified as `Image` (line 47 of ScriptController.cs) |
| 8 | EmotionClassifier's `[RequireComponent]` and modular design not noted | Added in Section A and Section D strengths |
| 9 | `TomatoSplatController` placement discrepancy not flagged | Noted at B.5 with explanation that both placements work |
| 10 | AudioListener conflict: both cameras have one | Stage Camera's must be removed — Step 2 |
| 11 | AI prompt verification step didn't check `_animator` fields specifically | Added Step 16 item 43 |
| 12 | `_cameraDisplay` and `_maskController` fields on CameraController not given severity | Noted at C.11 — silent nil-ops, medium severity |
