# Simon Round‑Completion Detection — Full Logic Audit

> **Date:** 2026-06-01  
> **Scope:** Every code path, parameter, flag, and timing mechanism that determines whether a player wins or loses a single round.  
> **Finding:** One confirmed bug (emotion rounds can NEVER score `Correct`); two race-condition mitigations already in place; several design notes.

---

## 0. Critical Clarification: This Is "Simon SAYS," Not "Simon Memory"

| Expectation | Reality |
|---|---|
| Growing sequence (round 1: [Red]; round 2: [Red, Blue]; …) | ❌ No sequence. Each round issues exactly **one** command. |
| `targetSequence[]` array | ❌ Does not exist. |
| `playerInput[]` array | ❌ Does not exist. |
| Step index | ❌ Does not exist. |
| Memory reproduction | ❌ Not applicable. |

This is a **truth/lie game**: the player must obey when the command contains "Simón dice" and must stay still when it does not. A "round win" = obeying the truth table correctly for that single command.

---

## 1. Architecture Overview — Who Does What

```
SimonGame (orchestrator)
  ├─ GamePhase state machine (Idle → Generating → DisplayCommand → WaitResponse → Judging → Feedback → …)
  ├─ Owns _currentCommand, _roundAlreadyJudged, _correctStreak, _currentRound
  ├─ Calls _judge.SetExpectedGesture/SetExpectedZone/SetSimonDiceFlag/SetEmotionRound before each round
  └─ Receives: HandlePlayerAction / HandlePlayerTricked / HandleEmotionMatched / HandleTimeout

SimonJudge (validator)
  ├─ Two detection modes: gesture+zone (event+polling) and emotion-only (polling every 250ms)
  ├─ Owns _actionAlreadyRegistered, _baselineGesture, _commandContainsSimonDice
  └─ Fires: OnPlayerAction / OnPlayerTricked / OnEmotionMatched

GestureDetector (low-level)
  ├─ 5-frame debounce, fires OnGestureDetected(string) on transitions only
  ├─ Whitelist-filtered to OpenHand + ClosedFist
  └─ Exposes CurrentDetectedGesture (polled every frame by SimonJudge.Update)

HandZoneClassifier (shared)
  ├─ 4-frame debounce, fires OnZoneChanged(old, new)
  └─ Exposes CurrentZone, IsInZone(HandZone)

EmotionGameBridge (singleton)
  ├─ AutoInterval mode: CurrentEmotion updates every WebSocket message
  └─ IsMatchingEmotion(string, float) → CurrentEmotion == target && Confidence >= threshold
```

---

## 2. Complete Round Flow (Gesture Round)

### 2.1 Phase: `Generating` → `DisplayCommand`

[`SimonGame.StartRound()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:332)  
↓  
[`SimonCommandGenerator.GenerateCommand()`](Assets/Scripts/Minigames/Simon/SimonCommandGenerator.cs:226) (async — LLM may take 1-3s)  
↓  
[`SimonGame.OnCommandReady()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:379)  

```
Phase = DisplayCommand
Shows dialogue text via _hud.ShowDialogue()
Starts CoDisplayPhase() coroutine
```

### 2.2 Phase: `DisplayCommand` → `WaitResponse`

[`CoDisplayPhase()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:396)

```
Waits _commandDisplayDuration (2.5s)
Shows position arrows (parallel hint — does NOT block timer)
Shows emotion HUD if emotion round
Calls BeginResponsePhase()
```

### 2.3 `BeginResponsePhase()` — Configures the Judge

[`BeginResponsePhase()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:423)

