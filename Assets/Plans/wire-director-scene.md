# Project Overview
- Game Title: CubiWare - Scene Director Minigame
- High-Level Concept: A theatrical emotion-matching minigame where players wear AR masks and match target emotions to perform for a virtual audience.
- Players: Single player
- Target Platform: PC (StandaloneWindows64)
- Render Pipeline: PC_RPAsset (likely URP or custom)
- Screen Orientation: Landscape 1920x1080

# Game Mechanics
## Core Gameplay Loop
The player sees a sequence of emotions to perform. They must hold the correct facial expression to fill an approval bar before the timer runs out. Success leads to audience applause and progressing the script; failure results in tomatoes being thrown and the round ending.

## Controls and Input Methods
- Webcam input for face tracking and emotion detection.
- Keyboard simulation (H/S/A/N) for testing.
- UI buttons for Pause, Resume, Quit, and Play Again.

# UI
- Stage view showing the player with an AR mask.
- Script panel showing current and next emotions.
- Countdown timer and Approval bar.
- Feedback overlays and Results/Pause panels.

# Key Asset & Context
- `Director.unity`: The main minigame scene.
- `MaskController.cs`: Manages 3D masks.
- `SceneDirectorGame.cs`: Main game logic and UI wiring.
- `ScenarioController.cs`: Curtain animations.
- `AudienceController.cs`: Audience reactions.
- `ScriptController.cs`: Sequence management.
- `StageRT`: RenderTexture for the stage view.

# Implementation Steps

## STEP 1: Fix Layer Assignments
1. Verify "StageLayer" exists at index 6 in TagManager.
2. Set `MaskRoot` and its children (`HappyMask`, `SurprisedMask`, `AngryMask`) to "StageLayer".
3. Set `StageBackground` to "StageLayer".
4. Set `Main Camera` Culling Mask to exclude "StageLayer" (ensure bit 6 is off).
5. Set `Stage Camera` Culling Mask to ONLY "StageLayer" (ensure only bit 6 is on).

## STEP 2: Fix AudioListener Conflict
1. Remove the `AudioListener` component from the `Stage Camera` GameObject.

## STEP 3: Verify StageRT Configuration
1. Update `Assets/RenderTextures/StageRT` RenderTexture:
   - Size: 640 x 480
   - Color Format: `R8G8B8A8_UNorm`
   - Depth Buffer: `None`
   - Filter Mode: `Bilinear`
2. Assign `StageRT` to `Stage Camera`'s `Target Texture` field.
3. Assign `StageRT` to `CameraDisplay` RawImage's `Texture` field.

## STEP 4: Create/Verify UI Child GameObjects
1. Under `Audience`, create `DialogueBubble` (TextMeshProUGUI):
   - Font size 24, center-aligned, positioned above `AudienceSprite`.
2. Verify `IntroText` under `Script`.
3. Verify `ResultsElementsPassed` under `ResultsPanel`.
4. Under `PausePanel`, create `PauseTitle` (TextMeshProUGUI):
   - Text "PAUSA", font size 60+, bold, center-aligned, top-center.
5. Verify `CountdownOverlayText` under `CountdownOverlay`.
6. Verify `FeedbackOverlayText` under `FeedbackOverlay`.

## STEP 5: Verify CameraController Placement
1. Create `CameraElement` GameObject as child of `Canvas`.
2. Add `CameraController` component to `CameraElement`.
3. Assign `_cameraDisplay` (CameraDisplay RawImage) and `_maskController` (MaskRoot GameObject).

## STEP 6: Fix Bar Fill Direction
1. On `Countdown -> Fill Bar` Image: Type=Filled, Method=Horizontal, Origin=Left.
2. On `ApprovalBar -> Fill Bar` Image: Type=Filled, Method=Horizontal, Origin=Left.

## STEP 7: Verify MaskController Fields
1. On `MaskRoot` (MaskController):
   - `_maskObjects` size 4: [0] null, [1] HappyMask, [2] SurprisedMask, [3] AngryMask.
   - `_maskRoot` = MaskRoot.
   - `_stageCamera` = Stage Camera.
   - `_reader` = Main Camera (FaceLandmarkReader).
   - `_maskDepth` = 1.5, `_scaleMultiplier` = 3.5.

## STEP 8: Assign SceneDirectorGame Fields
1. On `SceneDirectorGame`:
   - Assign all 11 UI Overlay fields (`_countdownOverlay`, `_countdownText`, etc.).
   - Set `_sceneIndex` and `_mainMenuSceneIndex`.
   - Set delays and points as specified.

## STEP 9: Assign Scenario and Audience Controller Fields
1. On `Scenario`: Assign `_animator` (from same GO).
2. On `Audience`: Assign `_animator` (from same GO) and `_dialogueText` (DialogueBubble).

## STEP 10: Assign ScriptController Fields
1. On `Script`:
   - Assign `_introText` (IntroText).
   - Assign `_emotionSprites` (4 elements with specified placeholders).
   - Verify other UI fields.
   - Set Difficulty Curve: `_sequenceLength` = 3, `_timeLimitStart` = 6, `_timeLimitEnd` = 3.

## STEP 11: Configure Animators
1. `Scenario` Animator:
   - Parameters: Trigger "Open", Trigger "Close".
   - Events: `OnOpenComplete_AnimEvent()` and `OnCloseComplete_AnimEvent()` at end of clips.
2. `Audience` Animator:
   - Parameters: Bool "SlightMove", Trigger "React", Bool "IsPositive".
   - Setup transitions (Idle <-> SlightMove, Any -> React).
   - Event: `OnReactComplete_AnimEvent()` at end of React clips.

## STEP 12: Verify EmotionClassifier
1. Ensure `FaceLandmarkReader` and `EmotionClassifier` are on `Main Camera`.

## STEP 13: Complete Audio Clips
1. On `SceneDirectorAudio`: Assign placeholder clips for `_audienceAmbient` and `_countdownUrgent`.
2. Set `_sfxSource` Play On Awake = false.
3. Set `_musicSource` Play On Awake = false, Loop = true.

## STEP 14: Configure CanvasGroups
1. Set Alpha=0, Interactable=false, Blocks Raycasts=false for `CountdownOverlay`, `FeedbackOverlay`, `PausePanel`, `ResultsPanel`.

## STEP 15: TomatoSplatCanvas Sort Order
1. Set `TomatoSplatCanvas` Sort Order to 100.

# Verification & Testing
1. Enter Play Mode.
2. Check Console for NullReferenceExceptions.
3. Verify Curtain opens and logs "Curtain open — starting countdown overlay.".
4. Use H/S/A/N keys to test mask swapping and bar filling.
5. Verify audience reactions and results screen.
