# Shooter Minigame — Hand Tracking Integration + Ammo System Plan

> **Based on:** [`docs/shooter_implementation.md`](../docs/shooter_implementation.md)  
> **Priority:** 1. Hand→Gun aiming  2. Ammo system  3. Timer (already done)

---

## Priority 1: Fix Hand → Gun Aiming (MediaPipe Integration)

### Problem

[`ShooterHandController.UpdateAimRay()`](../Assets/Scripts/Minigames/Shooter/ShooterHandController.cs:89) computes the aim direction as:
```csharp
AimDirection = (indexTip_world - indexMcp_world).normalized;
```

Both `indexTip_world` and `indexMcp_world` come from [`Hand3DProjector`](../Assets/Scripts/Hand/Hand3DProjector.cs), which uses **screen-to-world projection with estimated depth** (lines 154-162). This means:
- Both landmarks sit at nearly the same depth (Z estimated from hand scale)
- The world-space vector between them is tiny and dominated by screen XY, not true 3D finger orientation
- The gun can't "point into the scene" where targets are

### Solution: Screen-Space Aim Ray

Replace the world-space direction calculation with a **camera ray through the index fingertip screen position**:

```
Hand3DProjector provides normalized image coords (0-1)
    → Convert index tip (landmark 8) to screen pixel position
    → Camera.ScreenPointToRay(screenPos)
    → Use that ray for aiming
```

This is the same approach used by the debug mouse aiming in [`GunController.HandleDebugInput()`](../Assets/Scripts/Minigames/Shooter/GunController.cs:283) — it's proven to work.

### Changes to [`ShooterHandController.cs`](../Assets/Scripts/Minigames/Shooter/ShooterHandController.cs)

#### Modified: `UpdateAimRay()`

```csharp
private void UpdateAimRay()
{
    var norm = _projector.LastNormalizedLandmarks.landmarks;
    if (norm == null || norm.Count < 21)
    {
        IsAiming = false;
        return;
    }

    // Get index fingertip (landmark 8) normalized coordinates
    var tip = norm[8];
    Vector3 screenPos = new Vector3(tip.x * Screen.width, tip.y * Screen.height, 0f);

    // Flip X for mirrored webcam feed (matching Hand3DProjector's 1f - x)
    screenPos.x = Screen.width - screenPos.x;

    AimOrigin = _mainCamera != null ? _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f)) : Vector3.zero;

    // Cast ray through fingertip screen position
    if (_mainCamera != null)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _targetLayer))
        {
            _aimTargetPoint = hit.point;
            AimDirection = (_aimTargetPoint - AimOrigin).normalized;
        }
        else
        {
            _aimTargetPoint = ray.origin + ray.direction * _maxRayDistance;
            AimDirection = ray.direction;
        }
    }

    IsAiming = true;

    // Debug visualization
    if (_showDebugRay)
    {
        Debug.DrawRay(AimOrigin, AimDirection * _maxRayDistance, _safetyOn ? Color.yellow : Color.red);
    }
}
```

Key differences from current code:
1. Uses `LastNormalizedLandmarks` from `Hand3DProjector` directly (normalized image coords)
2. Converts landmark 8 (index tip) to screen pixel position
3. Casts `Camera.ScreenPointToRay()` through that position
4. Uses the ray hit point (or far point) as `_aimTargetPoint`
5. Mirror X coordinate (`Screen.width - screenPos.x`) to match Hand3DProjector's `1f - landmarks[i].x`

#### Modified: `Awake()`

Add camera caching since we now heavily depend on it:
```csharp
private void Awake()
{
    _projector = GetComponent<Hand3DProjector>();
    _gestureDetector = GetComponent<GestureDetector>();
    _mainCamera = Camera.main;
    _safetyOn = _startWithSafetyOn;
}
```

(This is already in the current code — no changes needed.)

#### Modified: `Fire()`

Add return value check from `_gunController.Shoot()` (for the ammo system):
```csharp
private void Fire()
{
    _lastFireTime = Time.time;
    _canFire = false;

    // Trigger gun visual — returns false if empty/reloading
    bool shotFired = _gunController != null && _gunController.Shoot();

    if (!shotFired)
    {
        Debug.Log("[ShooterHand] Gun empty or reloading — no bullet spawned.");
        Invoke(nameof(ResetFireCooldown), _fireCooldown);
        return;
    }

    Vector3 spawnPos = _bulletSpawnPoint != null
        ? _bulletSpawnPoint.position
        : AimOrigin;

    if (_bulletPrefab != null)
    {
        GameObject bullet = Instantiate(_bulletPrefab, spawnPos, Quaternion.LookRotation(AimDirection));
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = AimDirection * _bulletSpeed;
        Destroy(bullet, _maxRayDistance / _bulletSpeed);
    }
    else
    {
        PerformHitscan();
    }

    Debug.Log("[ShooterHand] FIRE!");
    Invoke(nameof(ResetFireCooldown), _fireCooldown);
}
```

---

## Priority 2: Ammo / Bullet System

### Changes to [`GunController.cs`](../Assets/Scripts/Minigames/Shooter/GunController.cs)

The revolver holds **6 bullets**. Ammo is tracked inside `GunController`.

#### New Serialized Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `_maxAmmo` | `int` | 6 | Revolver capacity |
| `_autoReloadOnEmpty` | `bool` | true | Auto-reload when ammo reaches 0 |

#### New Runtime State

| Field | Type | Description |
|-------|------|-------------|
| `_currentAmmo` | `int` | Current bullet count (initialized to `_maxAmmo` in Awake) |
| `OnAmmoChanged` | `event Action<int,int>` | Fired when ammo changes (current, max) |
| `OnOutOfAmmo` | `event Action` | Fired when ammo reaches 0 |
| `OnReloadStarted` | `event Action` | Fired when reload begins |
| `OnReloadCompleted` | `event Action` | Fired when reload finishes + ammo refilled |

