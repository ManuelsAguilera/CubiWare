# Scoring System Diagnosis — Targets Not Adding Points

## Summary of Findings

After tracing the entire scoring flow end-to-end, I identified **two independent bugs** and **one potential Editor setup issue** that compound to prevent points from being awarded when targets are shot.

---

## 1. How Scoring Is Supposed to Work

There are two game-start paths, each with a different scoring flow:

### Path A — DebugStartGame (Right-click in Inspector)
```
User shoots (left-click)
  → GunController.HandleDebugInput()
  → GunController.Shoot()
  → PerformHitscan() ─── raycast from muzzle forward
        │
        ├── Hits Target? → target.OnHit()
        │                     │
        │                     ├── Debug.Log score
        │                     ├── GameManager.Instance?.AddScore()  (null in debug mode — SKIPPED)
        │                     └── CoFallAndDeactivate()
        │
        ├── Fires OnTargetHit event ──→ ShooterGame.HandleTargetHit()
        │                                    │
        │                                    └── _score += target.HitScore
        │                                    └── UpdateHUD()
        │
        └── Fires OnShotMissed event
```

### Path B — OnStart (Full Bootstrap/GameManager flow)
```
User shoots
  → GunController.Shoot() → PerformHitscan()
        │
        ├── Hits Target? → target.OnHit()
        │                     │
        │                     └── GameManager.Instance.AddScore(points)
        │                             │
        │                             └── GameManager.OnScoreChanged event
        │                                    │
        │                                    └── ShooterGame.HandleScoreChanged()
        │                                           │
        │                                           └── _score = newScore
        │                                           └── UpdateHUD()
```

**Critical observation:** For BOTH paths, the raycast in `PerformHitscan()` MUST successfully detect the target. If it misses, no scoring path is ever triggered.

---

## 2. ISSUE A — Debug Rotation Bug (CODE Bug) ⚠️ CONFIRMED

**File:** [`Assets/Scripts/Minigames/Shooter/GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs:396)

```csharp
// Line 396 — WRONG
root.localRotation = Quaternion.Euler(0f, _debugYaw, _debugPitch);
```

**What's wrong:** `Quaternion.Euler(x, y, z)` applies rotations in Z-X-Y order. By placing `_debugPitch` in the Z parameter, the mouse Y movement is applied as **roll** (rotation around the forward axis), not **pitch** (rotation around the right axis).

**Effect:** When the player moves the mouse vertically, the gun rotates **sideways** instead of aiming up/down. The hitscan ray from the muzzle shoots in a completely different direction from where the player thinks they're aiming.

**Fix:**
```csharp
// Line 396 — CORRECTED
root.localRotation = Quaternion.Euler(-_debugPitch, _debugYaw, 0f);
```

**How this was verified:**
- The existing plan ([`plans/raycast-miss-diagnosis.md`](plans/raycast-miss-diagnosis.md)) documented this exact issue.
- The doc [`docs/shooter_implementation.md`](docs/shooter_implementation.md) section 10.4.2 describes the correct form `Quaternion.Euler(-_debugPitch, _debugYaw, 0f)` but the actual code at line 396 does not match.

---

## 3. ISSUE B — Barrel Hinge and Cylinder Point to the Same Transform (EDITOR Setup Issue) ⚠️ CONFIRMED

**File:** [`Assets/Prefabs/Shooter/Gun/Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab:52-53)

```yaml
  _cylinder: {fileID: 4435268834228717479}
  _barrelHinge: {fileID: 4435268834228717479}
```

Both `_cylinder` and `_barrelHinge` reference the **same** file ID (`4435268834228717479`), which resolves to the root FBX model transform. They should reference **different** child transforms:

| Field | Should Be |
|-------|-----------|
| `_cylinder` | The `GunPipe` child transform (the revolver cylinder that rotates) |
| `_barrelHinge` | The `Gun Barrel` child transform (the top break barrel that hinges open) |

**Effect on scoring:** This primarily affects animation (cylinder doesn't rotate independently, barrel doesn't hinge separately), but it ALSO affects the muzzle position/direction computation:

- [`MuzzleForward`](Assets/Scripts/Minigames/Shooter/GunController.cs:143) uses `_muzzleTransform.forward` as first priority — so **if `_muzzleTransform` is properly assigned**, this issue doesn't affect the raycast direction directly.

However, looking at the prefab:
- `_muzzleTransform` IS assigned (file ID `3535433330413786846`, the Muzzle child)
- So `MuzzleForward` correctly returns `_muzzleTransform.forward`
- The barrel/cylinder mixup doesn't affect scoring directly, but is a prefab setup error

---

## 4. ISSUE C — Potential Wrong `_muzzleTransform` Orientation (Could Affect Scoring)

