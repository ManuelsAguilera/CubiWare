# Remaining Tasks — Shooter Minigame

## Priority Order

### ✅ 1. Gun → Target Hit Detection (Completed 2026-05-10)
- [`GunController.Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:157) now performs hitscan from the muzzle transform
- `Physics.Raycast` from `_muzzleTransform` → detects `Target` component → calls `target.OnHit()`
- Fires `OnTargetHit(Target)` or `OnShotMissed()` events
- Aim preview sphere (green on target, red on miss) updates continuously, always visible
- Bullet trail (LineRenderer, 0.1s fade) drawn from muzzle to hit point
- Fire rate limiting via `_fireDelay` (0.3s) prevents spam
- **Scoring wired**: [`ShooterGame.DebugStartGame()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:56) subscribes to `OnTargetHit` → `HandleTargetHit()` adds score + updates HUD

### ✅ 2. Bullet Trail Visual (Completed 2026-05-10)
- Procedural `LineRenderer` created on each shot from muzzle to hit point or max distance
- Fades from opaque to transparent over `_bulletTrailDuration` (default 0.1s)
- Configurable material, width, color via Inspector

### ✅ 3. Fire Rate Limiting (Completed 2026-05-10)
- `_fireDelay` (default 0.3s) on `GunController`
- `_lastFireTime` tracked, `Shoot()` returns false if cooldown hasn't elapsed
- Dual-layer protection: `ShooterHandController._fireCooldown` + `GunController._fireDelay`

### ⚠️ ROOT CAUSE FOUND — Physics Disabled in Project Settings
- **The raycast always missed because 3D Physics was globally disabled** in Unity Project Settings (DynamicsManager)
- All `Physics.Raycast` calls silently returned false when physics is disabled at the project level
- The collider type mismatch (BoxCollider2D vs 3D Collider) was a secondary concern
- **Fix**: Enabled 3D Physics in Project Settings → Physics → "Enable Physics" and/or ensure the physics timestep/auto-simulation is checked
- After enabling physics, the hitscan + aim preview should function correctly
- **Still needed**: Add 3D BoxCollider to target prefabs (3D Physics.Raycast only detects 3D colliders)

### ⚠️ Known Issue — Collider Type Mismatch (Diagnosed 2026-05-10)
- Targets use `BoxCollider2D` (auto-generated from SpriteRenderer), but [`Target.cs:72`](Assets/Scripts/Minigames/Shooter/Target.cs:72) calls `GetComponent<Collider>()` which only finds 3D Colliders
- **Fix**: Add a 3D `BoxCollider` to each Target prefab in Unity Editor (remove existing `BoxCollider2D`)

---

### ✅ 4. Scoring Logic — Architectural Cleanup (Completed 2026-05-10)

**3 changes across 2 files:**

**Change 1 — [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs:149):** Removed `GameManager.Instance.AddScore(points)` from `OnHit()`
- Targets no longer own scoring responsibility — they only report hits via events

**Change 2 — [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:115):** Subscribed to `OnTargetHit` in Bootstrap path `OnStart()`
- Added `_gunController.OnTargetHit += HandleTargetHit;` after `OnReloadCompleted` subscription
- Makes Bootstrap path consistent with Debug path

**Change 3 — [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:296-300):** Updated `HandleTargetHit()` to report to GameManager in Bootstrap mode
- Debug mode (`_deps == null`): local `_score` + HUD only
- Bootstrap mode (`_deps?.GameManager != null`): also calls `_deps.GameManager.AddScore(target.HitScore)`
- Eliminates double-counting risk if both paths were accidentally wired

### ✅ 5. Aim Preview — Wire Visibility from ShooterGame + Sphere Size (Completed 2026-05-10)

**4 changes across 2 files:**

**Change 4 — [`ShooterGame.cs:58`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:58):** Activate preview in `DebugStartGame()`
**Change 5 — [`ShooterGame.cs:91`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:91):** Activate preview in `OnStart()` (Bootstrap path)
**Change 6 — [`ShooterGame.cs:130`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:130):** Deactivate preview in `OnEnd()`
**Change 7 — [`GunController.cs:517`](Assets/Scripts/Minigames/Shooter/GunController.cs:517):** Increased fallback sphere scale `0.15f` → `0.25f`

