// ============================================================================
// SessionMetrics.cs — Shared metrics for Agentic vs Baseline comparison
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Tracks identical metrics in both modes so we can prove agentic > baseline.
// Persisted to JSON for post-session analysis.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threshold.Core
{
    /// <summary>
    /// Which director system is driving the game.
    /// </summary>
    public enum DirectorMode
    {
        AGENTIC,    // Full Gemini 5-agent pipeline
        BASELINE    // FixedRuleDirector — dumb thresholds, no reasoning
    }

    /// <summary>
    /// Metrics collected per run (one dungeon attempt).
    /// </summary>
    [Serializable]
    public class RunMetrics
    {
        public int runNumber;
        public float durationSeconds;
        public int roomsCompleted;
        public int totalRooms;
        public int deaths;
        public int kills;
        public int defectionEvents;
        public bool runWon;
        public int difficultyLevel;
        public string timestamp;
    }

    /// <summary>
    /// Aggregate session metrics — persisted for A/B comparison.
    /// Identical structure used by both Agentic and Baseline modes.
    /// </summary>
    [Serializable]
    public class SessionMetrics
    {
        // ====================================================================
        // Identity
        // ====================================================================

        /// <summary>Which mode produced these metrics.</summary>
        public DirectorMode mode;

        /// <summary>Session start timestamp.</summary>
        public string sessionStartTime;

        // ====================================================================
        // Aggregate Stats
        // ====================================================================

        /// <summary>Total time across all runs in this session.</summary>
        public float totalSessionSeconds;

        /// <summary>Number of runs attempted.</summary>
        public int totalRuns;

        /// <summary>Number of runs won (reached EXIT).</summary>
        public int runsWon;

        /// <summary>Number of runs where player died and retried.</summary>
        public int retries;

        /// <summary>Total rooms completed across all runs.</summary>
        public int totalRoomsCompleted;

        /// <summary>Total kills across all runs.</summary>
        public int totalKills;

        /// <summary>Total deaths across all runs.</summary>
        public int totalDeaths;

        /// <summary>Total NPC defection events (agentic only, 0 for baseline).</summary>
        public int totalDefectionEvents;

        /// <summary>Per-run breakdown.</summary>
        public List<RunMetrics> runs = new();

        // ====================================================================
        // Computed Properties
        // ====================================================================

        /// <summary>Win rate as a percentage (0–100).</summary>
        public float WinRate => totalRuns > 0 ? (runsWon / (float)totalRuns) * 100f : 0f;

        /// <summary>Retry rate (deaths per run).</summary>
        public float RetryRate => totalRuns > 0 ? retries / (float)totalRuns : 0f;

        /// <summary>Average session length per run.</summary>
        public float AvgRunDuration => totalRuns > 0 ? totalSessionSeconds / totalRuns : 0f;

        /// <summary>Average rooms completed per run.</summary>
        public float AvgRoomsPerRun => totalRuns > 0 ? totalRoomsCompleted / (float)totalRuns : 0f;

        // ====================================================================
        // API
        // ====================================================================

        /// <summary>
        /// Records a completed run.
        /// </summary>
        public void RecordRun(RunMetrics run)
        {
            run.runNumber = totalRuns;
            run.timestamp = DateTime.Now.ToString("o");
            runs.Add(run);

            totalRuns++;
            totalSessionSeconds += run.durationSeconds;
            totalRoomsCompleted += run.roomsCompleted;
            totalKills += run.kills;
            totalDeaths += run.deaths;
            totalDefectionEvents += run.defectionEvents;

            if (run.runWon) runsWon++;
            if (run.deaths > 0) retries++;
        }

        /// <summary>
        /// Saves metrics to persistent storage.
        /// </summary>
        public void Save()
        {
            string filename = $"session_metrics_{mode}_{sessionStartTime.Replace(":", "-")}.json";
            string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            string json = JsonUtility.ToJson(this, true);
            System.IO.File.WriteAllText(path, json);
            Debug.Log($"[SessionMetrics] Saved {mode} metrics to {path}");
        }

        /// <summary>
        /// Logs a comparison summary between two session metrics.
        /// </summary>
        public static void LogComparison(SessionMetrics agentic, SessionMetrics baseline)
        {
            Debug.Log("═══════════════════════════════════════════════════");
            Debug.Log("  AGENTIC vs BASELINE COMPARISON");
            Debug.Log("═══════════════════════════════════════════════════");
            Debug.Log($"  Metric          | Agentic     | Baseline");
            Debug.Log($"  ────────────────┼─────────────┼───────────");
            Debug.Log($"  Win Rate        | {agentic.WinRate,8:F1}%   | {baseline.WinRate,8:F1}%");
            Debug.Log($"  Avg Duration    | {agentic.AvgRunDuration,8:F1}s   | {baseline.AvgRunDuration,8:F1}s");
            Debug.Log($"  Retry Rate      | {agentic.RetryRate,8:F2}    | {baseline.RetryRate,8:F2}");
            Debug.Log($"  Avg Rooms/Run   | {agentic.AvgRoomsPerRun,8:F1}    | {baseline.AvgRoomsPerRun,8:F1}");
            Debug.Log($"  Defections      | {agentic.totalDefectionEvents,8}    | {baseline.totalDefectionEvents,8}");
            Debug.Log($"  Total Runs      | {agentic.totalRuns,8}    | {baseline.totalRuns,8}");
            Debug.Log("═══════════════════════════════════════════════════");
        }

        /// <summary>
        /// Creates a fresh session for the given mode.
        /// </summary>
        public static SessionMetrics StartSession(DirectorMode mode)
        {
            return new SessionMetrics
            {
                mode = mode,
                sessionStartTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
            };
        }
    }
}
