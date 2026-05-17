// ============================================================================
// RunSummaryScreen.cs — Between-runs summary overlay
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Shows run stats, director explanation, reward breakdown, unlocks,
// incentive message, and optional next-run challenge.
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Data container for the summary screen.
    /// Populated by the game loop before showing the screen.
    /// </summary>
    [Serializable]
    public class RunSummaryData
    {
        // Run stats
        public int roomsCleared;
        public int totalRooms;
        public int kills;
        public int shotsFired;
        public int shotsHit;
        public float timeSeconds;
        public bool won;

        // Director Agent output
        public string directorExplanation;  // What changed for next run

        // Reward Agent output
        public int baseXP;
        public int bonusXP;
        public string bonusReason;          // e.g. "Accuracy bonus: 85%+"
        public string incentiveMessage;     // e.g. "Try clearing with zero deaths!"
        public string unlockAnnouncement;   // e.g. "New weapon: Pulse Rifle" (empty if none)
        public string challengeText;        // Optional next-run challenge

        // Computed
        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired * 100f : 0f;
        public int TotalXP => baseXP + bonusXP;
    }

    /// <summary>
    /// Full-screen summary overlay shown between dungeon runs.
    /// Built programmatically — no prefab needed.
    /// </summary>
    public class RunSummaryScreen : MonoBehaviour
    {
        // ====================================================================
        // Events
        // ====================================================================

        /// <summary>Fired when the player taps "Continue" / "Next Run".</summary>
        public event Action OnContinue;

        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Colors")]
        public Color bgColor = new(0.02f, 0.04f, 0.08f, 0.95f);
        public Color headerColor = new(0.3f, 0.85f, 1f, 1f);
        public Color statColor = new(0.85f, 0.9f, 0.95f, 1f);
        public Color xpColor = new(1f, 0.85f, 0.2f, 1f);
        public Color accentColor = new(0.3f, 0.95f, 0.5f, 1f);
        public Color dimColor = new(0.5f, 0.55f, 0.65f, 0.9f);
        public Color winColor = new(0.3f, 1f, 0.5f, 1f);
        public Color loseColor = new(1f, 0.3f, 0.3f, 1f);

        // ====================================================================
        // Singleton
        // ====================================================================

        public static RunSummaryScreen Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;
        private GameObject _root;
        private CanvasGroup _canvasGroup;
        private RectTransform _contentPanel;

        // Text fields
        private Text _outcomeText;
        private Text _roomsText;
        private Text _killsText;
        private Text _accuracyText;
        private Text _timeText;
        private Text _directorText;
        private Text _baseXPText;
        private Text _bonusXPText;
        private Text _totalXPText;
        private Text _bonusReasonText;
        private Text _incentiveText;
        private Text _unlockText;
        private Text _challengeText;
        private GameObject _unlockSection;
        private GameObject _challengeSection;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            _canvas = FindAnyObjectByType<Canvas>();
            // M6: Auto-create canvas if none exists yet (script execution order safety)
            if (_canvas == null)
            {
                var canvasObj = new GameObject("UI_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                canvasObj.AddComponent<GraphicRaycaster>();
            }
            BuildUI();
            _root.SetActive(false);
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
            // Full-screen overlay root
            _root = new GameObject("RunSummary_Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Background
            var bgImg = _root.AddComponent<Image>();
            bgImg.color = bgColor;
            bgImg.raycastTarget = true;

            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Scrollable content container
            var scrollObj = new GameObject("Scroll", typeof(RectTransform));
            scrollObj.transform.SetParent(_root.transform, false);
            var scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.08f, 0.05f);
            scrollRect.anchorMax = new Vector2(0.92f, 0.95f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            // Content panel (vertical layout)
            _contentPanel = scrollObj.GetComponent<RectTransform>();

            float y = 0f;
            float spacing = 8f;

            // --- OUTCOME HEADER ---
            _outcomeText = AddText(ref y, "RUN COMPLETE", 32, headerColor, FontStyle.Bold);
            y += spacing;

            // --- DIVIDER ---
            AddDivider(ref y);
            y += spacing * 2;

            // --- STATS SECTION ---
            AddText(ref y, "STATS", 16, dimColor, FontStyle.Bold);
            y += 4f;
            _roomsText = AddText(ref y, "Rooms Cleared:  0 / 0", 20, statColor);
            _killsText = AddText(ref y, "Kills:  0", 20, statColor);
            _accuracyText = AddText(ref y, "Accuracy:  0%", 20, statColor);
            _timeText = AddText(ref y, "Time:  0:00", 20, statColor);
            y += spacing * 2;

            // --- DIVIDER ---
            AddDivider(ref y);
            y += spacing * 2;

            // --- REWARDS ---
            AddText(ref y, "REWARDS", 16, dimColor, FontStyle.Bold);
            y += 4f;
            _baseXPText = AddText(ref y, "Base XP:   +0", 20, xpColor);
            _bonusXPText = AddText(ref y, "Bonus XP:  +0", 20, xpColor);
            _bonusReasonText = AddText(ref y, "", 16, accentColor, FontStyle.Italic);
            y += 4f;
            _totalXPText = AddText(ref y, "TOTAL:  0 XP", 26, xpColor, FontStyle.Bold);
            y += spacing * 2;

            // --- UNLOCK (conditional) ---
            float unlockY = y;
            _unlockText = AddText(ref y, "", 20, accentColor, FontStyle.Bold);
            _unlockSection = _unlockText.gameObject;
            _unlockSection.SetActive(false);
            y += spacing;

            // --- DIVIDER ---
            AddDivider(ref y);
            y += spacing * 2;

            // --- DIRECTOR EXPLANATION ---
            AddText(ref y, "DIRECTOR NOTES", 16, dimColor, FontStyle.Bold);
            y += 4f;
            _directorText = AddText(ref y, "", 17, statColor);
            _directorText.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);
            y += 40f;

            // --- INCENTIVE ---
            _incentiveText = AddText(ref y, "", 18, accentColor, FontStyle.Italic);
            y += spacing * 2;

            // --- CHALLENGE (conditional) ---
            _challengeText = AddText(ref y, "", 18, headerColor, FontStyle.Bold);
            _challengeSection = _challengeText.gameObject;
            _challengeSection.SetActive(false);
            y += spacing * 2;

            // --- CONTINUE BUTTON ---
            BuildContinueButton(ref y);
        }

        private Text AddText(ref float y, string content, int fontSize,
            Color color, FontStyle style = FontStyle.Normal)
        {
            float height = fontSize + 12f;
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_contentPanel, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(0f, height);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleLeft;
            text.font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            y += height;
            return text;
        }

        private void AddDivider(ref float y)
        {
            var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_contentPanel, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(0f, 2f);
            go.GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.5f, 0.5f);
            y += 2f;
        }

        private void BuildContinueButton(ref float y)
        {
            var btnObj = new GameObject("ContinueBtn", typeof(RectTransform), typeof(Image));
            btnObj.transform.SetParent(_contentPanel, false);
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.2f, 1f);
            btnRect.anchorMax = new Vector2(0.8f, 1f);
            btnRect.pivot = new Vector2(0.5f, 1f);
            btnRect.anchoredPosition = new Vector2(0f, -y);
            btnRect.sizeDelta = new Vector2(0f, 60f);
            btnObj.GetComponent<Image>().color = new Color(0.2f, 0.6f, 1f, 0.9f);

            var btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                Hide();
                OnContinue?.Invoke();
            });

            // Button label
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(btnObj.transform, false);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelObj.GetComponent<Text>();
            label.text = "NEXT RUN ▶";
            label.fontSize = 24;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            y += 60f;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Populates and shows the summary screen with run data.
        /// </summary>
        public void Show(RunSummaryData data)
        {
            // Outcome
            _outcomeText.text = data.won ? "RUN COMPLETE ✓" : "RUN FAILED ✗";
            _outcomeText.color = data.won ? winColor : loseColor;

            // Stats
            _roomsText.text = $"Rooms Cleared:  {data.roomsCleared} / {data.totalRooms}";
            _killsText.text = $"Kills:  {data.kills}";
            _accuracyText.text = $"Accuracy:  {data.Accuracy:F1}%";

            int min = Mathf.FloorToInt(data.timeSeconds / 60f);
            int sec = Mathf.FloorToInt(data.timeSeconds % 60f);
            _timeText.text = $"Time:  {min}:{sec:D2}";

            // Rewards
            _baseXPText.text = $"Base XP:   +{data.baseXP}";
            _bonusXPText.text = $"Bonus XP:  +{data.bonusXP}";
            _bonusReasonText.text = string.IsNullOrEmpty(data.bonusReason)
                ? "" : $"  ({data.bonusReason})";
            _totalXPText.text = $"TOTAL:  {data.TotalXP} XP";

            // Director
            _directorText.text = string.IsNullOrEmpty(data.directorExplanation)
                ? "No adjustments." : data.directorExplanation;

            // Incentive
            _incentiveText.text = string.IsNullOrEmpty(data.incentiveMessage)
                ? "" : $"💡 {data.incentiveMessage}";

            // Unlock (conditional)
            bool hasUnlock = !string.IsNullOrEmpty(data.unlockAnnouncement);
            _unlockSection.SetActive(hasUnlock);
            if (hasUnlock)
                _unlockText.text = $"🔓 UNLOCKED: {data.unlockAnnouncement}";

            // Challenge (conditional)
            bool hasChallenge = !string.IsNullOrEmpty(data.challengeText);
            _challengeSection.SetActive(hasChallenge);
            if (hasChallenge)
                _challengeText.text = $"⚡ CHALLENGE: {data.challengeText}";

            _root.SetActive(true);
            StartCoroutine(FadeIn());
        }

        /// <summary>Hides the summary screen.</summary>
        public void Hide()
        {
            StartCoroutine(FadeOut());
        }

        // ====================================================================
        // Animation
        // ====================================================================

        private IEnumerator FadeIn()
        {
            float elapsed = 0f;
            while (elapsed < 0.4f)
            {
                _canvasGroup.alpha = elapsed / 0.4f;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _canvasGroup.alpha = 1f;
        }

        private IEnumerator FadeOut()
        {
            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                _canvasGroup.alpha = 1f - (elapsed / 0.3f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
        }
    }
}
