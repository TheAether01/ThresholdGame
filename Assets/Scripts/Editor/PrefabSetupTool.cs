// ============================================================================
// PrefabSetupTool.cs — Editor tool to fix and configure room prefabs
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Place in Assets/Scripts/Editor/. Run from menu:
// Tools → Threshold → Setup Room Prefabs
// ============================================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Threshold.Core;
using Threshold.Generation;

namespace Threshold.Editor
{
    public class PrefabSetupTool
    {
        // The material GUID from your existing prefabs
        private const string WALL_MATERIAL_GUID = "31321ba15b8f8eb4c954353edc038b1d";

        [MenuItem("Tools/Threshold/Setup Room Prefabs")]
        public static void SetupAllPrefabs()
        {
            string prefabFolder = "Assets/Prefabs";

            // Load the material used by existing walls
            string matPath = AssetDatabase.GUIDToAssetPath(WALL_MATERIAL_GUID);
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (wallMat == null)
            {
                Debug.LogWarning("[PrefabSetup] Could not load wall material, will use default.");
            }

            // ================================================================
            // Define the 5 room shapes
            // ================================================================
            // Each room is 10x10 units. Walls are at ±5 from center.
            // Doorway opening = 4-unit gap in the center of the wall.
            // Wall segments: two 3-unit pieces flanking the 4-unit gap.
            // ================================================================

            SetupPrefab($"{prefabFolder}/Module005.prefab", "Room_DeadEnd",
                RoomShape.DEAD_END, wallMat,
                wallNorth: true, wallEast: true, wallSouth: false, wallWest: true);

            SetupPrefab($"{prefabFolder}/Module003.prefab", "Room_Straight",
                RoomShape.STRAIGHT, wallMat,
                wallNorth: false, wallEast: true, wallSouth: false, wallWest: true);

            SetupPrefab($"{prefabFolder}/Module004.prefab", "Room_Corner",
                RoomShape.CORNER, wallMat,
                wallNorth: false, wallEast: false, wallSouth: true, wallWest: true);

            SetupPrefab($"{prefabFolder}/Module002.prefab", "Room_TJunction",
                RoomShape.T_JUNCTION, wallMat,
                wallNorth: false, wallEast: false, wallSouth: false, wallWest: true);

            SetupPrefab($"{prefabFolder}/Module001.prefab", "Room_Crossroads",
                RoomShape.CROSSROADS, wallMat,
                wallNorth: false, wallEast: false, wallSouth: false, wallWest: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PrefabSetup] ✓ All 5 room prefabs configured successfully!");
        }

        private static void SetupPrefab(string assetPath, string newName,
            RoomShape shape, Material mat,
            bool wallNorth, bool wallEast, bool wallSouth, bool wallWest)
        {
            // Load the prefab for editing
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogError($"[PrefabSetup] Could not load prefab at: {assetPath}");
                return;
            }

            // Open prefab for editing
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            // Reset root position
            root.transform.localPosition = Vector3.zero;
            root.name = newName;

            // ============================================================
            // Step 1: Clear all existing children
            // ============================================================
            while (root.transform.childCount > 0)
            {
                Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            }

