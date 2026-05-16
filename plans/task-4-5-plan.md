# Task 4 & 5 — Implementation Plan

> Based on [`plans/remaining-tasks.md`](plans/remaining-tasks.md), analysis of current code in [`GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs), [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs), [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs), and [`AimPreview.cs`](Assets/Scripts/Minigames/Shooter/AimPreview.cs).

---

## ✅ Task 3 Status: Complete
Fire rate limiting already works — dual-layer protection via `_fireDelay` (0.3s) in both `GunController` and `ShooterHandController`.

---

## 📋 Task 4 — Scoring Logic: Clean Up Dual Paths

### Current Problem
Two competing scoring paths exist:

**Path A — Target → GameManager (still active)**
In [`Target.OnHit()`](Assets/Scripts/Minigames/Shooter/Target.cs:137):
```
OnHit() → GameManager.Instance?.AddScore(points)
```
This is called by [`GunController.PerformHitscan()`](Assets/Scripts/Minigames/Shooter/GunController.cs:431) on every hit.

**Path B — GunController → ShooterGame (Debug only)**
In [`ShooterGame.DebugStartGame()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:54):
```
GunController.OnTargetHit → ShooterGame.HandleTargetHit()
```
But [`ShooterGame.OnStart()`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:87) (Bootstrap path) does **not** subscribe to `OnTargetHit`.

**Result:** In Debug mode with GameManager present, score is **double-counted**. In Bootstrap mode, `HandleTargetHit()` is never called.

### Changes Required

#### 1. [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs:137) — Remove GameManager scoring from `OnHit()`
- **Lines 149-150:** Remove the two lines:
  ```csharp
  if (GameManager.Instance != null)
      GameManager.Instance.AddScore(points);
  ```
  Targets should not manage score — they only report hits.

#### 2. [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:87) — Subscribe to `OnTargetHit` in Bootstrap path
- **After line 110** (after `OnReloadCompleted` subscription), add:
  ```csharp
  _gunController.OnTargetHit += HandleTargetHit;
  ```
  This ensures scoring works in both Debug and Bootstrap modes.

#### 3. [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:283) — Update `HandleTargetHit()` to report to GameManager
- Current behavior: only adds to local `_score` + updates HUD
- New behavior: if `_deps?.GameManager != null` (Bootstrap mode), also report via `_deps.GameManager.AddScore(target.HitScore)` so GameManager tracks total score
- In Debug mode (no `_deps`): just use local `_score` as before

---

## 📋 Task 5 — Aim Preview: Wire Visibility + Sphere Size

### Current Problem
- [`SetAimPreviewActive()`](Assets/Scripts/Minigames/Shooter/GunController.cs:227) is defined but **never called** from anywhere — preview is always visible
- Fallback sphere scale is `0.15f` ([line 517](Assets/Scripts/Minigames/Shooter/GunController.cs:517)), which is hard to see at long distances

### Changes Required

#### 1. [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:54) — Activate preview on game start (Debug)
- **After line 53** in `DebugStartGame()`:
  ```csharp
  _gunController?.SetAimPreviewActive(true);
  ```

#### 2. [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:87) — Activate preview on game start (Bootstrap)
- **After line 89** in `OnStart()`:
  ```csharp
  _gunController?.SetAimPreviewActive(true);
  ```

#### 3. [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs:123) — Deactivate preview on game end
- **After line 125** in `OnEnd()`:
  ```csharp
  _gunController?.SetAimPreviewActive(false);
  ```

#### 4. [`GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs:505) — Increase fallback sphere scale
- **Line 517:** Change `Vector3.one * 0.15f` → `Vector3.one * 0.25f`
- Since `CreateAimPreview()` only creates the fallback sphere when no prefab is assigned, this change applies when using the procedural sphere. If a prefab is used, adjust the prefab's scale in the Unity Editor instead.

---

## Summary of Files to Modify

| File | Changes |
|------|---------|
| [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs) | Remove `GameManager.Instance?.AddScore(points)` lines (149-150) |
| [`ShooterGame.cs`](Assets/Scripts/Minigames/Shooter/ShooterGame.cs) | Add `OnTargetHit` subscription in `OnStart()`; update `HandleTargetHit()` for GameManager reporting; add `SetAimPreviewActive(true/false)` calls in `DebugStartGame()`, `OnStart()`, and `OnEnd()` |
| [`GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs) | Change fallback sphere scale from 0.15 to 0.25 |
