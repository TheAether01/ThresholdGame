// ============================================================================
// FloorGenerator.cs — Physical dungeon instantiation from RoomGraphConfig
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using Threshold.Core;
using UnityEngine;

namespace Threshold.Generation
{
    /// <summary>
    /// Takes a validated RoomGraphConfig and instantiates physical room prefabs
    /// in the scene. Handles prefab matching, rotation, positioning, and
    /// populating spawn points and items from the config.
    /// </summary>
    public class FloorGenerator : MonoBehaviour
    {
        [Header("Room Prefabs")]
        [Tooltip("Available room prefabs. Must have RoomModule components.")]
        [SerializeField] private RoomModule[] roomPrefabs;

        [Header("Player Prefab")]
        [Tooltip("Player prefab with PlayerController, PlayerHealth, PlayerWeapon. Auto-spawned at ENTRY room.")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Item Prefabs")]
        [Tooltip("Prefabs for each ItemType, indexed by enum order.")]
        [SerializeField] private GameObject healthKitPrefab;
        [SerializeField] private GameObject ammoCachePrefab;
        [SerializeField] private GameObject weaponPickupPrefab;
        [SerializeField] private GameObject shieldBoostPrefab;
        [SerializeField] private GameObject trapPrefab;

        [Header("Settings")]
        [Tooltip("Module width override. 0 = use prefab's moduleWidth.")]
        [SerializeField] private float moduleWidthOverride = 0f;

        [Tooltip("Height offset above floor when spawning the player.")]
        [SerializeField] private float playerSpawnYOffset = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool logInstantiation = true;

        // Runtime state
        private readonly List<GameObject> _instantiatedRooms = new();
        private readonly List<GameObject> _instantiatedItems = new();
        private readonly Dictionary<string, RoomModule> _roomModuleMap = new();

        /// <summary>
        /// Unscaled container for spawned entities (NPCs, items, player).
        /// Prevents child objects from inheriting room prefab scale.
        /// </summary>
        private Transform _entityContainer;
        private GameObject _spawnedPlayer;

        /// <summary>World position of the ENTRY room center.</summary>
        public Vector3 EntryWorldPosition { get; private set; }

        /// <summary>World position of the EXIT room center.</summary>
        public Vector3 ExitWorldPosition { get; private set; }

        /// <summary>The config currently built in the scene.</summary>
        public RoomGraphConfig CurrentConfig { get; private set; }

        /// <summary>Reference to the spawned player GameObject (null if using a pre-placed player).</summary>
        public GameObject SpawnedPlayer => _spawnedPlayer;

        /// <summary>Reference to the spawned player's Transform.</summary>
        public Transform PlayerTransform => _spawnedPlayer != null ? _spawnedPlayer.transform : null;

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Builds the entire floor from a validated config.
        /// Cleans up any previous dungeon first.
        /// </summary>
        public bool BuildFloor(RoomGraphConfig config)
        {
            if (config == null || config.rooms == null || config.rooms.Count == 0)
            {
                Debug.LogError("[FloorGenerator] Cannot build — config is null or empty.");
                return false;
            }

            CleanUp();
            CurrentConfig = config;

            // Create unscaled root-level container for entities (NPCs, items)
            // Parented to scene root (not FloorGenerator) to guarantee world scale = (1,1,1)
            var containerObj = new GameObject("_EntityContainer");
            containerObj.transform.position = Vector3.zero;
            containerObj.transform.rotation = Quaternion.identity;
            containerObj.transform.localScale = Vector3.one;
            _entityContainer = containerObj.transform;

            float mw = GetModuleWidth();
            bool hasSkippedCriticalRoom = false;

            foreach (var roomConfig in config.rooms)
            {
                // Find matching prefab
                RoomModule prefab = FindPrefabForShape(roomConfig.shape);
                if (prefab == null)
                {
                    Debug.LogError($"[FloorGenerator] No prefab found for shape {roomConfig.shape}.");
                    if (roomConfig.role == RoomRole.ENTRY || roomConfig.role == RoomRole.EXIT)
                        hasSkippedCriticalRoom = true;
                    continue;
                }

                // Find rotation to align doorways
                bool needN = roomConfig.HasDoorway(Direction.NORTH);
                bool needE = roomConfig.HasDoorway(Direction.EAST);
                bool needS = roomConfig.HasDoorway(Direction.SOUTH);
                bool needW = roomConfig.HasDoorway(Direction.WEST);

                // Prefer exact match (no extra doorways facing void)
                int rotSteps = prefab.FindExactMatchingRotation(needN, needE, needS, needW);
                if (rotSteps < 0)
                {
                    // Superset fallback — extra doorways will be sealed below
                    rotSteps = prefab.FindMatchingRotation(needN, needE, needS, needW);
                }
                if (rotSteps < 0)
                {
                    rotSteps = 0;
                    Debug.LogError($"[FloorGenerator] No rotation match for {roomConfig.roomId} " +
                                   $"({roomConfig.shape}) needing doors=[" +
                                   $"{(needN ? "N" : "")}{(needE ? "E" : "")}" +
                                   $"{(needS ? "S" : "")}{(needW ? "W" : "")}]. " +
                                   $"Prefab defaults=[{(prefab.doorNorth ? "N" : "")}" +
                                   $"{(prefab.doorEast ? "E" : "")}{(prefab.doorSouth ? "S" : "")}" +
                                   $"{(prefab.doorWest ? "W" : "")}]. Using 0° fallback.");
                }

                roomConfig.rotationDegrees = rotSteps * 90;

                // Calculate world position: col * width, 0, row * width
                // Higher gridRow = NORTH = +Z (must match edge direction convention)
                Vector3 worldPos = new(
                    roomConfig.gridCol * mw,
                    0f,
                    roomConfig.gridRow * mw
                );

                // Instantiate
                Quaternion rotation = Quaternion.Euler(0, rotSteps * 90f, 0);
                GameObject roomObj = Instantiate(prefab.gameObject, worldPos, rotation, transform);
                roomObj.name = $"{roomConfig.roomId}_{roomConfig.shape}_{roomConfig.role}";

                RoomModule module = roomObj.GetComponent<RoomModule>();
                _instantiatedRooms.Add(roomObj);
                _roomModuleMap[roomConfig.roomId] = module;

                // Seal extra doorways that face void (superset match case)
                SealUnusedDoorways(module, rotSteps, needN, needE, needS, needW, mw);

                // Track entry/exit positions
                if (roomConfig.role == RoomRole.ENTRY)
                {
                    EntryWorldPosition = module.playerStartPoint != null
                        ? module.playerStartPoint.position
                        : worldPos;
                }
                else if (roomConfig.role == RoomRole.EXIT)
                {
                    ExitWorldPosition = worldPos;
                }

                // Populate items
                PopulateRoomItems(roomConfig, module);

                if (logInstantiation)
                {
                    Debug.Log($"[FloorGenerator] Placed {roomConfig.roomId}: " +
                             $"{roomConfig.shape} / {roomConfig.role} at ({roomConfig.gridCol},{roomConfig.gridRow}) " +
                             $"rot={roomConfig.rotationDegrees}° enemies={roomConfig.TotalEnemyCount()}");
                }
            }

            Debug.Log($"[FloorGenerator] Floor built: {_instantiatedRooms.Count} rooms, " +
                     $"Entry={EntryWorldPosition}, Exit={ExitWorldPosition}");

            // M12 FIX: Fail if critical rooms were skipped
            if (hasSkippedCriticalRoom)
            {
                Debug.LogError("[FloorGenerator] ENTRY or EXIT room could not be instantiated!");
                return false;
            }

            // L3 FIX: Remind about NavMesh — NPCs using NavMeshAgent will fail without it
            Debug.Log("[FloorGenerator] ⚠ NavMesh: If using NavMeshAgent NPCs, rebake NavMesh now. " +
                      "Use NavMeshSurface.BuildNavMesh() at runtime or bake in Editor.");

            // Auto-spawn player at entry position
            if (playerPrefab != null)
            {
                SpawnPlayer();
            }
            else
            {
                // If no prefab, try to find an existing player in the scene and teleport
                var existingPlayer = GameObject.FindGameObjectWithTag("Player");
                if (existingPlayer != null)
                {
                    var controller = existingPlayer.GetComponent<Threshold.Player.PlayerController>();
                    if (controller != null)
                    {
                        controller.TeleportTo(EntryWorldPosition + Vector3.up * playerSpawnYOffset);
                    }
                    else
                    {
                        existingPlayer.transform.position = EntryWorldPosition + Vector3.up * playerSpawnYOffset;
                    }
                    Debug.Log($"[FloorGenerator] Teleported existing player to entry: {EntryWorldPosition}");
                }
                else
                {
                    Debug.LogWarning("[FloorGenerator] No playerPrefab assigned and no Player found in scene. " +
                                     "Assign a prefab or place a Player GameObject with tag 'Player'.");
                }
            }

            return true;
        }

        /// <summary>
        /// Destroys all instantiated room and item objects.
        /// </summary>
        public void CleanUp()
        {
            // Destroy spawned player
            if (_spawnedPlayer != null)
            {
                Destroy(_spawnedPlayer);
                _spawnedPlayer = null;
            }

            // Destroy entity container (takes all NPCs and items with it)
            if (_entityContainer != null)
            {
                Destroy(_entityContainer.gameObject);
                _entityContainer = null;
            }

            foreach (var obj in _instantiatedRooms)
            {
                if (obj != null) Destroy(obj);
            }
            foreach (var obj in _instantiatedItems)
            {
                if (obj != null) Destroy(obj);
            }
            foreach (var obj in _instantiatedNPCs)
            {
                if (obj != null) Destroy(obj);
            }
            _instantiatedRooms.Clear();
            _instantiatedItems.Clear();
            _instantiatedNPCs.Clear();
            _roomModuleMap.Clear();
            CurrentConfig = null;
        }

        /// <summary>
        /// Returns the RoomModule instance for a given room ID.
        /// </summary>
        public RoomModule GetRoomModule(string roomId)
        {
            _roomModuleMap.TryGetValue(roomId, out var module);
            return module;
        }

        [Header("NPC Prefabs")]
        [Tooltip("Default NPC prefab. Must have NPCStateMachine + NavMeshAgent.")]
        [SerializeField] private GameObject npcPrefab;

        // NPC tracking
        private readonly List<GameObject> _instantiatedNPCs = new();

        /// <summary>
        /// Returns spawn point transforms for a given room.
        /// </summary>
        public Transform[] GetSpawnPoints(string roomId)
        {
            var module = GetRoomModule(roomId);
            return module != null ? module.spawnPoints : null;
        }

        // ====================================================================
        // Player Spawning
        // ====================================================================

        /// <summary>
        /// Spawns the player at the ENTRY room position. If a player already
        /// exists in the scene, teleports it instead.
        /// Returns the player's Transform for use with SpawnAllNPCs.
        /// </summary>
        public Transform SpawnPlayer()
        {
            // Check if a player already exists
            var existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer != null)
            {
                // Teleport existing player
                existingPlayer.transform.position = EntryWorldPosition + Vector3.up * playerSpawnYOffset;

                var controller = existingPlayer.GetComponent<Threshold.Player.PlayerController>();
                controller?.TeleportTo(EntryWorldPosition + Vector3.up * playerSpawnYOffset);

                _spawnedPlayer = existingPlayer;
                Debug.Log($"[FloorGenerator] Existing player teleported to {EntryWorldPosition}");
                return existingPlayer.transform;
            }

            // Spawn from prefab
            if (playerPrefab == null)
            {
                Debug.LogError("[FloorGenerator] Cannot spawn player — playerPrefab is not assigned.");
                return null;
            }

            Vector3 spawnPos = EntryWorldPosition + Vector3.up * playerSpawnYOffset;
            _spawnedPlayer = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            _spawnedPlayer.name = "Player";

            // Ensure it has the Player tag
            if (!_spawnedPlayer.CompareTag("Player"))
            {
                try { _spawnedPlayer.tag = "Player"; }
                catch { Debug.LogWarning("[FloorGenerator] Could not set 'Player' tag. Add it via Tags and Layers."); }
            }

            // Set up camera to follow the new player
            var camera = Threshold.UI.TopDownCamera.Instance;
            if (camera != null)
            {
                camera.SetTarget(_spawnedPlayer.transform);
            }

            if (logInstantiation)
                Debug.Log($"[FloorGenerator] Player spawned at {spawnPos}");

            return _spawnedPlayer.transform;
        }

