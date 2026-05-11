# ARcade Rush — Implementation Plan (Refactored)

> Stored in `docs/` per project convention.  
> Reference doc: `docs/ARcade Context.md` (v1.0)  
> Developer guide: `docs/developer-guide.md`  
> Last updated: 2026-05-10

---

## Status Overview

| Phase | Description | Status |
|-------|-------------|--------|
| A | Configuration & Fixes | ✅ Done |
| B | Shooter Minigame Scripts | ✅ Done |
| C | Scenes & Prefabs | 🔧 Needs Unity Editor |
| D | Cleanup (Legacy Files) | ✅ Done |
| E | Verification | ⏳ Pending (after Unity setup) |
| F | Developer Documentation | ✅ Done |

---

## Phase A — Configuration & Fixes

| # | Task | File | Status |
|---|------|------|--------|
| A1 | Fix MediaPipe plugin path | [`Packages/manifest.json`](Packages/manifest.json) | ✅ Changed from Linux absolute `file:/home/...` to relative `file:./com.github.homuler.mediapipe` |
| A2 | Clean duplicate model files | [`Assets/StreamingAssets/`](Assets/StreamingAssets/) | ✅ Deleted `.task` files from root; kept only in `mediapipe/` subfolder |
| A3 | Create GroqConfig ScriptableObject | [`Assets/Resources/GroqConfig.asset`](Assets/Resources/GroqConfig.asset) | ✅ Asset exists — user must set API key in Unity Inspector |
| A4 | Fix MainMenu scene indices | [`Assets/Scripts/UI/MainMenuController.cs`](Assets/Scripts/UI/MainMenuController.cs) | ✅ Renamed `_startInterrogatorioBtn` → `_startShooterBtn`, index 3→2 |

---

## Phase B — Shooter Minigame Scripts

All scripts in [`Assets/Scripts/Minigames/Shooter/`](Assets/Scripts/Minigames/Shooter/) — namespace `ARcadeRush.Minigames.Shooter`.

