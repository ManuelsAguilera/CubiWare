using System.Collections;
using UnityEngine;
using TMPro;

namespace ARcadeRush.UI
{
    public class DialogueUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _dialogueText;
        [SerializeField] private CanvasGroup _canvasGroup;

        private Coroutine _dialogueCo;

        private void Awake()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        public void ShowLine(string text, float duration)
        {
            if (_dialogueCo != null)
            {
                StopCoroutine(_dialogueCo);
            }

            if (_dialogueText != null)
            {
                _dialogueText.text = text;
            }

            _dialogueCo = StartCoroutine(CoShowDialogue(duration));
        }

        private IEnumerator CoShowDialogue(float duration)
        {
            // Fade in
            float t = 0;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Clamp01(t / 0.25f);
                yield return null;
            }

            // Hold
            yield return new WaitForSeconds(duration);

            // Fade out
            t = 0;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Clamp01(1f - (t / 0.25f));
                yield return null;
            }

            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            _dialogueCo = null;
        }
    }
}
