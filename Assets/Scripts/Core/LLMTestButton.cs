using UnityEngine;
using UnityEngine.UI;

namespace ARcadeRush.Core
{
    public class LLMTestButton : MonoBehaviour
    {
        private void Start()
        {
            var btn = GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(TestLLM);
            }
        }

        private void TestLLM()
        {
            Debug.Log("LLMTestButton: Sending test request...");
            float startTime = Time.realtimeSinceStartup;

            LLMConnector.Instance.Ask(
                "You are a helpful assistant. Reply in one short sentence.",
                "Say hello to the ARcade Rush team!",
                onComplete: (text) => 
                {
                    float duration = Time.realtimeSinceStartup - startTime;
                    Debug.Log($"LLMTestButton: Success in {duration:F2}s\nResponse: {text}");
                },
                onError: (err) => 
                {
                    float duration = Time.realtimeSinceStartup - startTime;
                    Debug.LogError($"LLMTestButton: Failed in {duration:F2}s. Error: {err}");
                }
            );
        }
    }
}