```
Phase = WaitResponse

For gesture rounds:
  _judge.SetExpectedGesture(_currentCommand.GestureTarget)   // "OpenHand" or "ClosedFist"
  _judge.SetExpectedZone(_currentCommand.ExpectedZone)        // e.g., HandZone.UpLeft
  _judge.SetSimonDiceFlag(_currentCommand.ContainsSimonDice)  // true/false

For emotion rounds:
  _judge.SetEmotionRound(_currentCommand.EmotionTarget)       // e.g., SimonEmotionTarget.Happy
  _judge.SetSimonDiceFlag(_currentCommand.ContainsSimonDice)
  _judge.SetExpectedZone(HandZone.None)                       // no zone for emotion rounds

_judge.StartMonitoring()
Starts _responseTimer = _responseTimePerRound (5s)
Starts CoResponseTimer() coroutine
```

### 2.4 `SimonJudge.StartMonitoring()` — Arms Detection

[`StartMonitoring()`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:92)

For **gesture rounds**:
```
_isMonitoring = true
_actionAlreadyRegistered = false
Captures _baselineGesture = _gestureDetector.CurrentDetectedGesture  // pre-held gestures ignored
Subscribes: _gestureDetector.OnGestureDetected += HandleGestureDetected
```

For **emotion rounds**:
```
_isMonitoring = true
_actionAlreadyRegistered = false
Sets _emotionBridge.SetMode(AutoInterval)
Starts polling (handled in Update())
```

---

## 3. Detection Mechanisms — The Two Paths to Round Resolution

### 3.1 Path A: Gesture Detection — Event-Driven (Fast Path)

[`HandleGestureDetected(string)`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:306)

**Trigger:** `GestureDetector.OnGestureDetected` fires on gesture TRANSITIONS (after internal 5-frame debounce).

**Filter chain (in order):**

| Step | Check | If Fails |
|------|-------|----------|
| 1 | `!_isMonitoring` | `return` (ignore) |
| 2 | `_isEmotionRound` | `return` (emotion round — gestures ignored) |
| 3 | `gestureName == "None"` | Fire `OnPlayerReturnedToNeutral` if already acted; `return` |
| 4 | `gestureName == _baselineGesture` | `return` (pre-held gesture — not a new action) |
| 5 | Zone check: `_handZoneClassifier.IsInZone(_expectedZone)` | `return` (silently reject — gesture in wrong zone) |
| 6 | `_actionAlreadyRegistered` | `return` (already fired this round) |
| 7 | `!_commandContainsSimonDice` | Fire `OnPlayerTricked(gestureName)` ← **game over** |
| 8 | *(passed all)* | Fire `OnPlayerAction(gestureName)` ← **correct** |

### 3.2 Path B: Gesture Detection — Continuous Polling (v6 Safety Net)

[`SimonJudge.Update()`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:260)

**Trigger:** Every frame during `WaitResponse` for gesture rounds.

**Purpose:** Catches the race condition where `HandleGestureDetected` fires before `HandZoneClassifier`'s 4-frame debounce settles. The event rejects the gesture at step 5 (zone not confirmed), but `Update()` polls both gesture and zone every subsequent frame until both match.

**Filter chain:**

| Step | Check | If Fails |
|------|-------|----------|
| 1 | `!_hasExpectedGesture` | `return` |
| 2 | `_actionAlreadyRegistered` | `return` (already fired) |
| 3 | `_gestureDetector == null` | `return` |
| 4 | `currentGesture == null \|\| "None"` | `return` |
| 5 | `currentGesture == _baselineGesture` | `return` |
| 6 | Gesture match: `currentGesture == _expectedGesture.ToString()` | `return` (wrong gesture — keep polling) |
| 7 | Zone check: `_handZoneClassifier.IsInZone(_expectedZone)` | `return` (zone not settled — keep polling) |
| 8 | `!_commandContainsSimonDice` | Fire `OnPlayerTricked(currentGesture)` |
| 9 | *(passed all)* | Fire `OnPlayerAction(currentGesture)` |

**Key insight:** Step 6 and 7 use `return` (not break) — the method keeps polling every frame until BOTH conditions are true simultaneously. This is the v6 fix for the race condition.

