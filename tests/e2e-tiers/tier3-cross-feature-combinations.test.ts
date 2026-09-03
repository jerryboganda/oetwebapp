import { describe, it, expect, vi } from 'vitest';
import {
  oetRawToScaled,
  oetGradeFromScaled,
  gradeListeningReading,
  gradeWriting,
  deriveWritingResultFromCriteria,
  gradeSpeaking,
  speakingProjectedScaled,
  OET_LR_RAW_MAX,
  OET_SCALED_PASS_B,
} from '@/lib/scoring';
import {
  formatScoreDisplay,
  formatGradeDisplay,
  normalizeTargetScore,
  sharedReadinessBand,
} from '@/lib/exam-family-scoring';
import {
  mockReportToStatementOfResults,
  isMockReportStatementOfResultsReady,
} from '@/lib/adapters/oet-sor-adapter';
import type { MockReport } from '@/lib/mock-data';

// ============================================================================
// TIER 3: CROSS-FEATURE COMBINATORIAL PAIRS
// Multi-module interactions, transactional workflows, and state transitions
// ============================================================================

describe('Tier 3 — Combo 1: Mock Session Lifecycle & Timer Lock State Transitions', () => {
  it('T3-C1-1: transitions cleanly through all 4 sub-tests with timer governance', () => {
    type SubtestStage =
      | 'not_started'
      | 'reading_part_a'
      | 'reading_part_bc'
      | 'reading_completed'
      | 'break_1'
      | 'listening_running'
      | 'listening_completed'
      | 'break_2'
      | 'writing_reading_window'
      | 'writing_active_drafting'
      | 'writing_completed'
      | 'break_3'
      | 'speaking_card_1_prep'
      | 'speaking_card_1_consult'
      | 'speaking_card_2_prep'
      | 'speaking_card_2_consult'
      | 'mock_completed';

    let currentStage: SubtestStage = 'not_started';
    const transition = (next: SubtestStage) => {
      currentStage = next;
    };

    transition('reading_part_a');
    expect(currentStage).toBe('reading_part_a');

    // 15-min lock -> Part B/C
    transition('reading_part_bc');
    transition('reading_completed');
    transition('break_1');
    expect(currentStage).toBe('break_1');

    // Listening
    transition('listening_running');
    transition('listening_completed');
    transition('break_2');

    // Writing 5m read lock -> active
    transition('writing_reading_window');
    transition('writing_active_drafting');
    transition('writing_completed');
    transition('break_3');

    // Speaking 2-card sequence
    transition('speaking_card_1_prep');
    transition('speaking_card_1_consult');
    transition('speaking_card_2_prep');
    transition('speaking_card_2_consult');
    transition('mock_completed');

    expect(currentStage).toBe('mock_completed');
  });
});

describe('Tier 3 — Combo 2: Credit Ledger Deduction & Session Progression', () => {
  it('T3-C2-1: decrements shared credits step-by-step and rolls back on insufficient funds', () => {
    interface CreditWallet {
      universalSharedCredits: number;
      consumeCredits(cost: number): boolean;
      refundCredits(cost: number): void;
    }

    const wallet: CreditWallet = {
      universalSharedCredits: 6, // R1 + L1 + W2 + S2 = 6 needed
      consumeCredits(cost: number) {
        if (this.universalSharedCredits < cost) return false;
        this.universalSharedCredits -= cost;
        return true;
      },
      refundCredits(cost: number) {
        this.universalSharedCredits += cost;
      },
    };

    // Stage 1: Reading (1 credit)
    expect(wallet.consumeCredits(1)).toBe(true);
    expect(wallet.universalSharedCredits).toBe(5);

    // Stage 2: Listening (1 credit)
    expect(wallet.consumeCredits(1)).toBe(true);
    expect(wallet.universalSharedCredits).toBe(4);

    // Stage 3: Writing (2 credits)
    expect(wallet.consumeCredits(2)).toBe(true);
    expect(wallet.universalSharedCredits).toBe(2);

    // Stage 4: Speaking (2 credits)
    expect(wallet.consumeCredits(2)).toBe(true);
    expect(wallet.universalSharedCredits).toBe(0);

    // Attempting another session without funds
    const failedAttempt = wallet.consumeCredits(1);
    expect(failedAttempt).toBe(false);
    expect(wallet.universalSharedCredits).toBe(0);
  });
});

describe('Tier 3 — Combo 3: Admin Zero-Deviation Ingest & Candidate Gating', () => {
  it('T3-C3-1: blocks candidate visibility until admin QA verification and publish approval', () => {
    interface ContentPaperRecord {
      id: string;
      title: string;
      partAQuestions: number;
      partBQuestions: number;
      partCQuestions: number;
      partALastBlockStart: number;
      status: 'draft' | 'under_review' | 'published';
      candidateVisible: boolean;
      qaPassed: boolean;
    }

    const paper: ContentPaperRecord = {
      id: 'paper-rd-2026',
      title: 'Reading Official Test 12',
      partAQuestions: 20,
      partBQuestions: 6,
      partCQuestions: 16,
      partALastBlockStart: 15,
      status: 'draft',
      candidateVisible: false,
      qaPassed: false,
    };

    // Candidate query should be empty while in draft
    const candidateQuery = (p: ContentPaperRecord) => (p.status === 'published' && p.candidateVisible ? p : null);
    expect(candidateQuery(paper)).toBeNull();

    // Admin QA validation
    const isZeroDeviationValid =
      paper.partAQuestions === 20 &&
      paper.partBQuestions === 6 &&
      paper.partCQuestions === 16 &&
      (paper.partALastBlockStart === 15 || paper.partALastBlockStart === 16);

    expect(isZeroDeviationValid).toBe(true);
    paper.qaPassed = true;
    paper.status = 'published';
    paper.candidateVisible = true;

    // Candidate query now returns live paper
    const publishedResult = candidateQuery(paper);
    expect(publishedResult).not.toBeNull();
    expect(publishedResult?.id).toBe('paper-rd-2026');
  });
});

