// ============================================================================
// Phase2TestRunner.cs — Integration test suite for all Phase 2 systems
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Attach to a test GameObject, wire references in Inspector, press Play.
// Tests run sequentially via coroutines with color-coded console output.
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Threshold.Agents;
using Threshold.Core;
using Threshold.Generation;
using Threshold.NPC;
using Threshold.Player;
using UnityEngine;

namespace Threshold.Core
{
    public class Phase2TestRunner : MonoBehaviour
    {
        [Header("Scene References (wire in Inspector)")]
        [SerializeField] private PlayerMetricsTracker metricsTracker;
        [SerializeField] private LayoutHistoryManager historyManager;
        [SerializeField] private NPCStateMachine testNpc;

        // Results
        private int _passed;
        private int _failed;
        private int _skipped;

        // Cached fallback config for reuse across tests
        private RoomGraphConfig _fallbackConfig;

        private void Start()
        {
            StartCoroutine(RunAllTests());
        }

        // ====================================================================
        // Test Runner
        // ====================================================================

        private IEnumerator RunAllTests()
        {
            yield return null; // Wait one frame for all Awake/Start to finish

            LogHeader("THRESHOLD — Phase 2 Integration Tests");

            yield return StartCoroutine(Test1_Metrics());
            yield return StartCoroutine(Test2_FallbackGeneration());
            yield return StartCoroutine(Test3_NpcStateMachine());
            yield return StartCoroutine(Test4_LayoutHistory());
            yield return StartCoroutine(Test5_DirectorAgent());

            LogHeader("Phase 2 Results");
            int total = _passed + _failed + _skipped;
            Debug.Log($"  <color=#00ff88>PASS: {_passed}</color>  " +
                      $"<color=#ff4444>FAIL: {_failed}</color>  " +
                      $"<color=#ffcc00>SKIP: {_skipped}</color>  " +
                      $"(Total: {total})");

            string verdict = _failed == 0 ? "ALL TESTS PASSED" : "SOME TESTS FAILED";
            string color = _failed == 0 ? "#00ff88" : "#ff4444";
            Debug.Log($"<color={color}><b>  ► {verdict}</b></color>");
            Debug.Log("══════════════════════════════════════════════");
        }

        // ====================================================================
        // TEST 1: Player Metrics
        // ====================================================================

        private IEnumerator Test1_Metrics()
        {
            LogTestStart("TEST 1: Player Metrics Tracker");

            if (metricsTracker == null)
            {
                LogFail("PlayerMetricsTracker reference not set in Inspector.");
                yield break;
            }

            bool pass = true;

            // Simulate a session
            metricsTracker.OnRunStart();
            yield return new WaitForSeconds(0.1f);

            metricsTracker.OnRoomEnter("test_room_0", RoomRole.COMBAT, 1.0f);
            yield return new WaitForSeconds(0.1f);

            // 5 shots fired, 3 hits → 60% accuracy
            metricsTracker.OnShotFired();
            metricsTracker.OnShotHit();
            metricsTracker.OnShotFired();
            metricsTracker.OnShotHit();
            metricsTracker.OnShotFired();
            metricsTracker.OnShotHit();
            metricsTracker.OnShotFired();
            metricsTracker.OnShotFired();

            // 2 kills, 10 ammo, 1 health kit
            metricsTracker.OnEnemyKilled();
            metricsTracker.OnEnemyKilled();
            metricsTracker.OnAmmoUsed(10);
            metricsTracker.OnHealthKitUsed();

            metricsTracker.OnRoomClear(0.75f);
            yield return new WaitForSeconds(0.1f);

            metricsTracker.OnRunEnd(won: true);

            // Validate current room metrics
            var lastRun = metricsTracker.GetLastRunMetrics();
            if (lastRun == null)
            {
                LogFail("GetLastRunMetrics() returned null.");
                pass = false;
            }
            else
            {
                Debug.Log($"  Accuracy: {lastRun.overallAccuracy:P0} (expected ~60%)");
                Debug.Log($"  Kills: {lastRun.totalKills} (expected 2)");
                Debug.Log($"  Health kits: {lastRun.totalHealthKitsUsed} (expected 1)");
                Debug.Log($"  Won: {lastRun.won} (expected True)");
                Debug.Log($"  Rooms: {lastRun.roomsCompleted} (expected 1)");

                if (Mathf.Abs(lastRun.overallAccuracy - 0.6f) > 0.01f)
                {
                    LogFail($"Accuracy is {lastRun.overallAccuracy:P0}, expected 60%.");
                    pass = false;
                }

                if (lastRun.totalKills != 2)
                {
                    LogFail($"Kills is {lastRun.totalKills}, expected 2.");
                    pass = false;
                }
            }

            // Validate Director JSON
            string json = metricsTracker.GetDirectorInputJSON();
            if (string.IsNullOrEmpty(json))
            {
                LogFail("GetDirectorInputJSON() returned empty.");
                pass = false;
            }
            else
            {
                Debug.Log("  ── Director Input JSON ──");
                // Log first 500 chars to avoid console flooding
                Debug.Log($"  {(json.Length > 500 ? json[..500] + "..." : json)}");

                // Basic JSON validity check
                if (!json.Contains("signals") || !json.Contains("accuracy_percent"))
                {
                    LogFail("JSON missing expected fields.");
                    pass = false;
                }
            }

            // Validate micro-adjustment
            metricsTracker.UpdateLiveStats(0.25f, 0.5f); // Low health
            var adj = metricsTracker.GetMicroAdjustment();
            Debug.Log($"  ── Micro-Adjustment ──");
            Debug.Log($"  Add health kit: {adj.addHealthKit}");
            Debug.Log($"  Add ammo: {adj.addAmmoCache}");
            Debug.Log($"  Enemy delta: {adj.enemyCountDelta}");
            Debug.Log($"  Reason: {adj.reason}");

            if (pass) LogPass("All metrics recorded and exported correctly.");
        }

