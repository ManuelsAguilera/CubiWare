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
├── Main Camera
├── Stage Camera
├── MaskRoot
│   ├── HappyMask
│   ├── SurprisedMask
│   └── AngryMask
├── Canvas
│   ├── CameraDisplay
│   ├── Scenario
│   │   └── CurtainImage
│   ├── Audience
│   │   └── AudienceSprite
│   ├── ApprovalBar
│   │   ├── BarBackground
│   │   ├── FillBar
│   │   └── FeedbackText
│   ├── Countdown
│   │   ├── FillBar
│   │   └── TimeText
│   ├── Script
│   │   ├── CurrentEmotionText
│   │   ├── NextEmotionText
│   │   ├── ProgressText
│   │   └── EmotionIcon
│   ├── ResultsPanel *(create manually)*
│   │   ├── HeadlineText
│   │   ├── EvaluationText
│   │   ├── ScoreText
│   │   └── ElementsPassedText
│   └── CameraElement
├── EventSystem
└── SceneDirectorGame
```

---

### Main Camera
**Components:** Camera (depth=−1, Culling Mask = Everything except StageLayer)

**What it does:** Renders the UI canvas and all non-stage world elements. Excludes the StageLayer so it never sees the 3D mask meshes directly — those are composited through the RenderTexture pipeline instead. This camera is the player's primary view of the theater UI.

---

### Stage Camera
**Components:** Camera (depth=0, Culling Mask = StageLayer only, Target Texture = StageRT)

**What it does:** Dedicated camera that renders only the 3D theatrical mask meshes (HappyMask, SurprisedMask, AngryMask) into a RenderTexture (`StageRT` at 640×480). The result is displayed on the `CameraDisplay` RawImage, which composites the masks visually on top of the webcam feed inside the canvas. Without this camera, the 3D masks would be invisible behind the Screen Space Overlay canvas.

---

### MaskRoot
**Components:** Transform, MaskController

**What it does:** The root transform for all 3D theatrical mask meshes. `MaskController` updates this object's world position every frame by converting the player's nose-tip position (read from `FaceLandmarkReader.FaceCenterNormalized`) through the Stage Camera's viewport-to-world transform. Its local scale is driven by `FaceLandmarkReader.FaceScale` so the mask grows and shrinks as the player moves closer or further from the camera. Only one child mask is active at a time.

---

### HappyMask / SurprisedMask / AngryMask *(children of MaskRoot)*
**Components:** MeshFilter, MeshRenderer (Layer = StageLayer)

**What it does:** Each is a 3D theatrical mask mesh for one emotion state. `MaskController.SetEmotion(EmotionLabel)` activates the matching child and deactivates the others. Only the active mesh is rendered by the Stage Camera into the RenderTexture. In testing mode, H/S/A/N keys trigger the swap; in live mode, `EmotionClassifier.OnEmotionChanged` drives it.

---

### Canvas
**Components:** Canvas (Screen Space — Overlay), Canvas Scaler (1920×1080), Graphic Raycaster

**What it does:** The root of all 2D UI in the Director scene. Renders in Screen Space Overlay so it always draws on top of everything. Child draw order (top to bottom in hierarchy = back to front on screen) determines what appears above what — `CameraDisplay` must be the first child so it renders behind all other UI panels.

---

### CameraDisplay *(first child of Canvas)*
**Components:** RectTransform (stretch full screen), RawImage (Texture = StageRT)

**What it does:** Full-screen background that displays the Stage Camera's RenderTexture output — the webcam feed with the 3D mask composited on top. Must be the first child of Canvas so it renders behind all other UI elements. `CameraController.BindFeed()` does NOT write to this object's texture directly; the Stage Camera handles that through StageRT.

---

### Scenario *(child of Canvas)*
**Components:** RectTransform (stretch full screen), Animator, Image (curtain sprite), ScenarioController

**What it does:** The theater curtain overlay. At the start of every round, `SceneDirectorGame.OnStart()` calls `ScenarioController.Open()`, which triggers the Animator to play the curtain-open animation. When the animation finishes, the Animation Event `OnOpenComplete_AnimEvent()` fires, which notifies `SceneDirectorGame` that the stage is ready and the sequence can begin. At round end, `ScenarioController.Close()` triggers the curtain-close animation; its completion event fires `OnEnd()` to return to the main menu.

---

### CurtainImage *(child of Scenario)*
**Components:** RectTransform (stretch full screen), Image

**What it does:** The actual curtain sprite. Animated by the Scenario Animator. Can use a sprite sheet or a single image manipulated by animation curves (slide, scale, fade). This is the visual the player sees opening and closing around the performance.

---

### Audience *(child of Canvas)*
**Components:** RectTransform (bottom strip), Animator, AudienceController

**What it does:** The virtual crowd that reacts to the player's performance in real time. Driven by three Animator states: **Idle** (audience waits), **SlightMove** (audience gets excited while the player holds the correct emotion — triggered from `SceneDirectorGame.Update()` via `ApprovalBarController.IsCorrect`), and **React** (big movement — applause if `IsPositive=true`, tomatoes if `IsPositive=false`). React fires on element pass/fail and auto-returns to Idle via the `OnReactComplete_AnimEvent` Animation Event.

---

### AudienceSprite *(child of Audience)*
**Components:** RectTransform, Image

**What it does:** The audience sprite or sprite sheet driven by the Audience Animator. The `IsPositive` Animator bool controls which reaction animation clip plays during the React state — allowing the same trigger to produce either applause or tomato-throwing depending on whether the player passed or failed the element.

---

### ApprovalBar *(child of Canvas)*
**Components:** RectTransform, ApprovalBarController

**What it does:** The core per-emotion scoring mechanic. When a new script element begins, `SceneDirectorGame` calls `ApprovalBarController.Activate(requiredEmotion)` which resets the fill to 0 and starts the fill/drain loop. Every frame: if the detected emotion matches the required emotion, the bar fills at `_fillRate`/sec; otherwise it drains at `_drainRate`/sec. When the fill reaches 1.0, `OnBarFilled` fires and the element is passed. When it reaches 0.0, `OnBarEmptied` fires and the element fails. Both events are wired in `SceneDirectorGame` to stop the countdown, deactivate the bar, and drive the audience reaction.

---

### BarBackground *(child of ApprovalBar)*
**Components:** RectTransform (stretch), Image (dark gray)

**What it does:** Static dark background behind the fill bar. Provides visual contrast so the fill bar's color changes are readable against any background.

---

### FillBar *(child of ApprovalBar)*
**Components:** RectTransform (stretch), Image (Type=Filled, Fill Method=Horizontal, Fill Origin=Left)

**What it does:** The fill bar image whose `fillAmount` [0..1] is driven each frame by `ApprovalBarController`. Color interpolates between `_colorDraining` (red) and `_colorFilling` (green) proportionally to the current fill level. The player reads this bar to know how well they are performing on the current emotion element.

---

### FeedbackText *(child of ApprovalBar)*
**Components:** RectTransform, TextMeshProUGUI

**What it does:** Overlay text that shows `"CORRECT"` (green) while the player is holding the required emotion, or `"SHOW: {required}"` (white/red) when they are not. Color shifts to red when the fill is below `_warningThreshold`. Gives the player a text confirmation of what emotion they need to perform.

---

### Countdown *(child of Canvas)*
**Components:** RectTransform, CountdownController

**What it does:** The per-emotion timer. When a new element starts, `SceneDirectorGame` calls `CountdownController.StartCountdown(element.TimeLimit)`. The timer counts down in real time and refreshes both `TimeText` and its own `FillBar` every frame. When it reaches zero, `OnCountdownExpired` fires, which causes `SceneDirectorGame` to deactivate the approval bar and call `ScriptController.FailCurrentElement()`. The countdown is stopped (without firing) by `CountdownController.Stop()` when the player succeeds.

---

### FillBar *(child of Countdown)*
**Components:** RectTransform, Image (Type=Filled, Horizontal)

**What it does:** Visual drain bar that shows remaining time. Starts full (fillAmount=1) and drains toward 0 as the countdown runs. Color transitions from green to red as it falls below `_warningThreshold` (default 35% remaining).

---

### TimeText *(child of Countdown)*
**Components:** RectTransform, TextMeshProUGUI

**What it does:** Displays the ceiling of `_remaining` as an integer (e.g. `"5"`, `"4"`, `"3"`). Color matches the bar — switches to `_colorWarning` (red) below the warning threshold. Large font, center-aligned, intended to be the player's primary time awareness cue.

---

### Script *(child of Canvas)*
**Components:** RectTransform, ScriptController

**What it does:** The "guión" panel — shows the player what emotion they must perform. `ScriptController` manages the entire sequence state machine: it loads the 3–6 element sequence (hardcoded in testing mode, LLM-generated in production), fires `OnElementStarted` when a new element is active (which starts the countdown and activates the approval bar), and fires `OnSequenceComplete` when all elements are passed. `RefreshUI()` updates all four child labels every time the active element changes.

---

### CurrentEmotionText *(child of Script)*
**Components:** RectTransform, TextMeshProUGUI (large, bold, center)

**What it does:** Shows the required emotion in large uppercase text (e.g. `"HAPPY"`, `"ANGRY"`). This is the primary instruction the player reads to know what to perform. Also temporarily shows `"Get Ready!"` during the inter-element delay (code-side TODO).

---

### NextEmotionText *(child of Script)*
**Components:** RectTransform, TextMeshProUGUI (small, gray, center)

**What it does:** Shows a preview of the upcoming emotion (e.g. `"Next: SURPRISED"`). Gives the player time to mentally prepare for the next element while the current one is still active. Shows `"Next: —"` on the final element.

---

### ProgressText *(child of Script)*
**Components:** RectTransform, TextMeshProUGUI (small, right-aligned)

**What it does:** Shows how far through the sequence the player is (e.g. `"2 / 4"`). Clamps at total count when the sequence is complete. Helps the player understand how many elements remain.

---

### EmotionIcon *(child of Script)*
**Components:** RectTransform (~80×80px), Image

**What it does:** A visual icon that swaps to the sprite corresponding to the current required emotion. `ScriptController._emotionSprites[]` is indexed by `(int)EmotionLabel`: [0]=Neutral, [1]=Happy, [2]=Surprised, [3]=Angry. Provides an immediate visual cue alongside the text label. Sprites must be assigned in the Inspector.

---

### ResultsPanel *(child of Canvas — create manually)*
**Components:** RectTransform (center, large), Canvas Group (alpha=0, interactable=false by default)

**What it does:** The post-round feedback screen. Activated by `SceneDirectorGame.EndRound()` — the Canvas Group alpha fades from 0 to 1 over 0.3 seconds at the start of the round-end delay. Shows the player their performance summary before the curtain closes. Remains visible while the curtain closes over it. Must be created manually in Director.unity; the code references it via a serialized field.

---

### HeadlineText *(child of ResultsPanel)*
**Components:** RectTransform, TextMeshProUGUI (large, bold, center)

**What it does:** Displays `"BRAVO!"` on win or `"BOOED OFF STAGE!"` on loss. The player's first read on how they did.

---

### EvaluationText *(child of ResultsPanel)*
**Components:** RectTransform, TextMeshProUGUI (medium, center)

**What it does:** Shows the LLM-generated (or hardcoded) performance evaluation string (e.g. `"Bravo! The crowd goes wild! A magnificent performance!"`). Provides theatrical narrative feedback about the performance.

---

### ScoreText *(child of ResultsPanel)*
**Components:** RectTransform, TextMeshProUGUI (large, center)

**What it does:** Displays the final score (e.g. `"Score: 300"`). Each passed element contributes 100 points (configurable via `_pointsPerElement`).

---

### ElementsPassedText *(child of ResultsPanel)*
**Components:** RectTransform, TextMeshProUGUI (small, center)

**What it does:** Shows the ratio of elements passed (e.g. `"3 / 3"` on a full win, `"1 / 3"` on early failure). Helps the player understand exactly where they succeeded or struggled.

---

### CameraElement *(child of Canvas)*
**Components:** RectTransform, CameraController

**What it does:** The camera UI element singleton. In `OnStart()`, `SceneDirectorGame` calls `CameraController.BindFeed(deps.Camera)`, which routes the live `WebCamTexture` to the `CameraDisplay` RawImage. In testing mode (`_testingMode = true`), `CameraController.Update()` listens for H/S/A/N keypresses and calls `OnEmotionDetected(emotion)`, which simultaneously swaps the AR mask via `MaskController.SetEmotion()` and updates the approval bar via `ApprovalBarController.SetDetectedEmotion()`. In live mode, this method is called by the `EmotionClassifier.OnEmotionChanged` subscription in `SceneDirectorGame.WireEvents()`.

---

### EventSystem
**Components:** EventSystem, Standalone Input Module

**What it does:** Unity's standard UI input system. Added automatically when the Canvas is created. Handles all button clicks and UI interactions (e.g. any future in-game buttons). Leave at defaults.

---

### SceneDirectorGame
**Components:** SceneDirectorGame (script only — no visual components)

**What it does:** The central orchestrator of the entire minigame. Implements `IMiniGame` so the `GameManager` can inject dependencies (`Camera`, `MediaPipe`, `LLM`) via `OnStart(MiniGameDependencies deps)`. Subscribes to all events from the 5 element singletons in `WireEvents()` and routes them through the `ResolveElement(bool passed)` method, which is the single point where element pass/fail logic is resolved. Manages the overall game state from curtain-open through sequence completion to curtain-close and menu return. Drives `AudienceController.SlightMove()` from `Update()` based on live approval bar state.

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

---

## 9. TODO List

Items are separated by who performs them. **BLOCKING** items must be resolved before any integration testing can occur.

---

### 9.1 You must do manually in the Unity Editor

- [ ] **Register `Director.unity` in Build Settings** ⛔ BLOCKING
  File → Build Settings → Add Open Scenes while Director.unity is open. Note the assigned index and communicate it so `SceneDirectorGame.SceneIndex` can be updated to match. Current value (5) collides with EmotionTest.unity.

- [ ] **Create `StageRT` RenderTexture asset**
  Right-click `Assets/RenderTextures/` → Create → Render Texture. Name it `StageRT`, set size to **640×480**. Assign to: (1) Stage Camera → Target Texture, (2) CameraDisplay RawImage → Texture. This is what makes 3D masks composite over the webcam feed inside the UI canvas.

- [ ] **Create `ResultsPanel` canvas group in Director.unity** ⛔ BLOCKING
  Add a Canvas Group child to the main Canvas. Set alpha=0, interactable=false by default. Add children: win/lose headline (TMP), evaluation text (TMP), score label (TMP), elements-passed label (TMP, e.g. `"3/3"`). This panel is activated by code at the start of `EndRound()`.

- [ ] **Add score display label to the Script panel** ⛔ BLOCKING
  Add a `TextMeshProUGUI` as a fourth child of the `Script` canvas panel (alongside CurrentEmotionText, NextEmotionText, ProgressText). This label will be wired in code to show the live score.

- [ ] **Add audience speech bubble label**
  Add a `TextMeshProUGUI` above the `AudienceSprite` in the hierarchy. This will be driven by `AudienceController.ShowDialogue()` for LLM audience lines.

- [ ] **Add `StageLayer` layer**
  Project Settings → Tags and Layers → add `StageLayer`. Assign it to `MaskRoot` and all three mask child GameObjects. Set Stage Camera Culling Mask to StageLayer only; set Main Camera to exclude StageLayer.

- [ ] **Assign 3D theatrical mask models** *(when assets are ready)*
  Drag Happy, Surprised, Angry mesh prefabs into `MaskController._maskObjects[1]`, `[2]`, `[3]` in the Inspector.

- [ ] **Disable testing mode when EmotionClassifier is wired**
  Set `CameraController._testingMode = false` in the Inspector once the live EmotionClassifier subscription is uncommented in code.

---

### 9.2 Claude handles in code

#### BLOCKING

- [ ] **Update `SceneDirectorGame.SceneIndex`**
  Once Director.unity is registered in Build Settings and you know its index, update line 40 of `SceneDirectorGame.cs`.
  _File:_ `SceneDirectorGame.cs`

- [ ] **Implement score display logic**
  Subscribe to `GameManager.OnScoreChanged` (or hook into `ResolveElement`) and update the score label. Flash it green for 0.3s on increase via a short coroutine.
  _File:_ `SceneDirectorGame.cs`, `ScriptController.cs` or a new thin controller

- [ ] **Implement `ResultsPanel` activation in `EndRound()`**
  Populate the panel with win/lose headline, evaluation string, final score, and elements passed count. Fade it in (CanvasGroup.alpha over 0.3s) at the start of `EndRound()` before the `_endRoundDelay` wait.
  _File:_ `SceneDirectorGame.cs`

#### HIGH

- [ ] **Null-guard `EndRound` + `_roundEnding` flag in `OnDestroy`**
  Add null check before `ScenarioController.Instance.Close()`. Add `private bool _roundEnding` set to true when `EndRound` starts; in `OnDestroy`, if true call `OnEnd()` directly to prevent `GameManager` from getting stuck in `GameState.Playing`.
  _File:_ `SceneDirectorGame.cs`

- [ ] **Stage Camera RenderTexture validation warning**
  In `MaskController.Awake()`, if `_stageCamera != null && _stageCamera.targetTexture == null` emit a `ServiceLogger.LogWarning`. Prevents silent failure where mask floats over black.
  _File:_ `MaskController.cs`

- [ ] **"Get Ready" cue between elements**
  In `ActivateElementWithDelay()`, show `"Get Ready!"` on `_currentEmotionText` for the first 0.8s of the delay, then reveal the actual required emotion for the final 0.7s. Uses existing wired UI — no new GameObjects needed.
  _File:_ `SceneDirectorGame.cs`

#### MEDIUM

- [ ] **Uncomment `EmotionClassifier` wiring**
  Uncomment the subscription block in `SceneDirectorGame.WireEvents()` (lines 113–116). Prerequisite: Linux camera issues on EmotionClassifier resolved.
  _File:_ `SceneDirectorGame.cs`

- [ ] **Add `CurrentTopEmotion` to `EmotionClassifier` + wire continuous bar polling**
  Add `public EmotionLabel CurrentTopEmotion { get; private set; }` returning the highest-confidence emotion each frame (not gated by confirmation hold). In `SceneDirectorGame.Update()`, call `ApprovalBarController.SetDetectedEmotion(classifier.CurrentTopEmotion)` each frame in live mode. Mask swap continues to use `OnEmotionChanged` (confirmed) for stability.
  _Files:_ `EmotionClassifier.cs`, `SceneDirectorGame.cs`

- [ ] **Add `_initialFillAmount` to `ApprovalBarController`**
  Add `[Range(0f, 0.5f)] [SerializeField] private float _initialFillAmount = 0f;` and use it in `Activate()` instead of hard-coding 0f. Setting it to 0.2 in the Inspector gives a player-friendly head start.
  _File:_ `ApprovalBarController.cs`

- [ ] **Add difficulty curve fields to `ScriptController`**
  Add `[SerializeField] private int _sequenceLength = 3;` (Range 3–6). Decrease `TimeLimit` per element in the hardcoded sequence (e.g. 5.0 → 4.5 → 4.0s). Both are Inspector-configurable.
  _File:_ `ScriptController.cs`

- [ ] **Convert `StartSequence()` to coroutine/callback pattern**
  Refactor to accept an optional `Action<List<ScriptElement>>` callback (or become a coroutine) so async LLM generation can slot in without blocking. Keep synchronous `LoadSequence()` as the immediate fallback.
  _Files:_ `ScriptController.cs`, `SceneDirectorGame.cs`

#### LOW

- [ ] **Wire gesture bonuses**
  Subscribe to `GestureDetector` events in `WireEvents()` (with null guard via `FindFirstObjectByType`). Mapping: ThumbDown = instant bar fill + 50 bonus pts; OpenHand during Surprised = ×1.5 score; ClosedFist during Angry = ×1.5; Pinch during Happy = ×1.5. Gestures are additive, never required.
  _Files:_ `SceneDirectorGame.cs`, `ApprovalBarController.cs`

- [ ] **Add `ShowDialogue(string, float)` to `AudienceController`**
  Displays the speech bubble label for the given duration then hides it. Called from `SceneDirectorGame` on element result; LLM audience lines target this method once wired.
  _File:_ `AudienceController.cs`

- [ ] **Enable LLM sequence generation**
  Use `_deps.LLM.Ask()` (LLMConnector — real HTTP). Do NOT use `ILLMService` which routes to the GroqLLMService mock. Prompt: `"Output only valid JSON. Format: [{\"e\":1,\"t\":5}] where e=EmotionLabel int, t=seconds. No other text."` Parse in a new `ScriptLLMParser.cs`. Pre-generate 2–3 audience lines in the same call to avoid mid-round latency.
  _Files:_ `ScriptController.cs`, new `ScriptLLMParser.cs`, `SceneDirectorGame.cs`

- [ ] **Add `RequiredGesture` to `ScriptElement`**
  Add `public GestureType RequiredGesture;` (default `None` = not required). Update `ApprovalBarController.IsCorrect` to check gesture when `_requiredGesture != None`.
  _Files:_ `ScriptController.cs`, `ApprovalBarController.cs`

- [ ] **Add mask swap scale-bounce coroutine to `MaskController`**
  `SwapMask(int newIndex)`: scale new mesh 0.1→1 and old mesh 1→0 simultaneously over 0.15s. Implement only after real 3D mask models exist — effect is invisible on placeholder geometry.
  _File:_ `MaskController.cs`

- [ ] **Document scene-scoped singleton constraint in Section 3**
  Add a note: scene-scoped singletons must NOT receive `DontDestroyOnLoad`. Director.unity is always a full scene swap, never additive. Adding DontDestroyOnLoad would break the Instance lifecycle on scene reload.
  _File:_ `docs/SceneDirector.md`
