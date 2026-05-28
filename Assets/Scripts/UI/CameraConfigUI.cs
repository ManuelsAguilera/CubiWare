using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Core;
using CubiWare.Core.Logging;
 
namespace ARcadeRush.UI
{
    public class CameraConfigUI : MonoBehaviour
    {
        [Header("Always Visible")]
        [SerializeField] private TMP_Text _cameraNameLabel;
        [SerializeField] private Button _startCameraButton;
        [SerializeField] private Button _configButton;
        [SerializeField] private RawImage _outputImage; // Added for mirroring reference

        [Header("Config Panel (hidden by default)")]
        [SerializeField] private GameObject _configPanel;
        [SerializeField] private TMP_Dropdown _cameraDropdown;
        [SerializeField] private Toggle _mirrorToggle;

        private const string LogServiceName = "CameraConfigUI";
        private const string PrefCameraIndex = "SelectedCameraIndex";
        private const string PrefMirrorState = "IsMirrored";
        private bool _panelOpen = false;

        private void Start()
        {
            if (_configPanel != null) _configPanel.SetActive(false);
            if (_startCameraButton != null) _startCameraButton.onClick.AddListener(OnStartCamera);
            if (_configButton != null) _configButton.onClick.AddListener(TogglePanel);
            if (_cameraDropdown != null) _cameraDropdown.onValueChanged.AddListener(OnCameraSelected);
            if (_mirrorToggle != null) 
            {
                _mirrorToggle.onValueChanged.AddListener(OnMirrorToggle);
                bool isMirrored = PlayerPrefs.GetInt(PrefMirrorState, 0) == 1;
                _mirrorToggle.isOn = isMirrored;
                ApplyMirroring(isMirrored);
            }

            // Restore camera selection
            int savedIndex = PlayerPrefs.GetInt(PrefCameraIndex, 0);
            if (CameraFeedCtrl.Instance != null && savedIndex >= 0)
            {
                string[] deviceNames = CameraFeedCtrl.GetDeviceNames();
                if (savedIndex < deviceNames.Length)
                {
                    CameraFeedCtrl.Instance.SwitchCamera(deviceNames[savedIndex]);
                }
            }

            RefreshUI();
        }

        private void OnMirrorToggle(bool mirrored)
        {
            PlayerPrefs.SetInt(PrefMirrorState, mirrored ? 1 : 0);
            ApplyMirroring(mirrored);
            
            // Sync with hand tracker
            var tracker = FindFirstObjectByType<ARcadeRush.Hand.HandPositionTracker>();
            if (tracker != null) tracker.IsMirrored = mirrored;
        }

        private void ApplyMirroring(bool mirrored)
        {
            if (_outputImage != null)
            {
                Vector3 scale = _outputImage.transform.localScale;
                scale.x = mirrored ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
                _outputImage.transform.localScale = scale;
            }
        }

        private void OnStartCamera()
        {
            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.StartCamera();
                RefreshUI();

                var btnText = _startCameraButton.GetComponentInChildren<TMP_Text>();
                if (btnText != null)
                    btnText.text = CameraFeedCtrl.Instance.IsPlaying ? "Camara ON" : "Camara OFF";
            }
        }

        private void TogglePanel()
        {
            _panelOpen = !_panelOpen;
            if (_configPanel != null) _configPanel.SetActive(_panelOpen);
            if (_panelOpen) PopulateDropdown();
        }

        private void PopulateDropdown()
        {
            if (_cameraDropdown == null) return;
            
            string[] deviceNames = CameraFeedCtrl.GetDeviceNames();
            _cameraDropdown.ClearOptions();

            List<string> options = new List<string>();
            foreach (var device in deviceNames) options.Add(device);

            if (options.Count == 0) options.Add("No cameras found");
            
            _cameraDropdown.AddOptions(options);
            _cameraDropdown.SetValueWithoutNotify(PlayerPrefs.GetInt(PrefCameraIndex, 0));
        }

        private void OnCameraSelected(int index)
        {
            string[] deviceNames = CameraFeedCtrl.GetDeviceNames();
            if (index < 0 || index >= deviceNames.Length) return;

            PlayerPrefs.SetInt(PrefCameraIndex, index);
            string selectedDevice = deviceNames[index];
            ServiceLogger.Instance.LogInfo(LogServiceName, $"User selected camera: '{selectedDevice}'");

            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.SwitchCamera(selectedDevice);
            }

            RefreshUI();

            _panelOpen = false;
            if (_configPanel != null) _configPanel.SetActive(false);
        }

        private void RefreshUI()
        {
            if (_cameraNameLabel != null && CameraFeedCtrl.Instance != null)
            {
                string name = CameraFeedCtrl.Instance.ActiveDeviceName;
                _cameraNameLabel.text = string.IsNullOrEmpty(name) ? "No Camera" : name;
            }
        }
    }
}