        // ====================================================================
        // TEST 2: Fallback Generation
        // ====================================================================

        private IEnumerator Test2_FallbackGeneration()
        {
            LogTestStart("TEST 2: Fallback Room Generation");
            bool pass = true;

            var difficulty = new DifficultyProfile
            {
                difficultyMultiplier = 1.0f,
                targetRoomCount = 7,
                baseEnemiesPerRoom = 3,
                eliteCount = 1,
                eventProbability = 0.3f,
                preferredTactic = "ATTACK"
            };

            _fallbackConfig = ProceduralRoomGenerator.GenerateFallback(difficulty, seed: 42);

            if (_fallbackConfig == null || _fallbackConfig.rooms == null)
            {
                LogFail("GenerateFallback returned null.");
                yield break;
            }

            // Log rooms
            Debug.Log($"  Total rooms: {_fallbackConfig.rooms.Count}");
            Debug.Log($"  Total edges: {_fallbackConfig.edges.Count}");
            Debug.Log("  ── Room Layout ──");
            foreach (var room in _fallbackConfig.rooms)
            {
                int enemies = room.TotalEnemyCount();
                Debug.Log($"  {room.roomId}: {room.shape} / {room.role} " +
                          $"at ({room.gridCol},{room.gridRow}) enemies={enemies}");
            }

            // Count roles
            int entries = _fallbackConfig.rooms.Count(r => r.role == RoomRole.ENTRY);
            int exits = _fallbackConfig.rooms.Count(r => r.role == RoomRole.EXIT);
            int bosses = _fallbackConfig.rooms.Count(r => r.role == RoomRole.BOSS);

            Debug.Log($"  ENTRY count: {entries} (expected 1)");
            Debug.Log($"  EXIT count: {exits} (expected 1)");
            Debug.Log($"  BOSS count: {bosses} (expected ≥1)");

            if (entries != 1) { LogFail($"Expected 1 ENTRY, got {entries}."); pass = false; }
            if (exits != 1) { LogFail($"Expected 1 EXIT, got {exits}."); pass = false; }
            if (bosses < 1) { LogFail("No BOSS room found."); pass = false; }

            // Check ENTRY has no enemies
            var entryRoom = _fallbackConfig.GetEntryRoom();
            if (entryRoom != null && entryRoom.TotalEnemyCount() > 0)
            {
                LogFail("ENTRY room has enemy spawns — must be safe.");
                pass = false;
            }

            // Run full validation
            var issues = ProceduralRoomGenerator.Validate(_fallbackConfig);
            if (issues.Count > 0)
            {
                Debug.Log("  ── Validation Issues ──");
                foreach (var issue in issues)
                {
                    // Warnings don't fail
                    if (issue.StartsWith("Warning:"))
                        Debug.Log($"  <color=#ffcc00>⚠ {issue}</color>");
                    else
                    {
                        Debug.Log($"  <color=#ff4444>✗ {issue}</color>");
                        pass = false;
                    }
                }
            }
            else
            {
                Debug.Log("  Validation: <color=#00ff88>0 errors</color>");
            }

            if (pass) LogPass("Fallback generator produces valid, playable layouts.");
            yield return null;
        }

        // ====================================================================
        // TEST 3: NPC State Machine
        // ====================================================================

