using System.Collections.Generic;
using UnityEngine;
using ARcadeRush.Core;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace ARcadeRush.Hand
{
    public class HandModel : MonoBehaviour
    {
        [SerializeField] private Color _color = new Color(0.11f, 0.62f, 0.46f, 0.7f); // Teal #1D9E75 with alpha

        [Header("Sizing")]
        [SerializeField] private float _jointRadius = 0.18f;
        [SerializeField] private float _boneWidth = 0.10f;
        [SerializeField, Tooltip("Scales the entire projected hand (smaller = smaller hand)")]
        private float _overallScale = 0.25f;

        [Header("Motion")]
        [SerializeField] private float _smoothSpeed = 30f;

        private IHandDetector _handDetector;
        private Transform[] _joints = new Transform[21];
        private LineRenderer[] _bones = new LineRenderer[20];
        private GameObject _modelRoot;
        private bool _haveLastJointWorld;
        private List<Vector2> _lastLandmarks;
        private Camera _mainCamera;

        private static readonly int[,] BoneConnections = new int[,]
        {
            {0, 1}, {1, 2}, {2, 3}, {3, 4},       // Thumb
            {0, 5}, {5, 6}, {6, 7}, {7, 8},       // Index
            {0, 9}, {9, 10}, {10, 11}, {11, 12},  // Middle
            {0, 13}, {13, 14}, {14, 15}, {15, 16},// Ring
            {0, 17}, {17, 18}, {18, 19}, {19, 20} // Pinky
        };

        private void Awake()
        {
            _mainCamera = Camera.main;

            // Resolve IHandDetector
            _handDetector = FindFirstObjectByType<MonoBehaviour>() as IHandDetector;
            if (_handDetector == null && MediaPipeController.Instance != null)
            {
                _handDetector = MediaPipeController.Instance.HandDetector;
            }

            _modelRoot = new GameObject("HandVisualRoot");
            _modelRoot.transform.SetParent(transform);
            _modelRoot.transform.localScale = Vector3.one * _overallScale;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material mat = new Material(shader);
            mat.color = _color;

            float sphereDiameter = _jointRadius * 2f;
            for (int i = 0; i < 21; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(_modelRoot.transform);
                sphere.transform.localScale = Vector3.one * sphereDiameter;
                sphere.GetComponent<Renderer>().material = mat;
                Destroy(sphere.GetComponent<Collider>());
                sphere.SetActive(false);
                _joints[i] = sphere.transform;
            }

            for (int i = 0; i < 20; i++)
            {
                GameObject boneObj = new GameObject($"Bone_{i}");
                boneObj.transform.SetParent(_modelRoot.transform);
                var lr = boneObj.AddComponent<LineRenderer>();
                lr.material = mat;
                lr.startWidth = _boneWidth;
                lr.endWidth = _boneWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.enabled = false;
                _bones[i] = lr;
            }

            _modelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (_handDetector != null)
            {
                _handDetector.OnHandDetected += HandleHandDetected;
                _handDetector.OnHandLost += HandleHandLost;
            }
        }

        private void OnDisable()
        {
            if (_handDetector != null)
            {
                _handDetector.OnHandDetected -= HandleHandDetected;
                _handDetector.OnHandLost -= HandleHandLost;
            }
        }

        private void HandleHandDetected(HandLandmarkData data)
        {
            _lastLandmarks = data.Landmarks;
        }

        private void HandleHandLost()
        {
            _lastLandmarks = null;
            _haveLastJointWorld = false;
            for (int i = 0; i < 21; i++)
            {
                if (_joints[i] != null)
                    _joints[i].gameObject.SetActive(false);
            }
            foreach (LineRenderer lr in _bones)
            {
                if (lr != null) lr.enabled = false;
            }
            _modelRoot.SetActive(false);
        }

        private void Update()
        {
            if (_lastLandmarks == null || _lastLandmarks.Count < 21)
            {
                _haveLastJointWorld = false;
                _modelRoot.SetActive(false);
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            _modelRoot.SetActive(true);
            _modelRoot.transform.localScale = Vector3.one * _overallScale;

            float lerpT = Mathf.Clamp01(Time.deltaTime * _smoothSpeed);
            bool snap = !_haveLastJointWorld;
            _haveLastJointWorld = true;

            // Simple 2D to 3D mapping for visualization (matching the spirit of Hand3DProjector but local)
            for (int i = 0; i < 21; i++)
            {
                Vector3 screenPos = new Vector3(
                    (1f - _lastLandmarks[i].x) * Screen.width,
                    (1f - _lastLandmarks[i].y) * Screen.height,
                    10f // Fixed depth for visualization
                );
                Vector3 worldPos = _mainCamera.ScreenToWorldPoint(screenPos);

                _joints[i].gameObject.SetActive(true);
                _joints[i].position = snap ? worldPos : Vector3.Lerp(_joints[i].position, worldPos, lerpT);
            }

            for (int i = 0; i < 20; i++)
            {
                int startIdx = BoneConnections[i, 0];
                int endIdx = BoneConnections[i, 1];

                if (!_joints[startIdx].gameObject.activeSelf || !_joints[endIdx].gameObject.activeSelf)
                {
                    _bones[i].enabled = false;
                    continue;
                }

                _bones[i].enabled = true;
                Vector3 a = _joints[startIdx].position;
                Vector3 b = _joints[endIdx].position;
                _bones[i].SetPosition(0, a);
                _bones[i].SetPosition(1, b);
            }
        }
    }
}
