# TEST_INFRA.md — OET Preparation Platform & Multi-Exam Engine Test Infrastructure

## 1. Test Philosophy & Engineering Principles

The OET Preparation Platform test infrastructure is engineered around **four core principles**:
1. **Opaque-Box Requirement-Driven Verification**: Every test case exercises public interface contracts, domain state machines, and system invariants derived directly from `PROJECT.md` and `ORIGINAL_REQUEST.md` without cheating or hardcoding internal facade mocks.
2. **Deterministic Mathematical Anchor Verification**: OET scoring adheres to strict canonical invariants:
   - Objective Sub-Tests (Reading & Listening): 42 items total, exactly $30/42 \equiv 350/500$ (Grade B pass anchor), $0/42 \equiv 0$, $42/42 \equiv 500$.
   - Subjective Writing: 6 criteria with Purpose on a 0–3 scale and 5 other criteria on a 0–7 scale (max raw $38 \equiv 500$). Country-dependent pass threshold ($350$ for UK/IE/AU/NZ/CA vs $300$ for US/QA vs null for missing/unsupported).
   - Subjective Speaking: 9 criteria with Linguistic (4 criteria 0–6, max 24) and Clinical (5 criteria 0–3, max 15) combining to max raw $39 \equiv 500$. Universal $350$ pass threshold.
3. **Single-Turn Audit & Safety Invariants**: Every external AI execution must route through a centralized server-side gateway (`IAiGatewayService`) producing exactly one immutable `AiUsageRecord` per turn with SHA-256 fingerprinting, token metering, and circuit-breaker fallbacks.
4. **Zero-Deviation Content & Persistence Governance**: Official Reading papers must strictly enforce the 20/6/16=42 structure, Part A last block starting at 15 or 16, part-only PDFs, `texts: []`, printed answer keys, and fail-closed publication gating.

---

## 2. Feature Inventory & Requirements Traceability Matrix

| Feature ID | Feature Name | Specification Reference | Primary Contract / Artifact | Test Suite Location |
|---|---|---|---|---|
| **F01** | Reading 42-Item Sub-Test Engine | ORIGINAL_REQUEST §R1, PROJECT §Feature 1 | 20/6/16 structure, 15m Part A lock, Part B/C break | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F02** | Listening 42-Item Sub-Test Engine | ORIGINAL_REQUEST §R1, PROJECT §Feature 2 | 24/6/12 structure, 10-phase sequence, one-play locks | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F03** | Writing 45-Min Sub-Test Engine | ORIGINAL_REQUEST §R1, PROJECT §Feature 3 | Case notes viewer, 5m read lock, 180–200 words | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F04** | Speaking 2-Card Sub-Test Engine | ORIGINAL_REQUEST §R1, PROJECT §Feature 4 | 2 role-play cards, 3m prep + 5m talk, Whisper STT | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F05** | 4-Skill Unified Mock Orchestrator | ORIGINAL_REQUEST §R1, R4, PROJECT §Feature 5 | Cross-subtest coordinator (R→L→W→S), session state | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F06** | Objective Server Scoring Engine | ORIGINAL_REQUEST §R2, PROJECT §Feature 6 | 0–500 scale, 30/42=350 Grade B benchmark | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F07** | Subjective Rubric & Destination Policy | ORIGINAL_REQUEST §R2, PROJECT §Feature 7 | Writing 6 criteria (max 38), Speaking 9 (max 39) | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F08** | Statement of Results & Analytics | ORIGINAL_REQUEST §R2, PROJECT §Feature 8 | CBLA Statement of Results card, trend analytics | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F09** | Centralized AI Gateway & Telemetry | ORIGINAL_REQUEST §R5, PROJECT §Feature 9 | `IAiGatewayService`, `AiUsageRecord`, audit logging | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F10** | Persistent Volume Storage Infrastructure | ORIGINAL_REQUEST §R5, PROJECT §Feature 10 | `IFileStorage`, Docker named volumes, safe deletion | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F11** | Multi-Exam Extensible Strategy Pattern | ORIGINAL_REQUEST §R3, PROJECT §Feature 11 | `IExamScoringStrategy`, OET/IELTS/PTE/TOEFL decoupling | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F12** | Candidate Hub & Entitlements | ORIGINAL_REQUEST §R4, PROJECT §Feature 12 | Shared Credits (R1/L1/W2/S2) vs Flex W/S, gifts | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |
| **F13** | Admin Management & Zero-Deviation | ORIGINAL_REQUEST §R4, PROJECT §Feature 13 | 20/6/16 validation, chunked upload, publish gates | `tests/e2e-tiers/tier1-feature-coverage.test.ts` |

