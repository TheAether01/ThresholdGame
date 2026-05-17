// ============================================================================
// LevelGenerationPipeline.cs — Async orchestrator: Director → LevelGen → QC → Build
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Coordinates the complete run-start flow with QC reject/regen retry loop.
// Falls back to ProceduralRoomGenerator when Gemini fails 3 times.
// Every step is logged for hackathon submission evidence.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Threshold.Agents;
using Threshold.Core;
using Threshold.Player;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Threshold.Generation
{
    /// <summary>
    /// Pipeline result returned to the game loop after a new run is built.
    /// </summary>
    public class PipelineResult
    {
        public bool success;
        public RoomGraphConfig config;
        public DifficultyProfile difficulty;
        public string directorDecisionText;
        public Vector3 entryPosition;
        public Vector3 exitPosition;
        public string generationSource;    // "gemini", "fallback"
        public int qcAttempts;
        public float totalPipelineTimeMs;
        public List<PipelineStep> steps;
    }

    /// <summary>
    /// A single logged step in the pipeline for hackathon trace export.
    /// </summary>
    [Serializable]
    public class PipelineStep
    {
        public string stepName;
        public string source;           // "gemini" or "local"
        public bool success;
        public float durationMs;
        public string details;
        public AgentTrace trace;
    }

    /// <summary>
    /// Orchestrates the full level generation pipeline at each run start.
    /// Attach to a persistent GameObject alongside FloorGenerator and
    /// LayoutHistoryManager.
    /// </summary>
    public class LevelGenerationPipeline : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FloorGenerator floorGenerator;
        [SerializeField] private LayoutHistoryManager historyManager;

        [Header("Pipeline Settings")]
        [Tooltip("Max QC rejection attempts before falling back to local generator.")]
        [SerializeField] private int maxQcRetries = 3;

        [Header("Debug")]
        [SerializeField] private bool logPipeline = true;

        /// <summary>The result of the last pipeline run.</summary>
        public PipelineResult LastResult { get; private set; }

        /// <summary>True while a pipeline is executing.</summary>
        public bool IsRunning { get; private set; }

        // ====================================================================
        // Events (subscribe from UI/game loop)
        // ====================================================================

        public event Action<PipelineResult> OnPipelineComplete;
        public event Action<string> OnPipelineStatusChanged;

        // ====================================================================
        // Main Pipeline
        // ====================================================================

        /// <summary>
        /// Starts the full Director → LevelGen → QC → Build pipeline.
        /// Call this at each run start. Async, non-blocking.
        /// </summary>
        public async Task<PipelineResult> StartNewRun()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Pipeline] Already running. Ignoring duplicate call.");
                return LastResult;
            }

            IsRunning = true;
            var totalTimer = Stopwatch.StartNew();
            var steps = new List<PipelineStep>();
            var result = new PipelineResult { steps = steps };

            try
            {
                // ==============================================================
                // STEP 1: Director Agent — get DifficultyProfile
                // ==============================================================
                SetStatus("Consulting Director Agent...");
                var dirStep = await RunDirectorStep();
                steps.Add(dirStep);

                if (!dirStep.success || !(dirStep is DirectorPipelineStep dps))
                {
                    Debug.LogError("[Pipeline] Director step failed. Using default difficulty.");
                    result.difficulty = new DifficultyProfile();
                    result.directorDecisionText = "Using default balanced settings.";
                }
                else
                {
                    result.difficulty = dps.profile;
                    result.directorDecisionText = dps.decisionText;
                }

                // ==============================================================
                // STEP 2+3: Level Gen + QC retry loop
                // ==============================================================
                RoomGraphConfig acceptedConfig = null;
                int attempt = 0;
                string lastRejectionReason = null;

                while (attempt < maxQcRetries && acceptedConfig == null)
                {
                    attempt++;
                    SetStatus($"Generating level (attempt {attempt}/{maxQcRetries})...");

                    // STEP 2: Level Gen Agent
                    var genStep = await RunLevelGenStep(result.difficulty, lastRejectionReason, attempt);
                    steps.Add(genStep);

                    if (!genStep.success || !(genStep is LevelGenPipelineStep lgStep) || lgStep.config == null)
                    {
                        lastRejectionReason = "Level Gen Agent returned invalid config.";
                        Debug.LogWarning($"[Pipeline] Level Gen attempt {attempt} failed: {lastRejectionReason}");
                        continue;
                    }

                    // Local validation first (fast, free)
                    var localIssues = ProceduralRoomGenerator.Validate(lgStep.config);
                    if (localIssues.Count > 0)
                    {
                        lastRejectionReason = $"Local validation: {string.Join("; ", localIssues)}";
                        steps.Add(new PipelineStep
                        {
                            stepName = $"Local Validation (attempt {attempt})",
                            source = "local",
                            success = false,
                            durationMs = 0,
                            details = lastRejectionReason
                        });
                        Debug.LogWarning($"[Pipeline] Local validation failed: {lastRejectionReason}");
                        continue;
                    }

                    // STEP 3: QC Agent
                    SetStatus($"QC validation (attempt {attempt})...");
                    var qcStep = await RunQcStep(lgStep.config, attempt);
                    steps.Add(qcStep);

                    if (qcStep.success)
                    {
                        acceptedConfig = lgStep.config;
                    }
                    else
                    {
                        lastRejectionReason = qcStep.details;
                        Debug.LogWarning($"[Pipeline] QC rejected attempt {attempt}: {lastRejectionReason}");
                    }
                }

                // ==============================================================
                // STEP 4: Fallback if all attempts failed
                // ==============================================================
                if (acceptedConfig == null)
                {
                    SetStatus("Gemini failed — using local fallback generator...");
                    var fallbackTimer = Stopwatch.StartNew();

                    acceptedConfig = ProceduralRoomGenerator.GenerateFallback(result.difficulty);
                    fallbackTimer.Stop();

                    steps.Add(new PipelineStep
                    {
                        stepName = "Fallback Generator",
                        source = "local",
                        success = true,
                        durationMs = fallbackTimer.ElapsedMilliseconds,
                        details = $"Generated {acceptedConfig.rooms.Count} rooms locally after {attempt} Gemini failures."
                    });

                    result.generationSource = "fallback";
                    Debug.Log("[Pipeline] Fallback generator produced a valid layout.");
                }
                else
                {
                    result.generationSource = "gemini";
                }

                result.config = acceptedConfig;
                result.qcAttempts = attempt;
                acceptedConfig.metadata ??= new LayoutMetadata();
                acceptedConfig.metadata.qcAttempts = attempt;

                // ==============================================================
                // STEP 5: Save to history for novelty comparison
                // ==============================================================
                if (historyManager != null)
                {
                    float novelty = historyManager.CalculateNoveltyScore(acceptedConfig);
                    acceptedConfig.metadata.noveltyScore = novelty;
                    historyManager.RecordLayout(acceptedConfig);

                    steps.Add(new PipelineStep
                    {
                        stepName = "Novelty Check",
                        source = "local",
                        success = true,
                        details = $"Novelty score: {novelty:F2} (vs {historyManager.HistoryCount} recent layouts)"
                    });
                }

                // ==============================================================
                // STEP 6: Build the physical floor
                // ==============================================================
                SetStatus("Building floor...");
                var buildTimer = Stopwatch.StartNew();
                bool buildOk = floorGenerator != null && floorGenerator.BuildFloor(acceptedConfig);
                buildTimer.Stop();

                steps.Add(new PipelineStep
                {
                    stepName = "Floor Build",
                    source = "local",
                    success = buildOk,
                    durationMs = buildTimer.ElapsedMilliseconds,
                    details = buildOk
                        ? $"Instantiated {acceptedConfig.rooms.Count} rooms."
                        : "FloorGenerator.BuildFloor failed or not assigned."
                });

                if (buildOk)
                {
                    result.entryPosition = floorGenerator.EntryWorldPosition;
                    result.exitPosition = floorGenerator.ExitWorldPosition;
                }

                result.success = buildOk;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipeline] Unhandled exception: {ex.Message}\n{ex.StackTrace}");
                result.success = false;
                steps.Add(new PipelineStep
                {
                    stepName = "Exception",
                    source = "local",
                    success = false,
                    details = ex.Message
                });
            }
            finally
            {
                totalTimer.Stop();
                result.totalPipelineTimeMs = totalTimer.ElapsedMilliseconds;
                LastResult = result;
                IsRunning = false;

                SetStatus(result.success ? "Pipeline complete." : "Pipeline failed.");
                LogPipelineSummary(result);
                OnPipelineComplete?.Invoke(result);
            }

            return result;
        }

        // ====================================================================
        // Individual Steps
        // ====================================================================

        private async Task<PipelineStep> RunDirectorStep()
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var dirResult = await DirectorAgentCaller.CallDirector();
                timer.Stop();

                return new DirectorPipelineStep
                {
                    stepName = "Director Agent",
                    source = dirResult.source,
                    success = dirResult.success,
                    durationMs = timer.ElapsedMilliseconds,
                    details = dirResult.decisionText,
                    trace = dirResult.trace,
                    profile = dirResult.profile,
                    decisionText = dirResult.decisionText
                };
            }
            catch (Exception ex)
            {
                timer.Stop();
                return new PipelineStep
                {
                    stepName = "Director Agent",
                    source = "error",
                    success = false,
                    durationMs = timer.ElapsedMilliseconds,
                    details = ex.Message
                };
            }
        }

        private async Task<PipelineStep> RunLevelGenStep(DifficultyProfile difficulty,
            string previousRejection, int attempt)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                string prompt = BuildLevelGenPrompt(difficulty, previousRejection);
                string gameState = JsonUtility.ToJson(difficulty);

                // Include rejection context if retrying
                if (!string.IsNullOrEmpty(previousRejection))
                {
                    gameState = $"{{\"difficulty\": {gameState}, " +
                                $"\"previous_rejection\": \"{EscapeJson(previousRejection)}\", " +
                                $"\"attempt\": {attempt}}}";
                }

                var request = new AgentRequest(
                    agentName: "level_gen",
                    systemPrompt: prompt,
                    gameStateJson: gameState,
                    model: GeminiModel.Flash
                );

                var response = await GeminiAgentBridge.Instance.SendAgentRequest(request);
                timer.Stop();

                if (!response.success)
                {
                    return new LevelGenPipelineStep
                    {
                        stepName = $"Level Gen (attempt {attempt})",
                        source = "gemini_level_gen",
                        success = false,
                        durationMs = timer.ElapsedMilliseconds,
                        details = response.error,
                        trace = response.trace
                    };
                }

                // Parse config from action field
                var config = ParseLevelConfig(response.trace?.action);

                return new LevelGenPipelineStep
                {
                    stepName = $"Level Gen (attempt {attempt})",
                    source = "gemini_level_gen",
                    success = config != null,
                    durationMs = timer.ElapsedMilliseconds,
                    details = config != null
                        ? $"Generated {config.rooms?.Count ?? 0} rooms."
                        : "Failed to parse level config from agent response.",
                    trace = response.trace,
                    config = config
                };
            }
            catch (Exception ex)
            {
                timer.Stop();
                return new LevelGenPipelineStep
                {
                    stepName = $"Level Gen (attempt {attempt})",
                    source = "error",
                    success = false,
                    durationMs = timer.ElapsedMilliseconds,
                    details = ex.Message
                };
            }
        }

        private async Task<PipelineStep> RunQcStep(RoomGraphConfig config, int attempt)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                string qcPrompt = BuildQcPrompt();
                string configJson = JsonUtility.ToJson(config);

                var request = new AgentRequest(
                    agentName: "qc",
                    systemPrompt: qcPrompt,
                    gameStateJson: configJson,
                    model: GeminiModel.Flash
                );

                var response = await GeminiAgentBridge.Instance.SendAgentRequest(request);
                timer.Stop();

                if (!response.success)
                {
                    // If QC agent is down, accept based on local validation passing
                    return new PipelineStep
                    {
                        stepName = $"QC Agent (attempt {attempt})",
                        source = "local_passthrough",
                        success = true,
                        durationMs = timer.ElapsedMilliseconds,
                        details = "QC agent unavailable — accepted on local validation.",
                        trace = response.trace
                    };
                }

                // Parse QC verdict from action field
                bool accepted = ParseQcVerdict(response.trace?.action, out string reason);

                return new PipelineStep
                {
                    stepName = $"QC Agent (attempt {attempt})",
                    source = "gemini_qc",
                    success = accepted,
                    durationMs = timer.ElapsedMilliseconds,
                    details = accepted ? "ACCEPTED" : $"REJECTED: {reason}",
                    trace = response.trace
                };
            }
            catch (Exception ex)
            {
                timer.Stop();
                return new PipelineStep
                {
                    stepName = $"QC Agent (attempt {attempt})",
                    source = "error",
                    success = true, // Accept on error — local validation already passed
                    durationMs = timer.ElapsedMilliseconds,
                    details = $"QC exception (accepted on local validation): {ex.Message}"
                };
            }
        }

        // ====================================================================
        // Prompt Builders
        // ====================================================================

        private string BuildLevelGenPrompt(DifficultyProfile difficulty, string rejection)
        {
            string base_prompt = @"You are the LEVEL GENERATION AGENT for THRESHOLD.

Given a DifficultyProfile, produce a complete room graph config.

ROOM SHAPES (by doorway pattern):
- CROSSROADS: 4 doorways (N,E,S,W)
- T_JUNCTION: 3 doorways
- STRAIGHT: 2 opposite doorways
- CORNER: 2 adjacent doorways
- DEAD_END: 1 doorway

GAMEPLAY ROLES (independent of shape):
- ENTRY: player spawn, no enemies
- EXIT: run endpoint
- PACING: breathing room, resources
- COMBAT: standard encounter
- AMBUSH: surprise spawns
- BOSS: elite encounter (prefer CROSSROADS)
- LOOT: optional branch with rewards
- CHOKE: narrow tactical room

RULES:
- Exactly 1 ENTRY and 1 EXIT
- All rooms must be connected (reachable from ENTRY)
- ENTRY and EXIT must have no enemy spawns
- Connected rooms must have matching doorways
- BOSS rooms should be CROSSROADS when possible

Output a JSON RoomGraphConfig with rooms, edges, and metadata.";

            if (!string.IsNullOrEmpty(rejection))
            {
                base_prompt += $"\n\nPREVIOUS ATTEMPT WAS REJECTED. Reason: {rejection}\n" +
                               "Fix the issues and regenerate.";
            }

            return base_prompt;
        }

        private string BuildQcPrompt()
        {
            return @"You are the QC (Quality Control) AGENT for THRESHOLD.

You receive a RoomGraphConfig and must validate it for playability.

CHECK ALL OF THESE:
1. Connectivity: all rooms reachable from ENTRY via BFS
2. Spawn safety: no enemies in ENTRY or EXIT rooms
3. Doorway consistency: connected rooms have matching doorways
4. Role constraints: exactly 1 ENTRY, exactly 1 EXIT
5. Solvability: path exists from ENTRY to EXIT
6. Balance: enemy count appropriate for difficulty multiplier

OUTPUT your action as JSON:
{
  ""status"": ""ACCEPTED"" or ""REJECTED"",
  ""failures"": [""list of specific failure reasons""],
  ""validation_checks"": <number of checks performed>,
  ""passed"": <number passed>
}

If ANY check fails, REJECT and list all failures.";
        }

        // ====================================================================
        // Parsers
        // ====================================================================

        private RoomGraphConfig ParseLevelConfig(string actionJson)
        {
            if (string.IsNullOrWhiteSpace(actionJson)) return null;
            try
            {
                string clean = StripCodeFences(actionJson);
                return JsonUtility.FromJson<RoomGraphConfig>(clean);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipeline] Failed to parse level config: {ex.Message}");
                return null;
            }
        }

        private bool ParseQcVerdict(string actionJson, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(actionJson))
            {
                reason = "Empty QC response";
                return false;
            }

            try
            {
                string clean = StripCodeFences(actionJson);

                // Check for ACCEPTED/REJECTED in the text
                if (clean.Contains("ACCEPTED", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (clean.Contains("REJECTED", StringComparison.OrdinalIgnoreCase))
                {
                    reason = clean;
                    return false;
                }

                // Try JSON parse
                var qc = JsonUtility.FromJson<QcOutputRaw>(clean);
                if (qc != null)
                {
                    if (qc.status == "ACCEPTED") return true;
                    reason = qc.failures != null ? string.Join("; ", qc.failures) : qc.status;
                    return false;
                }
            }
            catch { /* fall through */ }

            reason = "Could not parse QC verdict.";
            return false;
        }

        // ====================================================================
        // Logging
        // ====================================================================

        private void LogPipelineSummary(PipelineResult result)
        {
            if (!logPipeline) return;

            Debug.Log("╔══════════════════════════════════════════════╗");
            Debug.Log("║   THRESHOLD — Level Generation Pipeline      ║");
            Debug.Log("╚══════════════════════════════════════════════╝");
            Debug.Log($"  Result:     {(result.success ? "✓ SUCCESS" : "✗ FAILED")}");
            Debug.Log($"  Source:     {result.generationSource}");
            Debug.Log($"  QC Attempts:{result.qcAttempts}");
            Debug.Log($"  Total Time: {result.totalPipelineTimeMs}ms");
            Debug.Log($"  Rooms:      {result.config?.rooms?.Count ?? 0}");
            Debug.Log("  ── Steps ──");

            foreach (var step in result.steps)
            {
                string icon = step.success ? "✓" : "✗";
                Debug.Log($"  {icon} {step.stepName} [{step.source}] " +
                          $"{step.durationMs}ms — {Truncate(step.details, 80)}");
            }

            Debug.Log("──────────────────────────────────────────────");

            if (GeminiAgentBridge.Instance != null)
                Debug.Log(GeminiAgentBridge.Instance.GetUsageReport());
        }

        private void SetStatus(string status)
        {
            if (logPipeline) Debug.Log($"[Pipeline] {status}");
            OnPipelineStatusChanged?.Invoke(status);
        }

        // ====================================================================
        // Utility
        // ====================================================================

        private static string StripCodeFences(string text)
        {
            string clean = text.Trim();
            if (clean.StartsWith("```"))
            {
                int nl = clean.IndexOf('\n');
                int lf = clean.LastIndexOf("```");
                if (nl > 0 && lf > nl)
                    clean = clean.Substring(nl + 1, lf - nl - 1).Trim();
            }
            return clean;
        }

        private static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";

        // ====================================================================
        // Internal Types
        // ====================================================================

        private class DirectorPipelineStep : PipelineStep
        {
            public DifficultyProfile profile;
            public string decisionText;
        }

        private class LevelGenPipelineStep : PipelineStep
        {
            public RoomGraphConfig config;
        }

        [Serializable]
        private class QcOutputRaw
        {
            public string status;
            public string[] failures;
            public int validation_checks;
            public int passed;
        }
    }
}
