using System.Collections;
using UnityEngine;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Controls the virtual audience: a horizontal row of profile-icon members,
    /// each bouncing independently. Three states driven by game events:
    ///   Idle       — members rest at their base position.
    ///   SlightMove — gentle continuous bobbing (correct emotion being held).
    ///   React      — staggered big bounces: up = applause, down dips = disapproval.
    ///
    /// Members are discovered automatically: every direct child with a RectTransform
    /// counts as one audience member. Movement is coroutine-driven (no Animator),
    /// but the public API is identical to the old Animator-based version so
    /// DirectorGame needs no changes.
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to a UI panel under the Canvas (bottom strip).
    ///   2. Add N child Images (profile icons) — they become the members.
    ///   3. Optionally assign _dialogueText; otherwise "DialogueBubble" is found by name.
    /// </summary>
    public class AudienceController : MonoBehaviour
    {
        public static AudienceController Instance { get; private set; }

        public enum AudienceState { Idle, SlightMove, React }
        public AudienceState CurrentState { get; private set; } = AudienceState.Idle;

        // ── Inspector — Motion ────────────────────────────────────────────────────
        [Header("Reaction Motion")]
        [Tooltip("Peak height (px) of one applause bounce.")]
        [SerializeField] private float _bounceHeight = 34f;
        [Tooltip("Depth (px) of one disapproval dip below the base position.")]
        [SerializeField] private float _dipDepth = 22f;
        [Tooltip("Seconds for a single up-down bounce.")]
        [SerializeField] private float _bounceDuration = 0.28f;
        [Tooltip("Bounces each member performs during a React.")]
        [SerializeField] private int _bouncesPerReact = 3;
        [Tooltip("Delay (s) between one member starting and the next — the 'wave' feel.")]
        [SerializeField] private float _staggerDelay = 0.09f;

        [Header("SlightMove Motion")]
        [Tooltip("Amplitude (px) of the subtle bobbing while the player holds the correct emotion.")]
        [SerializeField] private float _slightMoveHeight = 7f;
        [Tooltip("Bobbing speed multiplier for SlightMove.")]
        [SerializeField] private float _slightMoveSpeed = 3.5f;

        // ── Inspector — Dialogue ──────────────────────────────────────────────────
        [Header("Dialogue")]
        [Tooltip("TextMeshProUGUI positioned above the audience row. Shown via ShowDialogue().")]
        [SerializeField] private TMPro.TextMeshProUGUI _dialogueText;

        // ── State ─────────────────────────────────────────────────────────────────
        private RectTransform[] _members;
        private Vector2[]       _basePositions;
        private Coroutine       _stateRoutine;
        private Coroutine[]     _memberRoutines;

        private const string LogServiceName = "AudienceController";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Every direct child is one audience member.
            int count = transform.childCount;
            _members       = new RectTransform[count];
            _basePositions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                _members[i]       = transform.GetChild(i) as RectTransform;
                _basePositions[i] = _members[i] != null ? _members[i].anchoredPosition : Vector2.zero;
            }
            _memberRoutines = new Coroutine[count];

            if (_dialogueText == null)
                _dialogueText = GameObject.Find("DialogueBubble")?.GetComponent<TMPro.TextMeshProUGUI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Returns the audience to their resting state.</summary>
        public void SetIdle()
        {
            StopStateRoutine();
            RestoreBasePositions();
            CurrentState = AudienceState.Idle;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → Idle");
        }

        /// <summary>
        /// Audience reacts subtly — call while the player is close to the correct emotion
        /// but hasn't filled the approval bar yet. Loops until SetIdle or React is called.
        /// </summary>
        public void SlightMove()
        {
            if (CurrentState == AudienceState.React) return;      // don't interrupt a big reaction
            if (CurrentState == AudienceState.SlightMove) return; // already bobbing
            StopStateRoutine();
            CurrentState  = AudienceState.SlightMove;
            _stateRoutine = StartCoroutine(CoSlightMoveLoop());
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → SlightMove");
        }

        /// <summary>Big positive reaction — staggered applause bounces. Returns to Idle when done.</summary>
        public void ReactPositive()
        {
            StopStateRoutine();
            CurrentState  = AudienceState.React;
            _stateRoutine = StartCoroutine(CoReact(positive: true));
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → React (applause)");
        }

        /// <summary>Big negative reaction — staggered disapproval dips. Returns to Idle when done.</summary>
        public void ReactNegative()
        {
            StopStateRoutine();
            CurrentState  = AudienceState.React;
            _stateRoutine = StartCoroutine(CoReact(positive: false));
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → React (disapproval)");
        }

        // ── Dialogue ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows a dialogue bubble above the audience for the specified duration.
        /// Use for witty commentary, heckles, or LLM-generated audience lines.
        /// </summary>
        public void ShowDialogue(string text, float duration = 2.5f)
        {
            if (_dialogueText != null)
            {
                _dialogueText.text = text;
                _dialogueText.gameObject.SetActive(true);
                StartCoroutine(HideDialogueAfter(duration));
            }
        }

        private IEnumerator HideDialogueAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_dialogueText != null)
                _dialogueText.gameObject.SetActive(false);
        }

        // ── Legacy hook ───────────────────────────────────────────────────────────
        // Kept for compatibility with the previous Animator-based setup.

        public void OnReactComplete_AnimEvent()
        {
            SetIdle();
        }

        // ── Motion coroutines ─────────────────────────────────────────────────────

        private IEnumerator CoReact(bool positive)
        {
            // Random launch order so the "wave" looks organic each time.
            int[] order = new int[_members.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int running = 0;
            foreach (int idx in order)
            {
                if (_members[idx] == null) continue;
                running++;
                _memberRoutines[idx] = StartCoroutine(CoMemberBounce(idx, positive, () => running--));
                yield return new WaitForSeconds(_staggerDelay);
            }

            while (running > 0) yield return null;

            _stateRoutine = null;
            SetIdle();
        }

        private IEnumerator CoMemberBounce(int idx, bool positive, System.Action onDone)
        {
            RectTransform rt   = _members[idx];
            Vector2       home = _basePositions[idx];
            float amplitude    = positive ? _bounceHeight : -_dipDepth;

            for (int b = 0; b < _bouncesPerReact; b++)
            {
                float elapsed = 0f;
                while (elapsed < _bounceDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _bounceDuration);
                    // Half-sine: 0 → peak → 0 in one bounce.
                    float offset = Mathf.Sin(t * Mathf.PI) * amplitude;
                    rt.anchoredPosition = home + new Vector2(0f, offset);
                    yield return null;
                }
            }

            rt.anchoredPosition = home;
            onDone?.Invoke();
        }

        private IEnumerator CoSlightMoveLoop()
        {
            // Each member bobs with its own phase so the row doesn't move in lockstep.
            float[] phase = new float[_members.Length];
            for (int i = 0; i < phase.Length; i++) phase[i] = Random.Range(0f, Mathf.PI * 2f);

            while (true)
            {
                float time = Time.time * _slightMoveSpeed;
                for (int i = 0; i < _members.Length; i++)
                {
                    if (_members[i] == null) continue;
                    float offset = (Mathf.Sin(time + phase[i]) * 0.5f + 0.5f) * _slightMoveHeight;
                    _members[i].anchoredPosition = _basePositions[i] + new Vector2(0f, offset);
                }
                yield return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void StopStateRoutine()
        {
            if (_stateRoutine != null)
            {
                StopCoroutine(_stateRoutine);
                _stateRoutine = null;
            }
            if (_memberRoutines != null)
            {
                for (int i = 0; i < _memberRoutines.Length; i++)
                {
                    if (_memberRoutines[i] != null)
                    {
                        StopCoroutine(_memberRoutines[i]);
                        _memberRoutines[i] = null;
                    }
                }
            }
        }

        private void RestoreBasePositions()
        {
            if (_members == null) return;
            for (int i = 0; i < _members.Length; i++)
                if (_members[i] != null)
                    _members[i].anchoredPosition = _basePositions[i];
        }
    }
}