### ✅ Bug Fix — IndexOutOfRange in CoWaveProgression + Auto-Assign Target IDs (Completed 2026-05-10)

**Root Cause:** `_batchIntervals[wave]` accessed an index within `_rowOrder.Length` bounds but outside `_batchIntervals.Length` bounds when the three arrays had different lengths in the Inspector.

**Fix 1 — [`ShooterGame.cs:183`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:183):** Added `Mathf.Min` to compute `waveCount` from the shortest of all three arrays, preventing IndexOutOfRange regardless of Inspector misconfiguration.

**Fix 2 — [`Target.cs:24`](Assets/Scripts/Minigames/Shooter/Target.cs:24):** Removed `[SerializeField]` from `_targetId` — no longer manually assigned in Inspector. Added `AssignId(int)` method.

**Fix 3 — [`TargetManager.cs:114-124`](Assets/Scripts/Minigames/Shooter/TargetManager.cs:114):** `DiscoverTargets()` now auto-assigns sequential IDs (0, 1, 2...) per row based on discovery order, then sorts by the assigned ID for consistent round-robin behavior.

### 6. GameAudioController
- Create a new `GameAudioController.cs` (scene-local) that manages:
  - **Soundtrack** with 4 tracks: active game base, pause menu, intensity level 1/2/3
  - **SFX slots** for shoot sound + reload sound (let GunController play via the controller) methods `PlaySFX(AudioClip clip)`
  - Methods: `PlayMusic(GameMusicTrack track)`, `SetIntensity(int level)`
- Wire events: `ShooterGame` calls `SetIntensity()` as waves progress (Easy=1, Medium=2, Hard=3)
- Wire: GunController calls `PlaySFX(shootClip)` / `PlaySFX(reloadClip)` through the controller

### 7. UI — Wave Announcement and details
- Add `ShowWave(string waveName)` method to [`HUDController`](Assets/Scripts/UI/HUDController.cs)
- Shows a brief text overlay (e.g., "Wave: Easy", "Wave: Medium", "Wave: Hard") that fades after ~2 seconds
- Call from [`ShooterGame.CoWaveProgression()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:174) when a new wave starts
- When score goes up, the text of the score should shake.
- After the final wave the player should see a "Game Over" screen with the final score and a button to restart the game. or go to the main menu.

### 🏗️ Infrastructure — Bootstrap Additive Loading
Currently, the Bootstrap scene (index 0) must be the starting scene for Bootstrap-mode singletons (GameManager, SceneLoader, MediaPipeController, etc.) to exist. When launching the Shooter scene directly for debugging, these singletons are absent, so the Bootstrap path in [`ShooterGame.OnStart()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:87) cannot work.

**Fix plan (2 files, 1 new + 1 modified):**

**New file — [`Assets/Scripts/Core/BootstrapLoader.cs`](Assets/Scripts/Core/BootstrapLoader.cs):**
- A simple `MonoBehaviour` that checks if key Bootstrap singletons exist in `Awake()`
- If [`GameManager.Instance`](Assets/Scripts/Core/GameManager.cs:10) is null, load Bootstrap scene additively:
  ```csharp
  SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
  ```
- Place this component on a persistent GameObject in the Shooter scene (or use a ScriptableSingleton / bootstrapper pattern)

**Modification — [`SceneLoader.Start()`](Assets/Scripts/Core/SceneLoader.cs:22):**
- Current logic: `if (SceneManager.GetActiveScene().buildIndex == 0)` → auto-load MainMenu
- Problem: when Bootstrap is loaded additively, `GetActiveScene()` still returns the Shooter scene (not 0), so this won't trigger — which is correct behavior
- **However**, to be safe, add an additional check: `if (SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().buildIndex == 0)` — only auto-navigate when Bootstrap is the **only** loaded scene

