# Scene Director — Assets & Wiring Guide

**Last updated:** 2026-05-29  
**Status:** All code written — this guide covers Unity Editor setup only.

---

## 0. Quick Checklist (TL;DR)

- [x] Register `Director.unity` in Build Settings
- [x] Create `StageLayer` in Tags & Layers
- [x] Create `StageRT` RenderTexture asset (640×480)
- [x] Create 11 new UI panels in Canvas (see Section 4)
- [x] Assign all serialized field references on SceneDirectorGame, SceneDirectorAudio, TomatoSplatController
- [x] Import/create 3 theatrical mask 3D models
- [x] Import/create audience sprite sheet + curtain sprite
- [x] Import/create 15 audio clips
- [x] Create EmotionIcon sprites (3 or 4)
- [x] Create TomatoSplatImage prefab
- [x] Wire `OnReactComplete_AnimEvent` on Audience animator
- [x] Wire `OnOpenComplete_AnimEvent` / `OnCloseComplete_AnimEvent` on Scenario animator

---

## 1. Build Settings & Scene Registration

### 1.1 Add Director.unity to Build Settings

1. **File → Build Settings**
2. Click **"Add Open Scenes"** while `Assets/Scenes/Director.unity` is open
3. Note the assigned **Build Index** (number in the list, 0-based)

The [`MiniGameRegistry.cs`](Assets/Scripts/Core/MiniGameRegistry.cs) already maps `"Director"` to `"Assets/Scenes/Director.unity"`. No code change needed — [`SceneLoader`](Assets/Scripts/Core/SceneLoader.cs) resolves scenes by path.

### 1.2 SceneIndex Field

In [`SceneDirectorGame.cs`](Assets/Scripts/Minigames/SceneDirector/SceneDirectorGame.cs), the Inspector field `_sceneIndex` defaults to 5. Update it to match Director.unity's actual build index. This is used by `SceneIndex` property (IMiniGame) and the `TriggerPlayAgain()` reload.

---

## 2. Layer Setup

### 2.1 Create StageLayer

1. **Edit → Project Settings → Tags and Layers**
2. Under **"Layers"**, find an empty slot (User Layer 6+)
3. Enter: `StageLayer`

### 2.2 Assign StageLayer

| GameObject                | Layer        |
| ------------------------- | ------------ |
| `MaskRoot`                | `StageLayer` |
| `HappyMask`               | `StageLayer` |
| `SurprisedMask`           | `StageLayer` |
| `AngryMask`               | `StageLayer` |
| Background Quad (if used) | `StageLayer` |

### 2.3 Camera Culling Setup

| Camera           | Culling Mask                               |
| ---------------- | ------------------------------------------ |
| **Main Camera**  | Everything **EXCEPT** `StageLayer`         |
| **Stage Camera** | `StageLayer` **ONLY** (uncheck all others) |

---

## 3. RenderTexture — StageRT

### 3.1 Create the Asset

1. Right-click in `Assets/RenderTextures/` (create folder if missing)
2. **Create → Render Texture**
3. Name: `StageRT`
4. Properties:
   - **Size:** 640 × 480
   - **Color Format:** R8G8B8A8_UNorm (or R8G8B8A8_SRGB)
   - **Depth Buffer:** No depth buffer
   - **Filter Mode:** Bilinear

### 3.2 Assign in Scene

| Component                    | Field            | Value     |
| ---------------------------- | ---------------- | --------- |
| **Stage Camera** → Camera    | `Target Texture` | `StageRT` |
| **CameraDisplay** → RawImage | `Texture`        | `StageRT` |

Pro tip: If `MaskController.Awake()` logs a warning about `targetTexture == null`, you missed step 3.

---

## 4. Required Scene Hierarchy & New UI Panels

### 4.1 Complete Hierarchy (what to build in Director.unity)

