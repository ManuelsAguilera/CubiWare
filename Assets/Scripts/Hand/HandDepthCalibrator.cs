using UnityEngine;
using TMPro;

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

        private void Awake()
        {
            if (_projector == null)
                _projector = GetComponent<Hand3DProjector>();
        }

        private void Start()
        {
            LoadCalibration();
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

        private void SaveCalibration()
        {
            if (_projector == null) return;
            _projector.GetCalibrationScales(out float near, out float mid, out float far);
            PlayerPrefs.SetFloat(PrefsNear, near);
            PlayerPrefs.SetFloat(PrefsMid, mid);
            PlayerPrefs.SetFloat(PrefsFar, far);
            PlayerPrefs.Save();
        }

        private void LoadCalibration()
        {
            if (_projector == null) return;
            if (PlayerPrefs.HasKey(PrefsNear))
            {
                _projector.SetCalibrationScales(
                    PlayerPrefs.GetFloat(PrefsNear),
                    PlayerPrefs.GetFloat(PrefsMid),
                    PlayerPrefs.GetFloat(PrefsFar));
                UpdateStatus("Calibration loaded from PlayerPrefs");
            }
            else
            {
                UpdateStatus("No calibration in PlayerPrefs — using projector defaults.");
            }
        }

        private void ResetCalibration()
        {
            PlayerPrefs.DeleteKey(PrefsNear);
            PlayerPrefs.DeleteKey(PrefsMid);
            PlayerPrefs.DeleteKey(PrefsFar);
            if (_projector != null)
                _projector.ResetCalibrationScalesToDefaults();
            UpdateStatus("Calibration reset to defaults.");
        }

        private void UpdateStatus(string msg)
        {
            Debug.Log("[HandDepthCalibrator] " + msg);
            if (_statusText != null)
                _statusText.text = msg;
        }
    }
}
