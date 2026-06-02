using UnityEngine;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Controls the 2D virtual audience. Three states driven by game events:
    ///   Idle       — audience waiting, no performance active.
    ///   SlightMove — audience reacting subtly (correct emotion detected, not yet confirmed).
    ///   React      — big reaction: applause (positive) or tomatoes (negative).
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach this script to the audience 2D sprite/canvas GameObject.
    ///   2. Assign the Animator in the Inspector.
    ///   3. Create the following Animator parameters:
    ///        - "SlightMove"  (Bool)    — true while a near-correct emotion is being held
    ///        - "React"       (Trigger) — fires the big reaction clip
    ///        - "IsPositive"  (Bool)    — true = applause clip, false = tomato clip
    ///   4. Transitions:
    ///        - Idle    → SlightMove  : when SlightMove == true
    ///        - SlightMove → Idle     : when SlightMove == false
    ///        - Any State → React     : on React trigger (interrupts everything)
    ///        - React   → Idle        : Has Exit Time = true (plays once, auto-returns)
    ///   5. Use IsPositive in the React state to blend or swap between applause/tomato sprites.
    /// </summary>
    public class AudienceController : MonoBehaviour
    {
        public static AudienceController Instance { get; private set; }

        public enum AudienceState { Idle, SlightMove, React }
        public AudienceState CurrentState { get; private set; } = AudienceState.Idle;

        [Header("References")]
        [SerializeField] private Animator _animator;

        [Header("Dialogue")]
        [Tooltip("TextMeshProUGUI positioned above the audience sprite. Shown via ShowDialogue().")]
        [SerializeField] private TMPro.TextMeshProUGUI _dialogueText;

        private static readonly int ParamSlightMove = Animator.StringToHash("SlightMove");
        private static readonly int TriggerReact    = Animator.StringToHash("React");
        private static readonly int ParamIsPositive = Animator.StringToHash("IsPositive");

        private const string LogServiceName = "AudienceController";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Code-based discovery if Inspector wiring is missing
            if (_animator == null)     _animator     = GetComponent<Animator>();
            if (_dialogueText == null) _dialogueText = GameObject.Find("DialogueBubble")?.GetComponent<TMPro.TextMeshProUGUI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Returns the audience to their resting state.</summary>
        public void SetIdle()
        {
            CurrentState = AudienceState.Idle;
            _animator.SetBool(ParamSlightMove, false);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → Idle");
        }

        /// <summary>
        /// Audience reacts subtly — call while the player is close to the correct emotion
        /// but hasn't filled the approval bar yet. Loops until SetIdle or React is called.
        /// </summary>
        public void SlightMove()
        {
            if (CurrentState == AudienceState.React) return; // don't interrupt a big reaction
            // Guard against log spam — only log and set animator if not already in SlightMove
            if (_animator != null && _animator.GetBool(ParamSlightMove)) return;
            CurrentState = AudienceState.SlightMove;
            _animator.SetBool(ParamSlightMove, true);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → SlightMove");
        }

        /// <summary>Big positive reaction — audience applauds. Plays once then returns to Idle.</summary>
        public void ReactPositive()
        {
            CurrentState = AudienceState.React;
            _animator.SetBool(ParamSlightMove, false);
            _animator.SetBool(ParamIsPositive, true);
            _animator.SetTrigger(TriggerReact);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → React (applause)");
        }

        /// <summary>Big negative reaction — audience throws tomatoes. Plays once then returns to Idle.</summary>
        public void ReactNegative()
        {
            CurrentState = AudienceState.React;
            _animator.SetBool(ParamSlightMove, false);
            _animator.SetBool(ParamIsPositive, false);
            _animator.SetTrigger(TriggerReact);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience → React (tomatoes)");
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

        private System.Collections.IEnumerator HideDialogueAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_dialogueText != null)
                _dialogueText.gameObject.SetActive(false);
        }

        // ── Animation Event ───────────────────────────────────────────────────────
        // Add this to the LAST FRAME of the React animation clip in the Animation window.

        public void OnReactComplete_AnimEvent()
        {
            SetIdle();
        }
    }
}
