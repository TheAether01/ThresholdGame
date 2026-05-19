// ============================================================================
// PlayerMetricsTracker.cs — Observation backbone for the 5-agent system
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Tracks 7 player performance signals + 8 retention metrics.
// Game systems call event methods (OnShotFired, OnRoomClear, etc.).
// Agents call data access methods (GetDirectorInputJSON, etc.).
// History persists across sessions via JSON in persistentDataPath.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Threshold.Core;
using UnityEngine;

namespace Threshold.Player
{
    // ========================================================================
    // Data Layer 1: Per-Room Metrics
    // ========================================================================

    [Serializable]
    public class RoomMetrics
    {
        public string roomId;
        public RoomRole roomRole;
        public float enterTime;
        public float clearTime;
        public float clearDurationSeconds;
        public int shotsFired;
        public int shotsHit;
        public int enemiesKilled;
        public int ammoUsed;
        public int healthKitsUsed;
        public int deaths;
        public int defectionsWitnessed;
        public float playerHealthOnEnter;
        public float playerHealthOnClear;

        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;
        public float AmmoPerKill => enemiesKilled > 0 ? (float)ammoUsed / enemiesKilled : 0f;
    }

    // ========================================================================
    // Data Layer 2: Per-Run Metrics
    // ========================================================================

    [Serializable]
    public class RunMetrics
    {
        public string runId;
        public string startTimestamp;
        public string endTimestamp;
        public float sessionLengthSeconds;
        public bool won;
        public int roomsCompleted;
        public int totalDeaths;
        public int totalKills;
        public int totalShotsFired;
        public int totalShotsHit;
        public int totalAmmoUsed;
        public int totalHealthKitsUsed;
        public int totalDefections;
        public float avgRoomClearTime;
        public float overallAccuracy;
        public float avgAmmoPerKill;
        public float timeSinceLastRun; // seconds, -1 if first run
        public bool wasQuickRetry;     // < 5s gap = frustrated
        public List<RoomMetrics> roomBreakdown;

        // Retention metrics
        public float churnRiskScore;      // 0-1, computed by tracker
        public float improvementDelta;    // accuracy delta vs previous run
        public float satisfactionProxy;   // session length delta vs previous
    }

    // ========================================================================
    // Data Layer 3: Persistent Player History
    // ========================================================================

    [Serializable]
    public class PlayerHistory
    {
        public int totalRuns;
        public int totalWins;
        public int totalLosses;
        public int winLossStreak;         // +N = win streak, -N = loss streak
        public int quickRetryCount;       // lifetime frustrated retries
        public float bestAccuracy;
        public float fastestRoomClear;
        public int mostKillsInRun;
        public int challengesAccepted;
        public int challengesCompleted;
        public int weaponTypesUsed;       // feature exploration
        public int pathBranchesExplored;  // feature exploration
        public float lastRunEndTime;      // Time.realtimeSinceStartup
        public List<RunMetrics> recentRuns = new();
    }

    // ========================================================================
    // Micro-Adjustment Output
    // ========================================================================

    [Serializable]
    public class MicroAdjustment
    {
        public bool addHealthKit;
        public bool addAmmoCache;
        public int enemyCountDelta;  // +1, -1, or 0
        public string reason;
    }

    // ========================================================================
    // Main Tracker
    // ========================================================================

    /// <summary>
    /// Singleton MonoBehaviour that records all player actions and computes
    /// the signals consumed by the Director, NPC Brain, and Reward agents.
    /// </summary>
    public class PlayerMetricsTracker : MonoBehaviour
    {
        private static PlayerMetricsTracker _instance;
        public static PlayerMetricsTracker Instance
        {
            get { return _instance; }
        }

        [Header("Configuration")]
        [SerializeField] private int maxRecentRuns = 10;
        [SerializeField] private float quickRetryThreshold = 5f; // seconds
        [SerializeField] private bool logEvents = true;