**Flow when testing from Shooter scene directly:**
1. Shooter scene loads with `BootstrapLoader` component
2. `BootstrapLoader.Awake()` detects `GameManager.Instance == null`
3. Loads Bootstrap.additively → all singletons initialize with `DontDestroyOnLoad`
4. Singletons persist across scenes as usual
5. `ShooterGame.DebugStartGame()` can now use `GameManager.Instance` healthily, and the Bootstrap path in `OnStart()` works

### 8. MediaPipe Integration
- Replace current debug input in `ShooterHandController` / `ShooterGame` with real hand tracking
- Wire `GestureDetector` events (ThumbDown → Shoot, Reload appears automatically when bullets stop → Reload)
- Wire the hand and index finger landmarks as the vector that sets the aim for the gun. Make a gesture of when the indexfinger is mildly straight, and only point when this happens. Take the palm landmarks only on the line of the index finger and calculate the angle with all those landmarks.
- Change the position of the gun, so it moves up, down, left and right based on the hand position in the camera.
- Already partially set up: [`ShooterHandController`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) has `GestureDetector` via `[RequireComponent]`
- Test hand detection in the Shooter scene after basic gun/target flow works

## Current State (working)
- Target discovery and row grouping via [`TargetManager`](Assets/Scripts/Minigames/Shooter/TargetManager.cs)
- Wave progression (Easy→Medium→Hard) via [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs)
- Target raise animation, fall animation, timeout logic via [`Target`](Assets/Scripts/Minigames/Shooter/Target.cs)
- Score-based wave advancement
- Debug: `+` key to add score, `Start Game (Debug)` context menu
- Gun controller animation (shoot/reload sequences)
- **Hitscan from muzzle** — [`GunController.Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:157) raycasts from `_muzzleTransform` forward, detects `Target`, calls `OnHit()`, uses `Ray` class explicitly
- **Aim preview** — green/red sphere at hit point follows aim continuously, created in Awake, updates every frame. Needs visibility wiring from ShooterGame (start/end). Fallback sphere scale is `0.15f` — too small at distance, should be `0.25f`.
- **Bullet trail** — procedural `LineRenderer` fades 1→0 alpha over 0.1s from muzzle to hit point
- **Fire rate limiting** — 0.3s cooldown via `_fireDelay` + `_lastFireTime` in both `GunController` and `ShooterHandController`
- [`ShooterHandController.Fire()`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs:168) simplified — delegates entirely to `GunController.Shoot()`, no separate hitscan or bullet spawning. All non-raycast logs removed.
- **Target hit scoring** — [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:16) subscribes to [`GunController.OnTargetHit`](Assets/Scripts/Minigames/Shooter/GunController.cs:121) in `DebugStartGame()`, adds score via `HandleTargetHit()`. Bootstrap path uses `GameManager` events instead.
- [`Target.HitScore`](Assets/Scripts/Minigames/Shooter/Target.cs:51) — public property exposing the score value for the hit target type (bandit positive, innocent negative).
- **Logs reduced** — Only 6 `Debug.Log` calls remain across all shooter scripts
- **`_hitLayerMask`** is now `public LayerMask` — visible in Inspector and accessible from other scripts
- **Root cause resolved**: 3D Physics re-enabled in Project Settings

### ✅ Known Issue — Pitch Axis Bug (Fixed 2026-05-10)
- Reverted to original rotation: `Quaternion.Euler(0f, _debugYaw, _debugPitch)` — confirmed correct for this FBX model in a separate test scene. User confirmed direction is correct.

### ⚠️ Known Issue — Collider Type Mismatch (Diagnosed 2026-05-10)
- Targets use `BoxCollider2D` (auto-generated from SpriteRenderer), but [`Target.cs:72`](Assets/Scripts/Minigames/Shooter/Target.cs:72) calls `GetComponent<Collider>()` which only finds 3D Colliders
- **Fix**: Add a 3D `BoxCollider` to each Target prefab in Unity Editor (remove existing `BoxCollider2D`). Since 3D Physics is now enabled, the raycast will detect 3D colliders.
