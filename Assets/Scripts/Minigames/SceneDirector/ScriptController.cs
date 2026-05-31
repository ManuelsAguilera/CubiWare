using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Holds and drives the emotion sequence the player must perform.
    /// Advances through elements on success, fires failure events on timeout.
    ///
    /// Testing mode: sequence is loaded from _hardcodedSequence in the Inspector.
    /// TODO: Replace with LLM-generated sequence via GenerateScriptFromLLM().
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to a UI panel GameObject in the Director canvas.
    ///   2. Assign UI references in the Inspector.
    ///   3. Fill _hardcodedSequence with 3–6 elements for testing.
    ///   4. Other systems call PassCurrentElement() or FailCurrentElement() to drive the sequence.
    /// </summary>
    public class ScriptController : MonoBehaviour
    {
        public static ScriptController Instance { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────────
        /// <summary>A new element is now active. Wire CountdownController.StartCountdown here.</summary>
        public event Action<ScriptElement> OnElementStarted;
        /// <summary>Current element was completed successfully.</summary>
        public event Action<ScriptElement> OnElementPassed;
        /// <summary>Current element timed out or was failed.</summary>
        public event Action<ScriptElement> OnElementFailed;
        /// <summary>All elements passed — the player won the round.</summary>
        public event Action OnSequenceComplete;

        // ── Inspector — UI ────────────────────────────────────────────────────────
        [Header("UI References")]
        [Tooltip("Large text showing the required emotion (e.g. 'HAPPY').")]
        [SerializeField] private TextMeshProUGUI _currentEmotionText;
        [Tooltip("Smaller text previewing the next emotion (e.g. 'Next: ANGRY').")]
        [SerializeField] private TextMeshProUGUI _nextEmotionText;
        [Tooltip("Progress label (e.g. '2 / 4').")]
        [SerializeField] private TextMeshProUGUI _progressText;
        [Tooltip("Icon swapped per required emotion. Assign one Image; swap Sprite from _emotionSprites.")]
        [SerializeField] private Image           _emotionIcon;
        [Tooltip("Sprites indexed by EmotionLabel cast to int: [0]=Neutral [1]=Happy [2]=Surprised [3]=Angry.")]
        [SerializeField] private Sprite[]        _emotionSprites;
        [Tooltip("Text that shows the LLM-generated intro. Displayed before countdown starts.")]
        [SerializeField] private TextMeshProUGUI _introText;

        // ── Inspector — Difficulty ────────────────────────────────────────────────
        [Header("Difficulty Curve (fallback when LLM is not used)")]
        [Tooltip("Number of emotions in the sequence. Range 3-6.")]
        [SerializeField] [Range(3, 6)] private int _sequenceLength = 3;
        [Tooltip("Time limit for the first element in the sequence.")]
        [SerializeField] private float _timeLimitStart = 6f;
        [Tooltip("Time limit for the last element (linearly interpolated across sequence).")]
        [SerializeField] private float _timeLimitEnd = 3f;

        // ── Inspector — Testing ───────────────────────────────────────────────────
        [Header("Hardcoded Test Sequence")]
        [Tooltip("Sequence used while LLM generation is not yet active.")]
        [SerializeField] private ScriptElement[] _hardcodedSequence = new ScriptElement[]
        {
            new ScriptElement { RequiredEmotion = EmotionLabel.Happy,     TimeLimit = 5f },
            new ScriptElement { RequiredEmotion = EmotionLabel.Surprised, TimeLimit = 5f },
            new ScriptElement { RequiredEmotion = EmotionLabel.Angry,     TimeLimit = 5f },
        };

        // ── State ─────────────────────────────────────────────────────────────────
        private List<ScriptElement> _sequence = new List<ScriptElement>();
        private int _currentIndex = -1;

        public ScriptElement CurrentElement => _currentIndex >= 0 && _currentIndex < _sequence.Count
            ? _sequence[_currentIndex] : default;
        public int  CurrentIndex   => _currentIndex;
        public int  TotalElements  => _sequence.Count;
        public int  SequenceLength => _sequenceLength;
        public bool IsActive       => _currentIndex >= 0 && _currentIndex < _sequence.Count;

        private const string LogServiceName = "ScriptController";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads a sequence and starts from the first element.
        /// In testing mode this is called automatically with the hardcoded sequence.
        /// TODO: Replace with LLM-generated list when LLM is ready.
        /// </summary>
        public void StartSequence()
        {
            // TODO: Replace with LLM-generated sequence.
            // Example: yield return GenerateScriptFromLLM(onComplete: LoadAndStart);
            LoadSequence(new List<ScriptElement>(_hardcodedSequence));
        }

        /// <summary>Loads a custom sequence (for LLM integration later).</summary>
        public void LoadSequence(List<ScriptElement> elements)
        {
            _sequence     = elements;
            _currentIndex = -1;
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Sequence loaded: {_sequence.Count} elements.");
            AdvanceToNext();
        }

        /// <summary>
        /// Displays the LLM-generated intro text in the UI.
        /// Called from SceneDirectorGame after sequence generation completes.
        /// </summary>
        public void ShowIntro(string intro)
        {
            if (_introText != null)
            {
                _introText.text = intro;
                _introText.gameObject.SetActive(true);
                ServiceLogger.Instance.LogInfo(LogServiceName, "Intro text displayed.");
            }
        }

        /// <summary>
        /// Hides the intro text — called when gameplay starts.
        /// </summary>
        public void HideIntro()
        {
            if (_introText != null)
                _introText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Generates a hardcoded sequence using the difficulty curve fields.
        /// Time limit is linearly interpolated from _timeLimitStart to _timeLimitEnd
        /// across the sequence.
        /// </summary>
        public List<ScriptElement> GenerateLocalSequence()
        {
            var list = new List<ScriptElement>(_sequenceLength);
            // Cycle through non-Neutral emotions: Happy(1), Surprised(2), Angry(3)
            for (int i = 0; i < _sequenceLength; i++)
            {
                float t = _sequenceLength > 1 ? (float)i / (_sequenceLength - 1) : 0f;
                float timeLimit = Mathf.Lerp(_timeLimitStart, _timeLimitEnd, t);
                list.Add(new ScriptElement
                {
                    RequiredEmotion = (EmotionLabel)(1 + (i % 3)), // 1=Happy, 2=Surprised, 3=Angry
                    TimeLimit = Mathf.Clamp(timeLimit, 3f, 8f)
                });
            }
            return list;
        }

        /// <summary>
        /// Call this when the player successfully holds the correct emotion (approval bar filled).
        /// Advances to the next element or fires OnSequenceComplete.
        /// </summary>
        public void PassCurrentElement()
        {
            if (!IsActive) return;
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Element {_currentIndex + 1} PASSED: {CurrentElement.RequiredEmotion}");
            OnElementPassed?.Invoke(CurrentElement);
            AdvanceToNext();
        }

        /// <summary>
        /// Call this when the countdown expires or the element otherwise fails.
        /// Does NOT advance — same element is retried or round ends depending on game rules.
        /// </summary>
        public void FailCurrentElement()
        {
            if (!IsActive) return;
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Element {_currentIndex + 1} FAILED: {CurrentElement.RequiredEmotion}");
            OnElementFailed?.Invoke(CurrentElement);
            // Caller (SceneDirectorGame) decides whether to retry or end the round.
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void AdvanceToNext()
        {
            _currentIndex++;

            if (_currentIndex >= _sequence.Count)
            {
                ServiceLogger.Instance.LogInfo(LogServiceName, "Sequence complete!");
                _currentIndex = _sequence.Count; // clamp past end
                RefreshUI();
                OnSequenceComplete?.Invoke();
                return;
            }

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"Element {_currentIndex + 1}/{_sequence.Count}: {CurrentElement.RequiredEmotion} ({CurrentElement.TimeLimit}s)");
            RefreshUI();
            OnElementStarted?.Invoke(CurrentElement);
        }

        private void RefreshUI()
        {
            if (_currentEmotionText != null)
                _currentEmotionText.text = IsActive ? CurrentElement.RequiredEmotion.ToString().ToUpper() : "—";

            if (_nextEmotionText != null)
            {
                int nextIndex = _currentIndex + 1;
                _nextEmotionText.text = nextIndex < _sequence.Count
                    ? $"Next: {_sequence[nextIndex].RequiredEmotion}"
                    : "Next: —";
            }

            if (_progressText != null)
                _progressText.text = $"{Mathf.Min(_currentIndex + 1, _sequence.Count)} / {_sequence.Count}";

            if (_emotionIcon != null && _emotionSprites != null && IsActive)
            {
                int spriteIndex = (int)CurrentElement.RequiredEmotion;
                if (spriteIndex < _emotionSprites.Length)
                    _emotionIcon.sprite = _emotionSprites[spriteIndex];
            }
        }
    }

    // ── Data ──────────────────────────────────────────────────────────────────────

    [Serializable]
    public struct ScriptElement
    {
        [Tooltip("The emotion the player must perform for this step.")]
        public EmotionLabel RequiredEmotion;
        [Tooltip("Seconds the player has to fill the approval bar.")]
        public float TimeLimit;
    }
}
