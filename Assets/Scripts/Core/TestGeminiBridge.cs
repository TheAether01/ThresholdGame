// ============================================================================
// TestGeminiBridge.cs — Quick verification script for GeminiAgentBridge
// THRESHOLD — Google Antigravity Mobile Game Challenge 2026
//
// Usage:
//   1. Create an empty GameObject in the scene
//   2. Attach GeminiAgentBridge and this script to it
//   3. EITHER enter your API key OR tick "Use Mock Responses" (saves API quota)
//   4. Press Play — results appear in the Console
// ============================================================================

using System.Threading.Tasks;
using Threshold.Agents;
using UnityEngine;

namespace Threshold.Core
{
    /// <summary>
    /// One-shot test that fires a Director Agent request on Start,
    /// validates the 5-step trace response, and logs results.
    /// Remove from the scene after verification.
    /// </summary>
    public class TestGeminiBridge : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Which Gemini model to test with.")]
        [SerializeField] private GeminiModel testModel = GeminiModel.Flash;

        [Tooltip("Automatically export traces after test completes.")]
        [SerializeField] private bool exportAfterTest = true;

        private async void Start()
        {
            // Wait one frame for GeminiAgentBridge.Awake() to complete
            await Task.Yield();

            if (GeminiAgentBridge.Instance == null)
            {
                Debug.LogError("[TestGeminiBridge] GeminiAgentBridge not found in scene. " +
                               "Add it to a GameObject first.");
                return;
            }

            if (!GeminiAgentBridge.Instance.IsConfigured)
            {
                Debug.LogError("[TestGeminiBridge] No API key configured. " +
                               "Set it in the GeminiAgentBridge Inspector or " +
                               "GEMINI_API_KEY environment variable.");
                return;
            }

            Debug.Log("╔══════════════════════════════════════════════╗");
            Debug.Log("║   THRESHOLD — GeminiAgentBridge Test         ║");
            Debug.Log("╚══════════════════════════════════════════════╝");

            await RunTest();
        }

        private async Task RunTest()
        {
            // -----------------------------------------------------------------
            // Build a mock Director Agent request with sample game state
            // -----------------------------------------------------------------

            const string systemPrompt =
                "You are the Director Agent for THRESHOLD, a top-down roguelite shooter. " +
                "Your role is to analyse player performance metrics and decide the " +
                "difficulty settings for the next run. " +
                "Respond with a JSON object containing your 5-step reasoning trace.";

            const string mockGameState = @"{
                ""last_run"": {
                    ""deaths"": 2,
                    ""kills"": 18,
                    ""accuracy_percent"": 72.5,
                    ""avg_room_time_seconds"": 22.4,
                    ""rooms_completed"": 6,
                    ""retries"": 1,
                    ""session_length_seconds"": 420,
                    ""win"": false
                },
                ""history"": {
                    ""total_runs"": 5,
                    ""win_streak"": 0,
                    ""loss_streak"": 2
                }
            }";

            var request = new AgentRequest(
                agentName: "director",
                systemPrompt: systemPrompt,
                gameStateJson: mockGameState,
                model: testModel
            );

            Debug.Log($"[TestGeminiBridge] Sending test request to {testModel} model...");

            // -----------------------------------------------------------------
            // Send the request
            // -----------------------------------------------------------------

            AgentResponse response = await GeminiAgentBridge.Instance.SendAgentRequest(request);

            // -----------------------------------------------------------------
            // Evaluate results
            // -----------------------------------------------------------------

            Debug.Log("────────────────────────────────────────────────");

            if (!response.success)
            {
                Debug.LogError($"[TestGeminiBridge] ✗ FAILED — {response.error}");
                Debug.LogError($"  Latency: {response.latencyMs}ms");
                LogVerdict(false);
                return;
            }

            Debug.Log($"[TestGeminiBridge] ✓ Response received in {response.latencyMs}ms");
            Debug.Log("────────────────────────────────────────────────");

            // Validate each trace field individually
            AgentTrace trace = response.trace;
            int passed = 0;
            int total = 5;

            passed += ValidateField("observation", trace.observation);
            passed += ValidateField("inference", trace.inference);
            passed += ValidateField("decision", trace.decision);
            passed += ValidateField("action", trace.action);
            passed += ValidateField("evaluation_plan", trace.evaluation_plan);

            Debug.Log("────────────────────────────────────────────────");
            Debug.Log($"[TestGeminiBridge] Trace fields: {passed}/{total} valid");

            // Log the full trace content
            if (passed == total)
            {
                Debug.Log("[TestGeminiBridge] ── Full Trace ──");
                Debug.Log($"  OBSERVATION:     {Truncate(trace.observation, 120)}");
                Debug.Log($"  INFERENCE:       {Truncate(trace.inference, 120)}");
                Debug.Log($"  DECISION:        {Truncate(trace.decision, 120)}");
                Debug.Log($"  ACTION:          {Truncate(trace.action, 120)}");
                Debug.Log($"  EVALUATION PLAN: {Truncate(trace.evaluation_plan, 120)}");
            }

            // Verify GetLastTrace works
            var lastTrace = GeminiAgentBridge.Instance.GetLastTrace("director");
            bool lookupWorks = lastTrace != null && lastTrace.success;
            Debug.Log($"[TestGeminiBridge] GetLastTrace(\"director\"): " +
                      $"{(lookupWorks ? "✓ works" : "✗ failed")}");

            // Export if configured
            if (exportAfterTest)
            {
                string path = GeminiAgentBridge.Instance.ExportTraces();
                if (path != null)
                {
                    Debug.Log($"[TestGeminiBridge] Traces exported to: {path}");
                }
            }

            // Log usage report (quota monitor)
            Debug.Log("[TestGeminiBridge] ── Usage Report ──");
            Debug.Log(GeminiAgentBridge.Instance.GetUsageReport());

            Debug.Log("────────────────────────────────────────────────");
            LogVerdict(passed == total && lookupWorks);
        }

        /// <summary>
        /// Validates a single trace field is present and non-empty.
        /// Returns 1 if valid, 0 if not.
        /// </summary>
        private int ValidateField(string fieldName, string value)
        {
            bool valid = !string.IsNullOrWhiteSpace(value);
            string icon = valid ? "✓" : "✗";
            string preview = valid ? Truncate(value, 60) : "(missing or empty)";

            if (valid)
                Debug.Log($"  {icon} {fieldName}: {preview}");
            else
                Debug.LogWarning($"  {icon} {fieldName}: {preview}");

            return valid ? 1 : 0;
        }

        private void LogVerdict(bool allPassed)
        {
            if (allPassed)
            {
                Debug.Log("╔══════════════════════════════════════════════╗");
                Debug.Log("║   ✓ ALL TESTS PASSED — Bridge is working!   ║");
                Debug.Log("╚══════════════════════════════════════════════╝");
            }
            else
            {
                Debug.LogError("╔══════════════════════════════════════════════╗");
                Debug.LogError("║   ✗ TEST FAILED — Check errors above        ║");
                Debug.LogError("╚══════════════════════════════════════════════╝");
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
