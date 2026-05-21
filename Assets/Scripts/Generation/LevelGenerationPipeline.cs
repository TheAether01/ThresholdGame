// ============================================================================
// LevelGenerationPipeline.cs — Async generation orchestrator
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Two generation modes (toggled via Inspector):
//   AI_Full:  Director → LevelGen(Pro) → QC(Flash) → Build   (~100s, old)
//   Hybrid:   Director → Local Spatial  → AI Populate(Flash) → Build  (~6s)
//
// Falls back to ProceduralRoomGenerator.GenerateFallback() when all else fails.
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
    /// Controls how the pipeline generates spatial layouts.
    /// Toggle via Inspector on the LevelGenerationPipeline component.
    /// </summary>
    public enum GenerationMode
    {
        /// <summary>
        /// Full AI pipeline: Director → LevelGen(Pro) → QC(Flash) → Build.
        /// Slower (~100s) but the AI handles all spatial reasoning.
        /// </summary>
        AI_Full,

        /// <summary>
        /// Hybrid pipeline: Director → Local Spatial Gen → AI Populate(Flash) → Build.
        /// Fast (~6s), spatial correctness guaranteed by code, AI assigns roles creatively.
        /// </summary>
        Hybrid
    }
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
        [Tooltip("AI_Full: LLM generates spatial layout (slow, ~100s).\n" +
                 "Hybrid: Local code generates layout, AI assigns roles (fast, ~6s).")]
        [SerializeField] private GenerationMode generationMode = GenerationMode.Hybrid;

        [Tooltip("Max QC rejection attempts before falling back to local generator (AI_Full mode only).")]
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
                // M10 FIX: Return null so callers know generation is in progress
                // (previously returned LastResult which could be stale)
                Debug.LogWarning("[Pipeline] Already running. Ignoring duplicate call.");
                return null;
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
                // BRANCH: AI_Full vs Hybrid generation mode
                // ==============================================================
                RoomGraphConfig acceptedConfig = null;
                int attempt = 0;

                if (generationMode == GenerationMode.Hybrid)
                {
                    // ══════════════════════════════════════════════════════
                    // HYBRID MODE: Local spatial gen + AI Populate agent
                    // ══════════════════════════════════════════════════════
                    attempt = 1;

                    // STEP 2: Local spatial generation (instant, always valid)
                    SetStatus("Generating layout (local)...");
                    var spatialTimer = Stopwatch.StartNew();
                    int seed = Environment.TickCount;
                    acceptedConfig = ProceduralRoomGenerator.GenerateLayout(result.difficulty, seed);
                    spatialTimer.Stop();

                    bool spatialOk = acceptedConfig != null && acceptedConfig.rooms.Count > 0;
                    steps.Add(new PipelineStep
                    {
                        stepName = "Spatial Generation (Local)",
                        source = "local_procedural",
                        success = spatialOk,
                        durationMs = spatialTimer.ElapsedMilliseconds,
                        details = spatialOk
                            ? $"Generated {acceptedConfig.rooms.Count} rooms, seed={seed}"
                            : "Local spatial generation returned null or empty."
                    });

                    if (spatialOk)
                    {
                        // STEP 3: AI Populate Agent (Flash, ~3s)
                        SetStatus("AI populating rooms...");
                        var populateStep = await RunPopulateStep(acceptedConfig, result.difficulty);
                        steps.Add(populateStep);

                        // Apply AI suggestions with local safety net
                        ApplyPopulateResult(acceptedConfig, populateStep, result.difficulty);

                        result.generationSource = "hybrid";
                    }
                    else
                    {
                        // Extremely unlikely — local gen should never fail
                        acceptedConfig = ProceduralRoomGenerator.GenerateFallback(result.difficulty);
                        result.generationSource = "fallback";
                        steps.Add(new PipelineStep
                        {
                            stepName = "Emergency Fallback",
                            source = "local",
                            success = true,
                            details = "Spatial gen failed; used full fallback."
                        });
                    }
                }
                else
                {
                    // ══════════════════════════════════════════════════════
                    // AI_FULL MODE: Original LevelGen + QC retry loop
                    // ══════════════════════════════════════════════════════
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

                        // Auto-repair doorways before validation (compensates for
                        // Flash model's weak spatial reasoning)
                        int repairCount = ProceduralRoomGenerator.RepairDoorways(lgStep.config);

                        // Auto-repair duplicate roles (e.g., 2 EXITs → demote extras to PACING)
                        repairCount += RepairDuplicateRoles(lgStep.config);

                        // Ensure spawnZones are initialized, then populate with enemies
                        foreach (var room in lgStep.config.rooms)
                        {
                            if (room.spawnZones == null)
                                room.spawnZones = new System.Collections.Generic.List<SpawnZoneConfig>();
                        }
                        ProceduralRoomGenerator.PopulateSpawnZones(lgStep.config, result.difficulty);

                        if (repairCount > 0)
                        {
                            steps.Add(new PipelineStep
                            {
                                stepName = $"Auto-Repair (attempt {attempt})",
                                source = "local",
                                success = true,
                                durationMs = 0,
                                details = $"Repaired {repairCount} issue(s), populated spawn zones."
                            });
                        }

                        // Local validation first (fast, free)
                        var localIssues = ProceduralRoomGenerator.Validate(lgStep.config);
                        // Filter: only hard errors cause rejection (warnings are acceptable)
                        var hardErrors = localIssues.FindAll(i => !i.StartsWith("Warning"));
                        if (hardErrors.Count > 0)
                        {
                            lastRejectionReason = $"Local validation: {string.Join("; ", hardErrors)}";
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

                    // Fallback if all AI_Full attempts failed
                    if (acceptedConfig == null)
                    {
                        SetStatus("Gemini failed — using local fallback generator...");
                        var fallbackTimer = Stopwatch.StartNew();

                        acceptedConfig = ProceduralRoomGenerator.GenerateFallback(result.difficulty);
                        fallbackTimer.Stop();

                        // C8 FIX: Validate the fallback config too
                        var fallbackIssues = ProceduralRoomGenerator.Validate(acceptedConfig);
                        bool fallbackValid = fallbackIssues.Count == 0 ||
                                             fallbackIssues.TrueForAll(i => i.StartsWith("Warning"));

                        steps.Add(new PipelineStep
                        {
                            stepName = "Fallback Generator",
                            source = "local",
                            success = fallbackValid,
                            durationMs = fallbackTimer.ElapsedMilliseconds,
                            details = fallbackValid
                                ? $"Generated {acceptedConfig.rooms.Count} rooms locally after {attempt} Gemini failures."
                                : $"Fallback generated but has issues: {string.Join("; ", fallbackIssues)}"
                        });

                        if (!fallbackValid)
                        {
                            Debug.LogWarning($"[Pipeline] Fallback has non-warning issues: {string.Join("; ", fallbackIssues)}");
                        }

                        result.generationSource = "fallback";
                        Debug.Log("[Pipeline] Fallback generator produced a valid layout.");
                    }
                    else
                    {
                        result.generationSource = "gemini";
                    }
                }

                result.config = acceptedConfig;
                result.qcAttempts = attempt;
                acceptedConfig.metadata ??= new LayoutMetadata();
                acceptedConfig.metadata.qcAttempts = attempt;

                // ==============================================================
                // COMMON: Save to history for novelty comparison
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
                // COMMON: Build the physical floor
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
                    model: GeminiModel.Pro,   // Pro (49B) for spatial reasoning
                    timeoutSeconds: 120       // Allow up to 120s — NVIDIA free tier can be slow
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

        private async Task<PipelineStep> RunPopulateStep(RoomGraphConfig config,
            DifficultyProfile difficulty)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                string prompt = BuildPopulatePrompt(config, difficulty);

                // Build a lightweight summary of the layout for the AI
                var roomSummary = new System.Text.StringBuilder();
                roomSummary.Append("{\"rooms\":[");
                for (int i = 0; i < config.rooms.Count; i++)
                {
                    var r = config.rooms[i];
                    if (i > 0) roomSummary.Append(",");
                    roomSummary.Append($"{{\"roomId\":\"{r.roomId}\",\"shape\":{(int)r.shape}," +
                        $"\"role\":{(int)r.role},\"doorwayCount\":{r.doorways?.Count ?? 0}," +
                        $"\"gridCol\":{r.gridCol},\"gridRow\":{r.gridRow}}}");
                }
                roomSummary.Append("]}");

                var request = new AgentRequest(
                    agentName: "populate",
                    systemPrompt: prompt,
                    gameStateJson: roomSummary.ToString(),
                    model: GeminiModel.Flash  // Flash for speed (~3s)
                );

                // Guard: if no API key or bridge not in scene, skip AI
                if (GeminiAgentBridge.Instance == null)
                {
                    timer.Stop();
                    return new PopulatePipelineStep
                    {
                        stepName = "AI Populate",
                        source = "no_api",
                        success = false,
                        durationMs = timer.ElapsedMilliseconds,
                        details = "GeminiAgentBridge not available. Will use local role assignment."
                    };
                }

                var response = await GeminiAgentBridge.Instance.SendAgentRequest(request);
                timer.Stop();

                if (!response.success)
                {
                    return new PopulatePipelineStep
                    {
                        stepName = "AI Populate",
                        source = "populate_failed",
                        success = false,
                        durationMs = timer.ElapsedMilliseconds,
                        details = $"Populate agent failed: {response.error}. Will use local role assignment.",
                        trace = response.trace
                    };
                }

                // Parse role assignments from the response
                var assignments = ParsePopulateResult(response.trace?.action);

                return new PopulatePipelineStep
                {
                    stepName = "AI Populate",
                    source = "gemini_populate",
                    success = assignments != null && assignments.Count > 0,
                    durationMs = timer.ElapsedMilliseconds,
                    details = assignments != null
                        ? $"AI assigned {assignments.Count} room roles."
                        : "Failed to parse populate response. Will use local fallback.",
                    trace = response.trace,
                    assignments = assignments
                };
            }
            catch (Exception ex)
            {
                timer.Stop();
                return new PopulatePipelineStep
                {
                    stepName = "AI Populate",
                    source = "error",
                    success = false,
                    durationMs = timer.ElapsedMilliseconds,
                    details = $"Populate exception: {ex.Message}. Will use local fallback."
                };
            }
        }

        // ====================================================================
        // Prompt Builders
        // ====================================================================

        private string BuildLevelGenPrompt(DifficultyProfile difficulty, string rejection)
        {
            string base_prompt = $@"You are the LEVEL GENERATION AGENT for THRESHOLD.
Generate a dungeon floor as a SINGLE JSON object. Output ONLY valid JSON.

ENUMS (use INTEGER values): RoomShape: CROSSROADS=0,T_JUNCTION=1,STRAIGHT=2,CORNER=3,DEAD_END=4
RoomRole: ENTRY=0,EXIT=1,PACING=2,COMBAT=3,AMBUSH=4,BOSS=5,LOOT=6,CHOKE=7
Direction: NORTH=0,EAST=1,SOUTH=2,WEST=3 | NPCArchetype: GRUNT=0,FLANKER=1,SUPPRESSOR=2,ELITE=3

DOORWAYS BY SHAPE: CROSSROADS=4(N,E,S,W) T_JUNCTION=3(any 3) STRAIGHT=2(opposite) CORNER=2(adjacent) DEAD_END=1

RULES: 1)Exactly 1 ENTRY(role=0), 1 EXIT(role=1) 2)ENTRY/EXIT have empty spawnZones
3)Matching doorways: if edge roomA→roomB dir D, roomA has D open, roomB has opposite open (N↔S,E↔W)
4)All rooms reachable from ENTRY via BFS 5)Doorway count matches shape 6)Grid coords: NORTH=(col,row-1) EAST=(col+1,row)

