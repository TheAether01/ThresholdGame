// ============================================================================
// DamageIndicatorSystem.cs — Directional damage indicators (live-tracking with guide ring)
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Shows red arrow wedges on a radial ring centered EXACTLY on the player, pointing
// toward the source of incoming damage. Indicators LIVE-TRACK the attacker's
// world position every frame — as the player moves, rotates, or if the camera
// pans/shakes, the ring and arrows follow the player model perfectly in screen space.
//
// Includes a real-time adjustable guide ring so that game designers can
// easily visualize and align the indicator radius on the screen in the editor.
// All values are exposed as public inspector fields for dynamic tuning.
//
// Uses a pre-allocated pool of UI indicators with smooth fade-out.
// Fully procedural — no textures, prefabs, or scene references needed.
// Bootstrapped automatically by PlayerHealth.Awake().
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Directional damage indicator overlay. Shows red arrow wedges that
    /// live-track attacker world positions. Auto-creates its own Canvas,
    /// procedural arrow textures, and manages a pool of indicators.
    /// Exposes configuration variables to the Unity Inspector for real-time tuning.
    /// </summary>
    public class DamageIndicatorSystem : MonoBehaviour
    {
        // ====================================================================
        // Singleton
        // ====================================================================

        public static DamageIndicatorSystem Instance { get; private set; }

        // ====================================================================
        // Configuration (Exposed for real-time Inspector tuning)
        // ====================================================================

        [Header("Player Tracking")]
        [Tooltip("Height offset in world units to center the circle on the player's body rather than their feet.")]
        public float playerHeightOffset = 0.5f;

        [Header("Visual Tuning")]
        [Tooltip("Distance from screen center to place indicators (pixels at reference res).")]
        public float indicatorRadius = 220f;

        [Tooltip("How long the indicator stays visible before fully fading.")]
        public float indicatorLifetime = 1.2f;

        [Tooltip("Width of each arrow wedge (pixels).")]
        public float arrowWidth = 50f;

        [Tooltip("Height of each arrow wedge (pixels).")]
        public float arrowHeight = 70f;

        [Tooltip("How quickly the arrow rotation smoothly follows the recalculated angle (degrees/sec).")]
        public float rotationSmoothSpeed = 12f;

        [Header("Guide Ring Settings")]
        [Tooltip("Show a visual guide circle ring in-game to help tune/align the radius on-screen.")]
        public bool showGuideRing = true;

        [Tooltip("Color and transparency of the visual guide ring.")]
        public Color guideRingColor = new Color(1f, 1f, 1f, 0.2f);

        [Tooltip("How thick the guide ring line is relative to its overall size.")]
        [Range(0.005f, 0.1f)]
        public float guideRingThickness = 0.015f;

        // ====================================================================
        // Pool Settings
        // ====================================================================

        private const int PoolSize = 6;

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;
        private RectTransform _pivotRect;
        private IndicatorSlot[] _pool;
        private Texture2D _arrowTexture;
        private Texture2D _ringTexture;
        private RawImage _guideRingImage;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _arrowTexture = GenerateArrowTexture(64, 96);
            _ringTexture = GenerateRingTexture(256, guideRingThickness);
            CreateIndicatorUI();
        }

        private void Update()
        {
            var player = Player.PlayerController.Instance;
            if (player == null) return;

            Vector3 playerWorldPos = player.transform.position + Vector3.up * playerHeightOffset;

            // 1. Update Pivot Screen Position to track the player perfectly on screen
            Camera cam = Camera.main;
            if (cam != null && _canvas != null && _pivotRect != null)
            {
                Vector3 screenPoint = cam.WorldToScreenPoint(playerWorldPos);
                
                // Only position if the player is in front of the camera
                if (screenPoint.z > 0f)
                {
                    RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint))
                    {
                        _pivotRect.anchoredPosition = localPoint;
                    }
                }
            }

            // 2. Update the visual guide ring dynamically
            if (_guideRingImage != null)
            {
                _guideRingImage.gameObject.SetActive(showGuideRing);
                if (showGuideRing)
                {
                    _guideRingImage.rectTransform.sizeDelta = new Vector2(indicatorRadius * 2f, indicatorRadius * 2f);
                    _guideRingImage.color = guideRingColor;
                }
            }

            // 3. Update and live-track active arrows, and apply real-time visual configurations
            if (_pool != null)
            {
                foreach (var slot in _pool)
                {
                    // Always update position/size parameters in case they are adjusted in the Inspector
                    if (slot.ArrowImage != null)
                    {
                        var arrowRect = slot.ArrowImage.rectTransform;
                        arrowRect.anchoredPosition = new Vector2(0f, indicatorRadius);
                        arrowRect.sizeDelta = new Vector2(arrowWidth, arrowHeight);
                    }

                    if (!slot.IsActive) continue;

                    // Recalculate the threat angle based on current player + camera positions
                    float targetAngle = CalculateThreatAngle(player.transform.position, slot.AttackerWorldPos);

                    // Smoothly interpolate the current display angle toward the new target angle
                    float currentAngle = slot.CurrentAngle;
                    float delta = Mathf.DeltaAngle(currentAngle, targetAngle);
                    float newAngle = currentAngle + delta * Mathf.Min(1f, rotationSmoothSpeed * Time.unscaledDeltaTime);
                    slot.CurrentAngle = newAngle;

                    // Apply the updated rotation
                    slot.RootTransform.localRotation = Quaternion.Euler(0f, 0f, -newAngle);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_arrowTexture != null) Destroy(_arrowTexture);
            if (_ringTexture != null) Destroy(_ringTexture);
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Shows a directional indicator pointing toward the attacker's world position.
        /// The indicator will live-track this position until it fades out.
        /// </summary>
        public void ShowIndicator(Vector3 attackerWorldPos)
        {
            // Find the player
            var player = Player.PlayerController.Instance;
            if (player == null) return;

            // Calculate initial threat angle relative to camera forward (screen space)
            float angle = CalculateThreatAngle(player.transform.position, attackerWorldPos);

            // Try to merge with an existing active indicator tracking a nearby attacker
            // (avoids stacking 3 arrows on top of each other from the same enemy)
            foreach (var slot in _pool)
            {
                if (slot.IsActive)
                {
                    // Check if this indicator is tracking a position close to the new attacker
                    float dist = Vector3.Distance(slot.AttackerWorldPos, attackerWorldPos);
                    if (dist < 3f)
                    {
                        // Refresh this indicator with the updated attacker position
                        slot.Refresh(attackerWorldPos, angle);
                        return;
                    }
                }
            }

            // Find an inactive slot
            foreach (var slot in _pool)
            {
                if (!slot.IsActive)
                {
                    slot.Activate(attackerWorldPos, angle);
                    return;
                }
            }

            // All slots active — recycle the oldest one
            float oldestTime = float.MaxValue;
            IndicatorSlot oldest = _pool[0];
            foreach (var slot in _pool)
            {
                if (slot.ActivationTime < oldestTime)
                {
                    oldestTime = slot.ActivationTime;
                    oldest = slot;
                }
            }
            oldest.Activate(attackerWorldPos, angle);
        }

        // ====================================================================
        // Angle Calculation
        // ====================================================================

        /// <summary>
        /// Calculates the angle (in degrees) from screen-up to the threat direction,
        /// measured relative to the camera's forward vector projected onto the XZ plane.
        /// </summary>
        private float CalculateThreatAngle(Vector3 playerPos, Vector3 attackerPos)
        {
            // Threat direction on the XZ plane
            Vector3 threatDir = attackerPos - playerPos;
            threatDir.y = 0f;
            if (threatDir.sqrMagnitude < 0.01f)
                return 0f;
            threatDir.Normalize();

            // Camera forward on the XZ plane
            Camera cam = Camera.main;
            Vector3 camForward = cam != null ? cam.transform.forward : Vector3.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude < 0.01f)
                camForward = Vector3.forward;
            camForward.Normalize();

            // Signed angle: positive = clockwise when viewed from above
            float angle = Vector3.SignedAngle(camForward, threatDir, Vector3.up);

            return angle;
        }

        // ====================================================================
        // UI Construction (Procedural)
        // ====================================================================

        private void CreateIndicatorUI()
        {
            // Create overlay canvas
            var canvasObj = new GameObject("DamageIndicatorCanvas");
            canvasObj.transform.SetParent(transform);

            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 997; // Below DamageVignette (998)

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // Prevent blocking input
            var cg = canvasObj.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Center pivot container (repositioned to player screen position in Update)
            var pivotObj = new GameObject("IndicatorPivot", typeof(RectTransform));
            pivotObj.transform.SetParent(canvasObj.transform, false);
            _pivotRect = pivotObj.GetComponent<RectTransform>();
            _pivotRect.anchorMin = _pivotRect.anchorMax = new Vector2(0.5f, 0.5f);
            _pivotRect.anchoredPosition = Vector2.zero;
            _pivotRect.sizeDelta = Vector2.zero;

            // Create Visual Guide Ring
            var ringObj = new GameObject("VisualGuideRing", typeof(RectTransform), typeof(RawImage));
            ringObj.transform.SetParent(_pivotRect.transform, false);
            var ringRect = ringObj.GetComponent<RectTransform>();
            ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringRect.anchoredPosition = Vector2.zero;
            ringRect.sizeDelta = new Vector2(indicatorRadius * 2f, indicatorRadius * 2f);

            _guideRingImage = ringObj.GetComponent<RawImage>();
            _guideRingImage.texture = _ringTexture;
            _guideRingImage.color = guideRingColor;
            _guideRingImage.raycastTarget = false;
            ringObj.SetActive(showGuideRing);

            // Create pool of indicator arrows
            _pool = new IndicatorSlot[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                _pool[i] = CreateIndicatorSlot(_pivotRect.transform, i);
            }
        }

        private IndicatorSlot CreateIndicatorSlot(Transform parent, int index)
        {
            // Root object — rotates around center to point at threat
            var rootObj = new GameObject($"Indicator_{index}", typeof(RectTransform));
            rootObj.transform.SetParent(parent, false);
            var rootRect = rootObj.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;

            // Arrow image — offset upward from center by indicatorRadius
            var arrowObj = new GameObject($"Arrow_{index}", typeof(RectTransform), typeof(RawImage));
            arrowObj.transform.SetParent(rootObj.transform, false);

            var arrowRect = arrowObj.GetComponent<RectTransform>();
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            // Place arrow above center (pointing "up" = toward the threat when root is rotated)
            arrowRect.anchoredPosition = new Vector2(0f, indicatorRadius);
            arrowRect.sizeDelta = new Vector2(arrowWidth, arrowHeight);

            var arrowImage = arrowObj.GetComponent<RawImage>();
            arrowImage.texture = _arrowTexture;
            arrowImage.color = new Color(1f, 0.15f, 0.08f, 0f); // Red, starts invisible
            arrowImage.raycastTarget = false;

            // Add a CanvasGroup for smooth alpha control
            var canvasGroup = arrowObj.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var slot = new IndicatorSlot
            {
                RootTransform = rootRect,
                ArrowImage = arrowImage,
                CanvasGroup = canvasGroup,
                IsActive = false,
                AttackerWorldPos = Vector3.zero,
                Owner = this
            };

            return slot;
        }

        // ====================================================================
        // Procedural Textures
        // ====================================================================

        /// <summary>
        /// Generates a pointed arrow/chevron texture pointing upward.
        /// The shape is a solid triangle (chevron) with soft edges.
        /// </summary>
        private Texture2D GenerateArrowTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float hw = width * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x - hw) / hw;
                    float ny = (float)y / height;

                    float edgeWidth = 1f - ny;

                    float alpha = 0f;
                    if (Mathf.Abs(nx) < edgeWidth)
                    {
                        alpha = 1f;

                        float innerStart = 0.35f;
                        float innerNy = (ny - innerStart) / (1f - innerStart);
                        if (innerNy > 0f)
                        {
                            float innerEdge = (1f - innerNy) * 0.55f;
                            if (Mathf.Abs(nx) < innerEdge)
                            {
                                alpha = 0f;
                            }
                        }

                        float distToEdge = edgeWidth - Mathf.Abs(nx);
                        float softness = 0.06f;
                        if (distToEdge < softness)
                        {
                            alpha *= distToEdge / softness;
                        }
                    }

                    alpha = Mathf.Clamp01(alpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Generates a procedural circular ring texture with anti-aliasing.
        /// </summary>
        private Texture2D GenerateRingTexture(int size, float thicknessPercent)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float center = size * 0.5f;
            float outerRadius = center - 2f;
            float innerRadius = center * (1f - thicknessPercent * 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = 0f;
                    if (dist <= outerRadius && dist >= innerRadius)
                    {
                        alpha = 1f;

                        // Antialiasing edges
                        float distToOuter = outerRadius - dist;
                        float distToInner = dist - innerRadius;
                        float softness = 1.5f;

                        if (distToOuter < softness)
                            alpha *= distToOuter / softness;
                        else if (distToInner < softness)
                            alpha *= distToInner / softness;
                    }

                    alpha = Mathf.Clamp01(alpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }

        // ====================================================================
        // Indicator Slot (pooled instance)
        // ====================================================================

        private class IndicatorSlot
        {
            public RectTransform RootTransform;
            public RawImage ArrowImage;
            public CanvasGroup CanvasGroup;
            public bool IsActive;
            public float CurrentAngle;
            public float ActivationTime;
            public Vector3 AttackerWorldPos;
            public DamageIndicatorSystem Owner;

            private Coroutine _fadeCoroutine;

            /// <summary>
            /// Activates this indicator to live-track the given attacker position.
            /// </summary>
            public void Activate(Vector3 attackerPos, float initialAngle)
            {
                IsActive = true;
                AttackerWorldPos = attackerPos;
                CurrentAngle = initialAngle;
                ActivationTime = Time.unscaledTime;

                RootTransform.localRotation = Quaternion.Euler(0f, 0f, -initialAngle);

                ArrowImage.color = new Color(1f, 0.15f, 0.08f, 1f);
                CanvasGroup.alpha = 1f;

                if (_fadeCoroutine != null)
                    Owner.StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = Owner.StartCoroutine(FadeCoroutine());
            }

            /// <summary>
            /// Refreshes an active indicator (resets fade timer, updates attacker position).
            /// </summary>
            public void Refresh(Vector3 attackerPos, float currentAngle)
            {
                AttackerWorldPos = attackerPos;
                CurrentAngle = currentAngle;
                ActivationTime = Time.unscaledTime;

                RootTransform.localRotation = Quaternion.Euler(0f, 0f, -currentAngle);
                ArrowImage.color = new Color(1f, 0.15f, 0.08f, 1f);
                CanvasGroup.alpha = 1f;

                if (_fadeCoroutine != null)
                    Owner.StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = Owner.StartCoroutine(FadeCoroutine());
            }

            private IEnumerator FadeCoroutine()
            {
                float holdTime = Owner.indicatorLifetime * 0.25f;
                yield return new WaitForSecondsRealtime(holdTime);

                float fadeTime = Owner.indicatorLifetime * 0.75f;
                float elapsed = 0f;

                while (elapsed < fadeTime)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = elapsed / fadeTime;

                    float alpha = 1f - (t * t);
                    CanvasGroup.alpha = alpha;

                    yield return null;
                }

                CanvasGroup.alpha = 0f;
                IsActive = false;
                _fadeCoroutine = null;
            }
        }
    }
}
