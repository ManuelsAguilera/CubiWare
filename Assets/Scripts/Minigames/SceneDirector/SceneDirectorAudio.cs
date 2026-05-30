using System.Collections;
using UnityEngine;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Central audio manager for the Scene Director minigame.
    /// Singleton that exposes 15+ audio cue slots, all Inspector-assignable.
    ///
    /// Setup in Unity Editor:
    ///   1. Create an empty GameObject named "SceneDirectorAudio" in Director.unity.
    ///   2. Attach this script and add an AudioSource component for music + one for SFX.
    ///   3. Assign _sfxSource to the SFX AudioSource (for one-shot sounds).
    ///   4. Assign _musicSource to the Music AudioSource (for looping tracks).
    ///   5. Drag audio clips into their Inspector slots.
    ///
    /// All Play* methods are fire-and-forget. Volume and pitch are controllable per call.
    /// </summary>
    public class SceneDirectorAudio : MonoBehaviour
    {
        public static SceneDirectorAudio Instance { get; private set; }

        [Header("Audio Sources")]
        [Tooltip("One-shot sound effects (curtain, UI, applause, boos, etc.).")]
        [SerializeField] private AudioSource _sfxSource;
        [Tooltip("Looping music and ambient tracks (audience murmur, dramatic sting, BGM).")]
        [SerializeField] private AudioSource _musicSource;

        // ── Curtain ─────────────────────────────────────────────────────────────
        [Header("Curtain")]
        [Tooltip("Fabric rustle / curtain open sound.")]
        [SerializeField] private AudioClip _curtainOpen;
        [Tooltip("Fabric rustle / curtain close sound.")]
        [SerializeField] private AudioClip _curtainClose;

        // ── Countdown UI ────────────────────────────────────────────────────────
        [Header("Countdown UI")]
        [Tooltip("Tick sound for each countdown number (3, 2, 1).")]
        [SerializeField] private AudioClip _countdownTick;
        [Tooltip("Final buzzer/announcement when countdown reaches '¡ACCIÓN!'.")]
        [SerializeField] private AudioClip _countdownGo;
        [Tooltip("Ticking heartbeat in final seconds of per-element countdown.")]
        [SerializeField] private AudioClip _countdownUrgent;

        // ── Element Feedback ────────────────────────────────────────────────────
        [Header("Element Feedback")]
        [Tooltip("Pleasant chime when player holds correct emotion (element passed).")]
        [SerializeField] private AudioClip _correctChime;
        [Tooltip("Harsh buzzer when player fails an element.")]
        [SerializeField] private AudioClip _wrongBuzzer;

        // ── Audience ────────────────────────────────────────────────────────────
        [Header("Audience")]
        [Tooltip("Looping ambient crowd murmur. Intensity controlled via volume.")]
        [SerializeField] private AudioClip _audienceAmbient;
        [Tooltip("Audience applauds/cheers (positive reaction).")]
        [SerializeField] private AudioClip _applause;
        [Tooltip("Audience boos/jeers (negative reaction).")]
        [SerializeField] private AudioClip _boo;
        [Tooltip("Tomato splat SFX. Play alongside TomatoSplatController.Splat*().")]
        [SerializeField] private AudioClip _tomatoSplat;
        [Tooltip("Audience laughter (for witty LLM commentary moments).")]
        [SerializeField] private AudioClip _audienceLaugh;

        // ── Music ───────────────────────────────────────────────────────────────
        [Header("Music")]
        [Tooltip("Main BGM loop during gameplay.")]
        [SerializeField] private AudioClip _bgmGameplay;
        [Tooltip("Short dramatic sting on WIN.")]
        [SerializeField] private AudioClip _stingWin;
        [Tooltip("Short dramatic sting on LOSE.")]
        [SerializeField] private AudioClip _stingLose;

        // ── State ────────────────────────────────────────────────────────────────
        private Coroutine _ambientCoroutine;
        private float _ambientBaseVolume = 0.3f;
        private float _ambientTargetVolume = 0.3f;
        private const string LogServiceName = "SceneDirectorAudio";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Curtain API ─────────────────────────────────────────────────────────

        public void PlayCurtainOpen()
        {
            PlayOneShot(_curtainOpen);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain open SFX.");
        }

        public void PlayCurtainClose()
        {
            PlayOneShot(_curtainClose);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain close SFX.");
        }

        // ── Countdown UI API ────────────────────────────────────────────────────

        public void PlayCountdownTick()
        {
            PlayOneShot(_countdownTick);
        }

        public void PlayCountdownGo()
        {
            PlayOneShot(_countdownGo);
        }

        public void PlayCountdownUrgent()
        {
            PlayOneShot(_countdownUrgent, volume: 0.8f);
        }

        // ── Element Feedback API ────────────────────────────────────────────────

        public void PlayCorrectChime()
        {
            PlayOneShot(_correctChime, volume: 1f);
        }

        public void PlayWrongBuzzer()
        {
            PlayOneShot(_wrongBuzzer, volume: 0.9f);
        }

        // ── Audience API ────────────────────────────────────────────────────────

        /// <summary>Start looping audience ambient murmur.</summary>
        public void StartAmbient(float volume = 0.3f)
        {
            if (_audienceAmbient == null || _musicSource == null) return;

            _ambientBaseVolume = volume;
            _ambientTargetVolume = volume;
            _musicSource.clip = _audienceAmbient;
            _musicSource.loop = true;
            _musicSource.volume = volume;
            _musicSource.Play();
            _ambientCoroutine = StartCoroutine(SmoothAmbientVolume());
            ServiceLogger.Instance.LogInfo(LogServiceName, "Audience ambient started.");
        }

        /// <summary>Stop audience ambient with optional fade.</summary>
        public void StopAmbient(float fadeOutDuration = 0.5f)
        {
            if (_musicSource == null || _musicSource.clip != _audienceAmbient) return;

            if (fadeOutDuration <= 0f)
            {
                _musicSource.Stop();
                return;
            }

            if (_ambientCoroutine != null)
                StopCoroutine(_ambientCoroutine);
            _ambientCoroutine = StartCoroutine(FadeOutAmbient(fadeOutDuration));
        }

        /// <summary>Adjusts ambient volume over time (e.g., louder when crowd excited).</summary>
        public void SetAmbientIntensity(float normalizedIntensity)
        {
            _ambientTargetVolume = Mathf.Clamp01(normalizedIntensity) * _ambientBaseVolume;
        }

        public void PlayApplause(float volume = 1f)
        {
            PlayOneShot(_applause, volume);
        }

        public void PlayBoo(float volume = 1f)
        {
            PlayOneShot(_boo, volume);
        }

        public void PlayTomatoSplat(float volume = 0.7f)
        {
            PlayOneShot(_tomatoSplat, volume, pitchVariation: 0.15f);
        }

        public void PlayAudienceLaugh(float volume = 0.8f)
        {
            PlayOneShot(_audienceLaugh, volume);
        }

        // ── Music API ───────────────────────────────────────────────────────────

        public void StartBGM()
        {
            if (_bgmGameplay == null || _musicSource == null) return;

            // Stop ambient if it was playing
            StopAmbient(0f);

            _musicSource.clip = _bgmGameplay;
            _musicSource.loop = true;
            _musicSource.volume = 0.6f;
            _musicSource.Play();
            ServiceLogger.Instance.LogInfo(LogServiceName, "BGM started.");
        }

        public void StopBGM(float fadeOutDuration = 1f)
        {
            if (_musicSource == null) return;
            if (fadeOutDuration <= 0f)
            {
                _musicSource.Stop();
                return;
            }

            if (_ambientCoroutine != null)
                StopCoroutine(_ambientCoroutine);
            _ambientCoroutine = StartCoroutine(FadeOutMusic(fadeOutDuration));
        }

        public void PlayStingWin()
        {
            StopBGM(0.3f);
            PlayOneShot(_stingWin, volume: 1f);
        }

        public void PlayStingLose()
        {
            StopBGM(0.3f);
            PlayOneShot(_stingLose, volume: 1f);
        }

        // ── Internal ────────────────────────────────────────────────────────────

        private void PlayOneShot(AudioClip clip, float volume = 1f, float pitchVariation = 0f)
        {
            if (clip == null || _sfxSource == null) return;

            if (pitchVariation > 0f)
            {
                float originalPitch = _sfxSource.pitch;
                _sfxSource.pitch = Random.Range(1f - pitchVariation, 1f + pitchVariation);
                _sfxSource.PlayOneShot(clip, volume);
                _sfxSource.pitch = originalPitch;
            }
            else
            {
                _sfxSource.PlayOneShot(clip, volume);
            }
        }

        private IEnumerator SmoothAmbientVolume()
        {
            while (_musicSource != null && _musicSource.isPlaying)
            {
                float currentVol = _musicSource.volume;
                float newVol = Mathf.Lerp(currentVol, _ambientTargetVolume, Time.deltaTime * 3f);
                _musicSource.volume = newVol;
                yield return null;
            }
        }

        private IEnumerator FadeOutAmbient(float duration)
        {
            if (_musicSource == null) yield break;
            float startVol = _musicSource.volume;
            float elapsed = 0f;
            while (elapsed < duration && _musicSource != null)
            {
                elapsed += Time.deltaTime;
                _musicSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
                yield return null;
            }
            if (_musicSource != null) _musicSource.Stop();
        }

        private IEnumerator FadeOutMusic(float duration)
        {
            if (_musicSource == null) yield break;
            float startVol = _musicSource.volume;
            float elapsed = 0f;
            while (elapsed < duration && _musicSource != null)
            {
                elapsed += Time.deltaTime;
                _musicSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
                yield return null;
            }
            if (_musicSource != null) _musicSource.Stop();
        }
    }
}