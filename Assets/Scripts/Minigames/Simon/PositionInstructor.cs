using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Shows visual arrow indicators and plays audio cues to guide the player's
    /// hand to a target zone. Communicates with HandZoneClassifier to detect arrival.
    ///
    /// UI Elements expected in scene (assign via Inspector):
    ///   - Arrow_UpLeft, Arrow_UpRight, Arrow_DownLeft, Arrow_DownRight, Arrow_Center
    ///     (Image or GameObject with arrow sprite pointing in the correct direction)
    ///   - ZoneLabel (TMP_Text) — displays zone name in Spanish
    ///   - ZoneHighlight (Image) — semi-transparent overlay on target quadrant
    /// </summary>
    public class PositionInstructor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandZoneClassifier _zoneClassifier;

        [Header("Arrow Indicators")]
        [SerializeField] private GameObject _arrowUpLeft;
        [SerializeField] private GameObject _arrowUpRight;
        [SerializeField] private GameObject _arrowDownLeft;
        [SerializeField] private GameObject _arrowDownRight;
        [SerializeField] private GameObject _arrowCenter;

        [Header("UI")]
        [SerializeField] private TMP_Text _zoneLabel;
        [SerializeField] private Image _zoneHighlight;
        [SerializeField] private Color _activeColor = new Color(0f, 1f, 0f, 0.3f);  // green tint
        [SerializeField] private Color _inactiveColor = new Color(1f, 1f, 1f, 0f); // transparent

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _zoneReachedClip;   // Plays when player reaches target
        [SerializeField] private AudioClip _instructionClip;    // Plays when new instruction shown

        [Header("Timing")]
        [SerializeField] private float _arrivalConfirmDuration = 0.5f; // Must stay in zone this long

        /// <summary>Fires when the player's hand is confirmed in the target zone.</summary>
        public event Action OnPlayerInPosition;

        /// <summary>Fires when the player leaves the target zone after having been confirmed.</summary>
        public event Action OnPlayerLeftPosition;

        private HandZone _targetZone = HandZone.None;
        private bool _isInstructing;
        private bool _wasInPosition;
        private Coroutine _arrivalCo;

        private void Start()
        {
            // Resolve classifier if not assigned
            if (_zoneClassifier == null)
                _zoneClassifier = FindAnyObjectByType<HandZoneClassifier>();
        }

        /// <summary>
        /// Shows the arrow for the target zone and begins monitoring for arrival.
        /// </summary>
        public void InstructZone(HandZone targetZone)
        {
            _targetZone = targetZone;
            _isInstructing = true;
            _wasInPosition = false;

            // Deactivate all arrows, activate only the target
            HideAllArrows();
            ShowArrow(targetZone);

            // Update label
            if (_zoneLabel != null)
            {
                _zoneLabel.text = GetZoneDisplayName(targetZone);
                _zoneLabel.gameObject.SetActive(true);
            }

            // Update highlight
            if (_zoneHighlight != null)
                _zoneHighlight.color = _activeColor;

            // Play instruction audio (optional)
            if (_audioSource != null && _instructionClip != null)
                _audioSource.PlayOneShot(_instructionClip);

            // Subscribe to zone changes from the shared HandZoneClassifier
            if (_zoneClassifier != null)
                _zoneClassifier.OnZoneChanged += HandleZoneChanged;

            // Check if already in position
            CheckCurrentPosition();
        }

        /// <summary>
        /// Hides all indicators and stops monitoring.
        /// </summary>
        public void ClearInstruction()
        {
            _isInstructing = false;
            _targetZone = HandZone.None;
            _wasInPosition = false;

            HideAllArrows();

            if (_zoneLabel != null)
                _zoneLabel.gameObject.SetActive(false);

            if (_zoneHighlight != null)
                _zoneHighlight.color = _inactiveColor;

            if (_zoneClassifier != null)
                _zoneClassifier.OnZoneChanged -= HandleZoneChanged;

            if (_arrivalCo != null)
            {
                StopCoroutine(_arrivalCo);
                _arrivalCo = null;
            }
        }

        private void HandleZoneChanged(HandZone oldZone, HandZone newZone)
        {
            if (!_isInstructing) return;
            CheckCurrentPosition();
        }

        private void CheckCurrentPosition()
        {
            if (_zoneClassifier == null) return;

            bool isInTarget = _zoneClassifier.IsInZone(_targetZone);

            if (isInTarget && !_wasInPosition)
            {
                // Player just entered target zone — start arrival confirmation timer
                if (_arrivalCo == null)
                    _arrivalCo = StartCoroutine(CoConfirmArrival());
            }
            else if (!isInTarget && _wasInPosition)
            {
                // Player left the target zone
                _wasInPosition = false;
                OnPlayerLeftPosition?.Invoke();

                if (_arrivalCo != null)
                {
                    StopCoroutine(_arrivalCo);
                    _arrivalCo = null;
                }
            }
        }

        private IEnumerator CoConfirmArrival()
        {
            yield return new WaitForSeconds(_arrivalConfirmDuration);

            // Re-check position after delay
            if (_zoneClassifier != null && _zoneClassifier.IsInZone(_targetZone))
            {
                // Play arrival sound (optional)
                if (_audioSource != null && _zoneReachedClip != null)
                    _audioSource.PlayOneShot(_zoneReachedClip);

                _wasInPosition = true;
                _isInstructing = false;
                OnPlayerInPosition?.Invoke();
            }

            _arrivalCo = null;
        }

        private void HideAllArrows()
        {
            if (_arrowUpLeft != null) _arrowUpLeft.SetActive(false);
            if (_arrowUpRight != null) _arrowUpRight.SetActive(false);
            if (_arrowDownLeft != null) _arrowDownLeft.SetActive(false);
            if (_arrowDownRight != null) _arrowDownRight.SetActive(false);
            if (_arrowCenter != null) _arrowCenter.SetActive(false);
        }

        private void ShowArrow(HandZone zone)
        {
            switch (zone)
            {
                case HandZone.UpLeft:    if (_arrowUpLeft != null) _arrowUpLeft.SetActive(true); break;
                case HandZone.UpRight:   if (_arrowUpRight != null) _arrowUpRight.SetActive(true); break;
                case HandZone.DownLeft:  if (_arrowDownLeft != null) _arrowDownLeft.SetActive(true); break;
                case HandZone.DownRight: if (_arrowDownRight != null) _arrowDownRight.SetActive(true); break;
                case HandZone.Center:    if (_arrowCenter != null) _arrowCenter.SetActive(true); break;
            }
        }

        /// <summary>Returns a Spanish display name for the zone.</summary>
        public static string GetZoneDisplayName(HandZone zone)
        {
            return zone switch
            {
                HandZone.UpLeft    => "Arriba Izquierda",
                HandZone.UpRight   => "Arriba Derecha",
                HandZone.DownLeft  => "Abajo Izquierda",
                HandZone.DownRight => "Abajo Derecha",
                HandZone.Center    => "Centro",
                _                  => ""
            };
        }

        private void OnDestroy()
        {
            ClearInstruction();
        }
    }
}
