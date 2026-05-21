// ============================================================================
// DamageVignette.cs — Screen-edge red vignette for low-health feedback
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Creates a full-screen radial gradient overlay at runtime (no texture needed).
// Flash() for one-shot hits, SetDanger() for persistent low-health pulsing.
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Runtime red vignette overlay for damage/low-health feedback.
    /// Auto-creates its own Canvas + Image with a procedural radial gradient.
    /// </summary>
    public class DamageVignette : MonoBehaviour
    {
        // ====================================================================
        // Singleton
        // ====================================================================

        public static DamageVignette Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;
        private RawImage _vignetteImage;
        private CanvasGroup _canvasGroup;
        private Coroutine _flashCoroutine;
        private Coroutine _dangerCoroutine;
        private float _dangerIntensity;
        private bool _inDanger;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            CreateVignetteUI();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Brief red flash — used on low-health hits.
        /// </summary>
        public void Flash(float intensity = 0.6f, float duration = 0.3f)
        {
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine(intensity, duration));
        }

        /// <summary>
        /// Set persistent danger state. When active, vignette pulses.
        /// Call with intensity 0 to disable.
        /// </summary>
        public void SetDanger(float intensity)
        {
            _dangerIntensity = Mathf.Clamp01(intensity);
            bool shouldPulse = _dangerIntensity > 0.01f;

            if (shouldPulse && !_inDanger)
            {
                _inDanger = true;
                if (_dangerCoroutine != null)
                    StopCoroutine(_dangerCoroutine);
                _dangerCoroutine = StartCoroutine(DangerPulseCoroutine());
            }
            else if (!shouldPulse && _inDanger)
            {
                _inDanger = false;
                if (_dangerCoroutine != null)
                {
                    StopCoroutine(_dangerCoroutine);
                    _dangerCoroutine = null;
                }
                if (_canvasGroup != null)
                    _canvasGroup.alpha = 0f;
            }
        }

        // ====================================================================
        // Coroutines
        // ====================================================================

        private IEnumerator FlashCoroutine(float intensity, float duration)
        {
            // Quick flash in, slow fade out
            float fadeInTime = duration * 0.15f;
            float fadeOutTime = duration * 0.85f;

            // Fade in
            float elapsed = 0f;
            while (elapsed < fadeInTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(0f, intensity, elapsed / fadeInTime);
                yield return null;
            }

            // Fade out
            elapsed = 0f;
            float startAlpha = _canvasGroup.alpha;
            float endAlpha = _inDanger ? _dangerIntensity * 0.15f : 0f;
            while (elapsed < fadeOutTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / fadeOutTime);
                yield return null;
            }
            _canvasGroup.alpha = endAlpha;

            _flashCoroutine = null;
        }

        private IEnumerator DangerPulseCoroutine()
        {
            while (_inDanger)
            {
                // Slow sine pulse between 0 and dangerIntensity * 0.25
                float pulse = (Mathf.Sin(Time.time * 3f) * 0.5f + 0.5f) * _dangerIntensity * 0.25f;

                // Only set if not mid-flash (flash takes priority)
                if (_flashCoroutine == null)
                    _canvasGroup.alpha = pulse;

                yield return null;
            }

            _dangerCoroutine = null;
        }

        // ====================================================================
        // Setup — Procedural UI
        // ====================================================================

        private void CreateVignetteUI()
        {
            // Create overlay canvas
            var canvasObj = new GameObject("DamageVignetteCanvas");
            canvasObj.transform.SetParent(transform);

            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 998; // Just below GameOverlay (999)

            canvasObj.AddComponent<CanvasScaler>();

            _canvasGroup = canvasObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            // Create full-screen image with radial gradient texture
            var imgObj = new GameObject("VignetteImage");
            imgObj.transform.SetParent(canvasObj.transform, false);

            _vignetteImage = imgObj.AddComponent<RawImage>();
            _vignetteImage.texture = GenerateVignetteTexture(256);
            _vignetteImage.color = Color.white;
            _vignetteImage.raycastTarget = false;

            // Stretch to fill screen
            var rect = _vignetteImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private Texture2D GenerateVignetteTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float halfSize = size * 0.5f;
            Color vignetteColor = new Color(0.8f, 0.05f, 0.02f); // Deep red

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from center (0 at center, 1 at corners)
                    float dx = (x - halfSize) / halfSize;
                    float dy = (y - halfSize) / halfSize;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Vignette curve: transparent center, opaque edges
                    // Start fading in at dist 0.5, fully opaque at dist 1.2+
                    float alpha = Mathf.SmoothStep(0f, 1f, (dist - 0.4f) / 0.7f);
                    alpha = Mathf.Clamp01(alpha);

                    tex.SetPixel(x, y, new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, alpha));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
