# Error Fix & Architecture Setup Plan

> **Date:** 2026-05-11 (Updated 2026-05-12)  
> **Context:** Post-refactoring compilation errors in CubiWare project  
> **Reference:** [`docs/developer-guide.md`](../docs/developer-guide.md), [`docs/refactoring-plan.md`](../docs/refactoring-plan.md)

---

## ✅ All Code Fixes Applied

All 19 compilation errors and 2 warnings across 6 files have been fixed. Below is a summary of what was changed.

### Fixed Files

| # | File | Errors | Fix Applied |
|---|------|--------|-------------|
| 1 | [`PlayerPrefsDataStore.cs`](../Assets/Scripts/Core/Services/PlayerPrefsDataStore.cs) | 2× CS7036 | Added `nameof(PlayerPrefsDataStore)` as first argument to `LogWarning()` calls on lines 53 and 60 |
| 2 | [`HandDetectorService.cs`](../Assets/Scripts/Core/Services/HandDetectorService.cs) | 1× CS0019 | Changed `result == null` to `result.Equals(default(HandLandmarkerResult))` on line 71 |
| 3 | [`FaceDetectorService.cs`](../Assets/Scripts/Core/Services/FaceDetectorService.cs) | 1× CS0019, 1× CS0023, 3× CS1061 | Changed `result == null` to `result.Equals(default(FaceLandmarkerResult))` on line 64; fixed `ExtractBlendshapes` method to use correct MediaPipe API: `Classifications.ClassificationList.Classification[i].Score` instead of the non-existent `blendshapes` property |
| 4 | [`GroqLLMService.cs`](../Assets/Scripts/Core/Services/GroqLLMService.cs) | 1× CS1626 | Extracted the `yield return` loop into a separate `EnumerateTokens` helper method to avoid C# restriction on `yield` inside `try-catch` |
| 5 | [`BootstrapManager.cs`](../Assets/Scripts/Core/BootstrapManager.cs) | 10× CS0103 | Added `using ARcadeRush.Core;` at the top of the file |
| 6 | [`EmotionDebugDisplay.cs`](../Assets/Scripts/Minigames/EmotionTest/EmotionDebugDisplay.cs) | 2× CS0618 (warnings) | Replaced `FindObjectOfType<T>()` with `FindFirstObjectByType<T>()` on lines 50-51 |

### Detailed Fix: FaceDetectorService.cs — ExtractBlendshapes

The `Classifications` protobuf type in the installed MediaPipe plugin uses this property chain:

```
Classifications.ClassificationList          → global::Mediapipe.ClassificationList
ClassificationList.Classification           → RepeatedField<global::Mediapipe.Classification>
Classification.Score                        → float
```

The original code incorrectly used `blendshapes` as a property name on `Classifications`. The corrected method:

```csharp
private static float[] ExtractBlendshapes(FaceLandmarkerResult result)
{
    try
    {
        if (result.faceBlendshapes == null || result.faceBlendshapes.Count == 0)
            return null;

        var blendshapes = result.faceBlendshapes[0];
        if (blendshapes?.ClassificationList?.Classification == null || 
            blendshapes.ClassificationList.Classification.Count == 0)
            return null;

        float[] weights = new float[blendshapes.ClassificationList.Classification.Count];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = blendshapes.ClassificationList.Classification[i].Score;
        }

        return weights;
    }
    catch
    {
        return null;
    }
}
```

---

## 📋 Remaining Tasks (Require Unity Editor — Must Be Done By You)

These are **editor/environment setup steps** that cannot be automated via code changes.

### Step 1: Verify Build Settings Scene Order

1. Open **File → Build Settings**
2. Ensure scenes are in this order:
   | Index | Scene |
   |-------|-------|
   | 0 | `Assets/Scenes/Bootstrap.unity` |
   | 1 | `Assets/Scenes/MainMenu.unity` |
   | 2 | `Assets/Scenes/Shooter.unity` |
3. If any scene is missing, click **Add Open Scenes** after opening each scene

### Step 2: Set GroqConfig API Key

1. Locate `Assets/Resources/GroqConfig.asset`
2. In the Inspector, enter your Groq API key in the `_apiKey` field
3. Ensure `GroqConfig.asset` is in `.gitignore`

### Step 3: Verify Bootstrap Scene Setup

1. Open `Assets/Scenes/Bootstrap.unity`
2. Verify there is a root GameObject named `[Bootstrap]` (or similar)
3. Verify it has these components attached:
   - `BootstrapManager`
   - `BootstrapSelfDestruct`
   - `SceneLoader`
   - `GameManager`
   - `CameraFeedCtrl`
   - `MediaPipeController`
   - `LLMConnector`
4. Verify `DontDestroyOnLoad` is called in `Awake()`

### Step 4: Verify MediaPipe Model Files

Check that `Assets/StreamingAssets/mediapipe/` contains:
- `hand_landmarker.task`
- `face_landmarker.task`

If missing, download from [MediaPipe model card](https://ai.google.dev) and place them there.

### Step 5: Verify Shooter Scene Setup

1. Open `Assets/Scenes/Shooter.unity` (or `MG_Shooter.unity`)
2. Verify it contains:
   - `ShooterGame` component (implements `IMiniGame`)
   - `TargetManager` with pre-placed targets
   - `GunController`
   - `ShooterHandController`
   - `HUDController`
   - `MiniGameManager` (for lifecycle tracking)

### Step 6: Compile & Verify

1. Switch to Unity Editor
2. Wait for domain reload / compilation
3. Check the Console pane — **expected: zero compilation errors**
4. Press Play from `Bootstrap.unity`
5. Verify in Console:
   - `[BootstrapManager] Bootstrap starting — State=Initializing`
   - `[BootstrapManager] Bootstrap complete — State=Initialized`
   - `[BootstrapManager] MainMenu loaded.`
6. Verify MainMenu appears
7. Click "Shooter" button
8. Verify Shooter scene loads without errors

---

## Architecture Setup Checklist

- [ ] **Step 1:** Build Settings scene order verified
- [ ] **Step 2:** GroqConfig API key set
- [ ] **Step 3:** Bootstrap scene components verified
- [ ] **Step 4:** MediaPipe model files present in StreamingAssets
- [ ] **Step 5:** Shooter scene components verified
- [ ] **Step 6:** Compilation passes with zero errors
- [ ] **Step 6:** Runtime flow works (Bootstrap → MainMenu → Shooter)
