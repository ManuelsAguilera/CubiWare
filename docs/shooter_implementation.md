# Shooter Minigame — Implementation Guide

> **Last updated:** 2026-05-07  
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
- [ ] `Shoot()` called in `Fire()` before bullet spawn
- [ ] `LookAt()` called in `Update()` when aiming
- [ ] `Reload()` exposed as public method on `ShooterHandController`
- [ ] [`Gun.prefab`](Assets/Prefabs/Shooter/Gun/Gun.prefab) updated with `GunController` component
- [ ] Transforms assigned: Gun Barrel → `_barrelHinge`, GunCock → `_cockpit`, GunPipe → `_cylinder`, Gun root → `_recoilRoot`
- [ ] `_useDebugInput` set to `false` for production builds
- [ ] HandController scene hierarchy includes Gun as child with `_gunController` reference wired
- [ ] Test: Left-click shoots with debug input → cylinder rotates, cockpit kicks, barrel recoils
- [ ] Test: `R` key triggers reload → barrel hinges open/close
- [ ] Test: `P` toggles mouse aim → gun follows cursor when on
- [ ] Test: Hand tracking → gun follows aim ray, fist fires, thumb-down toggles safety

---

*ARcade Rush — Shooter Implementation · PUCV 2026*
