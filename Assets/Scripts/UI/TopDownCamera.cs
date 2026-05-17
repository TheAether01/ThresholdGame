// ============================================================================
// TopDownCamera.cs — Top-down follow camera for mobile shooter
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Smooth follow with configurable height, angle, and dead-zone.
// Supports look-ahead based on movement direction.
// ============================================================================

using UnityEngine;

namespace Threshold.UI
{
    /// <summary>
    /// Top-down camera that smoothly tracks the player from above.
    /// Attach to the main camera GameObject.
    /// </summary>
    public class TopDownCamera : MonoBehaviour
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Target")]
        [Tooltip("The player transform to follow. Auto-finds 'Player' tag if null.")]
        public Transform target;

        [Header("Camera Position")]
        [Tooltip("Height above the player.")]
        public float height = 12f;

        [Tooltip("Forward offset (tilts view slightly ahead of player).")]
        public float forwardOffset = 2f;

        [Tooltip("Camera pitch angle in degrees (90 = directly above).")]
        [Range(45f, 90f)]
        public float pitchAngle = 75f;

        [Header("Smoothing")]
        [Tooltip("How fast the camera follows (higher = snappier).")]
        public float followSpeed = 8f;

        [Tooltip("How fast the camera rotates to match target heading.")]
        public float rotationSpeed = 5f;

        [Header("Look-Ahead")]
        [Tooltip("Camera shifts in the movement direction for better visibility.")]
        public float lookAheadDistance = 1.5f;

        [Tooltip("How fast the look-ahead adjusts.")]
        public float lookAheadSmoothing = 4f;

        [Header("Bounds (Optional)")]
        [Tooltip("If set, camera won't move beyond these world bounds.")]
        public bool useBounds;
        public Vector2 boundsMin = new(-50f, -50f);
        public Vector2 boundsMax = new(50f, 50f);

        // ====================================================================
        // Runtime
        // ====================================================================

        private Vector3 _currentLookAhead;
        private Vector3 _targetLookAhead;
        private Vector3 _velocity;

        // ====================================================================
        // Singleton (optional — for easy reference)
        // ====================================================================

        public static TopDownCamera Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Start()
        {
            if (target == null)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null) target = playerObj.transform;
            }

            if (target != null)
            {
                // Snap to initial position (no smoothing on first frame)
                transform.position = CalculateDesiredPosition();
                transform.rotation = CalculateDesiredRotation();
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            UpdateLookAhead();

            // Smooth position follow
            Vector3 desired = CalculateDesiredPosition();
            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref _velocity, 1f / followSpeed);

            // Smooth rotation
            Quaternion desiredRot = CalculateDesiredRotation();
            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot, rotationSpeed * Time.deltaTime);
        }

        // ====================================================================
        // Position / Rotation Calculation
        // ====================================================================

        private Vector3 CalculateDesiredPosition()
        {
            Vector3 targetPos = target.position + _currentLookAhead;

            // Calculate offset based on pitch angle
            float pitchRad = pitchAngle * Mathf.Deg2Rad;
            float backOffset = height / Mathf.Tan(pitchRad);

            Vector3 offset = new Vector3(0f, height, -backOffset + forwardOffset);
            Vector3 pos = targetPos + offset;

            // Clamp to bounds
            if (useBounds)
            {
                pos.x = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
                pos.z = Mathf.Clamp(pos.z, boundsMin.y, boundsMax.y);
            }

            return pos;
        }

        private Quaternion CalculateDesiredRotation()
        {
            return Quaternion.Euler(pitchAngle, 0f, 0f);
        }

        private void UpdateLookAhead()
        {
            // Derive look-ahead from player's velocity or facing direction
            var rb = target.GetComponent<Rigidbody>();
            Vector3 moveDir = Vector3.zero;

            if (rb != null && rb.linearVelocity.sqrMagnitude > 0.1f)
            {
                moveDir = rb.linearVelocity.normalized;
            }
            else
            {
                moveDir = target.forward;
            }

            // Only use XZ for top-down
            moveDir.y = 0f;
            _targetLookAhead = moveDir * lookAheadDistance;
            _currentLookAhead = Vector3.Lerp(
                _currentLookAhead, _targetLookAhead, lookAheadSmoothing * Time.deltaTime);
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Immediately snaps camera to target (no smoothing).
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            _currentLookAhead = Vector3.zero;
            transform.position = CalculateDesiredPosition();
            transform.rotation = CalculateDesiredRotation();
        }

        /// <summary>
        /// Applies a screen-shake effect (e.g. on explosion or boss hit).
        /// </summary>
        public void Shake(float intensity = 0.3f, float duration = 0.2f)
        {
            StartCoroutine(ShakeCoroutine(intensity, duration));
        }

        private System.Collections.IEnumerator ShakeCoroutine(float intensity, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = 1f - (elapsed / duration);
                Vector3 shake = UnityEngine.Random.insideUnitSphere * intensity * t;
                shake.y = 0f; // Keep shake horizontal
                transform.position += shake;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
