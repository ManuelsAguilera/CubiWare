using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using ARcadeRush.Core;
using CubiWare.Core.Interfaces;

/// <summary>
/// Fruit Ninja game manager — orchestrates spawning, scoring, lives, and game-over flow.
///
/// Camera integration (v2):
///   - Implements IMiniGame so MiniGameDependencies (incl. CameraFeedCtrl) are injected
///     by SceneLoader, following the same pattern as EmotionTestGame and SimonGame.
///   - SetupCamera() wires the cameraOverlay RawImage via CameraFeedCtrl.SetOutputImage(),
///     with fallback to the legacy CameraFeedCtrl.Instance singleton for backward
///     compatibility when the scene is run standalone (not through SceneLoader).
///   - Update() monitors camera health and restarts if it stops unexpectedly.
///   - OnDestroy() cleans up camera references.
/// </summary>
public class GameManagerFruit : MonoBehaviour, IMiniGame
{
    [Header("UI Juego")]
    public TMP_Text scoreText;
    public RawImage cameraOverlay;
    public CanvasGroup cameraUIAlpha;

    [Header("UI Game Over")]
    public GameObject panelGameOver;
    public TMP_Text gameOverScoreText;

    // ── IMiniGame ──────────────────────────────────────────────────────────

    public int SceneIndex => SceneManager.GetActiveScene().buildIndex;

    // ── Internal State ─────────────────────────────────────────────────────

    private Blade blade;
    private Spawner spawner;
    private int score;

    /// <summary>Injected by SceneLoader via IMiniGame.OnStart().</summary>
    private MiniGameDependencies _deps;

    /// <summary>True after OnStart() has been called.</summary>
    private bool _isPlaying;

    /// <summary>True after OnStart() has initialized the camera display.</summary>
    private bool _cameraInitialized;

