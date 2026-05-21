// ============================================================================
// ProjectileTracer.cs — Short bullet tracer visual
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Spawns a short glowing bullet that travels from muzzle to hit point.
// Purely visual — all damage is handled by raycast (hitscan).
// Call ProjectileTracer.Spawn() from any weapon system.
// ============================================================================

using UnityEngine;

namespace Threshold.Player
{
    /// <summary>
    /// Configuration struct for tracer appearance.
    /// Set these values on PlayerWeapon in the Inspector.
    /// </summary>
    [System.Serializable]
    public struct TracerConfig
    {
        [Tooltip("Main color of the bullet tracer.")]
        public Color color;

        [Tooltip("Width of the bullet at the front.")]
        [Range(0.01f, 0.5f)]
        public float startWidth;

        [Tooltip("Width of the bullet at the tail.")]
        [Range(0.005f, 0.3f)]
        public float endWidth;

        [Tooltip("Length of the bullet tracer (world units).")]
        [Range(0.1f, 3f)]
        public float bulletLength;

        [Tooltip("Travel speed of the bullet (units/sec). Higher = faster.")]
        [Range(30f, 300f)]
        public float speed;

        [Tooltip("Glow intensity multiplier for HDR bloom. 1 = normal, 2+ = bright glow.")]
        [Range(0.5f, 8f)]
        public float glowIntensity;

        /// <summary>Default bright blue bullet config (player).</summary>
        public static TracerConfig Default => new()
        {
            color         = new Color(0.2f, 0.5f, 1f, 1f),    // Bright blue
            startWidth    = 0.08f,
            endWidth      = 0.02f,
            bulletLength  = 0.6f,
            speed         = 120f,
            glowIntensity = 3f
        };

        /// <summary>Bright red bullet config (enemy NPC).</summary>
        public static TracerConfig EnemyDefault => new()
        {
            color         = new Color(1f, 0.15f, 0.1f, 1f),   // Bright red
            startWidth    = 0.08f,
            endWidth      = 0.02f,
            bulletLength  = 0.6f,
            speed         = 100f,
            glowIntensity = 3f
        };
    }

    /// <summary>
    /// Visual bullet tracer that travels from origin to target.
    /// Self-destructs on arrival. No physics.
    /// </summary>
    public class ProjectileTracer : MonoBehaviour
    {
        // ====================================================================
        // Internal
        // ====================================================================

        private LineRenderer _lineRenderer;
        private Vector3 _origin;
        private Vector3 _target;
        private Vector3 _direction;
        private float _totalDistance;
        private float _travelledDistance;
        private float _bulletLength;
        private float _speed;
        private Color _baseColor;
        private float _startWidth;
        private float _endWidth;

        // ====================================================================
        // Static Factory
        // ====================================================================

        /// <summary>
        /// Spawns a bullet tracer from origin to target using a TracerConfig.
        /// </summary>
        public static ProjectileTracer Spawn(Vector3 origin, Vector3 target, TracerConfig config)
        {
            var go = new GameObject("BulletTracer");
            var tracer = go.AddComponent<ProjectileTracer>();
            tracer.Initialize(origin, target, config);
            return tracer;
        }

        /// <summary>
        /// Spawns a bullet tracer from origin to target with the given color (legacy overload).
        /// Uses EnemyDefault config for red-ish colors, Default for anything else.
        /// </summary>
        public static ProjectileTracer Spawn(Vector3 origin, Vector3 target, Color color)
        {
            // Detect if color is red-ish (enemy) or blue-ish (player)
            TracerConfig cfg = color.r > 0.7f && color.g < 0.4f
                ? TracerConfig.EnemyDefault
                : TracerConfig.Default;
            cfg.color = color;
            return Spawn(origin, target, cfg);
        }

        /// <summary>
        /// Spawns a bullet tracer with the default player color.
        /// </summary>
        public static ProjectileTracer Spawn(Vector3 origin, Vector3 target)
        {
            return Spawn(origin, target, TracerConfig.Default);
        }

        // ====================================================================
        // Setup
        // ====================================================================

        private void Initialize(Vector3 origin, Vector3 target, TracerConfig config)
        {
            _origin         = origin;
            _target         = target;
            _direction      = (target - origin).normalized;
            _totalDistance   = Vector3.Distance(origin, target);
            _travelledDistance = 0f;
            _bulletLength   = config.bulletLength;
            _speed          = config.speed;
            _baseColor      = config.color;
            _startWidth     = config.startWidth;
            _endWidth       = config.endWidth;

            // HDR glow: multiply color by intensity for bloom support
            Color hdrColor = _baseColor * config.glowIntensity;
            hdrColor.a = 1f;

            // Tail color — slightly dimmer, more transparent
            Color tailColor = hdrColor * 0.5f;
            tailColor.a = 0.4f;

            // Create LineRenderer for the short bullet segment
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, origin);

            // Material — use built-in sprite shader for additive glow
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

            // Width — front is wider (bullet head), tail tapers off
            _lineRenderer.startWidth = _startWidth;
            _lineRenderer.endWidth   = _endWidth;

            // Color gradient — bright head, dimmer tail
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new(tailColor, 0f),   // Tail (start of line = back)
                    new(hdrColor,  1f)    // Head (end of line = front)
                },
                new GradientAlphaKey[]
                {
                    new(0.3f, 0f),        // Tail is semi-transparent
                    new(1f,   0.5f),      // Body is solid
                    new(1f,   1f)         // Head is solid
                }
            );
            _lineRenderer.colorGradient = gradient;

            // Shadow off
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            // Auto-destroy safety net (if bullet takes too long)
            float maxLifetime = (_totalDistance / Mathf.Max(_speed, 1f)) + 0.5f;
            Destroy(gameObject, maxLifetime);
        }

        // ====================================================================
        // Travel
        // ====================================================================

        private void Update()
        {
            if (_lineRenderer == null) return;

            // Move bullet forward
            _travelledDistance += _speed * Time.deltaTime;

            if (_travelledDistance >= _totalDistance)
            {
                // Bullet arrived — destroy
                Destroy(gameObject);
                return;
            }

            // Head position (leading edge of bullet)
            float headDist = _travelledDistance;
            Vector3 headPos = _origin + _direction * headDist;

            // Tail position (trailing edge of bullet)
            float tailDist = Mathf.Max(0f, headDist - _bulletLength);
            Vector3 tailPos = _origin + _direction * tailDist;

            _lineRenderer.SetPosition(0, tailPos);
            _lineRenderer.SetPosition(1, headPos);
        }
    }
}
