// ============================================================================
// GenerationTestRunner.cs — Visual procedural generation tester
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Attach to a test GameObject. Generates and physically instantiates a
// dungeon layout when Play is pressed. Supports both local fallback and
// full Gemini pipeline modes. Draws colored gizmos per room role.
// ============================================================================

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Threshold.Agents;
using Threshold.Core;
using Threshold.Generation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Threshold.Core
{
    public class GenerationTestRunner : MonoBehaviour
    {
        // ====================================================================
        // References (wire in Inspector)
        // ====================================================================

        [Header("Required References")]
        [SerializeField] private FloorGenerator floorGenerator;

        [Header("Optional — Full Pipeline Mode")]
        [Tooltip("Enable to run Director → LevelGen → QC → Build instead of local fallback.")]
        [SerializeField] private bool useGeminiPipeline = false;

        [Tooltip("Required only when Use Gemini Pipeline is true.")]
        [SerializeField] private LevelGenerationPipeline levelPipeline;

        // ====================================================================
        // Difficulty Parameters (tweak in Inspector)
        // ====================================================================

        [Header("Difficulty Settings")]
        [Range(0.5f, 2.5f)]
        [SerializeField] private float difficultyMultiplier = 1.0f;

        [Range(5, 12)]
        [SerializeField] private int roomCount = 7;

        [SerializeField] private int baseEnemiesPerRoom = 3;
        [SerializeField] private int eliteCount = 1;

        [Header("Generation")]
        [SerializeField] private int seed = -1; // -1 = random

        // ====================================================================
        // Runtime State
        // ====================================================================

        private RoomGraphConfig _currentConfig;
        private bool _isGenerating;

        // Gizmo data: world position + role for each room
        private readonly List<(Vector3 position, float moduleWidth, RoomRole role, string label)> _gizmoRooms = new();

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Start()
        {
            Log("═══════════════════════════════════════════════════════");
            Log("  THRESHOLD — Generation Visual Test");
            Log($"  Mode: {(useGeminiPipeline ? "GEMINI PIPELINE" : "LOCAL FALLBACK")}");
            Log("  Controls: [R] Regenerate  [V] Validate");
            Log("═══════════════════════════════════════════════════════");

            GenerateAndBuild();
        }

        private void Update()
        {
            if (_isGenerating) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                Log("──────────────────────────────────────────────");
                Log("[R] Regenerating with new random seed...");
                seed = -1; // force new random seed
                GenerateAndBuild();
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                ValidateCurrentConfig();
            }
        }

        // ====================================================================
        // Generation
        // ====================================================================

        private async void GenerateAndBuild()
        {
            if (_isGenerating) return;
            _isGenerating = true;

            try
            {
                if (useGeminiPipeline)
                    await RunGeminiPipeline();
                else
                    RunLocalFallback();
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private void RunLocalFallback()
        {
            var difficulty = BuildDifficultyProfile();
            int actualSeed = seed < 0 ? System.Environment.TickCount : seed;

            Log($"\n── LOCAL FALLBACK GENERATION ──");
            Log($"  Seed: {actualSeed}");
            Log($"  Multiplier: {difficultyMultiplier:F1}x | Rooms: {roomCount} | Enemies/Room: {baseEnemiesPerRoom} | Elites: {eliteCount}");

            var timer = Stopwatch.StartNew();
            _currentConfig = ProceduralRoomGenerator.GenerateFallback(difficulty, actualSeed);
            timer.Stop();

            if (_currentConfig == null || _currentConfig.rooms.Count == 0)
            {
                LogError("GenerateFallback returned null or empty config!");
                return;
            }

            Log($"  Generated in {timer.ElapsedMilliseconds}ms");
            LogLayout(_currentConfig);

            // Build physical floor
            BuildPhysicalFloor(_currentConfig);
        }

        private async Task RunGeminiPipeline()
        {
            if (levelPipeline == null)
            {
                LogError("LevelGenerationPipeline reference is null! Assign it in the Inspector.");
                RunLocalFallback();
                return;
            }

            Log($"\n── GEMINI PIPELINE GENERATION ──");
            Log($"  Multiplier: {difficultyMultiplier:F1}x | Rooms: {roomCount}");

            var result = await levelPipeline.StartNewRun();

            if (result == null || !result.success)
            {
                LogError("Gemini pipeline failed! Check previous logs.");
                return;
            }

            _currentConfig = result.config;
            Log($"  Pipeline completed in {result.totalPipelineTimeMs}ms");
            Log($"  Source: {result.generationSource} | QC Attempts: {result.qcAttempts}");
            LogLayout(_currentConfig);

            // Floor already built by the pipeline
            CacheGizmoData(_currentConfig);

            Log($"  Entry: {result.entryPosition}");
            Log($"  Exit:  {result.exitPosition}");
        }

        // ====================================================================
        // Floor Building
        // ====================================================================

        private void BuildPhysicalFloor(RoomGraphConfig config)
        {
            if (floorGenerator == null)
            {
                LogError("FloorGenerator reference is null! Assign it in the Inspector.");
                return;
            }

            var timer = Stopwatch.StartNew();
            bool success = floorGenerator.BuildFloor(config);
            timer.Stop();

            if (!success)
            {
                LogError("FloorGenerator.BuildFloor() failed!");
                return;
            }

            CacheGizmoData(config);

            Log($"\n  ── Physical Build ──");
            Log($"  Build time: {timer.ElapsedMilliseconds}ms");
            Log($"  Entry world position: {floorGenerator.EntryWorldPosition}");
            Log($"  Exit world position:  {floorGenerator.ExitWorldPosition}");
            Log($"  Rooms instantiated under: {floorGenerator.transform.name}");
            Log("  ✓ Floor built! Check Scene view to see the layout.");
        }

        // ====================================================================
        // Gizmo Data Cache
        // ====================================================================

        private void CacheGizmoData(RoomGraphConfig config)
        {
            _gizmoRooms.Clear();

            // Get module width from the first prefab, or the config
            float mw = 10f; // L7 FIX: was 20, now matches 10x10 prefab standard
            if (floorGenerator != null && floorGenerator.CurrentConfig != null)
            {
                // Use the actual module width from the floor generator
                var firstRoom = floorGenerator.GetRoomModule(config.rooms[0].roomId);
                if (firstRoom != null) mw = firstRoom.moduleWidth;
            }

            foreach (var room in config.rooms)
            {
                Vector3 worldPos = new(room.gridCol * mw, 0f, room.gridRow * mw);
                string label = $"{room.roomId}\n{room.shape}/{room.role}\nEnemies:{room.TotalEnemyCount()}";
                _gizmoRooms.Add((worldPos, mw, room.role, label));
            }
        }

        // ====================================================================
        // Validation
        // ====================================================================

        private void ValidateCurrentConfig()
        {
            if (_currentConfig == null)
            {
                LogError("[V] No config to validate — generate first.");
                return;
            }

            Log("\n── VALIDATION ──");
            var issues = ProceduralRoomGenerator.Validate(_currentConfig);

            if (issues.Count == 0)
            {
                Log("  <color=green>✓ VALID — All checks passed.</color>", "green");
            }
            else
            {
                Log($"  <color=red>✗ {issues.Count} issue(s) found:</color>", "red");
                foreach (var issue in issues)
                {
                    bool isWarning = issue.StartsWith("Warning");
                    Log($"    {(isWarning ? "⚠" : "✗")} {issue}", isWarning ? "yellow" : "red");
                }
            }
        }

        // ====================================================================
        // Layout Logging
        // ====================================================================

        private void LogLayout(RoomGraphConfig config)
        {
            Log($"\n  ── Layout Summary ──");
            Log($"  Rooms: {config.rooms.Count} | Edges: {config.edges.Count}");
            Log($"  Grid: {config.metadata?.gridWidth}×{config.metadata?.gridHeight}");
            Log($"  Method: {config.metadata?.generationMethod}");
            Log($"  Seed: {config.metadata?.seed}");

            // Shape distribution
            var shapeCounts = new Dictionary<RoomShape, int>();
            var roleCounts = new Dictionary<RoomRole, int>();
            int totalEnemies = 0;

            foreach (var room in config.rooms)
            {
                shapeCounts[room.shape] = shapeCounts.GetValueOrDefault(room.shape) + 1;
                roleCounts[room.role] = roleCounts.GetValueOrDefault(room.role) + 1;
                totalEnemies += room.TotalEnemyCount();
            }

            Log($"  Total enemies: {totalEnemies}");

            // Shape breakdown
            Log("  Shapes:");
            foreach (var kvp in shapeCounts)
                Log($"    {kvp.Key}: {kvp.Value}");

            // Role breakdown
            Log("  Roles:");
            foreach (var kvp in roleCounts)
                Log($"    {kvp.Key}: {kvp.Value}");

            // Room details
            Log("  ── Room Details ──");
            foreach (var room in config.rooms)
            {
                string doors = "";
                if (room.HasDoorway(Direction.NORTH)) doors += "N";
                if (room.HasDoorway(Direction.EAST))  doors += "E";
                if (room.HasDoorway(Direction.SOUTH)) doors += "S";
                if (room.HasDoorway(Direction.WEST))  doors += "W";

                string roleColor = GetRoleColorTag(room.role);
                Log($"    <color={roleColor}>{room.roomId}</color>: " +
                    $"{room.shape}/{room.role} at ({room.gridCol},{room.gridRow}) " +
                    $"doors=[{doors}] enemies={room.TotalEnemyCount()}" +
                    $"{((room.items?.Count ?? 0) > 0 ? $" items={room.items.Count}" : "")}");
            }

            // Edges
            Log($"  ── Edges ({config.edges.Count}) ──");
            foreach (var edge in config.edges)
            {
                Log($"    {edge.roomIdA} ←{edge.directionFromA}→ {edge.roomIdB}");
            }
        }

        // ====================================================================
        // Gizmos (Scene View)
        // ====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_gizmoRooms.Count == 0) return;

            foreach (var (position, moduleWidth, role, label) in _gizmoRooms)
            {
                // Role-based color
                Gizmos.color = GetRoleColor(role);

                // Draw room outline
                float half = moduleWidth;
                Gizmos.DrawWireCube(position + Vector3.up * 1.5f, new Vector3(half, 3f, half));

                // Draw solid floor with transparency
                Color floorColor = Gizmos.color;
                floorColor.a = 0.15f;
                Gizmos.color = floorColor;
                Gizmos.DrawCube(position + Vector3.up * 0.01f, new Vector3(half, 0.02f, half));

                // Label — L2 FIX: cache GUIStyle to avoid per-frame allocation
                UnityEditor.Handles.color = GetRoleColor(role);
                UnityEditor.Handles.Label(position + Vector3.up * 3.5f, label, GizmoLabelStyle(role));
            }

            // Draw edges as lines between room centers
            if (_currentConfig?.edges != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
                float mw = _gizmoRooms.Count > 0 ? _gizmoRooms[0].moduleWidth : 10f; // L7 FIX: was 20

                foreach (var edge in _currentConfig.edges)
                {
                    var roomA = _currentConfig.GetRoom(edge.roomIdA);
                    var roomB = _currentConfig.GetRoom(edge.roomIdB);
                    if (roomA == null || roomB == null) continue;

                    Vector3 posA = new(roomA.gridCol * mw, 1f, roomA.gridRow * mw);
                    Vector3 posB = new(roomB.gridCol * mw, 1f, roomB.gridRow * mw);
                    Gizmos.DrawLine(posA, posB);
                }
            }
        }

        // L2 FIX: Cached GUIStyle to avoid per-frame allocation in OnDrawGizmos
        private static GUIStyle _cachedGizmoStyle;
        private static Color _cachedGizmoStyleColor;

        private static GUIStyle GizmoLabelStyle(RoomRole role)
        {
            Color c = GetRoleColor(role);
            if (_cachedGizmoStyle == null || _cachedGizmoStyleColor != c)
            {
                _cachedGizmoStyle = new GUIStyle
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = c }
                };
                _cachedGizmoStyleColor = c;
            }
            return _cachedGizmoStyle;
        }
