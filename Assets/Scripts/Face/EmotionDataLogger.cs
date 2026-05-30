using System;
using System.IO;
using UnityEngine;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Logging;

namespace ARcadeRush.Face
{
    /// <summary>
    /// Developer utility — logs DeepFace emotion scores to CSV for analysis.
    /// Hold a key to tag the current frame with a ground-truth label:
    ///   [H] = Happy   [S] = Surprised   [A] = Angry   [N] = Neutral
    ///   [F] = Fear    [D] = Disgust      [Z] = Sad
    /// </summary>
    public class EmotionDataLogger : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string _fileName    = "EmotionLogs.csv";
        [SerializeField] private float  _logInterval = 0.1f;

        private string _filePath;
        private float  _timer          = 0f;
        private bool   _headerWritten  = false;

        private void Start()
        {
            _filePath = Path.Combine(Application.dataPath, _fileName);
            InitializeCSV();
        }

        private void Update()
        {
            string label = GetCurrentTag();
            if (string.IsNullOrEmpty(label)) return;

            _timer += Time.deltaTime;
            if (_timer < _logInterval) return;
            _timer = 0f;
            LogScores(label);
        }

        private string GetCurrentTag()
        {
            if (Input.GetKey(KeyCode.H)) return "Happy";
            if (Input.GetKey(KeyCode.S)) return "Surprised";
            if (Input.GetKey(KeyCode.A)) return "Angry";
            if (Input.GetKey(KeyCode.N)) return "Neutral";
            if (Input.GetKey(KeyCode.F)) return "Fear";
            if (Input.GetKey(KeyCode.D)) return "Disgust";
            if (Input.GetKey(KeyCode.Z)) return "Sad";
            return null;
        }

        private void InitializeCSV()
        {
            if (!File.Exists(_filePath))
            {
                string header = "Timestamp,Label,Dominant,Confidence,FaceDetected,Angry,Disgust,Fear,Happy,Sad,Surprise,Neutral\n";
                File.WriteAllText(_filePath, header);
                ServiceLogger.Instance.LogInfo("EmotionDataLogger", $"Created: {_filePath}");
            }
            else
            {
                ServiceLogger.Instance.LogInfo("EmotionDataLogger", $"Appending to: {_filePath}");
            }
            _headerWritten = true;
        }

        private void LogScores(string label)
        {
            if (!_headerWritten) return;

            var bridge = EmotionGameBridge.Instance;
            if (bridge == null || !bridge.IsConnected) return;

            var d = bridge.LatestData;
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = string.Format(
                "{0},{1},{2},{3:F4},{4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4}\n",
                ts, label,
                d.dominant_emotion, d.confidence, d.face_detected,
                d.scores.angry, d.scores.disgust, d.scores.fear,
                d.scores.happy, d.scores.sad, d.scores.surprise, d.scores.neutral);

            try { File.AppendAllText(_filePath, line); }
            catch (Exception ex)
            {
                ServiceLogger.Instance.LogError("EmotionDataLogger",
                    $"CSV write failed: {ex.Message}", ServiceErrorCode.DataStoreWriteFailed);
            }
        }

        // Expose LatestData publicly for external consumers
        public EmotionData LatestData => EmotionGameBridge.Instance?.LatestData ?? default;
    }
}
