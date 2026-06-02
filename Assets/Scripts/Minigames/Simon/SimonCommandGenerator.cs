using System;
using System.Collections.Generic;
using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Generates Simon Says commands with LLM-powered natural dialogue.
    /// Game-logic parameters are pre-determined; LLM only decorates with natural language.
    /// Falls back to template phrases if LLM is unavailable.
    ///
    /// v3: Pre-plans the entire game's "Simón dice" distribution at game start
    /// to guarantee at least 1-2 false commands per 5-round game.
    ///
    /// v5: Re-integrated emotions as ALTERNATING independent rounds.
    /// Each round is exclusively EITHER gesture+position OR emotion-only.
    /// Never both in the same command.
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
            "Eres Simón, un presentador de show de juegos excéntrico y carismático que dirige \"Simón dice\" en español. " +
            "Tu personalidad es juguetona, impredecible: a veces amable, a veces sarcástico, a veces motivador, a veces misterioso.\n\n" +
            "REGLAS IMPORTANTES:\n" +
            "- A veces DEBES decir \"Simón dice\" (al inicio, al final o en medio de la frase) para que el jugador deba obedecer.\n" +
            "- A veces NO debes decirlo PARA ENGAÑAR al jugador. Cuando no lo digas, haz que la orden suene natural para que el jugador caiga en la trampa.\n" +
            "- Hay DOS tipos de rondas, y NUNCA se mezclan en la misma orden:\n" +
            "  * Rondas de GESTO+POSICIÓN: pide \"mano abierta\" o \"puño cerrado\" en \"arriba-izquierda\", \"arriba-derecha\", \"abajo-izquierda\", \"abajo-derecha\" o \"centro\".\n" +
            "  * Rondas de EMOCIÓN: pide una expresión facial como \"sonríe\", \"pon cara de enojado\", \"muestra una expresión triste\", \"cara neutral\" o \"cara de sorprendido\".\n" +
            "- NUNCA combines gesto+posición con emoción en la misma orden. Una ronda es O gesto O emoción, jamás los dos.\n" +
            "- Varía tu estilo: a veces usa frases largas con relleno (\"A ver, qué tal si...\", \"Vamos, ahora...\"), a veces sé directo y rápido.\n" +
            "- Cuando digas \"Simón dice\", colócalo a veces al inicio, a veces al final, a veces en medio de la frase.\n" +
            "- Las órdenes sin \"Simón dice\" deben sonar naturales, como instrucciones que cualquiera seguiría sin pensar.\n" +
            "- Mantén cada orden en máximo 20 palabras.\n" +
            "- NO uses ningún formato especial, comillas, ni puntuación extra. SOLO el texto de la orden.\n" +
            "- Usas español latino neutro, natural y conversacional.";

        // v5: Emotion rounds alternate with gesture+position rounds.
        // Each round is exclusively one type — never mixed.
        [Header("Emotion Integration (v5 — alternating rounds)")]
        [Tooltip("Probability (0-1) that any given round is emotion-only instead of gesture+position. " +
                 "Default 0.4 = ~40% emotion rounds. Set to 0 to disable emotions entirely.")]
        [SerializeField] [Range(0f, 1f)] private float _emotionRoundProbability = 0.4f;

        /// <summary>When true, emotion rounds are completely disabled regardless of probability.</summary>
        [Tooltip("Master kill-switch for emotion rounds. Overrides _emotionRoundProbability.")]
        [SerializeField] private bool _emotionsDisabled = false;

        // ── Fallback Templates ──────────────────────────────────────────

        private static readonly string[] FallbackTemplates_SimonDice = {
            "Simón dice: {0}",
            "Simón dice que hagas {0}",
            "{0}, ¡lo ordena Simón!",
            "A ver… Simón dice {0}",
            "Simón dice: ¡{0} ya!"
        };

        private static readonly string[] FallbackTemplates_NoSimonDice = {
            "¡{0}!",
            "{0} ahora mismo",
            "¡Rápido, {0}!",
            "Vamos, {0}",
            "A ver, {0}",
            "Qué tal si haces {0}"
        };

        // v5: Emotion-only fallback templates — never include gestures or positions
        private static readonly string[] FallbackTemplates_EmotionSimonDice = {
            "Simón dice: {0}",
            "Simón dice que pongas {0}",
            "{0}, ¡lo ordena Simón!",
            "Simón dice: ¡{0} ya!"
        };

        private static readonly string[] FallbackTemplates_EmotionNoSimonDice = {
            "¡{0}!",
            "¡Rápido, {0}!",
            "Vamos, {0}",
            "A ver, {0}",
            "Qué tal si pones {0}"
        };

        // Phase 1: Only OpenHand and ClosedFist are active for Simon game.
        private static readonly SimonGestureTarget[] EnabledGestureTargets =
        {
            SimonGestureTarget.OpenHand,
            SimonGestureTarget.ClosedFist
        };

        // v5: Whitelist of emotions available for emotion rounds.
        // Edit this array in the Inspector or code to include fewer/more emotions.
        // Default: Happy, Sad, Surprise (3 of the 5 available emotions).
        [Tooltip("Emotions available for emotion rounds. Remove entries to exclude them.")]
        [SerializeField] private SimonEmotionTarget[] _enabledEmotionTargets =
        {
            SimonEmotionTarget.Happy,
            SimonEmotionTarget.Sad,
            SimonEmotionTarget.Surprise
        };

        private static readonly Dictionary<SimonGestureTarget, string> GestureNames = new()
        {
            { SimonGestureTarget.OpenHand,   "mano abierta" },
            { SimonGestureTarget.ClosedFist, "puño cerrado" },
            // REMOVED for Phase 1: Point, Pinch, ThumbDown
        };

        // Phase 2: available zones for random selection (excluding None)
        private static readonly HandZone[] AvailableZones =
        {
            HandZone.UpLeft, HandZone.UpRight,
            HandZone.DownLeft, HandZone.DownRight,
            HandZone.Center
        };

        // v5: Emotion name mappings — active again for alternating emotion rounds.
        private static readonly Dictionary<SimonEmotionTarget, string> EmotionNames = new()
        {
            { SimonEmotionTarget.Happy,    "feliz" },
            { SimonEmotionTarget.Angry,    "enojado" },
            { SimonEmotionTarget.Sad,      "triste" },
            { SimonEmotionTarget.Neutral,  "neutral" },
            { SimonEmotionTarget.Surprise, "sorprendido" },
        };

        /// <summary>
        /// Returns the Spanish display name for an emotion target.
        /// Used for LLM prompts and fallback text generation.
        /// </summary>
        public static string GetEmotionDisplayName(SimonEmotionTarget target)
        {
            return EmotionNames.TryGetValue(target, out string name) ? name : target.ToString();
        }

        // ── English names for EmotionGameBridge lookups ────────────────────

        private static readonly Dictionary<SimonEmotionTarget, string> EmotionEnglishNames = new()
        {
            { SimonEmotionTarget.Happy,    "happy" },
            { SimonEmotionTarget.Angry,    "angry" },
            { SimonEmotionTarget.Sad,      "sad" },
            { SimonEmotionTarget.Neutral,  "neutral" },
            { SimonEmotionTarget.Surprise, "surprise" },
        };

        /// <summary>
        /// Returns the English (lowercase) emotion name for bridge matching.
        /// These keys match EmotionGameBridge._map dictionary exactly.
        /// </summary>
        public static string GetEmotionEnglishName(SimonEmotionTarget target)
        {
            return EmotionEnglishNames.TryGetValue(target, out string name) ? name : target.ToString().ToLowerInvariant();
        }

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
        /// Generates a command asynchronously. Uses pre-planned distribution
        /// for both SaysSimonDice and ContainsSimonDice (they are always equal).
        /// v5: Each round is EITHER gesture+position OR emotion-only — never both.
        /// v6: Removed independent _trickProbability roll — ContainsSimonDice now
        /// always equals SaysSimonDice. The pre-plan already guarantees 1-2 false
        /// rounds per game. Having two independent flags caused contradictions where
        /// the LLM text said "Simón dice", the judge expected obedience, but the UI
        /// showed orange "trick" color.
        /// If LLM fails, automatically falls back to template phrases.
        /// </summary>
        /// <param name="round">Zero-based round index.</param>
        /// <param name="maxRounds">Total rounds in the game.</param>
        /// <param name="llm">LLMConnector instance from MiniGameDependencies.</param>
        /// <param name="onComplete">Callback with the generated SimonCommand.</param>
        public void GenerateCommand(int round, int maxRounds, LLMConnector llm, Action<SimonCommand> onComplete)
        {
            bool saysSimonDice = GetSaysSimonDice(round);

            // v6: ContainsSimonDice always equals SaysSimonDice.
            // The pre-planned distribution (PlanGame) already guarantees
            // 1-2 false rounds per game. No independent random roll needed.
            bool containsSimonDice = saysSimonDice;

            // ── v5: Decide round type — emotion or gesture? ──────────────────
            // Never both. Emotion rounds have no gesture and no zone.
            // Gesture rounds have no emotion target.
            bool isEmotionRound = !_emotionsDisabled && UnityEngine.Random.value < _emotionRoundProbability;

            SimonGestureTarget gestureTarget = SimonGestureTarget.OpenHand; // only used if not emotion
            HandZone zoneTarget = HandZone.Center;                           // only used if not emotion
            SimonEmotionTarget emotionTarget = SimonEmotionTarget.Neutral;   // only used if emotion

            if (isEmotionRound)
            {
                // Emotion round: pick from whitelist (edit _enabledEmotionTargets in Inspector to include fewer emotions)
                var enabled = _enabledEmotionTargets != null && _enabledEmotionTargets.Length > 0
                    ? _enabledEmotionTargets
                    : (SimonEmotionTarget[])Enum.GetValues(typeof(SimonEmotionTarget));
                emotionTarget = enabled[UnityEngine.Random.Range(0, enabled.Length)];
            }
            else
            {
                // Gesture+position round: pick gesture and zone
                gestureTarget = EnabledGestureTargets[UnityEngine.Random.Range(0, EnabledGestureTargets.Length)];
                zoneTarget = AvailableZones[UnityEngine.Random.Range(0, AvailableZones.Length)];
            }

            var cmd = new SimonCommand
            {
                SaysSimonDice = saysSimonDice,
                ContainsSimonDice = containsSimonDice,
                ActionType = isEmotionRound ? SimonActionType.Emotion : SimonActionType.Gesture,
                GestureTarget = gestureTarget,
                ExpectedZone = isEmotionRound ? HandZone.None : zoneTarget,
                EmotionTarget = emotionTarget,
            };

            if (!_useLLM || llm == null)
            {
                cmd.DialogueText = GenerateFallbackText(cmd);
                onComplete?.Invoke(cmd);
                return;
            }

            string condition = containsSimonDice
                ? "DEBES decir \"Simón dice\" en tu orden (al inicio, al final o en medio)."
                : "NO debes decir \"Simón dice\" NI usar la palabra \"Simón\". Haz que suene natural para ENGAÑAR al jugador.";

            string userPrompt;

            if (isEmotionRound)
            {
                string emotionName = EmotionNames[emotionTarget];
                userPrompt = $"Ronda {round + 1} de {maxRounds}. RONDA DE EMOCIÓN.\n{condition}\n" +
                             $"El jugador debe mostrar la expresión facial: \"{emotionName}\".\n" +
                             "NO menciones gestos de mano ni posiciones. Solo la emoción facial.\n" +
                             "Usa frases como 'sonríe', 'pon cara de enojado', 'muestra una expresión triste', etc.\n" +
                             "Genera UNA sola orden en español, natural y conversacional.";
            }
            else
            {
                string gestureName = GestureNames[gestureTarget];
                string zoneName = PositionInstructor.GetZoneDisplayName(zoneTarget);
                userPrompt = $"Ronda {round + 1} de {maxRounds}. RONDA DE GESTO+POSICIÓN.\n{condition}\n" +
                             $"El jugador debe hacer el gesto \"{gestureName}\" en la posición \"{zoneName}\".\n" +
                             "NO menciones emociones, caras ni expresiones faciales. Solo el gesto y la posición.\n" +
                             "Genera UNA sola orden en español, natural y conversacional.";
            }

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
        /// v5: Supports both gesture+position and emotion-only rounds.
        /// </summary>
        private string GenerateFallbackText(SimonCommand cmd)
        {
            string baseAction;

            if (cmd.ActionType == SimonActionType.Emotion)
            {
                // Emotion-only round
                string emotionName = EmotionNames.TryGetValue(cmd.EmotionTarget, out string eName)
                    ? eName : cmd.EmotionTarget.ToString();
                baseAction = $"cara de {emotionName}";
            }
            else
            {
                // Gesture+position round
                string gestureName = GestureNames.TryGetValue(cmd.GestureTarget, out string gName)
                    ? gName : cmd.GestureTarget.ToString();
                string zoneName = PositionInstructor.GetZoneDisplayName(cmd.ExpectedZone);
                baseAction = $"{gestureName} en {zoneName}";
            }

            // Pick appropriate template set based on simon-dice and round type
            string[] templates;
            if (cmd.ActionType == SimonActionType.Emotion)
            {
                templates = cmd.ContainsSimonDice
                    ? FallbackTemplates_EmotionSimonDice
                    : FallbackTemplates_EmotionNoSimonDice;
            }
            else
            {
                templates = cmd.ContainsSimonDice
                    ? FallbackTemplates_SimonDice
                    : FallbackTemplates_NoSimonDice;
            }

            string template = templates[UnityEngine.Random.Range(0, templates.Length)];
            return string.Format(template, baseAction);
        }
    }
}
