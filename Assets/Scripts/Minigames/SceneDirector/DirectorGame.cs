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

        // ── Lives ─────────────────────────────────────────────────────────────
        private const int MaxLives = 3;
        private int _lives;
        private TextMeshProUGUI _livesText;

        // ── Emotion ───────────────────────────────────────────────────────────
        private EmotionLabel _lastSimulated = EmotionLabel.Neutral;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() { /* panels built in OnStart after scene is fully loaded */ }

        private void Start()
        {
            // SceneLoader always calls OnStart() — don't call StartGame() here,
            // that would trigger a second OnStart() while GamePanel is already hidden,
            // causing all FindUI calls to return null.
            if (GameManager.Instance != null && _deps != null)
                GameManager.Instance.RegisterGame(this);
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

            // Configure visual styles (Image type, colors, font sizes)
            // so elements are visible regardless of how the scene was built.
            SetupGameUIStyles();

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

        private EmotionLabel _lastLoggedEmotion = EmotionLabel.Neutral;
        private float        _diagTimer          = 0f;

        // ── Audience dialogue (LLM) ───────────────────────────────────────────
        private readonly AudienceDialogueProvider _audienceDialogue = new AudienceDialogueProvider();
        private float _dialogueCooldown = 0f;
        private float _prevFill         = 0.5f;

        private void Update()
        {
            if (_state != State.Playing) return;

            EmotionLabel active = EmotionLabel.Neutral;

#if UNITY_EDITOR
            // In Editor: keyboard takes priority. Bridge is fallback when no key pressed.
            var fromKey = SimulateKeyboard();
            if (fromKey != EmotionLabel.Neutral)
                active = fromKey;
            else if (_deps?.EmotionBridge != null && _deps.EmotionBridge.IsConnected)
                active = ToEmotionLabel(_deps.EmotionBridge.CurrentEmotion);
#else
            // In build: always use DeepFace bridge
            if (_deps?.EmotionBridge != null && _deps.EmotionBridge.IsConnected)
                active = ToEmotionLabel(_deps.EmotionBridge.CurrentEmotion);
#endif

            // Log when emotion changes (not every frame to avoid spam)
            if (active != _lastLoggedEmotion)
            {
                _lastLoggedEmotion = active;
                Debug.Log($"[DirectorGame] Emoción activa: {active}");
            }

            Bar?.SetDetectedEmotion(active);

            // Audience dialogue on bar threshold crossings (cooldown stops flapping spam)
            _dialogueCooldown -= Time.deltaTime;
            if (Bar != null && Bar.IsActive && !Bar.InGracePeriod)
            {
                float fill = Bar.FillAmount;
                if (_dialogueCooldown <= 0f)
                {
                    if (fill >= 0.75f && _prevFill < 0.75f)
                    {
                        Audience?.ShowDialogue(_audienceDialogue.NextApproval());
                        _dialogueCooldown = 3f;
                    }
                    else if (fill <= 0.25f && _prevFill > 0.25f)
                    {
                        Audience?.ShowDialogue(_audienceDialogue.NextDisapproval());
                        _dialogueCooldown = 3f;
                    }
                }
                _prevFill = fill;
            }

            // Periodic diagnostic log every 3s (remove after debugging)
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= 3f)
            {
                _diagTimer = 0f;
                var bridge = _deps?.EmotionBridge;
                Debug.Log(
                    $"[Director DIAG] " +
                    $"Bridge={bridge?.IsConnected} | " +
                    $"Cara={bridge?.FaceDetected} | " +
                    $"DeepFace={bridge?.CurrentEmotion} conf={bridge?.Confidence:F2} | " +
                    $"Activa={active} | " +
                    $"Barra={Bar?.RequiredEmotion} | " +
                    $"IsCorrect={Bar?.IsCorrect} | " +
                    $"Fill={Bar?.FillAmount:F2}");
            }

            // Timer color: yellow → red when < 35% remaining
            if (_timerText != null && Countdown != null)
                _timerText.color = Countdown.NormalizedTime < 0.35f ? _timerUrgent : _timerNormal;
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
            _lives = MaxLives;
            UpdateLivesUI();

            // Clear default "New Text" from TMP elements before ScriptController sets them
            if (_emotionText  != null) _emotionText.text  = "";
            if (_timerText    != null) _timerText.text    = "";
            if (_progressText != null) _progressText.text = "";
            var nextTmp = GameObject.Find("NextEmotionText")?.GetComponent<TextMeshProUGUI>();
            if (nextTmp != null) nextTmp.text = "";

            // Wire Scenario events here (not in OnStart) to prevent the Animator
            // from auto-firing OnOpenComplete before the user clicks Play.
            if (Scenario != null)
            {
                Scenario.OnOpenComplete  -= OnCurtainOpen;   // guard against double-sub
                Scenario.OnCloseComplete -= OnCurtainClose;
                Scenario.OnOpenComplete  += OnCurtainOpen;
                Scenario.OnCloseComplete += OnCurtainClose;
            }

            ServiceLogger.Instance.LogInfo(LOG, "Play clicked — countdown on closed curtain, then opening.");
            StartCoroutine(PlayIntroThenOpen());
        }

        private void OnExitClicked() => OnEnd();

        // ── Curtain events ────────────────────────────────────────────────────

        /// <summary>
        /// Shows the big "3·2·1·Empieza la obra" countdown ON the closed curtain,
        /// then opens the curtain. The sequence itself loads in OnCurtainOpen once
        /// the curtain finishes opening (so the grace period starts on the open stage).
        /// </summary>
        private IEnumerator PlayIntroThenOpen()
        {
            Debug.Log($"[Director] PlayIntroThenOpen — overlay={_countdownOverlay != null}, text={_countdownOverlayText != null}");

            if (_countdownOverlayText != null)
            {
                // Procedural styling — large, centered, wide enough for "Empieza la obra".
                _countdownOverlayText.fontSize             = 160;
                _countdownOverlayText.enableAutoSizing     = true;
                _countdownOverlayText.fontSizeMin          = 80;
                _countdownOverlayText.fontSizeMax          = 200;
                _countdownOverlayText.color                = Color.white;
                _countdownOverlayText.fontStyle            = FontStyles.Bold;
                _countdownOverlayText.alignment            = TextAlignmentOptions.Center;
                _countdownOverlayText.enableWordWrapping   = true;
                var trt = _countdownOverlayText.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.sizeDelta = new Vector2(1400f, 400f);
                trt.anchoredPosition = Vector2.zero;
            }

            // Draw the countdown ON TOP of the closed curtain.
            if (_countdownOverlay != null)
                _countdownOverlay.transform.SetAsLastSibling();

            ShowCanvasGroup(_countdownOverlay);
            foreach (string n in new[] { "3", "2", "1", "Empieza la obra" })
            {
                if (_countdownOverlayText != null) _countdownOverlayText.text = n;
                yield return new WaitForSeconds(n == "Empieza la obra" ? 1.0f : 0.7f);
            }
            HideCanvasGroup(_countdownOverlay);

            // Now raise the curtain — OnCurtainOpen loads the sequence when it finishes.
            ServiceLogger.Instance.LogInfo(LOG, "Countdown done — opening curtain.");
            Scenario?.Open();
        }

        private void OnCurtainOpen()
        {
            ServiceLogger.Instance.LogInfo(LOG, "Curtain open — loading sequence.");
            var seq = Script?.GenerateLocalSequence(timePerElement: 10f) ?? new List<ScriptElement>();
            Debug.Log($"[Director DIAG] seq.Count={seq.Count}  seq[0].TimeLimit={( seq.Count > 0 ? seq[0].TimeLimit.ToString("F1") : "N/A")}  Script.Instance={Script != null}");
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

            // One LLM request per element — lines are cached and reused on retries.
            _audienceDialogue.PrepareFor(el.RequiredEmotion, _deps?.LLM);
            _dialogueCooldown = 0f;
            _prevFill         = 0.5f;

            if (_emotionText != null)  _emotionText.text  = EmotionEs.ToSpanish(el.RequiredEmotion);
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
            Audience?.ShowDialogue(_audienceDialogue.NextApproval());
            StartCoroutine(ShowFeedback("¡CORRECTO!", Color.green));
        }

        private void OnElementFailed(ScriptElement el)
        {
            Audience?.ReactNegative();
            Audience?.ShowDialogue(_audienceDialogue.NextDisapproval());
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

        private void OnBarEmptied()     => HandleElementFail();
        private void OnCountdownExpired() => HandleElementFail();

        private void HandleElementFail()
        {
            if (_elementResolved) return;
            _elementResolved = true;

            Bar?.Deactivate();
            Countdown?.Stop();

            _lives--;
            UpdateLivesUI();

            Debug.Log($"[Director] ❤ vida perdida — quedan {_lives}/{MaxLives}");

            Script?.FailCurrentElement(); // dispara OnElementFailed (feedback visual/audio)

            if (_lives > 0)
                StartCoroutine(RetryCurrentElement());
            else
                StartCoroutine(EndRound(won: false));
        }

        private IEnumerator RetryCurrentElement()
        {
            yield return new WaitForSeconds(1.2f);
            if (_state != State.Playing) yield break;

            _elementResolved = false;
            var el = Script.CurrentElement;
            Debug.Log($"[Director] 🔄 reintentando {el.RequiredEmotion} (vidas={_lives})");
            Bar?.Activate(el.RequiredEmotion);
            Countdown?.StartCountdown(el.TimeLimit);
        }

        private void UpdateLivesUI()
        {
            if (_livesText != null)
                _livesText.text = new string('♥', _lives) + new string('♡', MaxLives - _lives);
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

        // ── Game UI visual setup ──────────────────────────────────────────────

        // Timer colors — yellow → red as time runs out
        private static readonly Color _timerNormal  = new Color(1f, 0.8f, 0f, 1f);   // #FFCC00
        private static readonly Color _timerUrgent  = new Color(1f, 0.27f, 0.27f, 1f); // #FF4444

        private void SetupGameUIStyles()
        {
            // ── Approval bar ──────────────────────────────────────────────────
            if (_barFill != null)
            {
                _barFill.type       = Image.Type.Filled;
                _barFill.fillMethod = Image.FillMethod.Horizontal;
                _barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                // White: the segmented red→green sprite provides the color.
                _barFill.color      = Color.white;

                // Wide strip above the audience (5%–95% horizontally, 17.5%–23.5% vertically)
                var rt = _barFill.rectTransform;
                rt.anchorMin = new Vector2(0.05f, 0.175f);
                rt.anchorMax = new Vector2(0.95f, 0.235f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;

                // Dark background behind the bar
                var bgGO = new GameObject("ApprovalBarBG");
                bgGO.layer = 5;
                var bgRt = bgGO.AddComponent<RectTransform>();
                bgGO.transform.SetParent(_barFill.transform.parent, false);
                bgGO.transform.SetSiblingIndex(_barFill.transform.GetSiblingIndex());
                bgRt.anchorMin = new Vector2(0.05f, 0.175f);
                bgRt.anchorMax = new Vector2(0.95f, 0.235f);
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
                bgGO.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.13f, 1f);
            }

            // ── Emotion name — large, on the script paper (right side) ────────
            //   ★ FELIZ ★  above the emotion icon
            StyleText(_emotionText, 54, Color.white,
                new Vector2(0.865f, 0.72f), new Vector2(460, 90));
            if (_emotionText != null) _emotionText.fontStyle = FontStyles.Bold;

            // ── Next emotion — on the paper, below the icon ───────────────────
            //   "Siguiente: ENOJADO"
            var nextLabel = _progressText != null
                ? GameObject.Find("NextEmotionText")?.GetComponent<TextMeshProUGUI>()
                : null;
            StyleText(nextLabel, 26, new Color(0f, 0.706f, 1f, 1f),  // #00B4FF
                new Vector2(0.865f, 0.44f), new Vector2(400, 50));

            // ── Timer — top-left, above the bar, changes color near zero ──────
            StyleText(_timerText, 42, _timerNormal,
                new Vector2(0.12f, 0.275f), new Vector2(300, 60));
            if (_timerText != null) _timerText.alignment = TextAlignmentOptions.Center;

            // ── Progress — top-center, above the bar ──────────────────────────
            StyleText(_progressText, 36, new Color(0.67f, 0.67f, 0.67f, 1f), // #AAAAAA
                new Vector2(0.5f, 0.275f), new Vector2(400, 70));
            if (_progressText != null) _progressText.alignment = TextAlignmentOptions.Center;

            // ── Lives — top center ────────────────────────────────────────────
            if (_livesText == null)
            {
                var canvas = FindCanvasInScene();
                if (canvas != null)
                {
                    var go = new GameObject("LivesText");
                    go.layer = 5;
                    go.AddComponent<RectTransform>();
                    go.transform.SetParent(_gamePanel != null ? _gamePanel.transform : canvas.transform, false);
                    _livesText = go.AddComponent<TextMeshProUGUI>();
                }
            }
            StyleText(_livesText, 36, new Color(1f, 0.3f, 0.3f, 1f),
                new Vector2(0.5f, 0.95f), new Vector2(160, 50));
            if (_livesText != null) _livesText.alignment = TextAlignmentOptions.Center;
        }

        private static void StyleText(TextMeshProUGUI tmp, float size, Color color,
            Vector2 anchor, Vector2 sizePixels)
        {
            if (tmp == null) return;
            tmp.fontSize  = size;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.Center;

            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta  = sizePixels;
            rt.anchoredPosition = Vector2.zero;
        }

        // ── Menu panels ───────────────────────────────────────────────────────

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

            // Background — curtain image (falls back to solid dark if the sprite is missing)
            var bg = panel.AddComponent<Image>();
            var curtainBg = LoadCurtainSprite();
            if (curtainBg != null)
            {
                bg.sprite = curtainBg;
                bg.type   = Image.Type.Simple;
                bg.color  = Color.white;
            }
            else
            {
                bg.color = new Color(0f, 0f, 0.031f, 1f); // fully opaque fallback
            }
            bg.raycastTarget = true;

            // Dim overlay so the title/buttons stay readable over the curtain art.
            if (curtainBg != null)
            {
                var dimGO = new GameObject("PanelDim");
                dimGO.layer = 5;
                var drt = dimGO.AddComponent<RectTransform>();
                dimGO.transform.SetParent(panel.transform, false);
                drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
                drt.offsetMin = drt.offsetMax = Vector2.zero;
                var dimImg = dimGO.AddComponent<Image>();
                dimImg.color = new Color(0f, 0f, 0f, 0.45f);
                dimImg.raycastTarget = false;
            }

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

        /// <summary>
        /// Reuses the curtain sprite already loaded in the scene (on CurtainPanel's Image).
        /// Runtime-safe — no AssetDatabase/Resources dependency. Null if not found.
        /// </summary>
        private static Sprite LoadCurtainSprite()
            => GameObject.Find("CurtainPanel")?.GetComponentInChildren<Image>(true)?.sprite;

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
        // Latch: press once to set emotion, stays until another key or N for Neutral.
        private EmotionLabel _latchedEmotion = EmotionLabel.Neutral;

        private EmotionLabel SimulateKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.H)) { _latchedEmotion = EmotionLabel.Happy;     Debug.Log("[DirectorGame] Tecla: H → Happy (latch)");     }
            if (Input.GetKeyDown(KeyCode.S)) { _latchedEmotion = EmotionLabel.Surprised; Debug.Log("[DirectorGame] Tecla: S → Surprised (latch)"); }
            if (Input.GetKeyDown(KeyCode.A)) { _latchedEmotion = EmotionLabel.Angry;     Debug.Log("[DirectorGame] Tecla: A → Angry (latch)");     }
            if (Input.GetKeyDown(KeyCode.F)) { _latchedEmotion = EmotionLabel.Fear;      Debug.Log("[DirectorGame] Tecla: F → Fear (latch)");      }
            if (Input.GetKeyDown(KeyCode.D)) { _latchedEmotion = EmotionLabel.Disgust;   Debug.Log("[DirectorGame] Tecla: D → Disgust (latch)");   }
            if (Input.GetKeyDown(KeyCode.Z)) { _latchedEmotion = EmotionLabel.Sad;       Debug.Log("[DirectorGame] Tecla: Z → Sad (latch)");       }
            if (Input.GetKeyDown(KeyCode.N)) { _latchedEmotion = EmotionLabel.Neutral;   Debug.Log("[DirectorGame] Tecla: N → Neutral (latch)");   }
            return _latchedEmotion;
        }
#endif
    }
}
