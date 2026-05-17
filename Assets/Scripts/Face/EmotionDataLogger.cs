using System;
using System.IO;
using UnityEngine;
using CubiWare.Core.Logging;
 
namespace ARcadeRush.Face
{
    /// <summary>
    /// Developer utility to log raw facial metrics to a CSV file for threshold tuning.
    /// Tag each row with a "Ground Truth" label using keyboard shortcuts:
    ///   [H] = Happy
    ///   [S] = Surprised
    ///   [A] = Angry
    ///   [N] = Neutral
    /// </summary>
    [RequireComponent(typeof(FaceLandmarkReader))]
    public class EmotionDataLogger : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string _fileName = "EmotionLogs.csv";
        [SerializeField] private float _logInterval = 0.1f; // 10 samples per second
 
        private FaceLandmarkReader _reader;
        private string _filePath;
        private float _timer = 0f;
        private bool _headerWritten = false;
 
        private void Awake()
        {
            _reader = GetComponent<FaceLandmarkReader>();
            _filePath = Path.Combine(Application.dataPath, _fileName);
        }
 
        private void Start()
        {
            InitializeCSV();
        }
 
        private void Update()
        {
            string label = GetCurrentTag();
            if (string.IsNullOrEmpty(label)) return;
 
            _timer += Time.deltaTime;
            if (_timer >= _logInterval)
            {
                _timer = 0f;
                LogMetrics(label);
            }
        }
 
        private string GetCurrentTag()
        {
            if (Input.GetKey(KeyCode.H)) return "Happy";
            if (Input.GetKey(KeyCode.S)) return "Surprised";
            if (Input.GetKey(KeyCode.A)) return "Angry";
            if (Input.GetKey(KeyCode.N)) return "Neutral";
            return null;
        }
 
        private void InitializeCSV()
        {
            if (!File.Exists(_filePath))
            {
                string header = "Timestamp,Label,MouthOpen,EyeL,EyeR,BrowRaise,Smile,Furrow,Frown,MouthPress,Pucker,HeadConfidence\n";
                File.WriteAllText(_filePath, header);
                _headerWritten = true;
                ServiceLogger.Instance.LogInfo("EmotionDataLogger", $"Created new log file at: {_filePath}");
            }
            else
            {
                _headerWritten = true;
                ServiceLogger.Instance.LogInfo("EmotionDataLogger", $"Appending to existing log file at: {_filePath}");
            }
        }
 
        private void LogMetrics(string label)
        {
            if (!_headerWritten) return;
            if (!_reader.HasFreshData) return;
 
            float[] metrics = _reader.NormalizedMetrics;
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            
            // Format: Timestamp,Label,MouthOpen,EyeL,EyeR,BrowRaise,Smile,Furrow,Frown,MouthPress,Pucker,HeadConfidence
            string line = string.Format("{0},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4}\n",
                timestamp,
                label,
                metrics[0], // MouthOpen
                metrics[1], // EyeL
                metrics[2], // EyeR
                metrics[3], // BrowRaise
                metrics[4], // Smile
                metrics[5], // Furrow
                metrics[6], // Frown
                metrics[7], // MouthPress
                metrics[8], // Pucker
                _reader.HeadConfidence
            );
 
            try
            {
                File.AppendAllText(_filePath, line);
            }
            catch (Exception ex)
            {
                ServiceLogger.Instance.LogError("EmotionDataLogger", $"Failed to write to CSV: {ex.Message}", ServiceErrorCode.DataStoreWriteFailed);
            }
        }
    }
}