```
Director.unity
├── Main Camera                    (Camera, depth=-1, excludes StageLayer)
├── Stage Camera                   (Camera, depth=0, StageLayer only, Target Texture=StageRT)
├── MaskRoot                       (Layer=StageLayer, MaskController component)
│   ├── HappyMask                  (Layer=StageLayer, MeshFilter, MeshRenderer)
│   ├── SurprisedMask              (Layer=StageLayer, MeshFilter, MeshRenderer)
│   └── AngryMask                  (Layer=StageLayer, MeshFilter, MeshRenderer)
├── Canvas                         (Screen Space - Overlay, 1920×1080)
│   ├── CameraDisplay              (RawImage, Texture=StageRT, stretch full)
│   ├── Scenario                   (CurtainImage child + Animator + ScenarioController)
│   │   └── CurtainImage           (Image, curtain sprite)
│   ├── Audience                   (AudienceSprite child + Animator + AudienceController)
│   │   ├── AudienceSprite         (Image, audience sprite sheet)
│   │   └── DialogueBubble         (TextMeshProUGUI — audience speech)
│   ├── ApprovalBar                (ApprovalBarController)
│   │   ├── BarBackground          (Image — dark background)
│   │   ├── FillBar                (Image — Filled/Horizontal/Left)
│   │   └── FeedbackText           (TextMeshProUGUI — "CORRECT" / "SHOW:...")
│   ├── Countdown                  (CountdownController)
│   │   ├── FillBar                (Image — Filled/Horizontal/Left)
│   │   └── TimeText               (TextMeshProUGUI — "5" "4" "3"...)
│   ├── Script                     (ScriptController)
│   │   ├── CurrentEmotionText     (TextMeshProUGUI — large bold)
│   │   ├── NextEmotionText        (TextMeshProUGUI — small gray)
│   │   ├── ProgressText           (TextMeshProUGUI — small right-aligned)
│   │   ├── EmotionIcon            (Image — swaps sprites)
│   │   └── IntroText              (TextMeshProUGUI — LLM intro, center)
│   ├── StageArea                  *** NEW ***
│   │   └── TomatoSplatCanvas      *** NEW *** (Canvas overlay for splats)
│   ├── CountdownOverlay           *** NEW *** (CanvasGroup, full-screen)
│   │   └── CountdownOverlayText   *** NEW *** (TextMeshProUGUI — "3" "2" "1" "¡ACCIÓN!")
│   ├── FeedbackOverlay            *** NEW *** (CanvasGroup, full-screen)
│   │   └── FeedbackOverlayText    *** NEW *** (TextMeshProUGUI — "¡CORRECTO!"/"¡FALLADO!")
│   ├── PausePanel                 *** NEW *** (CanvasGroup, alpha=0)
│   │   ├── PauseTitle             *** NEW *** (TextMeshProUGUI — "PAUSA")
│   │   ├── ResumeButton           *** NEW *** (Button → SceneDirectorGame.Resume)
│   │   └── QuitButton             *** NEW *** (Button → SceneDirectorGame.QuitToMenu)
│   ├── ResultsPanel               (CanvasGroup, alpha=0)
│   │   ├── ResultsHeadline        (TextMeshProUGUI — "¡BRAVO!" / "¡ABUCHEADO!")
│   │   ├── ResultsEvaluation      (TextMeshProUGUI — LLM evaluation text)
│   │   ├── ResultsScore           (TextMeshProUGUI — "Score: 300")
│   │   ├── ResultsElementsPassed  (TextMeshProUGUI — "3 / 3")
│   │   └── PlayAgainButton        *** NEW *** (Button → SceneDirectorGame.TriggerPlayAgain)
│   ├── ScoreHUD                   *** NEW *** (TextMeshProUGUI — live score)
│   └── CameraElement              (CameraController)
├── EventSystem                    (auto-created with Canvas)
├── SceneDirectorGame              (SceneDirectorGame component — serialized refs)
├── SceneDirectorAudio             *** NEW *** (SceneDirectorAudio + 2 AudioSources)
└── TomatoSplatController          *** NEW *** (TomatoSplatController)

*** NEW *** = Not present in existing Director.unity — must be created.
```

### 4.2 New Panel Details

#### CountdownOverlay (CanvasGroup, full-screen rect)

This overlay shows the 3-2-1-¡ACCIÓN! countdown in the center of the screen.

**Child:** `CountdownOverlayText` (TextMeshProUGUI)
- font size: 120+, bold, center-aligned
- stretches to fill parent
- initially shows nothing (controlled by code)

**CanvasGroup:** `_countdownOverlay` on SceneDirectorGame
- Alpha: 0 (hidden by default)
- Interactable: false
- Blocks Raycasts: false

#### FeedbackOverlay (CanvasGroup, full-screen rect)