**File:** [`Assets/Prefabs/Shooter/Gun/Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab:136-149)

The Muzzle child transform has:
```yaml
m_LocalRotation: {x: 0, y: 0.7071068, z: 0, w: 0.7071068}  # 90° around Y
m_LocalPosition: {x: 3.648, y: 2.009, z: 0.009}
```

This rotation (90° Y) means `Muzzle.forward` points **perpendicular** to the parent's forward. Whether this is correct depends on the FBX model's orientation. If the FBX model's barrel points along the local Z axis, then this 90° Y rotation would make the raycast fire in the wrong direction.

**Verdict:** This could be correct if the FBX model is oriented with the barrel along the X axis (common for some imported models). The 90° Y rotation would rotate the forward vector from Z to X, aligning with the barrel. **Needs visual verification in the Unity Scene view** using the `Debug.DrawRay` at line 438.

---

## 5. ISSUE D — Fire Rate-Limiting Could Mask the Problem

Not a bug per se, but worth noting: if the player spams clicks, [`_fireDelay`](Assets/Scripts/Minigames/Shooter/GunController.cs:66) (0.3s) blocks rapid shots. The cooldown at line 161 returns `false` without any log output, so the player might think they're shooting but nothing happens. Combined with the rotation bug, this makes the problem harder to diagnose.

---

## 6. What's NOT Wrong (Eliminated Hypotheses)

| Hypothesis | Status | Evidence |
|------------|--------|----------|
| `_hitLayerMask` set to Nothing (0) | **ELIMINATED** ✅ | Prefab serializes `m_Bits: 4294967295` = `-1` = Everything |
| Target has `BoxCollider2D` instead of `BoxCollider` (3D) | **ELIMINATED** ✅ | Prefab shows proper 3D `BoxCollider` (component `814978872746351573`) on line 63, type `BoxCollider` on line 64 |
| `GameManager.Instance` is null | **PARTIAL** — Expected in debug path | `DebugStartGame()` intentionally skips GameManager; scoring goes through `OnTargetHit` event instead |
| `ShooterGame` doesn't subscribe to events | **ELIMINATED** ✅ | `DebugStartGame()` subscribes to `OnTargetHit` at line 73; `OnStart()` subscribes to `GameManager.OnScoreChanged` at line 102 |
| `_muzzleTransform` is null / auto-created | **ELIMINATED** ✅ | Prefab shows `_muzzleTransform` is assigned to the Muzzle child |

---

## 7. Recommended Fix Plan

### Step 1: Fix Debug Rotation Bug (CODE Change)

**File:** [`Assets/Scripts/Minigames/Shooter/GunController.cs`](Assets/Scripts/Minigames/Shooter/GunController.cs:396)

Change:
```csharp
root.localRotation = Quaternion.Euler(0f, _debugYaw, _debugPitch);
```
To:
```csharp
root.localRotation = Quaternion.Euler(-_debugPitch, _debugYaw, 0f);
```

**Why this matters for scoring:** Without this fix, the debug input mode makes the gun aim in the wrong direction. The hitscan ray from the muzzle never intersects any target, so `OnTargetHit` is never fired, and `target.OnHit()` is never called. **This is the primary reason targets don't add points.**

### Step 2: Fix Barrel Hinge / Cylinder Assignment (EDITOR Setup)

**File:** [`Assets/Prefabs/Shooter/Gun/Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab)

1. Open the Gun prefab in Prefab Mode
2. In the `GunController` component:
   - Drag the **`GunPipe`** child transform (under the FBX model) to `_cylinder`
   - Drag the **`Gun Barrel`** child transform (under the FBX model) to `_barrelHinge`

**Why:** This ensures the cylinder rotates independently on shoot, and the barrel hinges open independently on reload. While this doesn't directly fix scoring, it prevents compounding issues with muzzle orientation if those transforms affect `MuzzleForward` fallback computation.

### Step 3: Verify Muzzle Orientation Visually

1. Run the scene in debug mode (Left-click)
2. Look in the **Scene view** for the red `Debug.DrawRay` line emitted by [`PerformHitscan()`](Assets/Scripts/Minigames/Shooter/GunController.cs:438)
3. Verify the ray originates from the barrel tip and points **toward** the targets
4. If the ray points sideways or backward: adjust the Muzzle child's local rotation in the prefab

### Step 4: Test the Scoring Flow End-to-End

After Steps 1-3 are verified:

1. Run the Shooter scene
2. Right-click `ShooterGameController` → "Start Game (Debug)"
3. Left-click to shoot at targets
4. Verify the Console shows:
   - `[GunController] Hitscan: origin=... dir=... hit=... point=... target=True`
   - `[Target] Bandit/Innocent (Easy) hit! Score: 5`
   - `[ShooterGame] Target hit! Score: 5 (total: 5)`
5. Verify the HUD score text updates

---

## 8. Root Cause Summary

**The scoring system code is architecturally correct** — events are properly wired, handlers exist, and score propagation has two independent paths. The problem is that **the hitscan raycast never hits a target** because of the debug rotation bug (Issue A).

| # | Issue | Type | Severity | Blocks Scoring? |
|---|-------|------|----------|-----------------|
| A | `Quaternion.Euler(0f, _debugYaw, _debugPitch)` applies pitch as roll | **Code Bug** | Critical | **YES** — gun aims wrong direction, ray never hits target |
| B | `_cylinder` and `_barrelHinge` reference same transform | **Editor Setup** | Medium | No (but breaks animations) |
| C | Muzzle orientation may be misaligned with barrel | **Editor Setup** | Low-Medium | Only if `_muzzleTransform` forward doesn't match barrel direction |

**The fix order is:** Fix A (code) → Fix B (editor) → Verify C (visual) → Test scoring.
