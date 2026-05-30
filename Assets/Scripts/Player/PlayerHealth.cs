// ============================================================================
// PlayerHealth.cs — Player health, damage, and death system
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Manages HP pool, invincibility frames, death events, and HUD sync.
// NPCs call TakeDamage() via their TryFire() raycast hits.
// ============================================================================

using System;
using System.Collections;
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

        [Header("Spawn Immunity")]
        [Tooltip("If true, player is immune until first movement after spawn/respawn.")]
        [SerializeField] private bool enableSpawnImmunity = true;

        [Tooltip("Flash speed during spawn immunity (flashes per second).")]
        [Range(2f, 20f)]
        [SerializeField] private float immunityFlashRate = 8f;

        [Header("Visual Feedback")]
        [Tooltip("Camera shake intensity on hit.")]
        [SerializeField] private float hitShakeIntensity = 0.25f;

        [Tooltip("Camera shake duration on hit.")]
        [SerializeField] private float hitShakeDuration = 0.15f;

        [Tooltip("Camera shake intensity on death.")]
        [SerializeField] private float deathShakeIntensity = 0.6f;

        [Tooltip("Camera shake duration on death.")]
        [SerializeField] private float deathShakeDuration = 0.4f;

        [Header("Hit SFX")]
        [Tooltip("Sound played when the player takes damage.")]
        [SerializeField] private AudioClip hitSFX;

        [Tooltip("Volume for hit sound.")]
        [Range(0f, 1f)]
        [SerializeField] private float hitSFXVolume = 0.7f;

        [Header("Hit Flash")]
        [Tooltip("Duration of the red flash on player model when hit.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float hitFlashDuration = 0.12f;

        [Tooltip("Color the player flashes when hit.")]
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.1f, 0.05f, 1f);

        [Header("Low Health Vignette")]
        [Tooltip("Health threshold (0-1) below which the red vignette appears.")]
        [Range(0.1f, 0.6f)]
        [SerializeField] private float dangerThreshold = 0.35f;

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
        public bool IsInvincible => _spawnImmune || Time.time < _iFrameEndTime;

        /// <summary>True if the player has spawn immunity active.</summary>
        public bool HasSpawnImmunity => _spawnImmune;

        // ====================================================================
        // Singleton
        // ====================================================================

        public static PlayerHealth Instance { get; private set; }

        // ====================================================================
        // Internal
        // ====================================================================

        private float _iFrameEndTime;
        private bool _spawnImmune;
        private Coroutine _flashCoroutine;
        private Coroutine _hitFlashCoroutine;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private AudioSource _audioSource;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Cache renderers for flashing effect
            _renderers = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();

            // Audio source for hit SFX
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            // Ensure DamageVignette exists
            if (UI.DamageVignette.Instance == null)
            {
                var vignetteObj = new GameObject("DamageVignette");
                vignetteObj.AddComponent<UI.DamageVignette>();
            }

            // Ensure DamageIndicatorSystem exists
            if (UI.DamageIndicatorSystem.Instance == null)
            {
                var indicatorObj = new GameObject("DamageIndicatorSystem");
                indicatorObj.AddComponent<UI.DamageIndicatorSystem>();
            }
        }

        private void Start()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            SyncHUD();
        }

        private void Update()
        {
            // Check if spawn immunity should end (player started moving)
            if (_spawnImmune && enableSpawnImmunity)
            {
                var controller = PlayerController.Instance;
                if (controller != null && controller.IsMoving)
                {
                    DeactivateSpawnImmunity();
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Apply damage to the player. Respects invincibility frames and spawn immunity.
        /// Returns true if the player dies from this hit.
        /// </summary>
        public bool TakeDamage(float amount)
        {
            return TakeDamage(amount, null);
        }

        /// <summary>
        /// Apply damage to the player with attacker position for directional indicators.
        /// Respects invincibility frames and spawn immunity.
        /// Returns true if the player dies from this hit.
        /// </summary>
        public bool TakeDamage(float amount, Vector3? attackerPosition)
        {
            if (IsDead || IsInvincible || amount <= 0f) return false;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);

            // Activate invincibility frames
            _iFrameEndTime = Time.time + iFrameDuration;

            // --- Feedback Layer 1: Camera shake (always) ---
            var uiManager = UI.ThresholdUIManager.Instance;
            uiManager?.ShakeCamera(hitShakeIntensity, hitShakeDuration);

            // --- Feedback Layer 2: Hit SFX (always) ---
            if (hitSFX != null && _audioSource != null)
                _audioSource.PlayOneShot(hitSFX, hitSFXVolume);

            // --- Feedback Layer 3: Red flash on player model (always) ---
            if (_hitFlashCoroutine != null)
                StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashCoroutine());

            // --- Feedback Layer 4: Vignette (only when low health) ---
            var vignette = UI.DamageVignette.Instance;
            if (vignette != null)
            {
                if (HealthPercent <= dangerThreshold)
                {
                    // Flash the vignette on this hit + start/update persistent danger pulse
                    float dangerNormalized = 1f - (HealthPercent / dangerThreshold); // 0 at threshold, 1 at 0HP
                    vignette.Flash(0.5f + dangerNormalized * 0.4f, 0.35f);
                    vignette.SetDanger(dangerNormalized);
                }
                else
                {
                    // Above threshold — ensure danger is off
                    vignette.SetDanger(0f);
                }
            }

            // --- Feedback Layer 5: Directional damage indicator ---
            if (attackerPosition.HasValue)
            {
                UI.DamageIndicatorSystem.Instance?.ShowIndicator(attackerPosition.Value);
            }

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

            // Clear danger vignette if health recovered above threshold
            if (HealthPercent > dangerThreshold)
                UI.DamageVignette.Instance?.SetDanger(0f);

            OnHealed?.Invoke(HealthPercent);
        }

        /// <summary>
        /// Fully restore health (e.g. on new run start).
        /// Re-activates spawn immunity.
        /// </summary>
        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            _iFrameEndTime = 0f;
            SyncHUD();

            // Clear danger vignette on reset
            UI.DamageVignette.Instance?.SetDanger(0f);

            // Re-activate spawn immunity on respawn
            if (enableSpawnImmunity)
                ActivateSpawnImmunity();
        }

        // ====================================================================
        // Spawn Immunity
        // ====================================================================

        private void ActivateSpawnImmunity()
        {
            _spawnImmune = true;

            // Start flashing effect
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine());

            Debug.Log("[PlayerHealth] Spawn immunity ACTIVE — move to dismiss.");
        }

        private void DeactivateSpawnImmunity()
        {
            _spawnImmune = false;

            // Stop flashing and ensure all renderers are visible
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
            }
            SetRenderersVisible(true);

            Debug.Log("[PlayerHealth] Spawn immunity ENDED — player moved.");
        }

        private IEnumerator FlashCoroutine()
        {
            float interval = 1f / (immunityFlashRate * 2f); // half-period (on/off)
            bool visible = true;

            while (_spawnImmune)
            {
                visible = !visible;
                SetRenderersVisible(visible);
                yield return new WaitForSeconds(interval);
            }

            // Ensure visible when done
            SetRenderersVisible(true);
            _flashCoroutine = null;
        }

        private void SetRenderersVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
            {
                if (r != null)
                    r.enabled = visible;
            }
        }

        // ====================================================================
        // Hit Flash (red tint on damage)
        // ====================================================================

        private IEnumerator HitFlashCoroutine()
        {
            if (_renderers == null || _renderers.Length == 0) yield break;

            // Apply red tint via MaterialPropertyBlock (avoids material cloning)
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", hitFlashColor); // URP
                _mpb.SetColor("_Color", hitFlashColor);     // Standard
                r.SetPropertyBlock(_mpb);
            }

            yield return new WaitForSeconds(hitFlashDuration);

            // Restore original colors by clearing the property block
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.Clear();
                r.SetPropertyBlock(_mpb);
            }

            _hitFlashCoroutine = null;
        }

        // ====================================================================
        // Internal
        // ====================================================================

        private void Die()
        {
            IsDead = true;

            // Cancel spawn immunity if active
            if (_spawnImmune)
                DeactivateSpawnImmunity();

            // Clear vignette on death
            UI.DamageVignette.Instance?.SetDanger(0f);

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