This overlay briefly shows "¡CORRECTO!" (green) or "¡FALLADO!" (red) when an element resolves.

**Child:** `FeedbackOverlayText` (TextMeshProUGUI)
- font size: 100+, bold, center-aligned
- stretches to fill parent
- initially shows nothing

**CanvasGroup:** `_feedbackOverlay` on SceneDirectorGame
- Alpha: 0
- Interactable: false
- Blocks Raycasts: false

#### PausePanel (CanvasGroup, alpha=0)

**Children:**
- `PauseTitle` (TextMeshProUGUI): "PAUSA" — large, center-top
- `ResumeButton` (Button): "Reanudar" → `SceneDirectorGame.Resume()`
- `QuitButton` (Button): "Salir" → `SceneDirectorGame.QuitToMenu()`

**CanvasGroup:** `_pausePanel` on SceneDirectorGame
- Alpha: 0 (shown/hidden by Pause()/Resume())

#### ScoreHUD (TextMeshProUGUI, top-right or near Script panel)

Live score display. Shows "Score: 0" during gameplay and updates via `GameManager.OnScoreChanged`.

**Field:** `_scoreText` on SceneDirectorGame

#### PlayAgainButton (child of ResultsPanel)

**Button:** "¡Otra vez!" → `SceneDirectorGame.TriggerPlayAgain()`
- Visible only when ResultsPanel is shown

#### TomatoSplatCanvas (Canvas, full-screen)

A separate Screen Space - Overlay canvas with **high sort order** (e.g., 100) so splats render above everything.

**Component:** `TomatoSplatController`
- The canvas itself is the `_poolParent` for splat instances, or create an empty child `SplatPool` as parent.

#### SceneDirectorAudio GameObject

Empty GameObject with:
- `SceneDirectorAudio` component (this script)
- **SFX AudioSource** (child or sibling): assigned to `_sfxSource`
- **Music AudioSource** (child or sibling): assigned to `_musicSource`

Both AudioSources should have:
- `Play On Awake`: false
- `Loop`: depends on usage (music source loops, SFX source does not)

---

## 5. SceneDirectorGame — Inspector Serialized Fields

### 5.1 Scene Settings

| Field                  | Description                            | Suggested Value      |
| ---------------------- | -------------------------------------- | -------------------- |
| `_sceneIndex`          | Director.unity build index             | Match Build Settings |
| `_mainMenuSceneIndex`  | MainMenu build index                   | 1 (verify)           |
| `_betweenElementDelay` | Seconds between elements               | 1.5                  |
| `_endRoundDelayWin`    | Seconds between win and curtain close  | 3.0                  |
| `_endRoundDelayLose`   | Seconds between lose and curtain close | 2.0                  |
| `_pointsPerElement`    | Base points per passed element         | 100                  |

### 5.2 LLM

| Field     | Description                             | Value                              |
| --------- | --------------------------------------- | ---------------------------------- |
| `_useLLM` | Toggle LLM for sequence/eval generation | true (production), false (testing) |

### 5.3 Editor Simulation

| Field               | Description                        | Value                                     |
| ------------------- | ---------------------------------- | ----------------------------------------- |
| `_editorSimulation` | Enable keyboard H/S/A/N simulation | true (editor testing), false (production) |

### 5.4 UI References — Overlays (drag all GameObjects here)

| Serialized Field         | GameObject to drag                      |
| ------------------------ | --------------------------------------- |
| `_countdownOverlay`      | CountdownOverlay (CanvasGroup)          |
| `_countdownText`         | CountdownOverlayText (TextMeshProUGUI)  |
| `_feedbackOverlay`       | FeedbackOverlay (CanvasGroup)           |
| `_feedbackOverlayText`   | FeedbackOverlayText (TextMeshProUGUI)   |
| `_resultsPanel`          | ResultsPanel (CanvasGroup)              |
| `_resultsHeadline`       | ResultsHeadline (TextMeshProUGUI)       |
| `_resultsEvaluation`     | ResultsEvaluation (TextMeshProUGUI)     |
| `_resultsScore`          | ResultsScore (TextMeshProUGUI)          |
| `_resultsElementsPassed` | ResultsElementsPassed (TextMeshProUGUI) |
| `_pausePanel`            | PausePanel (CanvasGroup)                |
| `_scoreText`             | ScoreHUD (TextMeshProUGUI)              |

