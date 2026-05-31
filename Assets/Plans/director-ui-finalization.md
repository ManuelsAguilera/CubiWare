# Project Overview
- Game Title: CubiWare (Minigame: Scene Director)
- High-Level Concept: An AR-based minigame where the player acts out emotions requested by a director.
- Players: Single player.
- Target Platform: Standalone Windows.
- Screen Orientation: Landscape 1920x1080.
- Render Pipeline: URP (PC_RPAsset).

# Game Mechanics
## Core Gameplay Loop
The player sees an emotion on the Script panel. They must perform it toward the webcam. MediaPipe detects the face and applies a 3D theatrical mask. An approval bar fills up as the player holds the correct emotion. After several successful elements, the player wins the round.

## Controls and Input Methods
- Webcam/Face Tracking: Primary input for emotion detection.
- Mouse/Touch: Interaction with UI buttons (Pause, Resume, Restart).
- Keyboard: Simulation keys (H, S, A, N) for testing.

# UI
The UI follows a theatrical theme with:
- **Script Panel (Top-Left):** Shows current/next emotions and progress.
- **Scenario (Center):** Webcam feed framed by curtains and a frame.
- **Bars (Sides):** Time limit on the left, approval progress on the right.
- **Pop-ups:** Results and Pause overlays.
- **Bottom Props:** Wooden blocks and theatrical elements.

# Key Asset & Context
- **Scripts:** `SceneDirectorGame`, `ScriptController`, `MaskController`, `TomatoSplatController`, `ApprovalBarController`.
- **Assets:** `StageRT` (RenderTexture), `Emoticon_*.png` sprites, `TomatoSplatImage` prefab, `HappyMask`/`SurprisedMask`/`AngryMask` 3D models.
- **Guide:** `Assets/Assets2/scenedirector/scene-director-assets-guide.md`.

# Implementation Steps
1. **Scene Setup & Layers:**
   - Ensure `StageLayer` exists and is assigned to `Mask Root` and its children.
   - Configure `Stage Camera` to only render `StageLayer` and target `StageRT`.
   - Configure `Main Camera` to exclude `StageLayer`.
2. **Component Wiring (ScriptController):**
   - Assign `_currentEmotionText`, `_nextEmotionText`, `_progressText`, `_emotionIcon`, and `_introText` from the hierarchy.
   - Assign the 4 `Emoticon_*.png` sprites to the `_emotionSprites` array.
3. **Component Wiring (MaskController):**
   - Assign `_stageCamera` and `_maskRoot`.
   - Wire `_maskObjects` array with Happy, Surprised, and Angry masks from the `Mask Root` hierarchy.
   - Assign `_reader` from the scene.
4. **Component Wiring (TomatoSplatController):**
   - Assign `_poolParent` and the `TomatoSplatImage` prefab.
5. **Button Wiring:**
   - Connect `ResumeButton.onClick` to `SceneDirectorGame.Resume()`.
   - Connect `QuitButton.onClick` to `SceneDirectorGame.QuitToMenu()`.
   - Connect `PlayAgainButton.onClick` to `SceneDirectorGame.TriggerPlayAgain()`.
6. **Visual Styling:**
   - Apply Figma-specified colors (Cream for script, Dark Brown for results, Green for approval).
   - Set font sizes and bold styles for headlines.
   - Set the `IntroText` default content.
7. **Verification:**
   - Check all 12+ serialized fields on `SceneDirectorGame`.
   - Verify that the game loop starts and transitions correctly in the Editor.

# Verification & Testing
- **Manual Check:** Run the scene and verify the "Bravo" popup appears, the script panel updates, and the bars fill.
- **Simulation Test:** Use H/S/A/N keys to ensure masks swap and the approval bar reacts.
- **Button Test:** Click Pause -> Resume to verify game state resumes.
- **Console Check:** Ensure no NullReferenceExceptions appear on start.