        [Header("Micro-Adjustment Thresholds")]
        [SerializeField] private float healthKitThreshold = 0.30f;
        [SerializeField] private float ammoLowThreshold = 0.20f;
        [SerializeField] private float fastClearMultiplier = 0.5f;  // < 50% of par
        [SerializeField] private float slowClearMultiplier = 2.0f;  // > 200% of par
        [SerializeField] private float parRoomClearTime = 25f;

        // State
        private PlayerHistory _history = new();
        private RunMetrics _currentRun;
        private RoomMetrics _currentRoom;
        private float _runStartTime;
        private string _savePath;
        private bool _runEnded;

        // Live signals (updated each frame or on events)
        private float _currentPlayerHealth = 1f;
        private float _currentPlayerAmmoPercent = 1f;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _savePath = Path.Combine(Application.persistentDataPath, "player_history.json");
            LoadHistory();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ====================================================================
        // Event Recording — called by game systems
        // ====================================================================

        public void OnRunStart()
        {
            // C2: Auto-finalize orphan run if previous wasn't ended
            if (_currentRun != null && !_runEnded)
            {
                Debug.LogWarning("[PlayerMetrics] Previous run not ended — auto-finalizing as LOSS.");
                OnRunEnd(false);
            }

            float now = Time.realtimeSinceStartup;
            float gap = _history.lastRunEndTime > 0 ? now - _history.lastRunEndTime : -1f;

            _currentRun = new RunMetrics
            {
                runId = $"run_{_history.totalRuns}",
                startTimestamp = DateTime.UtcNow.ToString("o"),
                roomBreakdown = new List<RoomMetrics>(),
                timeSinceLastRun = gap,
                wasQuickRetry = gap >= 0 && gap < quickRetryThreshold
            };
            _runStartTime = now;
            _runEnded = false;

            if (_currentRun.wasQuickRetry)
            {
                _history.quickRetryCount++;
                if (logEvents) Debug.Log("[PlayerMetrics] Quick retry detected (frustration signal).");
            }

            if (logEvents) Debug.Log($"[PlayerMetrics] Run started: {_currentRun.runId}");
        }

        public void OnRunEnd(bool won)
        {
            if (_currentRun == null || _runEnded) return;
            _runEnded = true;

            // Finalize current room if still open
            if (_currentRoom != null) FinalizeRoom();

            float now = Time.realtimeSinceStartup;
            _currentRun.won = won;
            _currentRun.sessionLengthSeconds = now - _runStartTime;
            _currentRun.endTimestamp = DateTime.UtcNow.ToString("o");

            // Compute aggregates
            var rooms = _currentRun.roomBreakdown;
            _currentRun.roomsCompleted = rooms.Count;
            _currentRun.totalDeaths = rooms.Sum(r => r.deaths);
            _currentRun.totalKills = rooms.Sum(r => r.enemiesKilled);
            _currentRun.totalShotsFired = rooms.Sum(r => r.shotsFired);
            _currentRun.totalShotsHit = rooms.Sum(r => r.shotsHit);
            _currentRun.totalAmmoUsed = rooms.Sum(r => r.ammoUsed);
            _currentRun.totalHealthKitsUsed = rooms.Sum(r => r.healthKitsUsed);
            _currentRun.totalDefections = rooms.Sum(r => r.defectionsWitnessed);

            _currentRun.overallAccuracy = _currentRun.totalShotsFired > 0
                ? (float)_currentRun.totalShotsHit / _currentRun.totalShotsFired : 0f;

            _currentRun.avgAmmoPerKill = _currentRun.totalKills > 0
                ? (float)_currentRun.totalAmmoUsed / _currentRun.totalKills : 0f;

            var combatRooms = rooms.Where(r => r.clearDurationSeconds > 0).ToList();
            _currentRun.avgRoomClearTime = combatRooms.Count > 0
                ? combatRooms.Average(r => r.clearDurationSeconds) : 0f;

            // Retention metrics
            ComputeRetentionMetrics(_currentRun);

            // Update history
            _history.totalRuns++;
            if (won) { _history.totalWins++; }
            else { _history.totalLosses++; }

            // Win/loss streak
            if (won)
                _history.winLossStreak = _history.winLossStreak >= 0
                    ? _history.winLossStreak + 1 : 1;
            else
                _history.winLossStreak = _history.winLossStreak <= 0
                    ? _history.winLossStreak - 1 : -1;

            // Lifetime bests
            if (_currentRun.overallAccuracy > _history.bestAccuracy)
                _history.bestAccuracy = _currentRun.overallAccuracy;
            if (_currentRun.totalKills > _history.mostKillsInRun)
                _history.mostKillsInRun = _currentRun.totalKills;

            foreach (var r in combatRooms)
            {
                if (_history.fastestRoomClear <= 0 || r.clearDurationSeconds < _history.fastestRoomClear)
                    _history.fastestRoomClear = r.clearDurationSeconds;
            }

            _history.lastRunEndTime = now;
            _history.recentRuns.Add(_currentRun);
            while (_history.recentRuns.Count > maxRecentRuns)
                _history.recentRuns.RemoveAt(0);

            SaveHistory();

            if (logEvents)
                Debug.Log($"[PlayerMetrics] Run ended: {(won ? "WIN" : "LOSS")} | " +
                          $"Kills={_currentRun.totalKills} Acc={_currentRun.overallAccuracy:P0} " +
                          $"Time={_currentRun.sessionLengthSeconds:F1}s Streak={_history.winLossStreak}");

            _currentRun = null;
        }

