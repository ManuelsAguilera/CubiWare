# ARcade Rush — Developer Guide

> **Last updated:** 2026-05-10
> **Unity Version:** 2022.3 LTS (URP)  
> **Scripting Runtime:** .NET Standard 2.1  
> **Namespace Convention:** `ARcadeRush.{Module}` (e.g. `Core`, `Hand`, `Face`, `UI`, `Minigames.Shoot`)

---

## Table of Contents

1. [Project Architecture](#1-project-architecture)
2. [Singleton System](#2-singleton-system)
3. [Scene Flow](#3-scene-flow)
4. [Hand Pipeline](#4-hand-pipeline)
5. [Face Pipeline](#5-face-pipeline)
6. [Camera System](#6-camera-system)
7. [UI Components](#7-ui-components)
8. [Creating a New Minigame](#8-creating-a-new-minigame)
9. [LLM Integration](#9-llm-integration)
10. [Shooter Minigame](#10-shooter-minigame)
11. [Build Settings](#11-build-settings)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Project Architecture

```
Assets/
├── Scenes/
│   ├── Bootstrap.unity       [Build Index 0] — Entry point
│   ├── MainMenu.unity        [Build Index 1] — Hub / minigame selector
│   └── MG_Shooter.unity      [Build Index 2] — First minigame
├── Scripts/
│   ├── Core/                  — Singletons & shared systems
│   ├── Hand/                  — Hand tracking pipeline
│   ├── Face/                  — Face landmark pipeline
│   ├── UI/                    — Reusable UI widgets
│   └── Minigames/
│       └── Shooter/           — Shooter minigame logic
├── Prefabs/                   — Reusable prefabs
├── Resources/                 — ScriptableObjects (GroqConfig)
└── StreamingAssets/
    └── mediapipe/             — MediaPipe model files
```

### Architecture Flow

```
Bootstrap.unity (Index 0)
  │  Creates DontDestroyOnLoad singletons:
  │    • GameManager        — Game state machine
  │    • SceneLoader        — Scene transitions
  │    • CameraFeedCtrl     — WebCamTexture manager
  │    • MediaPipeController — Hand + Face landmarker
  │    • LLMConnector       — Groq API wrapper
  │
  └─→ Auto-loads MainMenu.unity (Index 1)
         │
         └─→ Player clicks a minigame button
                │
                └─→ SceneLoader.LoadScene(2)  →  MG_Shooter.unity
                       │  SceneLoader is still alive (DontDestroyOnLoad)
                       │  GameManager.StartGame(iMiniGame) is called
                       │
                       └─→ Minigame calls GameManager.EndGame()
                              └─→ Returns to MainMenu
```

All singletons are placed on a root `GameObject` named `Bootstrap` in [`Bootstrap.unity`](Assets/Scenes/Bootstrap.unity). The `SceneLoader` auto-loads `MainMenu` on `Start()`.

---

## 2. Singleton System

Five core singletons follow the same pattern. Each has:
- `public static {Type} Instance { get; private set; }`
- `Awake()` — sets `Instance`, calls `DontDestroyOnLoad`, destroys duplicates

### 2.1 GameManager

[`GameManager.cs`](Assets/Scripts/Core/GameManager.cs)

```csharp
public enum GameState { Idle, Playing, Paused, Results }
```

| Member | Purpose |
|--------|---------|
| `StartGame(IMiniGame game)` | Sets state → Playing, invokes `OnGameStarted` |
| `EndGame()` | Sets state → Results, invokes `OnGameEnded` |
| `AddScore(int delta)` | Updates score, invokes `OnScoreChanged` |
| `PauseGame()` / `ResumeGame()` | Toggle pause state |

**Events:**
- `event Action OnGameStarted`
- `event Action OnGameEnded`
- `event Action<int> OnScoreChanged`

### 2.2 SceneLoader

[`SceneLoader.cs`](Assets/Scripts/Core/SceneLoader.cs)

| Method | Description |
|--------|-------------|
| `LoadScene(int index)` | Immediately loads scene by build index |
| `LoadSceneDelayed(int index, float delay)` | Loads after `delay` seconds via coroutine |

Used by all scene transitions. The `Bootstrap` scene auto-loads `MainMenu` (index 1) on `Start()`.

### 2.3 CameraFeedCtrl

[`CameraFeedCtrl.cs`](Assets/Scripts/Core/CameraFeedCtrl.cs)

| Method | Description |
|--------|-------------|
| `StartCamera()` | Requests permissions, starts `WebCamTexture` |
| `StopCamera()` | Stops webcam feed |
| `SwitchCamera(string deviceName)` | Switches to a specific camera device |
| `GetDeviceNames()` | Returns available camera names (static) |
| `SetOutputImage(RawImage newOutput)` | Redirects camera feed to a different RawImage |

**Important:** The camera does NOT start on `Awake()`. It must be started explicitly via the UI (`CameraConfigUI` → "Encender" button) or programmatically.

### 2.4 MediaPipeController

[`MediaPipeController.cs`](Assets/Scripts/Core/MediaPipeController.cs)

Creates a dual `HandLandmarker` + `FaceLandmarker` running in `LIVE_STREAM` mode. Results arrive on a background thread and are queued via `ConcurrentQueue` for thread-safe delivery on `Update()`.

**Events:**
- `event Action<NormalizedLandmarks> OnHandLandmarks`
- `event Action<NormalizedLandmarks> OnFaceLandmarks`
- `event Action OnTrackingLost`

### 2.5 LLMConnector

[`LLMConnector.cs`](Assets/Scripts/Core/LLMConnector.cs)

See [Section 9 — LLM Integration](#9-llm-integration).

---

## 3. Scene Flow

### Build Index Assignment

| Index | Scene | Purpose |
|-------|-------|---------|
| 0 | `Bootstrap.unity` | Entry point, creates singletons |
| 1 | `MainMenu.unity` | Minigame selection hub |
| 2 | `MG_Shooter.unity` | Shooter minigame |

> **Note:** Build Indices must be updated in **File → Build Settings** whenever a new scene is added or removed.

### Bootstrap → MainMenu Transition

In [`SceneLoader.cs`](Assets/Scripts/Core/SceneLoader.cs), the `Start()` method calls:
```csharp
SceneManager.LoadScene(1); // MainMenu
```

### MainMenu → Minigame

In [`MainMenuController.cs`](Assets/Scripts/UI/MainMenuController.cs):
```csharp
_startShooterBtn.onClick.AddListener(() => LoadScene(2));
```
The `LoadScene` method routes through `SceneLoader.Instance.LoadSceneDelayed(index, 0.5f)` for a smooth transition.

### Minigame → MainMenu (Return)

Each minigame calls `GameManager.Instance.EndGame()`, then loads MainMenu (index 1) via `SceneLoader`.

Example from [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs):
```csharp
private void TimeUp()
{
    GameManager.Instance.EndGame();
    // After 2s delay, return to MainMenu
    SceneLoader.Instance.LoadSceneDelayed(1, 2f);
}
```

---

## 4. Hand Pipeline

```
MediaPipeController (raw NormalizedLandmarks)
  │
  ▼
Hand3DProjector (2D → 3D world-space conversion)
  │  Uses depth calibration (Near/Mid/Far)
  │  Exposes: LandmarkWorldPositions[21]
  │
  ├─► GestureDetector (event-based gesture recognition)
  │     Events: OnClosedFist, OnOpenHand, OnPointing, OnThumbDown
  │
  ├─► HandModel (visual skeleton — spheres + lines)
  │
  └─► HandDepthCalibrator (runtime calibration, PlayerPrefs)
```

### 4.1 Hand3DProjector

[`Hand3DProjector.cs`](Assets/Scripts/Hand/Hand3DProjector.cs)

Converts normalized 2D MediaPipe landmarks (0–1 range) into 3D world positions. Uses a **three-point depth calibration** (Near/Mid/Far) with lerp to determine Z-depth from hand scale.

**Key property:** `public Vector3[] LandmarkWorldPositions { get; }` — array of 21 world-space positions.

**Landmark indices (MediaPipe hand topology):**
| Index | Name | Usage |
|-------|------|-------|
| 0 | Wrist | Root |
| 4 | Thumb Tip | Thumb-down detection |
| 5 | Index MCP | Base of index finger |
| 8 | Index Tip | **Aim direction** (Shooter) |
| 12 | Middle Tip | Pointing detection |
| 20 | Pinky Tip | Pointing detection |

### 4.2 GestureDetector

[`GestureDetector.cs`](Assets/Scripts/Hand/GestureDetector.cs)

**Gesture events:**
| Event | Detection Logic |
|-------|----------------|
| `OnClosedFist` | All finger tips curled toward palm |
| `OnOpenHand` | All fingers extended |
| `OnPointing` | Index extended, others curled |
| `OnThumbDown` | Index pointing + thumb tip below thumb IP joint |

Thumb-down detection uses image-space Y coordinates:
```csharp
bool thumbDown = indexUp && norm[4].y > norm[3].y;
// norm[4] = thumb tip, norm[3] = thumb IP joint
```

### 4.3 HandModel

[`HandModel.cs`](Assets/Scripts/Hand/HandModel.cs)

Visualizes the detected hand as a skeleton using 21 spheres connected by 20 `LineRenderer` segments. Colors and materials are configurable in the Inspector.

### 4.4 HandDepthCalibrator

[`HandDepthCalibrator.cs`](Assets/Scripts/Hand/HandDepthCalibrator.cs)

Runtime calibration tool. Keys:
- **1** — Calibrate Near
- **2** — Calibrate Mid
- **3** — Calibrate Far
- **R** — Reset to defaults

Calibration values are saved to `PlayerPrefs`.

---

## 5. Face Pipeline

```
MediaPipeController (raw NormalizedLandmarks)
  │
  ▼
FaceLandmarkReader (computes 4 normalized metrics)
  │  • mouthOpenness  (0-1)
  │  • leftEyeOpenness (0-1)
  │  • rightEyeOpenness (0-1)
  │  • browRaise       (0-1)
  │
  ▼
EmotionClassifier (threshold-based, 8-frame smoothing)
  │  Output: EmotionLabel (Happy / Surprised / Angry / Neutral)
  │  Events: OnEmotionChanged
```

### 5.1 FaceLandmarkReader

[`FaceLandmarkReader.cs`](Assets/Scripts/Face/FaceLandmarkReader.cs)

Reads 478 MediaPipe face landmarks and computes normalized ratios. The metrics are ratios (0-1) comparing the current measurement against the user's rest/baseline state.

### 5.2 EmotionClassifier

[`EmotionClassifier.cs`](Assets/Scripts/Face/EmotionClassifier.cs)

Uses simple threshold rules:
- **Happy:** mouth open + brows raised
- **Surprised:** mouth very open + eyes wide
- **Angry:** brows lowered + mouth neutral

Temporal smoothing: maintains an 8-frame window of classifications and outputs the most frequent label. This prevents flickering between emotions.

---

## 6. Camera System

[`CameraFeedCtrl.cs`](Assets/Scripts/Core/CameraFeedCtrl.cs)

### Setup

1. The `CameraFeedCtrl` singleton is created in the `Bootstrap` scene
2. It does NOT auto-start — must be triggered by user action
3. On `StartCamera()`:
   - Requests `WebCam` permission (Android/iOS)
   - Starts the first available `WebCamTexture`
   - Routes frames to the assigned `RawImage` via `SetOutputImage()`

### CameraConfigUI

[`CameraConfigUI.cs`](Assets/Scripts/UI/CameraConfigUI.cs)

Provides the **"Encender"** (Start) button and a gear panel with:
- Camera device dropdown (populated from available devices)
- Start/stop toggle

### CameraOverlay

[`CameraOverlay.cs`](Assets/Scripts/UI/CameraOverlay.cs)

Attached to the AR camera `RawImage`. Maintains aspect ratio of the camera feed by adjusting the `RectTransform` scale on `Update()`.

---

## 7. UI Components

### 7.1 HUDController

[`HUDController.cs`](Assets/Scripts/UI/HUDController.cs)

In-game heads-up display. Methods:
- `UpdateTimer(float seconds)` — shows `MM:SS` format
- `UpdateScore(int score)` — shows current score
- `ShowEmotion(EmotionLabel label)` — shows detected emotion with color coding
- `SetHUDVisible(bool)` — toggles all HUD elements (timer, score, ammo, reload indicator, music label, emotion label, wave announcement) — used when pausing/resuming the game
- `ShowPauseOverlay(string message)` — shows a pause overlay with a custom message (e.g. "PRESS SPACE TO START", "PAUSED")
- `HidePauseOverlay()` — hides the pause overlay

**New serialized fields:**

| Field | Type | Description |
|-------|------|-------------|
| `_pauseOverlay` | `GameObject` | Root overlay GameObject shown when game is paused or waiting for first input |
| `_pauseText` | `TMP_Text` | Text label on the pause overlay to display the pause message |

### 7.2 DebugTrackerUI

[`DebugTrackerUI.cs`](Assets/Scripts/UI/DebugTrackerUI.cs)

Real-time debug overlay showing:
- Current gesture (from GestureDetector)
- Current emotion (from EmotionClassifier)

Useful during development. Can be disabled for release builds.

### 7.3 DialogueUI

[`DialogueUI.cs`](Assets/Scripts/UI/DialogueUI.cs)

Displays text with a fade-in/hold/fade-out animation. Used by minigames that need dialogue (e.g. Interrogatorio — retained for future minigames).

### 7.4 MainMenuController

[`MainMenuController.cs`](Assets/Scripts/UI/MainMenuController.cs)

Handles minigame selection. Each button's `onClick` calls `SceneLoader.Instance.LoadSceneDelayed(buildIndex, 0.5f)`.

**Serialized fields:**
```csharp
[SerializeField] private Button _startShooterBtn;  // Build index 3
[SerializeField] private Button _startTestingSceneBtn;  // Build index 2 (optional)
```

**Score display (new):**
```csharp
[Header("Score Display")]
[SerializeField] private TMP_Text _lastScoreText;     // Displays ShooterGame.LastScore
[SerializeField] private string _scorePrefix = "Last Score: ";
```

In `Start()`, the controller reads `ShooterGame.LastScore` (static field) and displays it. The label is hidden when no score is available (initial launch). This allows the MainMenu to function as the "Game Over" screen — after the Shooter minigame ends, the player returns to the same MainMenu layout with their last score shown.

---

## 8. Creating a New Minigame

### Step 1: Implement IMiniGame

[`IMiniGame.cs`](Assets/Scripts/Core/IMiniGame.cs)

```csharp
public interface IMiniGame
{
    int SceneIndex { get; }
    void OnStart(MiniGameDependencies deps);
    void OnEnd();
}

public class MiniGameDependencies
{
    public GameManager GameManager;
    public CameraFeedCtrl CameraFeed;
    public MediaPipeController MediaPipe;
    public LLMConnector LLM;
}
```

### Step 2: Create the scene

1. Create a new scene at `Assets/Scenes/MG_{Name}.unity`
2. Add it to **Build Settings** with the next available index
3. The scene should contain:
   - An object with your `IMiniGame` implementation
   - A camera + light
   - Any minigame-specific GameObjects

### Step 3: Wire into MainMenu

In [`MainMenuController.cs`](Assets/Scripts/UI/MainMenuController.cs):
1. Add a new `[SerializeField] private Button _start{Name}Btn;`
2. In `Start()`, add:
   ```csharp
   _start{Name}Btn.onClick.AddListener(() => LoadScene({buildIndex}));
   ```
3. In the MainMenu scene, assign the button reference in the Inspector

### Step 4: Add to Build Settings

Open **File → Build Settings** and ensure the new scene is added with the correct index order:
0. Bootstrap
1. MainMenu
2. MG_Shooter
3. MG_{Name} (or replace Shooter if this is the new primary)

### Step 5: Access dependencies

When `OnStart(MiniGameDependencies deps)` is called, store the references:
```csharp
public void OnStart(MiniGameDependencies deps)
{
    _gameManager = deps.GameManager;
    _cameraFeed = deps.CameraFeed;
    _mediaPipe = deps.MediaPipe;
    _llm = deps.LLM;
}
```

---

## 9. LLM Integration

### 9.1 Configuration

1. Create or locate [`GroqConfig.asset`](Assets/Resources/GroqConfig.asset) in `Assets/Resources/`
2. Set your API key in the Inspector (the `_apiKey` field)
3. The asset is a `ScriptableObject` and is loaded by `LLMConnector` on `Awake()`

### 9.2 LLMConnector API

[`LLMConnector.cs`](Assets/Scripts/Core/LLMConnector.cs)

```csharp
public void Ask(
    string systemPrompt,
    string userMessage,
    Action<string> onComplete,
    Action<string> onError = null
);
```

| Parameter | Description |
|-----------|-------------|
| `systemPrompt` | System role definition (e.g. "You are a detective...") |
| `userMessage` | The player's response or query |
| `onComplete` | Callback with the LLM response text |
| `onError` | Callback with error message |

**Rate limiting:** The connector includes automatic retry with exponential backoff (up to 3 retries) when a 429 (rate limit) response is received.

### 9.3 Test Button

[`LLMTestButton.cs`](Assets/Scripts/Core/LLMTestButton.cs)

A debug button that triggers a test LLM request. Place it in any scene during development. It logs the response to the Unity Console.

---

## 10. Shooter Minigame

### 10.1 Overview

```
MG_Shooter.unity
├── Main Camera (field of view ~60°)
├── Directional Light
├── ShooterGameController
│   └── ShooterGame.cs (IMiniGame implementation)
├── TargetManager
│   └── TargetManager.cs (row grouping, round-robin activation)
├── Targets (parent)
│   └── Target_Easy/Med/Hard (pre-placed, start disabled)
├── HUDController (timer + score + ammo + pause overlay)
└── HandController
    └── ShooterHandController.cs (aim + fire mechanics)
```

### 10.2 Game Flow

1. **Start (Paused):** Game loads but starts in a paused state. HUD is hidden. "PRESS SPACE TO START" overlay is shown.
2. **Unpause:** Player presses Space → `BeginGame()` is called → HUD appears, wave progression begins, 90-second timer starts.
3. **Spawning:** Targets appear in 3 rows (Easy → Medium → Hard) based on score thresholds.
4. **Aiming:** Player points index finger at targets.
5. **Firing:** Make a fist gesture to shoot (or left-click in debug mode).
6. **Scoring:** Bandit = +5/+10/+20 (per row), Innocent = -10/-10/-15.
7. **Pause:** GameManager can pause the game (e.g. via emotion detection) → all HUD elements hide, "PAUSED" overlay appears.
8. **Resume:** Unpause → HUD elements reappear, gameplay continues.
9. **End:** Timer expires → `OnEnd()` stores `LastScore` (static), loads MainMenu scene with score displayed.

### 10.3 ShooterGame

[`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs)

| Member | Description |
|--------|-------------|
| `SceneIndex` | Returns `2` |
| `OnStart(deps)` | Subscribes to GameManager events, starts in paused state (HUD hidden, "PRESS SPACE TO START" shown) |
| `OnEnd()` | Stores `LastScore`, unsubscribes, deactivates targets, loads MainMenu scene |
| `BeginGame()` | Called on first unpause — starts `CoWaveProgression()` and `InvokeRepeating(TimerTick)` |
| `HandleGamePaused()` | Called via `GameManager.OnGamePaused` — hides HUD, shows "PAUSED" overlay |
| `HandleGameResumed()` | Called via `GameManager.OnGameResumed` — shows HUD, hides overlay |
| `LastScore` | `public static int` — final score from last game, read by `MainMenuController` |
| `_gameDuration` | 90 seconds (configurable in Inspector) |
| `_unpauseKey` | `KeyCode.Space` — key to unpause (configurable in Inspector) |

### 10.4 TargetSpawner

[`TargetSpawner.cs`](Assets/Scripts/Minigames/Shooter/TargetSpawner.cs)

**Row configuration:**

| Row | Z Position | Max Targets |
|-----|-----------|-------------|
| Near | 5 | 3 |
| Mid | 12 | 4 |
| Far | 20 | 5 |

**Object pooling:** Pre-warms 20 objects (10 bandit, 10 innocent) on `Awake()`. Pool is stored as children of a `TargetPool` GameObject. Recycles inactive objects; creates new ones if pool is exhausted.

**Configurable fields:**
- `_spawnInterval` (default 2s)
- `_banditRatio` (default 70%)
- `_xMin` / `_xMax` (horizontal range, default -6 to 6)
- `_yPosition` (vertical position, default 2)

### 10.5 Target

[`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs)

```csharp
public enum TargetType { Bandit, Innocent }
```

| Property | Description |
|----------|-------------|
| `Type` | Bandit (+10) or Innocent (-20) |
| `IsAlive` | False after `OnHit()` is called |
| `OnHit()` | Calculates score, plays hit effect (if assigned), deactivates self |

After `OnHit()`, the target is returned to the pool (deactivated) for reuse.

### 10.6 ShooterHandController

[`ShooterHandController.cs`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs)

**Aiming:** Uses `Hand3DProjector.LandmarkWorldPositions[8]` (index finger tip) as the aim origin. Direction is calculated as `tip - mcp` (MCP = landmark 5, the index finger base joint).

**Firing:** On `OnClosedFist` event:
1. If safety is on → ignore
2. If cooldown (0.3s) still active → ignore
3. If bullet prefab is assigned → instantiate and launch via `Rigidbody`
4. If no bullet prefab → fall back to `Physics.Raycast` (hitscan)

**Safety toggle:** On `OnThumbDown` event → toggles `_safetyOn` flag. A yellow debug ray indicates safety is on; red indicates firing mode.

### 10.7 How to Set Up the Shooter Scene in Unity

After opening the project in Unity:

1. **Create Target Prefabs:**
   - Create a Cube (1.5×2×1) with red material → add `Target` component → set Type=Bandit → save as `Assets/Prefabs/Bandit.prefab`
   - Create a Cube (1.5×2×1) with green material → add `Target` component → set Type=Innocent → save as `Assets/Prefabs/Innocent.prefab`

2. **Create Bullet Prefab:**
   - Create a Sphere (radius 0.15) with yellow emissive material → add `Rigidbody` (gravity off) → add `TrailRenderer` (time 0.3s) → set Collider as Trigger → save as `Assets/Prefabs/Bullet.prefab`

3. **Create MG_Shooter.unity:**
   - New scene → add Camera (FOV ~60°, clear color dark blue) → Directional Light
   - Create `ShooterGameController` GameObject → add `ShooterGame` component
   - Create `TargetSpawner` GameObject → add `TargetSpawner` component → assign Bandit + Innocent prefabs
   - Create `HandController` GameObject → add `ShooterHandController` component → assign Bullet prefab
   - Save scene as `Assets/Scenes/MG_Shooter.unity`

4. **Update Build Settings:**
   - File → Build Settings → ensure order: Bootstrap(0), MainMenu(1), MG_Shooter(2)

---

## 11. Build Settings

### Current Configuration

| Index | Scene | Required |
|-------|-------|----------|
| 0 | `Assets/Scenes/Bootstrap.unity` | Yes |
| 1 | `Assets/Scenes/MainMenu.unity` | Yes |
| 2 | `Assets/Scenes/MG_Shooter.unity` | Yes (must be created) |

### Adding a New Scene

1. Create the scene in `Assets/Scenes/`
2. **File → Build Settings → Add Open Scenes**
3. Reorder to the correct index
4. Update `MainMenuController` and any script referencing build indices
5. Update this documentation

### Removing a Scene

1. **File → Build Settings** → select scene → Remove Selection
2. Delete the `.unity` file and its `.meta`
3. Re-index remaining scenes
4. Update any script referencing the removed scene's index

---

## 12. Troubleshooting

### "MediaPipe model files not found"

Ensure model files are in `Assets/StreamingAssets/mediapipe/`:
- `hand_landmarker.task`
- `face_landmarker.task`

### "SceneLoader Instance is missing"

You started from a scene other than Bootstrap. Always start play mode from `Bootstrap.unity` (index 0).

### Camera feed not showing

1. Click **"Encender"** button to start the camera
2. Check the dropdown selects the correct camera device
3. Verify the `RawImage` is assigned to `CameraFeedCtrl._outputImage`

### Thumb-down gesture not working

- Ensure good lighting and hand visibility
- The thumb must be visibly pointing downward while the index is extended
- Check the debug overlay shows "ThumbDown" when gesture is detected

### Build errors after cleanup

If scenes were deleted from disk but still appear in Build Settings:
1. Open **File → Build Settings**
2. They will show as "Missing" — select and remove them
3. Re-add only the current scenes (Bootstrap, MainMenu, MG_Shooter)