#### New Public Properties

```csharp
public int CurrentAmmo => _currentAmmo;
public int MaxAmmo => _maxAmmo;
public bool IsEmpty => _currentAmmo <= 0;
```

#### Modified: `Shoot()` — returns `bool`

```csharp
public bool Shoot()
{
    if (_isShooting || _isReloading) return false;
    if (_currentAmmo <= 0)
    {
        if (_autoReloadOnEmpty && !_isReloading)
            Reload();
        return false;
    }

    _currentAmmo--;
    OnAmmoChanged?.Invoke(_currentAmmo, _maxAmmo);
    if (_currentAmmo <= 0) OnOutOfAmmo?.Invoke();

    StartCoroutine(ShootSequence());
    return true;
}
```

#### Modified: `Reload()` — returns `bool`, ignores if already full

```csharp
public bool Reload()
{
    if (_isShooting || _isReloading) return false;
    if (_currentAmmo >= _maxAmmo) return false;

    OnReloadStarted?.Invoke();
    StartCoroutine(ReloadSequence());
    return true;
}
```

#### Modified: `ReloadSequence()` — refill ammo at end

Add at the end of the coroutine, after barrel close and cylinder restore:
```csharp
_currentAmmo = _maxAmmo;
OnAmmoChanged?.Invoke(_currentAmmo, _maxAmmo);
OnReloadCompleted?.Invoke();
_isReloading = false;
```

#### Modified: `Awake()` — init ammo

```csharp
private void Awake()
{
    CacheRestPoses();
    _currentAmmo = _maxAmmo;
}
```

#### No changes to:
- `HandleDebugInput()` — left-click calls `Shoot()` which already handles empty state
- `ShootSequence()` — animation logic stays same, ammo is decremented before starting the coroutine

### State Machine

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

### Changes to [`ShooterGame.cs`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs)

Wire GunController events to HUD.

#### New Serialized Field

| Field | Type | Description |
|-------|------|-------------|
| `_gunController` | `GunController` | Reference to the gun for ammo events |

#### Modified: `OnStart()` — subscribe

```csharp
if (_gunController != null)
{
    _gunController.OnAmmoChanged += HandleAmmoChanged;
    _gunController.OnReloadStarted += HandleReloadStarted;
    _gunController.OnReloadCompleted += HandleReloadCompleted;
}
```

#### Modified: `OnEnd()` — unsubscribe

```csharp
if (_gunController != null)
{
    _gunController.OnAmmoChanged -= HandleAmmoChanged;
    _gunController.OnReloadStarted -= HandleReloadStarted;
    _gunController.OnReloadCompleted -= HandleReloadCompleted;
}
```

#### New Handlers

```csharp
private void HandleAmmoChanged(int current, int max)
{
    _hudController?.UpdateAmmo(current, max);
}

private void HandleReloadStarted()
{
    _hudController?.ShowReloading(true);
}

private void HandleReloadCompleted()
{
    _hudController?.ShowReloading(false);
}
```

### HUDController Changes

Two new methods needed (location TBD — depends on where HUDController lives):

```csharp
public void UpdateAmmo(int current, int max);   // Display "5 / 6" etc.
public void ShowReloading(bool isReloading);     // Show/hide "RELOADING..." text
```

---

## Priority 3: Timer

✅ **Already implemented** in [`ShooterGame.cs`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs):
- [`_gameDuration`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs:21) = 90s
- [`TimerTick()`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs:90) called every 1s via `InvokeRepeating`
- [`HandleTimeout()`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs:123) → returns to MainMenu

**No changes needed.** Timer and ammo are independent systems.

---

## Files Summary

| File | Action | Key Changes |
|------|--------|-------------|
| [`ShooterHandController.cs`](../Assets/Scripts/Minigames/Shooter/ShooterHandController.cs) | **Modify** | Fix `UpdateAimRay()` to use screen-space raycast from index fingertip; check `Shoot()` return value in `Fire()` |
| [`GunController.cs`](../Assets/Scripts/Minigames/Shooter/GunController.cs) | **Modify** | Add `_maxAmmo=6`, `_currentAmmo`, ammo events, `Shoot()`/`Reload()` return bool, auto-reload, ammo refill |
| [`ShooterGame.cs`](../Assets/Scripts/Minigames/Shooter/ShooterGame.cs) | **Modify** | Wire GunController ammo events to HUD |
| `HUDController` | **Modify** | Add `UpdateAmmo()` and `ShowReloading()` |

---

## Verification Checklist

### Hand Aiming
- [ ] Index finger tip screen position ≈ crosshair / aim point
- [ ] Gun rotates to follow aim ray via `LookAt()`
- [ ] Raycast hits targets at correct depth (Near z=5, Mid z=12, Far z=20)
- [ ] Debug ray visible (yellow/yellow=safety, red=firing)
- [ ] ClosedFist gesture → gun shoots + bullet spawns
- [ ] ThumbDown → safety toggle (stops firing)
- [ ] P key toggles debug input → mouse aim still works

### Ammo System
- [ ] Gun starts with 6/6 ammo
- [ ] Each shot decrements ammo, HUD updates
- [ ] At 0 ammo → auto-reload triggers (0.8s animation)
- [ ] Shots blocked during reload
- [ ] After reload → 6/6, can shoot again
- [ ] Reload during full ammo → no-op
- [ ] Timer (90s) → game ends independently of ammo

---

*ARcade Rush — Shooter Hand Tracking + Ammo Plan · PUCV 2026*
