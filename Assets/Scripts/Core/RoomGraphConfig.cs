// ============================================================================
// RoomGraphConfig.cs — Data contract between Gemini agents and Unity
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// This file defines the complete room graph JSON schema. Both the Level Gen
// Agent (Gemini) and the local fallback generator produce this exact structure.
// FloorGenerator.cs consumes it to instantiate the physical dungeon.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threshold.Core
{
    // ========================================================================
    // Enums
    // ========================================================================

    /// <summary>
    /// Physical room prefab shape, defined by doorway count and arrangement.
    /// </summary>
    public enum RoomShape
    {
        CROSSROADS,   // 4 doorways: N, E, S, W
        T_JUNCTION,   // 3 doorways
        STRAIGHT,     // 2 doorways on opposite sides
        CORNER,       // 2 doorways on adjacent sides
        DEAD_END      // 1 doorway
    }

    /// <summary>
    /// Gameplay role assigned to a room, independent of its physical shape.
    /// The Level Gen Agent decides role assignment based on the Director's
    /// difficulty profile and the player's recent performance.
    /// </summary>
    public enum RoomRole
    {
        ENTRY,    // Player spawn point. Safe, no enemies.
        EXIT,     // Run endpoint, reached after boss.
        PACING,   // Breathing room. Low threat, resource recovery.
        COMBAT,   // Standard enemy encounter.
        AMBUSH,   // Surprise spawns triggered by player movement.
        BOSS,     // Elite encounter. Hardest room in the run.
        LOOT,     // Optional branch with rewards or traps.
        CHOKE     // Narrow tactical room favouring suppression combat.
    }

    /// <summary>
    /// Cardinal direction for doorway placement and room connectivity.
    /// </summary>
    public enum Direction
    {
        NORTH = 0,
        EAST  = 1,
        SOUTH = 2,
        WEST  = 3
    }

    /// <summary>
    /// NPC archetype for spawn zone configuration.
    /// </summary>
    public enum NPCArchetype
    {
        GRUNT,
        FLANKER,
        SUPPRESSOR,
        ELITE
    }

    /// <summary>
    /// Item types that can be placed in rooms.
    /// </summary>
    public enum ItemType
    {
        HEALTH_KIT,     // Full heal (medkit)
        AMMO_CACHE,     // Ammo box
        BANDAGE         // Partial heal
    }

    /// <summary>
    /// Event trigger types for room events.
    /// </summary>
    public enum EventTriggerType
    {
        ON_ENTER,         // Fires when player enters the room
        ON_CLEAR,         // Fires when all enemies are eliminated
        ON_POSITION,      // Fires when player reaches a specific position
        ON_TIMER,         // Fires after a delay
        ON_HEALTH_THRESHOLD // Fires when player health drops below threshold
    }

    // ========================================================================
    // Root Config
    // ========================================================================

    /// <summary>
    /// Complete room graph configuration — the primary data contract between
    /// AI agents and Unity's FloorGenerator.
    /// </summary>
    [Serializable]
    public class RoomGraphConfig
    {
        /// <summary>Difficulty settings from the Director Agent.</summary>
        public DifficultyProfile difficulty;

        /// <summary>All rooms in the floor.</summary>
        public List<RoomConfig> rooms;

        /// <summary>Connections between rooms (bidirectional edges).</summary>
        public List<EdgeConfig> edges;

        /// <summary>Generation metadata for logging and novelty tracking.</summary>
        public LayoutMetadata metadata;

        /// <summary>
        /// Finds a room by its unique ID.
        /// </summary>
        public RoomConfig GetRoom(string roomId)
        {
            return rooms?.Find(r => r.roomId == roomId);
        }

        /// <summary>
        /// Returns the ENTRY room (there should be exactly one).
        /// </summary>
        public RoomConfig GetEntryRoom()
        {
            return rooms?.Find(r => r.role == RoomRole.ENTRY);
        }

        /// <summary>
        /// Returns the EXIT room (there should be exactly one).
        /// </summary>
        public RoomConfig GetExitRoom()
        {
            return rooms?.Find(r => r.role == RoomRole.EXIT);
        }

        /// <summary>
        /// Returns all rooms connected to a given room ID.
        /// </summary>
        public List<string> GetConnectedRoomIds(string roomId)
        {
            var result = new List<string>();
            if (edges == null) return result;

            foreach (var edge in edges)
            {
                if (edge.roomIdA == roomId) result.Add(edge.roomIdB);
                else if (edge.roomIdB == roomId) result.Add(edge.roomIdA);
            }
            return result;
        }
    }

    // ========================================================================
    // Difficulty Profile
    // ========================================================================

    /// <summary>
    /// Difficulty settings produced by the Director Agent.
    /// Consumed by both Level Gen Agent and the local fallback generator.
    /// </summary>
    [Serializable]
    public class DifficultyProfile
    {
        /// <summary>Global difficulty multiplier (0.5x to 2.5x).</summary>
        [Range(0.5f, 2.5f)]
        public float difficultyMultiplier = 1.0f;

        /// <summary>Target number of rooms for this run (5–12).</summary>
        [Range(5, 12)]
        public int targetRoomCount = 7;

        /// <summary>Base number of enemies per combat room.</summary>
        public int baseEnemiesPerRoom = 3;

        /// <summary>Number of ELITE NPCs to include in the entire floor.</summary>
        public int eliteCount = 0;

        /// <summary>Probability of random events triggering (0–1).</summary>
        [Range(0f, 1f)]
        public float eventProbability = 0.3f;

        /// <summary>Preferred NPC tactic bias from the Director.</summary>
        public string preferredTactic = "ATTACK";
    }

    // ========================================================================
    // Room Config
    // ========================================================================

    /// <summary>
    /// Configuration for a single room in the floor graph.
    /// </summary>
    [Serializable]
    public class RoomConfig
    {
        /// <summary>Unique identifier (e.g. "room_0", "room_1").</summary>
        public string roomId;

        /// <summary>Grid column position.</summary>
        public int gridCol;

        /// <summary>Grid row position.</summary>
        public int gridRow;

        /// <summary>Physical prefab shape.</summary>
        public RoomShape shape;

        /// <summary>Gameplay role (independent of shape).</summary>
        public RoomRole role;

        /// <summary>Rotation in degrees (0, 90, 180, 270) to align doorways.</summary>
        public int rotationDegrees;

        /// <summary>Which directions have open doorways (after rotation).</summary>
        public List<DoorwayConfig> doorways;

        /// <summary>Enemy spawn zones inside this room.</summary>
        public List<SpawnZoneConfig> spawnZones;

        /// <summary>Items placed in this room.</summary>
        public List<ItemConfig> items;

        /// <summary>Events that can trigger in this room.</summary>
        public List<EventConfig> events;

        /// <summary>
        /// Returns true if this room has a doorway in the given direction.
        /// </summary>
        public bool HasDoorway(Direction dir)
        {
            if (doorways == null) return false;
            return doorways.Exists(d => d.direction == dir && d.isOpen);
        }

        /// <summary>
        /// Returns the doorway config for a given direction, or null.
        /// </summary>
        public DoorwayConfig GetDoorway(Direction dir)
        {
            return doorways?.Find(d => d.direction == dir);
        }

        /// <summary>
        /// Total enemy count across all spawn zones.
        /// </summary>
        public int TotalEnemyCount()
        {
            if (spawnZones == null) return 0;
            int total = 0;
            foreach (var sz in spawnZones) total += sz.count;
            return total;
        }
    }

    // ========================================================================
    // Sub-configs
    // ========================================================================

    /// <summary>
    /// A doorway on one side of a room. Tracks direction, whether it's open,
    /// and which room it connects to.
    /// </summary>
    [Serializable]
    public class DoorwayConfig
    {
        /// <summary>Cardinal direction of this doorway.</summary>
        public Direction direction;

        /// <summary>Whether this doorway is open (has a passage).</summary>
        public bool isOpen;

        /// <summary>ID of the room this doorway connects to. Empty if none.</summary>
        public string connectedRoomId;
    }

    /// <summary>
    /// An enemy spawn zone within a room.
    /// </summary>
    [Serializable]
    public class SpawnZoneConfig
    {
        /// <summary>Local position offset within the room.</summary>
        public Vector3 localPosition;

        /// <summary>Which NPC archetype to spawn here.</summary>
        public NPCArchetype archetype;

        /// <summary>Number of NPCs to spawn at this zone.</summary>
        public int count;

        /// <summary>Spawn delay in seconds after room activation.</summary>
        public float spawnDelay;
    }

    /// <summary>
    /// An item placement within a room.
    /// </summary>
    [Serializable]
    public class ItemConfig
    {
        /// <summary>Type of item to place.</summary>
        public ItemType itemType;

        /// <summary>Local position offset within the room.</summary>
        public Vector3 localPosition;
    }

    /// <summary>
    /// A scripted event that can trigger during gameplay within a room.
    /// </summary>
    [Serializable]
    public class EventConfig
    {
        /// <summary>What triggers this event.</summary>
        public EventTriggerType triggerType;

        /// <summary>Description of the event effect.</summary>
        public string description;

        /// <summary>Parameter value (e.g. timer seconds, health threshold).</summary>
        public float parameter;
    }

    /// <summary>
    /// A connection between two rooms in the graph.
    /// </summary>
    [Serializable]
    public class EdgeConfig
    {
        /// <summary>First room ID.</summary>
        public string roomIdA;

        /// <summary>Second room ID.</summary>
        public string roomIdB;

        /// <summary>Direction from room A to room B.</summary>
        public Direction directionFromA;

        /// <summary>
        /// Returns the opposite direction (from B to A).
        /// </summary>
        public Direction DirectionFromB()
        {
            return (Direction)(((int)directionFromA + 2) % 4);
        }
    }

    /// <summary>
    /// Metadata about how the layout was generated.
    /// Used for logging, debugging, and novelty comparison.
    /// </summary>
    [Serializable]
    public class LayoutMetadata
    {
        /// <summary>Random seed used for generation.</summary>
        public int seed;

        /// <summary>How the layout was produced.</summary>
        public string generationMethod; // "gemini_level_gen", "local_fallback", "safe_template"

        /// <summary>Novelty score vs. recent layouts (0–1, higher = more novel).</summary>
        [Range(0f, 1f)]
        public float noveltyScore;

        /// <summary>ISO timestamp of generation.</summary>
        public string timestamp;

        /// <summary>Number of QC rejection attempts before acceptance.</summary>
        public int qcAttempts;

        /// <summary>Grid dimensions used.</summary>
        public int gridWidth;
        public int gridHeight;
    }
}