            // ============================================================
            // Step 2: Create Floor (10x10)
            // ============================================================
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform);
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);
            floor.transform.localRotation = Quaternion.identity;
            if (mat != null) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // ============================================================
            // Step 3: Create Walls (with doorway openings)
            // ============================================================
            // Room is 10x10. Walls at ±5 from center.
            // Each wall with a doorway = 2 segments of 3 units wide with 4-unit gap
            // Each solid wall = 1 segment of 10 units wide
            // Wall height = 3 units, thickness = 0.2 units
            // Wall center Y = 1.5 (bottom at 0, top at 3)

            float half = 5f;
            float wallHeight = 3f;
            float wallThickness = 0.2f;
            float wallY = wallHeight * 0.5f;
            float doorWidth = 4f;
            float segmentWidth = (10f - doorWidth) * 0.5f; // 3 units each

            // NORTH wall (z = +5)
            if (wallNorth)
            {
                CreateSolidWall(root.transform, "Wall_North", mat,
                    new Vector3(0, wallY, half),
                    new Vector3(10f, wallHeight, wallThickness));
            }
            else
            {
                // Doorway opening on North
                CreateWallSegment(root.transform, "Wall_North_Left", mat,
                    new Vector3(-(half - segmentWidth * 0.5f), wallY, half),
                    new Vector3(segmentWidth, wallHeight, wallThickness));
                CreateWallSegment(root.transform, "Wall_North_Right", mat,
                    new Vector3(half - segmentWidth * 0.5f, wallY, half),
                    new Vector3(segmentWidth, wallHeight, wallThickness));
            }

            // SOUTH wall (z = -5)
            if (wallSouth)
            {
                CreateSolidWall(root.transform, "Wall_South", mat,
                    new Vector3(0, wallY, -half),
                    new Vector3(10f, wallHeight, wallThickness));
            }
            else
            {
                CreateWallSegment(root.transform, "Wall_South_Left", mat,
                    new Vector3(-(half - segmentWidth * 0.5f), wallY, -half),
                    new Vector3(segmentWidth, wallHeight, wallThickness));
                CreateWallSegment(root.transform, "Wall_South_Right", mat,
                    new Vector3(half - segmentWidth * 0.5f, wallY, -half),
                    new Vector3(segmentWidth, wallHeight, wallThickness));
            }

            // EAST wall (x = +5)
            if (wallEast)
            {
                CreateSolidWall(root.transform, "Wall_East", mat,
                    new Vector3(half, wallY, 0),
                    new Vector3(wallThickness, wallHeight, 10f));
            }
            else
            {
                CreateWallSegment(root.transform, "Wall_East_Front", mat,
                    new Vector3(half, wallY, half - segmentWidth * 0.5f),
                    new Vector3(wallThickness, wallHeight, segmentWidth));
                CreateWallSegment(root.transform, "Wall_East_Back", mat,
                    new Vector3(half, wallY, -(half - segmentWidth * 0.5f)),
                    new Vector3(wallThickness, wallHeight, segmentWidth));
            }

            // WEST wall (x = -5)
            if (wallWest)
            {
                CreateSolidWall(root.transform, "Wall_West", mat,
                    new Vector3(-half, wallY, 0),
                    new Vector3(wallThickness, wallHeight, 10f));
            }
            else
            {
                CreateWallSegment(root.transform, "Wall_West_Front", mat,
                    new Vector3(-half, wallY, half - segmentWidth * 0.5f),
                    new Vector3(wallThickness, wallHeight, segmentWidth));
                CreateWallSegment(root.transform, "Wall_West_Back", mat,
                    new Vector3(-half, wallY, -(half - segmentWidth * 0.5f)),
                    new Vector3(wallThickness, wallHeight, segmentWidth));
            }

            // ============================================================
            // Step 4: Create Spawn/Item/Cover Points
            // ============================================================
            CreateEmptyChild(root.transform, "SpawnPoint_0", new Vector3(0, 0, 2));
            CreateEmptyChild(root.transform, "SpawnPoint_1", new Vector3(-3, 0, -2));
            CreateEmptyChild(root.transform, "SpawnPoint_2", new Vector3(3, 0, -2));

            CreateEmptyChild(root.transform, "ItemSlot_0", new Vector3(-3, 0, 0));
            CreateEmptyChild(root.transform, "ItemSlot_1", new Vector3(3, 0, 0));

            CreateEmptyChild(root.transform, "CoverPoint_0", new Vector3(-2, 0, 3));
            CreateEmptyChild(root.transform, "CoverPoint_1", new Vector3(2, 0, 3));
            CreateEmptyChild(root.transform, "CoverPoint_2", new Vector3(0, 0, -3));

            Transform playerStart = CreateEmptyChild(root.transform, "PlayerStart", new Vector3(0, 0, -3));

            // ============================================================
            // Step 5: Add and configure RoomModule
            // ============================================================
            RoomModule module = root.GetComponent<RoomModule>();
            if (module == null)
                module = root.AddComponent<RoomModule>();

            module.shape = shape;
            module.moduleWidth = 10f;

            // Doorway bools (where there IS a doorway = where there is NO wall)
            module.doorNorth = !wallNorth;
            module.doorEast  = !wallEast;
            module.doorSouth = !wallSouth;
            module.doorWest  = !wallWest;

            // Wire up spawn points
            module.spawnPoints = new Transform[3];
            module.spawnPoints[0] = root.transform.Find("SpawnPoint_0");
            module.spawnPoints[1] = root.transform.Find("SpawnPoint_1");
            module.spawnPoints[2] = root.transform.Find("SpawnPoint_2");

            // Wire up item slots
            module.itemSlots = new Transform[2];
            module.itemSlots[0] = root.transform.Find("ItemSlot_0");
            module.itemSlots[1] = root.transform.Find("ItemSlot_1");

            // Wire up cover points
            module.coverPoints = new Transform[3];
            module.coverPoints[0] = root.transform.Find("CoverPoint_0");
            module.coverPoints[1] = root.transform.Find("CoverPoint_1");
            module.coverPoints[2] = root.transform.Find("CoverPoint_2");

            // Wire up player start
            module.playerStartPoint = root.transform.Find("PlayerStart");

            // ============================================================
            // Step 6: Save prefab
            // ============================================================
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);

            Debug.Log($"[PrefabSetup] ✓ {newName} ({shape}): " +
                      $"doors=[{(module.doorNorth ? "N" : "")}{(module.doorEast ? "E" : "")}" +
                      $"{(module.doorSouth ? "S" : "")}{(module.doorWest ? "W" : "")}] " +
                      $"moduleWidth=10");
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private static void CreateSolidWall(Transform parent, string name, Material mat,
            Vector3 position, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent);
            wall.transform.localPosition = position;
            wall.transform.localScale = scale;
            wall.transform.localRotation = Quaternion.identity;
            if (mat != null) wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void CreateWallSegment(Transform parent, string name, Material mat,
            Vector3 position, Vector3 scale)
        {
            CreateSolidWall(parent, name, mat, position, scale);
        }

        private static Transform CreateEmptyChild(Transform parent, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child.transform;
        }
    }
}
#endif
