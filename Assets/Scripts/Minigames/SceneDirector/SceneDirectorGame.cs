using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Face;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Main controller for the Scene Director minigame. Implements IMiniGame and
    /// wires together all element singletons:
    ///   ScenarioController  → curtain open/close
    ///   AudienceController  → crowd reactions
    ///   CountdownController → per-emotion timer
    ///   ScriptController    → emotion sequence
    ///   CameraController    → webcam feed + AR mask
    ///   ApprovalBarController → fill/drain bar
    ///
    /// FULL STATE MACHINE
    ///   Idle → Bootstrapping → CurtainOpening → CountdownOverlay → ScriptReveal →
    ///   Playing (ShowEmotion ⇄ CountdownActive → BetweenElements → ShowEmotion) →
    ///   ElementFeedback → CurtainClosing → Results → Returning → Idle
    ///
    /// EMOTION PIPELINE (two data paths from EmotionGameBridge / DeepFace)
    ///   1. OnEmotionChanged (fires on dominant emotion change) → mask swap
    ///   2. CurrentEmotion (polled every frame, ~10fps from server) → approval bar
    ///
    /// EDITOR SIMULATION
    ///   #if UNITY_EDITOR: keyboard H/S/A/N keys update SimulatedEmotion on CameraController.
    ///   SceneDirectorGame.Update() reads it and drives mask + bar from a single point.
    /// </summary>
    public class SceneDirectorGame : MonoBehaviour, IMiniGame
    {
        // ── IMiniGame ─────────────────────────────────────────────────────────────
        /// <summary>Set to match Director.unity build index in EditorBuildSettings.</summary>
        public int SceneIndex => _sceneIndex;

        // ── Inspector — Scene ─────────────────────────────────────────────────────
        [Header("Scene Settings")]
        [SerializeField] private int   _sceneIndex           = 5;
        [SerializeField] private int   _mainMenuSceneIndex   = 1;
        [SerializeField] private float _betweenElementDelay  = 1.5f;
        [SerializeField] private float _endRoundDelayWin     = 3.0f;  // long enough for audience applaud (2.5s)
        [SerializeField] private float _endRoundDelayLose    = 2.0f;
        [SerializeField] private int   _pointsPerElement     = 100;

        [Header("LLM")]
        [Tooltip("When false, sequence and evaluation use local fallbacks. Toggle to test LLM.")]
        [SerializeField] private bool _useLLM = false;

        [Header("Editor Simulation")]
        [Tooltip("When true, keyboard H/S/A/N drives SimulatedEmotion (editor only).")]
        [SerializeField] private bool _editorSimulation = false;

        [Header("UI References — Overlays")]
        [SerializeField] private CanvasGroup _countdownOverlay;
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private CanvasGroup _feedbackOverlay;
        [SerializeField] private TextMeshProUGUI _feedbackOverlayText;
        [SerializeField] private CanvasGroup _resultsPanel;
        [SerializeField] private TextMeshProUGUI _resultsHeadline;
        [SerializeField] private TextMeshProUGUI _resultsEvaluation;
        [SerializeField] private TextMeshProUGUI _resultsScore;
        [SerializeField] private TextMeshProUGUI _resultsElementsPassed;
        [SerializeField] private CanvasGroup _pausePanel;
        [SerializeField] private TextMeshProUGUI _scoreText; // live HUD score

        // ── State Machine ─────────────────────────────────────────────────────────

        private enum GamePhase
        {
            Idle,
            Bootstrapping,
            CurtainOpening,
            CountdownOverlay,
            ScriptReveal,
            Playing,
            BetweenElements,
            ElementFeedback,
            CurtainClosing,
            Results,
            Returning
        }

        private GamePhase _phase = GamePhase.Idle;

        // Guard flags
        private bool _elementResolved;
        private bool _roundEnding;
        private bool _resultsShowing;
        private bool _paused;

        private MiniGameDependencies _deps;
        private EmotionGameBridge _bridge;
        private EmotionLabel _lastSimulatedEmotion = EmotionLabel.Neutral;

        // Scoring
        private int  _elementsPassed;
        private float _elementActiveTime; // Time.time when current element started
        private List<float> _speedMultipliers = new List<float>();

        // LLM async
        private bool _sequenceReady;

        // Pause state snapshot
        private float _pauseStoredFillAmount;
        private float _pauseStoredCountdown;

        private const string LogServiceName = "SceneDirectorGame";

        // ── IMiniGame ─────────────────────────────────────────────────────────────

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps;
            _phase = GamePhase.Bootstrapping;

            // Cache EmotionGameBridge for frame-by-frame poll (replaces EmotionClassifier)
            _bridge = deps.EmotionBridge;

            // Bind webcam feed to the RawImage display
            if (_deps.Camera != null)
                CameraController.Instance?.BindFeed(_deps.Camera);

            // Enable emotion detection if live (not simulation)
            if (_bridge != null && !_editorSimulation)
                _bridge.SetEnabled(true);

            WireEvents();

            ServiceLogger.Instance.LogInfo(LogServiceName, "OnStart — opening curtain.");

            // Start ambient crowd murmur
            SceneDirectorAudio.Instance?.StartAmbient(0.3f);

            // Guard: ScenarioController might be unavailable during editor testing
            if (ScenarioController.Instance != null)
            {
                _phase = GamePhase.CurtainOpening;
                ScenarioController.Instance.Open();
                SceneDirectorAudio.Instance?.PlayCurtainOpen();
            }
            else
            {
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    "ScenarioController.Instance is null — skipping curtain, jumping to gameplay.");
                StartCoroutine(GenerateSequenceAsync());
            }
        }

        public void OnEnd()
        {
            _playingUpdateActive = false;

            if (_bridge != null)
                _bridge.SetEnabled(false);

            UnwireEvents();

            StopAllCoroutines();

            _deps?.GameManager?.EndGame();

            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadSceneDelayed(_mainMenuSceneIndex, 0.5f);
        }

        // ── Unity Lifecycle ─────────────────────────────────────────────────────

        private bool _playingUpdateActive;

        private void Update()
        {
            if (_phase != GamePhase.Playing || _paused)
            {
                _playingUpdateActive = false;
                return;
            }
            _playingUpdateActive = true;

            EmotionLabel activeEmotion = EmotionLabel.Neutral;

#if UNITY_EDITOR
            if (_editorSimulation)
            {
                // Query simulated emotion from CameraController
                activeEmotion = CameraController.Instance != null
                    ? CameraController.Instance.SimulatedEmotion
                    : EmotionLabel.Neutral;

                // In editor simulation, immediately set the mask emotion when it changes
                if (CameraController.Instance != null && activeEmotion != _lastSimulatedEmotion)
                {
                    _lastSimulatedEmotion = activeEmotion;
                    CameraController.Instance.SetMaskEmotion(activeEmotion);
                }
            }
            else
#endif
            if (_bridge != null)
            {
                // Poll current emotion from DeepFace bridge (updates at ~10fps, no hold/hysteresis)
                activeEmotion = ToEmotionLabel(_bridge.CurrentEmotion);
            }

            // Single point of entry for approval bar polling
            ApprovalBarController.Instance?.SetDetectedEmotion(activeEmotion);

            // Audience subtle reaction based on bar correctness
            if (ApprovalBarController.Instance != null && ApprovalBarController.Instance.IsActive)
            {
                if (ApprovalBarController.Instance.IsCorrect)
                {
                    AudienceController.Instance?.SlightMove();
                }
                else if (AudienceController.Instance?.CurrentState == AudienceController.AudienceState.SlightMove)
                {
                    AudienceController.Instance?.SetIdle();
                }
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            UnwireEvents();

            // Release GameManager if scene was force-unloaded mid-round
            if (_playingUpdateActive || _roundEnding)
                _deps?.GameManager?.EndGame();
        }

        // ── Event Wiring ──────────────────────────────────────────────────────────

        private void WireEvents()
        {
            if (ScenarioController.Instance != null)
            {
                ScenarioController.Instance.OnOpenComplete  += OnCurtainOpen;
                ScenarioController.Instance.OnCloseComplete += OnCurtainClose;
            }

            if (ScriptController.Instance != null)
            {
                ScriptController.Instance.OnElementStarted   += OnElementStarted;
                ScriptController.Instance.OnSequenceComplete += OnSequenceComplete;
            }

            if (ApprovalBarController.Instance != null)
            {
                ApprovalBarController.Instance.OnBarFilled  += OnBarFilled;
                ApprovalBarController.Instance.OnBarEmptied += OnBarEmptied;
            }

            if (CountdownController.Instance != null)
                CountdownController.Instance.OnCountdownExpired += OnCountdownExpired;

            // Wire EmotionGameBridge for confirmed mask swaps (live mode only)
            if (_bridge != null && !_editorSimulation)
            {
                _bridge.OnEmotionChanged += OnEmotionChangedAdapter;
            }

            // Subscribe to score changes for live HUD display
            if (_deps?.GameManager != null)
                _deps.GameManager.OnScoreChanged += OnScoreChanged;
        }

        private void UnwireEvents()
        {
            if (ScenarioController.Instance != null)
            {
                ScenarioController.Instance.OnOpenComplete  -= OnCurtainOpen;
                ScenarioController.Instance.OnCloseComplete -= OnCurtainClose;
            }

            if (ScriptController.Instance != null)
            {
                ScriptController.Instance.OnElementStarted   -= OnElementStarted;
                ScriptController.Instance.OnSequenceComplete -= OnSequenceComplete;
            }

            if (ApprovalBarController.Instance != null)
            {
                ApprovalBarController.Instance.OnBarFilled  -= OnBarFilled;
                ApprovalBarController.Instance.OnBarEmptied -= OnBarEmptied;
            }

            if (CountdownController.Instance != null)
                CountdownController.Instance.OnCountdownExpired -= OnCountdownExpired;

            if (_bridge != null)
                _bridge.OnEmotionChanged -= OnEmotionChangedAdapter;

            if (_deps?.GameManager != null)
                _deps.GameManager.OnScoreChanged -= OnScoreChanged;
        }

        // ── Emotion Bridge Callbacks ──────────────────────────────────────────────

        /// Adapter: converts EmotionType (DeepFace) → EmotionLabel (SceneDirector).
        /// Fires on each emotion change from EmotionGameBridge.
        private void OnEmotionChangedAdapter(EmotionType type, float confidence)
            => OnEmotionChanged_Confirmed(ToEmotionLabel(type));

        /// <summary>
        /// Drives AR mask swap only — bar polling uses CurrentEmotion in Update().
        /// </summary>
        private void OnEmotionChanged_Confirmed(EmotionLabel emotion)
        {
            CameraController.Instance?.SetMaskEmotion(emotion);
        }

        /// Maps DeepFace EmotionType to SceneDirector EmotionLabel.
        private static EmotionLabel ToEmotionLabel(EmotionType t) => t switch
        {
            EmotionType.Happy    => EmotionLabel.Happy,
            EmotionType.Surprise => EmotionLabel.Surprised,
            EmotionType.Angry    => EmotionLabel.Angry,
            EmotionType.Disgust  => EmotionLabel.Disgust,
            EmotionType.Fear     => EmotionLabel.Fear,
            EmotionType.Sad      => EmotionLabel.Sad,
            EmotionType.Neutral  => EmotionLabel.Neutral,
            _                    => EmotionLabel.Neutral,
        };

        // ── Curtain Flow ──────────────────────────────────────────────────────────

        private void OnCurtainOpen()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain open — starting countdown overlay.");
            StartCoroutine(GenerateSequenceAsync());
        }

        /// <summary>Audio hook: called each time the countdown overlay number changes.</summary>
        private void OnCountdownOverlayTick(string numberText)
        {
            if (numberText == "¡ACCIÓN!")
                SceneDirectorAudio.Instance?.PlayCountdownGo();
            else
                SceneDirectorAudio.Instance?.PlayCountdownTick();
        }

        /// <summary>
        /// Generates the sequence (LLM or local), plays countdown overlay,
        /// and waits for LLM latency if needed before starting gameplay.
        /// </summary>
        private IEnumerator GenerateSequenceAsync()
        {
            _sequenceReady = false;
            _phase = GamePhase.CountdownOverlay;

            // Start sequence generation (async LLM or local fallback)
            if (_useLLM && _deps?.LLM != null)
            {
                string systemPrompt =
                    "Eres un director de teatro dramático español. Hablas con pasión, usas " +
                    "metáforas teatrales, y te diriges al jugador como si fuera un actor en " +
                    "tu escenario. Tus respuestas son breves (máximo 2 oraciones), dramáticas, " +
                    "y siempre en español.";

                int seqLen = ScriptController.Instance != null ? ScriptController.Instance.SequenceLength : 3;
                string genPrompt = $"Genera una secuencia de {seqLen} emociones " +
                    "para una actuación teatral. Formato JSON exacto: " +
                    "{\"script\":[{\"e\":1,\"t\":5},{\"e\":2,\"t\":5},{\"e\":3,\"t\":5}],\"intro\":\"Tu texto introductorio\"}\n" +
                    "Donde e: 1=Happy 2=Surprised 3=Angry, t: segundos de límite (3-8).";

                _deps.LLM.Ask(systemPrompt, genPrompt,
                    onComplete: json =>
                    {
                        if (ScriptLLMParser.TryParse(json, out var elements, out var intro))
                        {
                            ScriptController.Instance.LoadSequence(elements);
                            ScriptController.Instance.ShowIntro(intro);
                        }
                        else
                        {
                            LoadFallbackSequence();
                        }
                        _sequenceReady = true;
                    },
                    onError: _ =>
                    {
                        LoadFallbackSequence();
                        _sequenceReady = true;
                    }
                );
            }
            else
            {
                // Local fallback — instant
                LoadFallbackSequence();
                _sequenceReady = true;
            }

            // Play countdown overlay: 3 → 2 → 1 → (wait for LLM) → ¡ACCIÓN!
            yield return StartCoroutine(PlayCountdownOverlay());

            // If LLM is still generating, show loading state
            if (!_sequenceReady)
            {
                SetCountdownText("Cargando guion...");
                yield return new WaitUntil(() => _sequenceReady);
            }
            else
            {
                SetCountdownText("¡ACCIÓN!");
                OnCountdownOverlayTick("¡ACCIÓN!");
                yield return new WaitForSeconds(0.5f);
            }

            // Hide overlay and intro, start gameplay
            SetCanvasGroupAlpha(_countdownOverlay, 0f);
            ScriptController.Instance?.HideIntro();

            // Start BGM and transition ambient
            SceneDirectorAudio.Instance?.StartBGM();

            _phase = GamePhase.Playing;
        }

        private IEnumerator PlayCountdownOverlay()
        {
            if (_countdownOverlay == null || _countdownText == null) yield break;

            SetCanvasGroupAlpha(_countdownOverlay, 1f);

            SetCountdownText("3");
            OnCountdownOverlayTick("3");
            yield return new WaitForSeconds(1f);

            SetCountdownText("2");
            OnCountdownOverlayTick("2");
            yield return new WaitForSeconds(1f);

            SetCountdownText("1");
            OnCountdownOverlayTick("1");
            yield return new WaitForSeconds(1f);

            // LLM gating happens in GenerateSequenceAsync after this returns
        }

        private void SetCountdownText(string text)
        {
            if (_countdownText != null)
                _countdownText.text = text;
        }

        // ── Element Flow ──────────────────────────────────────────────────────────

        private void OnElementStarted(ScriptElement element)
        {
            StartCoroutine(ActivateElementWithDelay(element));
        }

        private IEnumerator ActivateElementWithDelay(ScriptElement element)
        {
            // Reset guard and phase FIRST — before any yield — prevents stale events
            _elementResolved = false;
            _phase = GamePhase.BetweenElements;

            // Skip delay for the first element (no previous audience reaction to wait for)
            if (ScriptController.Instance != null && ScriptController.Instance.CurrentIndex > 0)
                yield return new WaitForSeconds(_betweenElementDelay);

            // If EndRound started during the delay, abort
            if (_roundEnding) yield break;

            _phase = GamePhase.Playing;
            _elementActiveTime = Time.time;

            AudienceController.Instance?.SetIdle();
            CountdownController.Instance.StartCountdown(element.TimeLimit);
            ApprovalBarController.Instance.Activate(element.RequiredEmotion);

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"Element active: {element.RequiredEmotion} ({element.TimeLimit}s)");
        }

        // ── Resolution — single entry point, guarded against double-fire ──────────

        private void OnBarFilled()  => ResolveElement(passed: true);
        private void OnBarEmptied() => ResolveElement(passed: false);

        private void OnCountdownExpired()
        {
            // Deactivate bar first so it doesn't also fire OnBarEmptied
            ApprovalBarController.Instance.Deactivate();
            ResolveElement(passed: false);
        }

        private void ResolveElement(bool passed)
        {
            if (_elementResolved) return;
            _elementResolved = true;

            CountdownController.Instance.Stop();
            ApprovalBarController.Instance.Deactivate();

            if (passed)
            {
                // Calculate speed bonus
                float fillTime = Time.time - _elementActiveTime;
                float timeLimit = ScriptController.Instance.IsActive
                    ? ScriptController.Instance.CurrentElement.TimeLimit : 5f;
                float percentUsed = fillTime / Mathf.Max(timeLimit, 0.1f);

                float multiplier = 1.0f;
                if (percentUsed < 0.4f) multiplier = 1.5f;
                else if (percentUsed < 0.7f) multiplier = 1.2f;

                _speedMultipliers.Add(multiplier);
                _elementsPassed++;

                int points = Mathf.RoundToInt(_pointsPerElement * multiplier);
                _deps?.GameManager?.AddScore(points);

                ServiceLogger.Instance.LogInfo(LogServiceName,
                    $"Element PASSED — {points} pts (×{multiplier:F1}, {percentUsed:P0} of time)");

                SceneDirectorAudio.Instance?.PlayCorrectChime();
                SceneDirectorAudio.Instance?.PlayApplause();
                AudienceController.Instance?.ReactPositive();
                ScriptController.Instance.PassCurrentElement();
                // If more elements remain, OnElementStarted fires → ActivateElementWithDelay handles it.
                // If sequence is done, OnSequenceComplete fires.
            }
            else
            {
                ServiceLogger.Instance.LogInfo(LogServiceName, "Element FAILED.");

                SceneDirectorAudio.Instance?.PlayWrongBuzzer();
                SceneDirectorAudio.Instance?.PlayBoo();
                SceneDirectorAudio.Instance?.PlayTomatoSplat();
                TomatoSplatController.Instance?.SplatBurst(3);
                ScriptController.Instance.FailCurrentElement();
                AudienceController.Instance?.ReactNegative();

                // Show brief "¡FALLADO!" feedback
                StartCoroutine(ShowElementFeedback(false));

                // End round (will wait through feedback then close curtain)
                StartCoroutine(EndRound(won: false));
            }
        }

        // ── Element Feedback Overlay ─────────────────────────────────────────────

        private IEnumerator ShowElementFeedback(bool passed)
        {
            if (_feedbackOverlay == null || _feedbackOverlayText == null) yield break;

            _feedbackOverlayText.text = passed ? "¡CORRECTO!" : "¡FALLADO!";
            _feedbackOverlayText.color = passed ? Color.green : Color.red;

            // Fade in 0.2s
            yield return StartCoroutine(FadeCanvasGroup(_feedbackOverlay, 0f, 1f, 0.2f));
            yield return new WaitForSeconds(0.4f);
            // Fade out 0.2s
            yield return StartCoroutine(FadeCanvasGroup(_feedbackOverlay, 1f, 0f, 0.2f));
        }

        // ── Round End ──────────────────────────────────────────────────────────────

        private void OnSequenceComplete()
        {
            // Show brief "¡CORRECTO!" for the final element before ending
            StartCoroutine(ShowElementFeedback(true));
            StartCoroutine(EndRound(won: true));
        }

        /// <summary>
        /// Entry point that guards against double-fire, then re-launches
        /// the body coroutine after StopAllCoroutines.
        /// </summary>
        private IEnumerator EndRound(bool won)
        {
            if (_roundEnding) yield break;
            _roundEnding = true;
            _phase = GamePhase.ElementFeedback;

            // Cancel ActivateElementWithDelay if it's still waiting in its yield
            // StopAllCoroutines kills this coroutine too, so immediately re-launch body
            StopAllCoroutines();
            StartCoroutine(EndRoundBody(won));
        }

        private IEnumerator EndRoundBody(bool won)
        {
            _playingUpdateActive = false;

            ServiceLogger.Instance.LogInfo(LogServiceName, won ? "Round WON." : "Round LOST.");

            // Wait for feedback overlay + audience reaction to finish
            float delay = won ? _endRoundDelayWin : _endRoundDelayLose;
            yield return new WaitForSeconds(delay);

            // Generate evaluation text and await it (LLM or hardcoded)
            string evaluation;
            if (_useLLM && _deps?.LLM != null)
            {
                string evalPrompt = won
                    ? $"El jugador ganó la actuación teatral. Pasó {_elementsPassed} de {ScriptController.Instance.TotalElements} emociones. " +
                      $"Evalúa su actuación en UNA oración dramática en español."
                    : $"El jugador perdió. Solo pasó {_elementsPassed} emociones. " +
                      $"Evalúa su actuación en UNA oración dramática en español.";

                string evalSystem = "Eres un director de teatro dramático español. Responde con UNA sola oración en español.";

                bool evalReady = false;
                string llmEval = string.Empty;
                _deps.LLM.Ask(evalSystem, evalPrompt,
                    onComplete: result =>
                    {
                        llmEval = result;
                        evalReady = true;
                    },
                    onError: _ =>
                    {
                        llmEval = GetHardcodedEvaluation(won);
                        evalReady = true;
                    }
                );
                // Await async LLM response before proceeding
                yield return new WaitUntil(() => evalReady);
                evaluation = llmEval;
            }
            else
            {
                evaluation = GetHardcodedEvaluation(won);
            }

            ShowEvaluationText(won, evaluation);

            // Populate ResultsPanel data (shown after curtain closes)
            PopulateResultsPanel(won, evaluation);

            _phase = GamePhase.CurtainClosing;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Closing curtain.");

            // Play win/lose sting and stop BGM
            if (won)
                SceneDirectorAudio.Instance?.PlayStingWin();
            else
                SceneDirectorAudio.Instance?.PlayStingLose();

            SceneDirectorAudio.Instance?.StopAmbient(1.5f);
            TomatoSplatController.Instance?.ClearAllSplats();

            ScenarioController.Instance?.Close();
            SceneDirectorAudio.Instance?.PlayCurtainClose();
        }

        private void OnCurtainClose()
        {
            if (_resultsShowing) return;
            _resultsShowing = true;

            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain closed — showing results.");
            _phase = GamePhase.Results;

            // Fade in ResultsPanel over closed curtain
            if (_resultsPanel != null)
                StartCoroutine(FadeCanvasGroup(_resultsPanel, 0f, 1f, 0.5f));
        }

        // ── Results Panel ──────────────────────────────────────────────────────────

        private void PopulateResultsPanel(bool won, string evaluation)
        {
            float avgSpeedBonus = _speedMultipliers.Count > 0 ? _speedMultipliers.Average() : 0f;
            int totalElements = ScriptController.Instance != null
                ? ScriptController.Instance.TotalElements : 0;

            if (_resultsHeadline != null)
                _resultsHeadline.text = won
                    ? (avgSpeedBonus >= 1.4f ? "¡OVACIÓN DE PIE!" : "¡BRAVO!")
                    : (_elementsPassed >= totalElements / 2 ? "¡BUEN INTENTO!" : "¡ABUCHEADO!");

            if (_resultsEvaluation != null)
                _resultsEvaluation.text = evaluation;

            if (_resultsScore != null)
                _resultsScore.text = $"Score: {_deps?.GameManager?.CurrentScore ?? 0}";

            if (_resultsElementsPassed != null)
                _resultsElementsPassed.text = $"{_elementsPassed} / {totalElements} elementos";

            // Report to GameManager
            if (_deps?.GameManager != null)
            {
                var sessionData = new MinigameSessionData
                {
                    MinigameName = "SceneDirector",
                    Score = _deps.GameManager.CurrentScore,
                    Completed = won,
                    StartTime = DateTime.Now, // approximate — real start time would be stored
                    EndTime = DateTime.Now,
                    DurationSeconds = 0f,
                    CustomStats = new Dictionary<string, object>
                    {
                        ["ElementsPassed"] = _elementsPassed,
                        ["TotalElements"] = totalElements,
                        ["AvgSpeedBonus"] = avgSpeedBonus,
                        ["Won"] = won
                    }
                };
                _deps.GameManager.CollectMinigameData(sessionData);
            }
        }

        private void ShowEvaluationText(bool won, string evaluation)
        {
            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"[Evaluation] {(won ? "WIN" : "LOSE")}: {evaluation}");
        }

        // ── Hardcoded Fallbacks ───────────────────────────────────────────────────

        private void LoadFallbackSequence()
        {
            if (ScriptController.Instance == null) return;

            // Use the difficulty-curve generated sequence, or fall back to inspector hardcoded
            var localSeq = ScriptController.Instance.GenerateLocalSequence();
            if (localSeq != null && localSeq.Count > 0)
            {
                ScriptController.Instance.LoadSequence(localSeq);
            }
            ServiceLogger.Instance.LogInfo(LogServiceName, "Using local/fallback sequence.");
            _sequenceReady = true;
        }

        private string GetHardcodedEvaluation(bool won)
        {
            float avgSpeed = _speedMultipliers.Count > 0 ? _speedMultipliers.Average() : 0f;
            int total = ScriptController.Instance != null ? ScriptController.Instance.TotalElements : 0;

            if (won && avgSpeed >= 1.4f)
                return "¡Una actuación legendaria! El público se pondrá de pie durante generaciones.";
            if (won)
                return "¡Magnífica actuación! El público aplaude con entusiasmo.";
            if (_elementsPassed >= total / 2)
                return "La audiencia reconoce tu esfuerzo, pero esperaban más.";
            if (_elementsPassed > 0)
                return "¡El público enfurecido exige un reembolso!";
            return "Los actores son escoltados fuera del escenario entre una lluvia de tomates.";
        }

        // ── Pause / Resume ────────────────────────────────────────────────────────

        public void Pause()
        {
            if (_phase != GamePhase.Playing || _paused) return;

            _paused = true;

            // Snapshot current element state
            if (ApprovalBarController.Instance != null)
            {
                _pauseStoredFillAmount = ApprovalBarController.Instance.FillAmount;
                ApprovalBarController.Instance.Deactivate();
            }

            if (CountdownController.Instance != null)
            {
                _pauseStoredCountdown = CountdownController.Instance.RemainingSeconds;
                CountdownController.Instance.Pause();
            }

            SetCanvasGroupAlpha(_pausePanel, 1f);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Game paused.");
        }

        public void Resume()
        {
            if (!_paused) return;

            _paused = false;
            SetCanvasGroupAlpha(_pausePanel, 0f);

            // Restore element state
            if (ScriptController.Instance.IsActive)
            {
                CountdownController.Instance.Resume();
                ApprovalBarController.Instance.Activate(
                    ScriptController.Instance.CurrentElement.RequiredEmotion);
            }

            ServiceLogger.Instance.LogInfo(LogServiceName, "Game resumed.");
        }

        public void QuitToMenu()
        {
            _paused = false;
            SetCanvasGroupAlpha(_pausePanel, 0f);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Player quit to main menu.");
            OnEnd();
        }

        public void TriggerPlayAgain()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Play Again — reloading scene.");
            _deps?.GameManager?.EndGame();
            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadSceneDelayed(_sceneIndex, 0.3f);
        }

        // ── Score HUD ─────────────────────────────────────────────────────────────

        private void OnScoreChanged(int newScore)
        {
            if (_scoreText != null)
                _scoreText.text = $"Score: {newScore}";
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        private static void SetCanvasGroupAlpha(CanvasGroup cg, float alpha)
        {
            if (cg == null) return;
            cg.alpha = alpha;
            cg.interactable = alpha >= 1f;
            cg.blocksRaycasts = alpha >= 1f;
        }

        private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
        {
            if (cg == null) yield break;
            float elapsed = 0f;
            cg.alpha = from;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            cg.alpha = to;
            cg.interactable = to >= 1f;
            cg.blocksRaycasts = to >= 1f;
        }

        // ── Editor Debug ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Debug: Start from Curtain Open")]
        private void DebugStartFromCurtainOpen()
        {
            _phase = GamePhase.CurtainOpening;
            OnCurtainOpen();
        }

        [ContextMenu("Debug: Start from Playing (element 1)")]
        private void DebugStartFromPlaying()
        {
            _sequenceReady = true;
            _phase = GamePhase.Playing;
            ScriptController.Instance?.StartSequence();
        }

        [ContextMenu("Debug: Trigger Win")]
        private void DebugTriggerWin()
        {
            ResolveElement(passed: true);
        }

        [ContextMenu("Debug: Trigger Lose")]
        private void DebugTriggerLose()
        {
            ResolveElement(passed: false);
        }

        [ContextMenu("Debug: Show Results (win)")]
        private void DebugShowResultsWin()
        {
            _elementsPassed = 3;
            _speedMultipliers.Clear();
            _speedMultipliers.AddRange(new[] { 1.5f, 1.2f, 1.0f });
            string eval = GetHardcodedEvaluation(true);
            PopulateResultsPanel(true, eval);
            _phase = GamePhase.Results;
            SetCanvasGroupAlpha(_resultsPanel, 1f);
        }

        [ContextMenu("Debug: Show Results (lose)")]
        private void DebugShowResultsLose()
        {
            _elementsPassed = 0;
            _speedMultipliers.Clear();
            string eval = GetHardcodedEvaluation(false);
            PopulateResultsPanel(false, eval);
            _phase = GamePhase.Results;
            SetCanvasGroupAlpha(_resultsPanel, 1f);
        }
#endif
    }
}