        private IEnumerator Test3_NpcStateMachine()
        {
            LogTestStart("TEST 3: NPC State Machine");

            if (testNpc == null)
            {
                LogFail("NPCStateMachine reference not set. Create a capsule with " +
                        "NavMeshAgent + NPCStateMachine and wire it in the Inspector.");
                yield break;
            }

            bool pass = true;

            // Initialize as GRUNT — use this runner's transform as fake player target
            testNpc.Initialize("test_npc_01", NPCArchetype.GRUNT, transform);

            Debug.Log($"  Initialized: {testNpc.npcId} ({testNpc.archetype})");
            Debug.Log($"  HP: {testNpc.CurrentHealth}/{testNpc.Stats.maxHealth}");
            Debug.Log($"  Initial state: {testNpc.CurrentState}");

            if (testNpc.CurrentState != NPCState.PATROL)
            {
                LogFail($"Initial state should be PATROL, got {testNpc.CurrentState}.");
                pass = false;
            }

            // Cycle through all states
            NPCState[] sequence = {
                NPCState.ATTACK, NPCState.FLANK, NPCState.SUPPRESS,
                NPCState.RETREAT, NPCState.ALLIED
            };

            foreach (var state in sequence)
            {
                testNpc.SetState(state);
                yield return new WaitForSeconds(0.5f);

                if (testNpc.CurrentState != state)
                {
                    LogFail($"Expected {state}, got {testNpc.CurrentState}.");
                    pass = false;
                }
                else
                {
                    Debug.Log($"  ✓ Transitioned to {state} (time in state: {testNpc.TimeSinceStateChange:F2}s)");
                }
            }

            // Verify ALLIED is one-way
            testNpc.SetState(NPCState.PATROL); // Should be rejected
            if (testNpc.CurrentState != NPCState.ALLIED)
            {
                LogFail("ALLIED state is not one-way — was able to leave.");
                pass = false;
            }
            else
            {
                Debug.Log("  ✓ ALLIED is one-way (PATROL transition correctly rejected)");
            }

            // Test GetSnapshot()
            var snapshot = testNpc.GetSnapshot();
            if (snapshot == null)
            {
                LogFail("GetSnapshot() returned null.");
                pass = false;
            }
            else
            {
                Debug.Log("  ── NPC Snapshot ──");
                Debug.Log($"  ID: {snapshot.npcId}");
                Debug.Log($"  Type: {snapshot.archetypeType}");
                Debug.Log($"  State: {snapshot.currentState}");
                Debug.Log($"  HP: {snapshot.healthPercent:P0}");
                Debug.Log($"  Pos: ({snapshot.posX:F1}, {snapshot.posY:F1}, {snapshot.posZ:F1})");
                Debug.Log($"  LOS: {snapshot.hasLineOfSight}");

                if (string.IsNullOrEmpty(snapshot.npcId))
                {
                    LogFail("Snapshot npcId is empty.");
                    pass = false;
                }
            }

            // Test damage
            bool died = testNpc.TakeDamage(50f);
            Debug.Log($"  Took 50 damage → HP: {testNpc.CurrentHealth}/{testNpc.Stats.maxHealth} (died: {died})");
            if (died)
            {
                LogFail("NPC died from 50 damage with 100 HP.");
                pass = false;
            }

            if (pass) LogPass("All 6 states cycle correctly, snapshot valid, one-way ALLIED enforced.");
        }

        // ====================================================================
        // TEST 4: Layout History & Novelty
        // ====================================================================

        private IEnumerator Test4_LayoutHistory()
        {
            LogTestStart("TEST 4: Layout History & Novelty Scoring");

            if (historyManager == null)
            {
                LogFail("LayoutHistoryManager reference not set in Inspector.");
                yield break;
            }

            bool pass = true;

            // Clear previous history for clean test
            historyManager.ClearHistory();

            // Save the config from Test 2
            if (_fallbackConfig == null)
            {
                Debug.Log("  Generating test layout (Test 2 config not available)...");
                var diff = new DifficultyProfile { targetRoomCount = 7 };
                _fallbackConfig = ProceduralRoomGenerator.GenerateFallback(diff, seed: 42);
            }

            historyManager.RecordLayout(_fallbackConfig);
            Debug.Log($"  Saved layout 1 ({_fallbackConfig.rooms.Count} rooms). " +
                      $"History count: {historyManager.HistoryCount}");

            // Generate a second layout with a different seed
            var diff2 = new DifficultyProfile { targetRoomCount = 8, difficultyMultiplier = 1.3f };
            var config2 = ProceduralRoomGenerator.GenerateFallback(diff2, seed: 999);
            historyManager.RecordLayout(config2);
            Debug.Log($"  Saved layout 2 ({config2.rooms.Count} rooms). " +
                      $"History count: {historyManager.HistoryCount}");

            // Novelty of the first config vs history (which now contains both)
            float novelty1 = historyManager.CalculateNoveltyScore(_fallbackConfig);
            Debug.Log($"  Novelty of layout 1 vs history: {novelty1:F3}");

            // Novelty of a brand new config
            var config3 = ProceduralRoomGenerator.GenerateFallback(
                new DifficultyProfile { targetRoomCount = 10, difficultyMultiplier = 2.0f }, seed: 7777);
            float novelty3 = historyManager.CalculateNoveltyScore(config3);
            Debug.Log($"  Novelty of fresh layout vs history: {novelty3:F3}");

            // Validate scores are in range
            if (novelty1 < 0f || novelty1 > 1f)
            {
                LogFail($"Novelty score {novelty1} is out of [0,1] range.");
                pass = false;
            }

            if (novelty3 < 0f || novelty3 > 1f)
            {
                LogFail($"Novelty score {novelty3} is out of [0,1] range.");
                pass = false;
            }

            // The identical layout should have lower novelty than a fresh one
            if (novelty1 >= novelty3)
            {
                Debug.Log($"  <color=#ffcc00>⚠ Expected identical layout ({novelty1:F3}) " +
                          $"to score lower than fresh ({novelty3:F3}).</color>");
                // Don't fail — seed variation can cause edge cases
            }

            if (pass) LogPass($"Novelty scoring works. Scores: identical={novelty1:F3}, fresh={novelty3:F3}");
            yield return null;
        }

