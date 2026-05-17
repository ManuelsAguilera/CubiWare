using UnityEngine;
using UnityEditor;

namespace ARcadeRush.Hand
{
    [CustomEditor(typeof(GestureRecordingManager))]
    public class GestureRecordingEditor : Editor
    {
        private string _gestureToAnalyze = "";
        private string _suggestedHeuristic = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GestureRecordingManager manager = (GestureRecordingManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);

            _gestureToAnalyze = EditorGUILayout.TextField("Gesture Name", _gestureToAnalyze);

            if (GUILayout.Button("Suggest Heuristic"))
            {
                _suggestedHeuristic = manager.SuggestHeuristic(_gestureToAnalyze);
            }

            if (!string.IsNullOrEmpty(_suggestedHeuristic))
            {
                EditorGUILayout.TextArea(_suggestedHeuristic);
                if (GUILayout.Button("Copy to Clipboard"))
                {
                    EditorGUIUtility.systemCopyBuffer = _suggestedHeuristic;
                }
            }
        }
    }
}
