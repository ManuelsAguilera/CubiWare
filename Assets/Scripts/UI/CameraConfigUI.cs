using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Core;

namespace ARcadeRush.UI
{
    /// <summary>
    /// Camera configuration UI. Shows current camera name, a gear button
    /// that opens a config panel with a dropdown to switch cameras.
    /// 
    /// Setup in Unity:
    /// 1. Create a Canvas child (or use existing one).
    /// 2. Add a TextMeshPro label for the camera name.
    /// 3. Add a Button (gear icon / "⚙") to toggle the config panel.
    /// 4. Create a Panel with a TMP_Dropdown inside it.
    /// 5. Drag all references into this script's Inspector slots.
    /// </summary>
    public class CameraConfigUI : MonoBehaviour
    {
        [Header("Always Visible")]
        [SerializeField] private TMP_Text _cameraNameLabel;
        [SerializeField] private Button _startCameraButton;
        [SerializeField] private Button _configButton;

        [Header("Config Panel (hidden by default)")]
        [SerializeField] private GameObject _configPanel;
        [SerializeField] private TMP_Dropdown _cameraDropdown;

        private bool _panelOpen = false;

        private void Start()
        {
            // Start with panel closed
            if (_configPanel != null)
                _configPanel.SetActive(false);

            // Wire up start camera button
            if (_startCameraButton != null)
                _startCameraButton.onClick.AddListener(OnStartCamera);

            // Wire up the gear button
            if (_configButton != null)
                _configButton.onClick.AddListener(TogglePanel);

            // Wire up dropdown change
            if (_cameraDropdown != null)
                _cameraDropdown.onValueChanged.AddListener(OnCameraSelected);

            // Initial refresh
            RefreshUI();
        }

        private void OnStartCamera()
        {
            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.StartCamera();
                RefreshUI();

                // Update button text to show it's running
                var btnText = _startCameraButton.GetComponentInChildren<TMP_Text>();
                if (btnText != null)
                    btnText.text = CameraFeedCtrl.Instance.IsPlaying ? "Camara ON" : "Camara OFF";
            }
        }

        private void TogglePanel()
        {
            _panelOpen = !_panelOpen;

            if (_configPanel != null)
                _configPanel.SetActive(_panelOpen);

            // Refresh the dropdown every time we open
            if (_panelOpen)
                PopulateDropdown();
        }

        private void PopulateDropdown()
        {
            if (_cameraDropdown == null) return;

            string[] deviceNames = CameraFeedCtrl.GetDeviceNames();
            string currentDevice = CameraFeedCtrl.Instance != null
                ? CameraFeedCtrl.Instance.ActiveDeviceName
                : "";

            _cameraDropdown.ClearOptions();

            List<string> options = new List<string>();
            int currentIndex = 0;

            for (int i = 0; i < deviceNames.Length; i++)
            {
                options.Add(deviceNames[i]);
                if (deviceNames[i] == currentDevice)
                    currentIndex = i;
            }

            if (options.Count == 0)
            {
                options.Add("No cameras found");
            }

            _cameraDropdown.AddOptions(options);
            _cameraDropdown.SetValueWithoutNotify(currentIndex);
        }

        private void OnCameraSelected(int index)
        {
            string[] deviceNames = CameraFeedCtrl.GetDeviceNames();
            if (index < 0 || index >= deviceNames.Length) return;

            string selectedDevice = deviceNames[index];
            Debug.Log($"[CamConfig] User selected camera: '{selectedDevice}'");

            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.SwitchCamera(selectedDevice);
            }

            RefreshUI();

            // Auto-close panel after selection
            _panelOpen = false;
            if (_configPanel != null)
                _configPanel.SetActive(false);
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
