# Raycast Always Misses — Systematic Diagnosis

## Problem Statement

When firing the gun in both debug and hand-tracking modes, the hitscan raycast never hits a [`Target`](Assets/Scripts/Minigames/Shooter/Target.cs) object. The ray consistently reports a miss, with the direction vector showing a leftward bias (e.g. `dir=(-0.35, -0.11, 0.93)`).

The user explicitly suspects a **collider problem**, noting: *"the BoxCollider Component cannot be edited. maybe it is because this is a collition of the sprite renderer?"*

---

## 1. Collider Type Mismatch (PRIMARY SUSPECT)

### Current Code in [`Target.cs`](Assets/Scripts/Minigames/Shooter/Target.cs:72)

```csharp
private void Awake()
{
    _collider = GetComponent<Collider>();   // ← 3D Collider only
    _startPosition = transform.position;
    gameObject.SetActive(false);
}
```

- `GetComponent<Collider>()` searches for **3D colliders** only (`BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`).
- It will **NOT** find a `BoxCollider2D`, `CircleCollider2D`, or `CapsuleCollider2D`.

### User Observation

> *"the BoxCollider Component cannot be edited. maybe it is because this is a collition of the sprite renderer?"*

**What happens when `SpriteRenderer` generates a collider:**
1. In Unity, when a [`SpriteRenderer`](https://docs.unity3d.com/Manual/class-SpriteRenderer.html) has a sprite assigned, you can enable **"Sprite Cast"** or click **"Generate Physics Shape"**.
2. This automatically adds a **`BoxCollider2D`** (a 2D collider), NOT a `BoxCollider` (3D).
3. `BoxCollider2D` components are locked (uneditable) because they auto-conform to the sprite's texture shape.

### Consequences

| Code line | What happens when `_collider = null` |
|-----------|---------------------------------------|
| `_collider.enabled = false` (line 90) | No-op — collider remains active |
| `_collider.enabled = true` (line 214) | No-op |
| `Physics.Raycast(..., QueryTriggerInteraction.Collide)` (line 618) | 3D raycast can **NEVER** hit `BoxCollider2D` — different physics system |

The `Physics2D.Raycast` (line 633) should theoretically hit the `BoxCollider2D`, but it still misses — which points to a secondary direction problem.

---

## 2. Pitch/Rotation Axis Bug (SECONDARY SUSPECT — direction problem)

### Current Code in [`GunController.HandleDebugInput()`](Assets/Scripts/Minigames/Shooter/GunController.cs:486-487)

```csharp
Transform root = _recoilRoot != null ? _recoilRoot : transform;
root.localRotation = Quaternion.Euler(0f, _debugYaw, _debugPitch);
```

### The Bug

`Quaternion.Euler(0f, _debugYaw, _debugPitch)` applies:
- **`_debugYaw`** → rotation around **local Y-axis** ✅ (correct for horizontal aim)
- **`_debugPitch`** → rotation around **local Z-axis** ❌ (this is ROLL, not PITCH)

In Unity, `Quaternion.Euler(x, y, z)` applies rotations in **Z-X-Y** order:
1. Z rotation first (roll)
2. X rotation second (pitch)
3. Y rotation last (yaw)

So `Quaternion.Euler(0f, yaw, pitch)` = `(roll=pitch, pitch=0, yaw=yaw)` — the pitch value is applied as roll around the Z axis, NOT as pitch around the X axis.

**This is why the direction consistently has a leftward (horizontal) bias instead of a vertical component.** The "pitch" input is rotating the gun sideways, not up/down.

### Correct Code

```csharp
root.localRotation = Quaternion.Euler(-_debugPitch, _debugYaw, 0f);
//                         pitch goes in X:  ^^^^^^^^^^^
//                         yaw goes in Y:     ^^^^^^^^^^^
//                         roll stays zero:               ^^
```

---

## 3. Summary of All Possible Causes

| # | Cause | Confidence | Effect |
|---|-------|------------|--------|
| **A** | `Collider` vs `BoxCollider2D` mismatch in Target.cs | **HIGH** | 3D raycast never hits; `_collider.enabled` does nothing |
| **B** | `Quaternion.Euler(0f, yaw, pitch)` applies pitch on Z-axis instead of X-axis | **HIGH** | Gun visually rotates sideways; direction has leftward bias |
| **C** | Both 3D and 2D raycasts use same (incorrect) direction | **HIGH** | Both raycast types miss because direction doesn't point at targets |
| **D** | Muzzle position near camera causes direction computed from `_debugAimTarget` to deviate | **LOW** | Only if muzzle is far from camera's forward line |
| **E** | `_hitLayerMask = -1` includes everything — not a cause | **NONE** | Should hit any collider |
| **F** | `_maxShootDistance = 50m` is sufficient for 30m targets | **NONE** | Not the cause |

---

## 4. Recommended Fix Plan

### Step 1: Fix the Pitch/Rotation Bug in [`GunController.cs:487`](Assets/Scripts/Minigames/Shooter/GunController.cs:487)

```csharp
// BEFORE (WRONG — pitch on Z axis):
root.localRotation = Quaternion.Euler(0f, _debugYaw, _debugPitch);

// AFTER (CORRECT — pitch on X axis):
root.localRotation = Quaternion.Euler(-_debugPitch, _debugYaw, 0f);
```

**Why:** This ensures mouse Y movement rotates the gun up/down (pitch around X), not sideways (roll around Z).

### Step 2: Add a 3D BoxCollider to Target Prefab (Prefab Change)

**User's choice:** *"If it is not detecting the BoxCollider2D we can create another collider in 3D."*

**Actions (in Unity Editor):**
1. Select each Target GameObject/prefab in the scene
2. **Remove** the auto-generated `BoxCollider2D` (from SpriteRenderer)
3. **Add** a `BoxCollider` (3D) component
4. Size it to match the sprite's visual bounds (set `Center` and `Size` appropriately)
5. Ensure **"Is Trigger"** is **UNCHECKED**
6. The existing code `GetComponent<Collider>()` in [`Target.cs:72`](Assets/Scripts/Minigames/Shooter/Target.cs:72) will now find this 3D `BoxCollider`
7. All `_collider.enabled = false/true` calls will work as intended

**No code changes needed in Target.cs** for the collider — only the prefab changes.

### Step 3: Verify with Diagnostic Log

Add a one-time diagnostic log in [`Target.Awake()`](Assets/Scripts/Minigames/Shooter/Target.cs:70):

```csharp
Debug.Log($"[Target] Awake — " +
    $"Collider3D={GetComponent<Collider>()?.GetType().Name ?? "null"} " +
    $"Collider2D={GetComponent<Collider2D>()?.GetType().Name ?? "null"}");
```

### Step 4: Visual Verification

The existing `Debug.DrawRay(origin, direction * _maxShootDistance, Color.red, 2f)` in [`PerformHitscan()`](Assets/Scripts/Minigames/Shooter/GunController.cs:609) already shows the ray in the Scene view. After Step 1 fixes the direction, you'll be able to see the red ray passing through targets.

---

## 5. Final Diagnosis

**The raycast misses because of TWO independent bugs that compound each other:**

1. **Collider type mismatch** — The targets have `BoxCollider2D` (auto-generated from SpriteRenderer), but the code looks for `Collider` (3D). The 3D raycast **never can hit** these targets.

2. **Wrong rotation axis** — `Quaternion.Euler(0f, _debugYaw, _debugPitch)` applies pitch as roll around Z instead of pitch around X. This makes the gun aim sideways, so **even the 2D raycast misses**.

**Fix order:**
1. Fix the rotation axis in `HandleDebugInput()` (code change)
2. Add 3D BoxCollider to target prefabs (Unity Editor change)
3. Verify with Debug.DrawRay in Scene view

After both fixes, the `BoxCollider` (3D) will be detected by `Physics.Raycast`, and the direction will point correctly at targets.