---

## 6. SceneDirectorAudio — Inspector Serialized Fields

### 6.1 Audio Sources

| Field          | Reference                                                           |
| -------------- | ------------------------------------------------------------------- |
| `_sfxSource`   | AudioSource for one-shot SFX (child of SceneDirectorAudio)          |
| `_musicSource` | AudioSource for looping music/ambient (child of SceneDirectorAudio) |

### 6.2 Audio Clips (15 slots, all optional — null-safe)

#### Curtain
| Field           | Suggested Clip                | Duration |
| --------------- | ----------------------------- | -------- |
| `_curtainOpen`  | Fabric whoosh / curtain open  | 1–2s     |
| `_curtainClose` | Fabric whoosh / curtain close | 1–2s     |

#### Countdown UI
| Field              | Suggested Clip                   | Duration |
| ------------------ | -------------------------------- | -------- |
| `_countdownTick`   | Short tick/beep                  | 0.1–0.3s |
| `_countdownGo`     | Fanfare / "go" chime             | 0.5–1.0s |
| `_countdownUrgent` | Fast heartbeat / pulse (looping) | any      |

#### Element Feedback
| Field           | Suggested Clip              | Duration |
| --------------- | --------------------------- | -------- |
| `_correctChime` | Pleasant ding/chime         | 0.5–1.0s |
| `_wrongBuzzer`  | Harsh buzzer / wrong answer | 0.5–1.0s |

#### Audience
| Field              | Suggested Clip       | Duration |
| ------------------ | -------------------- | -------- |
| `_audienceAmbient` | Looping crowd murmur | any      |
| `_applause`        | Applause / cheering  | 2–4s     |
| `_boo`             | Booing / jeering     | 1–3s     |
| `_tomatoSplat`     | Wet splat impact     | 0.5–1.0s |
| `_audienceLaugh`   | Audience laughter    | 1–3s     |

#### Music
| Field          | Suggested Clip                        | Duration |
| -------------- | ------------------------------------- | -------- |
| `_bgmGameplay` | Upbeat theater/circus loop            | any      |
| `_stingWin`    | Dramatic victory fanfare              | 3–5s     |
| `_stingLose`   | Dramatic failure sting / sad trombone | 3–5s     |

---

## 7. TomatoSplatController — Inspector Serialized Fields

| Field              | Description                                   | Suggested Value                  |
| ------------------ | --------------------------------------------- | -------------------------------- |
| `_poolParent`      | Transform under which splats are instantiated | Empty child of TomatoSplatCanvas |
| `_splatPrefab`     | Image prefab with tomato splat sprite         | Must have Image + RectTransform  |
| `_poolSize`        | Number of pooled splat instances              | 10                               |
| `_fadeInDuration`  | Fade-in time per splat                        | 0.15                             |
| `_holdDuration`    | Full-opacity hold time                        | 1.5                              |
| `_fadeOutDuration` | Fade-out time                                 | 0.8                              |
| `_minScale`        | Minimum random scale (0-1)                    | 0.6                              |
| `_maxScale`        | Maximum random scale (1+)                     | 1.2                              |
| `_edgeMargin`      | Minimum distance from screen edge             | 0.1                              |

### 7.1 TomatoSplatImage Prefab

1. Create a GameObject with an `Image` component
2. Set `Raycast Target` = **false**
3. Assign the tomato splat sprite (a red splatter texture with alpha)
4. Image color: white (allows color modulation)
5. Make it a prefab in `Assets/Prefabs/SceneDirector/`
6. Drag the prefab into `TomatoSplatController._splatPrefab`

---

## 8. ScriptController — Inspector Fields

| Field                 | Description                              | Suggested Value                             |
| --------------------- | ---------------------------------------- | ------------------------------------------- |
| `_currentEmotionText` | Displays required emotion                | already exists                              |
| `_nextEmotionText`    | Previews next emotion                    | already exists                              |
| `_progressText`       | Shows "1/4" progress                     | already exists                              |
| `_emotionIcon`        | Image that swaps sprites                 | already exists                              |
| `_emotionSprites`     | Array of sprites indexed by EmotionLabel | need 3–4 sprites                            |
| `_introText`          | Shows LLM intro text                     | new TextMeshProUGUI (child of Script panel) |
| `_sequenceLength`     | Number of emotions (3–6)                 | 3                                           |
| `_timeLimitStart`     | Seconds for first element                | 6.0                                         |
| `_timeLimitEnd`       | Seconds for last element                 | 3.0                                         |