        /// <summary>
        /// Convenience method: spawns player + all NPCs in one call.
        /// Use this after BuildFloor() if you didn't assign a playerPrefab
        /// (which auto-spawns) or need to manually control the sequence.
        /// </summary>
        public Dictionary<string, List<Threshold.NPC.NPCStateMachine>> SpawnPlayerAndNPCs()
        {
            Transform player = SpawnPlayer();
            if (player == null)
            {
                Debug.LogError("[FloorGenerator] Cannot spawn NPCs without a player.");
                return new Dictionary<string, List<Threshold.NPC.NPCStateMachine>>();
            }
            return SpawnAllNPCs(player);
        }

        /// <summary>
        /// C7 FIX: Spawns NPC GameObjects at the spawn zones defined in a room config.
        /// Returns all spawned NPCStateMachine components for registration with the Brain Controller.
        /// Requires a player Transform for NPC initialization.
        /// </summary>
        public List<Threshold.NPC.NPCStateMachine> SpawnNPCsForRoom(
            RoomConfig roomConfig, Transform player, float spawnYOffset = 0.5f)
        {
            var spawned = new List<Threshold.NPC.NPCStateMachine>();

            if (roomConfig.spawnZones == null || roomConfig.spawnZones.Count == 0)
                return spawned;

            if (npcPrefab == null)
            {
                Debug.LogWarning("[FloorGenerator] Cannot spawn NPCs — npcPrefab is not assigned.");
                return spawned;
            }

            var module = GetRoomModule(roomConfig.roomId);
            if (module == null)
            {
                Debug.LogWarning($"[FloorGenerator] Cannot spawn NPCs — room {roomConfig.roomId} not instantiated.");
                return spawned;
            }

            int npcIndex = 0;
            foreach (var zone in roomConfig.spawnZones)
            {
                for (int i = 0; i < zone.count; i++)
                {
                    // Calculate world position from local offset + room position
                    Vector3 localOffset = zone.localPosition;
                    // Spread NPCs within the same zone in a circle (not stacked)
                    if (zone.count > 1)
                    {
                        float angle = (360f / zone.count) * i * Mathf.Deg2Rad;
                        float spreadRadius = 2.0f; // 2m radius circle
                        localOffset.x += Mathf.Cos(angle) * spreadRadius;
                        localOffset.z += Mathf.Sin(angle) * spreadRadius;
                    }
                    // Add slight random jitter on top of the spread
                    localOffset.x += UnityEngine.Random.Range(-0.5f, 0.5f);
                    localOffset.z += UnityEngine.Random.Range(-0.5f, 0.5f);
                    localOffset.y = spawnYOffset;

                    Vector3 worldPos = module.transform.TransformPoint(localOffset);

                    // Spawn under unscaled container to prevent inheriting room scale
                    Transform parent = _entityContainer != null ? _entityContainer : null;
                    string npcId = $"npc_{roomConfig.roomId}_{npcIndex}";

                    // ── Instantiate prefab ──────────────────────────
                    GameObject prefabInstance = Instantiate(npcPrefab, worldPos, Quaternion.identity, parent);

                    // ── Handle scaled prefabs (e.g. RoboCop at 20x) ──
                    // NavMeshAgent doesn't work on GameObjects with large scale.
                    // Fix: create a scale-1 wrapper parent, put the visual model as child,
                    // and move NavMeshAgent + NPCStateMachine + Collider to the wrapper.

                    // ELITE enemies are 2× the normal model size (big boss feel)
                    if (zone.archetype == Threshold.Core.NPCArchetype.ELITE)
                    {
                        prefabInstance.transform.localScale *= 2f;
                    }

                    Vector3 prefabScale = prefabInstance.transform.localScale;
                    bool isScaled = prefabScale.x > 1.5f || prefabScale.y > 1.5f || prefabScale.z > 1.5f;

                    GameObject entityRoot; // The root that gets NavMeshAgent, StateMachine, Collider

                    if (isScaled)
                    {
                        // Create unscaled wrapper — this is what NavMeshAgent drives
                        entityRoot = new GameObject(npcId);
                        entityRoot.transform.position = worldPos;
                        entityRoot.transform.rotation = Quaternion.identity;
                        entityRoot.transform.SetParent(parent, true);

                        // Destroy components from the prefab that belong on the wrapper.
                        // ORDER MATTERS: NPCStateMachine has [RequireComponent(typeof(NavMeshAgent))],
                        // so we must destroy NPCStateMachine FIRST, then NavMeshAgent.
                        var prefabSM = prefabInstance.GetComponent<Threshold.NPC.NPCStateMachine>();
                        if (prefabSM != null) DestroyImmediate(prefabSM);
                        var prefabAgent = prefabInstance.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        if (prefabAgent != null) DestroyImmediate(prefabAgent);
                        var prefabCol = prefabInstance.GetComponent<CapsuleCollider>();
                        if (prefabCol != null) DestroyImmediate(prefabCol);

                        // Re-parent visual model under the wrapper
                        prefabInstance.transform.SetParent(entityRoot.transform, false);
                        prefabInstance.transform.localPosition = Vector3.zero;
                        // Scale stays on the visual model (e.g. 40x)

                        // Add properly-sized components to the unscaled wrapper
                        var wrapperAgent = entityRoot.AddComponent<UnityEngine.AI.NavMeshAgent>();
                        wrapperAgent.speed = 3.5f;
                        wrapperAgent.radius = 0.5f;
                        wrapperAgent.height = 2f;
                        wrapperAgent.baseOffset = 0f;
                        wrapperAgent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.HighQualityObstacleAvoidance;

                        // Add world-scale collider to wrapper, sized to match the visual model
                        var wrapperCol = entityRoot.AddComponent<CapsuleCollider>();
                        // Calculate bounds from the visual model's renderers
                        var childRenderers = prefabInstance.GetComponentsInChildren<Renderer>();
                        if (childRenderers.Length > 0)
                        {
                            Bounds visualBounds = childRenderers[0].bounds;
                            foreach (var r in childRenderers) visualBounds.Encapsulate(r.bounds);
                            // Convert world-space bounds to wrapper-local space
                            wrapperCol.center = entityRoot.transform.InverseTransformPoint(visualBounds.center);
                            wrapperCol.height = visualBounds.size.y;
                            wrapperCol.radius = Mathf.Max(visualBounds.extents.x, visualBounds.extents.z) * 0.5f;
                        }
                        else
                        {
                            // Fallback: generic humanoid size
                            wrapperCol.height = 2f;
                            wrapperCol.radius = 0.5f;
                            wrapperCol.center = Vector3.up;
                        }

                        if (logInstantiation)
                            Debug.Log($"[FloorGenerator] Wrapped scaled prefab ({prefabScale.x}x) in unscaled parent. " +
                                $"Collider: h={wrapperCol.height:F1} r={wrapperCol.radius:F1} center={wrapperCol.center}");
                    }
                    else
                    {
                        entityRoot = prefabInstance;

                        // ── Ensure physics components exist ──────────────
                        if (entityRoot.GetComponentInChildren<Collider>() == null)
                        {
                            var capsule = entityRoot.AddComponent<CapsuleCollider>();
                            var renderers = entityRoot.GetComponentsInChildren<Renderer>();
                            if (renderers.Length > 0)
                            {
                                Bounds b = renderers[0].bounds;
                                foreach (var r in renderers) b.Encapsulate(r.bounds);
                                capsule.center = entityRoot.transform.InverseTransformPoint(b.center);
                                capsule.height = b.size.y;
                                capsule.radius = Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
                            }
                            else
                            {
                                capsule.height = 2f;
                                capsule.center = Vector3.up;
                                capsule.radius = 0.5f;
                            }
                        }

                        // NavMeshAgent: required by NPCStateMachine
                        if (entityRoot.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                            entityRoot.AddComponent<UnityEngine.AI.NavMeshAgent>();
                    }

                    entityRoot.name = npcId;

                    // Disable Animator root motion so NavMesh controls movement
                    var anim = entityRoot.GetComponentInChildren<Animator>();
                    if (anim != null) anim.applyRootMotion = false;

                    // ── NPCStateMachine on the entity root ──────────
                    var sm = entityRoot.GetComponent<Threshold.NPC.NPCStateMachine>();
                    if (sm == null)
                        sm = entityRoot.AddComponent<Threshold.NPC.NPCStateMachine>();

                    sm.Initialize(npcId, zone.archetype, player);

                    // Apply difficulty scaling from the floor config
                    float diffMult = CurrentConfig?.difficulty?.difficultyMultiplier ?? 1.0f;
                    sm.ApplyDifficultyModifiers(diffMult);

                    // ── Force NavMeshAgent onto the NavMesh ─────────
                    var agent = entityRoot.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent != null)
                    {
                        // Sample nearest valid NavMesh position and warp there
                        if (UnityEngine.AI.NavMesh.SamplePosition(worldPos, out UnityEngine.AI.NavMeshHit navHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            agent.Warp(navHit.position);
                        }
                        else
                        {
                            Debug.LogWarning($"[FloorGenerator] NPC {npcId} could not find NavMesh near {worldPos}");
                        }
                    }

                    spawned.Add(sm);
                    _instantiatedNPCs.Add(entityRoot);
                    npcIndex++;

                    if (logInstantiation)
                        Debug.Log($"[FloorGenerator] Spawned {npcId} ({zone.archetype}) at {worldPos}" +
                            (isScaled ? " [wrapped]" : ""));
                }
            }

            return spawned;
        }

        /// <summary>
        /// Spawns NPCs for ALL rooms in the current config.
        /// Returns a dictionary of roomId → list of NPCStateMachines.
        /// </summary>
        public Dictionary<string, List<Threshold.NPC.NPCStateMachine>> SpawnAllNPCs(Transform player)
        {
            var result = new Dictionary<string, List<Threshold.NPC.NPCStateMachine>>();

            if (CurrentConfig?.rooms == null) return result;

            foreach (var roomConfig in CurrentConfig.rooms)
            {
                var npcs = SpawnNPCsForRoom(roomConfig, player);
                if (npcs.Count > 0)
                    result[roomConfig.roomId] = npcs;
            }

            Debug.Log($"[FloorGenerator] Spawned NPCs in {result.Count} rooms, " +
                      $"{result.Values.Sum(l => l.Count)} total NPCs.");
            return result;
        }

        // ====================================================================
        // Internal
        // ====================================================================

        /// <summary>
        /// Seals doorways in the instantiated prefab that are NOT needed by
        /// the graph (extra openings from superset rotation match).
        /// Spawns wall cubes to block them so players can't see void.
        /// </summary>
        private void SealUnusedDoorways(RoomModule module, int rotSteps,
            bool needN, bool needE, bool needS, bool needW, float mw)
        {
            // C7 FIX: Use rotation step 0 because the prefab's booleans describe
            // the un-rotated state. The GameObject is already physically rotated,
            // so its local +Z/+X/etc. already point to the correct world directions.
            // We need the DEFAULT doorway pattern, not the rotated one.
            bool[] defaults = { module.doorNorth, module.doorEast, module.doorSouth, module.doorWest };
            // Rotate the defaults to match the applied rotation
            bool hasN = defaults[((0 - rotSteps) % 4 + 4) % 4];
            bool hasE = defaults[((1 - rotSteps) % 4 + 4) % 4];
            bool hasS = defaults[((2 - rotSteps) % 4 + 4) % 4];
            bool hasW = defaults[((3 - rotSteps) % 4 + 4) % 4];

            float half = mw * 0.5f;
            // Scale seal dimensions proportionally to module width
            // (at mw=10: height=3, thickness=0.2, doorWidth=4)
            float wallHeight = mw * 0.3f;
            float wallThickness = mw * 0.02f;
            float doorWidth = mw * 0.4f;

            // If prefab has a door but graph doesn't need it, seal it.
            // Seal positions use LOCAL coordinates — the parent is rotated so
            // local +Z is always the prefab's original NORTH, regardless of
            // world-space orientation. We need to seal in the LOCAL direction
            // that corresponds to each WORLD direction after rotation.
            // The need flags (needN/E/S/W) are in WORLD space.
            // We must convert them to local space to know which local face to seal.
            // Local face for world NORTH = rotate NORTH backward by rotSteps
            int invRot = (4 - rotSteps) % 4;
            // World directions mapped to local indices: N=0,E=1,S=2,W=3
            // localIndex = (worldIndex + invRot) % 4
            // Local face positions: 0=+Z, 1=+X, 2=-Z, 3=-X
            bool[] worldNeed = { needN, needE, needS, needW };
            bool[] worldHas = { hasN, hasE, hasS, hasW };

            for (int dir = 0; dir < 4; dir++)
            {
                if (!worldHas[dir] || worldNeed[dir]) continue;

                // Convert world direction to local direction
                int localDir = (dir + invRot) % 4;
                Vector3 pos = localDir switch
                {
                    0 => new Vector3(0, wallHeight * 0.5f, half),   // local NORTH (+Z)
                    1 => new Vector3(half, wallHeight * 0.5f, 0),   // local EAST (+X)
                    2 => new Vector3(0, wallHeight * 0.5f, -half),  // local SOUTH (-Z)
                    3 => new Vector3(-half, wallHeight * 0.5f, 0),  // local WEST (-X)
                    _ => Vector3.zero
                };
                Vector3 scale = (localDir == 0 || localDir == 2)
                    ? new Vector3(doorWidth, wallHeight, wallThickness)
                    : new Vector3(wallThickness, wallHeight, doorWidth);

                SpawnSealWall(module.transform, pos, scale);
            }
        }

        private void SpawnSealWall(Transform parent, Vector3 localPos, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "SealWall";
            wall.transform.SetParent(parent);
            wall.transform.localRotation = Quaternion.identity;

            // Compensate for parent scale: divide desired world-space dimensions
            // by parent's lossy scale so the wall appears at the correct size.
            // Without this, a parent scale of (3,1.5,3) would triple the wall.
            Vector3 parentScale = parent.lossyScale;
            wall.transform.localPosition = new Vector3(
                localPos.x / parentScale.x,
                localPos.y / parentScale.y,
                localPos.z / parentScale.z
            );
            wall.transform.localScale = new Vector3(
                scale.x / parentScale.x,
                scale.y / parentScale.y,
                scale.z / parentScale.z
            );

            // Match existing wall material if possible
            var existingWall = parent.Find("Floor");
            if (existingWall != null)
            {
                var mat = existingWall.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (mat != null) wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private float GetModuleWidth()
        {
            if (moduleWidthOverride > 0f) return moduleWidthOverride;
            if (roomPrefabs != null && roomPrefabs.Length > 0 && roomPrefabs[0] != null)
                return roomPrefabs[0].moduleWidth;
            return 10f; // M1 FIX: Default matches 10x10 prefab standard
        }

        private RoomModule FindPrefabForShape(RoomShape shape)
        {
            if (roomPrefabs == null) return null;

            // Exact shape match only (M3 FIX: removed doorway-count fallback
            // that could return STRAIGHT for CORNER or vice versa)
            foreach (var prefab in roomPrefabs)
            {
                if (prefab != null && prefab.shape == shape)
                    return prefab;
            }

            Debug.LogWarning($"[FloorGenerator] No prefab with shape {shape} in the array.");
            return null;
        }

        private void PopulateRoomItems(RoomConfig roomConfig, RoomModule module)
        {
            if (roomConfig.items == null || roomConfig.items.Count == 0) return;

            int slotIndex = 0;
            foreach (var item in roomConfig.items)
            {
                GameObject prefab = GetItemPrefab(item.itemType);
                if (prefab == null) continue;

                // Use item slot transforms if available, otherwise use local position
                Vector3 position;
                if (module.itemSlots != null && slotIndex < module.itemSlots.Length &&
                    module.itemSlots[slotIndex] != null)
                {
                    position = module.itemSlots[slotIndex].position;
                    slotIndex++;
                }
                else
                {
                    position = module.transform.TransformPoint(item.localPosition);
                }

                // Spawn under unscaled container to prevent inheriting room scale
                Transform itemParent = _entityContainer != null ? _entityContainer : null;
                GameObject itemObj = Instantiate(prefab, position, Quaternion.identity, itemParent);
                itemObj.name = $"Item_{item.itemType}";
                _instantiatedItems.Add(itemObj);
            }
        }

        private GameObject GetItemPrefab(ItemType type)
        {
            return type switch
            {
                ItemType.HEALTH_KIT => healthKitPrefab,
                ItemType.AMMO_CACHE => ammoCachePrefab,
                ItemType.WEAPON_PICKUP => weaponPickupPrefab,
                ItemType.SHIELD_BOOST => shieldBoostPrefab,
                ItemType.TRAP => trapPrefab,
                _ => null
            };
        }

        private void OnDestroy()
        {
            CleanUp();
        }
    }
}
