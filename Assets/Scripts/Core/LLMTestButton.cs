using UnityEngine;
using UnityEngine.UI;
using CubiWare.Core.Logging;
 
namespace ARcadeRush.Core
{
    public class LLMTestButton : MonoBehaviour
    {
        private const string LogServiceName = "LLMTestButton";

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
            ServiceLogger.Instance.LogInfo(LogServiceName, "Sending test request...");
            float startTime = Time.realtimeSinceStartup;

            LLMConnector.Instance.Ask(
                "You are a helpful assistant. Reply in one short sentence.",
                "Say hello to the ARcade Rush team!",
                onComplete: (text) =>
                {
                    float duration = Time.realtimeSinceStartup - startTime;
                    ServiceLogger.Instance.LogInfo(LogServiceName, $"Success in {duration:F2}s\nResponse: {text}");
                },
                onError: (err) =>
                {
                    float duration = Time.realtimeSinceStartup - startTime;
                    ServiceLogger.Instance.LogError(LogServiceName, $"Failed in {duration:F2}s. Error: {err}", ServiceErrorCode.LLMConnectionFailed);
                }
            );
        }
    }
}