### 8.1 EmotionIcon Sprites

Create 4 sprites:
- `[0]` = Neutral (circle outline, gray)
- `[1]` = Happy (smiley, yellow/gold)
- `[2]` = Surprised (wide eyes, orange/amber)
- `[3]` = Angry (furrowed, red)

Assign all 4 in `_emotionSprites` on ScriptController.

---

## 9. MaskController — Inspector Fields

| Field              | Description                                  | Suggested Value                             |
| ------------------ | -------------------------------------------- | ------------------------------------------- |
| `_stageCamera`     | Stage Camera reference                       | drag Stage Camera                           |
| `_maskDepth`       | Z distance of masks from Stage Camera        | 5.0 (tune)                                  |
| `_scaleMultiplier` | Multiplier applied to FaceScale              | 1.5 (tune)                                  |
| `_masks`           | Array of 4 GameObjects by EmotionLabel index | [null, HappyMask, SurprisedMask, AngryMask] |

### 9.1 3D Theatrical Masks

Three meshes needed:
- **HappyMask** — Comedy/tragedy comedy half (smiling). Gold/brass material.
- **SurprisedMask** — Wide-eyed, open mouth. Ivory/cream material.
- **AngryMask** — Furrowed brow, frown. Dark red/copper material.

Index `[0]` should be **null** (Neutral = no mask visible).

When imported:
1. Place in `Assets/Models/SceneDirector/`
2. Make them Prefabs
3. Drag into MaskController `_masks[1]`, `[2]`, `[3]`

---

## 10. Animations & Animator Controllers

### 10.1 Scenario Curtain — ScenarioController

**Animator required** on the Scenario GameObject. Two animation states:

| State          | Type           | Trigger Parameter |
| -------------- | -------------- | ----------------- |
| Idle (default) | empty          | —                 |
| Open           | Animation clip | `Open` (Trigger)  |
| Close          | Animation clip | `Close` (Trigger) |

**Parameters:**
- `Open` (Trigger)
- `Close` (Trigger)

**Animation clips:**
- **Open clip:** Curtain parts/reveals stage area
- **Close clip:** Curtain closes/shrouds stage area

**Animation Events required** (last frame of each clip):
- On the final frame of **Open**: call `ScenarioController.OnOpenComplete_AnimEvent()`
- On the final frame of **Close**: call `ScenarioController.OnCloseComplete_AnimEvent()`

Without these animation events, the game will hang — `OnCurtainOpen()` and `OnCurtainClose()` on `SceneDirectorGame` will never fire.

### 10.2 Audience — AudienceController

**Animator required** on the Audience GameObject. Three states:

| State          | Transition                                        |
| -------------- | ------------------------------------------------- |
| Idle (default) | —                                                 |
| SlightMove     | Idle → SlightMove when `SlightMove` (Bool) = true |
| React          | Any State → React on `React` (Trigger)            |

**Parameters:**
- `SlightMove` (Bool)
- `React` (Trigger)
- `IsPositive` (Bool) — read during React state to choose applause vs tomato animation

**Animation clips:**
- **Idle clip:** Still crowd, looping
- **SlightMove clip:** Subtle excited movement, looping
- **React (IsPositive=true) clip:** Big applause/cheering, plays once, auto-returns to Idle
- **React (IsPositive=false) clip:** Angry booing/tomato throwing, plays once, auto-returns to Idle

**Animation Event required** (last frame of React clip):
- Call `AudienceController.OnReactComplete_AnimEvent()`

If the Audience controller has separate positive/negative React clips, add the event to **both** clips.

### 10.3 Audience Sprite Sheet

Create a sprite sheet containing:
- Idle frame(s): static crowd
- SlightMove frames: slight movement, 2-4 frames for a looping subtle animation
- React positive frames: audience leaping, clapping, cheering — 4-8 frames
- React negative frames: audience jeering, throwing tomatoes — 4-8 frames

Import as Unity sprite sheet (Sprite Mode: Multiple) and slice accordingly.

---

## 11. Prefabs to Create

### 11.1 TomatoSplatImage Prefab
- (`Assets/Prefabs/SceneDirector/TomatoSplatImage.prefab`)
- See Section 7.1

