# Fix: Debug Input Aiming in GunController

## Root Cause

In [`HandleDebugInput()`](Assets/Scripts/Minigames/Shooter/GunController.cs:366-367), the gun aims using pure camera-forward vector:

```csharp
Vector3 aimPoint = cam.transform.position + cam.transform.forward * 50f;
LookAt(aimPoint);
```

This makes the gun point at a fixed point 50 units straight ahead of the camera, ignoring where the screen center actually hits in world space.

## What the User Wants

> Standard FPS debug mode: cursor hidden + locked to screen center, and the gun aims where a raycast from **screen center** hits.

## What's Already Correct (no changes needed)

| Feature | Status | Location |
|---------|--------|----------|
| `_useDebugInput = false` (default off) | ✅ Already set | [`line 61`](Assets/Scripts/Minigames/Shooter/GunController.cs:61) |
| Cursor lock on P toggle | ✅ Already implemented | [`lines 356-357`](Assets/Scripts/Minigames/Shooter/GunController.cs:356-357) |
| Shoot() returns bool for ammo system | ✅ Already implemented | [`line 113`](Assets/Scripts/Minigames/Shooter/GunController.cs:113) |
| Reload() coroutine refills ammo | ✅ Already implemented | [`line 314`](Assets/Scripts/Minigames/Shooter/GunController.cs:314) |

## Changes Required

### Change 1: Fix aiming method in HandleDebugInput()

Replace lines 365-372 from camera-forward to **screen-center raycast**.

**Current (WRONG):**

```csharp
if (cam != null)
{
    // Gun always aims where the camera is looking
    Vector3 aimPoint = cam.transform.position + cam.transform.forward * 50f;
    LookAt(aimPoint);

    // Debug ray showing where the gun is pointing
    Debug.DrawLine(cam.transform.position, aimPoint, Color.cyan);
}
```

**Replacement (CORRECT):**

```csharp
if (cam != null)
{
    // Raycast from screen center — cursor is locked to center via CursorLockMode.Locked
    Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
    Ray ray = cam.ScreenPointToRay(screenCenter);

    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
    {
        LookAt(hit.point);
        Debug.DrawLine(ray.origin, hit.point, Color.green);
    }
    else
    {
        Vector3 farPoint = ray.origin + ray.direction * 50f;
        LookAt(farPoint);
        Debug.DrawLine(ray.origin, farPoint, Color.yellow);
    }
}
```

### Change 2: Add diagnostic logging

Add `Debug.Log` calls to print camera direction vs gun direction each frame in debug mode. This lets you verify in the Console that both align when the screen-center raycast is working.

```csharp
if (cam != null)
{
    // Raycast from screen center — cursor is locked to center via CursorLockMode.Locked
    Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
    Ray ray = cam.ScreenPointToRay(screenCenter);

    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
    {
        LookAt(hit.point);
        Debug.DrawLine(ray.origin, hit.point, Color.green);

        // DIAGNOSTIC: Log camera forward vs gun direction
        Debug.Log($"[GunController] Cam forward: {cam.transform.forward} | Gun target: {hit.point} | Ray hit: {hit.collider.name}");
    }
    else
    {
        Vector3 farPoint = ray.origin + ray.direction * 50f;
        LookAt(farPoint);
        Debug.DrawLine(ray.origin, farPoint, Color.yellow);

        // DIAGNOSTIC: Log camera forward vs gun direction (no hit)
        Debug.Log($"[GunController] Cam forward: {cam.transform.forward} | Gun target (far): {farPoint} | No hit");
    }
}
```

### Change 3: Update debug log message on toggle

- Line 358 current: `"ON (camera-forward aim)"`
- Change to: `"ON (crosshair aim)"`

## Why This Works

1. **Cursor is locked** → mouse can't leave the window (`CursorLockMode.Locked`)
2. **Cursor is invisible** → no distracting cursor visible (`Cursor.visible = false`)
3. **Screen-center raycast** → gun aims where a crosshair would be (standard FPS)
4. **Green ray when hitting** / **yellow ray when missing** → clear visual feedback
5. **Console logs** → you can verify camera forward vs gun target direction in real-time

## Verification Checklist

- [ ] Press **P** → cursor hides + locks, debug mode activates
- [ ] Move mouse → gun rotates to follow screen-center raycast hits
- [ ] Console shows `[GunController] Cam forward: (x,y,z) | Gun target: (x,y,z) | ...` each frame
- [ ] Left-click → Shoot() fires (returns false if empty)
- [ ] **R** key → triggers reload animation
- [ ] Press **P** again → cursor returns, debug mode deactivates
