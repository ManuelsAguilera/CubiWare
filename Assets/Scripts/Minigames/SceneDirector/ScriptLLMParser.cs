using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Static utility that parses LLM-generated JSON strings into typed script data.
    /// Handles malformed LLM output with liberal fallback heuristics.
    ///
    /// Expected LLM JSON format:
    /// {
    ///     "script": [
    ///         {"e": 1, "t": 5},
    ///         {"e": 2, "t": 4},
    ///         {"e": 3, "t": 3}
    ///     ],
    ///     "intro": "¡Bienvenido al escenario, actor!"
    /// }
    ///
    /// Where e (emotion): 1=Happy, 2=Surprised, 3=Angry
    ///       t (time limit): seconds, 3-8
    /// </summary>
    public static class ScriptLLMParser
    {
        private const string LogServiceName = "ScriptLLMParser";

        // Regex to extract JSON object even if buried in markdown/fence blocks
        private static readonly Regex JsonBlockRegex = new Regex(
            @"\{[^{}]*""script""\s*:\s*\[[^\]]*\][^{}]*\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ElementRegex = new Regex(
            @"""e""\s*:\s*(\d)\s*,\s*""t""\s*:\s*([\d.]+)",
            RegexOptions.Compiled);

        private static readonly Regex IntroRegex = new Regex(
            @"""intro""\s*:\s*""([^""]*)""",
            RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse an LLM response string into a sequence of ScriptElements and an intro text.
        /// </summary>
        /// <param name="json">Raw LLM response string.</param>
        /// <param name="elements">Parsed elements (empty if parse fails).</param>
        /// <param name="intro">Parsed intro text (empty if parse fails).</param>
        /// <returns>True if at least one valid element was extracted.</returns>
        public static bool TryParse(string json, out List<ScriptElement> elements, out string intro)
        {
            elements = new List<ScriptElement>();
            intro = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "Empty JSON input — cannot parse.");
                return false;
            }

            // Step 1: Extract the JSON block (handles markdown fences like ```json ... ```)
            Match jsonMatch = JsonBlockRegex.Match(json);
            string jsonBlock = jsonMatch.Success ? jsonMatch.Value : json;

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"Parsing LLM response ({jsonBlock.Length} chars)...");

            // Step 2: Extract intro text
            Match introMatch = IntroRegex.Match(jsonBlock);
            if (introMatch.Success)
            {
                intro = introMatch.Groups[1].Value
                    .Replace("\\\"", "\"")   // Unescape quotes
                    .Replace("\\n", "\n");   // Unescape newlines
            }

            // Step 3: Extract elements
            MatchCollection elementMatches = ElementRegex.Matches(jsonBlock);
            if (elementMatches.Count == 0)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    "No valid emotion elements found in LLM JSON. Raw: " +
                    (jsonBlock.Length > 120 ? jsonBlock.Substring(0, 120) + "..." : jsonBlock));
                return false;
            }

            foreach (Match match in elementMatches)
            {
                if (!int.TryParse(match.Groups[1].Value, out int emotionInt))
                    continue;
                if (!float.TryParse(match.Groups[2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float timeLimit))
                    continue;

                // Validate emotion range (1=Happy, 2=Surprised, 3=Angry)
                if (emotionInt < 1 || emotionInt > 3)
                {
                    ServiceLogger.Instance.LogWarning(LogServiceName,
                        $"Skipping element with invalid emotion index: {emotionInt}");
                    continue;
                }

                // Clamp time limit to safe range
                timeLimit = Mathf.Clamp(timeLimit, 3f, 8f);

                elements.Add(new ScriptElement
                {
                    RequiredEmotion = (EmotionLabel)emotionInt,
                    TimeLimit = timeLimit
                });
            }

            // Also clamp sequence length to reasonable range (3-6)
            if (elements.Count > 6)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    $"LLM returned {elements.Count} elements — truncating to 6.");
                elements = elements.GetRange(0, 6);
            }

            if (elements.Count < 3)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    $"LLM returned only {elements.Count} elements — need at least 3. Falling back.");
                return false;
            }

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"Parsed {elements.Count} elements. Intro: \"{(intro.Length > 60 ? intro.Substring(0, 60) + "..." : intro)}\"");
            return true;
        }
    }
}