#endif

        // ====================================================================
        // Color Helpers
        // ====================================================================

        private static Color GetRoleColor(RoomRole role)
        {
            return role switch
            {
                RoomRole.ENTRY  => Color.green,
                RoomRole.EXIT   => new Color(0f, 1f, 1f),       // cyan
                RoomRole.BOSS   => Color.red,
                RoomRole.PACING => new Color(0.3f, 0.5f, 1f),   // blue
                RoomRole.LOOT   => Color.yellow,
                RoomRole.AMBUSH => new Color(1f, 0.5f, 0f),     // orange
                RoomRole.COMBAT => Color.white,
                RoomRole.CHOKE  => new Color(0.7f, 0.3f, 1f),   // purple
                _               => Color.gray
            };
        }

        private static string GetRoleColorTag(RoomRole role)
        {
            return role switch
            {
                RoomRole.ENTRY  => "green",
                RoomRole.EXIT   => "cyan",
                RoomRole.BOSS   => "red",
                RoomRole.PACING => "#5080FF",
                RoomRole.LOOT   => "yellow",
                RoomRole.AMBUSH => "orange",
                RoomRole.COMBAT => "white",
                RoomRole.CHOKE  => "#B050FF",
                _               => "gray"
            };
        }

        // ====================================================================
        // Logging
        // ====================================================================

        private DifficultyProfile BuildDifficultyProfile()
        {
            return new DifficultyProfile
            {
                difficultyMultiplier = difficultyMultiplier,
                targetRoomCount = roomCount,
                baseEnemiesPerRoom = baseEnemiesPerRoom,
                eliteCount = eliteCount
            };
        }

        private static void Log(string msg, string color = null)
        {
            Debug.Log(color != null ? $"<color={color}>{msg}</color>" : msg);
        }

        private static void LogError(string msg)
        {
            Debug.LogError($"[GenTest] {msg}");
        }
    }
}
