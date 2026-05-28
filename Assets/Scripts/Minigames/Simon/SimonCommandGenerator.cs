using System;
using System.Collections.Generic;
using UnityEngine;
using ARcadeRush.Core;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Generates Simon Says commands with LLM-powered natural dialogue.
    /// Game-logic parameters are pre-determined; LLM only decorates with natural language.
    /// Falls back to template phrases if LLM is unavailable.
    ///
    /// v3: Pre-plans the entire game's "Simón dice" distribution at game start
    /// to guarantee at least 1-2 false commands per 5-round game.
    /// </summary>
    public class SimonCommandGenerator : MonoBehaviour
    {
        [Header("Distribution")]
        [Tooltip("Minimum number of 'false' (no Simón dice) rounds per game.")]
        [SerializeField] private int _minFalseRounds = 1;
        [Tooltip("Maximum number of 'false' rounds per game.")]
        [SerializeField] private int _maxFalseRounds = 2;

        [Header("LLM")]
        [SerializeField] private bool _useLLM = true;
        [SerializeField] private string _systemPrompt =
            "Eres Simón, un personaje excéntrico que dirige un juego de \"Simón dice\" en español. " +
            "Tu personalidad es divertida, a veces sarcástica, a veces motivadora.\n\n" +
            "REGLAS:\n" +
            "- Cuando debas decir \"Simón dice\", SIEMPRE empieza la frase con \"Simón dice: \".\n" +
            "- Cuando NO debas decirlo, NUNCA uses la frase \"Simón dice\" ni la palabra \"Simón\".\n" +
            "- Varía tu tono entre rondas: amable, exigente, gracioso, misterioso.\n" +
            "- Mantén cada orden en máximo 15 palabras.\n" +
            "- Usas español latino neutro.";

        // ── Fallback Templates ──────────────────────────────────────────

        // v3 Fix (F11): "¡lo ordena Simón!" moved to SimonDice-only list
        private static readonly string[] FallbackTemplates_SimonDice = {
            "Simón dice: {0}",
            "Simón dice que hagas {0}",
            "{0}, ¡lo ordena Simón!"
        };

        private static readonly string[] FallbackTemplates_NoSimonDice = {
            "¡{0}!",
            "{0} ahora mismo",
            "¡Rápido, {0}!"
        };

        private static readonly Dictionary<SimonGestureTarget, string> GestureNames = new()
        {
            { SimonGestureTarget.OpenHand,   "mano abierta" },
            { SimonGestureTarget.ClosedFist, "puño cerrado" },
            { SimonGestureTarget.Point,      "señala con el dedo" },
            { SimonGestureTarget.Pinch,      "pellizco" },
            { SimonGestureTarget.ThumbDown,  "pulgar abajo" },
        };

        // ── Pre-planned distribution ──────────────────────────────────────

        private bool[] _roundPlan; // true = saysSimonDice for each round
        private bool _planGenerated;

        /// <summary>
        /// Pre-plans the "Simón dice" distribution for the entire game.
        /// Guarantees [_minFalseRounds, _maxFalseRounds] false commands.
        /// Call once at game start (before first round).
        /// </summary>
        public void PlanGame(int totalRounds)
        {
            if (_planGenerated) return;
            _planGenerated = true;

            _roundPlan = new bool[totalRounds];
            int falseCount = UnityEngine.Random.Range(_minFalseRounds, _maxFalseRounds + 1);
            falseCount = Mathf.Clamp(falseCount, 0, totalRounds);

            // Fill all as true, then randomly assign false positions
            for (int i = 0; i < totalRounds; i++) _roundPlan[i] = true;

            // Fisher-Yates to pick random false positions
            var indices = new List<int>(totalRounds);
            for (int i = 0; i < totalRounds; i++) indices.Add(i);
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            for (int i = 0; i < falseCount; i++) _roundPlan[indices[i]] = false;

            Debug.Log($"[SimonCommandGenerator] Plan: {totalRounds} rounds, {falseCount} false commands. " +
                      $"Distribution: [{string.Join(", ", _roundPlan)}]");
        }

        /// <summary>
        /// Resets the pre-planned distribution. Call when restarting game.
        /// </summary>
        public void ResetPlan()
        {
            _planGenerated = false;
            _roundPlan = null;
        }

        /// <summary>
        /// Generates a command asynchronously. Uses pre-planned distribution.
        /// If LLM fails, automatically falls back to template phrases.
        /// </summary>
        /// <param name="round">Zero-based round index.</param>
        /// <param name="maxRounds">Total rounds in the game.</param>
        /// <param name="llm">LLMConnector instance from MiniGameDependencies.</param>
        /// <param name="onComplete">Callback with the generated SimonCommand.</param>
        public void GenerateCommand(int round, int maxRounds, LLMConnector llm, Action<SimonCommand> onComplete)
        {
            bool saysSimonDice = GetSaysSimonDice(round);

            var gestureTarget = (SimonGestureTarget)UnityEngine.Random.Range(
                0, Enum.GetValues(typeof(SimonGestureTarget)).Length);

            var cmd = new SimonCommand
            {
                SaysSimonDice = saysSimonDice,
                ActionType = SimonActionType.Gesture,
                GestureTarget = gestureTarget,
            };

            if (!_useLLM || llm == null)
            {
                cmd.DialogueText = GenerateFallbackText(cmd);
                onComplete?.Invoke(cmd);
                return;
            }

            string gestureName = GestureNames[gestureTarget];
            string condition = saysSimonDice
                ? "DEBES decir \"Simón dice\"."
                : "NO debes decir \"Simón dice\" ni usar la palabra \"Simón\".";

            string userPrompt = $"Ronda {round + 1} de {maxRounds}.\n{condition}\n" +
                                $"El jugador debe hacer el gesto \"{gestureName}\".\n" +
                                "Genera UNA sola orden en español.";

            // v3 Fix (F14): use onError callback for automatic fallback
            llm.Ask(_systemPrompt, userPrompt,
                onComplete: (response) =>
                {
                    cmd.DialogueText = response.Trim();
                    onComplete?.Invoke(cmd);
                },
                onError: (error) =>
                {
                    Debug.LogWarning($"[SimonCommandGenerator] LLM failed ({error}), using fallback.");
                    cmd.DialogueText = GenerateFallbackText(cmd);
                    onComplete?.Invoke(cmd);
                }
            );
        }

        /// <summary>
        /// Returns whether this round should say "Simón dice" based on the pre-planned distribution.
        /// Falls back to random if no plan was generated.
        /// </summary>
        private bool GetSaysSimonDice(int round)
        {
            if (_roundPlan != null && round < _roundPlan.Length)
                return _roundPlan[round];

            // Safety fallback if PlanGame was not called
            return UnityEngine.Random.value < 0.6f;
        }

        /// <summary>
        /// Generates dialogue text using template phrases (no LLM).
        /// </summary>
        private string GenerateFallbackText(SimonCommand cmd)
        {
            string gestureName = GestureNames.TryGetValue(cmd.GestureTarget, out string name)
                ? name : cmd.GestureTarget.ToString();

            string[] templates = cmd.SaysSimonDice
                ? FallbackTemplates_SimonDice
                : FallbackTemplates_NoSimonDice;

            string template = templates[UnityEngine.Random.Range(0, templates.Length)];
            return string.Format(template, gestureName);
        }
    }
}
