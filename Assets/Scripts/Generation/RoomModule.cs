// ============================================================================
// RoomModule.cs — MonoBehaviour for room prefabs
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Attach to each room prefab. Defines the physical shape, default doorway
// pattern, and slots for spawn points, items, and cover positions.
// ProceduralRoomGenerator uses CanMatchDoorways() to find which prefab
// fits a required doorway pattern at any of the 4 cardinal rotations.
// ============================================================================

using System.Collections.Generic;
using Threshold.Core;
using UnityEngine;

namespace Threshold.Generation
{
    /// <summary>
    /// Component on room prefabs that describes their physical layout.
    /// Each prefab defines its default doorway orientation; the generator
    /// rotates the prefab to match the required doorway pattern.
    /// </summary>
    public class RoomModule : MonoBehaviour
    {
        // ====================================================================
        // Shape & Doorways (default orientation, 0° rotation)
        // ====================================================================

        [Header("Room Identity")]
        [Tooltip("Physical shape classification of this room prefab.")]
        public RoomShape shape;

        [Header("Default Doorways (0° rotation)")]
        [Tooltip("Has a doorway on the NORTH side in default orientation.")]
        public bool doorNorth;

        [Tooltip("Has a doorway on the EAST side in default orientation.")]
        public bool doorEast;

        [Tooltip("Has a doorway on the SOUTH side in default orientation.")]
        public bool doorSouth;

        [Tooltip("Has a doorway on the WEST side in default orientation.")]
        public bool doorWest;

        // ====================================================================
        // Room Dimensions
        // ====================================================================

        [Header("Dimensions")]
        [Tooltip("Width/depth of the room module in world units. Rooms are square.")]
        public float moduleWidth = 10f; // M1 FIX: matches 10x10 prefab standard

        // ====================================================================
        // Spatial Slots (assign in prefab)
        // ====================================================================

        [Header("Spawn Points")]
        [Tooltip("Transforms where enemies can spawn inside this room.")]
        public Transform[] spawnPoints;

        [Header("Item Slots")]
        [Tooltip("Transforms where items can be placed.")]
        public Transform[] itemSlots;

        [Header("Cover Points")]
        [Tooltip("Transforms marking cover positions for NPC AI.")]
        public Transform[] coverPoints;

        [Header("Player Start")]
        [Tooltip("Where the player spawns if this room is the ENTRY. Optional.")]
        public Transform playerStartPoint;

        // ====================================================================
        // Doorway Matching
        // ====================================================================

        /// <summary>
        /// Returns the doorway pattern (N, E, S, W booleans) at a given rotation.
        /// Rotation steps: 0 = 0°, 1 = 90° CW, 2 = 180°, 3 = 270° CW.
        /// </summary>
        public void GetDoorwaysAtRotation(int rotationSteps, out bool n, out bool e, out bool s, out bool w)
        {
            // Normalize to 0–3
            rotationSteps = ((rotationSteps % 4) + 4) % 4;

            // Default pattern as array [N, E, S, W]
            bool[] defaults = { doorNorth, doorEast, doorSouth, doorWest };

            // Rotate clockwise: each CW step shifts indices backward
            // e.g. 1 step CW: what was West becomes North, North→East, etc.
            n = defaults[((0 - rotationSteps) % 4 + 4) % 4];
            e = defaults[((1 - rotationSteps) % 4 + 4) % 4];
            s = defaults[((2 - rotationSteps) % 4 + 4) % 4];
            w = defaults[((3 - rotationSteps) % 4 + 4) % 4];
        }

