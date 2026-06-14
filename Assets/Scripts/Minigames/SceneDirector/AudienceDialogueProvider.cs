using System.Collections.Generic;
using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Face;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Pre-generates short audience reaction lines via the LLM — ONE request per
    /// script element, fired at element start — and hands them out on demand while
    /// the approval bar moves. Never calls the LLM per-frame or per-event.
    /// Falls back to hardcoded lines when the LLM is missing, slow, or errors out
    /// (same pattern as SimonCommandGenerator's fallback text).
    /// </summary>
    public class AudienceDialogueProvider
    {
        private static readonly string[] FallbackApproval =
            { "¡Bravo!", "¡Eso es!", "¡Qué actuación!", "¡Sigue así!" };
        private static readonly string[] FallbackDisapproval =
            { "¡Buuu!", "¿Eso es actuar?", "¡Devuélvanme mi entrada!", "¡Más emoción!" };

        private List<string> _approval    = new List<string>(FallbackApproval);
        private List<string> _disapproval = new List<string>(FallbackDisapproval);
        private int _approvalIdx;
        private int _disapprovalIdx;

        private const string SystemPrompt =
            "Eres el público de un teatro en un videojuego. Responde SOLO con 6 frases muy " +
            "cortas en español separadas por '|'. Las primeras 3 son de aprobación/ánimo al " +
            "actor, las últimas 3 de abucheo o burla suave. Sin números, sin comillas, sin " +
            "explicaciones, sin texto adicional.";

        /// <summary>
        /// Fires one LLM request for the given emotion. The previous lines (or the
        /// fallbacks) stay available until the response arrives, so callers can ask
        /// for lines at any time.
        /// </summary>
        public void PrepareFor(EmotionLabel emotion, LLMConnector llm)
        {
            _approvalIdx = _disapprovalIdx = 0;
            if (llm == null) return;

            string user = $"El actor debe interpretar la emoción: {emotion}.";
            llm.Ask(SystemPrompt, user,
                onComplete: response =>
                {
                    var parts = response.Split('|');
                    var ok  = new List<string>();
                    var bad = new List<string>();
                    foreach (var raw in parts)
                    {
                        string line = raw.Trim().Trim('"');
                        if (line.Length == 0 || line.Length > 60) continue;
                        if (ok.Count < 3) ok.Add(line);
                        else              bad.Add(line);
                    }
                    if (ok.Count  >= 2) _approval    = ok;
                    if (bad.Count >= 2) _disapproval = bad;
                    Debug.Log($"[AudienceDialogue] 💬 LLM lines ready — approval={ok.Count} boo={bad.Count}");
                },
                onError: err =>
                    Debug.Log($"[AudienceDialogue] LLM failed ({err}) — keeping fallback lines"));
        }

        public string NextApproval()    => _approval[(_approvalIdx++)       % _approval.Count];
        public string NextDisapproval() => _disapproval[(_disapprovalIdx++) % _disapproval.Count];
    }
}