        public void OnRoomEnter(string roomId, RoomRole role, float playerHealth)
        {
            if (_currentRoom != null) FinalizeRoom();

            _currentRoom = new RoomMetrics
            {
                roomId = roomId,
                roomRole = role,
                enterTime = Time.realtimeSinceStartup,
                playerHealthOnEnter = playerHealth
            };
            _currentPlayerHealth = playerHealth;

            if (logEvents) Debug.Log($"[PlayerMetrics] Entered {roomId} ({role})");
        }

        public void OnRoomClear(float playerHealth)
        {
            if (_currentRoom == null) return;
            _currentRoom.playerHealthOnClear = playerHealth;
            _currentPlayerHealth = playerHealth;
            FinalizeRoom();
        }

        public void OnShotFired()
        {
            if (_currentRoom != null) _currentRoom.shotsFired++;
        }

        public void OnShotHit()
        {
            // C3: Clamp shotsHit to never exceed shotsFired (spread weapons)
            if (_currentRoom != null && _currentRoom.shotsHit < _currentRoom.shotsFired)
                _currentRoom.shotsHit++;
        }

        public void OnEnemyKilled()
        {
            if (_currentRoom != null) _currentRoom.enemiesKilled++;
        }

        public void OnAmmoUsed(int amount = 1)
        {
            if (_currentRoom != null) _currentRoom.ammoUsed += amount;
        }

        public void OnHealthKitUsed()
        {
            if (_currentRoom != null) _currentRoom.healthKitsUsed++;
        }

        public void OnPlayerDeath()
        {
            if (_currentRoom != null) _currentRoom.deaths++;
        }

        public void OnDefectionWitnessed()
        {
            if (_currentRoom != null) _currentRoom.defectionsWitnessed++;
        }

        public void OnChallengeAccepted() => _history.challengesAccepted++;
        public void OnChallengeCompleted() => _history.challengesCompleted++;
        public void OnNewWeaponUsed() => _history.weaponTypesUsed++;
        public void OnBranchExplored() => _history.pathBranchesExplored++;

        /// <summary>Update live health/ammo for micro-adjustment calculations.</summary>
        public void UpdateLiveStats(float healthPercent, float ammoPercent)
        {
            _currentPlayerHealth = healthPercent;
            _currentPlayerAmmoPercent = ammoPercent;
        }

        // ====================================================================
        // Data Access — called by agents
        // ====================================================================

        public RunMetrics GetLastRunMetrics()
        {
            return _history.recentRuns.Count > 0
                ? _history.recentRuns[^1] : null;
        }

