// ============================================================================
// EnemySystemTestRunner.cs — Visual test for NPC spawning + state machines
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Generates a floor, spawns NPCs at config spawn zones, creates a player
// capsule, and runs the NPC Brain Controller loop. Walk through rooms and
// observe NPC behaviour. Debug keys for combat simulation.
//
// SCENE SETUP:
//   1. Create empty scene
//   2. Add empty GameObject → attach this script
//   3. Add empty GameObject → attach FloorGenerator, assign room prefabs
//   4. Add empty GameObject → attach NPCBrainController
//   5. Create NPC prefab: Capsule + NavMeshAgent + NPCStateMachine
//   6. Assign references in Inspector (see [SerializeField] fields)
//   7. Bake NavMesh (or add NavMeshSurface for runtime bake)
//   8. Press Play
//
// DEBUG KEYS:
//   K — Kill nearest enemy NPC
//   H — Set player health to 25%
//   D — Force-damage nearest NPC to 15% HP
//   L — Log all living NPCs with state + health
//   B — Force Brain Agent evaluation immediately
//   R — Regenerate floor + respawn everything
//   P — Toggle pause
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Threshold.Core;
using Threshold.Generation;
using Threshold.NPC;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Threshold.Core
{
    /// <summary>
    /// Test runner for the NPC system. Generates a floor, spawns enemies,
    /// creates a player, and lets you walk through to observe AI behaviour.
    /// </summary>
    public class EnemySystemTestRunner : MonoBehaviour
    {
        // ====================================================================
        // Serialized References — Wire in Inspector
        // ====================================================================

        [Header("Required References")]
        [Tooltip("FloorGenerator component that builds the dungeon.")]
        [SerializeField] private FloorGenerator floorGenerator;

        [Tooltip("NPCBrainController that drives NPC tactical evaluation.")]
        [SerializeField] private NPCBrainController brainController;

        [Tooltip("NavMeshSurface for runtime baking. If null, one is auto-created.")]
        [SerializeField] private NavMeshSurface navMeshSurface;

        [Header("Player")]
        [Tooltip("Existing player object. If null, a capsule is auto-created.")]
        [SerializeField] private GameObject playerObject;

        [Tooltip("Player move speed (WASD).")]
        [SerializeField] private float playerSpeed = 6f;

        [Header("NPC Prefab")]
        [Tooltip("NPC prefab with NavMeshAgent + NPCStateMachine. " +
                 "If FloorGenerator's npcPrefab is set, this can be left null.")]
        [SerializeField] private GameObject npcPrefab;

        [Header("Generation Settings")]
        [Tooltip("Number of rooms to generate.")]
        [SerializeField] private int targetRoomCount = 7;

        [Tooltip("Random seed. 0 = random each time.")]
        [SerializeField] private int seed = 0;

        [Header("Test Controls")]
        [Tooltip("Simulate player health for defection tests.")]
        [SerializeField] private float simulatedPlayerHealth = 1.0f;

        [Tooltip("Simulate player accuracy for defection tests.")]
        [SerializeField] private float simulatedPlayerAccuracy = 0.65f;

        [Header("Debug")]
        [SerializeField] private bool autoStartBrainOnRoomEnter = true;
        [SerializeField] private bool drawNPCGizmos = true;

        // ====================================================================
        // Runtime State
        // ====================================================================

        private Transform _playerTransform;
        private NavMeshAgent _playerAgent;
        private RoomGraphConfig _currentConfig;
        private Dictionary<string, List<NPCStateMachine>> _roomNPCs = new();
        private List<NPCStateMachine> _allNPCs = new();
        private string _currentPlayerRoom;
        private int _killCount;
        private int _killStreak;
        private bool _isPaused;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void Start()
        {
            Log("═══════════════════════════════════════════════════", "HEADER");
            Log("  THRESHOLD — Enemy System Visual Test", "HEADER");
            Log("═══════════════════════════════════════════════════", "HEADER");

            if (floorGenerator == null)
            {
                Log("ERROR: FloorGenerator reference not set!", "ERROR");
                return;
            }

            if (brainController == null)
            {
                Log("WARNING: NPCBrainController not set. Brain eval disabled.", "WARN");
            }

            GenerateAndSpawn();

            Log("", "HEADER");
            Log("  DEBUG KEYS:", "HEADER");
            Log("  K = Kill nearest enemy", "HEADER");
            Log("  H = Set player health to 25%", "HEADER");
            Log("  D = Damage nearest NPC to 15% HP", "HEADER");
            Log("  L = Log all living NPCs", "HEADER");
            Log("  B = Force Brain evaluation", "HEADER");
            Log("  R = Regenerate floor", "HEADER");
            Log("  P = Pause/Resume", "HEADER");
            Log("═══════════════════════════════════════════════════", "HEADER");
        }

        private void Update()
        {
            if (_isPaused) return;

            HandlePlayerMovement();
            HandleDebugKeys();
            DetectPlayerRoom();
        }

        // ====================================================================
        // Generation + Spawning
        // ====================================================================

        private void GenerateAndSpawn()
        {
            // Generate floor config
            int usedSeed = seed > 0 ? seed : System.Environment.TickCount;
            Log($"Generating floor — Rooms: {targetRoomCount}, Seed: {usedSeed}");

            var difficulty = new DifficultyProfile
            {
                difficultyMultiplier = 1.0f,
                targetRoomCount = targetRoomCount,
                baseEnemiesPerRoom = 3,
                eliteCount = 1,
                eventProbability = 0.3f,
                preferredTactic = "ATTACK"
            };

            _currentConfig = Threshold.Generation.ProceduralRoomGenerator.GenerateFallback(difficulty, usedSeed);

            if (_currentConfig == null || _currentConfig.rooms.Count == 0)
            {
                Log("Generation FAILED — no config produced.", "ERROR");
                return;
            }

            Log($"Config generated: {_currentConfig.rooms.Count} rooms, " +
                $"{_currentConfig.edges.Count} edges.");

            // Build physical floor
            bool success = floorGenerator.BuildFloor(_currentConfig);
            if (!success)
            {
                Log("FloorGenerator.BuildFloor() failed!", "ERROR");
                return;
            }

            // Bake NavMesh at runtime (NPCs require this)
            BakeNavMesh();

            // Create or position player
            SetupPlayer();

            // Spawn NPCs
            SpawnAllNPCs();

            // Start Brain Controller
            if (brainController != null)
            {
                brainController.OnRunStart();
                Log($"Brain Controller initialized. Eval interval: 20s.");
            }
        }

        private void BakeNavMesh()
        {
            // Auto-create NavMeshSurface if not assigned
            if (navMeshSurface == null)
            {
                navMeshSurface = FindAnyObjectByType<NavMeshSurface>();
            }
            if (navMeshSurface == null)
            {
                // Create one on the FloorGenerator parent (covers all rooms)
                navMeshSurface = floorGenerator.gameObject.AddComponent<NavMeshSurface>();
                navMeshSurface.collectObjects = CollectObjects.Children;
                navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                Log("Auto-created NavMeshSurface on FloorGenerator.");
            }

            navMeshSurface.BuildNavMesh();
            Log($"NavMesh baked at runtime — NPCs can now pathfind.");
        }

        private void SetupPlayer()
        {
            if (playerObject == null)
            {
                // Auto-create player capsule
                playerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                playerObject.name = "TestPlayer";
                playerObject.tag = "Player";

                // Color it green
                var renderer = playerObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(Shader.Find("Standard"));
                    renderer.material.color = new Color(0.2f, 0.8f, 0.3f, 1f);
                }

                // Add NavMeshAgent for pathfinding through doorways
                _playerAgent = playerObject.AddComponent<NavMeshAgent>();
                _playerAgent.speed = playerSpeed;
                _playerAgent.radius = 0.4f;
                _playerAgent.height = 2f;
                _playerAgent.acceleration = 20f;

                // Add Rigidbody for physics interactions (kinematic)
                var rb = playerObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                Log("Auto-created player capsule (green).");
            }
            else
            {
                _playerAgent = playerObject.GetComponent<NavMeshAgent>();
            }

            _playerTransform = playerObject.transform;

            // Position at entry room
            Vector3 entryPos = floorGenerator.EntryWorldPosition;
            entryPos.y = 1f;
            playerObject.transform.position = entryPos;

            // Try to place on NavMesh
            if (NavMesh.SamplePosition(entryPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                playerObject.transform.position = hit.position;
                if (_playerAgent != null && !_playerAgent.isOnNavMesh)
                    _playerAgent.Warp(hit.position);
            }

            Log($"Player positioned at entry: {playerObject.transform.position}");
        }

        private void SpawnAllNPCs()
        {
            _roomNPCs.Clear();
            _allNPCs.Clear();
            _killCount = 0;
            _killStreak = 0;

            if (_currentConfig?.rooms == null) return;

            // Use FloorGenerator's spawn system
            _roomNPCs = floorGenerator.SpawnAllNPCs(_playerTransform);

            foreach (var kvp in _roomNPCs)
            {
                foreach (var npc in kvp.Value)
                {
                    _allNPCs.Add(npc);
                }
            }

            int totalNPCs = _allNPCs.Count;
            int roomsWithNPCs = _roomNPCs.Count;
            Log($"Spawned {totalNPCs} NPCs across {roomsWithNPCs} rooms.");

            // Log per-room breakdown
            foreach (var kvp in _roomNPCs)
            {
                var types = string.Join(", ", kvp.Value.Select(n => $"{n.archetype}"));
                Log($"  Room {kvp.Key}: {kvp.Value.Count} NPCs [{types}]");
            }
        }

        // ====================================================================
        // Player Movement (WASD)
        // ====================================================================

        private void HandlePlayerMovement()
        {
            if (_playerTransform == null) return;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (Mathf.Abs(h) < 0.01f && Mathf.Abs(v) < 0.01f) return;

            Vector3 moveDir = new Vector3(h, 0f, v).normalized;

            if (_playerAgent != null && _playerAgent.isOnNavMesh)
            {
                // Use NavMeshAgent for proper doorway navigation
                Vector3 target = _playerTransform.position + moveDir * playerSpeed * Time.deltaTime * 3f;
                _playerAgent.SetDestination(target);
            }
            else
            {
                // Fallback: direct transform movement
                _playerTransform.position += moveDir * playerSpeed * Time.deltaTime;
            }

            // Face movement direction
            if (moveDir.sqrMagnitude > 0.01f)
                _playerTransform.rotation = Quaternion.LookRotation(moveDir);
        }

        // ====================================================================
        // Room Detection
        // ====================================================================

        private void DetectPlayerRoom()
        {
            if (_currentConfig == null || _playerTransform == null) return;

            float mw = 10f; // Module width
            int col = Mathf.RoundToInt(_playerTransform.position.x / mw);
            int row = Mathf.RoundToInt(_playerTransform.position.z / mw);

            // Find room at this grid position
            var room = _currentConfig.rooms.Find(r => r.gridCol == col && r.gridRow == row);
            if (room == null) return;

            string roomId = room.roomId;

            if (roomId != _currentPlayerRoom)
            {
                string oldRoom = _currentPlayerRoom;
                _currentPlayerRoom = roomId;

                Log($"Player entered room: {roomId} ({room.role})", "ROOM");

                // Update Brain Controller
                if (autoStartBrainOnRoomEnter && brainController != null)
                {
                    // Exit previous room
                    if (!string.IsNullOrEmpty(oldRoom))
                        brainController.OnRoomExit();

                    // Enter new room with its NPCs
                    if (_roomNPCs.TryGetValue(roomId, out var npcsInRoom))
                    {
                        var living = npcsInRoom.Where(n => n != null && !n.IsDead).ToList();
                        brainController.OnRoomEnter(roomId, living, _playerTransform);
                        brainController.UpdatePlayerState(
                            simulatedPlayerHealth,
                            simulatedPlayerAccuracy,
                            _killStreak
                        );
                        Log($"Brain Controller: {living.Count} NPCs registered for room {roomId}.");
                    }
                }
            }
        }

        // ====================================================================
        // Debug Keys
        // ====================================================================

        private void HandleDebugKeys()
        {
            if (Input.GetKeyDown(KeyCode.K)) DebugKillNearest();
            if (Input.GetKeyDown(KeyCode.H)) DebugSetPlayerLowHealth();
            if (Input.GetKeyDown(KeyCode.D)) DebugDamageNearest();
            if (Input.GetKeyDown(KeyCode.L)) DebugLogAllNPCs();
            if (Input.GetKeyDown(KeyCode.B)) DebugForceBrainEval();
            if (Input.GetKeyDown(KeyCode.R)) DebugRegenerate();
            if (Input.GetKeyDown(KeyCode.P)) DebugTogglePause();
        }

        // --- K: Kill nearest enemy ---
        private void DebugKillNearest()
        {
            var nearest = FindNearestHostileNPC();
            if (nearest == null)
            {
                Log("No hostile NPCs alive to kill.", "DEBUG");
                return;
            }

            string id = nearest.npcId;
            string arch = nearest.archetype.ToString();
            nearest.TakeDamage(nearest.CurrentHealth + 1f);
            _killCount++;
            _killStreak++;

            Log($"[DEBUG] KILLED: {id} ({arch}) — Total kills: {_killCount}, Streak: {_killStreak}", "KILL");

            // Notify brain controller
            brainController?.OnNPCDeath(nearest);
            brainController?.UpdatePlayerState(simulatedPlayerHealth, simulatedPlayerAccuracy, _killStreak);
        }

        // --- H: Set player health low ---
        private void DebugSetPlayerLowHealth()
        {
            simulatedPlayerHealth = 0.25f;
            brainController?.UpdatePlayerState(simulatedPlayerHealth, simulatedPlayerAccuracy, _killStreak);
            Log("[DEBUG] Player health set to 25%. NPCs may trigger SUPPRESS.", "DEBUG");
        }

        // --- D: Damage nearest NPC to 15% HP ---
        private void DebugDamageNearest()
        {
            var nearest = FindNearestHostileNPC();
            if (nearest == null)
            {
                Log("No hostile NPCs alive to damage.", "DEBUG");
                return;
            }

            float targetHP = nearest.Stats.maxHealth * 0.15f;
            float damage = nearest.CurrentHealth - targetHP;
            if (damage > 0) nearest.TakeDamage(damage);

            Log($"[DEBUG] Damaged {nearest.npcId} ({nearest.archetype}) to " +
                $"{nearest.HealthPercent:P0} HP. May trigger RETREAT/DEFECTION.", "DEBUG");

            brainController?.UpdatePlayerState(simulatedPlayerHealth, simulatedPlayerAccuracy, _killStreak);
        }

        // --- L: Log all living NPCs ---
        private void DebugLogAllNPCs()
        {
            Log("═══════════════════ LIVING NPCs ═══════════════════", "DEBUG");
            int count = 0;
            foreach (var npc in _allNPCs)
            {
                if (npc == null || npc.IsDead) continue;
                count++;
                Log($"  {npc.npcId,-20} | {npc.archetype,-12} | {npc.CurrentState,-10} | " +
                    $"HP: {npc.HealthPercent:P0} | Allied: {npc.IsAllied}", "DEBUG");
            }
            Log($"  Total alive: {count} / {_allNPCs.Count}", "DEBUG");
            Log("═══════════════════════════════════════════════════", "DEBUG");
        }

        // --- B: Force Brain evaluation ---
        private void DebugForceBrainEval()
        {
            if (brainController == null)
            {
                Log("No Brain Controller assigned.", "DEBUG");
                return;
            }

            brainController.UpdatePlayerState(simulatedPlayerHealth, simulatedPlayerAccuracy, _killStreak);
            brainController.ForceEvaluation();
            Log("[DEBUG] Forced Brain evaluation.", "DEBUG");
        }

        // --- R: Regenerate ---
        private void DebugRegenerate()
        {
            Log("[DEBUG] Regenerating floor...", "DEBUG");
            brainController?.OnRoomExit();
            _currentPlayerRoom = null;
            seed = System.Environment.TickCount;
            GenerateAndSpawn();
        }

        // --- P: Pause ---
        private void DebugTogglePause()
        {
            _isPaused = !_isPaused;
            Time.timeScale = _isPaused ? 0f : 1f;
            Log($"[DEBUG] {(_isPaused ? "PAUSED" : "RESUMED")}", "DEBUG");
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private NPCStateMachine FindNearestHostileNPC()
        {
            if (_playerTransform == null) return null;

            NPCStateMachine nearest = null;
            float minDist = float.MaxValue;

            foreach (var npc in _allNPCs)
            {
                if (npc == null || npc.IsDead || npc.IsAllied) continue;

                float dist = Vector3.Distance(_playerTransform.position, npc.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = npc;
                }
            }
            return nearest;
        }

        private void Log(string msg, string category = "INFO")
        {
            string prefix = category switch
            {
                "HEADER" => "",
                "ERROR" => "  ❌ ",
                "WARN" => "  ⚠ ",
                "ROOM" => "  🚪 ",
                "KILL" => "  💀 ",
                "DEBUG" => "  🔧 ",
                _ => "  "
            };
            Debug.Log($"{prefix}{msg}");
        }

        // ====================================================================
        // Gizmos
        // ====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawNPCGizmos || _allNPCs == null) return;

            foreach (var npc in _allNPCs)
            {
                if (npc == null) continue;

                // Color by state
                Color col = npc.CurrentState switch
                {
                    NPCState.PATROL => Color.blue,
                    NPCState.ATTACK => Color.red,
                    NPCState.FLANK => new Color(1f, 0.5f, 0f), // Orange
                    NPCState.SUPPRESS => Color.yellow,
                    NPCState.RETREAT => Color.cyan,
                    NPCState.ALLIED => Color.green,
                    _ => Color.gray
                };

                if (npc.IsDead) col = Color.gray;

                // Sphere at NPC position
                Gizmos.color = col;
                float radius = npc.IsDead ? 0.3f : 0.6f;
                Gizmos.DrawSphere(npc.transform.position + Vector3.up, radius);

                // Label
                if (!npc.IsDead)
                {
                    string label = $"{npc.npcId}\n{npc.archetype} | {npc.CurrentState}\n" +
                                   $"HP: {npc.HealthPercent:P0}";
                    UnityEditor.Handles.color = col;
                    UnityEditor.Handles.Label(npc.transform.position + Vector3.up * 2.5f, label);
                }

                // Line to player
                if (_playerTransform != null && !npc.IsDead && !npc.IsAllied)
                {
                    Gizmos.color = new Color(col.r, col.g, col.b, 0.3f);
                    Gizmos.DrawLine(npc.transform.position + Vector3.up,
                        _playerTransform.position + Vector3.up);
                }
            }

            // Player indicator
            if (_playerTransform != null)
            {
                Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.8f);
                Gizmos.DrawWireSphere(_playerTransform.position + Vector3.up, 0.8f);

                // Current room indicator
                if (!string.IsNullOrEmpty(_currentPlayerRoom))
                {
                    UnityEditor.Handles.color = Color.white;
                    UnityEditor.Handles.Label(_playerTransform.position + Vector3.up * 3f,
                        $"Room: {_currentPlayerRoom}");
                }
            }
        }
#endif
    }
}