DIFFICULTY: rooms={difficulty.targetRoomCount}, multiplier={difficulty.difficultyMultiplier:F1}, enemies/room={difficulty.baseEnemiesPerRoom}, elites={difficulty.eliteCount}

MINIMAL EXAMPLE (2 rooms):
{{""rooms"":[
{{""roomId"":""room_0"",""gridCol"":0,""gridRow"":0,""shape"":4,""role"":0,""rotationDegrees"":0,""doorways"":[{{""direction"":1,""isOpen"":true,""connectedRoomId"":""room_1""}}],""spawnZones"":[],""items"":[],""events"":[]}},
{{""roomId"":""room_1"",""gridCol"":1,""gridRow"":0,""shape"":4,""role"":1,""rotationDegrees"":0,""doorways"":[{{""direction"":3,""isOpen"":true,""connectedRoomId"":""room_0""}}],""spawnZones"":[],""items"":[],""events"":[]}}
],""edges"":[{{""roomIdA"":""room_0"",""roomIdB"":""room_1"",""directionFromA"":1}}],
""metadata"":{{""seed"":42,""generationMethod"":""gemini_level_gen"",""noveltyScore"":0.8,""timestamp"":"""",""qcAttempts"":0,""gridWidth"":2,""gridHeight"":1}}}}

Generate {difficulty.targetRoomCount} rooms with varied shapes, branching paths, combat/loot/pacing rooms. Output ONLY JSON.";

            if (!string.IsNullOrEmpty(rejection))
            {
                base_prompt += $"\n\nPREVIOUS ATTEMPT REJECTED: {rejection}\nFix ALL listed issues.";
            }

            return base_prompt;
        }

        private string BuildQcPrompt()
        {
            return @"You are the QC AGENT for THRESHOLD. Validate a RoomGraphConfig JSON. Output ONLY JSON.

CHECKS (all must pass): 1)Exactly 1 ENTRY(role=0), 1 EXIT(role=1) 2)ENTRY/EXIT have empty spawnZones
3)Doorway consistency: edge roomA→roomB dir D means roomA has D open, roomB has opposite (0↔2,1↔3)
4)All rooms reachable from ENTRY via BFS 5)Path exists ENTRY→EXIT 6)Doorway count matches shape(CROSSROADS=4,T=3,STRAIGHT=2,CORNER=2,DEAD_END=1)

OUTPUT: {""status"":""ACCEPTED"",""failures"":[],""validation_checks"":6,""passed"":6}
or {""status"":""REJECTED"",""failures"":[""specific failure""],""validation_checks"":6,""passed"":N}";
        }

        private string BuildPopulatePrompt(RoomGraphConfig config, DifficultyProfile difficulty)
        {
            int interiorCount = 0;
            foreach (var r in config.rooms)
            {
                if (r.role != RoomRole.ENTRY && r.role != RoomRole.EXIT)
                    interiorCount++;
            }

            return $@"You are the POPULATION AGENT for THRESHOLD, a top-down roguelite corridor shooter.
You receive a spatially valid room layout. Grid positions, doorways, and shapes are ALREADY SET and CORRECT.
Your job: assign gameplay ROLES to the {interiorCount} interior rooms.

ROOM ROLES (use INTEGER values): PACING=2, COMBAT=3, AMBUSH=4, BOSS=5, LOOT=6, CHOKE=7
ROOM SHAPES: CROSSROADS=0(4 doors), T_JUNCTION=1(3 doors), STRAIGHT=2(2 doors opposite), CORNER=3(2 doors adjacent), DEAD_END=4(1 door)
DO NOT change ENTRY(0) or EXIT(1) rooms.

ASSIGNMENT RULES:
1) Exactly 1 room must be BOSS(5) — prefer multi-door room near EXIT
2) 1 PACING(2) room per 3 combat-type rooms — placed after intense sequences
3) DEAD_END shapes → LOOT(6) candidate
4) CORNER shapes → CHOKE(7) candidate
5) If difficulty ≥ 1.2, include 1 AMBUSH(4) room
6) Remaining rooms → COMBAT(3)

DIFFICULTY: multiplier={difficulty.difficultyMultiplier:F1}, enemies_per_room={difficulty.baseEnemiesPerRoom}, elites={difficulty.eliteCount}, tactic={difficulty.preferredTactic}

OUTPUT (strict JSON only):
{{""role_assignments"":[{{""roomId"":""room_X"",""role"":3,""reason"":""short reason""}}]}}

Assign a role to EVERY interior room. Output ONLY the JSON object.";
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

        /// <summary>
        /// Parses AI Populate agent output into a list of role assignments.
        /// </summary>
        private List<PopulateRoleAssignment> ParsePopulateResult(string actionJson)
        {
            if (string.IsNullOrWhiteSpace(actionJson)) return null;
            try
            {
                string clean = StripCodeFences(actionJson);
                var raw = JsonUtility.FromJson<PopulateOutputRaw>(clean);
                if (raw?.role_assignments == null || raw.role_assignments.Length == 0)
                    return null;

                var result = new List<PopulateRoleAssignment>();
                foreach (var a in raw.role_assignments)
                {
                    if (string.IsNullOrEmpty(a.roomId)) continue;
                    // Validate role is in valid range
                    if (a.role < 2 || a.role > 7) continue;
                    result.Add(new PopulateRoleAssignment
                    {
                        roomId = a.roomId,
                        role = (RoomRole)a.role
                    });
                }
                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipeline] Failed to parse populate result: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies AI role assignments to the config with a local safety net.
        /// If the AI fails, falls back to local AssignRoles.
        /// Spawn zones and items are ALWAYS populated by local code.
        /// </summary>
        private void ApplyPopulateResult(RoomGraphConfig config, PipelineStep step,
            DifficultyProfile difficulty)
        {
            bool aiApplied = false;

            if (step.success && step is PopulatePipelineStep pStep && pStep.assignments != null)
            {
                // Apply AI role assignments
                foreach (var assignment in pStep.assignments)
                {
                    var room = config.GetRoom(assignment.roomId);
                    if (room != null && room.role != RoomRole.ENTRY && room.role != RoomRole.EXIT)
                    {
                        room.role = assignment.role;
                    }
                }

                // Validate: ensure the layout meets minimum requirements
                EnsureRequiredRoles(config, difficulty);
                aiApplied = true;

                if (logPipeline)
                    Debug.Log($"[Pipeline] AI Populate applied {pStep.assignments.Count} role assignments.");
            }

            if (!aiApplied)
            {
                // Fallback: use local role assignment
                ProceduralRoomGenerator.AssignRoles(config, difficulty);
                if (logPipeline)
                    Debug.Log("[Pipeline] Using local role assignment (AI Populate unavailable).");
            }

            // ALWAYS populate spawn zones and items locally (guaranteed correct)
            ProceduralRoomGenerator.PopulateSpawnZones(config, difficulty);
            ProceduralRoomGenerator.PopulateItems(config);
        }

        /// <summary>
        /// Validates and fixes role assignment after AI Populate:
        /// - Ensures exactly 1 BOSS room
        /// - Ensures at least 1 COMBAT room
        /// - Demotes duplicate BOSS rooms
        /// </summary>
        private static void EnsureRequiredRoles(RoomGraphConfig config, DifficultyProfile difficulty)
        {
            if (config?.rooms == null) return;

            var interiorRooms = config.rooms.FindAll(r =>
                r.role != RoomRole.ENTRY && r.role != RoomRole.EXIT);

            // Ensure exactly 1 BOSS
            var bossRooms = interiorRooms.FindAll(r => r.role == RoomRole.BOSS);
            if (bossRooms.Count == 0 && interiorRooms.Count > 0)
            {
                // Promote the last multi-door room to BOSS
                var candidate = interiorRooms.FindLast(r =>
                    r.doorways != null && r.doorways.Count >= 2) ?? interiorRooms[^1];
                candidate.role = RoomRole.BOSS;
                Debug.Log($"[Pipeline] Promoted {candidate.roomId} to BOSS (none assigned by AI).");
            }
            else if (bossRooms.Count > 1)
            {
                // Keep only the first BOSS, demote others to COMBAT
                for (int i = 1; i < bossRooms.Count; i++)
                {
                    bossRooms[i].role = RoomRole.COMBAT;
                    Debug.Log($"[Pipeline] Demoted duplicate BOSS {bossRooms[i].roomId} to COMBAT.");
                }
            }

            // Ensure at least 1 COMBAT room
            var combatRooms = config.rooms.FindAll(r => r.role == RoomRole.COMBAT);
            if (combatRooms.Count == 0 && interiorRooms.Count > 1)
            {
                // Find a non-BOSS, non-ENTRY, non-EXIT room to promote
                var candidate = interiorRooms.Find(r =>
                    r.role != RoomRole.BOSS && r.role != RoomRole.ENTRY && r.role != RoomRole.EXIT);
                if (candidate != null)
                {
                    candidate.role = RoomRole.COMBAT;
                    Debug.Log($"[Pipeline] Promoted {candidate.roomId} to COMBAT (none present).");
                }
            }
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
        // Auto-Repair Helpers
        // ====================================================================

        /// <summary>
        /// If the AI generated multiple ENTRY or EXIT rooms, keep the first and
        /// demote extras to PACING. Returns the number of repairs made.
        /// </summary>
        private static int RepairDuplicateRoles(RoomGraphConfig config)
        {
            if (config?.rooms == null) return 0;
            int repairs = 0;

            bool foundEntry = false, foundExit = false;
            foreach (var room in config.rooms)
            {
                if (room.role == RoomRole.ENTRY)
                {
                    if (foundEntry) { room.role = RoomRole.PACING; repairs++; }
                    else foundEntry = true;
                }
                else if (room.role == RoomRole.EXIT)
                {
                    if (foundExit) { room.role = RoomRole.PACING; repairs++; }
                    else foundExit = true;
                }
            }

            if (repairs > 0)
                Debug.Log($"[Pipeline] Demoted {repairs} duplicate ENTRY/EXIT room(s) to PACING.");

            return repairs;
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
                // M8 FIX: Ensure lastFence is actually the closing fence, not the opening one
                if (nl > 0 && lf > nl && lf != 0)
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

        private class PopulatePipelineStep : PipelineStep
        {
            public List<PopulateRoleAssignment> assignments;
        }

        private class PopulateRoleAssignment
        {
            public string roomId;
            public RoomRole role;
        }

        [Serializable]
        private class PopulateOutputRaw
        {
            public PopulateOutputAssignment[] role_assignments;
        }

        [Serializable]
        private class PopulateOutputAssignment
        {
            public string roomId;
            public int role;
            public string reason;
        }
    }
}
