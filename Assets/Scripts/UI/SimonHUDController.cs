using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ARcadeRush.UI
{
    /// <summary>
    /// Pure HUD content controller for the Simón Dice minigame.
    /// Does NOT handle panel visibility — that's SimonMenuManager's job.
    /// Assumes its parent GameObject (GameplayHUD) is already active when called.
    ///
    /// Displays:
    ///   - Dialogue text (in a static UI panel, not anchored to head)
    ///   - Round counter
    ///   - Timer bar + text
    ///   - "Simón dice" prefix highlighting
    /// </summary>
    public class SimonHUDController : MonoBehaviour
    {
        // ── Serialized Fields ──────────────────────────────────────────

        [Header("Dialogue")]
        [SerializeField] private TMP_Text _dialogueText;
        [SerializeField] private GameObject _dialoguePanel;
        [SerializeField] private Color _simonDiceColor = Color.green;
        [SerializeField] private Color _noSimonDiceColor = new Color(1f, 0.6f, 0f); // orange

        [Header("HUD")]
        [SerializeField] private TMP_Text _roundCounterText;
        [SerializeField] private Image _timerFillBar;
        [SerializeField] private TMP_Text _timerText;

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Shows dialogue text with color-coded prefix for "Simón dice" vs non-Simón commands.
        /// </summary>
        public void ShowDialogue(string text, bool saysSimonDice)
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(true);

            if (_dialogueText != null)
            {
                _dialogueText.text = text;
                _dialogueText.color = saysSimonDice ? _simonDiceColor : _noSimonDiceColor;
            }
        }

        /// <summary>
        /// Hides the dialogue panel (called between rounds or at game end).
        /// </summary>
        public void HideDialogue()
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(false);

            if (_dialogueText != null)
                _dialogueText.text = string.Empty;
        }

        /// <summary>
        /// Updates the round counter display (e.g., "Ronda 3 / 5").
        /// </summary>
        public void UpdateRoundCounter(int round, int maxRounds)
        {
            if (_roundCounterText != null)
                _roundCounterText.text = $"Ronda {round} / {maxRounds}";
        }

        /// <summary>
        /// Updates the timer bar fill and text display.
        /// </summary>
        public void UpdateTimer(float remaining, float total)
        {
            if (_timerFillBar != null)
                _timerFillBar.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            if (_timerText != null)
                _timerText.text = $"{remaining:F1}s";
        }

        /// <summary>
        /// Resets all HUD displays for a new game.
        /// </summary>
        public void ResetAll()
        {
            HideDialogue();
            HideEmotionTarget();

            if (_roundCounterText != null)
                _roundCounterText.text = "Ronda 0 / 0";

            if (_timerFillBar != null)
                _timerFillBar.fillAmount = 1f;

            if (_timerText != null)
                _timerText.text = "0.0s";
        }

        // ── Emotion HUD (v5 — active for alternating emotion rounds) ──

        [Header("Emotion HUD (v5)")]
        [SerializeField] private GameObject _emotionTargetPanel;
        [SerializeField] private TMP_Text _emotionTargetText;
        [SerializeField] private TMP_Text _emotionMismatchText;

        /// <summary>
        /// Shows the expected emotion to the player during emotion-only rounds.
        /// v5: Re-activated for alternating emotion rounds.
        /// </summary>
        public void ShowEmotionTarget(string emotionDisplayName)
        {
            if (_emotionTargetPanel != null)
                _emotionTargetPanel.SetActive(true);

            if (_emotionTargetText != null)
                _emotionTargetText.text = $"Muestra: {emotionDisplayName}";
        }

        /// <summary>
        /// Hides the emotion target display (called at end of round, timeout, etc.).
        /// </summary>
        public void HideEmotionTarget()
        {
            if (_emotionTargetPanel != null)
                _emotionTargetPanel.SetActive(false);

            if (_emotionTargetText != null)
                _emotionTargetText.text = string.Empty;

            if (_emotionMismatchText != null)
                _emotionMismatchText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows a brief emotion mismatch message to the player.
        /// </summary>
        public void ShowEmotionMismatch(string expected, string current)
        {
            if (_emotionMismatchText != null)
            {
                _emotionMismatchText.text = $"Se esperaba '{expected}', se detectó '{current}'";
                _emotionMismatchText.gameObject.SetActive(true);
            }
        }

        // ── Tricked Feedback ──────────────────────────────────────────────

        /// <summary>
        /// Shows a "tricked" message when the player performed an action
        /// but the command did NOT contain "simon dice".
        /// </summary>
        public void ShowTrickedMessage()
        {
            if (_dialogueText != null)
            {
                _dialogueText.text = "¡Te engañó Simón! No dijo 'Simón dice'...";
                _dialogueText.color = new Color(1f, 0.3f, 0.3f); // red-ish
            }

            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(true);
        }
    }
}
