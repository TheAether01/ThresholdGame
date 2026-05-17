// ============================================================================
// VirtualJoystick.cs — Mobile touch joystick for movement input
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Self-contained UI joystick built programmatically. No prefab needed.
// Supports both touch and mouse input for editor testing.
// ============================================================================

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Threshold.UI
{
    /// <summary>
    /// Virtual joystick for mobile movement input.
    /// Builds its own Canvas UI at runtime — just add this component to any GameObject.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Joystick Settings")]
        [Tooltip("Size of the joystick base in screen pixels.")]
        public float baseSize = 200f;

        [Tooltip("Size of the joystick knob relative to base (0-1).")]
        public float knobRatio = 0.45f;

        [Tooltip("Maximum distance the knob can move from center (0-1 of base radius).")]
        public float maxRange = 0.85f;

        [Tooltip("Dead zone threshold — input below this is zero (0-1).")]
        public float deadZone = 0.15f;

        [Header("Appearance")]
        public Color baseColor = new(1f, 1f, 1f, 0.15f);
        public Color knobColor = new(1f, 1f, 1f, 0.5f);
        public Color knobActiveColor = new(0.4f, 0.9f, 1f, 0.7f);

        [Header("Position")]
        [Tooltip("Offset from bottom-left corner.")]
        public Vector2 screenOffset = new(140f, 140f);

        [Tooltip("If true, joystick appears where finger touches.")]
        public bool floatingJoystick = false;

        // ====================================================================
        // Output
        // ====================================================================

        /// <summary>Current joystick input vector (x, y), magnitude 0-1.</summary>
        public Vector2 InputVector { get; private set; }

        /// <summary>True if the joystick is currently being touched.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Input as a world-space XZ direction vector.</summary>
        public Vector3 MoveDirection => new(InputVector.x, 0f, InputVector.y);

        // ====================================================================
        // Singleton
        // ====================================================================

        public static VirtualJoystick Instance { get; private set; }

        // ====================================================================
        // Internal References
        // ====================================================================

        private Canvas _canvas;
        private RectTransform _baseRect;
        private RectTransform _knobRect;
        private Image _baseImage;
        private Image _knobImage;
        private Vector2 _inputOrigin;

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
            // --- Canvas (find existing or create) ---
            _canvas = FindAnyObjectByType<Canvas>();
            if (_canvas == null)
            {
                var canvasObj = new GameObject("UI_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                canvasObj.AddComponent<CanvasScaler>().uiScaleMode =
                    CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasObj.GetComponent<CanvasScaler>().referenceResolution =
                    new Vector2(1080, 1920);
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Ensure EventSystem exists
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            // --- Touch capture area (left half of screen) ---
            var touchArea = CreateUIElement("Joystick_TouchArea", _canvas.transform);
            var touchRect = touchArea.GetComponent<RectTransform>();
            touchRect.anchorMin = new Vector2(0f, 0f);
            touchRect.anchorMax = new Vector2(0.5f, 0.5f);
            touchRect.offsetMin = Vector2.zero;
            touchRect.offsetMax = Vector2.zero;
            var touchImg = touchArea.AddComponent<Image>();
            touchImg.color = Color.clear; // Invisible but catches input
            touchImg.raycastTarget = true;

            // Copy event handlers to the touch area
            var proxy = touchArea.AddComponent<JoystickTouchProxy>();
            proxy.joystick = this;

            // --- Base circle ---
            var baseObj = CreateUIElement("Joystick_Base", _canvas.transform);
            _baseRect = baseObj.GetComponent<RectTransform>();
            _baseRect.sizeDelta = new Vector2(baseSize, baseSize);
            _baseImage = baseObj.AddComponent<Image>();
            _baseImage.color = baseColor;
            _baseImage.raycastTarget = false;

            // Position at bottom-left
            _baseRect.anchorMin = new Vector2(0f, 0f);
            _baseRect.anchorMax = new Vector2(0f, 0f);
            _baseRect.pivot = new Vector2(0.5f, 0.5f);
            _baseRect.anchoredPosition = screenOffset;

            // Make it circular (use sprite if available, or just round)
            MakeCircular(_baseImage);

            // --- Knob ---
            var knobObj = CreateUIElement("Joystick_Knob", _baseRect);
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
            // Create a simple white circle texture at runtime
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((radius - dist) * 2f); // Anti-alias edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();

            img.sprite = Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f));
        }

        // ====================================================================
        // Input Handling
        // ====================================================================

        public void OnPointerDown(PointerEventData eventData)
        {
            IsActive = true;

            if (floatingJoystick)
            {
                // Move base to touch position
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    eventData.position, null, out Vector2 localPos);
                _baseRect.anchoredPosition = localPos;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _baseRect, eventData.position, null, out _inputOrigin);

            ProcessInput(eventData);
            _knobImage.color = knobActiveColor;
        }

        public void OnDrag(PointerEventData eventData)
        {
            ProcessInput(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsActive = false;
            InputVector = Vector2.zero;
            _knobRect.anchoredPosition = Vector2.zero;
            _knobImage.color = knobColor;
        }

        private void ProcessInput(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _baseRect, eventData.position, null, out Vector2 localPoint);

            float radius = baseSize * 0.5f * maxRange;
            Vector2 delta = localPoint;

            // Clamp to max range
            if (delta.magnitude > radius)
                delta = delta.normalized * radius;

            // Normalize to 0-1
            InputVector = delta / radius;

            // M4: Apply dead zone with remapping to avoid sudden jump
            float mag = InputVector.magnitude;
            if (mag < deadZone)
            {
                InputVector = Vector2.zero;
            }
            else
            {
                // Remap from [deadZone, 1] to [0, 1]
                float remapped = (mag - deadZone) / (1f - deadZone);
                InputVector = InputVector.normalized * remapped;
            }

            // Move knob visual
            _knobRect.anchoredPosition = delta;
        }
    }

    // ========================================================================
    // Helper: Touch proxy to forward events to the joystick
    // ========================================================================

    /// <summary>
    /// Forwards pointer events from the touch area to the joystick.
    /// </summary>
    public class JoystickTouchProxy : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [HideInInspector] public VirtualJoystick joystick;

        public void OnPointerDown(PointerEventData e) => joystick?.OnPointerDown(e);
        public void OnDrag(PointerEventData e) => joystick?.OnDrag(e);
        public void OnPointerUp(PointerEventData e) => joystick?.OnPointerUp(e);
    }
}