### 11.2 Mask Prefabs (3)
- (`Assets/Prefabs/SceneDirector/HappyMask.prefab`)
- (`Assets/Prefabs/SceneDirector/SurprisedMask.prefab`)
- (`Assets/Prefabs/SceneDirector/AngryMask.prefab`)
- See Section 9.1

### 11.3 Curtain Image Texture
- (`Assets/Sprites/SceneDirector/Curtain.png`)
- Red velvet texture or sprite
- Full-screen capable (1920×1080 or tiles)

### 11.4 Audience Sprite Sheet
- (`Assets/Sprites/SceneDirector/AudienceSpriteSheet.png`)
- See Section 10.3

### 11.5 Emotion Icons (4)
- (`Assets/Sprites/SceneDirector/Emoticon_*.png`)
- See Section 8.1

### 11.6 Tomato Splat Sprite
- (`Assets/Sprites/SceneDirector/TomatoSplat.png`)
- Red splatter with alpha, semi-transparent

---

## 12. Audio Clip Sources

All audio clips referenced in Section 6. Where to get them:

- **Free sources:** freesound.org, pixabay.com/sound-effects, mixkit.co
- **Curtain sounds:** search "curtain open whoosh" / "fabric"
- **Crowd sounds:** search "audience applause" / "crowd boo" / "audience murmur"
- **Tomato splat:** search "splat" / "tomato hit"
- **Music:** search "circus march" / "theater music" / "dramatic sting"
- **UI sounds:** search "correct chime" / "wrong buzzer" / "countdown tick"

Place imported clips in `Assets/Audio/SceneDirector/`.

---

## 13. Button Wiring

### 13.1 Pause Panel Buttons

| Button       | OnClick target                   | Method         |
| ------------ | -------------------------------- | -------------- |
| ResumeButton | SceneDirectorGame (scene object) | `Resume()`     |
| QuitButton   | SceneDirectorGame (scene object) | `QuitToMenu()` |

### 13.2 Results Panel Buttons

| Button          | OnClick target                   | Method               |
| --------------- | -------------------------------- | -------------------- |
| PlayAgainButton | SceneDirectorGame (scene object) | `TriggerPlayAgain()` |

---

## 14. Integration Verification Checklist

After setting up all assets in the Unity Editor, run through this checklist:

### 14.1 RenderTexture Pipeline
- [x] Stage Camera → Target Texture = StageRT
- [x] CameraDisplay → RawImage → Texture = StageRT
- [x] StageLayer assigned to MaskRoot + all mask children
- [x] Main Camera excludes StageLayer
- [x] Stage Camera culls only StageLayer
- [x] Play mode: webcam feed visible through CameraDisplay
- [x] Play mode: 3D mask meshes visible when Stage Camera looks at them

### 14.2 Curtain Animation
- [x] ScenarioController has Animator assigned
- [x] Animator has Open/Close triggers
- [x] Animation Event `OnOpenComplete_AnimEvent()` on last frame of Open
- [x] Animation Event `OnCloseComplete_AnimEvent()` on last frame of Close
- [x] On play: curtain opens → SceneDirectorGame.OnCurtainOpen fires (check logs)

### 14.3 Audience Animation
- [x] AudienceController has Animator assigned
- [x] Animator has SlightMove (Bool), React (Trigger), IsPositive (Bool)
- [x] Animation Event `OnReactComplete_AnimEvent()` on last frame of React clip(s)
- [x] On play + simulate correct emotion: audience moves to SlightMove
- [x] On element pass: audience reacts positive, then returns to Idle

### 14.4 SceneDirectorGame Inspector
- [x] All 12 UI serialized fields assigned
- [x] _sceneIndex matches Build Settings
- [x] All existing singleton controllers present in scene
- [x] Play mode: no NullReferenceException in Console from missing refs

### 14.5 SceneDirectorAudio
- [x] Both AudioSources present and assigned
- [x] All 15 clip slots filled (or at least the critical ones)
- [x] Play mode: no errors from null clips (all calls are null-safe)

### 14.6 TomatoSplatController
- [x] Canvas/SplatPool set up
- [x] _splatPrefab assigned (Image component)
- [x] Call `TomatoSplatController.Instance.SplatBurst(3)` from Console to test
- [x] Splats appear, hold, fade out, and pool correctly

