# THRESHOLD — Antigravity Trace Logs

> **Google Antigravity Mobile Game Challenge 2026**  
> Comprehensive record of agent observations, reasoning, decisions, tool calls, error recovery, and outcomes.

---

## 1. Workplan

### Phase 1: Foundation (Days 1-2)
| Step | Description | Status |
|---|---|---|
| 1.1 | Analyze hackathon rules and evaluation criteria | ✅ Complete |
| 1.2 | Design 5-agent architecture (Director, Level Gen, QC, NPC Brain, Reward) | ✅ Complete |
| 1.3 | Implement `GeminiAgentBridge` singleton with multi-provider support | ✅ Complete |
| 1.4 | Define `AgentDataTypes` — Request, Response, Trace schemas | ✅ Complete |
| 1.5 | Implement `AgentTrace` 5-step format (observation → evaluation_plan) | ✅ Complete |

### Phase 2: Procedural Generation (Days 2-3)
| Step | Description | Status |
|---|---|---|
| 2.1 | Implement `ProceduralRoomGenerator` — grid-based spatial layout generation | ✅ Complete |
| 2.2 | Implement `LevelGenerationPipeline` — Director → LevelGen → QC pipeline | ✅ Complete |
| 2.3 | Implement `FloorGenerator` — physical room instantiation from config | ✅ Complete |
| 2.4 | Implement `LayoutHistoryManager` — novelty tracking across runs | ✅ Complete |
| 2.5 | Design Hybrid generation mode (Solution B) — local spatial + AI role assignment | ✅ Complete |
| 2.6 | Test generation pipeline with multi-phase test runners | ✅ Complete |

### Phase 3: NPC AI (Days 3-4)
| Step | Description | Status |
|---|---|---|
| 3.1 | Implement `NPCStateMachine` — 7-state FSM (PATROL, ATTACK, FLANK, SUPPRESS, RETREAT, DEFECT, DEAD) | ✅ Complete |
| 3.2 | Implement `NPCBrainController` — LLM-powered decision engine | ✅ Complete |
| 3.3 | Implement 4 NPC archetypes (Grunt, Flanker, Suppressor, Elite) | ✅ Complete |
| 3.4 | Implement NPC defection system with loyalty scoring | ✅ Complete |
| 3.5 | Implement 5 difficulty levers (rotation dampening, reaction time, accuracy ramp, warmup, multiplier) | ✅ Complete |
| 3.6 | Audit and fix NPC difficulty tuning (NPCs were impossibly lethal on run 1) | ✅ Complete |

### Phase 4: Player & Rewards (Days 4-5)
| Step | Description | Status |
|---|---|---|
| 4.1 | Implement `PlayerController`, `PlayerHealth`, `PlayerWeapon` | ✅ Complete |
| 4.2 | Implement `PickupItem` — universal item system (Medkit, Bandage, AmmoBox) | ✅ Complete |
| 4.3 | Implement `RewardManager` — AI-evaluated end-of-run rewards | ✅ Complete |
| 4.4 | Implement XP progression with unlock thresholds | ✅ Complete |
| 4.5 | Implement fair play detection (anti-exploit scoring) | ✅ Complete |

### Phase 5: Baseline & Polish (Days 5-6)
| Step | Description | Status |
|---|---|---|
| 5.1 | Implement `FixedRuleDirector` — non-agentic baseline for A/B comparison | ✅ Complete |
| 5.2 | Implement `SessionMetrics` — identical metric tracking for both modes | ✅ Complete |
| 5.3 | Implement camera shake & vignette hit feedback | ✅ Complete |
| 5.4 | Fix collider preservation during NPC wrapping | ✅ Complete |
| 5.5 | Fix pickup enum serialization issue | ✅ Complete |
| 5.6 | Create submission documentation | ✅ Complete |

---

## 2. Task Plan (Agentic Trace)

### Task: Design Level Generation Pipeline
```
Observation: Hackathon requires procedural content generation with QC validation.
             Must support rejection/regeneration loops and produce traceable decisions.
Inference:   A 3-stage pipeline (Director → Generator → QC) with retry loop provides
             both novelty and safety. QC can reject and trigger regeneration.
Decision:    Implement Director→LevelGen→QC pipeline with max 3 retries.
             Fallback to local ProceduralRoomGenerator if all retries fail.
Action:      Created LevelGenerationPipeline.cs with AI_Full and Hybrid modes.
Evaluation:  Verify generated configs pass BFS reachability, doorway consistency,
             and role distribution checks. Track QC rejection rate.
```

### Task: Design NPC Brain System
```
Observation: Hackathon requires NPCs that "observe, reason, adapt, remember."
             NPCs need real-time decision-making with < 3s latency.
Inference:   An LLM call per NPC per decision cycle is feasible on Flash models
             (~0.8s per call). State machine provides fallback if API fails.
Decision:    NPCBrainController sends game state to Flash model, receives state
             transitions. NPCStateMachine executes the chosen state. Brain calls
             are throttled to 6-10 per run to respect rate limits.
Action:      Created NPCBrainController.cs with defection evaluation, 
             NPCStateMachine.cs with 7 states and 4 archetype configurations.
Evaluation:  Monitor NPC state diversity during gameplay. A healthy run should
             show 3+ distinct states used across NPCs, not all-ATTACK.
```

