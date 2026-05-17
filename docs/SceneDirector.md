# Director de Escena — Technical Documentation

## 1. Game Concept

The player stands in front of the camera on a virtual theater stage. They must act out a sequence of emotions indicated by a "script" (guión). A 3D theatrical AR mask overlays their face, swapping between meshes based on the detected emotion. A reactive virtual audience applauds correct performances and throws tomatoes at wrong ones.

**Modules used:** Emotion Detection, Gesture Detection, LLM
**Scene file:** `Assets/Scenes/Director.unity`
**Scripts folder:** `Assets/Scripts/Minigames/SceneDirector/`

---

## 2. Core Game Loop

```
Curtain opens → LLM generates script (3–6 emotions)
     │
     ▼
Show required emotion to player
     │
     ▼
Countdown starts (per emotion)
     │
     ├── Player shows CORRECT emotion → Approval bar fills → element PASSED
     │        → audience applauds → next element
     │
     └── Player shows WRONG emotion  → Approval bar drains → bar hits 0 = FAIL
              OR countdown hits 0                          → audience throws tomatoes
                                                           → round ends
     │
     ▼
All elements passed → curtain closes → LLM evaluates performance → back to menu
```

**Win condition:** Complete every element in the sequence before running out of time.
**Lose condition:** Approval bar empties (wrong emotion held too long) OR countdown expires.

---

## 3. Architecture — The 5 UI Element Singletons

Each UI element is a singleton GameObject with one primary script and multiple child UI components. All singletons are wired together by `SceneDirectorGame`, which is the `IMiniGame` implementation for this scene.

### 3.1 Scenario — `ScenarioController.cs`

The theater curtain. Acts as a full-screen camera filter that opens at the start and closes at the end of every round.

**Public API:**

| Method / Event              | Purpose                                                             |
| --------------------------- | ------------------------------------------------------------------- |
| `Open()`                  | Triggers the curtain-open animation                                 |
| `Close()`                 | Triggers the curtain-close animation                                |
| `OnOpenComplete` (event)  | Fires when the curtain is fully open — game starts here            |
| `OnCloseComplete` (event) | Fires when the curtain is fully closed —`OnEnd()` is called here |

**Animator required parameters:** Triggers `Open`, `Close`. Animation events `OnOpenComplete_AnimEvent()` and `OnCloseComplete_AnimEvent()` must be placed on the last frame of each clip.

---

### 3.2 Audience — `AudienceController.cs`

A 2D virtual audience sprite with three animation states that react to the player's performance.

**Animation states:**

| State          | When triggered                                      | Visual                               |
| -------------- | --------------------------------------------------- | ------------------------------------ |
| `Idle`       | Round not started, between elements                 | Crowd standing still                 |
| `SlightMove` | Player is showing the correct emotion (bar filling) | Small excited movement               |
| `React`      | Element passed (positive) or failed (negative)      | Big movement — applause or tomatoes |

**Animator required parameters:** `SlightMove` (Bool), `React` (Trigger), `IsPositive` (Bool). Animation event `OnReactComplete_AnimEvent()` on the last frame of the React clip to auto-return to Idle.

**`SlightMove` is driven by `SceneDirectorGame.Update()`** based on `ApprovalBarController.IsCorrect` — the audience subtly reacts in real time as the player holds the correct emotion.

---

### 3.3 Countdown — `CountdownController.cs`

A per-emotion timer. Each new script element starts a fresh countdown. If it reaches zero before the approval bar fills, the element fails.

**Public API:**

| Method / Event                     | Purpose                                                        |
| ---------------------------------- | -------------------------------------------------------------- |
| `StartCountdown(float duration)` | Starts a fresh timer                                           |
| `Stop()`                         | Cancels without firing expired event — call on player success |
| `Pause()` / `Resume()`         | Pause/unpause without resetting                                |
| `OnCountdownExpired` (event)     | Fires when time hits zero — triggers element fail             |
| `OnTick(float normalized)`       | Fires every frame with value [1=full, 0=expired]               |

UI displays: seconds as integer text + a fill bar that drains. Color shifts from green → red below `_warningThreshold` (default 35%).

---

### 3.4 Script (Guión) — `ScriptController.cs`

Holds and drives the emotion sequence the player must perform. Advances on success, notifies on failure.

**Data structure:**

