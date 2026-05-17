// ============================================================================
// DirectorAgentCaller.cs — Director Agent interface for THRESHOLD
// Google Antigravity Mobile Game Challenge 2026
//
// The Director is the top-level agent that observes ALL 7 player signals
// and produces a DifficultyProfile for the next run. Its reasoning must
// be contextual and agentic — NOT simple threshold rules.
// ============================================================================

using System;
using System.Threading.Tasks;
using Threshold.Core;
using Threshold.Player;
using UnityEngine;

namespace Threshold.Agents
{
    /// <summary>
    /// Builds the Director Agent prompt, calls Gemini, and parses the
    /// response into a DifficultyProfile. Includes fallback logic when
    /// the API is unavailable or returns invalid data.
    /// </summary>
    public static class DirectorAgentCaller
    {
        private const string AgentName = "director";

        // Last successful profile for fallback
        private static DifficultyProfile _lastProfile;
        private static string _lastDecisionText;

        /// <summary>Human-readable explanation of the last decision.</summary>
        public static string LastDecisionText => _lastDecisionText ?? "No decision yet.";

        // ====================================================================
        // System Prompt
        // ====================================================================

        private static readonly string SystemPrompt = @"
You are the DIRECTOR AGENT for THRESHOLD, a top-down roguelite corridor shooter.

YOUR ROLE:
You are the top-level game master. You observe ALL player performance data
simultaneously and decide the difficulty profile for the next run. Your
decisions shape every aspect of the upcoming experience.

CRITICAL REQUIREMENT — AGENTIC REASONING:
You MUST reason contextually about the WHOLE player picture. Simple threshold
rules like ""if deaths > 3, reduce difficulty"" are UNACCEPTABLE.

Instead you must:
- Consider WHY the player is dying (low accuracy? bad positioning? too many elites?)
- Consider the TREND across recent runs (improving? plateauing? declining?)
- Consider the EMOTIONAL state (quick retries = frustration, long gaps = boredom)
- Consider the INTERPLAY between signals (high accuracy + many deaths = enemies too tanky, not too many)
- Produce decisions that address ROOT CAUSES, not symptoms

THE 7 PLAYER SIGNALS YOU RECEIVE:
1. completion_time_avg — average seconds to clear a room
2. accuracy_percent — shot accuracy (0–100)
3. death_count — deaths in the last run
4. retry_behaviour — 'quick_retry' (< 5s, frustrated) or 'normal'
5. resource_efficiency — ammo spent per kill (lower = more efficient)
6. session_length — total play time of last run in seconds
7. win_loss_streak — signed integer (+3 = won last 3, -2 = lost last 2)

ADDITIONAL CONTEXT:
- history.total_runs, total_wins, total_losses
- history.best_accuracy, quick_retries, challenges_accepted/completed
- history.feature_exploration (how much of the game they've seen)
- last_run.churn_risk (0–1, estimated likelihood player quits)
- last_run.improvement_delta (accuracy change from previous run)

YOUR OUTPUT (strict JSON):
{
  ""difficulty_multiplier"": <float 0.5–2.5>,
  ""target_room_count"": <int 5–12>,
  ""base_enemies_per_room"": <int 2–6>,
  ""elite_count"": <int 0–3>,
  ""event_probability"": <float 0.0–1.0>,
  ""preferred_tactic"": ""ATTACK"" | ""FLANK"" | ""SUPPRESS"" | ""MIXED"",
  ""par_room_time"": <float seconds>,
  ""target_win_rate"": <float 0.3–0.7>,
  ""decision_explanation"": ""<2-3 sentence explanation of your reasoning for the player to read>""
}

EXAMPLE REASONING (do NOT copy, reason fresh each time):
A player with 72% accuracy but 22s room times and a -2 loss streak isn't
struggling with aim — they're being too cautious. Rather than reducing
enemy count, I should reduce SUPPRESSOR enemies (which pin players down)
and add more GRUNT enemies that reward aggressive play. The fast clear
time bonus will teach them to push forward.

Remember: your decision_explanation will be shown to the player on the
between-runs screen. Be specific about what you changed and why. Address
the player directly (""I noticed you..."").
".Trim();

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Calls the Director Agent with current player metrics and returns
        /// a DifficultyProfile. Falls back to adjusted previous profile
        /// if Gemini is unavailable.
        /// </summary>
        public static async Task<DirectorResult> CallDirector()
        {
            // Gather player data
            string gameStateJson;
            if (PlayerMetricsTracker.Instance != null)
            {
                gameStateJson = PlayerMetricsTracker.Instance.GetDirectorInputJSON();
            }
            else
            {
                gameStateJson = BuildDefaultGameState();
            }

            // Build request
            var request = new AgentRequest(
                agentName: AgentName,
                systemPrompt: SystemPrompt,
                gameStateJson: gameStateJson,
                model: GeminiModel.Flash // Director uses Flash for speed
            );

            // Call Gemini
            var response = await GeminiAgentBridge.Instance.SendAgentRequest(request);

            if (response.success && response.trace != null)
            {
                // Parse the action field as DifficultyProfile
                var profile = ParseProfile(response.trace.action);
                if (profile != null)
                {
                    _lastProfile = profile;
                    _lastDecisionText = ExtractDecisionText(response.trace);
                    return new DirectorResult
                    {
                        success = true,
                        profile = profile,
                        decisionText = _lastDecisionText,
                        source = "gemini_director",
                        trace = response.trace
                    };
                }

                Debug.LogWarning("[DirectorAgent] Failed to parse profile from trace action. Using fallback.");
            }
            else
            {
                Debug.LogWarning($"[DirectorAgent] API call failed: {response.error}. Using fallback.");
            }

            // Fallback
            return BuildFallback();
        }

        // ====================================================================
        // Parsing
        // ====================================================================

        private static DifficultyProfile ParseProfile(string actionJson)
        {
            if (string.IsNullOrWhiteSpace(actionJson)) return null;

            try
            {
                // Strip markdown code fences if present
                string clean = actionJson.Trim();
                if (clean.StartsWith("```"))
                {
                    int firstNewline = clean.IndexOf('\n');
                    int lastFence = clean.LastIndexOf("```");
                    if (firstNewline > 0 && lastFence > firstNewline)
                        clean = clean.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }

                var raw = JsonUtility.FromJson<DirectorOutputRaw>(clean);
                if (raw == null) return null;

                return new DifficultyProfile
                {
                    difficultyMultiplier = Mathf.Clamp(raw.difficulty_multiplier, 0.5f, 2.5f),
                    targetRoomCount = Mathf.Clamp(raw.target_room_count, 5, 12),
                    baseEnemiesPerRoom = Mathf.Clamp(raw.base_enemies_per_room, 2, 6),
                    eliteCount = Mathf.Clamp(raw.elite_count, 0, 3),
                    eventProbability = Mathf.Clamp01(raw.event_probability),
                    preferredTactic = raw.preferred_tactic ?? "ATTACK"
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectorAgent] JSON parse error: {ex.Message}");
                return null;
            }
        }

        private static string ExtractDecisionText(AgentTrace trace)
        {
            // Try to extract decision_explanation from the action JSON
            if (!string.IsNullOrEmpty(trace.action))
            {
                try
                {
                    string clean = trace.action.Trim();
                    if (clean.StartsWith("```"))
                    {
                        int nl = clean.IndexOf('\n');
                        int lf = clean.LastIndexOf("```");
                        if (nl > 0 && lf > nl)
                            clean = clean.Substring(nl + 1, lf - nl - 1).Trim();
                    }

                    var raw = JsonUtility.FromJson<DirectorOutputRaw>(clean);
                    if (!string.IsNullOrEmpty(raw?.decision_explanation))
                        return raw.decision_explanation;
                }
                catch { /* fall through */ }
            }

            // Fall back to the decision field of the 5-step trace
            return !string.IsNullOrEmpty(trace.decision) ? trace.decision : "Adjusting difficulty based on your performance.";
        }

        // ====================================================================
        // Fallback
        // ====================================================================

        private static DirectorResult BuildFallback()
        {
            DifficultyProfile profile;

            if (_lastProfile != null)
            {
                // Adjust previous profile by ±0.2 based on streak
                profile = CloneProfile(_lastProfile);
                int streak = PlayerMetricsTracker.Instance != null
                    ? PlayerMetricsTracker.Instance.GetWinLossStreak() : 0;

                if (streak <= -2)
                    profile.difficultyMultiplier = Mathf.Max(0.5f, profile.difficultyMultiplier - 0.2f);
                else if (streak >= 2)
                    profile.difficultyMultiplier = Mathf.Min(2.5f, profile.difficultyMultiplier + 0.2f);
            }
            else
            {
                profile = new DifficultyProfile(); // Defaults: 1.0x, 7 rooms, 3 enemies
            }

            _lastProfile = profile;
            _lastDecisionText = "Using balanced settings based on your recent performance.";

            return new DirectorResult
            {
                success = true,
                profile = profile,
                decisionText = _lastDecisionText,
                source = "local_fallback",
                trace = null
            };
        }

        private static DifficultyProfile CloneProfile(DifficultyProfile src) => new()
        {
            difficultyMultiplier = src.difficultyMultiplier,
            targetRoomCount = src.targetRoomCount,
            baseEnemiesPerRoom = src.baseEnemiesPerRoom,
            eliteCount = src.eliteCount,
            eventProbability = src.eventProbability,
            preferredTactic = src.preferredTactic
        };

        private static string BuildDefaultGameState() => @"{
            ""signals"": {
                ""completion_time_avg"": 20.0,
                ""accuracy_percent"": 50.0,
                ""death_count"": 1,
                ""retry_behaviour"": ""normal"",
                ""resource_efficiency"": 3.0,
                ""session_length"": 300,
                ""win_loss_streak"": 0
            },
            ""history"": { ""total_runs"": 0, ""total_wins"": 0, ""total_losses"": 0, ""streak"": 0 }
        }";

        // ====================================================================
        // Types
        // ====================================================================

        [Serializable]
        private class DirectorOutputRaw
        {
            public float difficulty_multiplier;
            public int target_room_count;
            public int base_enemies_per_room;
            public int elite_count;
            public float event_probability;
            public string preferred_tactic;
            public float par_room_time;
            public float target_win_rate;
            public string decision_explanation;
        }
    }

    /// <summary>
    /// Result from calling the Director Agent.
    /// </summary>
    public class DirectorResult
    {
        public bool success;
        public DifficultyProfile profile;
        public string decisionText;
        public string source; // "gemini_director" or "local_fallback"
        public AgentTrace trace;
    }
}
