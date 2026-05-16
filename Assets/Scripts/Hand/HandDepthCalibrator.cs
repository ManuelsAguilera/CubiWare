using UnityEngine;
using TMPro;
using System;
using System.Threading.Tasks;
using ARcadeRush.Core;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;
using CubiWare.Core.Services;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Runtime depth-scale calibration ported from Assets/Scenes/HandTracker/HandDepthCalibrator.cs.
    /// Keys: 1 = Near, 2 = Mid, 3 = Far, R = reset defaults and clear PlayerPrefs.
    /// </summary>
    public class HandDepthCalibrator : MonoBehaviour
    {
        private const string PrefsNear = "Hand_NearScale";
        private const string PrefsMid = "Hand_MidScale";
        private const string PrefsFar = "Hand_FarScale";

        [SerializeField] private Hand3DProjector _projector;
        [SerializeField] private TMP_Text _statusText;

        private IDataStore _dataStore;

        private void Awake()
        {
            if (_projector == null)
                _projector = GetComponent<Hand3DProjector>();

            // Resolve data store — prefer GameManager, fall back to direct instantiation
            if (GameManager.Instance != null && GameManager.Instance.DataStore != null)
                _dataStore = GameManager.Instance.DataStore;
            else
                _dataStore = new PlayerPrefsDataStore();
        }

        private async void Start()
        {
            await LoadCalibrationAsync();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) CalibrateNear();
            if (Input.GetKeyDown(KeyCode.Alpha2)) CalibrateMid();
            if (Input.GetKeyDown(KeyCode.Alpha3)) CalibrateFar();
            if (Input.GetKeyDown(KeyCode.R)) ResetCalibration();
        }

        public void CalibrateNear()
        {
            if (_projector == null) return;
            float scale = _projector.CurrentHandScale;
            if (scale <= 0f)
            {
                UpdateStatus("No hand detected for Near calibration!");
                return;
            }
            _projector.GetCalibrationScales(out _, out float mid, out float far);
            _projector.SetCalibrationScales(scale, mid, far);
            SaveCalibration();
            UpdateStatus($"Calibrated NEAR ({scale:F3})");
        }

        public void CalibrateMid()
        {
            if (_projector == null) return;
            float scale = _projector.CurrentHandScale;
            if (scale <= 0f)
            {
                UpdateStatus("No hand detected for Mid calibration!");
                return;
            }
            _projector.GetCalibrationScales(out float near, out _, out float far);
            _projector.SetCalibrationScales(near, scale, far);
            SaveCalibration();
            UpdateStatus($"Calibrated MID ({scale:F3})");
        }

        public void CalibrateFar()
        {
            if (_projector == null) return;
            float scale = _projector.CurrentHandScale;
            if (scale <= 0f)
            {
                UpdateStatus("No hand detected for Far calibration!");
                return;
            }
            _projector.GetCalibrationScales(out float near, out float mid, out _);
            _projector.SetCalibrationScales(near, mid, scale);
            SaveCalibration();
            UpdateStatus($"Calibrated FAR ({scale:F3})");
        }

        private async void SaveCalibration()
        {
            if (_projector == null) return;
            _projector.GetCalibrationScales(out float near, out float mid, out float far);

            try
            {
                await _dataStore.SaveAsync(PrefsNear, near);
                await _dataStore.SaveAsync(PrefsMid, mid);
                await _dataStore.SaveAsync(PrefsFar, far);
            }
            catch (Exception ex)
            {
                ServiceLogger.Instance.LogWarning("HandDepthCalibrator", $"DataStore save failed, falling back to PlayerPrefs: {ex.Message}");
                // Fallback to raw PlayerPrefs
                PlayerPrefs.SetFloat(PrefsNear, near);
                PlayerPrefs.SetFloat(PrefsMid, mid);
                PlayerPrefs.SetFloat(PrefsFar, far);
                PlayerPrefs.Save();
            }
        }

        private async Task LoadCalibrationAsync()
        {
            if (_projector == null) return;

            try
            {
                bool exists = await _dataStore.ExistsAsync(PrefsNear);
                if (exists)
                {
                    float near = await _dataStore.LoadAsync<float>(PrefsNear);
                    float mid = await _dataStore.LoadAsync<float>(PrefsMid);
                    float far = await _dataStore.LoadAsync<float>(PrefsFar);
                    _projector.SetCalibrationScales(near, mid, far);
                    UpdateStatus("Calibration loaded from data store");
                }
                else
                {
                    UpdateStatus("No calibration found — using projector defaults.");
                }
            }
            catch (Exception ex)
            {
                ServiceLogger.Instance.LogWarning("HandDepthCalibrator", $"DataStore load failed, falling back to PlayerPrefs: {ex.Message}");
                // Fallback to raw PlayerPrefs
                if (PlayerPrefs.HasKey(PrefsNear))
                {
                    _projector.SetCalibrationScales(
                        PlayerPrefs.GetFloat(PrefsNear),
                        PlayerPrefs.GetFloat(PrefsMid),
                        PlayerPrefs.GetFloat(PrefsFar));
                    UpdateStatus("Calibration loaded from PlayerPrefs (fallback)");
                }
                else
                {
                    UpdateStatus("No calibration found — using projector defaults.");
                }
            }
        }

        private async void ResetCalibration()
        {
            try
            {
                await _dataStore.DeleteAsync(PrefsNear);
                await _dataStore.DeleteAsync(PrefsMid);
                await _dataStore.DeleteAsync(PrefsFar);
            }
            catch (Exception ex)
            {
                ServiceLogger.Instance.LogWarning("HandDepthCalibrator", $"DataStore delete failed, falling back to PlayerPrefs: {ex.Message}");
                // Fallback to raw PlayerPrefs
                PlayerPrefs.DeleteKey(PrefsNear);
                PlayerPrefs.DeleteKey(PrefsMid);
                PlayerPrefs.DeleteKey(PrefsFar);
            }

            if (_projector != null)
                _projector.ResetCalibrationScalesToDefaults();
            UpdateStatus("Calibration reset to defaults.");
        }

        private void UpdateStatus(string msg)
        {
            ServiceLogger.Instance.LogInfo("HandDepthCalibrator", msg);
            if (_statusText != null)
                _statusText.text = msg;
        }
    }
}
