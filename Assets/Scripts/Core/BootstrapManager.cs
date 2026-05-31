using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using CubiWare.Core.Logging;
using CubiWare.Core.Services;
using CubiWare.Core.Interfaces;
using ARcadeRush.Core;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace CubiWare.Core
{
    /// <summary>
    /// Orchestrates the entire application initialization sequence.
    /// Lives in the Bootstrap scene and ensures all services are initialized
    /// in the correct order before loading the MainMenu scene.
    ///
    /// Step 8.5 automatically launches the DeepFace Python server:
    ///   • Creates a venv if missing (first-time setup, ~2-5 min)
    ///   • Checks if server is already running before launching
    ///   • Passes Unity's PID so Python can self-exit if Unity crashes
    ///   • Kills the server on application quit / shutdown
    /// </summary>
    public class BootstrapManager : MonoBehaviour
    {
        public static BootstrapManager Instance { get; private set; }

        public enum BootstrapState
        {
            NotStarted,
            Initializing,
            Initialized,
            ShuttingDown,
            ShutDown
        }

        public BootstrapState State { get; private set; }

        private ServiceLogger        _logger;
        private PlayerPrefsDataStore _dataStore;
        private Process              _pythonServerProcess;

        // ── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            State = BootstrapState.NotStarted;
            Debug.Log("[BootstrapManager] Awake — Instance set, DontDestroyOnLoad applied.");
        }

        private void Start()
        {
            // Create loading screen before everything else so it's visible immediately.
            var lsGO = new GameObject("LoadingScreenManager");
            DontDestroyOnLoad(lsGO);
            lsGO.AddComponent<ARcadeRush.UI.LoadingScreenManager>();
            StartCoroutine(InitializeAsync());
        }

        private void OnApplicationQuit()
        {
            // Kill Python server synchronously — coroutines may not complete before quit.
            KillPythonServer();
            StartCoroutine(ShutdownAsync());
        }

        // ── Initialization Sequence ────────────────────────────────────────

        private const int TOTAL_STEPS = 10;

        private void Step(int n, string message)
        {
            _logger?.LogInfo("BootstrapManager", message);
            ARcadeRush.UI.LoadingScreenManager.Instance?.SetStep(n, TOTAL_STEPS, message);
        }

        private IEnumerator InitializeAsync()
        {
            State = BootstrapState.Initializing;
            ARcadeRush.UI.LoadingScreenManager.Instance?.ShowBootstrap();
            yield return null;

            // ── Step 1: Logger ─────────────────────────────────────────────
            _logger = ServiceLogger.Instance;
            Step(1, "Iniciando servicios...");
            yield return null;

            // ── Step 2: Create data store ──────────────────────────────────
            _dataStore = new PlayerPrefsDataStore();
            Step(2, "Cargando datos de usuario...");
            yield return null;

            // ── Step 3: Initialize SceneLoader ─────────────────────────────
            SceneLoader.Instance.Initialize(this);
            Step(3, "Inicializando cargador de escenas...");
            yield return null;

            // ── Step 4: Initialize GameManager ─────────────────────────────
            GameManager.Instance.Initialize(_dataStore, SceneLoader.Instance);
            Step(4, "Inicializando GameManager...");
            yield return null;

            // ── Step 5: CameraFeedProvider ─────────────────────────────────
            // Start camera HERE during Bootstrap so any v4l2/driver freeze
            // happens while the loading screen is visible ("Inicializando cámara..."),
            // not silently after a minigame scene loads.
            Step(5, "Inicializando cámara...");
            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.StartCamera();

                // Wait up to 10 seconds for the camera to start playing.
                // The yield lets frames render while we wait — the loading screen
                // stays visible and the user sees progress, not a frozen screen.
                float cameraWait = 0f;
                while (!CameraFeedCtrl.Instance.IsPlaying && cameraWait < 10f)
                {
                    cameraWait += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (CameraFeedCtrl.Instance.IsPlaying)
                    _logger.LogInfo("BootstrapManager", $"Camera ready after {cameraWait:F1}s.");
                else
                    _logger.LogWarning("BootstrapManager", "Camera did not start within 10s — continuing anyway.");
            }
            else
            {
                _logger.LogError("BootstrapManager", "CameraFeedCtrl.Instance is null!", ServiceErrorCode.NotInitialized);
            }
            yield return null;

            // ── Step 6: HandDetectorService ────────────────────────────────
            Step(6, "Cargando detección de gestos (MediaPipe)...");
            if (MediaPipeController.Instance == null)
                _logger.LogError("BootstrapManager", "MediaPipeController.Instance is null!", ServiceErrorCode.NotInitialized);
            yield return null;

            // ── Step 7: FaceDetectorService ────────────────────────────────
            Step(7, "Cargando detección facial...");
            yield return null;

            // ── Step 8: GroqLLMService ─────────────────────────────────────
            Step(8, "Conectando al servicio LLM...");
            if (LLMConnector.Instance == null)
                _logger.LogError("BootstrapManager", "LLMConnector.Instance is null!", ServiceErrorCode.NotInitialized);
            yield return null;

            // ── Step 8.5: Python Emotion Server ───────────────────────────
            Step(9, "Iniciando servidor de emociones (DeepFace)...");
            yield return StartCoroutine(LaunchPythonServerAsync());

            // ── Step 9: EmotionGameBridge ──────────────────────────────────
            {
                var go = new GameObject("EmotionGameBridge");
                DontDestroyOnLoad(go);
                var bridge = go.AddComponent<ARcadeRush.EmotionDetection.EmotionGameBridge>();
                var client = go.AddComponent<ARcadeRush.EmotionDetection.EmotionWebSocketClient>();
                bridge.Initialize(client);
            }
            Step(10, "Cargando menú principal...");
            yield return null;

            // ── Complete ───────────────────────────────────────────────────
            State = BootstrapState.Initialized;
            ARcadeRush.UI.LoadingScreenManager.Instance?.SetBootstrapComplete();
            yield return null;

            SceneLoader.Instance.LoadSceneAsync("MainMenu", () =>
            {
                _logger.LogInfo("BootstrapManager", "MainMenu loaded.");
                ARcadeRush.UI.LoadingScreenManager.Instance?.Hide();
            });
        }

        // ── Python Server Lifecycle ────────────────────────────────────────

        private IEnumerator LaunchPythonServerAsync()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string serverDir   = Path.Combine(projectRoot, "PythonServer");
            string serverScript = Path.Combine(serverDir, "emotion_server.py");

            // Cross-platform venv Python path
            string pythonLinux = Path.Combine(serverDir, "venv", "bin", "python");
            string pythonWin   = Path.Combine(serverDir, "venv", "Scripts", "python.exe");
            string pythonExe   = File.Exists(pythonLinux) ? pythonLinux : pythonWin;

            if (!File.Exists(serverScript))
            {
                _logger.LogWarning("BootstrapManager", $"Python server script not found at: {serverScript}. Emotion detection unavailable.");
                yield break;
            }

            // ── Check if server is already running ─────────────────────────
            bool alreadyRunning = false;
            yield return StartCoroutine(CheckServerHealth(running => alreadyRunning = running));
            if (alreadyRunning)
            {
                _logger.LogInfo("BootstrapManager", "Python server already running — skipping launch.");
                yield break;
            }

            // ── Ensure venv exists (first-time setup) ──────────────────────
            if (!File.Exists(pythonExe))
            {
                _logger.LogInfo("BootstrapManager", "Python venv not found. Running first-time setup (this may take a few minutes)...");
                yield return StartCoroutine(EnsureVenvAsync(serverDir, pythonLinux));

                // Re-resolve after creation
                pythonExe = File.Exists(pythonLinux) ? pythonLinux : pythonWin;
                if (!File.Exists(pythonExe))
                {
                    _logger.LogError("BootstrapManager", "Venv creation failed. Is Python 3.10+ installed and in PATH?", ServiceErrorCode.NotInitialized);
                    yield break;
                }
            }

            // ── Launch server ──────────────────────────────────────────────
            int unityPid = Process.GetCurrentProcess().Id;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName         = pythonExe,
                    Arguments        = $"\"{serverScript}\" --parent-pid {unityPid}",
                    WorkingDirectory = serverDir,
                    UseShellExecute  = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow   = true,
                };

                _pythonServerProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                // Forward server logs to Unity console
                _pythonServerProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logger?.LogInfo("PythonServer", e.Data);
                };
                _pythonServerProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logger?.LogWarning("PythonServer", e.Data);
                };

                _pythonServerProcess.Start();
                _pythonServerProcess.BeginOutputReadLine();
                _pythonServerProcess.BeginErrorReadLine();

                _logger.LogInfo("BootstrapManager", $"Python server launched (PID {_pythonServerProcess.Id}).");
            }
            catch (Exception e)
            {
                _logger.LogError("BootstrapManager", $"Failed to launch Python server: {e.Message}", ServiceErrorCode.NotInitialized);
            }
        }

        private IEnumerator EnsureVenvAsync(string serverDir, string pythonExeTarget)
        {
            string systemPython = GetSystemPython();
            string venvPath     = Path.Combine(serverDir, "venv");
            string reqFile      = Path.Combine(serverDir, "requirements.txt");

            // Step A: create venv
            _logger.LogInfo("BootstrapManager", $"Creating venv at: {venvPath}");
            yield return StartCoroutine(RunProcessCoroutine(systemPython, $"-m venv \"{venvPath}\"", serverDir));

            if (!File.Exists(pythonExeTarget))
            {
                _logger.LogError("BootstrapManager", "Venv creation failed.", ServiceErrorCode.NotInitialized);
                yield break;
            }

            // Step B: install dependencies
            _logger.LogInfo("BootstrapManager", "Installing Python dependencies — this takes a few minutes on first run...");
            yield return StartCoroutine(RunProcessCoroutine(pythonExeTarget, $"-m pip install -r \"{reqFile}\"", serverDir));

            _logger.LogInfo("BootstrapManager", "Python environment ready.");
        }

        private IEnumerator RunProcessCoroutine(string exe, string args, string workDir)
        {
            bool done = false;
            int exitCode = -1;

            var psi = new ProcessStartInfo
            {
                FileName         = exe,
                Arguments        = args,
                WorkingDirectory = workDir,
                UseShellExecute  = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow   = true,
            };

            try
            {
                var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger?.LogInfo("Bootstrap:Python", e.Data); };
                p.ErrorDataReceived  += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger?.LogWarning("Bootstrap:Python", e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                var thread = new System.Threading.Thread(() =>
                {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                    p.Dispose();
                    done = true;
                });
                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception e)
            {
                _logger.LogError("Bootstrap:Python", $"Process failed: {e.Message}", ServiceErrorCode.NotInitialized);
                yield break;
            }

            while (!done) yield return null;

            if (exitCode != 0)
                _logger.LogWarning("Bootstrap:Python", $"Process exited with code {exitCode}");
        }

        private IEnumerator CheckServerHealth(Action<bool> callback)
        {
            using var req = UnityWebRequest.Get("http://localhost:8765/health");
            req.timeout = 1;
            yield return req.SendWebRequest();
            callback(req.result == UnityWebRequest.Result.Success);
        }

        private static string GetSystemPython()
        {
            // Try python3 first (Linux/macOS), fall back to python (Windows)
            foreach (var candidate in new[] { "python3", "python" })
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(candidate, "--version")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow = true,
                    });
                    p?.WaitForExit(2000);
                    if (p?.ExitCode == 0) return candidate;
                }
                catch { }
            }
            return "python3";
        }

        private void KillPythonServer()
        {
            if (_pythonServerProcess == null) return;
            try
            {
                if (!_pythonServerProcess.HasExited)
                {
                    _pythonServerProcess.Kill();
                    _pythonServerProcess.WaitForExit(2000);
                    _logger?.LogInfo("BootstrapManager", "Python server stopped.");
                }
            }
            catch (Exception e)
            {
                _logger?.LogWarning("BootstrapManager", $"Could not kill Python server: {e.Message}");
            }
            finally
            {
                _pythonServerProcess.Dispose();
                _pythonServerProcess = null;
            }
        }

        // ── Shutdown Sequence ──────────────────────────────────────────────

        public IEnumerator ShutdownAsync()
        {
            if (State == BootstrapState.ShuttingDown || State == BootstrapState.ShutDown)
                yield break;

            State = BootstrapState.ShuttingDown;
            _logger.LogInfo("BootstrapManager", "Shutdown starting — State=ShuttingDown");
            yield return null;

            // ── Shutdown Python server ─────────────────────────────────────
            KillPythonServer();
            yield return null;

            // ── Reverse Step 8: Shutdown GroqLLMService ────────────────────
            _logger.LogInfo("BootstrapManager", "Shutting down GroqLLMService...");
            yield return null;

            // ── Reverse Step 7: Shutdown FaceDetectorService ───────────────
            _logger.LogInfo("BootstrapManager", "Shutting down FaceDetectorService...");
            yield return null;

            // ── Reverse Step 6: Shutdown HandDetectorService ───────────────
            _logger.LogInfo("BootstrapManager", "Shutting down HandDetectorService...");
            yield return null;

            // ── Reverse Step 5: Shutdown CameraFeedProvider ────────────────
            _logger.LogInfo("BootstrapManager", "Shutting down CameraFeedProvider...");
            CameraFeedCtrl.Instance?.StopCamera();
            yield return null;

            // ── Reverse Step 4: Shutdown GameManager (save data) ──────────
            _logger.LogInfo("BootstrapManager", "Shutting down GameManager...");
            if (GameManager.Instance != null)
                _ = GameManager.Instance.SaveUserDataAsync();
            yield return null;

            State = BootstrapState.ShutDown;
            _logger.LogInfo("BootstrapManager", "Shutdown complete — State=ShutDown");
        }
    }
}