        public List<RunMetrics> GetRecentRuns(int count = 5)
        {
            int take = Mathf.Min(count, _history.recentRuns.Count);
            return _history.recentRuns.GetRange(_history.recentRuns.Count - take, take);
        }

        public RoomMetrics GetCurrentRoomMetrics() => _currentRoom;
        public RunMetrics GetCurrentRunMetrics() => _currentRun;
        public PlayerHistory GetHistory() => _history;
        public int GetWinLossStreak() => _history.winLossStreak;

        /// <summary>
        /// Builds the complete JSON payload for the Director Agent.
        /// Contains all 7 signals + retention metrics + history summary.
        /// </summary>
        public string GetDirectorInputJSON()
        {
            var lastRun = GetLastRunMetrics();
            var payload = new DirectorPayload
            {
                signals = new SignalBlock
                {
                    completion_time_avg = lastRun?.avgRoomClearTime ?? 0f,
                    accuracy_percent = lastRun != null ? lastRun.overallAccuracy * 100f : 0f,
                    death_count = lastRun?.totalDeaths ?? 0,
                    retry_behaviour = lastRun != null && lastRun.wasQuickRetry
                        ? "quick_retry" : "normal",
                    resource_efficiency = lastRun?.avgAmmoPerKill ?? 0f,
                    session_length = lastRun?.sessionLengthSeconds ?? 0f,
                    win_loss_streak = _history.winLossStreak
                },
                last_run = lastRun != null ? new RunSummary
                {
                    won = lastRun.won,
                    rooms_completed = lastRun.roomsCompleted,
                    kills = lastRun.totalKills,
                    deaths = lastRun.totalDeaths,
                    accuracy = lastRun.overallAccuracy,
                    avg_room_time = lastRun.avgRoomClearTime,
                    defections = lastRun.totalDefections,
                    churn_risk = lastRun.churnRiskScore,
                    improvement_delta = lastRun.improvementDelta
                } : null,
                history = new HistorySummary
                {
                    total_runs = _history.totalRuns,
                    total_wins = _history.totalWins,
                    total_losses = _history.totalLosses,
                    streak = _history.winLossStreak,
                    best_accuracy = _history.bestAccuracy,
                    quick_retries = _history.quickRetryCount,
                    challenges_accepted = _history.challengesAccepted,
                    challenges_completed = _history.challengesCompleted,
                    feature_exploration = _history.weaponTypesUsed + _history.pathBranchesExplored
                }
            };

            return JsonUtility.ToJson(payload, true);
        }

        // ====================================================================
        // Micro-Adjustment (between rooms, no Gemini call)
        // ====================================================================

        /// <summary>
        /// Computes small deterministic tweaks for the next room based on
        /// the player's current state. Called by the local game loop, NOT
        /// by Gemini agents. See GDD Ch05 "Mid-Run Micro-Adjustment".
        /// </summary>
        public MicroAdjustment GetMicroAdjustment()
        {
            var adj = new MicroAdjustment { enemyCountDelta = 0, reason = "" };

            // Health critically low → inject health kit
            if (_currentPlayerHealth < healthKitThreshold)
            {
                adj.addHealthKit = true;
                adj.reason = $"Player health {_currentPlayerHealth:P0} below {healthKitThreshold:P0} threshold.";
            }

            // Ammo critically low → inject ammo cache
            if (_currentPlayerAmmoPercent < ammoLowThreshold)
            {
                adj.addAmmoCache = true;
                adj.reason += $" Ammo {_currentPlayerAmmoPercent:P0} below threshold.";
            }

            // Last room cleared too fast → add enemy
            if (_currentRoom != null || (_currentRun?.roomBreakdown.Count > 0))
            {
                var lastCompleted = _currentRun?.roomBreakdown.LastOrDefault();
                if (lastCompleted != null && lastCompleted.clearDurationSeconds > 0)
                {
                    float ratio = lastCompleted.clearDurationSeconds / parRoomClearTime;
                    if (ratio < fastClearMultiplier)
                    {
                        adj.enemyCountDelta = 1;
                        adj.reason += $" Fast clear ({lastCompleted.clearDurationSeconds:F1}s vs {parRoomClearTime}s par) → +1 enemy.";
                    }
                    else if (ratio > slowClearMultiplier)
                    {
                        adj.enemyCountDelta = -1;
                        adj.reason += $" Slow clear ({lastCompleted.clearDurationSeconds:F1}s) → -1 enemy.";
                    }
                }
            }

            if (string.IsNullOrEmpty(adj.reason))
                adj.reason = "No adjustment needed.";

            return adj;
        }

