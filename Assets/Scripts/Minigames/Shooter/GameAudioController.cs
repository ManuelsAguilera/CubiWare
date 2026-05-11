using System.Collections;
using UnityEngine;
using ARcadeRush.Core;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Master music controller for the Shooter minigame.
    /// Designed as a prefab root with 4 child AudioSources (Low, Medium, High, Pause).
    ///
    /// All 3 intensity tracks play simultaneously from the start.
    /// Only one is audible at a time via volume cross-fading.
    /// On pause, the pause soundtrack fades in.
    /// On resume, the controller waits for the pause track's current loop to end
    /// before cross-fading back to the appropriate intensity track.
    ///
    /// SFX are handled independently by GunController and Target (each has its own AudioSource).
    /// All time-dependent operations use Time.unscaledDeltaTime so transitions
    /// work correctly while Time.timeScale == 0 (GameManager pause).
    /// </summary>
    public class GameAudioController : MonoBehaviour
    {
        [Header("Audio Source References (children of this prefab)")]
        [SerializeField] private AudioSource _lowSource;
        [SerializeField] private AudioSource _mediumSource;
        [SerializeField] private AudioSource _highSource;
        [SerializeField] private AudioSource _pauseSource;

        [Header("Volume Settings")]
        [SerializeField] private float _musicVolume = 0.8f;

        [Header("Fade Settings")]
        [SerializeField] private float _fadeDuration = 0.5f;
        [SerializeField] private float _pauseFadeDuration = 0.3f;

        // State
        private int _currentIntensity = 0; // 0 = none, 1 = Low, 2 = Medium, 3 = High
        private bool _isPaused = false;
        private bool _isTransitioning = false;

        /// <summary>Human-readable label for the currently active music track.</summary>
        public string CurrentIntensityLabel
        {
            get
            {
                if (_isPaused) return "Paused";
                return _currentIntensity switch
                {
                    1 => "Low",
                    2 => "Medium",
                    3 => "High",
                    _ => "None"
                };
            }
        }

        // Threshold near the loop end to trigger transition (seconds)
        private const float LoopEndThreshold = 0.05f;

        // ------ Public API ------

        /// <summary>Set game intensity level. 1 = Low, 2 = Medium, 3 = High.</summary>
        public void SetIntensity(int level)
        {
            if (level < 1 || level > 3)
            {
                Debug.LogWarning($"[GameAudioController] Invalid intensity level: {level}. Expected 1-3.");
                return;
            }

            if (level == _currentIntensity)
                return;

            if (_isTransitioning)
                return;

            AudioSource fadeOut = GetIntensitySource(_currentIntensity);
            AudioSource fadeIn = GetIntensitySource(level);

            if (fadeIn == null)
            {
                Debug.LogWarning($"[GameAudioController] No AudioSource for intensity level {level}.");
                return;
            }

            _currentIntensity = level;
            StartCoroutine(CoCrossFade(fadeOut, fadeIn));
        }

        /// <summary>
        /// Silently prepare all sources for playback without audible output.
        /// All 3 intensity sources keep playing (so they're warmed up for cross-fade),
        /// but at zero volume. Used when the game starts in a paused state.
        /// </summary>
        public void StartSilent()
        {
            if (_isTransitioning) return;

            SetSourceVolume(_lowSource, 0f);
            SetSourceVolume(_mediumSource, 0f);
            SetSourceVolume(_highSource, 0f);
            SetSourceVolume(_pauseSource, 0f);
            _currentIntensity = 0;
            _isPaused = false;
        }

        /// <summary>Transition from the current intensity track to the pause soundtrack.</summary>
        public void PauseMusic()
        {
            if (_isPaused) return;
            if (_isTransitioning) return;

            _isPaused = true;
            StartCoroutine(CoEnterPause());
        }

        /// <summary>
        /// Exit pause mode. The controller waits for the current pause track loop to finish
        /// naturally, then cross-fades back to the appropriate intensity track.
        /// </summary>
        public void ResumeMusic()
        {
            if (!_isPaused) return;
            if (_isTransitioning) return;

            StartCoroutine(CoWaitForPauseLoopEnd());
        }

        /// <summary>Stop all audio sources immediately (e.g., on game end).</summary>
        public void StopAllMusic()
        {
            StopAllCoroutines();
            _isTransitioning = false;

            SetSourceVolume(_lowSource, 0f);
            SetSourceVolume(_mediumSource, 0f);
            SetSourceVolume(_highSource, 0f);
            SetSourceVolume(_pauseSource, 0f);

            if (_lowSource != null) _lowSource.Stop();
            if (_mediumSource != null) _mediumSource.Stop();
            if (_highSource != null) _highSource.Stop();
            if (_pauseSource != null) _pauseSource.Stop();

            _isPaused = false;
            _currentIntensity = 0;
        }

        // ------ Unity Lifecycle ------

        private void Awake()
        {
            InitializeSources();
            StartSilent(); // All volumes at 0 — silent until ShooterGame calls PauseMusic()
        }

        private void Start()
        {
            // Self-subscribe to GameManager pause/resume events so this controller
            // handles music transitions independently of ShooterGame.
            GameManager gm = FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                gm.OnGamePaused += PauseMusic;
                gm.OnGameResumed += ResumeMusic;
            }
        }

        private void OnDestroy()
        {
            GameManager gm = FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                gm.OnGamePaused -= PauseMusic;
                gm.OnGameResumed -= ResumeMusic;
            }
        }

        // ------ Initialization ------

        /// <summary>
        /// Configure each source and start all music tracks playing.
        /// Only the first intensity track (Low) is audible at startup.
        /// </summary>
        private void InitializeSources()
        {
            // Configure all music sources
            ConfigureMusicSource(_lowSource);
            ConfigureMusicSource(_mediumSource);
            ConfigureMusicSource(_highSource);
            ConfigureMusicSource(_pauseSource);

            // Start all music tracks playing
            if (_lowSource != null) _lowSource.Play();
            if (_mediumSource != null) _mediumSource.Play();
            if (_highSource != null) _highSource.Play();

            // Pause source should not play yet
            if (_pauseSource != null) _pauseSource.Stop();

            // Set initial volumes: Low audible, others silent
            SetSourceVolume(_lowSource, _musicVolume);
            SetSourceVolume(_mediumSource, 0f);
            SetSourceVolume(_highSource, 0f);
            SetSourceVolume(_pauseSource, 0f);

            _currentIntensity = 1; // Start at Low
        }

        private static void ConfigureMusicSource(AudioSource source)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = true;
            source.ignoreListenerPause = true;
        }

        // ------ Cross-Fade ------

        private IEnumerator CoCrossFade(AudioSource fadeOut, AudioSource fadeIn)
        {
            _isTransitioning = true;

            float startVolumeOut = fadeOut != null ? fadeOut.volume : 0f;
            float startVolumeIn = fadeIn != null ? fadeIn.volume : 0f;
            float elapsed = 0f;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                if (fadeOut != null)
                    fadeOut.volume = Mathf.Lerp(startVolumeOut, 0f, smoothT);

                if (fadeIn != null)
                    fadeIn.volume = Mathf.Lerp(startVolumeIn, _musicVolume, smoothT);

                yield return null;
            }

            // Ensure final state
            if (fadeOut != null) fadeOut.volume = 0f;
            if (fadeIn != null) fadeIn.volume = _musicVolume;

            _isTransitioning = false;
        }

        // ------ Pause Transition ------

        private IEnumerator CoEnterPause()
        {
            _isTransitioning = true;

            AudioSource currentSource = GetIntensitySource(_currentIntensity);
            float startVolume = currentSource != null ? currentSource.volume : 0f;
            float elapsed = 0f;

            // Rapidly fade out current music
            while (elapsed < _pauseFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _pauseFadeDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                if (currentSource != null)
                    currentSource.volume = Mathf.Lerp(startVolume, 0f, smoothT);

                yield return null;
            }

            if (currentSource != null) currentSource.volume = 0f;

            // Start pause track from beginning
            if (_pauseSource != null && _pauseSource.clip != null)
            {
                _pauseSource.time = 0f;
                _pauseSource.volume = _musicVolume;
                _pauseSource.Play();
            }

            _isTransitioning = false;
        }

        // ------ Resume: Wait for Loop End ------

        private IEnumerator CoWaitForPauseLoopEnd()
        {
            _isTransitioning = true;

            float clipLength = _pauseSource != null && _pauseSource.clip != null
                ? _pauseSource.clip.length
                : 0f;

            // If there's no pause clip or it's not playing, transition immediately
            if (clipLength <= 0f || _pauseSource == null || !_pauseSource.isPlaying)
            {
                yield return CoCrossFade(null, GetIntensitySource(_currentIntensity));
                _isPaused = false;
                yield break;
            }

            // Wait until the pause track reaches near the end of its current loop
            while (_pauseSource.isPlaying)
            {
                float remaining = clipLength - _pauseSource.time;
                if (remaining <= LoopEndThreshold)
                    break;
                yield return null; // Next unscaled frame
            }

            // Now cross-fade: pause out, intensity in
            AudioSource intensitySource = GetIntensitySource(_currentIntensity);
            float fadeElapsed = 0f;

            while (fadeElapsed < _fadeDuration)
            {
                fadeElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeElapsed / _fadeDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                if (_pauseSource != null)
                    _pauseSource.volume = Mathf.Lerp(_musicVolume, 0f, smoothT);

                if (intensitySource != null)
                    intensitySource.volume = Mathf.Lerp(0f, _musicVolume, smoothT);

                yield return null;
            }

            // Ensure final state
            if (_pauseSource != null)
            {
                _pauseSource.volume = 0f;
                _pauseSource.Stop();
            }

            if (intensitySource != null)
                intensitySource.volume = _musicVolume;

            _isPaused = false;
            _isTransitioning = false;
        }

        // ------ Helpers ------

        private AudioSource GetIntensitySource(int level)
        {
            return level switch
            {
                1 => _lowSource,
                2 => _mediumSource,
                3 => _highSource,
                _ => null
            };
        }

        private static void SetSourceVolume(AudioSource source, float volume)
        {
            if (source != null)
                source.volume = volume;
        }
    }
}
