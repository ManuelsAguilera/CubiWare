# Gesture Detection Guide

This guide covers how to calibrate, record, and configure new gestures for the ARcade system.

## 1. Overview
The system consists of two parts:
- **`GestureDetector.cs`**: Runtime engine that detects gestures based on rules in `GestureHeuristics.csv`.
- **`GestureRecordingManager.cs`**: Tooling to record raw hand snapshots and generate heuristic rules automatically.

---

## 2. Setting Up a New Gesture
To teach the system a new gesture (e.g., "PeaceSign"):

### A. Record Data
1. Attach `GestureRecordingManager` to a GameObject in your scene.
2. In the Inspector, set `Current Gesture Name` to "PeaceSign".
3. Enter **Play Mode**.
4. Hold the "PeaceSign" pose.
5. Press **`R`** to start recording (Console will log `[RECORDING STARTED]`).
6. Hold for 2-3 seconds, then press **`R`** to stop.
7. Press **`S`** to save the data to `Assets/Resources/RecordedGestures.json`.

### B. Generate Heuristics
1. Select the GameObject with `GestureRecordingManager`.
2. Look at the `Gesture Recording Editor` component in the Inspector.
3. Type "PeaceSign" in the "Gesture Name" field.
4. Click **"Suggest Heuristic"**.
5. Copy the generated line from the text area.

### C. Update Detector
1. Paste the copied line into `Assets/Resources/GestureHeuristics.csv`.
2. Save the file. The `GestureDetector` will automatically pick up the new rule on the next play session.

---

## 3. Advanced Configuration
### Custom Rules
If a gesture needs thumb positioning (e.g., "Inside Palm"), you can manually set the `CustomRule` column in the CSV. Available rules:
- `None`
- `ThumbTipBelowIP`
- `ThumbExtended`
- `ThumbTucked`
- `ThumbAboveMCP`
- `ThumbInsidePalm`
- `ThumbOutsidePalm`

### Multi-finger "OR" Logic
If you want an Index OR Middle finger to satisfy a requirement, set that column in the CSV to **`EitherUp`**. The `GestureDetector` will treat this as a logical OR.

---

## 4. Debugging
- **Inspector Feedback:** Look at the `GestureDetector` component while in Play Mode.
- **`Current Detected Gesture`:** Shows the live gesture being detected.
- **`Debug Finger States`:** Displays the real-time `UP` or `DOWN` status of each finger based on the internal detection math (Tip Y > Joint Y).
- **Console:** The system will log `[GestureDetector] RECEIVED FIRST SET OF LANDMARKS!` upon successfully connecting to the camera feed.