| # | File | Description | Status |
|---|------|-------------|--------|
| B1 | [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs) | `TargetType` enum (Bandit/Innocent), wave-based activation, raise/fall animations, timeout, `OnTargetDeactivated` event | ✅ |
| B2 | [`TargetManager.cs`](Assets/Scripts/Minigames/Shooter/TargetManager.cs) | Scene target discovery, row grouping, round-robin activation, cooldowns | ✅ |
| B3 | [`ShooterHandController.cs`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) | Index finger tip (landmark 8) aim, ClosedFist fire, ThumbDown safety toggle, delegates shoot to GunController | ✅ |
| B4 | [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) | `IMiniGame` implementation — starts paused, Space to unpause, wave progression, 90s timer, HUD hide on pause, `LastScore` static for MainMenu display | ✅ |
| B5 | [`GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs) | Hitscan system, ammo management (6 rounds), auto-reload, aim preview, bullet trail, events for scoring/HUD | ✅ |
| B6 | Modify [`GestureDetector.cs`](Assets/Scripts/Hand/GestureDetector.cs) | Added `GestureType.ThumbDown` + `OnThumbDown` event for safety toggle | ✅ |
| B7 | [`HUDController.cs`](Assets/Scripts/UI/HUDController.cs) | Added `SetHUDVisible(bool)`, `ShowPauseOverlay(string)`, `HidePauseOverlay()` for pause state management | ✅ |
| B8 | [`MainMenuController.cs`](Assets/Scripts/UI/MainMenuController.cs) | Added `_lastScoreText` to display `ShooterGame.LastScore` on return from game | ✅ |

---

## Phase C — Scenes & Prefabs (Unity Editor Required)

Cannot be done from code — Unity YAML scene/prefab files must be created in-editor.

### Prefabs to Create

Save all in [`Assets/Prefabs/`](Assets/Prefabs/):

| Prefab | Components | Details |
|--------|-----------|---------|
| `Bandit.prefab` | Cube + `Target` + Rigidbody | 1.5×2×1, red URP Lit material, `TargetType.Bandit` |
| `Innocent.prefab` | Cube + `Target` + Rigidbody | 1.5×2×1, green URP Lit material, `TargetType.Innocent` |
| `Bullet.prefab` | Sphere + Rigidbody + TrailRenderer | radius 0.15, yellow emissive, gravity off, Trigger collider |

### Scene to Create

| Scene | Build Index | Contents |
|-------|-------------|----------|
| [`Assets/Scenes/MG_Shooter.unity`](Assets/Scenes/MG_Shooter.unity) | 2 | Camera (FOV 60°, dark blue clear), Directional Light, `ShooterGameController` (ShooterGame component), `TargetSpawner` (assign prefabs), `HandController` (ShooterHandController + Hand3DProjector + GestureDetector) |

### Build Settings

```
Index 0: Assets/Scenes/Bootstrap.unity
Index 1: Assets/Scenes/MainMenu.unity
Index 2: Assets/Scenes/MG_Shooter.unity
```

Detailed step-by-step instructions in [`docs/developer-guide.md`](docs/developer-guide.md#107-how-to-set-up-the-shooter-scene-in-unity) (Section 10.7).

---

## Phase D — Cleanup

All legacy files from the original prototype have been deleted:

| Path | Reason |
|------|--------|
| `Assets/Scenes/FaceEmotionTracking/` | Empty scene, no scripts |
| `Assets/Scenes/HandModelTracker/` | Prototype-only |
| `Assets/Scenes/HandTracker/` | Superseded by current scripts |
| `Assets/Scenes/Llm/` | Superseded by LLMConnector |
| `Assets/Scenes/MaiMenu/` | Superseded by MainMenu.unity |
| `Assets/Scenes/SampleScene.unity` | Default Unity scene |
| `Assets/Scenes/DummyTesting.unity` | Test-only |
| `Assets/Scenes/MG_Interrogatorio.unity` | Replaced by Shooter |
| `Assets/Scripts/Minigames/Interrogatorio/` | Replaced by Shooter |
| `Assets/Prefabs/Interrogatorio/` | Replaced by Shooter prefabs |

---

## Phase E — Verification (Pending Unity Setup)

After creating the scene and prefabs in Unity:

1. ✅ Open Bootstrap → verify all 5 singletons survive scene load
2. ✅ Run MainMenu → "Shooter" button loads MG_Shooter
3. ✅ Hand skeleton renders (HandModel spheres + lines)
4. ✅ GestureDetector fires OpenHand / ClosedFist / Point / ThumbDown
5. ✅ Camera feed appears on RawImage
6. ✅ Shooter: aim ray visible (yellow=safety, red=firing)
7. ✅ Shooter: ClosedFist spawns bullet / hitscan
8. ✅ Shooter: Bandit hit → +10 score, Innocent hit → -20
9. ✅ Shooter: timer counts down, returns to MainMenu at 0
10. ✅ Face landmarks detected, EmotionClassifier fires `OnEmotionChanged`
11. ✅ LLM test button returns response < 3s
12. ✅ All 5 singletons: GameManager, SceneLoader, CameraFeedCtrl, MediaPipeController, LLMConnector
13. ✅ Windows standalone build — zero compile errors
14. ✅ GroqConfig API key set and functional

---

## Phase F — Developer Documentation

| # | File | Description | Status |
|---|------|-------------|--------|
| F1 | [`docs/developer-guide.md`](docs/developer-guide.md) | 12 sections covering architecture, singletons, scene flow, hand/face pipelines, camera, UI, IMiniGame interface, LLM, Shooter, build settings, troubleshooting | ✅ Done |

---

## Architecture Overview

```
Bootstrap.unity (index 0)
  ├── GameManager (singleton)
  ├── SceneLoader (singleton)
  ├── CameraFeedCtrl (singleton)
  ├── MediaPipeController (singleton)
  ├── LLMConnector (singleton)
  └── Shared UI Canvas (CameraOverlay, CameraConfigUI)
       └── Loads MainMenu on start
              │
              ▼
MainMenu.unity (index 1)
  ├── MainMenuController (with _lastScoreText for ShooterGame.LastScore)
  └── Button → "Shooter" → LoadScene(3)
              │
              ▼
MG_Shooter.unity (index 2)
  ├── ShooterGame (IMiniGame)
  │   └── Starts paused → Space to unpause
  │       └── OnEnd → LastScore static → LoadScene(1)
  ├── TargetManager
  │   └── Pre-placed targets with row labels (Easy/Medium/Hard)
  ├── GunController (hitscan, ammo, auto-reload, aim preview)
  ├── ShooterHandController
  │   ├── Hand3DProjector
  │   └── GestureDetector
  └── HUDController (timer + score + ammo + pause overlay)
      └── SetHUDVisible() hides/shows all elements on pause
```

---

*ARcade Rush · Implementation Plan · PUCV 2026*
