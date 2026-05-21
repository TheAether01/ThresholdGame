# THRESHOLD — Cost & Scalability Analysis

> **Google Antigravity Mobile Game Challenge 2026**  
> Cost per operation, API call estimates, and 10x/100x scaling discussion.

---

## 1. Cost Per Operation

### API Calls Per Run (Hybrid Mode — Default)

| Agent | Model Tier | Calls/Run | Avg Tokens (In) | Avg Tokens (Out) | Provider |
|---|---|---|---|---|---|
| Director (A1) | Flash | 1 | ~300 | ~200 | Groq / Gemini |
| Level Populate (A2) | Flash | 1 | ~400 | ~150 | Groq / Gemini |
| NPC Brain (A4) | Flash | 6-10 | ~200 | ~100 | Groq / Gemini |
| Reward (A5) | Flash | 1 | ~400 | ~200 | Groq / Gemini |
| **Total** | | **9-13** | **~3,400** | **~1,550** | |

> **Note:** QC Agent (A3) is not used in Hybrid mode — spatial validation is handled locally.

### API Calls Per Run (AI_Full Mode)

| Agent | Model Tier | Calls/Run | Avg Tokens (In) | Avg Tokens (Out) | Provider |
|---|---|---|---|---|---|
| Director (A1) | Flash | 1 | ~300 | ~200 | Groq / Gemini |
| Level Gen (A2) | **Pro** | 1-3 | ~800 | ~400 | NVIDIA NIM |
| QC (A3) | Flash | 1-3 | ~500 | ~100 | Groq / Gemini |
| NPC Brain (A4) | Flash | 6-10 | ~200 | ~100 | Groq / Gemini |
| Reward (A5) | Flash | 1 | ~400 | ~200 | Groq / Gemini |
| **Total** | | **10-18** | **~5,000** | **~2,200** | |

---

## 2. Cost at Free Tier (Current)

All three supported providers offer free tiers sufficient for development and demonstration:

| Provider | Free Tier Limits | Cost | Runs/Day Possible |
|---|---|---|---|
| **Groq** | 30 RPM, 14,400 RPD | **$0.00** | ~1,100 runs (Hybrid) |
| **Gemini** | Flash: 15 RPM / 1,500 RPD; Pro: 2 RPM / 50 RPD | **$0.00** | ~115 runs (Hybrid) |
| **NVIDIA NIM** | 40 RPM, ~1,000 credits/day | **$0.00** | ~77 runs (Hybrid) |

**Current production cost: $0.00 per run.**

---

## 3. Cost at Paid Tier (Projected)

If the game were to operate on paid API tiers:

### Gemini Pricing (as of May 2026)
| Model | Input Cost | Output Cost |
|---|---|---|
| Gemini 2.0 Flash | $0.075 / 1M tokens | $0.30 / 1M tokens |
| Gemini 2.0 Pro | $1.25 / 1M tokens | $5.00 / 1M tokens |

### Cost Per Run (Hybrid Mode, Gemini Paid)
```
Input cost:  3,400 tokens × $0.075/1M = $0.000255
Output cost: 1,550 tokens × $0.30/1M  = $0.000465
─────────────────────────────────────────────────
Total per run:                           $0.00072 (~$0.001)
```

### Cost Per Run (AI_Full Mode, Gemini Paid)
```
Flash calls: ~$0.001 (same as Hybrid)
Pro call:    800 tokens × $1.25/1M + 400 tokens × $5.00/1M = $0.003
─────────────────────────────────────────────────
Total per run:                                                $0.004
```

---

## 4. Latency Analysis

### Per-Agent Latency

| Agent | Groq (Flash) | Gemini (Flash) | NVIDIA (Pro) |
|---|---|---|---|
| Director | 0.8-1.5s | 1.0-2.0s | N/A |
| Level Populate | 1.0-2.0s | 1.5-3.0s | N/A |
| Level Gen (AI_Full) | N/A | N/A | 40-90s (cold), 15-30s (warm) |
| QC Validator | 0.8-1.2s | 1.0-2.0s | N/A |
| NPC Brain | 0.5-1.0s | 0.8-1.5s | N/A |
| Reward | 1.0-2.0s | 1.5-3.0s | N/A |

### End-to-End Pipeline Latency

| Pipeline Mode | Best Case | Typical | Worst Case |
|---|---|---|---|
| **Hybrid** (recommended) | 4s | 7s | 15s |
| **AI_Full** | 20s | 60s | 360s (3 timeouts + fallback) |
| **Local fallback** | 0.05s | 0.1s | 0.2s |

### NPC Brain Throughput
- **Per NPC:** 0.5-1.0s per decision
- **8 NPCs, sequential:** 4-8s for full room evaluation
- **Throttling:** Brain calls are spaced across frames to avoid UI lag
- **Fallback:** If brain call fails or times out (3s), NPCStateMachine uses local FSM logic

---

## 5. Scaling Analysis