        // ====================================================================
        // Internal
        // ====================================================================

        private void FinalizeRoom()
        {
            if (_currentRoom == null) return;

            _currentRoom.clearTime = Time.realtimeSinceStartup;
            _currentRoom.clearDurationSeconds = _currentRoom.clearTime - _currentRoom.enterTime;

            _currentRun?.roomBreakdown.Add(_currentRoom);

            if (logEvents)
                Debug.Log($"[PlayerMetrics] Room {_currentRoom.roomId} cleared: " +
                          $"{_currentRoom.clearDurationSeconds:F1}s, " +
                          $"Acc={_currentRoom.Accuracy:P0}, Kills={_currentRoom.enemiesKilled}");

            _currentRoom = null;
        }

        private void ComputeRetentionMetrics(RunMetrics run)
        {
            var prev = GetLastRunMetrics();

            // Improvement delta (accuracy difference from last run)
            run.improvementDelta = prev != null
                ? run.overallAccuracy - prev.overallAccuracy : 0f;

            // Satisfaction proxy (session length delta)
            run.satisfactionProxy = prev != null
                ? run.sessionLengthSeconds - prev.sessionLengthSeconds : 0f;

            // Churn risk score (0–1)
            float risk = 0f;
            if (!run.won) risk += 0.2f;
            if (run.wasQuickRetry) risk += 0.15f;
            if (_history.winLossStreak <= -3) risk += 0.3f;
            if (run.totalDeaths >= 3) risk += 0.15f;
            if (run.sessionLengthSeconds < 120f) risk += 0.2f; // Very short session
            run.churnRiskScore = Mathf.Clamp01(risk);
        }

        // ====================================================================
        // Persistence
        // ====================================================================

        public void SaveHistory()
        {
            try
            {
                string json = JsonUtility.ToJson(_history, true);
                File.WriteAllText(_savePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerMetrics] Save failed: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_savePath))
                {
                    string json = File.ReadAllText(_savePath);
                    _history = JsonUtility.FromJson<PlayerHistory>(json);
                    _history.recentRuns ??= new List<RunMetrics>();
                    Debug.Log($"[PlayerMetrics] Loaded history: {_history.totalRuns} runs, " +
                             $"streak={_history.winLossStreak}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerMetrics] Load failed: {ex.Message}");
                _history = new PlayerHistory();
            }
        }

        // ====================================================================
        // Director Payload Structures (for JSON serialization)
        // ====================================================================

        [Serializable]
        private class DirectorPayload
        {
            public SignalBlock signals;
            public RunSummary last_run;
            public HistorySummary history;
        }

        [Serializable]
        private class SignalBlock
        {
            public float completion_time_avg;
            public float accuracy_percent;
            public int death_count;
            public string retry_behaviour;
            public float resource_efficiency;
            public float session_length;
            public int win_loss_streak;
        }

        [Serializable]
        private class RunSummary
        {
            public bool won;
            public int rooms_completed;
            public int kills;
            public int deaths;
            public float accuracy;
            public float avg_room_time;
            public int defections;
            public float churn_risk;
            public float improvement_delta;
        }

        [Serializable]
        private class HistorySummary
        {
            public int total_runs;
            public int total_wins;
            public int total_losses;
            public int streak;
            public float best_accuracy;
            public int quick_retries;
            public int challenges_accepted;
            public int challenges_completed;
            public int feature_exploration;
        }
    }
}
