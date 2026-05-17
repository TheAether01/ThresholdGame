// ============================================================================
// DefectionPopup.cs — NPC defection notification UI
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Shows a dramatic popup when an NPC defects to the player's side.
// Auto-dismisses after a configurable duration with slide + fade animation.
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Animated popup shown when an NPC defects.
    /// Call Show("NPC-04", "GRUNT") from any system.
    /// </summary>
    public class DefectionPopup : MonoBehaviour
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Timing")]
        public float displayDuration = 3.5f;
        public float fadeInDuration = 0.3f;
        public float fadeOutDuration = 0.5f;

        [Header("Appearance")]
        public Color panelColor = new(0.05f, 0.15f, 0.3f, 0.9f);
        public Color accentColor = new(0.3f, 0.9f, 0.5f, 1f);
        public Color textColor = new(0.9f, 0.95f, 1f, 1f);

        // ====================================================================
        // Singleton
        // ====================================================================

        public static DefectionPopup Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;
        private GameObject _popupRoot;
        private RectTransform _popupRect;
        private CanvasGroup _canvasGroup;
        private Text _titleText;
        private Text _nameText;
        private Text _subtitleText;
        private Image _accentBar;
        private bool _isShowing;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            _canvas = FindAnyObjectByType<Canvas>();
            BuildUI();
            _popupRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // UI Construction
        // ====================================================================

        private void BuildUI()
        {
            // Root panel
            _popupRoot = new GameObject("DefectionPopup_Root", typeof(RectTransform));
            _popupRoot.transform.SetParent(_canvas.transform, false);

            _popupRect = _popupRoot.GetComponent<RectTransform>();
            _popupRect.anchorMin = new Vector2(0.5f, 0.65f);
            _popupRect.anchorMax = new Vector2(0.5f, 0.65f);
            _popupRect.pivot = new Vector2(0.5f, 0.5f);
            _popupRect.sizeDelta = new Vector2(500f, 120f);

            _canvasGroup = _popupRoot.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            // Background panel
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_popupRoot.transform, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = panelColor;

            // Accent bar (left edge)
            var bar = new GameObject("AccentBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_popupRoot.transform, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(6f, 0f);
            _accentBar = bar.GetComponent<Image>();
            _accentBar.color = accentColor;

            // "DEFECTION" title
            var titleObj = CreateText("Title", _popupRoot.transform,
                "⚑ DEFECTION", 16, accentColor, TextAnchor.MiddleLeft);
            var titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.65f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-12f, -8f);
            _titleText = titleObj.GetComponent<Text>();

            // NPC name (large)
            var nameObj = CreateText("NPCName", _popupRoot.transform,
                "NPC-04 has joined your side", 22, textColor, TextAnchor.MiddleLeft);
            var nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.25f);
            nameRect.anchorMax = new Vector2(1f, 0.7f);
            nameRect.offsetMin = new Vector2(20f, 0f);
            nameRect.offsetMax = new Vector2(-12f, 0f);
            _nameText = nameObj.GetComponent<Text>();
            _nameText.fontStyle = FontStyle.Bold;

            // Subtitle
            var subObj = CreateText("Subtitle", _popupRoot.transform,
                "They will fight alongside you", 14,
                new Color(0.6f, 0.7f, 0.8f, 0.9f), TextAnchor.MiddleLeft);
            var subRect = subObj.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0.3f);
            subRect.offsetMin = new Vector2(20f, 6f);
            subRect.offsetMax = new Vector2(-12f, 0f);
            _subtitleText = subObj.GetComponent<Text>();
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

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Shows the defection popup with NPC info.
        /// </summary>
        /// <param name="npcId">NPC identifier (e.g. "NPC-04")</param>
        /// <param name="archetype">NPC type (e.g. "GRUNT", "FLANKER")</param>
        public void Show(string npcId, string archetype = "")
        {
            if (_isShowing) StopAllCoroutines();

            _nameText.text = $"{npcId} has joined your side";
            _subtitleText.text = string.IsNullOrEmpty(archetype)
                ? "They will fight alongside you"
                : $"{archetype} defector — now your ally";

            _popupRoot.SetActive(true);
            StartCoroutine(AnimatePopup());

            Debug.Log($"[DefectionPopup] {npcId} ({archetype}) defected!");
        }

        // ====================================================================
        // Animation
        // ====================================================================

        private IEnumerator AnimatePopup()
        {
            _isShowing = true;

            // Slide in from right + fade in
            float elapsed = 0f;
            Vector2 startPos = new(200f, 0f);
            Vector2 endPos = Vector2.zero;

            while (elapsed < fadeInDuration)
            {
                float t = elapsed / fadeInDuration;
                float ease = 1f - Mathf.Pow(1f - t, 3f); // Ease-out cubic
                _canvasGroup.alpha = ease;
                _popupRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _canvasGroup.alpha = 1f;
            _popupRect.anchoredPosition = endPos;

            // Hold (M2: use WaitForSecondsRealtime so popup works at timeScale=0)
            yield return new WaitForSecondsRealtime(displayDuration);

            // Fade out
            elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                float t = elapsed / fadeOutDuration;
                _canvasGroup.alpha = 1f - t;
                _popupRect.anchoredPosition = new Vector2(-50f * t, 0f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _canvasGroup.alpha = 0f;
            _popupRoot.SetActive(false);
            _isShowing = false;
        }
    }
}
