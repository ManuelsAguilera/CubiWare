using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Face;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Director de Escena minigame controller.
    ///
    /// Design principles:
    ///  - ZERO SerializeField references that need Inspector wiring.
    ///  - Subsystems found via their .Instance singletons.
    ///  - UI game elements found by GameObject.Find() by name.
    ///  - Start menu and Game Over panels built programmatically (no YAML GUIDs).
    ///  - Three states only: StartMenu → Playing → GameOver.
    ///
    /// Editor simulation: press H/S/A/N to simulate emotions without Python server.
    /// </summary>
    public class DirectorGame : MonoBehaviour, IMiniGame
    {
        public int SceneIndex => 8;

        private const string LOG = "DirectorGame";

        // ── Dependencies (injected by SceneLoader) ────────────────────────────
        private MiniGameDependencies _deps;

        // ── Subsystems (found via .Instance, zero Inspector wiring) ──────────
        private ApprovalBarController Bar       => ApprovalBarController.Instance;
        private ScriptController      Script    => ScriptController.Instance;
        private CountdownController   Countdown => CountdownController.Instance;
        private ScenarioController    Scenario  => ScenarioController.Instance;
        private AudienceController    Audience  => AudienceController.Instance;

        // ── UI game elements (found by name at runtime) ───────────────────────
        private Image           _barFill;
        private TextMeshProUGUI _timerText;
        private TextMeshProUGUI _emotionText;
        private TextMeshProUGUI _progressText;
        private RawImage        _cameraDisplay;
        private GameObject      _gamePanel;
        private CanvasGroup     _countdownOverlay;
        private TextMeshProUGUI _countdownOverlayText;
        private CanvasGroup     _feedbackOverlay;
        private TextMeshProUGUI _feedbackText;

        // ── Programmatic menu panels (created in Awake, no YAML) ─────────────
        private GameObject _startMenuPanel;
        private GameObject _gameOverPanel;
        private TextMeshProUGUI _startMenuTitle;
        private TextMeshProUGUI _gameOverTitle;

        // ── Game state ────────────────────────────────────────────────────────
        private enum State { StartMenu, Playing, GameOver }
        private State _state = State.StartMenu;
        private bool  _elementResolved;
        private int   _score;
        private int   _elementsPassed;

        // ── Emotion ───────────────────────────────────────────────────────────
        private EmotionLabel _lastSimulated = EmotionLabel.Neutral;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() { /* panels built in OnStart after scene is fully loaded */ }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                if (_deps != null) GameManager.Instance.RegisterGame(this);
                else               GameManager.Instance.StartGame(this);
            }
        }

        // ── IMiniGame ─────────────────────────────────────────────────────────

        public void OnStart(MiniGameDependencies deps)
        {
            _deps  = deps;
            _score = 0;
            _elementsPassed = 0;

            // Enable emotion bridge in live mode
            if (deps.EmotionBridge != null)
                deps.EmotionBridge.SetEnabled(true);

            // Find game UI by name — works regardless of Inspector wiring
            _barFill              = FindUI<Image>("ApprovalBarFill");
            _timerText            = FindUI<TextMeshProUGUI>("TimerText");
            _emotionText          = FindUI<TextMeshProUGUI>("EmotionText");
            _progressText         = FindUI<TextMeshProUGUI>("ProgressText");
            _cameraDisplay        = FindUI<RawImage>("CameraDisplay");
            _gamePanel            = GameObject.Find("GamePanel");
            _countdownOverlay     = FindUI<CanvasGroup>("CountdownOverlay");
            _countdownOverlayText = FindUI<TextMeshProUGUI>("CountdownText");
            _feedbackOverlay      = FindUI<CanvasGroup>("FeedbackOverlay");
            _feedbackText         = FindUI<TextMeshProUGUI>("FeedbackText");

            // Bind webcam
            deps.Camera?.SetOutputImage(_cameraDisplay);

            // Build menu panels NOW (not in Awake) so FindCanvasInScene finds
            // the Director's Canvas, not LoadingScreenManager's DontDestroyOnLoad canvas.
            BuildMenuPanels();

            // Hide game UI until Play is clicked
            _gamePanel?.SetActive(false);
            HideCanvasGroup(_countdownOverlay);
            HideCanvasGroup(_feedbackOverlay);

            // Wire subsystem events — Scenario events wired in OnPlayClicked
            // to prevent the Animator from auto-firing OnOpenComplete on scene load.
            if (Bar      != null) { Bar.OnBarFilled    += OnBarFilled;     Bar.OnBarEmptied   += OnBarEmptied;   }
            if (Countdown!= null) { Countdown.OnCountdownExpired += OnCountdownExpired; }
            if (Script   != null)
            {
                Script.OnElementStarted  += OnElementStarted;
                Script.OnElementPassed   += OnElementPassed;
                Script.OnElementFailed   += OnElementFailed;
                Script.OnSequenceComplete += OnSequenceComplete;
            }

            ServiceLogger.Instance.LogInfo(LOG, "OnStart — showing start menu.");
            ShowStartMenu();
        }

        public void OnEnd()
        {
            _state = State.StartMenu;

            _deps?.EmotionBridge?.SetEnabled(false);
            _deps?.GameManager?.EndGame();

            if (Bar      != null) { Bar.OnBarFilled    -= OnBarFilled;     Bar.OnBarEmptied   -= OnBarEmptied;   }
            if (Countdown!= null) { Countdown.OnCountdownExpired -= OnCountdownExpired; }
            if (Script   != null)
            {
                Script.OnElementStarted  -= OnElementStarted;
                Script.OnElementPassed   -= OnElementPassed;
                Script.OnElementFailed   -= OnElementFailed;
                Script.OnSequenceComplete -= OnSequenceComplete;
            }
            if (Scenario != null)
            {
                Scenario.OnOpenComplete  -= OnCurtainOpen;
                Scenario.OnCloseComplete -= OnCurtainClose;
            }

            StopAllCoroutines();

            UnityEngine.PlayerPrefs.SetInt("ReturnToGameSelector", 1);
            SceneLoader.Instance?.LoadSceneAsync("MainMenu");
        }

        // ── Update — emotion polling ──────────────────────────────────────────

        private void Update()
        {
            if (_state != State.Playing) return;

            EmotionLabel active;

#if UNITY_EDITOR
            active = SimulateKeyboard();
#else
            active = EmotionLabel.Neutral;
#endif
            // Live emotion from DeepFace
            if (_deps?.EmotionBridge != null && _deps.EmotionBridge.IsConnected)
                active = ToEmotionLabel(_deps.EmotionBridge.CurrentEmotion);

            Bar?.SetDetectedEmotion(active);
        }

        // ── Menu panels (built in code, no YAML) ─────────────────────────────

        private void ShowStartMenu()
        {
            _state = State.StartMenu;
            if (_startMenuTitle != null) _startMenuTitle.text = "DIRECTOR DE ESCENA";
            _startMenuPanel?.SetActive(true);
            _gameOverPanel?.SetActive(false);
        }

        private void ShowGameOver()
        {
            _state = State.GameOver;
            if (_gameOverTitle != null)
                _gameOverTitle.text = _elementsPassed > 0 ? $"GAME OVER\n{_elementsPassed} acertadas" : "GAME OVER";
            _gameOverPanel?.SetActive(true);
            _startMenuPanel?.SetActive(false);
        }

        private void OnPlayClicked()
        {
            // Destroy ALL StartMenuPanel instances — covers the case where
            // BuildMenuPanels() ran twice and created two panels.
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "StartMenuPanel") Object.Destroy(go);
            _startMenuPanel = null;

            _gamePanel?.SetActive(true);
            _state = State.Playing;

            // Wire Scenario events here (not in OnStart) to prevent the Animator
            // from auto-firing OnOpenComplete before the user clicks Play.
            if (Scenario != null)
            {
                Scenario.OnOpenComplete  -= OnCurtainOpen;   // guard against double-sub
                Scenario.OnCloseComplete -= OnCurtainClose;
                Scenario.OnOpenComplete  += OnCurtainOpen;
                Scenario.OnCloseComplete += OnCurtainClose;
            }

            ServiceLogger.Instance.LogInfo(LOG, "Play clicked — opening curtain.");
            Scenario?.Open();
        }

        private void OnExitClicked() => OnEnd();

        // ── Curtain events ────────────────────────────────────────────────────

        private void OnCurtainOpen()
        {
            ServiceLogger.Instance.LogInfo(LOG, "Curtain open — starting sequence.");
            StartCoroutine(StartWithCountdown());
        }

        private IEnumerator StartWithCountdown()
        {
            // 3-2-1 overlay
            ShowCanvasGroup(_countdownOverlay);
            foreach (string n in new[] { "3", "2", "1", "¡ACCIÓN!" })
            {
                if (_countdownOverlayText != null) _countdownOverlayText.text = n;
                yield return new WaitForSeconds(n == "¡ACCIÓN!" ? 0.8f : 0.7f);
            }
            HideCanvasGroup(_countdownOverlay);

            var seq = Script?.GenerateLocalSequence() ?? new List<ScriptElement>();
            Script?.LoadSequence(seq);
        }

        private void OnCurtainClose()
        {
            ServiceLogger.Instance.LogInfo(LOG, "Curtain closed.");
            ShowGameOver();
        }

        // ── Element events ────────────────────────────────────────────────────

        private void OnElementStarted(ScriptElement el)
        {
            _elementResolved = false;
            Audience?.SetIdle();

            if (_emotionText != null)  _emotionText.text  = el.RequiredEmotion.ToString().ToUpper();
            if (_progressText != null) _progressText.text = $"{Script.CurrentIndex + 1} / {Script.TotalElements}";

            Bar?.Activate(el.RequiredEmotion);
            Countdown?.StartCountdown(el.TimeLimit);

            ServiceLogger.Instance.LogInfo(LOG, $"Element started: {el.RequiredEmotion} ({el.TimeLimit}s)");
        }

        private void OnElementPassed(ScriptElement el)
        {
            _elementsPassed++;
            _score += 100;
            _deps?.GameManager?.AddScore(100);
            Audience?.ReactPositive();
            StartCoroutine(ShowFeedback("¡CORRECTO!", Color.green));
        }

        private void OnElementFailed(ScriptElement el)
        {
            Audience?.ReactNegative();
            StartCoroutine(ShowFeedback("¡FALLADO!", Color.red));
        }

        private void OnSequenceComplete()
        {
            ServiceLogger.Instance.LogInfo(LOG, "Sequence complete — WON!");
            StartCoroutine(EndRound(won: true));
        }

        // ── Bar / Countdown callbacks ─────────────────────────────────────────

        private void OnBarFilled()
        {
            if (_elementResolved) return;
            _elementResolved = true;
            Countdown?.Stop();
            Script?.PassCurrentElement();
        }

        private void OnBarEmptied()
        {
            if (_elementResolved) return;
            _elementResolved = true;
            Countdown?.Stop();
            Script?.FailCurrentElement();
            StartCoroutine(EndRound(won: false));
        }

        private void OnCountdownExpired()
        {
            if (_elementResolved) return;
            _elementResolved = true;
            Bar?.Deactivate();
            Script?.FailCurrentElement();
            StartCoroutine(EndRound(won: false));
        }

        // ── Round end ─────────────────────────────────────────────────────────

        private IEnumerator EndRound(bool won)
        {
            _state = State.GameOver;
            yield return new WaitForSeconds(won ? 1.5f : 1f);
            Scenario?.Close();
            // OnCurtainClose fires ShowGameOver
        }

        // ── Feedback overlay ──────────────────────────────────────────────────

        private IEnumerator ShowFeedback(string msg, Color col)
        {
            if (_feedbackText  != null) { _feedbackText.text = msg; _feedbackText.color = col; }
            ShowCanvasGroup(_feedbackOverlay);
            yield return new WaitForSeconds(1f);
            HideCanvasGroup(_feedbackOverlay);
        }

        // ── Programmatic UI construction ──────────────────────────────────────

        private void BuildMenuPanels()
        {
            var canvas = FindCanvasInScene();
            if (canvas == null) return;

            _startMenuPanel = BuildPanel(canvas, "StartMenuPanel",
                title: "DIRECTOR DE ESCENA",
                subtitle: "Expresa las emociones del guion",
                playLabel: "JUGAR",
                exitLabel: "SALIR",
                showPlay: true,
                out _startMenuTitle);

            _gameOverPanel = BuildPanel(canvas, "GameOverPanel",
                title: "GAME OVER",
                subtitle: null,
                playLabel: null,
                exitLabel: "SALIR",
                showPlay: false,
                out _gameOverTitle);

            _gameOverPanel.SetActive(false);
        }

        private GameObject BuildPanel(GameObject canvas, string panelName,
            string title, string subtitle, string playLabel, string exitLabel,
            bool showPlay, out TextMeshProUGUI titleTmp)
        {
            var panel = new GameObject(panelName);
            panel.layer = 5;
            // Add RectTransform BEFORE parenting — Unity requires this order for proper stretch
            var rt = panel.AddComponent<RectTransform>();
            panel.transform.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            // Background — solid dark fill
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0.031f, 0.95f);
            bg.raycastTarget = true;

            // Title
            titleTmp = MakeTMP(panel, "PanelTitle", title, 52, Color.white,
                new Vector2(0.1f, 0.65f), new Vector2(0.9f, 0.82f));
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;

            // Subtitle
            if (subtitle != null)
            {
                var sub = MakeTMP(panel, "PanelSubtitle", subtitle, 26, new Color(0.7f,0.7f,0.7f),
                    new Vector2(0.15f, 0.54f), new Vector2(0.85f, 0.63f));
                sub.alignment = TextAlignmentOptions.Center;
            }

            // Play button
            if (showPlay && playLabel != null)
            {
                var playBtn = MakeButton(panel, "PanelPlayBtn", playLabel,
                    new Vector2(0.35f, 0.40f), new Vector2(0.65f, 0.52f),
                    new Color(0f, 0.706f, 1f));
                playBtn.GetComponent<Button>().onClick.AddListener(OnPlayClicked);
            }

            // Exit button
            if (exitLabel != null)
            {
                float yMin = showPlay ? 0.26f : 0.38f;
                float yMax = showPlay ? 0.38f : 0.50f;
                var exitBtn = MakeButton(panel, "PanelExitBtn", exitLabel,
                    new Vector2(0.35f, yMin), new Vector2(0.65f, yMax),
                    new Color(0.3f, 0.3f, 0.3f));
                exitBtn.GetComponent<Button>().onClick.AddListener(OnExitClicked);
            }

            return panel;
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private static GameObject FindCanvasInScene()
        {
            // Search by name to find the Director scene's Canvas,
            // NOT LoadingScreenManager's "LoadingCanvas" (DontDestroyOnLoad).
            var go = GameObject.Find("Canvas");
            if (go != null && go.GetComponent<Canvas>() != null) return go;

            // Fallback: search only in the active scene
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<Canvas>(true);
                if (c != null) return c.gameObject;
            }
            return null;
        }

        private static T FindUI<T>(string goName) where T : Component
            => GameObject.Find(goName)?.GetComponent<T>();

        private static void ShowCanvasGroup(CanvasGroup g)
        {
            if (g == null) return;
            g.alpha = 1f; g.interactable = true; g.blocksRaycasts = true;
        }

        private static void HideCanvasGroup(CanvasGroup g)
        {
            if (g == null) return;
            g.alpha = 0f; g.interactable = false; g.blocksRaycasts = false;
        }

        private static TextMeshProUGUI MakeTMP(GameObject parent, string name, string text,
            float size, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.layer = 5;
            // RectTransform BEFORE SetParent
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent.transform, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static GameObject MakeButton(GameObject parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bgColor)
        {
            var go = new GameObject(name);
            go.layer = 5;
            // RectTransform BEFORE SetParent (same fix as BuildPanel)
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent.transform, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            go.AddComponent<Button>();

            var txtGO = new GameObject("Label");
            txtGO.transform.SetParent(go.transform, false);
            txtGO.layer = 5;
            var trt = txtGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 32; tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;

            return go;
        }

        // ── Emotion mapping ───────────────────────────────────────────────────

        private static EmotionLabel ToEmotionLabel(EmotionType t) => t switch
        {
            EmotionType.Happy    => EmotionLabel.Happy,
            EmotionType.Surprise => EmotionLabel.Surprised,
            EmotionType.Angry    => EmotionLabel.Angry,
            EmotionType.Disgust  => EmotionLabel.Disgust,
            EmotionType.Fear     => EmotionLabel.Fear,
            EmotionType.Sad      => EmotionLabel.Sad,
            _                    => EmotionLabel.Neutral,
        };

#if UNITY_EDITOR
        private EmotionLabel SimulateKeyboard()
        {
            if (Input.GetKey(KeyCode.H)) return EmotionLabel.Happy;
            if (Input.GetKey(KeyCode.S)) return EmotionLabel.Surprised;
            if (Input.GetKey(KeyCode.A)) return EmotionLabel.Angry;
            if (Input.GetKey(KeyCode.F)) return EmotionLabel.Fear;
            if (Input.GetKey(KeyCode.D)) return EmotionLabel.Disgust;
            if (Input.GetKey(KeyCode.Z)) return EmotionLabel.Sad;
            return EmotionLabel.Neutral;
        }
#endif
    }
}