---

## 3. Systematic 4-Tier Test Architecture & Methodology

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TIER 4: REAL-WORLD APPLICATION SCENARIOS                 │
│  - Full 4-Skill Candidate Mock to Official Statement of Results             │
│  - Admin Zero-Deviation Ingest to Candidate Exam Completion                 │
│  - Purchase, Entitlements, Credit Consumption & Auto-Top-Up                │
│  - AI Gateway Circuit Breaker & Resilient Multi-Provider Fallback           │
│  - Timed Exam Disconnect, Local Storage & Monotonic Reconciliation          │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                  TIER 3: CROSS-FEATURE COMBINATORIAL PAIRS                  │
│  - Mock Session Sequencing ⨯ Sub-Test Timer Enforcements                   │
│  - Credit Ledger Decrement ⨯ Session State Transitions                     │
│  - Admin Zero-Deviation Ingestion ⨯ Candidate Visibility Gating             │
│  - AI Gateway Provider Outage ⨯ Deterministic Fallback & Persistence        │
│  - Multi-Exam Strategy Switching ⨯ Profile Goal Calibration                 │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                   TIER 2: BOUNDARY & CORNER CASE TESTS                      │
│  - Empty / Null Inputs (Blank Answers, 0-Word Essays, Empty Audio Chunks)   │
│  - Extreme Max Sizes (42/42 Correct, 500 Score Cap, 1000-Word Essay Limit) │
│  - 0/42 Zero Boundary & Grade E Classification                              │
│  - 30/42 Benchmark Threshold & Delta Transitions (29 vs 30 vs 31)           │
│  - Country Resolution Invariants (GB/IE/AU/NZ/CA vs US/QA vs Unsupported)   │
│  - Clock Drift, 15-min Lock Expiry & 5-min Reading Lock Timeouts            │
│  - Audio Stream Chunk Corruption & Base64 Packet Loss Recovery              │
│  - Network Drops & Monotonic Offline State Autosave Conflict Resolution     │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                    TIER 1: SYSTEMATIC FEATURE COVERAGE                      │
│  >= 5 Test Cases per Feature covering full Happy Paths for F01 through F13  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Tier Descriptions:
1. **Tier 1: Feature Coverage**: Verifies that every single feature identified in PROJECT.md satisfies its explicit requirements under normal operating conditions.
2. **Tier 2: Boundary & Corner Cases**: Exercises system limits, extreme raw score boundaries, clock timeouts, network anomalies, payload stress, and country resolution edge cases.
3. **Tier 3: Cross-Feature Combinations**: Pairwise verification ensuring multi-module interactions maintain transactional integrity, credit synchronization, and gating isolation.
4. **Tier 4: Real-World Application Scenarios**: High-fidelity lifecycle simulations exercising full user and administrator journeys from start to finish.

---

## 4. Test Execution Commands & Environment

### Primary E2E Test Suite Runner:
To run the complete 4-tier E2E test suite with fast, deterministic execution and full assertion verification:
```powershell
pnpm exec vitest run -c vitest.e2e.config.ts
```

### Scoped Tier Runs:
```powershell
# Tier 1: Feature Coverage
pnpm exec vitest run tests/e2e-tiers/tier1-feature-coverage.test.ts -c vitest.e2e.config.ts

# Tier 2: Boundary & Corner Cases
pnpm exec vitest run tests/e2e-tiers/tier2-boundary-corner.test.ts -c vitest.e2e.config.ts

# Tier 3: Cross-Feature Combinations
pnpm exec vitest run tests/e2e-tiers/tier3-cross-feature-combinations.test.ts -c vitest.e2e.config.ts

# Tier 4: Real-World Application Scenarios
pnpm exec vitest run tests/e2e-tiers/tier4-real-world-scenarios.test.ts -c vitest.e2e.config.ts
```

### Full Validation Suite Ladder:
```powershell
pnpm exec tsc --noEmit
pnpm exec vitest run -c vitest.e2e.config.ts
pnpm run backend:build
pnpm run backend:test
```
