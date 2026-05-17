// ============================================================================
// FloorGenerator.cs — Physical dungeon instantiation from RoomGraphConfig
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
// ============================================================================

using System.Collections.Generic;
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

        [Header("Debug")]
        [SerializeField] private bool logInstantiation = true;

        // Runtime state
        private readonly List<GameObject> _instantiatedRooms = new();
        private readonly List<GameObject> _instantiatedItems = new();
        private readonly Dictionary<string, RoomModule> _roomModuleMap = new();

        /// <summary>World position of the ENTRY room center.</summary>
        public Vector3 EntryWorldPosition { get; private set; }

        /// <summary>World position of the EXIT room center.</summary>
        public Vector3 ExitWorldPosition { get; private set; }

        /// <summary>The config currently built in the scene.</summary>
        public RoomGraphConfig CurrentConfig { get; private set; }

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

            float mw = GetModuleWidth();

            foreach (var roomConfig in config.rooms)
            {
                // Find matching prefab
                RoomModule prefab = FindPrefabForShape(roomConfig.shape);
                if (prefab == null)
                {
                    Debug.LogError($"[FloorGenerator] No prefab found for shape {roomConfig.shape}.");
                    continue;
                }

                // Find rotation to align doorways
                bool needN = roomConfig.HasDoorway(Direction.NORTH);
                bool needE = roomConfig.HasDoorway(Direction.EAST);
                bool needS = roomConfig.HasDoorway(Direction.SOUTH);
                bool needW = roomConfig.HasDoorway(Direction.WEST);

                int rotSteps = prefab.FindMatchingRotation(needN, needE, needS, needW);
                if (rotSteps < 0)
                {
                    // Fallback: use exact match or default rotation
                    rotSteps = 0;
                    Debug.LogWarning($"[FloorGenerator] No rotation match for {roomConfig.roomId} " +
                                     $"({roomConfig.shape}). Using default orientation.");
                }

                roomConfig.rotationDegrees = rotSteps * 90;

                // Calculate world position: col * width, 0, row * -width
                Vector3 worldPos = new(
                    roomConfig.gridCol * mw,
                    0f,
                    roomConfig.gridRow * -mw
                );

                // Instantiate
                Quaternion rotation = Quaternion.Euler(0, rotSteps * 90f, 0);
                GameObject roomObj = Instantiate(prefab.gameObject, worldPos, rotation, transform);
                roomObj.name = $"{roomConfig.roomId}_{roomConfig.shape}_{roomConfig.role}";

                RoomModule module = roomObj.GetComponent<RoomModule>();
                _instantiatedRooms.Add(roomObj);
                _roomModuleMap[roomConfig.roomId] = module;

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
            return true;
        }

        /// <summary>
        /// Destroys all instantiated room and item objects.
        /// </summary>
        public void CleanUp()
        {
            foreach (var obj in _instantiatedRooms)
            {
                if (obj != null) Destroy(obj);
            }
            foreach (var obj in _instantiatedItems)
            {
                if (obj != null) Destroy(obj);
            }
            _instantiatedRooms.Clear();
            _instantiatedItems.Clear();
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

        /// <summary>
        /// Returns spawn point transforms for a given room.
        /// </summary>
        public Transform[] GetSpawnPoints(string roomId)
        {
            var module = GetRoomModule(roomId);
            return module != null ? module.spawnPoints : null;
        }

        // ====================================================================
        // Internal
        // ====================================================================

        private float GetModuleWidth()
        {
            if (moduleWidthOverride > 0f) return moduleWidthOverride;
            if (roomPrefabs != null && roomPrefabs.Length > 0 && roomPrefabs[0] != null)
                return roomPrefabs[0].moduleWidth;
            return 20f; // Default fallback
        }

        private RoomModule FindPrefabForShape(RoomShape shape)
        {
            if (roomPrefabs == null) return null;

            // First pass: exact shape match
            foreach (var prefab in roomPrefabs)
            {
                if (prefab != null && prefab.shape == shape)
                    return prefab;
            }

            // Second pass: match by doorway count
            int targetDoors = shape switch
            {
                RoomShape.CROSSROADS => 4,
                RoomShape.T_JUNCTION => 3,
                RoomShape.STRAIGHT => 2,
                RoomShape.CORNER => 2,
                RoomShape.DEAD_END => 1,
                _ => 2
            };

            foreach (var prefab in roomPrefabs)
            {
                if (prefab != null && prefab.DoorwayCount() == targetDoors)
                    return prefab;
            }

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

                GameObject itemObj = Instantiate(prefab, position, Quaternion.identity, module.transform);
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
