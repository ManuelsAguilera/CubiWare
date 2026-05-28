using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ARcadeRush.Core;
using Mediapipe.Tasks.Components.Containers;
using CubiWare.Core.Logging;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Utility to record hand gestures from MediaPipe and save them to a JSON database.
    /// Provides hotkeys and logging to help the user build a template library.
    /// </summary>
    public class GestureRecordingManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string _outputFileName = "RecordedGestures.json";
        [SerializeField] private KeyCode _recordToggleKey = KeyCode.R;
        [SerializeField] private KeyCode _saveKey = KeyCode.S;

        [Header("Current Recording")]
        [SerializeField] private string _currentGestureName = "NewGesture";
        
        private bool _isRecording = false;
        private RecordedGesture _activeRecording;
        private GestureDatabase _database = new GestureDatabase();

        private void Awake()
        {
            LoadDatabase();
        }

        private void OnEnable()
        {
            if (MediaPipeController.Instance != null)
                MediaPipeController.Instance.OnHandDetected += HandleHandDetected;
        }

        private void OnDisable()
        {
            if (MediaPipeController.Instance != null)
                MediaPipeController.Instance.OnHandDetected -= HandleHandDetected;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_recordToggleKey))
            {
                if (!_isRecording) StartRecording();
                else StopRecording();
            }

            if (Input.GetKeyDown(_saveKey))
            {
                SaveDatabase();
            }
        }

        private void HandleHandDetected(NormalizedLandmarks landmarks)
        {
            if (!_isRecording || _activeRecording == null) return;

            _activeRecording.Snapshots.Add(new HandSnapshot(landmarks));
        }

        private void StartRecording()
        {
            _isRecording = true;
            _activeRecording = new RecordedGesture(_currentGestureName);
            ServiceLogger.Instance.LogInfo("GestureRecorder", $"[RECORDING STARTED] Capture for: {_currentGestureName}");
        }

        private void StopRecording()
        {
            _isRecording = false;
            if (_activeRecording != null && _activeRecording.Snapshots.Count > 0)
            {
                _database.Gestures.Add(_activeRecording);
                ServiceLogger.Instance.LogInfo("GestureRecorder", $"[RECORDING STOPPED] Saved {_activeRecording.Snapshots.Count} snapshots for: {_activeRecording.GestureName}");
            }
            else
            {
                ServiceLogger.Instance.LogInfo("GestureRecorder", "[RECORDING STOPPED] No data captured.");
            }
            _activeRecording = null;
        }

        private void SaveDatabase()
        {
            string path = Path.Combine(Application.dataPath, "Resources", _outputFileName);
            string json = JsonUtility.ToJson(_database, true);
            
            // Ensure Resources directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            
            File.WriteAllText(path, json);
            ServiceLogger.Instance.LogInfo("GestureRecorder", $"[DATABASE SAVED] Successfully wrote to: {path}");
        }

        private void LoadDatabase()
        {
            string path = Path.Combine(Application.dataPath, "Resources", _outputFileName);
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loadedDatabase = JsonUtility.FromJson<GestureDatabase>(json);
                if (loadedDatabase != null)
                {
                    _database = loadedDatabase;
                    ServiceLogger.Instance.LogInfo("GestureRecorder", $"Loaded database with {_database.Gestures.Count} gestures.");
                }
                else
                {
                    ServiceLogger.Instance.LogInfo("GestureRecorder", "Database file found but failed to deserialize. Starting with empty database.");
                    _database = new GestureDatabase();
                }
            }
        }

        /// <summary>
        /// Analyzes a recorded gesture and suggests a heuristic line for the CSV.
        /// </summary>
        public string SuggestHeuristic(string gestureName)
        {
            RecordedGesture gesture = _database.Gestures.Find(g => g.GestureName == gestureName);
            if (gesture == null || gesture.Snapshots.Count == 0) return "Gesture not found.";

            // Logic to analyze UP/DOWN states across all snapshots
            // (Comparing tip y vs MCP y as in GestureDetector.cs)
            // Finger indices: Thumb(4/2), Index(8/5), Middle(12/9), Ring(16/13), Pinky(20/17)
            
            return AnalyzeFingerStates(gesture);
        }

        private string AnalyzeFingerStates(RecordedGesture gesture)
        {
            // Count "UP" frames for each finger using the same logic as GestureDetector
            // (Note: GestureDetector uses y > joint for UP in inverted space)
            int[] upCounts = new int[5];
            int total = gesture.Snapshots.Count;

            foreach (var snap in gesture.Snapshots)
            {
                var l = snap.NormalizedLandmarks;
                if (l.Count < 21) continue;

                if (l[4].y > l[2].y) upCounts[0]++; // Thumb
                if (l[8].y > l[5].y) upCounts[1]++; // Index
                if (l[12].y > l[9].y) upCounts[2]++; // Middle
                if (l[16].y > l[13].y) upCounts[3]++; // Ring
                if (l[20].y > l[17].y) upCounts[4]++; // Pinky
            }

            string[] states = new string[5];
            for (int i = 0; i < 5; i++)
            {
                float ratio = (float)upCounts[i] / total;
                if (ratio > 0.8f) states[i] = "UP";
                else if (ratio < 0.2f) states[i] = "DOWN";
                else states[i] = "ANY";
            }

            // Determine custom rule based on average thumb position
            string customRule = "None";
            float avgDist = 0;
            float avgXDiff = 0;
            float avgXThumbToIdxMCP = 0;
            foreach(var snap in gesture.Snapshots)
            {
                var l = snap.NormalizedLandmarks;
                avgDist += Vector2.Distance(l[4], l[2]);
                avgXDiff += Mathf.Abs(l[4].x - l[2].x);
                avgXThumbToIdxMCP += (l[4].x - l[5].x);
            }
            avgDist /= total;
            avgXDiff /= total;
            avgXThumbToIdxMCP /= total;

            if (avgDist < 0.1f) customRule = "ThumbTucked";
            else if (avgXDiff > 0.15f) customRule = "ThumbExtended";
            else if (avgXThumbToIdxMCP < -0.05f) customRule = "ThumbInsidePalm";
            else if (avgXThumbToIdxMCP > 0.05f) customRule = "ThumbOutsidePalm";

            return $"{gesture.GestureName},{states[0]},{states[1]},{states[2]},{states[3]},{states[4]},1.0,{customRule}";
        }
    }
}
