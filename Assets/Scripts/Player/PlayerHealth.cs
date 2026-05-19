// ============================================================================
// PlayerHealth.cs — Player health, damage, and death system
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Manages HP pool, invincibility frames, death events, and HUD sync.
// NPCs call TakeDamage() via their TryFire() raycast hits.
// ============================================================================

using System;
using UnityEngine;

namespace Threshold.Player
{
    /// <summary>
    /// Player health system. Attach to the Player GameObject alongside
    /// PlayerController and PlayerWeapon.
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        // ====================================================================
        // Configuration
        // ====================================================================

        [Header("Health")]
        [Tooltip("Maximum hit points.")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Duration of invincibility after being hit (seconds).")]
        [SerializeField] private float iFrameDuration = 0.2f;

        [Header("Visual Feedback")]
        [Tooltip("Camera shake intensity on hit.")]
        [SerializeField] private float hitShakeIntensity = 0.25f;

        [Tooltip("Camera shake duration on hit.")]
        [SerializeField] private float hitShakeDuration = 0.15f;

        [Tooltip("Camera shake intensity on death.")]
        [SerializeField] private float deathShakeIntensity = 0.6f;

        [Tooltip("Camera shake duration on death.")]
        [SerializeField] private float deathShakeDuration = 0.4f;

        // ====================================================================
        // Events
        // ====================================================================

        /// <summary>Fired when the player takes damage. Arg = normalized health (0–1).</summary>
        public event Action<float> OnDamaged;

        /// <summary>Fired when the player is healed. Arg = normalized health (0–1).</summary>
        public event Action<float> OnHealed;

        /// <summary>Fired when the player dies.</summary>
        public event Action OnDied;

        // ====================================================================
        // State
        // ====================================================================

        /// <summary>Current hit points.</summary>
        public float CurrentHealth { get; private set; }

        /// <summary>Maximum hit points.</summary>
        public float MaxHealth => maxHealth;

        /// <summary>Normalized health (0–1).</summary>
        public float HealthPercent => maxHealth > 0 ? CurrentHealth / maxHealth : 0f;

        /// <summary>True if health <= 0.</summary>
        public bool IsDead { get; private set; }

        /// <summary>True during invincibility frames.</summary>
        public bool IsInvincible => Time.time < _iFrameEndTime;

        // ====================================================================
        // Singleton
        // ====================================================================

        public static PlayerHealth Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private float _iFrameEndTime;

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
            CurrentHealth = maxHealth;
            IsDead = false;
            SyncHUD();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Apply damage to the player. Respects invincibility frames.
        /// Returns true if the player dies from this hit.
        /// </summary>
        public bool TakeDamage(float amount)
        {
            if (IsDead || IsInvincible || amount <= 0f) return false;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);

            // Activate invincibility frames
            _iFrameEndTime = Time.time + iFrameDuration;

            // Camera shake
            var uiManager = UI.ThresholdUIManager.Instance;
            uiManager?.ShakeCamera(hitShakeIntensity, hitShakeDuration);

            // Update HUD
            SyncHUD();

            // Notify metrics tracker
            PlayerMetricsTracker.Instance?.UpdateLiveStats(HealthPercent,
                PlayerWeapon.Instance != null ? PlayerWeapon.Instance.AmmoPercent : 1f);

            // Fire event
            OnDamaged?.Invoke(HealthPercent);

            if (CurrentHealth <= 0f)
            {
                Die();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Heal the player by the specified amount.
        /// </summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            SyncHUD();

            OnHealed?.Invoke(HealthPercent);
        }

        /// <summary>
        /// Fully restore health (e.g. on new run start).
        /// </summary>
        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            _iFrameEndTime = 0f;
            SyncHUD();
        }

        // ====================================================================
        // Internal
        // ====================================================================

        private void Die()
        {
            IsDead = true;

            // Heavy camera shake on death
            UI.ThresholdUIManager.Instance?.ShakeCamera(deathShakeIntensity, deathShakeDuration);

            // Notify metrics
            PlayerMetricsTracker.Instance?.OnPlayerDeath();

            // Fire event
            OnDied?.Invoke();

            Debug.Log("[PlayerHealth] Player died.");
        }

        private void SyncHUD()
        {
            UI.ThresholdUIManager.Instance?.UpdateHealth(HealthPercent);
        }
    }
}
