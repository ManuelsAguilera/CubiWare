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
            StartCoroutine(InitializeAsync());
        }

        private void OnApplicationQuit()
        {
            // Kill Python server synchronously — coroutines may not complete before quit.
            KillPythonServer();
            StartCoroutine(ShutdownAsync());
        }

        // ── Initialization Sequence ────────────────────────────────────────

        private IEnumerator InitializeAsync()
        {
            State = BootstrapState.Initializing;

            // ── Step 1: Logger ─────────────────────────────────────────────
            _logger = ServiceLogger.Instance;
            _logger.LogInfo("BootstrapManager", "Bootstrap starting — State=Initializing");
            yield return null;

            // ── Step 2: Create data store ──────────────────────────────────
            _dataStore = new PlayerPrefsDataStore();
            _logger.LogInfo("BootstrapManager", "PlayerPrefsDataStore created.");
            yield return null;

            // ── Step 3: Initialize SceneLoader ─────────────────────────────
            SceneLoader.Instance.Initialize(this);
            _logger.LogInfo("BootstrapManager", "SceneLoader initialized.");
            yield return null;

            // ── Step 4: Initialize GameManager ─────────────────────────────
            GameManager.Instance.Initialize(_dataStore, SceneLoader.Instance);
            _logger.LogInfo("BootstrapManager", "GameManager initialized.");
            yield return null;

            // ── Step 5: CameraFeedProvider ─────────────────────────────────
            if (CameraFeedCtrl.Instance != null)
                _logger.LogInfo("BootstrapManager", "CameraFeedProvider ready.");
            else
                _logger.LogError("BootstrapManager", "CameraFeedCtrl.Instance is null!", ServiceErrorCode.NotInitialized);
            yield return null;

            // ── Step 6: HandDetectorService ────────────────────────────────
            if (MediaPipeController.Instance != null)
                _logger.LogInfo("BootstrapManager", "HandDetectorService will be initialized by MediaPipeController.Start().");
            else
                _logger.LogError("BootstrapManager", "MediaPipeController.Instance is null!", ServiceErrorCode.NotInitialized);
            yield return null;

            // ── Step 7: FaceDetectorService ────────────────────────────────
            _logger.LogInfo("BootstrapManager", "FaceDetectorService will be initialized by MediaPipeController.Start().");
            yield return null;

            // ── Step 8: GroqLLMService ─────────────────────────────────────
            if (LLMConnector.Instance != null)
                _logger.LogInfo("BootstrapManager", "GroqLLMService initialized by LLMConnector.Awake().");
            else
                _logger.LogError("BootstrapManager", "LLMConnector.Instance is null!", ServiceErrorCode.NotInitialized);
            yield return null;

            // ── Step 8.5: Python Emotion Server ───────────────────────────
            yield return StartCoroutine(LaunchPythonServerAsync());

            // ── Step 9: EmotionGameBridge ──────────────────────────────────
            {
                var go = new GameObject("EmotionGameBridge");
                DontDestroyOnLoad(go);
                var bridge = go.AddComponent<ARcadeRush.EmotionDetection.EmotionGameBridge>();
                var client = go.AddComponent<ARcadeRush.EmotionDetection.EmotionWebSocketClient>();
                bridge.Initialize(client);
                _logger.LogInfo("BootstrapManager", "EmotionGameBridge initialized.");
            }
            yield return null;

            // ── Step 10: Mark as Initialized ──────────────────────────────
            State = BootstrapState.Initialized;
            _logger.LogInfo("BootstrapManager", "Bootstrap complete — State=Initialized");
            yield return null;

            // ── Step 11-12: Load MainMenu ──────────────────────────────────
            _logger.LogInfo("BootstrapManager", "Loading MainMenu...");
            SceneLoader.Instance.LoadSceneAsync("MainMenu", () =>
                _logger.LogInfo("BootstrapManager", "MainMenu loaded."));
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