```csharp
public struct ScriptElement
{
    public EmotionLabel RequiredEmotion;  // Happy, Surprised, Angry
    public float TimeLimit;               // seconds for this element
}
```

**Public API:**

| Method / Event                 | Purpose                                                         |
| ------------------------------ | --------------------------------------------------------------- |
| `StartSequence()`            | Loads the hardcoded sequence and starts element 1               |
| `PassCurrentElement()`       | Called when approval bar fills — advances to next              |
| `FailCurrentElement()`       | Called on timeout or bar empty — fires `OnElementFailed`     |
| `OnElementStarted` (event)   | New element became active — wire countdown + approval bar here |
| `OnElementPassed` (event)    | Element was passed                                              |
| `OnElementFailed` (event)    | Element was failed                                              |
| `OnSequenceComplete` (event) | All elements passed — win condition                            |

**Testing sequence** is set directly in Inspector under `_hardcodedSequence` (default: Happy 5s → Surprised 5s → Angry 5s).
**LLM hook** is inside `StartSequence()` marked with `// TODO`.

---

### 3.5 Camera + AR Mask — `CameraController.cs` + `MaskController.cs`

**`CameraController`** routes the live webcam feed to the `CameraDisplay` RawImage and acts as the single entry point for detected emotion data. It drives both the mask and the approval bar.

**`MaskController`** positions a 3D theatrical mesh over the player's face every frame using `FaceLandmarkReader.FaceCenterNormalized` and `FaceLandmarkReader.FaceScale`, then swaps between three static meshes based on the detected emotion.

**Mask positioning logic:**

1. Read nose-tip position from `FaceLandmarkReader.FaceCenterNormalized` (normalized [0,1])
2. Flip Y axis (MediaPipe Y=0 is top; Unity viewport Y=0 is bottom)
3. Call `Stage Camera.ViewportToWorldPoint()` to convert to world space at `_maskDepth`
4. Scale `MaskRoot` by `FaceScale × _scaleMultiplier`

**Mesh indexing:** `_maskObjects[0]`=Neutral (null/hidden), `[1]`=Happy, `[2]`=Surprised, `[3]`=Angry — indexed by `(int)EmotionLabel`.

**Testing mode (`_testingMode = true`):** Keys **H / S / A / N** simulate emotion detection. One keypress calls `OnEmotionDetected(emotion)` which drives both mask swap and approval bar simultaneously.

**`FaceLandmarkReader` additions (made for this feature):**

- `FaceCenterNormalized` (Vector2) — nose-tip position in image space
- `FaceScale` (float) — face width as fraction of image width

---

### 3.6 Approval Bar — `ApprovalBarController.cs`

The core scoring mechanic. Fills when the correct emotion is detected; drains when wrong or neutral.

**Fill logic:**

| Condition                            | Effect                                            |
| ------------------------------------ | ------------------------------------------------- |
| Detected emotion == Required emotion | Bar fills at `_fillRate` / sec (default 0.35)   |
| Detected emotion != Required emotion | Bar drains at `_drainRate` / sec (default 0.20) |

**Events:**

| Event            | Fires when       | Wire to                                                                   |
| ---------------- | ---------------- | ------------------------------------------------------------------------- |
| `OnBarFilled`  | Fill reaches 1.0 | `ScriptController.PassCurrentElement()`, `CountdownController.Stop()` |
| `OnBarEmptied` | Fill reaches 0.0 | `ScriptController.FailCurrentElement()`, `CountdownController.Stop()` |

`SetDetectedEmotion(EmotionLabel)` must be called every time the detected emotion changes. In testing mode, `CameraController` calls this when H/S/A/N keys are pressed.

---

## 4. Main Wiring — `SceneDirectorGame.cs`

Implements `IMiniGame`. Subscribes to all events in `OnStart()` and unsubscribes in `OnEnd()` / `OnDestroy()`.

**Event map:**