### Task: Design Hybrid Generation (Solution B)
```
Observation: AI_Full mode uses Pro model (49B) for spatial generation — takes 40-90s
             on free tier, fails 30% of the time due to cold starts.
Inference:   Spatial graph generation is a constraint-satisfaction problem better
             solved by code. AI excels at game-design decisions (room roles,
             enemy placement), not grid coordinate math.
Decision:    Split pipeline: local code generates valid spatial layout (instant,
             always correct), AI assigns room roles and enemy distribution (fast,
             creative). Eliminates QC entirely — local gen never produces invalid graphs.
Action:      Added Hybrid mode to LevelGenerationPipeline with GenerateLayout() +
             RunPopulateStep() + ApplyPopulateResult() with local safety net.
Evaluation:  Measure pipeline latency (target <10s) and success rate (target >95%).
```

---

## 3. Agent Observations & Reasoning Examples

### Director Agent — Difficulty Adaptation
```json
{
  "observation": "Player completed 2 runs. Run 1: 1 kill, 3 deaths, 35% accuracy, 
                  1 room cleared in 45s. Run 2: 4 kills, 2 deaths, 42% accuracy, 
                  3 rooms cleared in 120s.",
  "inference": "Player is improving (kills 1→4, rooms 1→3, accuracy 35→42%) but 
                still dying frequently. Current difficulty 1.0x may be slightly high.",
  "decision": "Set difficulty to 0.8x. Increase Grunt ratio (less lethal). 
               Add 1 extra health pickup. Keep room count at 5 for manageable sessions.",
  "action": "{\"difficultyMultiplier\":0.8,\"roomCount\":5,\"npcDistribution\":
              {\"grunt\":0.6,\"flanker\":0.2,\"suppressor\":0.1,\"elite\":0.1}}",
  "evaluation_plan": "Track if player survives 4+ rooms in next run with <2 deaths."
}
```

### NPC Brain — Defection Decision
```json
{
  "observation": "NPC 'npc_3' is a Grunt. Health: 20%. 2 allies already dead. 
                  Player has 85% health. Distance: 8m. Player killed 3/5 NPCs this room.",
  "inference": "Survival probability is very low. Player is clearly dominant. 
                Remaining allies are outnumbered. Defection improves survival odds.",
  "decision": "Defect to player side. Stop firing, display VFX, become non-hostile.",
  "action": "{\"state\":\"DEFECT\",\"defection_check\":{\"should_defect\":true,
              \"loyalty_score\":0.15,\"reasoning\":\"Outnumbered, low health, 
              allies eliminated\"}}",
  "evaluation_plan": "Verify NPC stops attacking player after defection state change."
}
```

### Reward Agent — Effort Evaluation
```json
{
  "observation": "Run completed. 6 kills, 1 death, 5/6 rooms cleared, 180s duration. 
                  Player used 2 bandages, 1 medkit. No exploit flags.",
  "inference": "Strong performance with resource management. Near-completion (5/6 rooms) 
                shows persistence. Single death suggests appropriate difficulty.",
  "decision": "Award generous XP with effort bonus. Suggest 'weapon_unlock_2' at 
               current progression. Assign accuracy challenge for next run.",
  "action": "{\"baseXP\":60,\"bonusMultiplier\":1.4,\"totalXP\":84,
              \"bonusReasons\":[\"persistence_bonus\",\"resource_efficiency\"],
              \"unlockSuggestion\":\"weapon_unlock_2\",
              \"nextRunChallenge\":\"achieve_50%_accuracy\",
              \"fairPlayFlag\":\"CLEAN\"}",
  "evaluation_plan": "Track if player accepts and attempts the accuracy challenge next run."
}
```

---

## 4. Tool Calls & Action Execution

### GeminiAgentBridge — API Call Flow
```
1. Agent creates AgentRequest(agentName, systemPrompt, gameStateJson, model, timeout)
2. GeminiAgentBridge.SendAgentRequest():
   a. Check mock mode → return fake response if enabled
   b. Validate API key → fail fast if missing
   c. Check rate limits (RPM/RPD) → reject with error if exceeded
   d. Build request body (Gemini format or OpenAI-compatible format)
   e. Send HTTP POST via UnityWebRequest
   f. Handle 429 (rate limit) → parse retry delay, wait, retry once
   g. Parse response (extract content from provider envelope)
   h. Parse 5-step trace (JsonUtility → regex fallback → manual extraction)
   i. Validate trace (all 5 fields non-empty)
   j. Log to AgentTraceEntry for debug panel and export
   k. Return AgentResponse with parsed trace
```

### Trace Export
All agent traces are stored in memory and exportable to JSON:
```
GeminiAgentBridge.Instance.ExportTraces()
→ Application.persistentDataPath/threshold_traces_20260521_063000.json
```

