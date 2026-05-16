# Shooter Minigame — Implementation Guide

> **Last updated:** 2026-05-10
> **Unity Version:** 2022.3 LTS (URP)  
> **Namespace:** `ARcadeRush.Minigames.Shooter`  

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [GunController](#2-guncontroller)
3. [ShooterHandController (Updated)](#3-shooterhandcontroller-updated)
4. [Prefab Setup (Unity Editor)](#4-prefab-setup-unity-editor)
5. [Debug Input Mode](#5-debug-input-mode)
6. [Animation Reference](#6-animation-reference)
7. [Integration Checklist](#7-integration-checklist)
8. [Ammo / Bullet System](#8-ammo--bullet-system-added-2026-05-08)
9. [Target System (Pre-Placed, Wave-Based)](#9-target-system-pre-placed-wave-based-added-2026-05-09)
10. [Game Flow — Start Menu, Pause, Game Over](#10-game-flow--start-menu-pause-game-over-added-2026-05-10)

---

## 1. Architecture Overview

```
Input Sources
  ├── MediaPipe Hand Tracking  →  ShooterHandController
  │                                │
  └── Debug (Mouse + Keyboard) →  GunController (when _useDebugInput=true)
                                     │
                                     ▼
                               GunController
                            (public API only)
                               │
                   ┌───────────┼───────────┐
                   ▼           ▼           ▼
               Cylinder    Cockpit/Hammer  Barrel Hinge
              (rotate)     (translate -Z)  (open/close)
                   └─────── Recoil Root ──────┘
                          (translate -Z)
```

The gun system has **two layers**:

| Layer | Component | Role |
|-------|-----------|------|
| **Input** | [`ShooterHandController`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) | Maps hand gestures → gun API calls |
| **Visual** | [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) | Pure visual controller, no MediaPipe dependency |

The [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) is completely self-contained. It can be placed in any scene, controlled by any input system, and tested in isolation via its debug input mode.

---

## 2. GunController

**File:** [`Assets/Scripts/Minigames/Shooter/GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs)

### 2.1 Public API

```csharp
public void Shoot();                    // Fire animation (cylinder, cockpit, recoil)
public void Reload();                   // Barrel hinge open/close
public void LookAt(Vector3 target);     // Smooth rotation toward world point
public bool IsShooting { get; }         // True during shoot animation
public bool IsReloading { get; }        // True during reload animation
```

### 2.2 Serialized Fields

All axis fields are in **local space** — configure them in the Inspector to match your FBX model's orientation.

| Category | Field | Default | Description |
|----------|-------|---------|-------------|
| Parts | `_cylinder` | — | GunPipe transform — rotates on shoot |
| Parts | `_barrelHinge` | — | Gun Barrel transform — tilts open on reload |
| Parts | `_cockpit` | — | GunCock transform — moves back on shoot |
| Parts | `_recoilRoot` | — | Root Gun transform — whole-gun recoil + LookAt |
| Cylinder | `_cylinderRotationAngle` | 60° | Degrees to snap-rotate the cylinder each shot |
| Cylinder | `_cylinderRotationAxis` | (0,0,1) | Local rotation axis — try (0,1,0) or (1,0,0) if wrong |
| Cockpit | `_cockpitTravel` | 0.05 | Distance cockpit moves (meters) |
| Cockpit | `_cockpitKickAxis` | (0,0,-1) | Local direction the cockpit kicks — use Back for recoil |
| Cockpit | `_cockpitDuration` | 0.12s | Total kick + return time |
| Recoil | `_recoilDistance` | 0.03 | Distance gun kicks back (meters) |
| Recoil | `_recoilKickAxis` | (0,0,-1) | Local direction the gun recoils — use Back for -Z |
| Recoil | `_recoilDuration` | 0.10s | Total recoil time |
| Barrel | `_barrelOpenAngle` | 45° | Barrel hinge open angle |
| Barrel | `_barrelOpenAxis` | (0,1,0) | Local rotation axis for barrel hinge (Up = opens left) |
| Barrel | `_barrelOpenDuration` | 0.30s | Time to open barrel |
| Barrel | `_barrelHoldDuration` | 0.20s | Time barrel stays open |
| Barrel | `_barrelCloseDuration` | 0.30s | Time to close barrel |
| LookAt | `_rotationSpeed` | 360°/s | Max rotation speed for LookAt |
| Muzzle | `_muzzleFlash` | null | Optional flash prefab to spawn |
| Muzzle | `_muzzleFlashDuration` | 0.05s | Flash visible duration |
| Hitscan | `_muzzleTransform` | null | Barrel tip transform — ray origin for hitscan |
| Hitscan | `_maxShootDistance` | 50 | Max raycast distance for hitscan |
| Hitscan | `_hitLayerMask` | -1 | Layers the hitscan ray intersects |
| Fire Rate | `_fireDelay` | 0.3s | Minimum time between shots |
| Aim Preview | `_aimPreviewPrefab` | null | Optional prefab (sphere) placed at aim hit point |
| Aim Preview | `_aimPreviewValidColor` | Green | Preview color when aiming at a valid Target |
| Aim Preview | `_aimPreviewInvalidColor` | Red | Preview color when aiming at nothing/invalid |
| Bullet Trail | `_bulletTrailMaterial` | null | Material for the procedural LineRenderer trail |
| Bullet Trail | `_bulletTrailDuration` | 0.1s | How long the trail line is visible |
| Bullet Trail | `_bulletTrailWidth` | 0.02 | Width of the trail line at the muzzle |
| Bullet Trail | `_bulletTrailColor` | White | Color of the trail line |
| Debug | `_useDebugInput` | true | Enable mouse/keyboard testing mode |

### 2.3 State Machine

```
         ┌──────────┐
  ┌──────│   IDLE   │──────┐
  │      └──────────┘      │
  │                        │
  ▼                        ▼
┌──────────┐          ┌──────────┐
│ SHOOTING │          │ RELOADING│
│ (0.12s)  │          │ (0.8s)   │
└────┬─────┘          └────┬─────┘
     │                     │
     ▼                     ▼
   IDLE                   IDLE
```

- `Shoot()` is ignored if `IsReloading` or `IsShooting`
- `Reload()` is ignored if `IsShooting` or `IsReloading`
- All animated transforms cache their rest pose in `Awake()` and restore final state after each animation
- Cylinder rotation uses `_cylinderRestRotation * Quaternion.AngleAxis(angle, _cylinderRotationAxis)` — local-space rotation around the configurable axis

### 2.3 New Events (Added 2026-05-10)

| Event | Signature | Fired When |
|-------|-----------|------------|
| `OnTargetHit` | `Action<Target>` | Hitscan ray hits a `Target` component |
| `OnShotMissed` | `Action` | Shot is fired but hits nothing (or non-target) |

These events are used by [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) for scoring and by [`HUDController`](Assets/Scripts/UI/HUDController.cs) for visual feedback. See [Section 10](#10-hitscan-system-added-2026-05-10).

### 2.4 Animation Timing (Shoot)

Three sequential phases:

```
Phase 1 (t=0.00s):     Muzzle flash activates + cylinder snap-rotates 60° on local axis
Phase 2 (t=0.00–0.02s): Brief gap — muzzle visible, cylinder in next chamber
Phase 3 (t=0.02s+):    Recoil + cockpit kick back concurrently (ease-out), then return (ease-in)
                        at 0.12s both fully returned → sequence complete
```

- Cylinder rotation is instant (snap) using `_cylinderRestRotation * Quaternion.AngleAxis(angle, axis)`
- Cockpit kicks along `_cockpitKickAxis` direction (default Back = -Z)
- Recoil kicks along `_recoilKickAxis` direction (default Back = -Z)
- Muzzle flash auto-destroys after `_muzzleFlashDuration`

### 2.5 Animation Timing (Reload)

```
Phase 1 (0.00–0.30s): Barrel hinge rotates open (ease-in-out)
Phase 2 (0.30–0.50s): Hold open
Phase 3 (0.50–0.80s): Barrel hinge rotates closed (ease-in-out)
```

---

## 3. ShooterHandController (Updated)

**File:** [`Assets/Scripts/Minigames/Shooter/ShooterHandController.cs`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs)

### 3.1 New Additions

| Member | Type | Purpose |
|--------|------|---------|
| `_gunController` | `[SerializeField] GunController` | Reference to the gun visual controller |
| `_aimTargetPoint` | `private Vector3` | Computed world target for the gun to look at |
| `Reload()` | `public void` | Delegates to `_gunController.Reload()` |

### 3.2 Modified Methods

| Method | Change |
|--------|--------|
| `Update()` | Now calls `_gunController.LookAt(_aimTargetPoint)` when aiming |
| `UpdateAimRay()` | Now computes `_aimTargetPoint` (hit point or far point along ray) |
| `Fire()` | Now calls `_gunController.Shoot()` before spawning bullet |

### 3.3 Wiring

```csharp
// In the scene, the Gun prefab is a child of the HandController:
HandController (ShooterHandController + Hand3DProjector + GestureDetector)
  └── Gun (GunController + Gun.fbx model)
```

The [`_gunController`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs:19) field is assigned via drag-and-drop in the Unity Inspector.

---

## 4. Prefab Setup (Unity Editor)

### 4.1 Required: Assign Transforms on [`Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab)

Open the existing [`Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab) in **Prefab Mode**:

1. Add the [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) component to the root `Gun` GameObject
2. Expand the FBX model child to see the hierarchy:

```
Gun (root)
  └── Gun (FBX model)
        ├── Gun Barrel    → Drag to _barrelHinge
        ├── GunCock       → Drag to _cockpit
        ├── GunHandler    → (not used, static)
        └── GunPipe       → Drag to _cylinder
```

3. Assign `Gun` (root) to `_recoilRoot`
4. Set `_useDebugInput` to `true` for testing, `false` for production
5. Configure animation parameters (defaults should work for initial testing)
6. Save prefab

### 4.2 Scene Setup

In the shooter scene, the `HandController` GameObject should have:

```
HandController
  ├── ShooterHandController (script)
  │     _gunController → drag Gun child here
  ├── Hand3DProjector (script)
  ├── GestureDetector (script)
  └── Gun (prefab instance)
        └── GunController (script) ← transforms pre-assigned in prefab
```

---

## 5. Debug Input Mode

When `_useDebugInput` is `true` on the [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs), the gun can be tested independently from MediaPipe:

| Input | Action | Description |
|-------|--------|-------------|
| **Left Click** | `Shoot()` | Fires the gun at cursor position |
| **`P`** | Toggle mouse aim | Switches mouse-aim on/off |
| **Mouse move** | `LookAt(hit)` | Raycasts from camera through cursor |
| **`R`** | `Reload()` | Triggers reload animation |

**Toggle behavior:**
- `P` key toggles `_debugInputEnabled` flag (starts `true` by default)
- When disabled (`false`), the gun still responds to programmatic calls from `ShooterHandController`
- Allows seamless switching: test with mouse → press `P` → hand tracking takes over
**Edge cases:**
- Raycasting always uses `Camera.main` — independent of any child camera on the gun
- Left-click is blocked during active animations (same guard as `Shoot()`)


---

## 6. Animation Reference

### 6.1 FBX Bone Mapping

| FBX Transform | GunController Field | Animation | Axis |
|---------------|-------------------|-----------|------|
| `Gun Barrel` | `_barrelHinge` | Barrel tilt (reload) | Rotate around local **right** axis |
| `GunCock` | `_cockpit` | Cockpit kick (shoot) | Translate along local **forward** (negative) |
| `GunPipe` | `_cylinder` | Cylinder rotation (shoot) | Rotate around configurable `_cylinderRotationAxis` (default: local **forward**) |
| `Gun` (root) | `_recoilRoot` | Recoil + LookAt | Translate along local **forward** (negative) |

### 6.2 Easing Functions

All animations use cubic easing for natural motion:

| Easing | Used For | Formula |
|--------|----------|---------|
| `EaseOutCubic` | Recoil kick-out, cockpit go-back | `1 - (1-t)³` |
| `EaseInCubic` | Recoil return, cockpit return | `t³` |
| `EaseInOutCubic` | Barrel hinge open/close | `4t³` if t<0.5 else `1 - (-2t+2)³/2` |

### 6.3 Default Timing Values

```
Shoot total:     ~0.12s
Reload total:    ~0.80s
LookAt speed:    360°/s
```

---

## 7. Integration Checklist

- [ ] [`GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs) created with full public API
- [ ] [`ShooterHandController.cs`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) updated with `_gunController` reference
- [ ] `Shoot()` called in `Fire()` — GunController handles hitscan internally
- [ ] `LookAt()` called in `Update()` when aiming
- [ ] `Reload()` exposed as public method on `ShooterHandController`
- [ ] [`Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab) updated with `GunController` component
- [ ] Transforms assigned: Gun Barrel → `_barrelHinge`, GunCock → `_cockpit`, GunPipe → `_cylinder`, Gun root → `_recoilRoot`
- [ ] Muzzle transform assigned: empty child at barrel tip → `_muzzleTransform`
- [ ] `_useDebugInput` set to `false` for production builds
- [ ] HandController scene hierarchy includes Gun as child with `_gunController` reference wired
- [ ] Test: Left-click shoots with debug input → cylinder rotates, cockpit kicks, barrel recoils
- [ ] Test: `R` key triggers reload → barrel hinges open/close
- [ ] Test: `P` toggles mouse aim → gun follows cursor when on
- [ ] Test: Hand tracking → gun follows aim ray, fist fires, thumb-down toggles safety
- [ ] Test: Aim preview sphere shows green on target, red on miss
- [ ] Test: Bullet trail appears on each shot from muzzle to hit point
- [ ] Test: Fire rate limited — spam-click only fires every 0.3s
- [ ] Test: Hitscan detects Target and calls `OnHit()`
- [ ] HUDController: `_pauseOverlay` (GameObject) and `_pauseText` (TMP_Text) assigned in Inspector
- [ ] HUDController: `SetHUDVisible(bool)`, `ShowPauseOverlay(string)`, `HidePauseOverlay()` methods implemented
- [ ] MainMenuController: `_lastScoreText` (TMP_Text) assigned to display `ShooterGame.LastScore`
- [ ] Game starts paused — "PRESS SPACE TO START" overlay shown, HUD hidden
- [ ] Test: Press Space → HUD appears, wave progression and timer begin
- [ ] Test: GameManager pause → HUD hides, pause overlay shows "PAUSED"
- [ ] Test: GameManager resume → HUD shows, pause overlay hides
- [ ] Test: Timer expires → returns to MainMenu (identical layout), `LastScore` displayed

---

## 8. Ammo / Bullet System (Added 2026-05-08)

### 8.1 Overview

The revolver holds **6 bullets** (one per chamber). The [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) manages ammo internally — it tracks the current count, decrements on each shoot, and **auto-triggers the reload animation** when all 6 bullets are spent.

### 8.2 New Fields on GunController

| Category | Field | Type | Default | Description |
|----------|-------|------|---------|-------------|
| Ammo | `_maxAmmo` | `int` | 6 | Revolver capacity |
| Ammo | `_autoReloadOnEmpty` | `bool` | `true` | Auto-reload when ammo reaches 0 |

### 8.3 New Public API on GunController

```csharp
public int  CurrentAmmo { get; }            // Current bullet count
public int  MaxAmmo     { get; }            // Max capacity
public bool IsEmpty     { get; }            // currentAmmo <= 0

// Events
public event Action<int, int> OnAmmoChanged;      // (current, max)
public event Action          OnOutOfAmmo;          // Fired at 0
public event Action          OnReloadStarted;      // Reload begins
public event Action          OnReloadCompleted;    // Reload done + ammo refilled
```

### 8.4 Modified Methods

| Method | Change |
|--------|--------|
| [`Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:82) | Now returns `bool` — `true` if shot fired, `false` if empty/reloading |
| [`Reload()`](Assets/Scripts/Minigames/Shooter/GunController.cs:92) | Now returns `bool` — returns `false` if already at max ammo |
| [`ReloadSequence()`](Assets/Scripts/Minigames/Shooter/GunController.cs:203) | Refills ammo to `_maxAmmo` after barrel close + fires `OnReloadCompleted` |
| [`Awake()`](Assets/Scripts/Minigames/Shooter/GunController.cs:114) | Initializes `_currentAmmo = _maxAmmo` |
| [`ShootSequence()`](Assets/Scripts/Minigames/Shooter/GunController.cs:128) | No changes (animation logic stays the same) |

### 8.5 Updated State Machine

```
         ┌──────────┐
  ┌──────│   IDLE   │──────┐
  │      │ (6/6)    │      │
  │      └──────────┘      │
  │     (ammo>0)           │ (ammo==0 → auto-reload)
  │                        │
  ▼                        ▼
┌──────────┐          ┌──────────┐
│ SHOOTING │          │ RELOADING│
│ (-1 ammo)│          │ (+6 ammo)│
└────┬─────┘          └────┬─────┘
     │                     │
     ▼                     ▼
   IDLE                   IDLE
   (ammo-n)               (6/6)
```

### 8.6 Auto-Reload Behavior

1. Player shoots last bullet → `_currentAmmo` drops to 0 → `OnOutOfAmmo` fires
2. Next call to `Shoot()` detects `_currentAmmo <= 0`
3. If `_autoReloadOnEmpty` is `true`, calls `Reload()` automatically
4. `ReloadSequence()` plays barrel open → hold → close (0.8s total)
5. After barrel closes, `_currentAmmo = _maxAmmo`, `OnReloadCompleted` fires
6. Gun returns to IDLE with full ammo

### 8.7 ShooterHandController Integration

[`ShooterHandController.Fire()`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs:154) now checks the return value of `_gunController.Shoot()`:

- If **`true`** — bullet was fired → spawn projectile / hitscan normally
- If **`false`** — gun was empty (or reloading) → skip bullet spawn, just reset cooldown

### 8.8 HUD Integration

The [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) subscribes to `GunController` ammo events and relays them to the `HUDController`:

| HUD Method | Purpose |
|------------|---------|
| `UpdateAmmo(int current, int max)` | Display "5/6" etc. |
| `ShowReloading(bool)` | Show/hide "RELOADING..." indicator |
| `SetHUDVisible(bool)` | Toggle all HUD elements (timer, score, ammo, reload, music, emotion, wave announcement) — used on pause/resume |
| `ShowPauseOverlay(string)` | Show pause overlay with custom message (e.g. "PRESS SPACE TO START", "PAUSED") |
| `HidePauseOverlay()` | Hide the pause overlay |

### 8.9 Timer

The **game timer** (90s countdown) is implemented in [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs):
- [`_gameDuration`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:21) = 90 (serialized in Inspector)
- [`TimerTick()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:90) called every second via `InvokeRepeating`
- [`HandleTimeout()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:123) → `OnEnd()` → returns to MainMenu

**Timing nuance:** The timer does **not** start immediately on `OnStart()`. It is deferred to [`BeginGame()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:387) — which is called when the player presses Space to unpause for the first time. This prevents time from elapsing while the player sees the "PRESS SPACE TO START" overlay.

The timer and ammo system are **independent**: the countdown continues regardless of ammo state. The player must manage both time pressure and bullet conservation.

### 8.10 Scoring Wiring

When a target is hit, score propagation follows two paths depending on how the game was started. Both paths now start the game in a **paused state** — the player must press Space to unpause before scoring begins:

**DebugStartGame() path** (right-click → "Start Game (Debug)"):
1. [`GunController.PerformHitscan()`](Assets/Scripts/Minigames/Shooter/GunController.cs:579) → raycast hits Target → calls `target.OnHit()` → fires `OnTargetHit` event
2. [`ShooterGame.HandleTargetHit()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:362) subscribes to `OnTargetHit` → adds `target.HitScore` to `_score` → updates HUD
3. [`Target.OnHit()`](Assets/Scripts/Minigames/Shooter/Target.cs:125) → fall animation → `OnTargetDeactivated` → TargetManager cleans up
4. Note: `GameManager.Instance` is `null` in this path, so `Target.OnHit()` skips direct GameManager scoring

**OnStart() path** (full Bootstrap/GameManager flow):
1. Same raycast/hitscan flow
2. [`ShooterGame.HandleTargetHit()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:362) adds `target.HitScore` to `_score` → updates HUD
3. Also calls `_deps.GameManager.AddScore(target.HitScore)` for global GameManager tracking → fires `OnScoreChanged`
4. No double-scoring — `HandleTargetHit()` is the single entry point in both paths

### 8.11 Integration Checklist

- [ ] GunController: `_maxAmmo` = 6, `_currentAmmo` starts at 6
- [ ] GunController: `Shoot()` returns `bool`, decrements ammo
- [ ] GunController: Auto-reload when `_currentAmmo` reaches 0
- [ ] GunController: Ammo refilled to max after reload animation
- [ ] GunController: Events wired: `OnAmmoChanged`, `OnOutOfAmmo`, `OnReloadStarted`, `OnReloadCompleted`
- [ ] ShooterHandController: `Fire()` checks `Shoot()` return value before spawning bullet
- [ ] ShooterGame: Subscribes to GunController events, relays to HUD
- [ ] HUDController: `UpdateAmmo()` and `ShowReloading()` methods exist
- [ ] Timer: 90s countdown works independently (no regression)
- [ ] Test: Shoot 6 times → auto-reload triggers → 6 more shots possible
- [ ] Test: R key during full ammo → no-op (reload ignored)
- [ ] Test: Shoot during reload → blocked (existing guard)
- [ ] Test: Timer expires at any ammo state → game ends correctly

---

## 9. Target System (Pre-Placed, Wave-Based) — Added 2026-05-09

### 9.1 Overview

Targets are **pre-placed** in the scene by the designer (not dynamically spawned). Each target is assigned a **row label** (difficulty descriptor like `"Easy"`, `"Medium"`, `"Hard"`) via Inspector. The [`TargetManager`](Assets/Scripts/Minigames/Shooter/TargetManager.cs) discovers all targets at runtime, groups them by row label, and provides batch activation. The [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) acts as the **controller**, progressing through rows in waves as the player scores points.

All targets start **disabled** (`activeSelf = false`). They are activated by `TargetManager` when a batch is triggered.

### 9.2 Architecture

```
MG_Shooter.unity
├── ShooterGameController
│   └── ShooterGame.cs (IMiniGame — controls wave pacing)
├── TargetManager (GameObject)
│   └── TargetManager.cs — groups targets, activates batches
├── Targets (empty parent — optional)
│   ├── Target_Easy_01 (disabled)
│   │   └── Target.cs — rowLabel="Easy", targetId=1
│   ├── Target_Easy_02 (disabled)
│   │   └── Target.cs — rowLabel="Easy", targetId=2
│   ├── Target_Med_01 (disabled)
│   │   └── Target.cs — rowLabel="Medium", targetId=1
│   ├── … (more targets)
└── HandController (unchanged)
```

### 9.3 Target.cs

**File:** [`Assets/Scripts/Minigames/Shooter/Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs)

#### Key Changes from Original

| Change | Detail |
|--------|--------|
| **Row label** | New `[SerializeField] private string _rowLabel` — set per-target in Inspector, must match a `RowConfig` label in `TargetManager` |
| **Target ID** | New `[SerializeField] private int _targetId` — for ordering within a row |
| **Type randomization** | Type is no longer pre-assigned. Each `Activate()` call randomises 50/50 Bandit/Innocent |
| **Raise animation** | New `CoRaiseAndActivate()` — exact reverse of the fall animation. Target rises from the bottom (fallen) position to upright |
| **Timeout** | New `CoActiveCountdown()` — auto-deactivates target after its `_activeDuration` expires |
| **Event** | New `System.Action<Target> OnTargetDeactivated` — fired when target finishes deactivation (shot or timeout), so `TargetManager` can recycle it |
| **Fall on timeout** | Targets that time out play the same fall animation as shot targets (no score awarded) |

#### Serialized Fields

Field | Type | Default | Description |
|-------|------|---------|-------------|
`_rowLabel` | `string` | `"Easy"` | Must match a `TargetManager` RowConfig label |
`_targetId` | `int` | `0` | Unique ID for ordering within a row |
`_banditScore` | `int` | `10` | Score awarded for hitting a bandit (overridden by TargetManager on activate) |
`_innocentScore` | `int` | `-20` | Score deducted for hitting an innocent (overridden by TargetManager on activate) |
`_banditSprite` | `Sprite` | — | Sprite assigned when type is Bandit |
`_innocentSprite` | `Sprite` | — | Sprite assigned when type is Innocent |
`_hitEffectPrefab` | `GameObject` | — | Particle effect prefab spawned on hit |
`_hitEffectDuration` | `float` | `1` | Seconds before the hit effect is destroyed |
`_raiseDuration` | `float` | `0.5` | Duration of the raise animation |
`_fallDuration` | `float` | `0.5` | Duration of the fall animation |
`_poolReturnDelay` | `float` | `0.8` | Total time from fall start to deactivation |
`_spriteHalfHeight` | `float` | `1` | Half the sprite height — pivot sits this far below center |
`_spriteRenderer` | `SpriteRenderer` | — | **`[SerializeField]`** — must be assigned in the Inspector. Previously obtained via `GetComponent<SpriteRenderer>()` in `Awake()`, but now that `Target` lives on the parent prefab GameObject (separate from the child `SpriteRenderer`), this is an explicit reference to the child's renderer. |

#### Public API

```csharp
public void Activate(int banditScore, int innocentScore, float activeDuration);
public void OnHit();
public void Deactivate();

public string RowLabel { get; }
public int TargetId { get; }
public TargetType Type { get; }
public bool IsActive { get; }
public event System.Action<Target> OnTargetDeactivated;
```

#### Target Lifecycle

```
Disabled (start)
    │
    ▼
Activate(banditScore, innocentScore, activeDuration)
    │  gameObject.SetActive(true)
    ▼
OnEnable → Randomise type 50/50
         → Apply sprite
         → Position at fall-end (bottom)
         → Start CoRaiseAndActivate()
    │
    ▼
Raise animation completes (0.5s)
    │  Enable collider
    │  Start timeout countdown
    ▼
┌──────────────────────────────────────┐
│            ACTIVE STATE              │
│  (collider enabled, can be shot)     │
└──────────────────────────────────────┘
    │                              │
    ▼ (shot)                       ▼ (timeout)
OnHit()                           CoActiveCountdown()
    │  Disable collider               │  Disable collider
    │  Award score                    │  No score
    │  Play hit effect                │
    ▼                              ▼
CoFallAndDeactivate()             CoFallAndDeactivate()
    │  Fall animation (0.5s)          │  Fall animation (0.5s)
    │  Wait poolReturnDelay           │  Wait poolReturnDelay
    │  Fire OnTargetDeactivated       │  Fire OnTargetDeactivated
    │  SetActive(false)               │  SetActive(false)
    ▼                              ▼
Disabled (available for next batch)
```
                     │
                     ▼ (external code disables GameObject directly)
               OnDisable() called by Unity
                     │
               ┌─────┴─────┐
               │           │
           IsActive=true  IsActive=false
               │           (normal path — ignored)
               ▼
           IsActive = false
           Fire OnTargetDeactivated(this)
           StopAllCoroutines()
           (Safety net — ensures TargetManager is notified
            even if CoFallAndDeactivate was interrupted)


### 9.3.1 OnDisable Safety Net (Added 2026-05-10)

A bug was found where a target that receives a hit but is hidden (via `gameObject.SetActive(false)`) does not always notify `TargetManager`, causing the score to not update.

**Root cause:** When `gameObject.SetActive(false)` is called — either by [`CoFallAndDeactivate()`](Assets/Scripts/Minigames/Shooter/Target.cs:231) at line 256, or by external code (e.g., [`TargetManager.DeactivateAll()`](Assets/Scripts/Minigames/Shooter/TargetManager.cs), scene cleanup) — Unity triggers [`OnDisable()`](Assets/Scripts/Minigames/Shooter/Target.cs:98), which calls `StopAllCoroutines()`. In the normal hit path, `OnTargetDeactivated` fires *before* `gameObject.SetActive(false)` at line 255. However, if external code disables the `GameObject` directly, `OnDisable()` runs without the `OnTargetDeactivated` event ever being fired, leaving `TargetManager` believing the target is still active.

**Fix:** Added a safety net in [`Target.OnDisable()`](Assets/Scripts/Minigames/Shooter/Target.cs:98-116):

```csharp
private void OnDisable()
{
    // Safety net: if the target was active and is being disabled
    // (by any code path — normal deactivation, game end, scene unload, etc.),
    // notify the TargetManager so it can recycle this target.
    // Without this, any interruption to CoFallAndDeactivate() would
    // leave the TargetManager believing this target is still active.
    if (IsActive)
    {
        IsActive = false;
        OnTargetDeactivated?.Invoke(this);
    }

    StopAllCoroutines();
    _raiseCo = null;
    _fallCo = null;
    _timeoutCo = null;
    _isAnimating = false;
}
```

This ensures [`TargetManager.HandleTargetDeactivated()`](Assets/Scripts/Minigames/Shooter/TargetManager.cs) is always invoked when a target disables, regardless of code path.

### 9.4 TargetManager.cs

**File:** [`Assets/Scripts/Minigames/Shooter/TargetManager.cs`](Assets/Scripts/Minigames/Shooter/TargetManager.cs)

#### Row Configuration

Configured as an array in the Inspector:

| Field | Type | Description |
|-------|------|-------------|
| `label` | `string` | Must match `Target._rowLabel` values |
| `mode` | `ActivationMode` | `Fixed` = fixed number per batch, `Percentage` = percentage of row |
| `fixedCount` | `int` | Used when mode = Fixed |
| `percentage` | `float` | Used when mode = Percentage (0.0–1.0) |
| `banditScore` | `int` | Points awarded for hitting a bandit in this row |
| `innocentScore` | `int` | Points deducted for hitting an innocent in this row |
| `activeDuration` | `float` | Seconds each target stays active before auto-deactivating |
| `activationCooldown` | `float` | Minimum seconds between activation batches for this row |

#### Scoring Per Row

| Row | Bandit | Innocent |
|-----|--------|----------|
| Easy | +5 | -10 |
| Medium | +10 | -10 |
| Hard | +20 | -15 |

#### Scene Target Discovery

`TargetManager.Awake()` discovers targets using one of two methods:

1. **Parent transform scan** — If `_targetParent` is assigned in the Inspector, scans all children for `Target` components
2. **Full scene scan** — If `_targetParent` is null, uses `FindObjectsByType<Target>(FindObjectsInactive.Include)` to find all targets in the scene (including disabled ones)

Targets are then grouped by `_rowLabel` and sorted by `_targetId` within each group.

#### Round-Robin Activation

`ActivateBatch(rowLabel)` picks targets sequentially using a round-robin index, ensuring fair distribution:

```
Row "Easy" has 4 targets: [A, B, C, D]
  Batch 1: pick 2 → A, B  (index advances to 2)
  Batch 2: pick 2 → C, D  (index advances to 4, wraps to 0)
  Batch 3: pick 2 → A, B
```

#### Public API

| Method | Description |
|--------|-------------|
| `DiscoverTargets()` | (Re-)scans scene for all `Target` components and groups by row label |
| `ActivateBatch(string rowLabel)` | Activates next batch from the given row (round-robin). Returns false if unavailable |
| `GetActiveTargets(string rowLabel)` | Returns currently active targets in a row |
| `GetAvailableCount(string rowLabel)` | How many inactive targets remain in a row |
| `GetTotalCount(string rowLabel)` | Total targets (active + inactive) in a row |
| `DeactivateTarget(Target target)` | Force-deactivate a specific target |
| `DeactivateAll()` | Deactivate all active targets across all rows |

### 9.5 ShooterGame.cs — Wave Progression

**File:** [`Assets/Scripts/Minigames/Shooter/ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs)

#### Wave Flow

```
DebugStartGame() or OnStart(deps)
  │
  └─► Start in PAUSED state
        │  _isPlaying = true
        │  _gameReady = false
        │  _isGamePaused = true
        │  HUD hidden
        │  Show "PRESS SPACE TO START" overlay
        │  Timer and wave progression NOT started yet
        │
        ▼
   Player presses Space
        │
        ├── _gameReady == false
        │   └─► BeginGame()
        │         │  Set _gameReady = true, _isGamePaused = false
        │         │  Hide pause overlay
        │         │  Show HUD
        │         │  Start CoWaveProgression()
        │         │  Start timer (InvokeRepeating TimerTick)
        │         ▼
        │
        └── _gameReady == true (subsequent pause/resume)
            └─► GameManager.ResumeGame()
                  │  Time.timeScale = 1
                  │  HandleGameResumed()
                  │  HUD shown, pause overlay hidden
                  ▼
   Wave 1: "Easy" row
     │  Every 3s: ActivateBatch("Easy")
     │  Until: score >= 30
     │  Brief transition pause
     ▼
   Wave 2: "Medium" row
     │  Every 2.5s: ActivateBatch("Medium")
     │  Until: score >= 70
     │  Brief transition pause
     ▼
   Wave 3: "Hard" row
     │  Every 2s: ActivateBatch("Hard")
     │  Until: timer expires
     ▼
   OnEnd()
     │  Store LastScore = _score (static, for MainMenu display)
     │  Unsubscribe from events
     │  DeactivateAll targets
     │  Stop timer and wave coroutine
     │  LoadScene(_mainMenuSceneIndex) → returns to MainMenu
```

#### Serialized Fields (Wave Settings)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `_rowOrder` | `string[]` | `["Easy", "Medium", "Hard"]` | Wave order |
| `_scoreThresholds` | `int[]` | `[30, 70, int.MaxValue]` | Score target to advance to next wave |
| `_batchIntervals` | `float[]` | `[3f, 2.5f, 2f]` | Seconds between batch activations in each wave |
| `_waveTransitionDelay` | `float` | `1f` | Pause between waves |

### 9.5.1 Debug Entry Point

A [`DebugStartGame()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:45) method is provided for testing:

- **Via Inspector**: Right-click the `ShooterGame` component and select `"Start Game (Debug)"`.
- **Via UI Button**: Drag the `ShooterGame` GameObject onto a Button's `OnClick` event and select `ShooterGame → DebugStartGame()`.
- Creates a `MiniGameDependencies` from the singleton `GameManager.Instance` and calls `OnStart()`.
- Prevents double-start if the game is already running.
- **Start-paused behavior**: After `DebugStartGame()`, the game shows a "PRESS SPACE TO START" overlay with HUD hidden. The player must press Space to begin playing.

### 9.5.2 Pause / Resume Flow

The game has two distinct pause states:

| State | Trigger | HUD Visible | Pause Overlay | Timer/Wave Running |
|-------|---------|-------------|---------------|-------------------|
| Initial paused | `OnStart()` / `DebugStartGame()` | Hidden | "PRESS SPACE TO START" | No |
| GameManager paused | `GameManager.PauseGame()` event | Hidden | "PAUSED" | Frozen (Time.timeScale = 0) |

**Unpause logic** in [`Update()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:405):
- If `_gameReady == false` → first-time unpause → calls [`BeginGame()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:387)
- If `_gameReady == true` → subsequent pause → calls `GameManager.ResumeGame()` → `HandleGameResumed()`

### 9.5.3 Game Over (Legacy — Replaced by Section 14)

> **This section is obsolete.** The game over flow now uses an in-scene start menu panel instead of loading the MainMenu scene.
> See [Section 14 — Game Flow](docs/shooter_implementation.md:1104) for the current implementation.

**Previous behavior (replaced):**
- [`OnEnd()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:155) stored `LastScore` and loaded the MainMenu scene
- The MainMenu scene's [`MainMenuController`](Assets/Scripts/UI/MainMenuController.cs) read `ShooterGame.LastScore` in `Start()`
- The legacy `_gameOverPanel`, `ShowGameOver()`, `HideGameOver()`, and `CreateGameOverPanel()` methods on [`HUDController`](Assets/Scripts/UI/HUDController.cs) have been removed

### 9.6 Scene Setup (Unity Editor)

1. Create an empty `Targets` parent GameObject in the scene
2. Create child GameObjects for each target (Primitives with sprites, or sprite-based GameObjects)
3. Add the `Target` component to each child
4. Set `_rowLabel` (e.g. `"Easy"`, `"Medium"`, `"Hard"`), `_targetId`, and assign sprites
5. **Leave all target GameObjects disabled** (`activeSelf = false`)
6. Create a `TargetManager` GameObject, add the `TargetManager` script
7. Configure the `Row Configs` array — one entry per row, matching the labels and scoring
8. Optionally assign the `Targets` parent to `_targetParent` for scoped discovery
9. In `ShooterGameController`, assign the `_targetManager` reference

### 9.7 Target Animation Reference

#### Raise Animation (Activation)

The raise animation is the **exact reverse** of the fall animation:

```
Start:  Rotated 90° around right axis, at bottom pivot point
End:    Upright (rotation identity), at _startPosition
Duration: _raiseDuration (default 0.5s)
Easing: SmoothStep
```

#### Fall Animation (Hit or Timeout)

```
Start:  Upright at _startPosition
End:    Rotated 90° around right axis, at bottom pivot point
Duration: _fallDuration (default 0.5s)
Easing: SmoothStep
+ Wait: _poolReturnDelay - _fallDuration, then deactivate
```

Both animations use the same pivot-based math:
- Pivot = `_startPosition - transform.up * _spriteHalfHeight`
- Rotation axis = `transform.right`
- The object rotates around the pivot point

### 9.8 Integration Checklist

- [ ] [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs) modified — `_rowLabel`, `_targetId`, `Activate()`, raise animation, timeout, `OnTargetDeactivated`
- [ ] [`TargetManager.cs`](Assets/Scripts/Minigames/Shooter/TargetManager.cs) created — scene discovery, row grouping, round-robin activation, cooldowns
- [ ] [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) rewritten — `TargetManager` reference, wave progression, score thresholds
- [ ] Targets placed in scene, each with `_rowLabel` matching a `RowConfig`
- [ ] All target GameObjects start disabled
- [ ] TargetManager RowConfigs configured: Easy (+5/-10, 4s active), Medium (+10/-10, 3s), Hard (+20/-15, 2.5s)
- [ ] Test: Wave 1 activates Easy targets, score progresses, transitions to Medium at 30pts
- [ ] Test: Missing a target → it plays fall animation and deactivates after timeout
- [ ] Test: Hitting a target → correct score awarded per row, fall animation plays
- [ ] Test: All targets in a row exhausted → ActivateBatch returns false, waits for availability
- [ ] Test: Timer expires mid-wave → OnEnd called, DeactivateAll(), returns to MainMenu
- [ ] Test: Game starts paused with "PRESS SPACE TO START" overlay, HUD hidden
- [ ] Test: Press Space → timer begins, wave progression starts, HUD appears
- [ ] Test: GameManager pause → HUD hidden, "PAUSED" overlay shown
- [ ] Test: GameManager resume → HUD shown, overlay hidden
- [ ] Test: Game ends → MainMenu loads with `LastScore` displayed
- [ ] HUDController: `_pauseOverlay` GameObject and `_pauseText` TMP_Text assigned in Inspector
- [ ] MainMenuController: `_lastScoreText` TMP_Text assigned to display final score
- [ ] ShooterGame: `_unpauseKey` serialized (default Space), configurable in Inspector

---

## 10. Hitscan System (Added 2026-05-10)

### 10.1 Overview

Hit detection is handled by [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) using **hitscan** (instant raycast), not physics projectiles. When [`Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:117) is called, it performs a `Physics.Raycast` from `_muzzleTransform` along its forward direction.

The [`ShooterHandController`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) delegates entirely to [`GunController.Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:117) — it no longer spawns bullet objects or performs its own hitscan.

### 10.2 Data Flow

```
Hand/Gesture → ShooterHandController.Fire()
                      │
                      ▼
              GunController.Shoot()
                      │
              ┌───────┴───────┐
              ▼               ▼
        Ammo check       PerformHitscan()
        (decrement)           │
              │         ┌─────┴─────┐
              │         ▼           ▼
              │    Hit Target    Miss/nothing
              │    target.OnHit() OnShotMissed
              │    OnTargetHit      event
              │      event
              │         │
              ├─────────┘
              │
              ▼
      ShooterGame.HandleTargetHit()
      → adds score, updates HUD

              │
              ▼
        ShowBulletTrail()
        (LineRenderer fade)
```

### 10.3 Muzzle Transform

[`_muzzleTransform`](Assets/Scripts/Minigames/Shooter/GunController.cs:61) is an empty child placed at the barrel tip in the Gun prefab. If unassigned, it falls back to the GunController's own transform position/forward.

**To set up:**
1. Open the Gun prefab in Prefab Mode
2. Create an empty child under the FBX model at the barrel end
3. Name it "Muzzle" and position it at the barrel opening
4. Assign it to `_muzzleTransform` on the `GunController` component

### 10.4 Hit Layer Mask

[`_hitLayerMask`](Assets/Scripts/Minigames/Shooter/GunController.cs:63) defaults to `Everything` (-1). To limit hitscan to specific layers (e.g., only `Target` layer), configure in the Inspector.

### 10.4.1 Required: 3D Collider on Targets

Each `Target` GameObject **must** have a **3D `BoxCollider`** (or other 3D `Collider`) component. The code uses `GetComponent<Collider>()` which only finds 3D colliders.

**If targets use `BoxCollider2D`** (auto-generated from SpriteRenderer's "Sprite Cast" or "Generate Physics Shape"):
- `GetComponent<Collider>()` returns `null` — `_collider.enabled = false/true` is silently ignored
- `Physics.Raycast` (3D) can **never** detect the target
- The `Physics2D.Raycast` fallback exists but requires correct ray direction

**Setup in Unity Editor:**
1. Select each Target GameObject
2. Remove the auto-generated `BoxCollider2D` (if present, it's uneditable)
3. Add a `BoxCollider` (3D) component
4. Size it to match the sprite visual bounds
5. Ensure "Is Trigger" is **UNCHECKED**
6. The diagnostic log in `[Target.Awake()](Assets/Scripts/Minigames/Shooter/Target.cs:70)` will confirm the collider type on scene load

### 10.4.2 Debug Input Rotation Fix

The debug input mode in `[HandleDebugInput()](Assets/Scripts/Minigames/Shooter/GunController.cs:464)` applies yaw/pitch rotations to the gun root:

```csharp
// CORRECT: pitch in X, yaw in Y, roll = 0
root.localRotation = Quaternion.Euler(-_debugPitch, _debugYaw, 0f);
```

**Note:** `Quaternion.Euler(0f, yaw, pitch)` would apply `pitch` as **roll** (Z-axis), not pitch (X-axis), causing the gun to rotate sideways instead of up/down. The fix ensures pitch is on the X-axis.

### 10.5 Events

| Event | Description |
|-------|-------------|
| `GunController.OnTargetHit(Target)` | Called when a Target is hit. Used by ShooterGame for scoring. |
| `GunController.OnShotMissed()` | Called when a shot hits nothing. |

### 10.6 Debug Logging

Only raycast-related logs are enabled by default:

| Log | When | Purpose |
|-----|------|---------|
| `[GunController] Hitscan: origin=... dir=... hit=... point=... target=...` | Each shot, on hit | Verifies ray origin, direction, hit collider, and target detection |
| `[GunController] Hitscan: origin=... dir=... miss (max dist) point=...` | Each shot, on miss | Verifies ray direction when nothing is hit |
| `[Target] Bandit/Innocent (Row) hit! Score: X` | Target hit | Confirms target detection and score value |
| `[ShooterGame] Target hit! Score: X (total: Y)` | Target hit | Confirms score propagation to game state |

All non-raycast `Debug.Log` calls (safety toggles, fire notifications, wave progression, debug input diagnostics, setup logs) have been removed to reduce noise.

### 10.6 ShooterHandController Changes

The [`Fire()`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs:168) method is simplified:

```csharp
private void Fire()
{
    _lastFireTime = Time.time;
    _canFire = false;

    // GunController handles hitscan internally
    bool shotFired = _gunController != null && _gunController.Shoot();

    if (!shotFired)
    {
        Debug.Log("[ShooterHand] Gun empty or reloading — no shot fired.");
    }

    Invoke(nameof(ResetFireCooldown), _fireCooldown);
}
```

Removed: `_bulletPrefab`, `_bulletSpawnPoint`, `_bulletSpeed`, `PerformHitscan()`.

---

## 11. Aim Preview (Added 2026-05-10)

### 11.1 Overview

A visual indicator shows where the gun is currently aiming. It is a small sphere (or a custom prefab) placed at the hitscan hit point. Color changes based on target validity:

- **Green** — aiming at a valid `Target`
- **Red** — aiming at empty space or non-target object

### 11.2 Implementation

The preview uses a dedicated [`AimPreview`](Assets/Scripts/Minigames/Shooter/AimPreview.cs) component that handles its own Renderer discovery and material management. [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) instantiates the preview and calls `SetPreviewColor()` each frame.

**AimPreview component** ([`AimPreview.cs`](Assets/Scripts/Minigames/Shooter/AimPreview.cs)):
- `Awake()`: finds a `Renderer` via `GetComponentInChildren<Renderer>()`, creates a unique material instance so color changes don't affect other objects
- `SetPreviewColor(Color color)`: updates the material color
- `[RequireComponent(typeof(Renderer))]` was **removed** because the prefab root may not have a Renderer directly. The component uses `GetComponentInChildren<Renderer>()` to find the Renderer on a child GameObject.

**GunController flow** ([`CreateAimPreview()`](Assets/Scripts/Minigames/Shooter/GunController.cs:505)):
1. If `_aimPreviewPrefab` is assigned → instantiate it, get `GetComponent<AimPreview>()`
2. If no prefab → create a `PrimitiveType.Sphere`, remove its Collider, add `AimPreview` via `AddComponent<AimPreview>()`

**Per-frame update** ([`UpdateAimPreview()`](Assets/Scripts/Minigames/Shooter/GunController.cs:523)):
```
GunController.Update()
         │
         ▼
   Raycast from muzzle forward
         │
    ┌────┴────┐
    ▼         ▼
  Hit       No hit
  ───       ──────
  preview   preview = muzzle + direction * maxDistance
  = hit.point
         │
         ▼
   Position aim preview at preview point
   Call _aimPreviewComponent.SetPreviewColor(green/red)
```

The preview is **hidden during shoot/reload animations** to avoid visual clutter.

### 11.3 Configuration

| Field | Default | Description |
|-------|---------|-------------|
| `_aimPreviewPrefab` | `null` (creates default sphere) | Optional custom prefab for the indicator. **Must have an `AimPreview` component** on the root (uses `GetComponentInChildren<Renderer>()`) or on the child that has the Renderer. |
| `_aimPreviewValidColor` | Green | Color when pointing at a valid Target |
| `_aimPreviewInvalidColor` | Red | Color when pointing at nothing |

If no prefab is assigned, a small sphere (`PrimitiveType.Sphere`, scale 0.15) is created automatically with an `AimPreview` component added at runtime. The collider is removed so it doesn't interfere with raycasts.

### 11.4 Prefab Setup (Unity Editor)

1. Create a small sphere child GameObject under your aim preview prefab root
2. Add a `MeshRenderer` + `SphereCollider` (sphere) or assign a material
3. Add the [`AimPreview`](Assets/Scripts/Minigames/Shooter/AimPreview.cs) component to the **root** of the prefab
   - The component uses `GetComponentInChildren<Renderer>()` so it will find the child sphere's Renderer
4. Alternatively, add `AimPreview` directly on the sphere child

### 11.5 Visibility

The preview is created in `Awake()` and remains visible for the lifetime of the gun. It updates every frame via `UpdateAimPreview()`. During shoot/reload animations (`_isShooting || _isReloading`), `UpdateAimPreview()` skips its update so the preview doesn't flicker at the muzzle.

---

## 12. Bullet Trail (Added 2026-05-10)

### 12.1 Overview

Each shot draws a short-lived **LineRenderer** from the muzzle position to the hit point (or max distance). The line fades from opaque to transparent over [`_bulletTrailDuration`](Assets/Scripts/Minigames/Shooter/GunController.cs:79), then is destroyed.

### 12.2 Implementation

[`ShowBulletTrail()`](Assets/Scripts/Minigames/Shooter/GunController.cs:323) creates a procedural `GameObject` with a `LineRenderer`:

```
ShowBulletTrail(muzzlePos, hitPoint)
         │
         ▼
   Create GameObject "BulletTrail"
   Add LineRenderer
   Set positions [muzzle, hitPoint]
   Set width: start=_bulletTrailWidth, end=_bulletTrailWidth*0.5
   Start CoFadeTrail()
         │
         ▼
   CoFadeTrail: alpha 1→0 over _bulletTrailDuration
   Then Destroy(trailObj)
```

### 12.3 Configuration

| Field | Default | Description |
|-------|---------|-------------|
| `_bulletTrailMaterial` | `null` (uses Sprites/Default) | Optional custom material |
| `_bulletTrailDuration` | 0.1s | Duration of the trail fade |
| `_bulletTrailWidth` | 0.02 | Width at the muzzle (end is 50% narrower) |
| `_bulletTrailColor` | White | Start color (fades to transparent) |

---

## 13. Fire Rate Limiting (Added 2026-05-10)

### 13.1 Overview

[`Shoot()`](Assets/Scripts/Minigames/Shooter/GunController.cs:117) checks a cooldown timer before allowing a new shot. This prevents spam-shooting from rapid hand gestures or mouse clicks.

### 13.2 Implementation

```csharp
public bool Shoot()
{
    if (_isShooting || _isReloading) return false;

    // Fire rate limiting
    if (Time.time - _lastFireTime < _fireDelay) return false;

    // ... rest of shoot logic ...

    _lastFireTime = Time.time;
    // ...
}
```

[`_fireDelay`](Assets/Scripts/Minigames/Shooter/GunController.cs:66) defaults to **0.3 seconds** (≈200 RPM). Increase for slower fire rate, decrease for faster.

### 13.3 Stacking with ShooterHandController Cooldown

Both [`GunController`](Assets/Scripts/Minigames/Shooter/GunController.cs) and [`ShooterHandController`](Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) have a fire cooldown:

| Controller | Field | Default | Purpose |
|------------|-------|---------|---------|
| GunController | `_fireDelay` | 0.3s | Prevents programmatic spam (code-driven Shoot calls) |
| ShooterHandController | `_fireCooldown` | 0.3s | Prevents gesture-triggered spam (gesture events) |

Both should be set to the same value (0.3s) for consistent behavior. The GunController's check is the final gate — even if ShooterHandController bypasses its cooldown, GunController will enforce the rate limit.

---

---

## 14. Game Flow — Start Menu, Pause, Game Over (Added 2026-05-10)

### 14.1 Overview

The Shooter game uses three distinct UI states managed by [`HUDController`](Assets/Scripts/UI/HUDController.cs) and [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs):

1. **Start Menu** — shown when the scene loads, when returning from game over
2. **Gameplay (HUD)** — active while the player is playing
3. **Game Over** — shown when the timer expires or the game ends

Audio transitions are handled by [`GameAudioController`](Assets/Scripts/Minigames/Shooter/GameAudioController.cs).

### 14.2 Scene Hierarchy

The HUD hierarchy in the scene should be:

```
MainCanvas (Canvas) — [HUDController component]
├── HUD (Panel) — children: ammo, time, score, wave announcement, etc.
├── PauseOverlay (GameObject) — [TMP_Text child for pause messages]
└── Start/GameOver Menu (GameObject)
    ├── TitleText (TMP_Text)          → _startMenuTitleText
    ├── LastScoreText (TMP_Text)      → _startMenuScoreText
    └── MainMenuButton (Button)      → calls ShooterGame.LoadMainMenu()
```

### 14.3 Serialized Fields on HUDController

| Field | Type | Purpose |
|-------|------|---------|
| `_startMenuPanel` | `GameObject` | Root of the start/game-over menu |
| `_startMenuTitleText` | `TMP_Text` | Title (e.g. "SHOOTER" or "GAME OVER") |
| `_startMenuScoreText` | `TMP_Text` | "Last Score: X" display |
| `_pauseOverlay` | `GameObject` | Text overlay for prompts (already existed) |
| `_pauseText` | `TMP_Text` | Prompt message (e.g. "PRESS SPACE TO START") |
| `_gameOverPanel` | `GameObject` | Legacy game over panel (fallback dynamic creation) |

### 14.4 Game Flow State Machine

```
┌──────────────────────────────────────────────────────────────┐
│                     Scene Loads                              │
│  GameAudioController.Start() → InitializeSources()          │
│  → Low track plays at volume 0.8 (briefly audible)          │
└──────────────────────────┬───────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│              ShooterGame.OnStart() called                    │
│  or DebugStartGame()                                         │
├──────────────────────────────────────────────────────────────┤
│  1. ShowStartMenu("SHOOTER", lastScore, "PRESS SPACE...")   │
│     → _startMenuPanel active, title + score visible         │
│     → _pauseOverlay active with prompt text                 │
│  2. _audioController.PauseMusic()                           │
│     → pause soundtrack plays during menu                    │
│  3. HUD hidden                                              │
└──────────────────────────┬───────────────────────────────────┘
                           │
                    Player presses Space
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│                    ShooterGame.BeginGame()                   │
├──────────────────────────────────────────────────────────────┤
│  1. HideStartMenu() → _startMenuPanel + _pauseOverlay hide  │
│  2. SetHUDVisible(true) → HUD elements appear               │
│  3. _audioController.SetIntensity(1) → Low track starts     │
│  4. CoWaveProgression() begins                                │
│  5. Timer starts (InvokeRepeating TimerTick)                 │
└──────────────────────────┬───────────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              │                         │
         GameManager                Timer expires
         pauses game                     │
              │                         ▼
              ▼              ┌──────────────────────────┐
┌──────────────────────┐    │     ShooterGame.OnEnd()    │
│  HandleGamePaused()   │    ├──────────────────────────┤
│  → HUD hidden         │    │  1. StopAllMusic()        │
│  → ShowPauseOverlay   │    │  2. Store LastScore       │
│    ("PAUSED")         │    │  3. ShowStartMenu(        │
│  → PauseMusic() auto  │    │     "GAME OVER",          │
│    (via GameManager    │    │     lastScore,            │
│     event)             │    │     "PRESS SPACE TO       │
└──────────┬───────────┘    │     RESTART")              │
           │                │  4. PauseMusic() plays      │
    Player presses Space    │  5. HUD hidden             │
           │                └──────────┬──────────────────┘
           ▼                           │
┌──────────────────────┐         Player presses Space
│  HandleGameResumed()  │         or clicks Restart
│  → ResumeMusic()      │               │
│  → HidePauseOverlay   │               ▼
│  → HUD visible        │    ┌──────────────────────────┐
└──────────────────────┘    │  ShooterGame.Update()      │
           │                │  detects _hasGameEnded     │
           ▼                │  + Space → calls OnStart() │
     Gameplay continues     └──────────────────────────┘
```

### 14.5 Start Menu Methods on HUDController

```csharp
/// <summary>Show the start menu / game over panel.</summary>
public void ShowStartMenu(string title, int? lastScore, string promptMessage)
{
    if (_startMenuPanel != null) _startMenuPanel.SetActive(true);
    if (_startMenuTitleText != null) _startMenuTitleText.text = title;
    if (_startMenuScoreText != null)
    {
        if (lastScore.HasValue)
        {
            _startMenuScoreText.text = $"Last Score: {lastScore.Value}";
            _startMenuScoreText.gameObject.SetActive(true);
        }
        else
            _startMenuScoreText.gameObject.SetActive(false);
    }
    if (_pauseOverlay != null) _pauseOverlay.SetActive(true);
    if (_pauseText != null) _pauseText.text = promptMessage;
}

/// <summary>Hide the start menu panel and pause overlay.</summary>
public void HideStartMenu()
{
    if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
    if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
}
```

### 14.6 Restart Flow

When the game is in the game-over state (`_hasGameEnded == true`, `_isPlaying == false`), pressing Space triggers a restart:

```csharp
if (!_isPlaying && _hasGameEnded && Input.GetKeyDown(_unpauseKey))
{
    if (_deps != null)
        OnStart(_deps);
    else
        DebugStartGame();
}
```

This re-enters the start menu flow, preserving `LastScore` so it's shown on the start menu score display.

### 14.7 MainMenu Navigation

A `LoadMainMenu()` method is exposed on [`ShooterGame`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) for UI buttons:

```csharp
public void LoadMainMenu()
{
    if (SceneLoader.Instance != null)
        SceneLoader.Instance.LoadScene(_mainMenuSceneIndex);
}
```

Wire this to the MainMenuButton in the Start/GameOver Menu GameObject via the Unity Inspector's Button.OnClick event.

### 14.8 Integration Checklist

- [ ] `HUDController`: `_startMenuPanel`, `_startMenuTitleText`, `_startMenuScoreText` fields added
- [ ] `HUDController`: `ShowStartMenu()` and `HideStartMenu()` methods implemented
- [ ] `HUDController.Awake()`: `_startMenuPanel?.SetActive(false)` added
- [ ] `ShooterGame.OnStart()`: uses `ShowStartMenu()` + `PauseMusic()` instead of `StartSilent()` + `ShowPauseOverlay()`
- [ ] `ShooterGame.DebugStartGame()`: same change
- [ ] `ShooterGame.BeginGame()`: calls `HideStartMenu()` before showing HUD
- [ ] `ShooterGame.OnEnd()`: shows start menu in-scene instead of loading MainMenu
- [ ] `ShooterGame.Update()`: restart on Space after game over
- [ ] `ShooterGame.LoadMainMenu()`: public method for UI button
- [ ] Scene: `Start/GameOver Menu` GameObject exists with children
- [ ] Scene: `_startMenuPanel`, `_startMenuTitleText`, `_startMenuScoreText` assigned in Inspector
- [ ] Scene: MainMenuButton wired to `ShooterGame.LoadMainMenu()`
- [ ] Test: Scene loads → start menu visible with pause music, HUD hidden
- [ ] Test: Press Space → start menu hides, HUD appears, intensity music plays
- [ ] Test: Timer expires → game over menu shows with final score, pause music
- [ ] Test: Press Space on game over → game restarts to start menu
- [ ] Test: MainMenuButton → loads main menu scene

---

*ARcade Rush — Shooter Implementation · PUCV 2026*