### 3.3 Path C: Emotion Detection — Periodic Polling

[`SimonJudge.Update()`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:228)

**Trigger:** Every 250ms (`EmotionPollInterval`) during emotion rounds.

**Filter chain:**

| Step | Check | If Fails |
|------|-------|----------|
| 1 | `_emotionBridge == null \|\| !IsConnected \|\| !FaceDetected` | `return` (wait for data) |
| 2 | Target lookup: `GetEmotionEnglishName(_expectedEmotion)` → `targetEmotionStr` | `return` if null/empty |
| 3 | `_emotionBridge.IsMatchingEmotion(targetEmotionStr)` | Keep polling (not matched yet) |
| 4 | `_actionAlreadyRegistered` | `return` |
| 5 | `!_commandContainsSimonDice` | Fire `OnPlayerTricked(targetEmotionStr)` |
| 6 | *(passed all)* | Fire `OnEmotionMatched(targetEmotionStr)` |

`IsMatchingEmotion(string, float)` checks:
```
IsConnected && FaceDetected
  → Map name to EmotionType via _map dict
    → CurrentEmotion == targetType && Confidence >= minConfidence (default 0.40)
```

### 3.4 Path D: Timeout

[`CoResponseTimer()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:459)

Counts down from `_responseTimePerRound` (5s) every frame (respecting pause). When timer reaches 0:

```
HandleTimeout()
  → Guards: _roundAlreadyJudged, phase == WaitResponse
  → Debug snapshot (once per game session)
  → JudgeRound(playerAction: null, timedOut: true, isEmotion: …)
```

---

## 4. The Judgment — `JudgeRound()` Truth Table

[`JudgeRound()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:661)

| Command Type | Player Action | timedOut | Result | Score |
|---|---|---|---|---|
| `ContainsSimonDice = true` | Correct gesture | false | `Correct` | `_correctStreak++`, `_currentRound++` |
| `ContainsSimonDice = true` | **Wrong** gesture | false | `WrongGesture` | Game Over |
| `ContainsSimonDice = true` | (none) | true | `Timeout` | Game Over |
| `ContainsSimonDice = false` | Stayed neutral | true | `Correct` | `_correctStreak++`, `_currentRound++` |
| `ContainsSimonDice = false` | Did ANY action | false | `WrongAction` | Game Over |

**Note:** When `ContainsSimonDice = false` and player does the CORRECT action, that case never reaches `JudgeRound()` — it's intercepted by `HandlePlayerTricked` / `HandleEmotionMatched` → trick check → `OnPlayerTricked` fires → `ShowTrickedFeedback()` → immediate Game Over with "tricked" message.

### 4.1 Gesture Match Comparison

```csharp
string expectedGesture = _currentCommand.GestureTarget.ToString();  // "OpenHand" or "ClosedFist"
string.Equals(playerAction, expectedGesture, StringComparison.OrdinalIgnoreCase)
```

`playerAction` = the `gestureName` string from `HandlePlayerAction`, which is the raw string from `GestureDetector.CurrentDetectedGesture` at the time the judge fired.

**Verdict:** ✅ Correct. Both sides use the same `ToString()` representation of the `SimonGestureTarget` enum.

### 4.2 Emotion Match Comparison — 🔴 BUG CONFIRMED

```csharp
// In HandleEmotionMatched (SimonJudge.Update line 257):
OnEmotionMatched?.Invoke(targetEmotionStr);
// targetEmotionStr = SimonCommandGenerator.GetEmotionEnglishName(_expectedEmotion)
//                  = "happy", "angry", "sad", "neutral", or "surprise"  ← ENGLISH

// In JudgeRound (SimonGame line 682):
string expectedEmotion = SimonCommandGenerator.GetEmotionDisplayName(_currentCommand.EmotionTarget);
//                     = "feliz", "enojado", "triste", "neutral", or "sorprendido"  ← SPANISH
string.Equals(playerAction, expectedEmotion, StringComparison.OrdinalIgnoreCase)
//             "happy"      "feliz"      ← ALWAYS FALSE!
```