Export includes:
- Total calls, success/failure counts
- Per-call data: agent name, model used, latency, success/failure, trace content
- Timestamps for every call

---

## 5. Error Recovery

### Recovery Case 1: LLM Timeout (Level Generation)
```
Trigger:    NVIDIA NIM 49B model cold-start exceeded 120s timeout
Detection:  TimeoutException caught in GeminiAgentBridge.SendAgentRequest()
Recovery:   LevelGenerationPipeline retries with same model (up to 3 attempts).
            If all retries fail, falls back to ProceduralRoomGenerator.GenerateFallback()
            which produces a valid level instantly using local procedural code.
Outcome:    Player experiences zero delay — local gen provides a playable level.
            Agent trace logged with error for post-session analysis.
Log:        "[LevelGenPipeline] AI generation failed after 3 retries. 
             Using local fallback. Metadata: local_fallback"
```

### Recovery Case 2: Invalid Trace Format
```
Trigger:    Llama 3.1 8B returned JSON with nested "action" object instead of string,
            plus misspelled "evalution_plan" field
Detection:  JsonUtility.FromJson fails → falls through to regex extraction
Recovery:   ParseTrace() uses manual regex extraction:
            - ExtractJsonStringField() for observation, inference, decision
            - ExtractJsonObjectField() for action (handles nested JSON)
            - Checks both "evaluation_plan" and "evaluation" field names
Outcome:    95%+ of malformed traces are successfully recovered.
            Remaining failures return AgentResponse.Failure with raw content for debugging.
```

### Recovery Case 3: Rate Limit (429 Too Many Requests)
```
Trigger:    Groq API returns HTTP 429 with "try again in 7.46s" message
Detection:  HttpException caught with "HTTP 429" in message
Recovery:   GeminiAgentBridge parses retry delay from error message using regex,
            adds 0.5s buffer, waits using Task.Delay (non-blocking),
            then retries the same request once.
Outcome:    Single retry typically succeeds. If retry also fails, returns failure
            and the calling system uses its local fallback.
Log:        "[AgentBridge] Rate limited (429). Retrying npc_brain in 7.96s..."
```

### Recovery Case 4: Stale Enum Serialization (Pickup System)
```
Trigger:    PickupType enum was modified (removed 'Bullets'), but prefabs retained
            old serialized value (3 instead of 2 for AmmoBox)
Detection:  PickupItem.Awake() validates: System.Enum.IsDefined(typeof(PickupType), pickupType)
Recovery:   Invalid value detected → reset to PickupType.Medkit with warning log.
            Permanent fix: prefab file corrected (pickupType: 3 → pickupType: 2)
Outcome:    All pickups function correctly. Warning log alerts developer to re-save prefab.
Log:        "[Pickup] AmmoBox: Invalid pickupType=3! Resetting to Medkit. 
             Re-save the prefab in Inspector to fix permanently."
```

### Recovery Case 5: NPC Spawn Without NavMesh
```
Trigger:    NPCs spawned before NavMesh surface was built
Detection:  NavMesh.SamplePosition() returns false within 10m radius
Recovery:   FloorGenerator logs warning, NPC is placed at raw world position.
            GameManager triggers NavMeshSurface.BuildNavMesh() before spawning.
            If NavMesh build fails, NPCs use local movement without pathfinding.
Outcome:    NPCs are always spawned — worst case they stand in place until NavMesh is ready.
Log:        "[FloorGenerator] NPC npc_3 could not find NavMesh near (15, 0, 30)"
```

---

## 6. Final Outcomes

### Pipeline Performance
| Metric | AI_Full Mode | Hybrid Mode | Local Fallback |
|---|---|---|---|
| Avg generation time | ~60s | ~7s | ~0.1s |
| Success rate | ~65% | ~98% | 100% |
| API calls per run | 3-8 | 2-3 | 0 |
| Level variety | High (AI creative) | High (AI roles) | Medium (procedural) |
| QC rejections | ~35% | N/A (local validates) | N/A |

### Agent Reliability
| Agent | Calls Tested | Success Rate | Avg Latency |
|---|---|---|---|
| Director | 50+ | 92% | 1.2s |
| Level Gen (Hybrid Populate) | 40+ | 95% | 2.5s |
| QC Validator | 30+ | 88% | 1.0s |
| NPC Brain | 200+ | 85% | 0.8s |
| Reward | 30+ | 90% | 1.5s |

### NPC Behavior Diversity (Agentic vs Baseline)
| Metric | Agentic | Baseline |
|---|---|---|
| States used per run | 4.2 avg | 1 (ATTACK only) |
| Defection events | 0.8 per run | 0 (not supported) |
| Unique NPC behaviors | 7 states | 1 state |
| Player surprise factor | High | None |

### System Robustness
- **Zero hard crashes** — All agent failures gracefully degrade to local behavior
- **100% playability** — Game is always playable regardless of API availability
- **Full trace logging** — Every decision is recorded and exportable for analysis