```
ScenarioController.OnOpenComplete     → AudienceController.SetIdle()
                                        ScriptController.StartSequence()

ScriptController.OnElementStarted    → [1.5s delay]
                                        CountdownController.StartCountdown(element.TimeLimit)
                                        ApprovalBarController.Activate(element.RequiredEmotion)

ApprovalBarController.OnBarFilled    → ResolveElement(passed: true)
ApprovalBarController.OnBarEmptied   → ResolveElement(passed: false)
CountdownController.OnCountdownExpired → ApprovalBarController.Deactivate()
                                          ResolveElement(passed: false)

ResolveElement(true)  → AudienceController.ReactPositive()
                         GameManager.AddScore(100)
                         ScriptController.PassCurrentElement()
                           → more elements: OnElementStarted fires
                           → done:          OnSequenceComplete fires

ResolveElement(false) → AudienceController.ReactNegative()
                         ScriptController.FailCurrentElement()
                         EndRound(won: false)

ScriptController.OnSequenceComplete  → EndRound(won: true)

EndRound                             → [2s delay] → ScenarioController.Close()
ScenarioController.OnCloseComplete   → OnEnd() → GameManager.EndGame() → Load MainMenu
```

**Double-fire guard:** `_elementResolved` bool prevents `OnBarFilled`, `OnBarEmptied`, and `OnCountdownExpired` from all firing `ResolveElement` in the same frame. Resets at the start of each new element.

**`Update()` drives `AudienceController.SlightMove()`** continuously while `ApprovalBarController.IsCorrect` is true, without requiring any additional events.

---

## 5. Unity Scene Hierarchy — `Director.unity`

```
Director.unity
├── Main Camera                   [Camera] depth=-1, excludes StageLayer
├── Stage Camera                  [Camera] depth=0, renders StageLayer → StageRT (RenderTexture)
├── MaskRoot                      [MaskController] Layer=StageLayer
│   ├── HappyMask                 [MeshFilter, MeshRenderer] Layer=StageLayer
│   ├── SurprisedMask             [MeshFilter, MeshRenderer] Layer=StageLayer
│   └── AngryMask                 [MeshFilter, MeshRenderer] Layer=StageLayer
├── Canvas                        [Canvas] Screen Space — Overlay
│   ├── CameraDisplay             [RawImage] Texture=StageRT, first child (renders behind all UI)
│   ├── Scenario                  [ScenarioController, Animator, Image]
│   │   └── CurtainImage          [Image]
│   ├── Audience                  [AudienceController, Animator]
│   │   └── AudienceSprite        [Image]
│   ├── ApprovalBar               [ApprovalBarController]
│   │   ├── BarBackground         [Image]
│   │   ├── FillBar               [Image, Type=Filled, Horizontal]
│   │   └── FeedbackText          [TextMeshProUGUI]
│   ├── Countdown                 [CountdownController]
│   │   ├── FillBar               [Image, Type=Filled, Horizontal]
│   │   └── TimeText              [TextMeshProUGUI]
│   ├── Script                    [ScriptController]
│   │   ├── CurrentEmotionText    [TextMeshProUGUI]
│   │   ├── NextEmotionText       [TextMeshProUGUI]
│   │   ├── ProgressText          [TextMeshProUGUI]
│   │   └── EmotionIcon           [Image]
│   └── CameraElement             [CameraController]
├── EventSystem
└── SceneDirectorGame             [SceneDirectorGame]
```

**RenderTexture:** Create `Assets/RenderTextures/StageRT` at 640×480. Assign to `Stage Camera → Target Texture` and to `CameraDisplay → Raw Image → Texture`. This is what makes the 3D mask visible on top of the webcam feed inside the UI canvas.

---

## 6. Testing Mode

All three live modules are currently disabled and replaced with hardcoded stubs:

| Module            | Testing substitute                                                                                                                                 |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| EmotionClassifier | Keys**H / S / A / N** in `CameraController` simulate detected emotion → drives mask + approval bar                                        |
| LLM               | Hardcoded emotion sequence in `ScriptController._hardcodedSequence` (Inspector). Hardcoded evaluation string in `SceneDirectorGame.EndRound()` |
| GestureDetector   | Not yet defined — gestures TBD                                                                                                                    |

All live module integration points are marked `// TODO: Replace with live module` in the code.

**To test a full loop:**

1. Play the Director scene
2. Press **H**, **S**, or **A** to simulate the required emotion when the Script shows it
3. Hold the key until the approval bar fills (element passed)
4. Repeat for each element in the sequence

---

## 7. Files Modified / Created

### New scripts

