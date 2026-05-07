using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ARcadeRush.Core
{
    [Serializable]
    public class GroqMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class GroqRequest
    {
        public string model;
        public int max_tokens;
        public float temperature;
        public GroqMessage[] messages;
    }

    [Serializable]
    public class GroqResponse
    {
        public GroqChoice[] choices;
    }

    [Serializable]
    public class GroqChoice
    {
        public GroqMessage message;
    }

    public class LLMConnector : MonoBehaviour
    {
        public static LLMConnector Instance { get; private set; }

        private const string GROQ_API_URL = "https://api.groq.com/openai/v1/chat/completions";
        private const string MODEL_NAME = "llama-3-8b-8192";
        private const int MAX_TOKENS = 120;
        private const float TEMPERATURE = 0.7f;

        private GroqConfig _config;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);

            _config = Resources.Load<GroqConfig>("GroqConfig");
            if (_config == null || string.IsNullOrEmpty(_config.ApiKey))
            {
                Debug.LogError("LLMConnector: Groq API key invalid or missing. Check GroqConfig.asset.");
            }
        }

        public void Ask(string systemPrompt, string userMessage, Action<string> onComplete, Action<string> onError = null)
        {
            if (_config == null || string.IsNullOrEmpty(_config.ApiKey))
            {
                onError?.Invoke("API Key missing");
                return;
            }

            StartCoroutine(CoAskGroq(systemPrompt, userMessage, onComplete, onError, false));
        }

        private IEnumerator CoAskGroq(string systemPrompt, string userMessage, Action<string> onComplete, Action<string> onError, bool isRetry)
        {
            GroqRequest requestData = new GroqRequest
            {
                model = MODEL_NAME,
                max_tokens = MAX_TOKENS,
                temperature = TEMPERATURE,
                messages = new GroqMessage[]
                {
                    new GroqMessage { role = "system", content = systemPrompt },
                    new GroqMessage { role = "user", content = userMessage }
                }
            };

            string jsonPayload = JsonUtility.ToJson(requestData);

            using (UnityWebRequest request = new UnityWebRequest(GROQ_API_URL, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw); 
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {_config.ApiKey}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.LogError("LLMConnector: Connection failed. " + request.error);
                    onError?.Invoke("Connection failed");
                }
                else if (request.responseCode == 401)
                {
                    Debug.LogError("LLMConnector: Groq API key invalid or missing. Check GroqConfig.asset.");
                    onError?.Invoke("Unauthorized");
                }
                else if (request.responseCode == 429)
                {
                    if (!isRetry)
                    {
                        Debug.LogWarning("LLMConnector: Rate limited. Retrying in 2 seconds...");
                        yield return new WaitForSeconds(2f);
                        StartCoroutine(CoAskGroq(systemPrompt, userMessage, onComplete, onError, true));
                    }
                    else
                    {
                        Debug.LogError("LLMConnector: Rate limited again. Failing.");
                        onError?.Invoke("Rate limited");
                    }
                }
                else if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        GroqResponse responseJson = JsonUtility.FromJson<GroqResponse>(request.downloadHandler.text);
                        string resultText = string.Empty;
                        
                        if (responseJson != null && responseJson.choices != null && responseJson.choices.Length > 0 && responseJson.choices[0].message != null)
                        {
                            resultText = responseJson.choices[0].message.content;
                        }

                        if (!string.IsNullOrEmpty(resultText))
                        {
                            onComplete?.Invoke(resultText.Trim());
                        }
                        else
                        {
                            Debug.LogWarning("LLMConnector: Parsed successfully but text was empty.");
                            onError?.Invoke("Empty response");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("LLMConnector: Failed to parse response. " + e.Message);
                        onError?.Invoke("Parse error");
                    }
                }
                else
                {
                    Debug.LogError($"LLMConnector: HTTP Error {request.responseCode} - {request.error}");
                    onError?.Invoke("HTTP Error: " + request.responseCode);
                }
            }
        }
    }
}