### 14.7 ScriptController
- [x] All 4 UI text references assigned
- [x] _introText assigned (new)
- [x] _emotionSprites has 4 elements assigned
- [x] Difficulty fields set to desired values

### 14.8 MaskController
- [x] _stageCamera assigned
- [x] _masks[1] HappyMask assigned
- [x] _masks[2] SurprisedMask assigned
- [x] _masks[3] AngryMask assigned
- [x] _masks[0] is null (Neutral)

### 14.9 Editor Simulation
- [ ] `_editorSimulation = true` on SceneDirectorGame
- [ ] Press H during gameplay: mask swaps to Happy
- [ ] Press S: mask swaps to Surprised
- [ ] Press A: mask swaps to Angry
- [ ] Press N: mask hides (Neutral)

### 14.10 Full Game Loop
- [ ] Scene starts → curtain opens → 3-2-1 overlay
- [ ] Sequence starts → first emotion required
- [ ] Simulate correct emotion → bar fills → element passes
- [ ] All elements pass → curtain closes → results panel
- [ ] Simulate wrong emotion → bar drains → element fails → round ends
- [ ] Play Again button reloads the scene
- [ ] Pause button works (pauses countdown + bar)
- [ ] Resume button restores state
- [ ] Quit button returns to main menu

---

## 15. File Summary — What Exists vs. Needs Creation

### Already Exists in Code (11 files)

| File                          | Status                            |
| ----------------------------- | --------------------------------- |
| `SceneDirectorGame.cs`        | Rewritten — full state machine    |
| `ScenarioController.cs`       | Existing — no changes needed      |
| `AudienceController.cs`       | Modified — guard + dialogue       |
| `CountdownController.cs`      | Existing — no changes needed      |
| `ScriptController.cs`         | Modified — difficulty + intro     |
| `CameraController.cs`         | Modified — refactored simulation  |
| `MaskController.cs`           | Modified — RT validation          |
| `ApprovalBarController.cs`    | Modified — initial fill           |
| `ScriptLLMParser.cs`          | **New** — LLM JSON parser         |
| `TomatoSplatController.cs`    | **New** — splat pool system       |
| `SceneDirectorAudio.cs`       | **New** — 15-slot audio singleton |
| `MiniGameRegistry.cs` (Core)  | Modified — added Director entry   |
| `EmotionClassifier.cs` (Face) | Modified — added RawTopEmotion    |

### Needs Creation in Unity Editor

| Asset                                        | Type               | Location                        |
| -------------------------------------------- | ------------------ | ------------------------------- |
| StageRT                                      | RenderTexture      | `Assets/RenderTextures/`        |
| HappyMask                                    | 3D Model / Prefab  | `Assets/Models/SceneDirector/`  |
| SurprisedMask                                | 3D Model / Prefab  | `Assets/Models/SceneDirector/`  |
| AngryMask                                    | 3D Model / Prefab  | `Assets/Models/SceneDirector/`  |
| Curtain sprite                               | Texture/Sprite     | `Assets/Sprites/SceneDirector/` |
| Audience sprite sheet                        | Texture/Sprite     | `Assets/Sprites/SceneDirector/` |
| Emotion sprites (×4)                         | Sprite             | `Assets/Sprites/SceneDirector/` |
| TomatoSplatImage prefab                      | Prefab             | `Assets/Prefabs/SceneDirector/` |
| Tomato splat sprite                          | Sprite             | `Assets/Sprites/SceneDirector/` |
| Audio clips (×15)                            | AudioClip          | `Assets/Audio/SceneDirector/`   |
| Animator ac: Scenario                        | AnimatorController | `Assets/Animations/`            |
| Animator ac: Audience                        | AnimatorController | `Assets/Animations/`            |
| Anim clips: Open, Close                      | AnimationClip      | `Assets/Animations/`            |
| Anim clips: Idle, SlightMove, React+, React- | AnimationClip      | `Assets/Animations/`            |
| 6 new UI panels (Section 4)                  | GameObject         | Director.unity Canvas           |
| SceneDirectorAudio GameObject                | GameObject         | Director.unity root             |
| TomatoSplatController Canvas                 | GameObject         | Director.unity Canvas           |

---

*Guide complete. For implementation plan details, see [`plans/scene-director-plan.md`](../plans/scene-director-plan.md).*