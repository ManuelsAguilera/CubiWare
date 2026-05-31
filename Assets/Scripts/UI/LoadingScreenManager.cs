using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ARcadeRush.UI
{
    /// <summary>
    /// DontDestroyOnLoad loading screen with two modes:
    ///
    ///   Bootstrap — Diseño C: status messages list with ✓/●/○ icons.
    ///               Each step advances a progress bar at the bottom.
    ///               Use when initialization is long and the user needs context.
    ///
    ///   Transition — Diseño A: centered progress bar with scene name.
    ///                Use for scene loads (1–5 seconds).
    ///
    /// Created programmatically in Awake — no scene setup required.
    /// Sort Order 999 guarantees it renders above everything else.
    /// </summary>
    public class LoadingScreenManager : MonoBehaviour
    {
        public static LoadingScreenManager Instance { get; private set; }

        // ── Tuneable via Inspector ────────────────────────────────────────────
        [Header("Colors")]
        [SerializeField] private Color _backgroundColor  = new Color(0f, 0f, 0.031f, 1f); // #000008
        [SerializeField] private Color _accentColor      = new Color(0f, 0.706f, 1f, 1f);  // #00B4FF
        [SerializeField] private Color _textColor        = Color.white;
        [SerializeField] private Color _dimTextColor     = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color _barBgColor       = new Color(0.15f, 0.15f, 0.15f, 1f);

        [Header("Fade")]
        [SerializeField] private float _fadeDuration = 0.3f;

        // ── UI references (built in Awake) ────────────────────────────────────
        private Canvas         _canvas;
        private CanvasGroup    _canvasGroup;
        private GameObject     _bootstrapPanel;
        private GameObject     _transitionPanel;

        // Bootstrap panel elements
        private TMP_Text       _bTitle;
        private TMP_Text       _bStepLog;
        private Image          _bBarFill;
        private TMP_Text       _bPercent;

        // Transition panel elements
        private TMP_Text       _tLoadingLabel;
        private TMP_Text       _tSceneName;
        private Image          _tBarFill;
        private TMP_Text       _tPercent;

        // State
        private string         _completedSteps = "";
        private string         _currentStepMsg = "";
        private Coroutine      _fadeCoroutine;

        // Spinner
        private static readonly char[] _spinnerChars = { '|', '/', '-', '\\' };
        private int   _spinnerFrame   = 0;
        private float _spinnerTimer   = 0f;
        private const float SPINNER_INTERVAL = 0.12f;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUI();
            Hide(instant: true);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Shows the Bootstrap status panel immediately.</summary>
        public void ShowBootstrap(string title = "ARcade Rush")
        {
            _completedSteps = "";
            _currentStepMsg = "";
            _spinnerFrame   = 0;
            _spinnerTimer   = 0f;
            _bStepLog.text  = "";
            _bTitle.text    = title;
            SetBarFill(_bBarFill, 0f);
            _bPercent.text = "0%";

            _bootstrapPanel.SetActive(true);
            _transitionPanel.SetActive(false);
            FadeTo(1f);
        }

        /// <summary>Updates Bootstrap mode with the current step. Active step shows animated ASCII spinner.</summary>
        public void SetStep(int current, int total, string message)
        {
            if (!string.IsNullOrEmpty(_currentStepMsg))
                _completedSteps += $"\n<color=#00FF88>+</color>  {_currentStepMsg}";

            _currentStepMsg = message;
            _spinnerFrame   = 0;
            _spinnerTimer   = 0f;

            float pct = (float)(current - 1) / total;
            SetBarFill(_bBarFill, pct);
            _bPercent.text = $"{Mathf.RoundToInt(pct * 100)}%";
            UpdateStepDisplay();
        }

        /// <summary>Marks all steps complete and shows 100%.</summary>
        public void SetBootstrapComplete()
        {
            if (!string.IsNullOrEmpty(_currentStepMsg))
                _completedSteps += $"\n<color=#00FF88>+</color>  {_currentStepMsg}";
            _currentStepMsg = "";
            _bStepLog.text = _completedSteps.TrimStart('\n');
            SetBarFill(_bBarFill, 1f);
            _bPercent.text = "100%";
        }

        /// <summary>Shows the Transition (simple bar) panel.</summary>
        public void ShowTransition(string sceneName)
        {
            _tSceneName.text    = sceneName;
            _tLoadingLabel.text = "Cargando";
            SetBarFill(_tBarFill, 0f);
            _tPercent.text = "0%";

            _bootstrapPanel.SetActive(false);
            _transitionPanel.SetActive(true);
            FadeTo(1f);
        }

        /// <summary>Updates the Transition bar. value is 0.0–1.0.</summary>
        public void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            SetBarFill(_tBarFill, value);
            _tPercent.text = $"{Mathf.RoundToInt(value * 100)}%";
        }

        /// <summary>Fades out and hides the loading screen.</summary>
        public void Hide(bool instant = false)
        {
            if (instant)
            {
                _canvasGroup.alpha = 0f;
                gameObject.SetActive(false);
                return;
            }
            FadeTo(0f, onComplete: () => gameObject.SetActive(false));
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void Update()
        {
            if (_bootstrapPanel == null || !_bootstrapPanel.activeSelf) return;
            if (string.IsNullOrEmpty(_currentStepMsg)) return;

            _spinnerTimer += Time.unscaledDeltaTime;
            if (_spinnerTimer < SPINNER_INTERVAL) return;
            _spinnerTimer = 0f;
            _spinnerFrame = (_spinnerFrame + 1) % _spinnerChars.Length;
            UpdateStepDisplay();
        }

        private void UpdateStepDisplay()
        {
            string display = _completedSteps;
            if (!string.IsNullOrEmpty(_currentStepMsg))
                display += $"\n<color=#00B4FF>{_spinnerChars[_spinnerFrame]}</color>  {_currentStepMsg}";
            _bStepLog.text = display.TrimStart('\n');
        }

        private void FadeTo(float target, System.Action onComplete = null)
        {
            gameObject.SetActive(true);
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(CoFade(target, onComplete));
        }

        private IEnumerator CoFade(float target, System.Action onComplete)
        {
            float start   = _canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / _fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = target;
            onComplete?.Invoke();
        }

        private static void SetBarFill(Image bar, float t)
        {
            if (bar != null) bar.fillAmount = Mathf.Clamp01(t);
        }

        // ── UI Construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            // ── Canvas ────────────────────────────────────────────────────────
            var canvasGO = new GameObject("LoadingCanvas");
            canvasGO.transform.SetParent(transform);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup       = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable   = false;

            // ── Shared background ─────────────────────────────────────────────
            var bg = MakeImage(canvasGO, "Background", _backgroundColor);
            Stretch(bg.rectTransform);

            // ── Bootstrap Panel ───────────────────────────────────────────────
            _bootstrapPanel = MakePanel(canvasGO, "BootstrapPanel");

            // Title
            _bTitle = MakeText(_bootstrapPanel, "Title", "ARcade Rush", 52, _textColor);
            var titleRT = _bTitle.rectTransform;
            titleRT.anchorMin = new Vector2(0.5f, 0.85f);
            titleRT.anchorMax = new Vector2(0.5f, 0.95f);
            titleRT.sizeDelta = new Vector2(900, 80);
            titleRT.anchoredPosition = Vector2.zero;
            _bTitle.alignment = TextAlignmentOptions.Center;

            // Step log (scrolling status messages)
            _bStepLog = MakeText(_bootstrapPanel, "StepLog", "", 26, _textColor);
            var logRT = _bStepLog.rectTransform;
            logRT.anchorMin = new Vector2(0.15f, 0.30f);
            logRT.anchorMax = new Vector2(0.85f, 0.82f);
            logRT.offsetMin = logRT.offsetMax = Vector2.zero;
            _bStepLog.alignment  = TextAlignmentOptions.TopLeft;
            _bStepLog.overflowMode = TextOverflowModes.Truncate;

            // Progress bar (Bootstrap)
            _bBarFill = MakeProgressBar(_bootstrapPanel, "BBar",
                new Vector2(0.5f, 0.20f), new Vector2(700, 8));
            _bPercent = MakeText(_bootstrapPanel, "BPercent", "0%", 22, _dimTextColor);
            var pRT = _bPercent.rectTransform;
            pRT.anchorMin = new Vector2(0.5f, 0.13f);
            pRT.anchorMax = new Vector2(0.5f, 0.19f);
            pRT.sizeDelta = new Vector2(200, 30);
            pRT.anchoredPosition = Vector2.zero;
            _bPercent.alignment = TextAlignmentOptions.Center;

            // ── Transition Panel ──────────────────────────────────────────────
            _transitionPanel = MakePanel(canvasGO, "TransitionPanel");

            _tLoadingLabel = MakeText(_transitionPanel, "Label", "Cargando", 36, _dimTextColor);
            var lblRT = _tLoadingLabel.rectTransform;
            lblRT.anchorMin = new Vector2(0.5f, 0.55f);
            lblRT.anchorMax = new Vector2(0.5f, 0.62f);
            lblRT.sizeDelta = new Vector2(600, 50);
            lblRT.anchoredPosition = Vector2.zero;
            _tLoadingLabel.alignment = TextAlignmentOptions.Center;

            _tSceneName = MakeText(_transitionPanel, "SceneName", "", 52, _textColor);
            var snRT = _tSceneName.rectTransform;
            snRT.anchorMin = new Vector2(0.5f, 0.44f);
            snRT.anchorMax = new Vector2(0.5f, 0.54f);
            snRT.sizeDelta = new Vector2(800, 70);
            snRT.anchoredPosition = Vector2.zero;
            _tSceneName.alignment = TextAlignmentOptions.Center;

            _tBarFill = MakeProgressBar(_transitionPanel, "TBar",
                new Vector2(0.5f, 0.38f), new Vector2(700, 8));
            _tPercent = MakeText(_transitionPanel, "TPercent", "0%", 22, _dimTextColor);
            var tpRT = _tPercent.rectTransform;
            tpRT.anchorMin = new Vector2(0.5f, 0.31f);
            tpRT.anchorMax = new Vector2(0.5f, 0.37f);
            tpRT.sizeDelta = new Vector2(200, 30);
            tpRT.anchoredPosition = Vector2.zero;
            _tPercent.alignment = TextAlignmentOptions.Center;
        }

        // ── UI factory helpers ────────────────────────────────────────────────

        private static GameObject MakePanel(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            return go;
        }

        private Image MakeImage(GameObject parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var img  = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private TMP_Text MakeText(GameObject parent, string name, string text,
                                  float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.color     = color;
            tmp.enableWordWrapping = true;
            tmp.richText  = true;
            return tmp;
        }

        private Image MakeProgressBar(GameObject parent, string id,
                                      Vector2 anchorCenter, Vector2 size)
        {
            // Background track
            var bgGO  = new GameObject($"{id}_BG");
            bgGO.transform.SetParent(parent.transform, false);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = _barBgColor;
            var bgRT  = bgImg.rectTransform;
            bgRT.anchorMin = bgRT.anchorMax = anchorCenter;
            bgRT.sizeDelta = size;
            bgRT.anchoredPosition = Vector2.zero;

            // Fill
            var fillGO  = new GameObject($"{id}_Fill");
            fillGO.transform.SetParent(bgGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color     = _accentColor;
            fillImg.type      = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0f;
            var fillRT = fillImg.rectTransform;
            Stretch(fillRT);

            return fillImg;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
