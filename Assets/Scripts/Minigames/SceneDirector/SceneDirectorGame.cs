using System.Collections;
using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Main controller for the Scene Director minigame. Implements IMiniGame and
    /// wires together all five element singletons:
    ///   ScenarioController  → curtain open/close
    ///   AudienceController  → crowd reactions
    ///   CountdownController → per-emotion timer
    ///   ScriptController    → emotion sequence
    ///   CameraController    → webcam feed + AR mask
    ///   ApprovalBarController → fill/drain bar
    ///
    /// GAME FLOW
    /// ─────────
    ///   OnStart → Open curtain → Start sequence → [per element loop] → Close curtain → OnEnd
    ///
    /// PER-ELEMENT LOOP
    /// ────────────────
    ///   OnElementStarted → Start countdown + Activate bar → player acts →
    ///   ResolveElement(passed/failed) → audience reacts → [next element or end round]
    ///
    /// TESTING MODE
    /// ────────────
    ///   Emotion detection: H/S/A/N keys (handled in CameraController, flows here via OnEmotionDetected).
    ///   Script generation: hardcoded sequence from ScriptController._hardcodedSequence.
    ///   LLM evaluation:    hardcoded string (see EndRound coroutine).
    ///
    /// TODO: Add Director.unity to EditorBuildSettings.asset and set SceneIndex accordingly.
    /// </summary>
    public class SceneDirectorGame : MonoBehaviour, IMiniGame
    {
        // ── IMiniGame ─────────────────────────────────────────────────────────────
        /// <summary>TODO: Update to match Director.unity build index in EditorBuildSettings.</summary>
        public int SceneIndex => 5;

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Scene Settings")]
        [SerializeField] private int   _mainMenuSceneIndex  = 1;
        [SerializeField] private float _betweenElementDelay = 1.5f; // seconds between audience react and next element
        [SerializeField] private float _endRoundDelay       = 2.0f; // seconds after round ends before curtain closes
        [SerializeField] private int   _pointsPerElement    = 100;

        // ── State ─────────────────────────────────────────────────────────────────
        private MiniGameDependencies _deps;
        private bool _playing;
        private bool _elementResolved; // guard against double-fire from bar + countdown

        private const string LogServiceName = "SceneDirectorGame";

        // ── IMiniGame ─────────────────────────────────────────────────────────────

        public void OnStart(MiniGameDependencies deps)
        {
            _deps    = deps;
            _playing = false;

            // Bind webcam feed to the Camera element display
            if (_deps.Camera != null)
                CameraController.Instance?.BindFeed(_deps.Camera);

            WireEvents();

            ServiceLogger.Instance.LogInfo(LogServiceName, "Starting — opening curtain.");
            ScenarioController.Instance.Open();
        }

        public void OnEnd()
        {
            _playing = false;
            UnwireEvents();

            _deps?.GameManager?.EndGame();

            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadSceneDelayed(_mainMenuSceneIndex, 0.5f);
        }

        // ── Update — audience SlightMove driven by live approval bar state ────────

        private void Update()
        {
            if (!_playing) return;
            if (ApprovalBarController.Instance == null || !ApprovalBarController.Instance.IsActive) return;

            // Audience subtly reacts while the player is holding the correct emotion.
            if (ApprovalBarController.Instance.IsCorrect)
                AudienceController.Instance?.SlightMove();
            else if (AudienceController.Instance?.CurrentState == AudienceController.AudienceState.SlightMove)
                AudienceController.Instance?.SetIdle();
        }

        // ── Event wiring ──────────────────────────────────────────────────────────

        private void WireEvents()
        {
            ScenarioController.Instance.OnOpenComplete  += OnCurtainOpen;
            ScenarioController.Instance.OnCloseComplete += OnCurtainClose;

            ScriptController.Instance.OnElementStarted   += OnElementStarted;
            ScriptController.Instance.OnSequenceComplete += OnSequenceComplete;

            ApprovalBarController.Instance.OnBarFilled  += OnBarFilled;
            ApprovalBarController.Instance.OnBarEmptied += OnBarEmptied;

            CountdownController.Instance.OnCountdownExpired += OnCountdownExpired;

            // TODO: Implement Director de Escena game loop using _deps.EmotionBridge (DeepFace).
            // Available API:
            //   _deps.EmotionBridge.SetMode(EmotionDetectionMode.AutoInterval)
            //   _deps.EmotionBridge.OnEmotionChanged += (emotion, confidence) => { ... }
            //   _deps.EmotionBridge.IsEmotionActive(EmotionType.Happy, 0.55f)
            //   _deps.EmotionBridge.SetMode(EmotionDetectionMode.Window) + OpenDetectionWindow(duration)
            //   _deps.EmotionBridge.OnWindowClosed += dominant => { ... }
            // EmotionType: Angry, Disgust, Fear, Happy, Sad, Surprise, Neutral, Unknown
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
        }

        private void OnDestroy() => UnwireEvents();

        // ── Game flow handlers ────────────────────────────────────────────────────

        private void OnCurtainOpen()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain open — starting sequence.");
            _playing = true;
            AudienceController.Instance?.SetIdle();
            ScriptController.Instance.StartSequence();
        }

        private void OnElementStarted(ScriptElement element)
        {
            StartCoroutine(ActivateElementWithDelay(element));
        }

        private IEnumerator ActivateElementWithDelay(ScriptElement element)
        {
            // Brief pause so the audience reaction from the previous element can play.
            yield return new WaitForSeconds(_betweenElementDelay);

            _elementResolved = false;
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
            // Deactivate bar first so it doesn't also fire OnBarEmptied.
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
                ServiceLogger.Instance.LogInfo(LogServiceName, "Element PASSED.");
                _deps?.GameManager?.AddScore(_pointsPerElement);
                AudienceController.Instance?.ReactPositive();
                ScriptController.Instance.PassCurrentElement();
                // If more elements remain, OnElementStarted fires and ActivateElementWithDelay handles the rest.
                // If sequence is done, OnSequenceComplete fires.
            }
            else
            {
                ServiceLogger.Instance.LogInfo(LogServiceName, "Element FAILED.");
                ScriptController.Instance.FailCurrentElement();
                AudienceController.Instance?.ReactNegative();
                StartCoroutine(EndRound(won: false));
            }
        }

        // ── Round end ─────────────────────────────────────────────────────────────

        private void OnSequenceComplete()
        {
            StartCoroutine(EndRound(won: true));
        }

        private IEnumerator EndRound(bool won)
        {
            _playing = false;
            ServiceLogger.Instance.LogInfo(LogServiceName, won ? "Round WON." : "Round LOST.");

            // TODO: Replace hardcoded strings with LLM evaluation call:
            // string prompt = won
            //     ? "The player completed all emotions correctly. Give brief theatrical applause as a dramatic narrator."
            //     : "The player failed to perform the required emotions. Give a brief theatrical boo as a dramatic narrator.";
            // _deps.LLM.Ask(systemPrompt, prompt, result => ShowEvaluationText(result), _ => { });

            // Hardcoded evaluation for testing
            string evaluation = won
                ? "Bravo! The crowd goes wild! A magnificent performance!"
                : "The audience erupts in boos! Tomatoes rain upon the stage!";
            ServiceLogger.Instance.LogInfo(LogServiceName, $"[TEST] Evaluation: {evaluation}");

            yield return new WaitForSeconds(_endRoundDelay);

            ServiceLogger.Instance.LogInfo(LogServiceName, "Closing curtain.");
            ScenarioController.Instance.Close();
        }

        private void OnCurtainClose()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain closed — ending game.");
            OnEnd();
        }
    }
}