        /// <summary>
        /// Checks if this prefab can match a required doorway pattern at any
        /// of the 4 cardinal rotations (0°, 90°, 180°, 270°).
        /// Returns the rotation steps (0–3) if a match is found, or -1 if none.
        /// </summary>
        /// <param name="needN">Require a NORTH doorway.</param>
        /// <param name="needE">Require an EAST doorway.</param>
        /// <param name="needS">Require a SOUTH doorway.</param>
        /// <param name="needW">Require a WEST doorway.</param>
        public int FindMatchingRotation(bool needN, bool needE, bool needS, bool needW)
        {
            for (int rot = 0; rot < 4; rot++)
            {
                GetDoorwaysAtRotation(rot, out bool n, out bool e, out bool s, out bool w);

                // Match: required doorways must be present.
                // Extra doorways are allowed (they'll be walled off).
                if ((!needN || n) && (!needE || e) && (!needS || s) && (!needW || w))
                {
                    return rot;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds the rotation that exactly matches the required pattern
        /// (no extra doorways). Returns rotation steps (0–3) or -1.
        /// </summary>
        public int FindExactMatchingRotation(bool needN, bool needE, bool needS, bool needW)
        {
            for (int rot = 0; rot < 4; rot++)
            {
                GetDoorwaysAtRotation(rot, out bool n, out bool e, out bool s, out bool w);

                if (n == needN && e == needE && s == needS && w == needW)
                {
                    return rot;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the number of doorways this prefab has.
        /// </summary>
        public int DoorwayCount()
        {
            int count = 0;
            if (doorNorth) count++;
            if (doorEast) count++;
            if (doorSouth) count++;
            if (doorWest) count++;
            return count;
        }

        /// <summary>
        /// Returns the center world position of this room module.
        /// </summary>
        public Vector3 GetCenter()
        {
            return transform.position;
        }

        /// <summary>
        /// Returns the world position of a doorway in a given direction,
        /// accounting for the room's current position and module width.
        /// </summary>
        public Vector3 GetDoorwayWorldPosition(Direction dir)
        {
            Vector3 center = transform.position;
            float half = moduleWidth * 0.5f;

            return dir switch
            {
                Direction.NORTH => center + new Vector3(0, 0, half),
                Direction.EAST  => center + new Vector3(half, 0, 0),
                Direction.SOUTH => center + new Vector3(0, 0, -half),
                Direction.WEST  => center + new Vector3(-half, 0, 0),
                _ => center
            };
        }

        // ====================================================================
        // Editor Gizmos
        // ====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            float half = moduleWidth * 0.5f;
            Vector3 center = transform.position;

            // Draw room bounds
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireCube(center, new Vector3(moduleWidth, 2f, moduleWidth));

            // Draw doorways
            Gizmos.color = Color.green;
            float doorSize = 2f;
            if (doorNorth) Gizmos.DrawCube(center + new Vector3(0, 1, half), new Vector3(doorSize, 2f, 0.5f));
            if (doorSouth) Gizmos.DrawCube(center + new Vector3(0, 1, -half), new Vector3(doorSize, 2f, 0.5f));
            if (doorEast)  Gizmos.DrawCube(center + new Vector3(half, 1, 0), new Vector3(0.5f, 2f, doorSize));
            if (doorWest)  Gizmos.DrawCube(center + new Vector3(-half, 1, 0), new Vector3(0.5f, 2f, doorSize));

            // Draw spawn points
            Gizmos.color = Color.red;
            if (spawnPoints != null)
            {
                foreach (var sp in spawnPoints)
                {
                    if (sp != null) Gizmos.DrawSphere(sp.position, 0.4f);
                }
            }

            // Draw item slots
            Gizmos.color = Color.yellow;
            if (itemSlots != null)
            {
                foreach (var slot in itemSlots)
                {
                    if (slot != null) Gizmos.DrawCube(slot.position, new Vector3(0.5f, 0.5f, 0.5f));
                }
            }

            // Draw cover points
            Gizmos.color = new Color(0.5f, 0f, 1f, 0.8f);
            if (coverPoints != null)
            {
                foreach (var cp in coverPoints)
                {
                    if (cp != null) Gizmos.DrawCube(cp.position, new Vector3(0.6f, 1f, 0.6f));
                }
            }
        }
#endif
    }
}