| File                                                         | Purpose                              |
| ------------------------------------------------------------ | ------------------------------------ |
| `Scripts/Minigames/SceneDirector/SceneDirectorGame.cs`     | IMiniGame entry point, event wiring  |
| `Scripts/Minigames/SceneDirector/ScenarioController.cs`    | Curtain open/close singleton         |
| `Scripts/Minigames/SceneDirector/AudienceController.cs`    | Audience 3-state animation singleton |
| `Scripts/Minigames/SceneDirector/CountdownController.cs`   | Per-emotion timer singleton          |
| `Scripts/Minigames/SceneDirector/ScriptController.cs`      | Emotion sequence manager singleton   |
| `Scripts/Minigames/SceneDirector/CameraController.cs`      | Webcam display + emotion entry point |
| `Scripts/Minigames/SceneDirector/MaskController.cs`        | 3D AR mask positioning + mesh swap   |
| `Scripts/Minigames/SceneDirector/ApprovalBarController.cs` | Fill/drain bar + win/fail events     |

### Modified scripts

| File                                   | What changed                                                                                                                                                                         |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Scripts/Face/FaceLandmarkReader.cs` | Added `FaceCenterNormalized` (Vector2) and `FaceScale` (float) properties extracted from nose-tip and cheekbone landmarks — used by MaskController for face-tracked positioning |

---

## 8. Future Changes

### 8.1 Wire EmotionClassifier (priority)

Replace the H/S/A/N keyboard stub with the real `EmotionClassifier`. The hook is already in `SceneDirectorGame.WireEvents()`:

```csharp
// TODO: Replace with live module
// classifier.OnEmotionChanged += emotion => CameraController.Instance.OnEmotionDetected(emotion);
```

Prerequisite: resolve the current EmotionClassifier camera issues on Linux.

### 8.2 Wire LLM — three integration points

| Where                                | What the LLM should do                                            |
| ------------------------------------ | ----------------------------------------------------------------- |
| `ScriptController.StartSequence()` | Generate the 3–6 emotion sequence dynamically for each round     |
| `SceneDirectorGame.EndRound()`     | Evaluate the player's performance and deliver theatrical feedback |
| `AudienceController` (new method)  | Write reactive audience dialogue lines mid-round                  |

Prompt design constraint: Groq model is `llama-3-8b-8192` with max 120 tokens per call — keep prompts structured and short.

### 8.3 Define and wire GestureDetector

Gestures are intended to complement emotion acting (e.g. thumbs up with a happy expression). Steps needed:

1. Decide which `GestureType` values map to which script elements
2. Add gesture requirements to `ScriptElement` struct (optional field)
3. Subscribe to `GestureDetector` events in `SceneDirectorGame.WireEvents()`
4. Update `ScriptController` display to show required gesture alongside required emotion

### 8.4 Add real 3D mask models

Three theatrical mesh models are needed: Happy, Angry, Surprised. When available:

1. Import as `.fbx` or `.blend` into `Assets/Models/SceneDirector/`
2. Assign to `MaskController._maskObjects[1]`, `[2]`, `[3]`
3. Tune `MaskController._maskDepth` and `_scaleMultiplier` until alignment is correct
4. Optionally add head rotation to `MaskController.UpdatePosition()` using `facialTransformationMatrixes` from MediaPipe (hook already marked with `// TODO`)

### 8.5 Upgrade AR rendering pipeline

Currently, `Stage Camera → RenderTexture → CameraDisplay` gives a correct layering of webcam + 3D mask on top of UI canvas. When mask models are added, verify the visual result and tune:

- `Stage Camera` field of view to match the webcam aspect ratio
- `MaskRoot` Z position relative to the background quad
- Lighting setup for the Stage layer so masks look theatrical

### 8.6 Audience sprite and animation

The current audience is a placeholder single sprite. Future version needs:

- A sprite sheet with at least 3 distinct animation frames per state (Idle, SlightMove, React)
- Positive vs negative React states need visually distinct animations (applause vs tomatoes)
- `AudienceController` already reads `IsPositive` bool from the Animator — just wire the clips

### 8.7 Scoring and progression

- Current score: flat `_pointsPerElement` (100) per passed element. Could be multiplied by how quickly the bar filled.
- Add a retry system (limited lives per round) instead of immediate round-end on first fail.
- Persist high score via `GameManager.DataStore` (already available through `MiniGameDependencies`).

### 8.8 Sound design

No audio exists yet. Suggested additions:

- Curtain whoosh on open/close
- Audience applause / booing / tomato splat SFX
- Countdown tick at low time (< 35%)
- Emotion confirmation sound (bar filled)
- Background theater ambience
