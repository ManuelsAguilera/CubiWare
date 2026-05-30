# Simón Dice — Scene Wiring Guide

> **Scene:** `Assets/Scenes/Simon.unity`
> **Build Index:** 4

---

## 1. Scene Creation

1. Create new scene: `Assets/Scenes/Simon.unity`
2. Ensure it's in Build Settings at index 4 (File → Build Settings → Add Open Scenes)

---

## 2. Required Components in Scene

The Bootstrap scene provides singletons via `DontDestroyOnLoad`. Your scene needs these local components:

| Component | Type | How to get it |
|---|---|---|
| GestureDetector | `ARcadeRush.Hand.GestureDetector` | Copy from Shooter scene or create empty GameObject + attach |
| RawImage (camera) | `UnityEngine.UI.RawImage` | Create in a Canvas, `CameraFeedCtrl` pushes `WebCamTexture` at runtime via `SetOutputImage()` |

The following come from Bootstrap automatically (no scene reference needed):
- `LLMConnector.Instance` (via `MiniGameDependencies.LLM`)
- `GameManager.Instance` (via `MiniGameDependencies.GameManager`)
- `MediaPipeController.Instance` (via `MiniGameDependencies.MediaPipe`)
- `CameraFeedCtrl.Instance` (via `MiniGameDependencies.Camera` — manages webcam; no AR camera/Vuforia)

### Camera Feed Setup

This project uses a **RawImage** for the webcam feed — no AR Camera or Vuforia:

1. Create `Canvas_CameraFeed` (Screen Space - Overlay, **Sort Order = 0**)
2. Add child `RawImage` (stretch to full canvas)
3. Attach [`CameraOverlay.cs`](Assets/Scripts/UI/CameraOverlay.cs) to the RawImage (handles aspect ratio)
4. `CameraFeedCtrl.SetOutputImage()` pushes the `WebCamTexture` at runtime

The UI canvas (`Canvas_Simon`, **Sort Order = 1**) renders on top of the camera feed.

---

## 3. Scene Hierarchy

```
Simon.unity
├── Directional Light
│
├── Canvas_CameraFeed (Screen Space - Overlay)   ← Camera background
│   └── RawImage                                  ← Shows webcam via CameraFeedCtrl
│       └── CameraOverlay.cs                      ← Aspect ratio fitting
│
├── [SimonGameController]                    ← Root GameObject
│   ├── SimonGame.cs
│   │
│   ├── [SimonMenuManager]                   ← Child GameObject
│   │   └── SimonMenuManager.cs
│   │
│   ├── [SimonJudge]                         ← Child GameObject
│   │   └── SimonJudge.cs
│   │
│   ├── [SimonCommandGenerator]              ← Child GameObject
│   │   └── SimonCommandGenerator.cs
│   │
│   └── [GestureDetector]                    ← Child or reference to existing prefab
│       └── GestureDetector.cs
│
├── Canvas_Simon (Screen Space - Overlay)    ← Canvas GameObject
│   ├── StartMenuPanel
│   │   ├── TitleText (TMP_Text)
│   │   ├── StartButton (Button)
│   │   └── MainMenuButton (Button)
│   │
│   ├── CountdownPanel
│   │   └── CountdownText (TMP_Text)
│   │
│   ├── GameplayHUD
│   │   ├── DialoguePanel
│   │   │   ├── DialogueBackground (Image)
│   │   │   └── DialogueText (TMP_Text)
│   │   ├── RoundCounter (TMP_Text)
│   │   ├── TimerBar (Image — Filled type)
│   │   └── TimerText (TMP_Text)
│   │
│   ├── FeedbackPanel
│   │   ├── CheckmarkCard (Image — green ✓)
│   │   └── CrossCard (Image — red ✗)
│   │
│   ├── PausePanel
│   │   ├── PauseText (TMP_Text)
│   │   ├── ResumeButton (Button)
│   │   └── MainMenuButton (Button)
│   │
│   ├── VictoryPanel
│   │   ├── VictoryText (TMP_Text)
│   │   ├── StatsText (TMP_Text)
│   │   └── RestartButton (Button)
│   │
│   └── GameOverPanel
│       ├── GameOverText (TMP_Text)
│       ├── ReasonText (TMP_Text)
│       └── RestartButton (Button)
│
└── EventSystem (Unity UI)
```

---

## 4. Inspector Wiring — Step by Step

### 4.1 SimonGameController (Root GameObject)

**Attach:** `SimonGame.cs`

| Field | Target |
|-------|--------|
| `_menuManager` | `[SimonMenuManager]` child GameObject |
| `_hud` | `SimonHUDController` on `Canvas_Simon` |
| `_judge` | `[SimonJudge]` child GameObject |
| `_commandGenerator` | `[SimonCommandGenerator]` child GameObject |
| `_gestureDetector` | `[GestureDetector]` child GameObject |

| Config Field | Value |
|---|---|
| `_maxRounds` | `5` |
| `_mainMenuSceneIndex` | `1` (MainMenu) |
| `_commandDisplayDuration` | `2.5` |
| `_responseTimePerRound` | `5` |
| `_feedbackDuration` | `2` |
| `_roundTransitionDelay` | `1.5` |

### 4.2 SimonMenuManager (Child of SimonGameController)

**Attach:** `SimonMenuManager.cs`

