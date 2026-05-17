// ============================================================================
// FixedRuleDirector.cs — Non-agentic baseline for A/B comparison
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// This is the DELIBERATELY SIMPLE control system. It exists solely so we can
// prove the agentic Gemini pipeline outperforms dumb fixed rules.
//
// Rules:
//   Difficulty:   if deaths > 3 → difficulty -= 1.  if won → difficulty += 1.
//   Level gen:    random pick from 5 static JSON templates. No novelty check.
//   NPC behavior: always ATTACK. No flanking, suppression, or defection.
//   Rewards:      flat XP = kills × 10. No effort evaluation or messaging.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threshold.Core
{
    /// <summary>
    /// Fixed-rule director — the "dumb" baseline for hackathon A/B comparison.
    /// No AI reasoning, no context awareness, no adaptation beyond thresholds.
    /// </summary>
    public class FixedRuleDirector : MonoBehaviour
    {
        // ====================================================================
        // Inspector Config
        // ====================================================================

        [Header("Mode Toggle")]
        [Tooltip("Which director system to use for the current session.")]
        public DirectorMode activeMode = DirectorMode.BASELINE;

        [Header("Baseline Settings")]
        [Range(1, 5)]
        [Tooltip("Current difficulty level (1=easiest, 5=hardest).")]
        public int difficultyLevel = 1;

        [Tooltip("Template JSON files loaded from Resources/BaselineTemplates.")]
        public string[] templateNames = new[]
        {
            "baseline_template_1",
            "baseline_template_2",
            "baseline_template_3",
            "baseline_template_4",
            "baseline_template_5"
        };

        // ====================================================================
        // Runtime State
        // ====================================================================

        private SessionMetrics _agenticMetrics;
        private SessionMetrics _baselineMetrics;
        private RunMetrics _currentRun;
        private float _runStartTime;
        private int _lastTemplateIndex = -1;

        /// <summary>Current active metrics tracker based on mode.</summary>
        public SessionMetrics ActiveMetrics =>
            activeMode == DirectorMode.AGENTIC ? _agenticMetrics : _baselineMetrics;

        /// <summary>Current difficulty level (read-only for external systems).</summary>
        public int CurrentDifficulty => difficultyLevel;

        /// <summary>Current active mode.</summary>
        public DirectorMode ActiveMode => activeMode;

        // ====================================================================
        // Singleton
        // ====================================================================

        public static FixedRuleDirector Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Start()
        {
            _agenticMetrics = SessionMetrics.StartSession(DirectorMode.AGENTIC);
            _baselineMetrics = SessionMetrics.StartSession(DirectorMode.BASELINE);

            Debug.Log($"[FixedRuleDirector] Initialized — Mode: {activeMode}");
        }

        // ====================================================================
        // Mode Toggle
        // ====================================================================

        /// <summary>
        /// Switches between AGENTIC and BASELINE modes.
        /// Saves current session metrics before switching.
        /// </summary>
        public void SetMode(DirectorMode newMode)
        {
            if (activeMode == newMode) return;

            Debug.Log($"[FixedRuleDirector] Switching mode: {activeMode} → {newMode}");
            ActiveMetrics.Save();

            activeMode = newMode;
            difficultyLevel = 1; // Reset difficulty on mode switch
        }

        /// <summary>
        /// Toggles between modes.
        /// </summary>
        public void ToggleMode()
        {
            SetMode(activeMode == DirectorMode.AGENTIC
                ? DirectorMode.BASELINE
                : DirectorMode.AGENTIC);
        }

        // ====================================================================
        // BASELINE: Difficulty Adjustment (Dumb Thresholds)
        // ====================================================================

        /// <summary>
        /// Adjusts difficulty using fixed thresholds only.
        /// No context, no reasoning, no player modeling.
        /// Called at the end of each run.
        /// </summary>
        public void BaselineAdjustDifficulty(int deaths, bool won)
        {
            int oldDifficulty = difficultyLevel;

            // Rule 1: Too many deaths → ease up
            if (deaths > 3)
            {
                difficultyLevel = Mathf.Max(1, difficultyLevel - 1);
            }
            // Rule 2: Won → ramp up
            else if (won)
            {
                difficultyLevel = Mathf.Min(5, difficultyLevel + 1);
            }
            // Rule 3: Otherwise, no change

            if (oldDifficulty != difficultyLevel)
            {
                Debug.Log($"[Baseline] Difficulty: {oldDifficulty} → {difficultyLevel}" +
                          $" (deaths={deaths}, won={won})");
            }
        }

        // ====================================================================
        // BASELINE: Level Generation (Random Template Pick)
        // ====================================================================

        /// <summary>
        /// Picks a random template JSON config from the 5 pre-made templates.
        /// No novelty check, no QC validation, no reasoning.
        /// </summary>
        public RoomGraphConfig BaselineGetLevelConfig()
        {
            // Random pick from templates (can repeat — deliberately no novelty)
            int idx = UnityEngine.Random.Range(0, templateNames.Length);
            _lastTemplateIndex = idx;

            string templateName = templateNames[idx];
            TextAsset jsonAsset = Resources.Load<TextAsset>($"BaselineTemplates/{templateName}");

            if (jsonAsset == null)
            {
                Debug.LogError($"[Baseline] Template not found: BaselineTemplates/{templateName}");
                return CreateFallbackConfig();
            }

            Debug.Log($"[Baseline] Using template: {templateName} (index {idx})");

            RoomGraphConfig config = JsonUtility.FromJson<RoomGraphConfig>(jsonAsset.text);

            // Apply difficulty scaling to enemy counts
            ApplyDifficultyScaling(config);

            return config;
        }

        /// <summary>
        /// Applies crude difficulty scaling: multiply enemy counts by difficulty level.
        /// No nuance, no per-room consideration.
        /// </summary>
        private void ApplyDifficultyScaling(RoomGraphConfig config)
        {
            if (config?.rooms == null) return;

            float multiplier = 0.5f + (difficultyLevel * 0.25f); // 0.75x to 1.75x

            foreach (var room in config.rooms)
            {
                if (room.spawnZones == null) continue;
                foreach (var zone in room.spawnZones)
                {
                    zone.count = Mathf.Max(1, Mathf.RoundToInt(zone.count * multiplier));
                }
            }

            // Update difficulty profile
            if (config.difficulty == null)
                config.difficulty = new DifficultyProfile();

            config.difficulty.difficultyMultiplier = multiplier;
            config.difficulty.preferredTactic = "ATTACK"; // Always ATTACK in baseline
        }

        /// <summary>
        /// Emergency fallback if template loading fails.
        /// </summary>
        private RoomGraphConfig CreateFallbackConfig()
        {
            Debug.LogWarning("[Baseline] Using hardcoded fallback config.");
            return new RoomGraphConfig
            {
                difficulty = new DifficultyProfile
                {
                    difficultyMultiplier = 1.0f,
                    targetRoomCount = 5,
                    baseEnemiesPerRoom = 2,
                    preferredTactic = "ATTACK"
                },
                rooms = new List<RoomConfig>(),
                edges = new List<EdgeConfig>(),
                metadata = new LayoutMetadata
                {
                    generationMethod = "baseline_fallback",
                    timestamp = DateTime.Now.ToString("o")
                }
            };
        }

        // ====================================================================
        // BASELINE: NPC Behavior (Always ATTACK)
        // ====================================================================

        /// <summary>
        /// Returns the NPC behavior state for baseline mode.
        /// Always returns ATTACK. No flanking, no suppression, no defection.
        /// </summary>
        public string BaselineGetNPCBehavior()
        {
            return "ATTACK";
        }

        /// <summary>
        /// Returns whether NPC defection should occur.
        /// Baseline: never. Defection is an agentic-only feature.
        /// </summary>
        public bool BaselineShouldDefect()
        {
            return false;
        }

        // ====================================================================
        // BASELINE: Rewards (Flat XP)
        // ====================================================================

        /// <summary>
        /// Calculates reward XP for baseline mode.
        /// Flat formula: kills × 10. No effort evaluation, no bonuses.
        /// </summary>
        public BaselineReward BaselineCalculateReward(int kills)
        {
            return new BaselineReward
            {
                xp = kills * 10,
                message = "" // No messaging in baseline
            };
        }

        // ====================================================================
        // Run Lifecycle
        // ====================================================================

        /// <summary>
        /// Call when a new run begins.
        /// </summary>
        public void StartRun()
        {
            _runStartTime = Time.time;
            _currentRun = new RunMetrics
            {
                difficultyLevel = difficultyLevel
            };

            Debug.Log($"[{activeMode}] Run started — Difficulty {difficultyLevel}");
        }

        /// <summary>
        /// Records a room completion during the current run.
        /// </summary>
        public void RecordRoomCompleted()
        {
            if (_currentRun != null)
                _currentRun.roomsCompleted++;
        }

        /// <summary>
        /// Records a player death during the current run.
        /// </summary>
        public void RecordDeath()
        {
            if (_currentRun != null)
                _currentRun.deaths++;
        }

        /// <summary>
        /// Records a kill during the current run.
        /// </summary>
        public void RecordKill()
        {
            if (_currentRun != null)
                _currentRun.kills++;
        }

        /// <summary>
        /// Records a defection event during the current run.
        /// (Only meaningful in agentic mode.)
        /// </summary>
        public void RecordDefection()
        {
            if (_currentRun != null)
                _currentRun.defectionEvents++;
        }

        /// <summary>
        /// Call when the run ends (win or loss).
        /// Handles difficulty adjustment for baseline mode.
        /// </summary>
        public void EndRun(bool won, int totalRooms)
        {
            if (_currentRun == null) return;

            _currentRun.durationSeconds = Time.time - _runStartTime;
            _currentRun.runWon = won;
            _currentRun.totalRooms = totalRooms;

            ActiveMetrics.RecordRun(_currentRun);

            // Baseline difficulty adjustment
            if (activeMode == DirectorMode.BASELINE)
            {
                BaselineAdjustDifficulty(_currentRun.deaths, won);
            }

            Debug.Log($"[{activeMode}] Run ended — Won: {won}, " +
                      $"Rooms: {_currentRun.roomsCompleted}/{totalRooms}, " +
                      $"Kills: {_currentRun.kills}, Deaths: {_currentRun.deaths}");

            _currentRun = null;
        }

        // ====================================================================
        // Session Management
        // ====================================================================

        /// <summary>
        /// Saves both sessions and logs comparison.
        /// Call on app quit or at end of testing session.
        /// </summary>
        public void SaveAndCompare()
        {
            _agenticMetrics.Save();
            _baselineMetrics.Save();

            if (_agenticMetrics.totalRuns > 0 && _baselineMetrics.totalRuns > 0)
            {
                SessionMetrics.LogComparison(_agenticMetrics, _baselineMetrics);
            }
            else
            {
                Debug.Log("[FixedRuleDirector] Need runs in both modes for comparison.");
            }
        }

        private void OnApplicationQuit()
        {
            SaveAndCompare();
        }

        // ====================================================================
        // Debug / Inspector
        // ====================================================================

        /// <summary>
        /// Logs current state for debugging.
        /// </summary>
        public void LogStatus()
        {
            var m = ActiveMetrics;
            Debug.Log("═══════════════════════════════════════════════════");
            Debug.Log($"  Mode: {activeMode} | Difficulty: {difficultyLevel}");
            Debug.Log($"  Runs: {m.totalRuns} | Win Rate: {m.WinRate:F1}%");
            Debug.Log($"  Avg Duration: {m.AvgRunDuration:F1}s | Retry Rate: {m.RetryRate:F2}");
            Debug.Log($"  Rooms/Run: {m.AvgRoomsPerRun:F1} | Defections: {m.totalDefectionEvents}");
            Debug.Log("═══════════════════════════════════════════════════");
        }
    }

    // ========================================================================
    // Baseline Reward (simple data container)
    // ========================================================================

    /// <summary>
    /// Reward output for baseline mode. Deliberately minimal.
    /// </summary>
    [Serializable]
    public struct BaselineReward
    {
        public int xp;
        public string message;
    }
}