**Result:** Emotion rounds can NEVER produce `RoundResult.Correct`. The emotion will always be judged as `WrongGesture` even when the player shows the correct emotion. The only way an emotion round can "pass" is if `ContainsSimonDice = false` and the player times out (doing nothing = correct).

---

## 5. All Parameters, Flags, and Thresholds

### 5.1 Command Parameters (set once per round)

| Parameter | Source | Possible Values |
|---|---|---|
| `_currentCommand.ActionType` | [`SimonCommandGenerator`](Assets/Scripts/Minigames/Simon/SimonCommandGenerator.cs:238) | `Gesture` (60%), `Emotion` (40%) |
| `_currentCommand.GestureTarget` | Generator | `OpenHand`, `ClosedFist` |
| `_currentCommand.ExpectedZone` | Generator | `UpLeft`, `UpRight`, `DownLeft`, `DownRight`, `Center` (gesture rounds) / `None` (emotion rounds) |
| `_currentCommand.EmotionTarget` | Generator | `Happy`, `Angry`, `Sad`, `Neutral`, `Surprise` |
| `_currentCommand.ContainsSimonDice` | Generator (pre-planned) | `true` (3-4 rounds), `false` (1-2 rounds) |
| `_currentCommand.SaysSimonDice` | Generator | Always equals `ContainsSimonDice` (v6 fix) |

### 5.2 Judge Configuration (set by SimonGame before StartMonitoring)

| Parameter | Set Via | Purpose |
|---|---|---|
| `_expectedGesture` | `SetExpectedGesture()` | Continuous polling comparison target |
| `_expectedZone` | `SetExpectedZone()` | Zone validation in both event + polling paths |
| `_commandContainsSimonDice` | `SetSimonDiceFlag()` | Trick/obey branching |
| `_isEmotionRound` + `_expectedEmotion` | `SetEmotionRound()` | Switches judge to emotion polling mode |

### 5.3 Guard Flags (prevent double-evaluation)

| Flag | Location | Set True When | Reset When |
|---|---|---|---|
| `_roundAlreadyJudged` | `SimonGame` | Any handler receives valid action/emotion/timeout | `StartRound()` |
| `_actionAlreadyRegistered` | `SimonJudge` | Judge fires OnPlayerAction/OnPlayerTricked/OnEmotionMatched | `StartMonitoring()`, `ResetState()` |
| `_phase == WaitResponse` | `SimonGame` | `BeginResponsePhase()` | Any handler → `Judging` |
| `_isMonitoring` | `SimonJudge` | `StartMonitoring()` | `StopMonitoring()`, `OnDestroy()` |

### 5.4 Timing Parameters

| Parameter | Value | Where |
|---|---|---|
| `_commandDisplayDuration` | 2.5s | Reading time before response phase |
| `_responseTimePerRound` | 5s | Time to perform action after display |
| `_feedbackDuration` | 2s | Correct/Wrong visual feedback duration |
| `_roundTransitionDelay` | 1.5s | Gap between feedback and next round |
| `GestureDetector._requiredStableFrames` | 5 | Gesture must be stable 5 frames before transition event fires |
| `HandZoneClassifier._debounceFrames` | 4 | Hand must stay in zone 4 frames before CurrentZone updates |
| `SimonJudge.EmotionPollInterval` | 0.25s (250ms) | How often emotion bridge is polled |

### 5.5 Detection Thresholds

