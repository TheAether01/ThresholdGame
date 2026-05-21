// ============================================================================
// AimJoystick.cs — Right-side analogue stick for aim/shoot input
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Mirrors VirtualJoystick but for the right side. Touching = firing.
// Supports both editor pre-built UI (serialized refs) and runtime self-build.
// ============================================================================

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Right-side analogue stick for aim/shoot input.
    /// Touching the stick = firing. Drag direction = aim direction.
    /// </summary>
    public class AimJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Joystick Settings")]
        public float baseSize = 200f;
        public float knobRatio = 0.45f;
        public float maxRange = 0.85f;
        public float deadZone = 0.15f;

        [Header("Appearance")]
        public Color baseColor = new(1f, 1f, 1f, 0.15f);
        public Color knobColor = new(0.95f, 0.25f, 0.25f, 0.6f);
        public Color knobActiveColor = new(1f, 0.5f, 0.2f, 0.9f);

        [Header("Position")]
        public Vector2 screenOffset = new(-140f, 140f);

        [Header("Pre-built UI References (set by editor)")]
        [SerializeField] private RectTransform _baseRect;
        [SerializeField] private RectTransform _knobRect;
        [SerializeField] private Image _baseImage;
        [SerializeField] private Image _knobImage;

        // ====================================================================
        // Output
        // ====================================================================

        /// <summary>Current aim input vector (x, y), magnitude 0-1.</summary>
        public Vector2 AimVector { get; private set; }

        /// <summary>True if the stick is currently being touched (= firing).</summary>
        public bool IsAiming { get; private set; }

        /// <summary>Aim as a world-space XZ direction vector.</summary>
        public Vector3 AimDirection => new(AimVector.x, 0f, AimVector.y);

        // ====================================================================
        // Singleton
        // ====================================================================

        public static AimJoystick Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private Canvas _canvas;

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
            if (_baseRect == null) BuildUI();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // UI Construction (only if not pre-built by editor)
        // ====================================================================

        private void BuildUI()
        {
            _canvas = FindAnyObjectByType<Canvas>();
            if (_canvas == null) return;

            // Touch area — right half of screen
            var touchArea = CreateUIElement("AimStick_TouchArea", _canvas.transform);
            var touchRect = touchArea.GetComponent<RectTransform>();
            touchRect.anchorMin = new Vector2(0.5f, 0f);
            touchRect.anchorMax = new Vector2(1f, 0.5f);
            touchRect.offsetMin = Vector2.zero;
            touchRect.offsetMax = Vector2.zero;
            var touchImg = touchArea.AddComponent<Image>();
            touchImg.color = Color.clear;
            touchImg.raycastTarget = true;
            var proxy = touchArea.AddComponent<AimTouchProxy>();
            proxy.aimJoystick = this;

            // Base circle
            var baseObj = CreateUIElement("AimStick_Outer", _canvas.transform);
            _baseRect = baseObj.GetComponent<RectTransform>();
            _baseRect.sizeDelta = new Vector2(baseSize, baseSize);
            _baseImage = baseObj.AddComponent<Image>();
            _baseImage.color = baseColor;
            _baseImage.raycastTarget = false;
            _baseRect.anchorMin = new Vector2(1f, 0f);
            _baseRect.anchorMax = new Vector2(1f, 0f);
            _baseRect.pivot = new Vector2(0.5f, 0.5f);
            _baseRect.anchoredPosition = screenOffset;
            MakeCircular(_baseImage);

            // Knob
            var knobObj = CreateUIElement("AimStick_Inner", _baseRect);
            _knobRect = knobObj.GetComponent<RectTransform>();
            float knobSize = baseSize * knobRatio;
            _knobRect.sizeDelta = new Vector2(knobSize, knobSize);
            _knobRect.anchoredPosition = Vector2.zero;
            _knobImage = knobObj.AddComponent<Image>();
            _knobImage.color = knobColor;
            _knobImage.raycastTarget = false;
            MakeCircular(_knobImage);
        }

        private GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private void MakeCircular(Image img)
        {
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((radius - dist) * 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            tex.Apply();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // ====================================================================
        // Input Handling
        // ====================================================================

        public void OnPointerDown(PointerEventData eventData)
        {
            IsAiming = true;
            ProcessInput(eventData);
            if (_knobImage != null) _knobImage.color = knobActiveColor;
        }

        public void OnDrag(PointerEventData eventData)
        {
            ProcessInput(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsAiming = false;
            AimVector = Vector2.zero;
            if (_knobRect != null) _knobRect.anchoredPosition = Vector2.zero;
            if (_knobImage != null) _knobImage.color = knobColor;
        }

        private void ProcessInput(PointerEventData eventData)
        {
            if (_baseRect == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _baseRect, eventData.position, null, out Vector2 localPoint);

            float radius = baseSize * 0.5f * maxRange;
            Vector2 delta = localPoint;
            if (delta.magnitude > radius)
                delta = delta.normalized * radius;

            AimVector = delta / radius;

            float mag = AimVector.magnitude;
            if (mag < deadZone)
                AimVector = Vector2.zero;
            else
            {
                float remapped = (mag - deadZone) / (1f - deadZone);
                AimVector = AimVector.normalized * remapped;
            }

            if (_knobRect != null) _knobRect.anchoredPosition = delta;
        }
    }

    // ========================================================================
    // Helper: Touch proxy for aim joystick
    // ========================================================================

    public class AimTouchProxy : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [HideInInspector] public AimJoystick aimJoystick;

        public void OnPointerDown(PointerEventData e) => aimJoystick?.OnPointerDown(e);
        public void OnDrag(PointerEventData e) => aimJoystick?.OnDrag(e);
        public void OnPointerUp(PointerEventData e) => aimJoystick?.OnPointerUp(e);
    }
}