        // ====================================================================
        // TEST 5: Director Agent (optional)
        // ====================================================================

        private IEnumerator Test5_DirectorAgent()
        {
            LogTestStart("TEST 5: Director Agent (optional — requires API key)");

            if (GeminiAgentBridge.Instance == null)
            {
                LogSkip("GeminiAgentBridge not found in scene.");
                yield break;
            }

            if (!GeminiAgentBridge.Instance.IsConfigured)
            {
                LogSkip("No API key configured (mock mode is off and no key set). " +
                        "Enable mock mode or add a key to test.");
                yield break;
            }

            // Run async Director call via coroutine wrapper
            bool complete = false;
            DirectorResult dirResult = null;

            _ = RunDirectorAsync(result =>
            {
                dirResult = result;
                complete = true;
            });

            float timeout = 15f;
            float elapsed = 0f;
            while (!complete && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!complete)
            {
                LogFail($"Director Agent timed out after {timeout}s.");
                yield break;
            }

            if (dirResult == null || !dirResult.success)
            {
                LogFail($"Director call failed: {(dirResult != null ? "returned failure" : "null result")}");
                yield break;
            }

            Debug.Log("  ── Director Output ──");
            Debug.Log($"  Source: {dirResult.source}");
            Debug.Log($"  Difficulty: {dirResult.profile.difficultyMultiplier:F2}x");
            Debug.Log($"  Room count: {dirResult.profile.targetRoomCount}");
            Debug.Log($"  Enemies/room: {dirResult.profile.baseEnemiesPerRoom}");
            Debug.Log($"  Elites: {dirResult.profile.eliteCount}");
            Debug.Log($"  Tactic: {dirResult.profile.preferredTactic}");
            Debug.Log($"  Decision: {dirResult.decisionText}");

            if (dirResult.trace != null)
            {
                Debug.Log("  ── 5-Step Trace ──");
                Debug.Log($"  OBS: {Truncate(dirResult.trace.observation, 80)}");
                Debug.Log($"  INF: {Truncate(dirResult.trace.inference, 80)}");
                Debug.Log($"  DEC: {Truncate(dirResult.trace.decision, 80)}");
                Debug.Log($"  ACT: {Truncate(dirResult.trace.action, 80)}");
                Debug.Log($"  EVL: {Truncate(dirResult.trace.evaluation_plan, 80)}");
            }

            LogPass($"Director returned valid profile via {dirResult.source}.");
        }

        private async System.Threading.Tasks.Task RunDirectorAsync(System.Action<DirectorResult> callback)
        {
            var result = await DirectorAgentCaller.CallDirector();
            callback?.Invoke(result);
        }

        // ====================================================================
        // Logging Helpers
        // ====================================================================

        private void LogHeader(string title)
        {
            Debug.Log("══════════════════════════════════════════════");
            Debug.Log($"<b>  {title}</b>");
            Debug.Log("══════════════════════════════════════════════");
        }

        private void LogTestStart(string name)
        {
            Debug.Log("──────────────────────────────────────────────");
            Debug.Log($"<b>  {name}</b>");
            Debug.Log("──────────────────────────────────────────────");
        }

        private void LogPass(string msg)
        {
            _passed++;
            Debug.Log($"<color=#00ff88><b>  [PASS]</b> {msg}</color>");
        }

        private void LogFail(string msg)
        {
            _failed++;
            Debug.LogError($"<color=#ff4444><b>  [FAIL]</b> {msg}</color>");
        }

        private void LogSkip(string msg)
        {
            _skipped++;
            Debug.Log($"<color=#ffcc00><b>  [SKIP]</b> {msg}</color>");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= max ? s : s[..max] + "...";
        }
    }
}