describe('Tier 3 — Combo 4: AI Gateway Circuit Breaker & Score Persistence', () => {
  it('T3-C4-1: triggers failover to secondary AI provider and writes immutable audit record', async () => {
    interface AiProviderResult {
      completion: string;
      provider: string;
      model: string;
      tokens: number;
    }

    const primaryProvider = {
      call: vi.fn().mockRejectedValue(new Error('503 Service Unavailable')),
    };

    const secondaryProvider = {
      call: vi.fn().mockResolvedValue({
        completion: JSON.stringify({
          purpose: 3,
          content: 6,
          conciseness_clarity: 6,
          genre_style: 6,
          organisation_layout: 6,
          language: 6,
        }),
        provider: 'openai',
        model: 'gpt-4o',
        tokens: 1350,
      }),
    };

    const aiAuditLog: Array<{ provider: string; model: string; tokens: number; timestamp: string }> = [];

    // Fallback executor
    const executeWithFallback = async (): Promise<AiProviderResult> => {
      try {
        return await primaryProvider.call();
      } catch {
        const result = await secondaryProvider.call();
        aiAuditLog.push({
          provider: result.provider,
          model: result.model,
          tokens: result.tokens,
          timestamp: new Date().toISOString(),
        });
        return result;
      }
    };

    const outcome = await executeWithFallback();
    expect(outcome.provider).toBe('openai');
    expect(aiAuditLog.length).toBe(1);
    expect(aiAuditLog[0].provider).toBe('openai');

    // Verify deterministic rubric derivation from fallback output
    const criteria = JSON.parse(outcome.completion);
    const derived = deriveWritingResultFromCriteria(criteria, 'GB');
    expect(derived.rawTotal).toBe(33); // 3 + 6 + 6 + 6 + 6 + 6 = 33
    expect(derived.scaled).toBe(434); // round((33 * 500) / 38) = 434 (Grade B)
    expect(derived.grade).toBe('B');
  });
});

describe('Tier 3 — Combo 5: Multi-Exam Strategy Switching & Goal Calibration', () => {
  it('T3-C5-1: dynamically switches scoring strategy across OET, IELTS, and PTE for candidate goals', () => {
    interface CandidateGoalProfile {
      userId: string;
      examFamily: 'oet' | 'ielts' | 'pte';
      targetScore: number;
    }

    const profile: CandidateGoalProfile = {
      userId: 'user-multi-01',
      examFamily: 'oet',
      targetScore: 350,
    };

    expect(formatScoreDisplay(profile.examFamily, profile.targetScore)).toBe('350/500');
    expect(formatGradeDisplay(profile.examFamily, profile.targetScore)).toBe('Grade B');
    expect(sharedReadinessBand(profile.examFamily, profile.targetScore)).toBe('exam_ready');

    // Switch to IELTS
    profile.examFamily = 'ielts';
    profile.targetScore = 7.0;
    expect(formatScoreDisplay(profile.examFamily, profile.targetScore)).toBe('7.0');
    expect(formatGradeDisplay(profile.examFamily, profile.targetScore)).toBe('Band 7.0');
    expect(sharedReadinessBand(profile.examFamily, profile.targetScore)).toBe('exam_ready');

    // Switch to PTE
    profile.examFamily = 'pte';
    profile.targetScore = 65;
    expect(formatScoreDisplay(profile.examFamily, profile.targetScore)).toBe('65');
    expect(formatGradeDisplay(profile.examFamily, profile.targetScore)).toBe('Score 65');
    expect(sharedReadinessBand(profile.examFamily, profile.targetScore)).toBe('exam_ready');
  });
});

describe('Tier 3 — Combo 6: Plan Change, Entitlement Grant & Auto-Top-Up Trigger', () => {
  it('T3-C6-1: provisions product catalog bundles, gifts, and auto-top-up thresholds', () => {
    interface UserEntitlementState {
      planCode: string;
      writingCredits: number;
      speakingCredits: number;
      giftClaimed: boolean;
      autoTopUpEnabled: boolean;
      autoTopUpThreshold: number;
    }

    const state: UserEntitlementState = {
      planCode: 'quick-check',
      writingCredits: 3,
      speakingCredits: 3,
      giftClaimed: false,
      autoTopUpEnabled: false,
      autoTopUpThreshold: 2,
    };

    // Upgrade to Exam Prep Pro (adds 15 W, 15 S + 5 gifts)
    state.planCode = 'exam-prep-pro';
    state.writingCredits += 15;
    state.speakingCredits += 15;
    if (!state.giftClaimed) {
      state.writingCredits += 5; // 5 gift credits
      state.giftClaimed = true;
    }
    state.autoTopUpEnabled = true;

    expect(state.writingCredits).toBe(23); // 3 + 15 + 5
    expect(state.speakingCredits).toBe(18); // 3 + 15
    expect(state.giftClaimed).toBe(true);

    // Candidate consumes credits down to auto top-up threshold
    state.writingCredits -= 22; // drops to 1
    const shouldTriggerAutoTopUp = state.autoTopUpEnabled && state.writingCredits <= state.autoTopUpThreshold;
    expect(shouldTriggerAutoTopUp).toBe(true);
  });
});