| Threshold | Value | Where |
|---|---|---|
| `IsMatchingEmotion minConfidence` | 0.40 | Emotion must exceed this confidence to count as "matching" |
| `HandZoneClassifier._leftThreshold` | 0.5 | X < 0.5 = Left |
| `HandZoneClassifier._rightThreshold` | 0.5 | X > 0.5 = Right |
| `HandZoneClassifier._upThreshold` | 0.5 | Y < 0.5 = Up (MediaPipe: small Y = high) |
| `HandZoneClassifier._downThreshold` | 0.5 | Y > 0.5 = Down |
| `HandZoneClassifier._centerRange` | 0.15 | ±0.15 from (0.5, 0.5) = Center |
| `_requirePalmFacingCamera` | `true` | OpenHand requires palm toward camera |
| `_palmHandAssumption` | `Auto` | Skips palm check (no handedness data) |

---

## 6. Potential Issues & Diagnosis

### 🔴 Bug #1: Emotion Rounds Can Never Score `Correct`

**Location:** [`SimonGame.JudgeRound()`](Assets/Scripts/Minigames/Simon/SimonGame.cs:682)

**Root cause:** `HandleEmotionMatched` passes the **English** emotion name (e.g., `"happy"`) as `playerAction`, but `JudgeRound` compares it against the **Spanish** display name (e.g., `"feliz"`).

**Impact:** Every emotion round where the player correctly shows the emotion AND the command contains "Simón dice" is wrongly judged as `WrongGesture` → Game Over.

**Fix:** Align the comparison strings. Either:
- (A) Pass the English name through consistently, or
- (B) Compare `EmotionTarget` enum values instead of strings, or
- (C) Use `GetEmotionEnglishName()` in `JudgeRound()` instead of `GetEmotionDisplayName()`.

### 🟡 Mitigation #1: Gesture-Zone Race Condition (Already Partially Fixed)

**Location:** [`SimonJudge.HandleGestureDetected()`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:306) + [`Update()`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:260)

**Problem:** When player rapidly moves hand to zone AND forms gesture simultaneously:
1. `GestureDetector` fires `OnGestureDetected("OpenHand")` after 5-frame debounce
2. `HandleGestureDetected` checks zone → `HandZoneClassifier.IsInZone()` → zone debounce (4 frames) may not have settled → **rejects gesture**
3. But `Update()` continuous polling catches the match ~2 frames later when zone settles → fires correctly

**Status:** The v6 continuous-polling safety net mitigates this, but it depends on the gesture being held long enough for the zone to settle. If the player forms the gesture and releases quickly (<4 frames), the detection fails silently.

**Residual risk:** Player flashes a correct gesture in the correct zone faster than 4 frames → gesture event fires, zone check fails, and by the time zone settles the gesture is gone → continuous polling sees wrong/no gesture → timeout.

### 🟡 Mitigation #2: Double-Fire Prevention

**Location:** Both `HandleGestureDetected` and `Update()` set `_actionAlreadyRegistered = true` before firing. The one that fires first blocks the other.

**Status:** ✅ Correct. Both paths check `_actionAlreadyRegistered` before proceeding.

### 🟡 Observation: No "Round Won on Timeout for No-Simon-Dice"

When `ContainsSimonDice = false` and the player does nothing, `HandleTimeout()` fires after 5s → `JudgeRound(null, timedOut: true, …)` → `ContainsSimonDice = false && timedOut = true` → `Correct`. This works correctly.

### 🟡 Observation: `SaysSimonDice` vs `ContainsSimonDice`

Since v6, these two flags are always equal (line 233 of `SimonCommandGenerator`). `SaysSimonDice` is used for UI color coding (green vs orange), while `ContainsSimonDice` is used for judgment. There is no discrepancy risk.

### 🟡 Observation: `_emotionPollTimer` Reset After Rejection

In emotion polling (`Update()` line 232): `_emotionPollTimer` resets to 0 after exceeding interval. If `IsMatchingEmotion` returns false, it keeps polling every 250ms indefinitely until timer expires or match occurs. No state leak between rounds — `ResetState()` clears `_emotionPollTimer`.

---

## 7. Common Simon-Says Detection Pitfalls (Reference)

