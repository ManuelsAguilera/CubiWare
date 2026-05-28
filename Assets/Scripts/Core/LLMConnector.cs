using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using CubiWare.Core.Services;
using CubiWare.Core.Logging;

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
        public int max_completion_tokens;
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
        private const string MODEL_NAME = "llama-3.1-8b-instant";
        private const int MAX_TOKENS = 120;
        private const float TEMPERATURE = 0.7f;

        private GroqConfig _config;

        /// <summary>
        /// Service-layer LLM service for decoupled Groq API interactions.
        /// </summary>
        private GroqLLMService _llmService;

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);

            _config = Resources.Load<GroqConfig>("GroqConfig");
            if (_config == null || string.IsNullOrEmpty(_config.ApiKey))
            {
                _logger.LogError("LLMConnector", "Groq API key invalid or missing. Check GroqConfig.asset.", ServiceErrorCode.LLMAuthenticationFailed);
            }

            // Initialize the service-layer provider
            _llmService = new GroqLLMService();
            _ = InitializeLlmServiceAsync();
        }

        /// <summary>
        /// Initializes the service-layer LLM provider asynchronously.
        /// </summary>
        private async System.Threading.Tasks.Task InitializeLlmServiceAsync()
        {
            bool initialized = await _llmService.InitializeAsync();
            if (initialized)
            {
                _logger.LogInfo("LLMConnector", "GroqLLMService initialized successfully.");
            }
            else
            {
                _logger.LogError("LLMConnector", "GroqLLMService failed to initialize.", ServiceErrorCode.NotInitialized);
            }
        }

        public void Ask(string systemPrompt, string userMessage, Action<string> onComplete, Action<string> onError = null)
        {
            if (_config == null || string.IsNullOrEmpty(_config.ApiKey))
            {
                _logger.LogError("LLMConnector", "API Key missing in Ask()", ServiceErrorCode.LLMAuthenticationFailed);
                onError?.Invoke("API Key missing");
                return;
            }

            _logger.LogInfo("LLMConnector", $"Ask called. Delegating to existing Groq pipeline. Prompt: {systemPrompt} | User: {userMessage}");

            StartCoroutine(CoAskGroq(systemPrompt, userMessage, onComplete, onError, false));
        }

        private IEnumerator CoAskGroq(string systemPrompt, string userMessage, Action<string> onComplete, Action<string> onError, bool isRetry)
        {
            GroqRequest requestData = new GroqRequest
            {
                model = MODEL_NAME,
                max_tokens = MAX_TOKENS,
                max_completion_tokens = MAX_TOKENS,
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
                    _logger.LogError("LLMConnector", "Connection failed. " + request.error, ServiceErrorCode.LLMConnectionFailed);
                    onError?.Invoke("Connection failed");
                }
                else if (request.responseCode == 401)
                {
                    _logger.LogError("LLMConnector", "Groq API key invalid or missing. Check GroqConfig.asset.", ServiceErrorCode.LLMAuthenticationFailed);
                    onError?.Invoke("Unauthorized");
                }
                else if (request.responseCode == 429)
                {
                    if (!isRetry)
                    {
                        _logger.LogWarning("LLMConnector", "Rate limited. Retrying in 2 seconds...");
                        yield return new WaitForSeconds(2f);
                        StartCoroutine(CoAskGroq(systemPrompt, userMessage, onComplete, onError, true));
                    }
                    else
                    {
                        _logger.LogError("LLMConnector", "Rate limited again. Failing.", ServiceErrorCode.LLMRateLimited);
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
                            _logger.LogWarning("LLMConnector", "Parsed successfully but text was empty.");
                            onError?.Invoke("Empty response");
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("LLMConnector", "Failed to parse response. " + e.Message, ServiceErrorCode.LLMResponseParseError);
                        onError?.Invoke("Parse error");
                    }
                }
                else
                {
                    string respText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    _logger.LogError("LLMConnector", $"HTTP Error {request.responseCode} - {request.error}. Body: {respText}", ServiceErrorCode.LLMConnectionFailed);
                    onError?.Invoke("HTTP Error: " + request.responseCode + (string.IsNullOrEmpty(respText) ? string.Empty : " - " + respText));
                }
            }
        }
    }
}
