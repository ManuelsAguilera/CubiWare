using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARcadeRush.EmotionDetection
{
    // ── Data structs (match Python server JSON keys exactly) ─────────────────

    [Serializable]
    public struct EmotionData
    {
        public string dominant_emotion;
        public float  confidence;
        public bool   face_detected;
        public EmotionScores scores;
        public double timestamp;
    }

    [Serializable]
    public struct EmotionScores
    {
        public float angry;
        public float disgust;
        public float fear;
        public float happy;
        public float sad;
        public float surprise;
        public float neutral;
    }

    // ── Enums ─────────────────────────────────────────────────────────────────

    public enum EmotionType { Angry, Disgust, Fear, Happy, Sad, Surprise, Neutral, Unknown }

    public enum EmotionDetectionMode
    {
        /// <summary>CurrentEmotion updates on every received message from the server.</summary>
        AutoInterval,
        /// <summary>
        /// Silent until OpenDetectionWindow() is called. During the window, incoming
        /// messages vote for an emotion. OnWindowClosed fires with the dominant result.
        /// </summary>
        Window
    }

    // ── EmotionGameBridge ─────────────────────────────────────────────────────

    /// <summary>
    /// Translates raw DeepFace WebSocket data into a clean, mode-aware game API.
    ///
    /// Initialized by BootstrapManager. Accessed via EmotionGameBridge.Instance
    /// or through MiniGameDependencies.EmotionBridge.
    ///
    /// Two detection modes:
    ///   AutoInterval — CurrentEmotion updates continuously with each server message.
    ///   Window       — Reads nothing until OpenDetectionWindow() is called; accumulates
    ///                  votes during the window; OnWindowClosed fires with the winner.
    ///
    /// Usage example (AutoInterval):
    ///   _deps.EmotionBridge.SetMode(EmotionDetectionMode.AutoInterval);
    ///   _deps.EmotionBridge.OnEmotionChanged += (e, conf) => Debug.Log(e);
    ///
    /// Usage example (Window):
    ///   _deps.EmotionBridge.SetMode(EmotionDetectionMode.Window);
    ///   _deps.EmotionBridge.OnWindowClosed += dominant => HandleResult(dominant);
    ///   _deps.EmotionBridge.OpenDetectionWindow(3f);
    /// </summary>
    public class EmotionGameBridge : MonoBehaviour
    {
        public static EmotionGameBridge Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────
        public EmotionType  CurrentEmotion  { get; private set; } = EmotionType.Unknown;
        public float        Confidence      { get; private set; }
        public bool         FaceDetected    { get; private set; }
        public bool         IsConnected     => _client != null && _client.IsConnected;
        public EmotionDetectionMode DetectionMode { get; private set; } = EmotionDetectionMode.AutoInterval;
        public bool         IsWindowOpen    { get; private set; }
        public bool         IsEnabled       { get; private set; } = true;

        /// <summary>Raw data from the last server message (all 7 scores, timestamp, face_detected).</summary>
        public EmotionData  LatestData      { get; private set; }

        // backing field kept in sync with the public property
        private EmotionData _latestData;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fires when CurrentEmotion changes (AutoInterval mode).</summary>
        public event Action<EmotionType, float> OnEmotionChanged;

        /// <summary>Fires when a Window session closes, with the dominant emotion.</summary>
        public event Action<EmotionType> OnWindowClosed;

        // ── Window state ──────────────────────────────────────────────────────
        private float   _windowDuration;
        private float   _windowTimer;
        private float[] _windowAccum = new float[8]; // indexed by (int)EmotionType

        // ── Client reference (injected by Bootstrap) ──────────────────────────
        private EmotionWebSocketClient _client;

        // ── String → EmotionType mapping ──────────────────────────────────────
        private static readonly Dictionary<string, EmotionType> _map = new()
        {
            ["angry"]    = EmotionType.Angry,
            ["disgust"]  = EmotionType.Disgust,
            ["fear"]     = EmotionType.Fear,
            ["happy"]    = EmotionType.Happy,
            ["sad"]      = EmotionType.Sad,
            ["surprise"] = EmotionType.Surprise,
            ["neutral"]  = EmotionType.Neutral,
        };

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (_windowOpen_internal) TickWindow();
        }

        private void OnDestroy()
        {
            if (_client != null) _client.OnEmotionReceived -= HandleData;
        }

        // ── Bootstrap initializer ─────────────────────────────────────────────

        /// <summary>Called by BootstrapManager after creating the WebSocket client.</summary>
        public void Initialize(EmotionWebSocketClient client)
        {
            _client = client;
            _client.OnEmotionReceived += HandleData;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Enable or disable emotion processing. When disabled, CurrentEmotion stays Unknown.</summary>
        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            if (!enabled) ResetState();
        }

        /// <summary>Switch detection mode. Closes any open window.</summary>
        public void SetMode(EmotionDetectionMode mode)
        {
            if (_windowOpen_internal) CloseWindow(forceClose: true);
            DetectionMode = mode;
        }

        /// <summary>
        /// Opens a detection window. Only relevant in Window mode.
        /// The window accumulates votes for the specified duration, then fires OnWindowClosed.
        /// Calling this while a window is open restarts it.
        /// </summary>
        public void OpenDetectionWindow(float duration = 3f)
        {
            _windowDuration = duration;
            _windowTimer    = 0f;
            _windowOpen_internal = true;
            IsWindowOpen    = true;
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);
        }

        /// <summary>Returns the smoothed score (0-1) for a specific emotion from the last server message.</summary>
        public float GetScore(EmotionType emotion)
        {
            return emotion switch
            {
                EmotionType.Angry   => _latestData.scores.angry,
                EmotionType.Disgust => _latestData.scores.disgust,
                EmotionType.Fear    => _latestData.scores.fear,
                EmotionType.Happy   => _latestData.scores.happy,
                EmotionType.Sad     => _latestData.scores.sad,
                EmotionType.Surprise=> _latestData.scores.surprise,
                EmotionType.Neutral => _latestData.scores.neutral,
                _                   => 0f
            };
        }

        /// <summary>True if the specified emotion score exceeds the threshold AND the server is connected.</summary>
        public bool IsEmotionActive(EmotionType emotion, float threshold = 0.55f)
        {
            if (!IsConnected || !FaceDetected) return false;
            return GetScore(emotion) >= threshold;
        }

        // ── Phase 3: Emotion Matching & Query API ────────────────────────────

        /// <summary>
        /// Emotions verified as consistently detectable with DeepFace.
        /// Disgust and Fear are excluded due to low accuracy (~30-55%).
        /// </summary>
        public static readonly EmotionType[] VerifiedEmotions =
        {
            EmotionType.Happy,
            EmotionType.Angry,
            EmotionType.Sad,
            EmotionType.Neutral,
            EmotionType.Surprise
        };

        /// <summary>Returns true if the emotion is in the verified whitelist.</summary>
        public static bool IsVerified(EmotionType emotion)
        {
            for (int i = 0; i < VerifiedEmotions.Length; i++)
                if (VerifiedEmotions[i] == emotion) return true;
            return false;
        }

        /// <summary>
        /// Checks if the currently detected emotion matches the target.
        /// Returns false (no throw) if server unavailable or face not detected.
        /// </summary>
        /// <param name="targetEmotion">Expected emotion (case-insensitive name match).</param>
        /// <param name="minConfidence">Minimum confidence threshold (0-1). Default 0.40.</param>
        public bool IsMatchingEmotion(string targetEmotion, float minConfidence = 0.40f)
        {
            if (!IsConnected || !FaceDetected) return false;

            if (!_map.TryGetValue(targetEmotion.ToLowerInvariant(), out var targetType))
                return false;

            return IsMatchingEmotion(targetType, minConfidence);
        }

        /// <summary>
        /// Checks if the currently detected emotion matches the target EmotionType.
        /// Returns false (no throw) if server unavailable or face not detected.
        /// </summary>
        public bool IsMatchingEmotion(EmotionType target, float minConfidence = 0.40f)
        {
            if (!IsConnected || !FaceDetected) return false;
            if (target == EmotionType.Unknown) return false;

            return CurrentEmotion == target && Confidence >= minConfidence;
        }

        /// <summary>Returns the emotion type with the highest confidence score from the latest data.</summary>
        public string GetCurrentDominantEmotion()
        {
            return _latestData.dominant_emotion ?? "neutral";
        }

        /// <summary>Returns the confidence (0-1) for a specific emotion name (lowercase).</summary>
        public float GetEmotionConfidence(string emotionName)
        {
            if (string.IsNullOrEmpty(emotionName)) return 0f;
            return GetScore(emotionName);
        }

        /// <summary>Returns the confidence (0-1) for a specific EmotionType.</summary>
        public float GetEmotionConfidence(EmotionType emotion)
        {
            return GetScore(emotion);
        }

        /// <summary>Returns the list of emotion names the server supports (all 7 raw emotions).</summary>
        public string[] GetAvailableEmotions()
        {
            return new string[] { "angry", "disgust", "fear", "happy", "sad", "surprise", "neutral" };
        }

        /// <summary>Overload: GetScore by string name (lowercase).</summary>
        public float GetScore(string emotionName)
        {
            return emotionName.ToLowerInvariant() switch
            {
                "angry"    => _latestData.scores.angry,
                "disgust"  => _latestData.scores.disgust,
                "fear"     => _latestData.scores.fear,
                "happy"    => _latestData.scores.happy,
                "sad"      => _latestData.scores.sad,
                "surprise" => _latestData.scores.surprise,
                "neutral"  => _latestData.scores.neutral,
                _          => 0f
            };
        }

        /// <summary>Resets all state to initial values.</summary>
        public void ResetState()
        {
            CurrentEmotion = EmotionType.Unknown;
            Confidence     = 0f;
            FaceDetected   = false;
            _frameEmotion  = EmotionType.Unknown;
            _windowOpen_internal = false;
            IsWindowOpen   = false;
            _windowTimer   = 0f;
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private bool        _windowOpen_internal;
        // The emotion detected in the current frame from the server.
        // Updated every HandleData() call regardless of mode, so TickWindow()
        // always has a fresh per-frame value to accumulate — not a stale CurrentEmotion.
        private EmotionType _frameEmotion = EmotionType.Unknown;

        private void HandleData(EmotionData data)
        {
            if (!IsEnabled) return;

            _latestData  = data;
            LatestData   = data;
            FaceDetected = data.face_detected;
            Confidence   = data.confidence;

            if (!_map.TryGetValue(data.dominant_emotion, out var incoming))
                incoming = EmotionType.Unknown;

            // Always track the per-frame emotion so Window mode can accumulate correctly.
            _frameEmotion = incoming;

            if (DetectionMode == EmotionDetectionMode.AutoInterval)
            {
                if (incoming != CurrentEmotion)
                {
                    CurrentEmotion = incoming;
                    OnEmotionChanged?.Invoke(CurrentEmotion, Confidence);
                }
                else
                {
                    CurrentEmotion = incoming;
                }
            }
            // Window mode: _frameEmotion is used by TickWindow(); CurrentEmotion is
            // not updated until the window closes with the dominant result.
        }

        private void TickWindow()
        {
            _windowTimer += Time.deltaTime;

            // Accumulate time for the emotion detected THIS frame (not the stale CurrentEmotion).
            if (IsConnected && FaceDetected && _frameEmotion != EmotionType.Unknown)
            {
                int idx = (int)_frameEmotion;
                if (idx >= 0 && idx < _windowAccum.Length)
                    _windowAccum[idx] += Time.deltaTime;
            }

            if (_windowTimer >= _windowDuration)
                CloseWindow(forceClose: false);
        }

        private void CloseWindow(bool forceClose)
        {
            _windowOpen_internal = false;
            IsWindowOpen         = false;
            _windowTimer         = 0f;

            if (forceClose) return;

            // Find dominant emotion (most accumulated time)
            EmotionType dominant = EmotionType.Neutral;
            float maxTime = 0f;
            for (int i = 0; i < _windowAccum.Length; i++)
            {
                if (_windowAccum[i] > maxTime) { maxTime = _windowAccum[i]; dominant = (EmotionType)i; }
            }
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);

            CurrentEmotion = dominant;
            OnWindowClosed?.Invoke(dominant);
            OnEmotionChanged?.Invoke(dominant, Confidence);
        }
    }
}