| Field | Target |
|-------|--------|
| `_startMenuPanel` | `Canvas_Simon/StartMenuPanel` |
| `_countdownPanel` | `Canvas_Simon/CountdownPanel` |
| `_gameplayHUDPanel` | `Canvas_Simon/GameplayHUD` |
| `_feedbackPanel` | `Canvas_Simon/FeedbackPanel` |
| `_pausePanel` | `Canvas_Simon/PausePanel` |
| `_victoryPanel` | `Canvas_Simon/VictoryPanel` |
| `_gameOverPanel` | `Canvas_Simon/GameOverPanel` |
| `_checkmarkCard` | `Canvas_Simon/FeedbackPanel/CheckmarkCard` |
| `_crossCard` | `Canvas_Simon/FeedbackPanel/CrossCard` |
| `_startButton` | `Canvas_Simon/StartMenuPanel/StartButton` |
| `_mainMenuButton_Start` | `Canvas_Simon/StartMenuPanel/MainMenuButton` |
| `_resumeButton` | `Canvas_Simon/PausePanel/ResumeButton` |
| `_mainMenuButton_Pause` | `Canvas_Simon/PausePanel/MainMenuButton` |
| `_gameOverReasonText` | `Canvas_Simon/GameOverPanel/ReasonText` |
| `_restartButton_GameOver` | `Canvas_Simon/GameOverPanel/RestartButton` |
| `_victoryStatsText` | `Canvas_Simon/VictoryPanel/StatsText` |
| `_restartButton_Victory` | `Canvas_Simon/VictoryPanel/RestartButton` |
| `_countdownText` | `Canvas_Simon/CountdownPanel/CountdownText` |
| `_hudController` | `SimonHUDController` on `Canvas_Simon` |

### 4.3 SimonHUDController (on Canvas_Simon)

**Attach:** `SimonHUDController.cs`

| Field | Target |
|-------|--------|
| `_dialogueText` | `Canvas_Simon/GameplayHUD/DialoguePanel/DialogueText` |
| `_dialoguePanel` | `Canvas_Simon/GameplayHUD/DialoguePanel` |
| `_roundCounterText` | `Canvas_Simon/GameplayHUD/RoundCounter` |
| `_timerFillBar` | `Canvas_Simon/GameplayHUD/TimerBar` |
| `_timerText` | `Canvas_Simon/GameplayHUD/TimerText` |

| Config Field | Value |
|---|---|
| `_simonDiceColor` | Green (`#00FF00` or similar) |
| `_noSimonDiceColor` | Orange (`#FF9900` or similar) |

### 4.4 SimonJudge (Child of SimonGameController)

**Attach:** `SimonJudge.cs`

| Field | Target |
|-------|--------|
| `_gestureDetector` | Same `[GestureDetector]` as SimonGameController |

### 4.5 SimonCommandGenerator (Child of SimonGameController)

**Attach:** `SimonCommandGenerator.cs`

| Config Field | Value |
|---|---|
| `_minFalseRounds` | `1` |
| `_maxFalseRounds` | `2` |
| `_useLLM` | `true` |
| `_systemPrompt` | (default is fine) |

### 4.6 GestureDetector (Child of SimonGameController)

Either instantiate the existing GestureDetector prefab or copy from the Shooter scene.

| Config Field | Value |
|---|---|
| `_enabledByDefault` | `true` |
| `_enabledGestures` | Add: `OpenHand`, `ClosedFist`, `Point`, `Pinch`, `ThumbDown` |
| `_heuristicsCsvName` | `GestureHeuristics` |

---

## 5. UI Panel Initial States

Before running, set these panels **inactive** (uncheck the GameObject):

- [ ] `CountdownPanel`
- [ ] `GameplayHUD`
- [ ] `FeedbackPanel`
- [ ] `PausePanel`
- [ ] `VictoryPanel`
- [ ] `GameOverPanel`
- [ ] `CheckmarkCard` and `CrossCard`

**Leave active:**
- [x] `StartMenuPanel` (this is the initial state)

`SimonMenuManager.Awake()` will also handle this, but pre-setting avoids a flash.

---

## 6. Timer Bar Setup

Select `Canvas_Simon/GameplayHUD/TimerBar`:
- Image Type: **Filled**
- Fill Method: **Horizontal**
- Fill Origin: **Left**
- Fill Amount: `1` (full)

---

## 7. Quick Sanity Checklist

| Check | Expected |
|-------|----------|
| `SimonGame` implements `IMiniGame` | ✓ (confirmed in code) |
| `Simon.unity` is in Build Settings at index 4 | Must match `MiniGameRegistry._scenePaths` |
| `MiniGameRegistry` has `TryRegister("Simon", "ARcadeRush.Minigames.Simon.SimonGame")` | ✓ (already done) |
| MainMenu has `_startSimonBtn` wired | ✓ (already done) |
| `GestureHeuristics.csv` exists in `Assets/Resources/` | Verify — GestureDetector loads it at runtime |
| No `SimonHeadAnchor` references in any script | ✓ (not implemented) |

---

## 8. First Test

1. Open Bootstrap scene
2. Press Play
3. Navigate to MainMenu
4. Click "Simón Dice"
5. Scene should load → StartMenu appears
6. Click Start → Countdown (3, 2, 1, ¡YA!)
7. First command appears as text in dialogue panel
8. After display phase (~2.5s), timer starts
9. Perform the gesture → feedback → next round

---

*ARcade Rush — Simón Dice Wiring Guide v1 · PUCV 2026*