### Current Scale (1 Player)
- **API calls per session:** ~30-50 (3-5 runs × 9-13 calls/run)
- **Total tokens per session:** ~15,000-25,000
- **Session duration:** ~15-30 minutes
- **Cost:** $0.00 (free tier)

### 10x Scale (10 Concurrent Players)

| Resource | Demand | Free Tier Capacity | Status |
|---|---|---|---|
| Groq RPM | ~2-3 RPM per player = 20-30 RPM | 30 RPM | ⚠️ At limit |
| Groq RPD | ~50 calls/session × 10 = 500/day | 14,400 RPD | ✅ OK |
| Gemini Flash RPM | ~2-3 RPM per player = 20-30 RPM | 15 RPM | ❌ Exceeded |
| Gemini Flash RPD | ~50 × 10 = 500/day | 1,500 RPD | ✅ OK |
| Bandwidth | ~25KB/call × 500/day = 12.5 MB | Unlimited | ✅ OK |

**Solution for 10x:**
- Use Groq as primary provider (30 RPM handles 10 staggered players)
- Stagger run starts by 10-15 seconds to distribute RPM
- Each client uses its own API key (horizontal scaling)
- **Cost:** Still $0.00 if each user provides their own free API key

### 100x Scale (100 Concurrent Players)

| Resource | Demand | Solution |
|---|---|---|
| API calls | ~300 RPM peak | Paid tier (Gemini Flash: 2000 RPM) |
| Tokens | ~2.5M tokens/hour | Paid tier ($0.19/hour) |
| Infrastructure | No server needed | Client-side API calls |
| API keys | Per-user keys or shared paid key | Shared key with user auth |

**Cost at 100x (Gemini Paid):**
```
100 players × 5 runs/hour × $0.001/run = $0.50/hour
Monthly (8h/day × 30 days):              $120/month
```

**Architecture changes needed:**
- None for core pipeline — each client is independent
- Add optional server-side API key proxy to protect keys in production
- Implement user-level rate limiting to prevent abuse

### 1000x Scale (1,000 Concurrent Players)

| Optimization | Reduction | Description |
|---|---|---|
| Client-side NPC Brain | -80% API calls | Run small language model (TFLite/ONNX) on-device for NPC decisions |
| Cached Director profiles | -50% Director calls | Re-use difficulty profiles for similar player archetypes |
| Batch brain calls | -60% brain latency | Send all NPCs in one API call instead of sequential |
| Edge caching | -30% latency | CDN-cached responses for common game states |

**Cost at 1000x (optimized):**
```
1000 players × 5 runs/hour × $0.0003/run = $0.15/hour  (after -80% brain reduction)
Monthly: $36/month
```

**Architecture changes needed:**
- On-device inference for NPC Brain (biggest win)
- Server-side API proxy with caching layer
- Player segmentation for Director profile caching
- Monitoring dashboard for API usage per player

---

## 6. Rate Limit Management

### Built-in Safeguards

```csharp
// GeminiAgentBridge.cs — Per-provider rate limiting
[SerializeField] private int flashRpmLimit = 15;     // Gemini Flash
[SerializeField] private int flashRpdLimit = 1500;    // Gemini Flash daily
[SerializeField] private int groqRpmLimit = 30;       // Groq
[SerializeField] private int groqRpdLimit = 14400;    // Groq daily
[SerializeField] private int nvidiaRpmLimit = 40;     // NVIDIA NIM
[SerializeField] private int nvidiaRpdLimit = 1000;   // NVIDIA daily
```

- **Pre-flight check:** Every API call passes through `CheckRateLimit()` before sending
- **Rejection handling:** Rate-limited calls return `AgentResponse.Failure` immediately (no API call wasted)
- **429 auto-retry:** If the provider returns 429, the system parses the retry delay and retries once
- **Daily reset:** Daily counters reset automatically at midnight
- **Usage report:** `GetUsageReport()` provides real-time quota visibility

### Graceful Degradation Chain
```
API call → Rate limit exceeded? → Use mock response? → Use local fallback
                  ↓                      ↓                      ↓
            Log rejection          Return fake trace      Procedural/FSM logic
```

**The game is never blocked by an API limit.** Every agent call has a local fallback that produces a playable (if less adaptive) result.

---

## 7. Summary

| Metric | Value |
|---|---|
| **Cost per run (free tier)** | $0.00 |
| **Cost per run (paid tier)** | ~$0.001 (Hybrid), ~$0.004 (AI_Full) |
| **API calls per run** | 9-13 (Hybrid), 10-18 (AI_Full) |
| **Total tokens per run** | ~5,000 |
| **Pipeline latency** | ~7s (Hybrid), ~60s (AI_Full) |
| **NPC Brain latency** | ~0.8s per decision |
| **10x scale** | Free tier sufficient (Groq, staggered) |
| **100x scale** | ~$120/month (Gemini paid) |
| **1000x scale** | ~$36/month (with on-device NPC inference) |
| **Bottleneck** | RPM limits (solvable with paid tier or on-device inference) |