| Pitfall | Present in this code? | Notes |
|---|---|---|
| Premature checking (judging before player finishes acting) | ❌ No | Event-driven + continuous polling — checks on every frame/event |
| Off-by-one in sequence comparison | N/A | No sequence to compare |
| Race condition: gesture fires before zone settles | ✅ Yes | Mitigated by v6 continuous polling, but residual risk remains for very fast gestures |
| String comparison mismatch (case, language) | 🔴 Yes | Emotion rounds: English vs Spanish mismatch |
| Missing reset between rounds | ❌ No | `ResetState()`, `_roundAlreadyJudged = false` in `StartRound()` |
| Async LLM generation delaying timer start | ❌ No | Timer starts in `BeginResponsePhase()` after `OnCommandReady()` callback |
| Double-fire (same action evaluated twice) | ❌ No | `_actionAlreadyRegistered` + `_roundAlreadyJudged` dual guards |
| Pre-held gesture falsely detected as new action | ❌ No | `_baselineGesture` captured at monitoring start |
| "None" treated as valid gesture | ❌ No | Filtered out at step 3 of HandleGestureDetected |
| Zone flicker causing spurious rejections | ❌ No | 4-frame debounce on zone |
| Emotion confidence too low | ⚠️ Possible | Default threshold 0.40 — DeepFace accuracy varies; adjustable |

---

## 8. Proposed Fix Plan

### Fix 1: Emotion Round Judgment (Critical)

**File:** [`Assets/Scripts/Minigames/Simon/SimonGame.cs`](Assets/Scripts/Minigames/Simon/SimonGame.cs:679-690)

**Change:** In `JudgeRound()`, change the emotion comparison to use `GetEmotionEnglishName()` instead of `GetEmotionDisplayName()`, matching the value passed by `HandleEmotionMatched`.

```csharp
// BEFORE (line 682):
string expectedEmotion = SimonCommandGenerator.GetEmotionDisplayName(_currentCommand.EmotionTarget);

// AFTER:
string expectedEmotion = SimonCommandGenerator.GetEmotionEnglishName(_currentCommand.EmotionTarget);
```

**Risk:** Minimal — only changes the string used for comparison; both are deterministic lookups from the same enum value.

### Fix 2 (Optional): Reduce Zone Race-Condition Window

**File:** [`Assets/Scripts/Hand/HandZoneClassifier.cs`](Assets/Scripts/Hand/HandZoneClassifier.cs:62)

**Change:** Reduce `_debounceFrames` from 4 to 3 or 2, or add a "raw zone" accessor that skips debounce for use in the judge's event path.

**Risk:** Lower debounce increases zone flickering. The v6 continuous polling already mitigates this race, so this fix is lower priority.

### Fix 3 (Optional): Add Raw-Zone Fallback in Judge

**File:** [`Assets/Scripts/Minigames/Simon/SimonJudge.cs`](Assets/Scripts/Minigames/Simon/SimonJudge.cs:330)

**Change:** In `HandleGestureDetected()`, if the debounced `IsInZone()` check fails, also check the raw (undebounced) zone before rejecting. If raw zone matches, treat as accepted.

**Risk:** Complexity. The v6 continuous polling already handles this more cleanly.

---

## 9. Questions for Feedback

1. **Emotion bug:** Should the fix use English names throughout (matching `HandleEmotionMatched`'s `targetEmotionStr`) or should I refactor to compare `SimonEmotionTarget` enum values directly (eliminating string comparison entirely)?

2. **Zone race condition:** Is the v6 continuous-polling safety net performing adequately in playtesting, or are there still cases where the player correctly gestures in the right zone but the game doesn't register it?

3. **Is this analysis missing any code path you're concerned about?** Specifically, are there any scenarios where the player believes they should have won a round but the game disagrees?

---

*ARcade Rush — Simon Round Detection Analysis · 2026-06-01*
