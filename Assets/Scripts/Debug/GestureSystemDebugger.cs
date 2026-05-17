using UnityEngine;
using ARcadeRush.Hand;

namespace ARcadeRush.DebuggerTool
{
    public class GestureSystemDebugger : MonoBehaviour
    {
        private GestureDetector _detector;

        void Start()
        {
            _detector = FindFirstObjectByType<GestureDetector>();
            if (_detector == null)
            {
                Debug.LogError("[GestureDebugger] No GestureDetector found in scene!");
                return;
            }

            _detector.OnGestureDetected += (name) => {
                Debug.Log($"[GestureDebugger] Event Fired: {name}");
            };
        }
    }
}
