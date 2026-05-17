// ============================================================================
// LayoutHistoryManager.cs — Persistent layout history + novelty scoring
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Threshold.Core;
using UnityEngine;

namespace Threshold.Generation
{
    /// <summary>
    /// Saves the last N run layouts to persistent storage and computes
    /// novelty scores for candidate layouts vs. recent history.
    /// Used by the Level Gen Agent to avoid repetitive floor designs.
    /// </summary>
    public class LayoutHistoryManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Max layouts to keep in history.")]
        [SerializeField] private int maxHistory = 3;

        [Header("Weights for Novelty Scoring")]
        [SerializeField] private float shapeSequenceWeight = 0.4f;
        [SerializeField] private float roleSequenceWeight = 0.3f;
        [SerializeField] private float gridFootprintWeight = 0.3f;

        private List<LayoutSnapshot> _history = new();
        private string _savePath;

        private void Awake()
        {
            _savePath = Path.Combine(Application.persistentDataPath, "layout_history.json");
            LoadHistory();
        }

        // ====================================================================
        // Public API
        // ====================================================================

        /// <summary>
        /// Records a layout after a run. Trims history to maxHistory.
        /// </summary>
        public void RecordLayout(RoomGraphConfig config)
        {
            if (config?.rooms == null || config.rooms.Count == 0) return;

            var snapshot = new LayoutSnapshot
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                seed = config.metadata?.seed ?? 0,
                roomCount = config.rooms.Count,
                shapeSequence = config.rooms.Select(r => r.shape.ToString()).ToList(),
                roleSequence = config.rooms.Select(r => r.role.ToString()).ToList(),
                gridPositions = config.rooms
                    .Select(r => new int[] { r.gridCol, r.gridRow })
                    .ToList()
            };

            _history.Add(snapshot);
            while (_history.Count > maxHistory)
                _history.RemoveAt(0);

            SaveHistory();
        }

        /// <summary>
        /// Computes a novelty score (0–1) for a candidate layout compared
        /// to recent history. Higher = more novel.
        /// Returns 1.0 if history is empty.
        /// </summary>
        public float CalculateNoveltyScore(RoomGraphConfig candidate)
        {
            if (_history.Count == 0 || candidate?.rooms == null) return 1f;

            var candidateShapes = candidate.rooms.Select(r => r.shape.ToString()).ToList();
            var candidateRoles = candidate.rooms.Select(r => r.role.ToString()).ToList();
            var candidateGrid = candidate.rooms
                .Select(r => new Vector2Int(r.gridCol, r.gridRow))
                .ToHashSet();

            float totalNovelty = 0f;

            foreach (var past in _history)
            {
                float shapeSim = SequenceSimilarity(candidateShapes, past.shapeSequence);
                float roleSim = SequenceSimilarity(candidateRoles, past.roleSequence);
                float gridSim = GridSimilarity(candidateGrid, past.gridPositions);

                float combined = shapeSim * shapeSequenceWeight +
                                 roleSim * roleSequenceWeight +
                                 gridSim * gridFootprintWeight;

                // Novelty = 1 - similarity
                totalNovelty += (1f - combined);
            }

            return Mathf.Clamp01(totalNovelty / _history.Count);
        }

        /// <summary>Number of layouts in history.</summary>
        public int HistoryCount => _history.Count;

        /// <summary>Clears all history (for testing).</summary>
        public void ClearHistory()
        {
            _history.Clear();
            SaveHistory();
        }

        // ====================================================================
        // Similarity Metrics
        // ====================================================================

        /// <summary>
        /// Compares two string sequences using longest common subsequence ratio.
        /// Returns 0–1 where 1 = identical sequence.
        /// </summary>
        private float SequenceSimilarity(List<string> a, List<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0f;

            int lcsLength = LongestCommonSubsequenceLength(a, b);
            return (float)lcsLength / Mathf.Max(a.Count, b.Count);
        }

        private int LongestCommonSubsequenceLength(List<string> a, List<string> b)
        {
            int m = a.Count, n = b.Count;
            // Use two rows to save memory
            int[] prev = new int[n + 1];
            int[] curr = new int[n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (a[i - 1] == b[j - 1])
                        curr[j] = prev[j - 1] + 1;
                    else
                        curr[j] = Mathf.Max(prev[j], curr[j - 1]);
                }
                // Swap rows
                (prev, curr) = (curr, prev);
                Array.Clear(curr, 0, curr.Length);
            }

            return prev.Max();
        }

        /// <summary>
        /// Compares grid footprints using Jaccard similarity on occupied cells.
        /// Returns 0–1 where 1 = identical footprint.
        /// </summary>
        private float GridSimilarity(HashSet<Vector2Int> candidateGrid, List<int[]> pastPositions)
        {
            if (candidateGrid.Count == 0 || pastPositions == null || pastPositions.Count == 0)
                return 0f;

            var pastGrid = new HashSet<Vector2Int>();
            foreach (var pos in pastPositions)
            {
                if (pos.Length >= 2)
                    pastGrid.Add(new Vector2Int(pos[0], pos[1]));
            }

            int intersection = 0;
            foreach (var cell in candidateGrid)
            {
                if (pastGrid.Contains(cell)) intersection++;
            }

            int union = candidateGrid.Count + pastGrid.Count - intersection;
            return union > 0 ? (float)intersection / union : 0f;
        }

        // ====================================================================
        // Persistence
        // ====================================================================

        private void SaveHistory()
        {
            try
            {
                var wrapper = new HistoryWrapper { layouts = _history };
                string json = JsonUtility.ToJson(wrapper, true);
                File.WriteAllText(_savePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LayoutHistory] Failed to save: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_savePath))
                {
                    string json = File.ReadAllText(_savePath);
                    var wrapper = JsonUtility.FromJson<HistoryWrapper>(json);
                    _history = wrapper?.layouts ?? new List<LayoutSnapshot>();
                    Debug.Log($"[LayoutHistory] Loaded {_history.Count} past layouts.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LayoutHistory] Failed to load: {ex.Message}");
                _history = new List<LayoutSnapshot>();
            }
        }

        // ====================================================================
        // Serializable Data
        // ====================================================================

        [Serializable]
        private class HistoryWrapper
        {
            public List<LayoutSnapshot> layouts = new();
        }

        [Serializable]
        private class LayoutSnapshot
        {
            public string timestamp;
            public int seed;
            public int roomCount;
            public List<string> shapeSequence;
            public List<string> roleSequence;
            public List<int[]> gridPositions;
        }
    }
}
