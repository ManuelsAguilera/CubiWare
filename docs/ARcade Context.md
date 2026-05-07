# ARcade Rush — Prototype Architecture & Coding Agent Context

> **Version 1.1 · April 2026 · PUCV**  
> Authors: Manuel Aguilera, Samira Becerra Saa, Naomí Núñez, Vicente Rosales

---

## How to Use This Document

This is the primary context source for a coding agent building the first functional prototype of ARcade Rush. **Read it top to bottom before writing any code.** Every architectural decision, file naming convention, dependency, and implementation order described here must be followed exactly.

ARcade Rush is a Unity-based minigame platform that replaces physical controllers with real-time computer vision (MediaPipe) and adapts gameplay dynamically using a Large Language Model (Groq API). The prototype covers **one complete minigame end-to-end** (Interrogatorio) with the full shared infrastructure in place.

> ⚠️ **PROTOTYPE SCOPE ONLY.** Do not implement features marked `[POST-PROTOTYPE]` even if the architecture references them.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Unity Project Setup](#2-unity-project-setup)
3. [Core Architecture — Singletons & Interfaces](#3-core-architecture--singletons--interfaces)
4. [Hand Detection Pipeline](#4-hand-detection-pipeline)
5. [Face & Emotion Detection Pipeline](#5-face--emotion-detection-pipeline)
6. [Prototype Minigame — Interrogatorio](#6-prototype-minigame--interrogatorio)
7. [UI Components](#7-ui-components)
8. [Implementation Order](#8-implementation-order)
9. [C# Coding Conventions](#9-c-coding-conventions)
10. [MediaPipe Unity Plugin — Critical Setup Notes](#10-mediapipe-unity-plugin--critical-setup-notes)
11. [Groq API — Integration Notes](#11-groq-api--integration-notes)
12. [Post-Prototype Scope — Do Not Implement](#12-post-prototype-scope--do-not-implement)
13. [Prototype Verification Checklist](#13-prototype-verification-checklist)

---

## 1. Project Overview

| Field | Value |
|---|---|
| Project name | ARcade Rush |
| Engine | Unity 2022.3 LTS (URP) |
| Language | C# (.NET Standard 2.1) |
| CV library | MediaPipe Unity Plugin (`homuler/MediaPipeUnityPlugin`) |
| LLM provider | Groq Cloud API — model: `llama-3-8b-8192` |
| Target platform | Windows / macOS standalone (WebCam required) |
| Prototype goal | One complete minigame (Interrogatorio) + full shared infrastructure |

### 1.1 What the Prototype Must Prove

- MediaPipe hand landmarks can be read inside Unity and converted to 3D world-space positions with acceptable latency (<30 ms per frame at 30 fps).
- MediaPipe face landmarks can classify the player's current emotion (`happy / neutral / surprised / angry`) reliably enough to be used as game input.
- The Groq LLM API can be called asynchronously from Unity without blocking the game loop, and the response can be displayed as NPC dialogue within 2 seconds.
- The Bootstrap singleton architecture works: singletons survive scene loads and each minigame scene correctly resolves its dependencies through `IMiniGame`.
- The AR camera feed (`RawImage` canvas overlay) is visible and correctly positioned in every scene.

---

## 2. Unity Project Setup

### 2.1 Required Unity Packages

Install all packages via the Unity Package Manager (UPM) before writing any code. Use exact versions to avoid API drift.

| Package | Source | Version / Notes |
|---|---|---|
| Universal Render Pipeline | Unity Registry | 14.x (matches Unity 2022.3 LTS) |
| TextMeshPro | Unity Registry | 3.0.6 — import TMP Essentials on first run |
| MediaPipe Unity Plugin | GitHub UPM | `homuler/MediaPipeUnityPlugin` — add via `manifest.json` git URL |
| Newtonsoft Json | Unity Registry | `com.unity.nuget.newtonsoft-json` 3.2.1 |
| Unity WebRequest | Built-in | Enabled by default — used for Groq API calls |
| Input System | Unity Registry | 1.7.x — enable both input backends during install |

### 2.2 Project Folder Structure

Create the following folder structure under `Assets/`. Do not deviate — other team members and future scenes depend on these paths.

```
Assets/
  Scenes/
    Bootstrap.unity             ← first scene, index 0 in Build Settings
    MainMenu.unity              ← scene index 1
    MG_Interrogatorio.unity     ← minigame scene, index 2

  Scripts/
    Core/                       ← singletons, interfaces, shared utilities
      GameManager.cs
      CameraFeedCtrl.cs
      MediaPipeController.cs
      LLMConnector.cs
      IMiniGame.cs
      SceneLoader.cs
    Minigames/
      Interrogatorio/
        NPCController.cs
        EmotionEvaluator.cs
        ResponseHandler.cs
        InterrogatorioGame.cs   ← implements IMiniGame
    Hand/
      Hand3DProjector.cs
      GestureDetector.cs
      HandModel.cs
      HandTool.cs               ← abstract base
    Face/
      FaceLandmarkReader.cs
      EmotionClassifier.cs
    UI/
      CameraOverlay.cs
      CameraConfigUI.cs         ← manual start & camera switching
      MainMenuController.cs     ← main menu logic
      DebugTrackerUI.cs         ← realtime tracker visualization
      DialogueUI.cs
      HUDController.cs

  Prefabs/
    Core/
      [Bootstrap].prefab        ← all singletons under one root GameObject
      ARCameraCanvas.prefab     ← persistent RawImage overlay
    Hand/
      HandVisual.prefab
    Interrogatorio/
      NPC.prefab

  Materials/
  Textures/
  StreamingAssets/              ← MediaPipe model files live here
    mediapipe/
      hand_landmarker.task
      face_landmarker.task
```

### 2.3 Build Settings Scene Order

**File > Build Settings > Scenes In Build** must list scenes in exactly this order:

| Index | Scene |
|---|---|
| 0 | `Assets/Scenes/Bootstrap.unity` |
| 1 | `Assets/Scenes/MainMenu.unity` |
| 2 | `Assets/Scenes/MG_Interrogatorio.unity` |

> ⚠️ Bootstrap is index 0 so it is the first scene loaded on application start. It loads MainMenu additively after singletons initialize.

---

## 3. Core Architecture — Singletons & Interfaces

### 3.1 The Bootstrap Scene

The Bootstrap scene contains a single root GameObject named `[Bootstrap]`. This object has `DontDestroyOnLoad` called on it in `Awake`. It is the parent of all four singleton MonoBehaviours. It must be the first scene loaded (index 0) and must never be unloaded.

After all singletons finish their `Awake()` initialization, Bootstrap triggers `SceneLoader` to load MainMenu (index 1) using `LoadSceneAsync` in `Single` mode — replacing Bootstrap's scene content while keeping the `DontDestroyOnLoad` objects alive.

### 3.2 GameManager.cs

**Responsibility:** Central state machine. Tracks current game state, current score, and which minigame is active.

```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public GameState State { get; private set; }  // Idle | Playing | Paused | Results
    public int CurrentScore { get; private set; }

    public void StartGame(IMiniGame game);  // called by MainMenu
    public void EndGame();                   // called by IMiniGame.OnEnd()
    public void AddScore(int delta);
    public void PauseGame();
    public void ResumeGame();

    // Events
    public event Action OnGameStarted;
    public event Action OnGameEnded;
    public event Action<int> OnScoreChanged;
}
```

`GameManager` owns no Unity scene objects directly. It communicates state changes via C# events.

### 3.3 CameraFeedCtrl.cs

**Responsibility:** Owns the `WebCamTexture`. Manages camera lifecycle, platform permissions, and hardware handle release in the Editor.

Implementation rules:

- **Manual Start**: Does NOT play on `Awake`. Must be explicitly started via `StartCamera()` (e.g., from `CameraConfigUI`).
- **Permissions**: `StartCamera()` is a coroutine that requests `UserAuthorization.WebCam` on iOS/WebGL and `Permission.Camera` on Android before initializing.
- **Direct Output**: To ensure stability and avoid driver locks, it assigns `_webCamTexture` directly to the `RawImage.texture`.
- **Editor Safety**: Uses `EditorApplication.playModeStateChanged` to force-destroy the `WebCamTexture` and release the OS device handle when exiting Play mode.
- **Legacy Compatibility**: The `SharedTexture` property is deprecated and returns `null`. `MediaPipeController` now reads from `ActiveWebCamTexture`.

> ⚠️ The `RawImage` canvas should be assigned via `SetOutputImage()` or in the Inspector. It renders the raw camera feed.

### 3.4 MediaPipeController.cs

**Responsibility:** Initializes both MediaPipe task runners (Hand Landmarker, Face Landmarker). Runs inference each frame and broadcasts results via events.

Key rules:

- **Direct Pixel Reading**: Instead of using a `RenderTexture`, it pulls pixels directly from `CameraFeedCtrl.Instance.ActiveWebCamTexture.GetPixels32()`.
- Use the **async/GPU delegate mode** of the MediaPipe Unity Plugin.
- Hand results: `public event Action<NormalizedLandmarks> OnHandDetected`
- Face results: `public event Action<NormalizedLandmarks> OnFaceDetected`
- If no hand is detected for 3 frames: `public event Action OnHandLost`
- Load model files from `Application.streamingAssetsPath + "/mediapipe/hand_landmarker.task"`

> 💡 During prototype, set `numHands = 1` on the Hand Landmarker task. Multi-hand support is `[POST-PROTOTYPE]`.

### 3.5 LLMConnector.cs

**Responsibility:** Async wrapper around the Groq Cloud API. Returns generated text to callers without blocking the game loop.

```csharp
public class LLMConnector : MonoBehaviour
{
    public static LLMConnector Instance { get; private set; }

    // Fire-and-forget. Invokes callback on main thread when done.
    public void Ask(string systemPrompt, string userMessage,
                    Action<string> onComplete, Action<string> onError = null);
}
```

Internally, `Ask()` starts a coroutine that uses `UnityWebRequest` to POST to `https://api.groq.com/openai/v1/chat/completions`.

**Request body:**

```json
{
  "model": "llama-3-8b-8192",
  "max_tokens": 200,
  "temperature": 0.7,
  "messages": [
    { "role": "system", "content": "<systemPrompt>" },
    { "role": "user",   "content": "<userMessage>" }
  ]
}
```

Parse the response with `Newtonsoft.Json`. Extract `choices[0].message.content`.

> ⚠️ The API key must be stored in a `ScriptableObject` (`GroqConfig.asset`) at `Assets/Resources/GroqConfig.asset`. **Never hardcode it. Never commit it to source control.**

### 3.6 IMiniGame Interface

**Responsibility:** Contract that every minigame scene implements. Allows `GameManager` and `SceneLoader` to orchestrate any minigame without knowing its internals.

```csharp
public interface IMiniGame
{
    // Called by GameManager after the scene finishes loading.
    void OnStart(MiniGameDependencies deps);

    // Called by GameManager when the player exits or time runs out.
    void OnEnd();

    // The scene build index this minigame lives in.
    int SceneIndex { get; }
}

public class MiniGameDependencies
{
    public GameManager          GameManager;
    public CameraFeedCtrl       Camera;
    public MediaPipeController  MediaPipe;
    public LLMConnector         LLM;
}
```

---

## 4. Hand Detection Pipeline

### 4.1 Hand3DProjector.cs

**Responsibility:** Converts MediaPipe 2D normalized hand landmarks into Unity 3D world-space positions, including a depth estimate.

**Algorithm — implement exactly as described:**

1. Subscribe to `MediaPipeController.OnHandDetected` in `OnEnable`. Unsubscribe in `OnDisable`.
2. Receive `HandLandmarkList` (21 landmarks, each with `x ∈ [0,1]`, `y ∈ [0,1]`, `z` relative).
3. Convert x, y to screen space:
   ```
   screenPos.x = (1 - lm.x) * Screen.width   // mirror horizontally
   screenPos.y = (1 - lm.y) * Screen.height
   ```
4. Cast a ray from `Camera.main` through `screenPos` using `Camera.main.ScreenPointToRay()`. Project to a configurable world-space plane (default: `z = 0` in camera-relative coords).
5. **Depth heuristic:** Measure the pixel distance between wrist landmark (index 0) and middle-finger MCP landmark (index 9). Call this `handSpanPx`. Define a calibrated reference span (Inspector field, default `180 px`) that maps to `referenceDepth` (default `0.5 m`). Compute:
   ```
   depth = referenceDepth * (referenceSpanPx / handSpanPx)
   ```
   Clamp to `[0.1, 1.5]` metres.
6. Expose `public Vector3 WristWorldPos` and `public Vector3[] LandmarkWorldPositions` (length 21), updated every frame a hand is detected.
7. On `OnHandLost`: zero out positions and fire `public event Action OnLost`.

> ⚠️ The depth heuristic is intentionally simple for the prototype. Do not implement stereo or monocular depth networks — that is `[POST-PROTOTYPE]`.

### 4.2 GestureDetector.cs

**Responsibility:** Classifies the current hand pose as a discrete gesture using pure heuristics on landmark positions.

All gestures fire only on **transition** (rising/falling edge), not every frame. Use a `previousGesture` enum to track state.

| Gesture | Detection Rule | Event |
|---|---|---|
| Open hand | All 5 fingertips (4, 8, 12, 16, 20) above their MCP joints (`y < MCP.y` in image space) | `OnOpenHand` |
| Closed fist | All 5 fingertips below their MCP joints (`y > MCP.y` in image space) | `OnClosedFist` |
| Point | Index tip (8) above MCP (5), all others below their MCP | `OnPoint` |
| Pinch | Distance between thumb tip (4) and index tip (8) < `pinchThreshold` (default `0.05` normalized) | `OnPinch` / `OnPinchRelease` |

### 4.3 HandModel.cs

**Responsibility:** Renders a visual skeleton of the detected hand using Unity `LineRenderer`s — one per bone segment.

- On `Start`, create 21 sphere GameObjects (radius `0.008 m`) as joint markers and 20 `LineRenderer` components for bone segments.
- Every frame that landmarks are available, move each joint sphere to `LandmarkWorldPositions[i]` and update `LineRenderer` start/end points to match the MediaPipe hand connection graph.
- Set all renderers to a configurable color (default: teal `#1D9E75`). Alpha `0.7`.
- Disable all renderers when `OnHandLost` fires.

> 💡 The MediaPipe hand connection graph (which landmark indices to connect) is in the MediaPipe documentation. Hardcode the 20 connections as a `static readonly int[,]` array in `HandModel.cs`.

---

## 5. Face & Emotion Detection Pipeline

### 5.1 FaceLandmarkReader.cs

**Responsibility:** Receives raw face landmark data from `MediaPipeController` and extracts the landmark subsets used by `EmotionClassifier`.

Subscribe to `MediaPipeController.OnFaceDetected`. Extract and expose as `public float[]` the following normalized distances (indices are MediaPipe Face Mesh canonical indices):

| Metric | Landmarks | Normalization |
|---|---|---|
| Mouth openness | 13 (upper lip) to 14 (lower lip) | Face height: landmark 10 to 152 |
| Eye openness left | 159 to 145 | Eye width: landmark 33 to 133 |
| Eye openness right | 386 to 374 | Eye width: landmark 362 to 263 |
| Brow raise | Average y-distance of 70 and 300 (brow tips) vs 21 and 251 (brow base) | Face height: landmark 10 to 152 |

### 5.2 EmotionClassifier.cs

**Responsibility:** Classifies the current emotion from the normalized facial metrics provided by `FaceLandmarkReader`. Use a **threshold-based rule system** — no neural network required for the prototype.

```csharp
public enum EmotionLabel { Neutral, Happy, Surprised, Angry }
```

| Emotion | Rule |
|---|---|
| Happy | `mouthOpenness > 0.08` AND `browRaise < 0.1` |
| Surprised | `mouthOpenness > 0.12` AND `eyeOpennessAvg > 0.35` AND `browRaise > 0.15` |
| Angry | `mouthOpenness < 0.04` AND `browRaise < 0.05` AND `eyeOpenness < 0.25` |
| Neutral | None of the above (default state) |

- Fire `public event Action<EmotionLabel> OnEmotionChanged` only when the label changes.
- Apply **temporal smoothing**: require the same classification for **8 consecutive frames** before triggering the event.

---

## 6. Prototype Minigame — Interrogatorio

### 6.1 Concept

The player is a detective interrogating an NPC suspect. The player's detected emotion and hand gestures influence the NPC's responses, generated in real-time by the LLM. The player scores points by matching the correct emotion the NPC is "hiding" before a 120-second timer expires.

### 6.2 Scene Contents (`MG_Interrogatorio.unity`)

| GameObject | Components | Notes |
|---|---|---|
| `[Game]` | `InterrogatorioGame.cs` | Root logic object. Implements `IMiniGame`. |
| `[NPC]` | `NPCController.cs`, `Animator` | NPC prefab. Animator states: Idle / Talk / Nervous / Angry. |
| `[EmotionEval]` | `EmotionEvaluator.cs` | Reads from `EmotionClassifier`. Scores the player. |
| `[Response]` | `ResponseHandler.cs` | Calls `LLMConnector`. Pipes text to `DialogueUI`. |
| `AR Camera` | `Camera`, `CameraFeedCtrl` ref | Persistent across all scenes. Assigned in Bootstrap. |
| `Canvas_HUD` | `HUDController.cs`, TMP elements | Score, timer, emotion indicator. Sort order `100`. |
| `Canvas_Dialogue` | `DialogueUI.cs`, TMP elements | NPC speech bubble. Sort order `200`. |

### 6.3 InterrogatorioGame.cs — Full Behaviour

**`OnStart(deps)`:**
- Cache all dependency references.
- Subscribe to `EmotionClassifier.OnEmotionChanged` and `GestureDetector.OnOpenHand` / `OnClosedFist`.
- Randomly select a `targetEmotion` from `{Happy, Surprised, Angry}` and store privately.
- Set timer to `120` seconds.
- Call `NPCController.StartIntroSequence()`.

**Per-second update** (`InvokeRepeating` at 1 s interval):
- Decrement timer. If timer reaches `0`, call `EndGame()` with failure flag.
- Update `HUDController` with current timer and score.

**On emotion change** (`EmotionClassifier` event):
- If `detectedEmotion == targetEmotion`, call `EndGame()` with success flag.
- Otherwise, build a prompt (see Section 6.4) and call `LLMConnector.Ask()`.

**On LLM response received:**
- Call `DialogueUI.ShowLine(responseText, duration: 4f)`.
- Call `NPCController.TriggerTalkAnimation()`.

**`OnEnd()`:**
- Unsubscribe all events.
- Stop `InvokeRepeating`.
- Call `GameManager.EndGame()`.
- Load MainMenu via `SceneLoader` with a 2 s delay to show results.

### 6.4 LLM Prompt Template

Fill bracketed fields at runtime before calling `LLMConnector.Ask()`.

**System prompt:**
```
You are an NPC suspect in a detective interrogation game. Your hidden emotion is
[TARGET_EMOTION]. The detective can see your face. You must never directly reveal
your hidden emotion. Respond in character: 1-2 sentences maximum. Be evasive but
realistic. Do not break the fourth wall. The game language is Spanish.
```

**User message:**
```
The detective is currently showing the emotion: [DETECTED_EMOTION].
Time remaining: [TIMER] seconds. Score so far: [SCORE].
Generate the NPC's next line of dialogue.
```

> ⚠️ Keep `max_tokens` at `120` for the prototype. This caps cost and ensures fast return from Groq.

### 6.5 NPCController.cs

- Holds an `Animator` reference.
- Exposes: `StartIntroSequence()`, `TriggerTalkAnimation()`, `TriggerNervousAnimation()`, `TriggerAngryAnimation()`.
- `StartIntroSequence()` plays the Idle animation, waits 1.5 s, then calls `ResponseHandler.RequestIntroLine()` to get the opening line from the LLM.
- Does **not** own any game logic — purely drives visuals and animation state.

### 6.6 EmotionEvaluator.cs

- Listens to `EmotionClassifier.OnEmotionChanged`.
- Compares detected emotion to the `targetEmotion` stored in `InterrogatorioGame`.
- Adjacent emotion (e.g., Surprised when target is Happy): `GameManager.AddScore(5)`.
- Exact match: `GameManager.AddScore(20)`, then fires `OnCorrectEmotionDetected` event back to `InterrogatorioGame`.

### 6.7 ResponseHandler.cs

- Calls `LLMConnector.Ask()` with the prompt template from Section 6.4.
- On response: passes text to `DialogueUI.ShowLine()` and calls `NPCController.TriggerTalkAnimation()`.
- Tracks whether a request is in-flight (`bool _isRequesting`). If another emotion change arrives while in-flight, queue it and send after completion. **Queue max depth: 1** — discard older queued requests.

---

## 7. UI Components

### 7.1 DialogueUI.cs

- `ShowLine(string text, float duration)`: fades in, holds for `duration` seconds, fades out using a coroutine.
- If called while a coroutine is already running, stop the previous one and start fresh.
- Anchored to bottom-center of screen. Height: `120 px`. Width: `80%` of screen width.

### 7.2 HUDController.cs

- `UpdateTimer(float seconds)`: format as `"MM:SS"` using `TimeSpan.FromSeconds().ToString(@"mm\:ss")`.
- `UpdateScore(int score)`: update score TMP text.
- `ShowEmotion(EmotionLabel label)`: display icon + label top-left. Colors: Happy = green, Surprised = blue, Angry = red, Neutral = gray.

---

## 8. Implementation Order

Follow this order exactly. Verify each phase before proceeding.

| Phase | What to Build | Done When… |
|---|---|---|
| **1 — Project shell** | Unity project, URP, folder structure, Build Settings scene order, MediaPipe plugin import, StreamingAssets model files. | Editor opens without errors. No import errors in Console. |
| **2 — Camera feed** | `CameraFeedCtrl`, persistent `Canvas/RawImage` overlay. Bootstrap scene with `DontDestroyOnLoad` root object. | Camera feed visible in Game view. Object survives scene load. |
| **3 — MediaPipe hand** | `MediaPipeController` (hand task only), `Hand3DProjector`, `HandModel`, `GestureDetector`. | Hand skeleton renders in Game view tracking the developer's hand. `OnOpenHand` / `OnClosedFist` fire correctly. |
| **4 — MediaPipe face** | `FaceLandmarkReader`, `EmotionClassifier`. | `OnEmotionChanged` fires with correct label when developer makes expressions. |
| **5 — LLM integration** | `GroqConfig` ScriptableObject, `LLMConnector`. A temporary test button in Bootstrap that sends a hardcoded prompt. | Test button returns LLM text in Unity console within 3 seconds. |
| **6 — Singletons & interface** | `GameManager`, `IMiniGame`, `MiniGameDependencies`, `SceneLoader`, MainMenu stub (one button: Interrogatorio). | Button loads `MG_Interrogatorio`. `GameManager.State == Playing`. |
| **7 — Interrogatorio scene** | All GameObjects from Section 6.2. `InterrogatorioGame`, `NPCController` (Debug.Log stubs for animator), `EmotionEvaluator`, `ResponseHandler`, `DialogueUI`, `HUDController`. | Full loop runs: emotion detected → LLM called → dialogue shown → score updates → timer → returns to MainMenu. |
| **8 — NPC animator** | Import/create simple NPC model. Wire Animator with Idle / Talk states. Connect `NPCController`. | NPC plays Talk animation when LLM response arrives. |
| **9 — Polish & calibration** | Depth heuristic tuning, emotion threshold tuning, prompt tuning, latency measurement. | Median emotion-to-dialogue cycle < 2.5 s on test hardware. |

---

## 9. C# Coding Conventions

All code must follow these conventions without exception.

| Rule | Detail |
|---|---|
| **Namespaces** | `ARcadeRush.Core`, `ARcadeRush.Hand`, `ARcadeRush.Face`, `ARcadeRush.UI`, `ARcadeRush.Minigames.Interrogatorio` |
| **Singleton pattern** | `Awake` sets `Instance`. If `Instance != null` on Awake, `Destroy(gameObject)` and return. No lazy initialization. |
| **Events** | Use `System.Action` delegates. Never use `UnityEvent` for code-to-code communication (only for Inspector-wired UI callbacks). |
| **Coroutines** | Prefix with `Co`: `private IEnumerator CoShowDialogue()`. Store refs to stop them: `private Coroutine _dialogueCo`. |
| **Private fields** | Prefix with underscore: `private float _timer`. Public state is properties with `{ get; private set; }`. |
| **Inspector fields** | Use `[SerializeField] private` — never `public` fields. |
| **Null checks** | Use `?.` and `??`. Never let `NullReferenceException` fail silently — log with `Debug.LogError()`. |
| **Magic numbers** | No magic numbers in logic. Declare as `private const` or `[SerializeField]` with a default. |
| **Async** | Use coroutines for all Unity async work. Do not use `async/await` unless using Unity 2022+ Awaitable API — flag explicitly if used. |
| **Comments** | XML summary on all public methods. Inline comments only for non-obvious logic. |

---

## 10. MediaPipe Unity Plugin — Critical Setup Notes

### 10.1 Adding the Plugin

Add to `Packages/manifest.json` under `dependencies`:

```json
"com.github.homuler.mediapipe": "https://github.com/homuler/MediaPipeUnityPlugin.git?path=Packages/com.github.homuler.mediapipe"
```

Git must be installed and accessible from the command line. After the package resolves, the MediaPipe setup wizard will appear — follow all steps.

### 10.2 Model Files

- Download `hand_landmarker.task` and `face_landmarker.task` from the MediaPipe model card pages on [ai.google.dev](https://ai.google.dev).
- Place in `Assets/StreamingAssets/mediapipe/` exactly.
- Load at runtime:
  ```csharp
  var assetPath = Path.Combine(Application.streamingAssetsPath, "mediapipe", "hand_landmarker.task");
  ```

> 💡 `StreamingAssets` is the only Unity folder accessible at runtime on all platforms without AssetBundle overhead.

### 10.3 Platform Target

The prototype targets **standalone desktop only**. Do not configure Android or iOS build targets — they have different MediaPipe initialization sequences and are out of scope.

### 10.4 Editor Permissions

- **Allow Unsafe Code** must be enabled: `Player Settings > Other Settings > Allow Unsafe Code`.
- **macOS:** Grant camera access to the Unity process in `System Preferences > Privacy > Camera`.
- **Windows:** No special permission steps required.

---

## 11. Groq API — Integration Notes

### 11.1 Authentication

Create `Assets/Resources/GroqConfig.asset`:

```csharp
[CreateAssetMenu(fileName = "GroqConfig", menuName = "ARcadeRush/GroqConfig")]
public class GroqConfig : ScriptableObject
{
    [SerializeField] private string _apiKey;
    public string ApiKey => _apiKey;
}
```

`LLMConnector` loads it with `Resources.Load<GroqConfig>("GroqConfig")` in `Awake()`. Add `GroqConfig.asset` to `.gitignore`.

### 11.2 UnityWebRequest Setup

```csharp
var request = new UnityWebRequest("https://api.groq.com/openai/v1/chat/completions", "POST");
request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
request.downloadHandler = new DownloadHandlerBuffer();
request.SetRequestHeader("Content-Type", "application/json");
request.SetRequestHeader("Authorization", $"Bearer {_config.ApiKey}");
yield return request.SendWebRequest();
```

### 11.3 Error Handling

| HTTP Status | Action |
|---|---|
| `200` | Parse `choices[0].message.content` with `Newtonsoft.Json`. Invoke `onComplete`. |
| `429` (rate limit) | Wait 2 s, retry once. On second failure invoke `onError("Rate limited")`. |
| `401` | Log prominent error: `"Groq API key invalid or missing. Check GroqConfig.asset."` Do not retry. |
| Connection error | Log and invoke `onError("Connection failed")`. |

---

## 12. Post-Prototype Scope — Do Not Implement

The following are defined for completeness but must not be built during the prototype phase.

| Feature | Reason Deferred |
|---|---|
| Multi-hand detection | Adds complexity without validating the core pipeline. `numHands = 1` for now. |
| 3 additional minigames | Limpia Pantalla, Simon Dice, Oficinista defined but not in prototype. |
| Score persistence / leaderboard | Requires a database or file I/O layer not yet designed. |
| DeepSeek model migration | Groq + Llama-3-8B is sufficient for prototype validation. |
| Depth calibration UI | Inspector-exposed `referenceSpanPx` is sufficient; a runtime calibration flow is not. |
| Emotion ML classifier | Threshold-based `EmotionClassifier` is sufficient; an ONNX neural net is post-prototype. |
| Mobile / WebGL build | Desktop standalone only. |
| User accounts / login | No auth in prototype. |
| Multiplayer | Not in scope at all. |

---

## 13. Prototype Verification Checklist

The coding agent must verify every item before declaring the prototype complete.

| # | Check | How to Verify |
|---|---|---|
| 1 | WebCam feed visible in all scenes | Click "Start Camera" in MainMenu or Config. Feed visible. |
| 2 | Hand skeleton tracks correctly | Move hand slowly left/right/near/far. Skeleton follows without teleporting. |
| 3 | Depth heuristic is plausible | Hand near camera appears closer in 3D than hand far away. Visual inspection. |
| 4 | Open/Closed fist detected | Console logs `OnOpenHand` and `OnClosedFist` on correct gestures. |
| 5 | Happy emotion detected | Smile widely → console logs `Happy` within 8 frames (~0.27 s at 30 fps). |
| 6 | Surprised detected | Open mouth wide, raise brows → console logs `Surprised`. |
| 7 | Angry detected | Furrow brow, press lips → console logs `Angry`. |
| 8 | Neutral is default | Relaxed face → console logs `Neutral`. |
| 9 | LLM responds in < 3 s | Time from `Ask()` call to `onComplete` using `Time.realtimeSinceStartup`. |
| 10 | Dialogue UI shows and fades | `ShowLine()` plays fade-in, holds, fades out. No leftover text on screen. |
| 11 | Timer counts down | HUD timer decrements every second, stops at `0:00` and ends game. |
| 12 | Score increments on emotion match | Match target → score +20. Adjacent emotion → +5. |
| 13 | Game ends on correct emotion or timeout | Both paths load MainMenu after 2 s results delay. |
| 14 | Bootstrap persists across scene loads | `GameManager.Instance` is not null in `MG_Interrogatorio` scene. |
| 15 | No NullReferenceExceptions in normal operation | Play for 2 full minutes with varied gestures. Console shows zero NREs. |

---

*ARcade Rush · Prototype Context Document · PUCV 2026 · v1.0*