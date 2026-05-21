// ============================================================================
// PauseScreen.cs — In-game pause menu overlay
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Full-screen pause overlay with Resume, Restart, and Quit buttons.
// Self-building UI: all elements are created programmatically at runtime
// unless pre-wired by the editor tool (ThresholdUIBuilder).
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Pause screen overlay — freezes gameplay (Time.timeScale = 0) and
    /// presents Resume / Restart / Quit options.
    /// </summary>
    public class PauseScreen : MonoBehaviour
    {
        // ====================================================================
        // Events
        // ====================================================================

        /// <summary>Fired when the player taps Resume.</summary>
        public event Action OnResume;

        /// <summary>Fired when the player taps Restart.</summary>
        public event Action OnRestart;

        /// <summary>Fired when the player taps Quit.</summary>
        public event Action OnQuit;

        // ====================================================================
        // Singleton
        // ====================================================================

        public static PauseScreen Instance { get; private set; }

        // ====================================================================
        // State
        // ====================================================================

        /// <summary>True while the pause screen is visible.</summary>
        public bool IsPaused { get; private set; }

        // ====================================================================
        // Serialized References (editor tool can pre-assign these)
        // ====================================================================

        [SerializeField] private GameObject _root;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _quitButton;

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;
        private float _previousTimeScale = 1f;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            BuildUI();

            // Wire button callbacks
            if (_resumeButton != null) _resumeButton.onClick.AddListener(HandleResume);
            if (_restartButton != null) _restartButton.onClick.AddListener(HandleRestart);
            if (_quitButton != null) _quitButton.onClick.AddListener(HandleQuit);

            // Start hidden
            if (_root != null) _root.SetActive(false);
            IsPaused = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Opens the pause screen and freezes gameplay.
        /// </summary>
        public void Pause()
        {
            if (IsPaused) return;

            IsPaused = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (_root != null) _root.SetActive(true);
            if (_canvasGroup != null) { _canvasGroup.alpha = 1f; _canvasGroup.blocksRaycasts = true; }

            Debug.Log("[ThresholdUI] Game paused.");
        }

        /// <summary>
        /// Closes the pause screen and resumes gameplay.
        /// </summary>
        public void Resume()
        {
            if (!IsPaused) return;

            IsPaused = false;
            Time.timeScale = _previousTimeScale;

            if (_root != null) _root.SetActive(false);
            if (_canvasGroup != null) { _canvasGroup.alpha = 0f; _canvasGroup.blocksRaycasts = false; }

            Debug.Log("[ThresholdUI] Game resumed.");
        }

        // ====================================================================
        // Button Handlers
        // ====================================================================

        private void HandleResume()
        {
            Resume();
            OnResume?.Invoke();
        }

        private void HandleRestart()
        {
            // Unpause first so the game loop can run
            IsPaused = false;
            Time.timeScale = 1f;
            if (_root != null) _root.SetActive(false);

            OnRestart?.Invoke();

            Debug.Log("[ThresholdUI] Restarting...");

            // Reload the current scene
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private void HandleQuit()
        {
            OnQuit?.Invoke();

            Debug.Log("[ThresholdUI] Quit requested.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ====================================================================
        // UI Construction (self-building fallback)
        // ====================================================================

        private void BuildUI()
        {
            // Skip if already wired by editor tool
            if (_root != null) return;

            _canvas = GetOrCreateCanvas();

            // ── Root overlay ──
            var rootObj = CreatePanel("PauseScreen_Root", _canvas.transform);
            var rootRect = rootObj.GetComponent<RectTransform>();
            StretchFull(rootRect);
            rootObj.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.06f, 0.85f);
            _canvasGroup = rootObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;
            _root = rootObj;

            // ── Title ──
            var title = CreateText("Pause_Title", rootObj.transform, "PAUSED", 48,
                new Color(0.95f, 0.95f, 1f, 1f), TextAnchor.MiddleCenter);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0.62f);
            titleRect.anchorMax = new Vector2(0.9f, 0.78f);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
            title.GetComponent<Text>().fontStyle = FontStyle.Bold;

            // ── Subtitle ──
            var sub = CreateText("Pause_Sub", rootObj.transform, "— THRESHOLD —", 18,
                new Color(0.5f, 0.6f, 0.8f, 0.7f), TextAnchor.MiddleCenter);
            var subRect = sub.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.15f, 0.57f);
            subRect.anchorMax = new Vector2(0.85f, 0.63f);
            subRect.offsetMin = subRect.offsetMax = Vector2.zero;

            // ── Buttons ──
            float btnWidth = 320f;
            float btnHeight = 70f;
            float spacing = 20f;
            float startY = 0.50f; // anchor center for first button

            _resumeButton = CreateButton("Btn_Resume", rootObj.transform, "▶  RESUME",
                new Color(0.2f, 0.75f, 0.4f, 1f), new Color(0.15f, 0.6f, 0.3f, 1f),
                btnWidth, btnHeight, 0.5f, startY);

            _restartButton = CreateButton("Btn_Restart", rootObj.transform, "↺  RESTART",
                new Color(0.85f, 0.65f, 0.15f, 1f), new Color(0.7f, 0.5f, 0.1f, 1f),
                btnWidth, btnHeight, 0.5f, startY - 0.08f);

            _quitButton = CreateButton("Btn_Quit", rootObj.transform, "✕  QUIT",
                new Color(0.85f, 0.2f, 0.2f, 1f), new Color(0.65f, 0.15f, 0.15f, 1f),
                btnWidth, btnHeight, 0.5f, startY - 0.16f);
        }

        // ====================================================================
        // UI Helpers
        // ====================================================================

        private Canvas GetOrCreateCanvas()
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null) return canvas;

            var obj = new GameObject("UI_Canvas");
            canvas = obj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = obj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            obj.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            return go;
        }

        private GameObject CreateText(string name, Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return go;
        }

        private Button CreateButton(string name, Transform parent, string label,
            Color bgColor, Color pressedColor, float width, float height,
            float anchorX, float anchorY)
        {
            // Button container
            var btnObj = CreatePanel(name, parent);
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = btnRect.anchorMax = new Vector2(anchorX, anchorY);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta = new Vector2(width, height);
            btnRect.anchoredPosition = Vector2.zero;

            var btnImage = btnObj.GetComponent<Image>();
            btnImage.color = bgColor;

            var btn = btnObj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            btn.colors = colors;
            btn.targetGraphic = btnImage;

            // Label
            var labelObj = CreateText(name + "_Label", btnObj.transform, label,
                24, Color.white, TextAnchor.MiddleCenter);
            var labelRect = labelObj.GetComponent<RectTransform>();
            StretchFull(labelRect);
            labelObj.GetComponent<Text>().fontStyle = FontStyle.Bold;

            return btn;
        }

        private void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
