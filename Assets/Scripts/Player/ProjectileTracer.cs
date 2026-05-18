// ============================================================================
// ProjectileTracer.cs — Lightweight hitscan tracer visual
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Spawns a thin LineRenderer from origin to hit point that fades out.
// Purely visual — all damage is handled by raycast (hitscan).
// Call ProjectileTracer.Spawn() from any weapon system.
// ============================================================================

using UnityEngine;

namespace Threshold.Player
{
    /// <summary>
    /// Visual tracer line effect for hitscan weapons.
    /// Self-destructs after fade duration. No physics.
    /// </summary>
    public class ProjectileTracer : MonoBehaviour
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Tracer Settings")]
        [SerializeField] private float fadeDuration = 0.1f;
        [SerializeField] private float startWidth = 0.06f;
        [SerializeField] private float endWidth = 0.02f;

        // ====================================================================
        // Internal
        // ====================================================================

        private LineRenderer _lineRenderer;
        private float _spawnTime;
        private Color _baseColor;

        // ====================================================================
        // Static Factory
        // ====================================================================

        /// <summary>
        /// Spawns a tracer line from origin to target with the given color.
        /// </summary>
        public static ProjectileTracer Spawn(Vector3 origin, Vector3 target, Color color)
        {
            var go = new GameObject("Tracer");
            var tracer = go.AddComponent<ProjectileTracer>();
            tracer.Initialize(origin, target, color);
            return tracer;
        }

        /// <summary>
        /// Spawns a tracer with a default color.
        /// </summary>
        public static ProjectileTracer Spawn(Vector3 origin, Vector3 target)
        {
            return Spawn(origin, target, new Color(0.3f, 0.9f, 1f, 0.8f)); // Cyan
        }

        // ====================================================================
        // Setup
        // ====================================================================

        private void Initialize(Vector3 origin, Vector3 target, Color color)
        {
            _baseColor = color;
            _spawnTime = Time.time;

            // Create LineRenderer
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, target);

            // Material — use built-in sprite shader for additive glow
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _lineRenderer.material.SetFloat("_Mode", 1); // Additive-ish

            // Width
            _lineRenderer.startWidth = startWidth;
            _lineRenderer.endWidth = endWidth;

            // Color
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color * 0.7f;

            // Shadow off
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            // Auto-destroy safety net
            Destroy(gameObject, fadeDuration + 0.1f);
        }

        // ====================================================================
        // Fade
        // ====================================================================

        private void Update()
        {
            if (_lineRenderer == null) return;

            float elapsed = Time.time - _spawnTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            // Fade alpha
            float alpha = Mathf.Lerp(_baseColor.a, 0f, t);
            Color fadedStart = new(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
            Color fadedEnd = new(_baseColor.r * 0.7f, _baseColor.g * 0.7f, _baseColor.b * 0.7f, alpha * 0.7f);

            _lineRenderer.startColor = fadedStart;
            _lineRenderer.endColor = fadedEnd;

            // Shrink width as it fades
            _lineRenderer.startWidth = startWidth * (1f - t * 0.5f);
            _lineRenderer.endWidth = endWidth * (1f - t * 0.5f);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