    // ── Unity Lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        blade = FindAnyObjectByType<Blade>();
        spawner = FindAnyObjectByType<Spawner>();
    }

    private void Start()
    {
        if (panelGameOver != null)
            panelGameOver.SetActive(false);

        // ── Camera initialization ──────────────────────────────────────
        // If IMiniGame.OnStart() was already called (SceneLoader path),
        // SetupCamera() ran there and _cameraInitialized is true.
        // If running standalone, OnStart() hasn't been called — fall back
        // to the legacy singleton path.
        if (!_cameraInitialized)
        {
            SetupCameraFallback();
        }

        NewGame();
    }

    private void Update()
    {
        // ── Per-frame camera health monitoring ─────────────────────────
        // Same pattern as EmotionTestGame.Update(): if the camera stops
        // unexpectedly, restart it so the player always sees the feed.
        if (!_isPlaying) return;

        CameraFeedCtrl cam = GetActiveCamera();
        if (cam == null) return;

        if (cameraOverlay != null && !cam.IsPlaying)
        {
            Debug.LogWarning("[GameManagerFruit] Camera stopped unexpectedly. Restarting...");
            cam.StartCamera();
        }
    }

    private void OnDestroy()
    {
        // ── Cleanup camera display reference ───────────────────────────
        // Clear the RawImage texture so it doesn't hold a dangling
        // reference after this GameObject is destroyed.
        if (cameraOverlay != null)
        {
            cameraOverlay.texture = null;
        }

        _isPlaying = false;
    }

    // ── IMiniGame Implementation ───────────────────────────────────────────

    /// <summary>
    /// Called by SceneLoader after the FruitNinja scene finishes loading.
    /// Follows the same pattern as EmotionTestGame.OnStart() and
    /// SimonGame.OnStart().
    /// </summary>
    public void OnStart(MiniGameDependencies deps)
    {
        _deps = deps;
        _isPlaying = true;

        Debug.Log("[GameManagerFruit] OnStart called. Setting up camera feed...");

        // Register with GameManager if available
        _deps?.GameManager?.RegisterGame(this);

        // Initialize camera display using the injected CameraFeedCtrl
        SetupCamera();
    }

    /// <summary>
    /// Called when the game ends. Stops camera monitoring.
    /// </summary>
    public void OnEnd()
    {
        _isPlaying = false;

        Debug.Log("[GameManagerFruit] OnEnd called.");

        // Report session data if GameManager is available
        if (_deps?.GameManager != null)
        {
            var sessionData = new MinigameSessionData
            {
                MinigameName = "FruitNinja",
                Score = score,
                Completed = true,
                StartTime = System.DateTime.Now,
                EndTime = System.DateTime.Now,
                DurationSeconds = 0f,
                CustomStats = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "Score", score }
                }
            };

            _deps.GameManager.CollectMinigameData(sessionData);
            _deps.GameManager.EndGame();
        }

        // Navigate back to main menu
        UnityEngine.PlayerPrefs.SetInt("ReturnToGameSelector", 1);
        SceneLoader.Instance?.LoadSceneAsync("MainMenu");
    }

    // ── Camera Setup ───────────────────────────────────────────────────────

    /// <summary>
    /// Primary camera initialization path. Uses the CameraFeedCtrl injected
    /// through MiniGameDependencies (SceneLoader → IMiniGame.OnStart).
    ///
    /// This is the exact same pattern as EmotionTestGame.SetupCamera():
    ///   1. Validate dependencies and display target
    ///   2. Wire the RawImage via SetOutputImage()
    ///   3. Start the camera if not already playing, or reassign texture
    ///      if already active
    /// </summary>
    private void SetupCamera()
    {
        if (_cameraInitialized) return;
        _cameraInitialized = true;

        CameraFeedCtrl cam = _deps?.Camera;
        if (cam == null)
        {
            Debug.LogWarning("[GameManagerFruit] Camera not available in dependencies! " +
                "Falling back to singleton.");
            SetupCameraFallback();
            return;
        }

        if (cameraOverlay == null)
        {
            Debug.LogWarning("[GameManagerFruit] Camera display RawImage (cameraOverlay) not assigned!");
            return;
        }

        // Wire the RawImage to the camera feed — CameraFeedCtrl handles
        // texture assignment and UV rect (mirroring for selfie cameras)
        cam.SetOutputImage(cameraOverlay);

        if (!cam.IsPlaying)
        {
            // Camera hasn't been started yet — start it now.
            // SetOutputImage() handles both texture assignment and
            // uvRect mirroring (Rect(1,0,-1,1)).
            cam.StartCamera();
            Debug.Log("[GameManagerFruit] Camera started via deps.");
        }
        else
        {
            // Camera is already running (e.g. shared across scenes via
            // DontDestroyOnLoad). Re-wire via SetOutputImage so it
            // respects the global mirror setting from CameraConfigUI.
            if (cam.ActiveWebCamTexture != null)
            {
                cam.SetOutputImage(cameraOverlay);
                Debug.Log("[GameManagerFruit] Camera already active, SetOutputImage reassigned.");
            }
            else
            {
                // Edge case: IsPlaying is true but texture is null —
                // restart to recover
                cam.StartCamera();
                Debug.Log("[GameManagerFruit] Camera active but texture null, restarting.");
            }
        }
    }

    /// <summary>
    /// Fallback camera initialization using the legacy CameraFeedCtrl.Instance
    /// singleton. Used when the scene is run standalone (not through
    /// SceneLoader → IMiniGame.OnStart), preserving backward compatibility.
    /// </summary>
    private void SetupCameraFallback()
    {
        if (_cameraInitialized) return;
        _cameraInitialized = true;

        CameraFeedCtrl cam = CameraFeedCtrl.Instance;
        if (cam == null)
        {
            Debug.LogWarning("[GameManagerFruit] CameraFeedCtrl.Instance is null — no camera available.");
            return;
        }

        if (cameraOverlay == null)
        {
            Debug.LogWarning("[GameManagerFruit] Camera display RawImage (cameraOverlay) not assigned!");
            return;
        }

        cam.SetOutputImage(cameraOverlay);

        if (!cam.IsPlaying)
        {
            // SetOutputImage() above already wired the display;
            // StartCamera() will populate the texture.
            cam.StartCamera();
            Debug.Log("[GameManagerFruit] Camera started via legacy singleton.");
        }
        else if (cam.ActiveWebCamTexture != null)
        {
            cam.SetOutputImage(cameraOverlay);
            Debug.Log("[GameManagerFruit] Camera already active (legacy), SetOutputImage reassigned.");
        }
        else
        {
            cam.StartCamera();
            Debug.Log("[GameManagerFruit] Camera active but texture null (legacy), restarting.");
        }
    }

    /// <summary>
    /// Returns the active CameraFeedCtrl, preferring the injected dependency
    /// over the legacy singleton. Used by Update() for health monitoring.
    /// </summary>
    private CameraFeedCtrl GetActiveCamera()
    {
        return _deps?.Camera ?? CameraFeedCtrl.Instance;
    }

    // ── Game Flow ──────────────────────────────────────────────────────────

    private void NewGame()
    {
        Time.timeScale = 1f;

        if (panelGameOver != null)
            panelGameOver.SetActive(false);

        // Lower UI Alpha when playing
        if (cameraUIAlpha != null) cameraUIAlpha.alpha = 0.3f;

        ClearScene();

        blade.enabled = true;
        spawner.enabled = true;

        score = 0;
        scoreText.text = score.ToString();

        FindAnyObjectByType<LivesManager>()?.ResetVidas();
    }

    private void ClearScene()
    {
        Fruit[] fruits = FindObjectsByType<Fruit>();
        foreach (Fruit fruit in fruits)
            Destroy(fruit.gameObject);

        Bomb[] bombs = FindObjectsByType<Bomb>();
        foreach (Bomb bomb in bombs)
            Destroy(bomb.gameObject);
    }

    public void IncreaseScore()
    {
        score++;
        scoreText.text = score.ToString();
    }

    public void MostrarGameOver()
    {
        // Restore UI Alpha
        if (cameraUIAlpha != null) cameraUIAlpha.alpha = 1f;

        blade.enabled = false;
        spawner.enabled = false;
        FindAnyObjectByType<GameTimer>()?.PausarTimer(true);
        StartCoroutine(ExplodeSequence());
    }

    public void Explode()
    {
        // Restore UI Alpha
        if (cameraUIAlpha != null) cameraUIAlpha.alpha = 1f;

        blade.enabled = false;
        spawner.enabled = false;
        FindAnyObjectByType<GameTimer>()?.PausarTimer(true);
        StartCoroutine(ExplodeSequence());
    }

    private IEnumerator ExplodeSequence()
    {
        yield return new WaitForSecondsRealtime(1f);

        if (panelGameOver != null)
            panelGameOver.SetActive(true);

        if (gameOverScoreText != null)
            gameOverScoreText.text = "Tu puntaje es: " + score;
    }

    public void Reiniciar()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void VolverAlMenu()
    {
        Time.timeScale = 1f;
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadSceneAsync("MainMenuNinjaFruit");
        }
        else
        {
            SceneManager.LoadScene("MainMenuNinjaFruit");
        }
    }
}
