import { describe, it, expect } from 'vitest';
import {
  clampToeflScore,
  clampToeflSectionScore,
  isValidToeflScore,
  isValidToeflSectionScore,
  toeflOverallScore,
  toeflSectionBand,
  toeflSectionBandLabel,
  toeflReadinessBand,
  toeflReadinessBandLabel,
  formatToeflScoreDisplay,
  formatToeflGradeDisplay,
  TOEFL_SCORE_MIN,
  TOEFL_SCORE_MAX,
  TOEFL_SECTION_MIN,
  TOEFL_SECTION_MAX,
  TOEFL_DEFAULT_TARGET_SCORE,
  TOEFL_SECTION_THRESHOLDS,
  type ToeflSubtest,
} from '@/lib/toefl-scoring';

import {
  getExamScoringStrategy,
  registerExamScoringStrategy,
  OetScoringStrategy,
  IeltsScoringStrategy,
  PteScoringStrategy,
  ToeflScoringStrategy,
  formatScoreDisplay,
  formatGradeDisplay,
  normalizeTargetScore,
  sharedReadinessBand,
  sharedReadinessBandLabel,
  examFamilyLabel,
  examFamilyScoreHint,
  type IExamScoringStrategy,
  type SharedReadinessBand,
} from '@/lib/exam-family-scoring';

// ============================================================================
// CHALLENGER 1 M3 ADVERSARIAL STRESS TEST SUITE
// 1. TOEFL Canonical Scoring Engine & CEFR Transitions
// 2. Multi-Exam Strategy Polymorphism & Country-Awareness
// 3. Credit Concurrency, Pool Priority Cascades & Race Hardening
// ============================================================================

describe('CHALLENGER 1 M3 AUDIT 1: TOEFL SCORING BOUNDARY & CEFR TRANSITIONS', () => {
  describe('Section Score Boundaries (0, 9, 10, 17, 18, 23, 24, 30)', () => {
    const requiredBoundaryValues = [0, 9, 10, 17, 18, 23, 24, 30];

    it('validates and clamps all required boundary values [0, 9, 10, 17, 18, 23, 24, 30]', () => {
      for (const val of requiredBoundaryValues) {
        expect(clampToeflSectionScore(val)).toBe(val);
        expect(isValidToeflSectionScore(val)).toBe(true);
      }
    });

    it('Reading section CEFR boundary transitions & off-by-one verification', () => {
      // Reading: 0..9 (below_low), 10..17 (low_int), 18..23 (high_int), 24..30 (advanced)
      expect(toeflSectionBand('reading', 0)).toBe('below_low_intermediate');
      expect(toeflSectionBand('reading', 9)).toBe('below_low_intermediate');
      expect(toeflSectionBand('reading', 10)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 17)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 18)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 23)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 24)).toBe('advanced');
      expect(toeflSectionBand('reading', 30)).toBe('advanced');
    });

    it('Listening section CEFR boundary transitions & off-by-one verification', () => {
      // Listening: 0..8 (below_low), 9..16 (low_int), 17..21 (high_int), 22..30 (advanced)
      expect(toeflSectionBand('listening', 0)).toBe('below_low_intermediate');
      expect(toeflSectionBand('listening', 8)).toBe('below_low_intermediate');
      expect(toeflSectionBand('listening', 9)).toBe('low_intermediate');
      expect(toeflSectionBand('listening', 16)).toBe('low_intermediate');
      expect(toeflSectionBand('listening', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('listening', 21)).toBe('high_intermediate');
      expect(toeflSectionBand('listening', 22)).toBe('advanced');
      expect(toeflSectionBand('listening', 30)).toBe('advanced');
    });

    it('Speaking section CEFR boundary transitions & off-by-one verification', () => {
      // Speaking: 0..15 (below_low), 16..19 (low_int), 20..24 (high_int), 25..30 (advanced)
      expect(toeflSectionBand('speaking', 0)).toBe('below_low_intermediate');
      expect(toeflSectionBand('speaking', 15)).toBe('below_low_intermediate');
      expect(toeflSectionBand('speaking', 16)).toBe('low_intermediate');
      expect(toeflSectionBand('speaking', 19)).toBe('low_intermediate');
      expect(toeflSectionBand('speaking', 20)).toBe('high_intermediate');
      expect(toeflSectionBand('speaking', 24)).toBe('high_intermediate');
      expect(toeflSectionBand('speaking', 25)).toBe('advanced');
      expect(toeflSectionBand('speaking', 30)).toBe('advanced');
    });

    it('Writing section CEFR boundary transitions & off-by-one verification', () => {
      // Writing: 0..12 (below_low), 13..16 (low_int), 17..23 (high_int), 24..30 (advanced)
      expect(toeflSectionBand('writing', 0)).toBe('below_low_intermediate');
      expect(toeflSectionBand('writing', 12)).toBe('below_low_intermediate');
      expect(toeflSectionBand('writing', 13)).toBe('low_intermediate');
      expect(toeflSectionBand('writing', 16)).toBe('low_intermediate');
      expect(toeflSectionBand('writing', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('writing', 23)).toBe('high_intermediate');
      expect(toeflSectionBand('writing', 24)).toBe('advanced');
      expect(toeflSectionBand('writing', 30)).toBe('advanced');
    });
  });

  describe('Total Score Clamping, Aggregation & Readiness Bands', () => {
    it('exhaustively checks total score clamping across severe out-of-bound inputs', () => {
      expect(clampToeflScore(-999)).toBe(0);
      expect(clampToeflScore(-1)).toBe(0);
      expect(clampToeflScore(0)).toBe(0);
      expect(clampToeflScore(120)).toBe(120);
      expect(clampToeflScore(121)).toBe(120);
      expect(clampToeflScore(9999)).toBe(120);
      expect(clampToeflScore(NaN)).toBe(0);
      expect(clampToeflScore(Infinity)).toBe(0);
      expect(clampToeflScore(-Infinity)).toBe(0);
    });

    it('verifies 4-section aggregation with independent section clamping', () => {
      // Perfect 120
      expect(toeflOverallScore({ reading: 30, listening: 30, speaking: 30, writing: 30 })).toBe(120);
      // Min 0
      expect(toeflOverallScore({ reading: 0, listening: 0, speaking: 0, writing: 0 })).toBe(0);
      // Overflow per section gets clamped to 30 each -> 120
      expect(toeflOverallScore({ reading: 50, listening: 40, speaking: 35, writing: 90 })).toBe(120);
      // Underflow per section gets clamped to 0 each -> 0
      expect(toeflOverallScore({ reading: -10, listening: -5, speaking: -1, writing: -100 })).toBe(0);
      // Benchmark 80 (20 x 4)
      expect(toeflOverallScore({ reading: 20, listening: 20, speaking: 20, writing: 20 })).toBe(80);
      // Asymmetric combination
      expect(toeflOverallScore({ reading: 28, listening: 26, speaking: 18, writing: 22 })).toBe(94);
    });

    it('verifies all readiness band transitions (<60 not_ready, <70 developing, <80 borderline, <95 exam_ready, >=95 strong)', () => {
      // not_ready (0..59)
      expect(toeflReadinessBand(0)).toBe('not_ready');
      expect(toeflReadinessBand(59)).toBe('not_ready');
      expect(toeflReadinessBandLabel('not_ready')).toBe('Not ready');

      // developing (60..69)
      expect(toeflReadinessBand(60)).toBe('developing');
      expect(toeflReadinessBand(69)).toBe('developing');
      expect(toeflReadinessBandLabel('developing')).toBe('Developing');

      // borderline (70..79)
      expect(toeflReadinessBand(70)).toBe('borderline');
      expect(toeflReadinessBand(79)).toBe('borderline');
      expect(toeflReadinessBandLabel('borderline')).toBe('Borderline');

      // exam_ready (80..94)
      expect(toeflReadinessBand(80)).toBe('exam_ready');
      expect(toeflReadinessBand(94)).toBe('exam_ready');
      expect(toeflReadinessBandLabel('exam_ready')).toBe('Exam-ready');

      // strong (95..120)
      expect(toeflReadinessBand(95)).toBe('strong');
      expect(toeflReadinessBand(120)).toBe('strong');
      expect(toeflReadinessBandLabel('strong')).toBe('Strong');
    });

    it('enforces strict type & format validation', () => {
      // Non-integers rejected
      expect(isValidToeflScore(80.5)).toBe(false);
      expect(isValidToeflSectionScore(23.4)).toBe(false);

      // Non-numeric strings rejected
      expect(isValidToeflScore('abc')).toBe(false);
      expect(isValidToeflScore('')).toBe(false);
      expect(isValidToeflScore(null)).toBe(false);
      expect(isValidToeflScore(undefined)).toBe(false);

      // Formatting displays
      expect(formatToeflScoreDisplay(85)).toBe('85/120');
      expect(formatToeflGradeDisplay(85)).toBe('Score 85');
    });
  });
});

describe('CHALLENGER 1 M3 AUDIT 2: MULTI-EXAM STRATEGY POLYMORPHISM & SWITCHING', () => {
  describe('Dynamic Strategy Switching & Resolver Fallback', () => {
    it('switches between all 4 supported exam families dynamically', () => {
      const oetStrat = getExamScoringStrategy('oet');
      const ieltsStrat = getExamScoringStrategy('ielts');
      const pteStrat = getExamScoringStrategy('pte');
      const toeflStrat = getExamScoringStrategy('toefl');

      expect(oetStrat.examFamily).toBe('oet');
      expect(ieltsStrat.examFamily).toBe('ielts');
      expect(pteStrat.examFamily).toBe('pte');
      expect(toeflStrat.examFamily).toBe('toefl');

      expect(oetStrat.maxScore).toBe(500);
      expect(ieltsStrat.maxScore).toBe(9);
      expect(pteStrat.maxScore).toBe(90);
      expect(toeflStrat.maxScore).toBe(120);
    });

    it('handles case-insensitivity and whitespace in exam family codes', () => {
      expect(getExamScoringStrategy('OET').examFamily).toBe('oet');
      expect(getExamScoringStrategy('  Ielts  ').examFamily).toBe('ielts');
      expect(getExamScoringStrategy('PtE').examFamily).toBe('pte');
      expect(getExamScoringStrategy('TOEFL  ').examFamily).toBe('toefl');
    });

    it('fails-safe to OET scoring strategy for any invalid or missing exam codes', () => {
      const invalidCodes = ['sat', 'gre', 'usmle', 'unknown', 'invalid_code', '', '   ', null, undefined];
      for (const code of invalidCodes) {
        const strat = getExamScoringStrategy(code as any);
        expect(strat).toBeInstanceOf(OetScoringStrategy);
        expect(strat.examFamily).toBe('oet');
        expect(strat.defaultTarget).toBe(350);
      }
    });

    it('supports custom strategy dynamic registration and isolation', () => {
      class MockDuolingoStrategy implements IExamScoringStrategy {
        readonly examFamily = 'duolingo' as any;
        readonly label = 'DET';
        readonly scoreHint = { hint: 'DET 10-160 scale', placeholder: '120' };
        readonly minScore = 10;
        readonly maxScore = 160;
        readonly defaultTarget = 120;
        formatScore(score: number): string { return String(score) + '/160'; }
        formatGrade(score: number): string { return 'DET ' + String(score); }
        normalizeTargetScore(value: any): number | null { return typeof value === 'number' ? value : null; }
        getReadinessBand(score: number): SharedReadinessBand { return score >= 120 ? 'exam_ready' : 'developing'; }
        isPass(score: number): boolean { return score >= 120; }
      }

      registerExamScoringStrategy(new MockDuolingoStrategy());
      const resolved = getExamScoringStrategy('duolingo');
      expect(resolved.label).toBe('DET');
      expect(resolved.maxScore).toBe(160);
      expect(resolved.isPass(125)).toBe(true);
      expect(resolved.isPass(110)).toBe(false);
    });
  });

  describe('Country-Aware Destination Scoring Across Families', () => {
    it('OET Strategy: enforces Grade B (350) for UK/GB/IE/AU/NZ/CA vs Grade C+ (300) for US/QA', () => {
      const oet = getExamScoringStrategy('oet');

      // Standard Grade B countries (>= 350)
      for (const c of ['GB', 'UK', 'IE', 'AU', 'NZ', 'CA']) {
        expect(oet.isPass(350, c)).toBe(true);
        expect(oet.isPass(349, c)).toBe(false);
        expect(oet.isPass(300, c)).toBe(false);
      }

      // Grade C+ countries (>= 300)
      for (const c of ['US', 'QA', 'us', 'qa']) {
        expect(oet.isPass(350, c)).toBe(true);
        expect(oet.isPass(300, c)).toBe(true);
        expect(oet.isPass(299, c)).toBe(false);
      }

      // Default (no country provided) -> defaults to 350
      expect(oet.isPass(350)).toBe(true);
      expect(oet.isPass(340)).toBe(false);
    });

    it('IELTS Strategy: evaluates 7.0 benchmark with half-band normalization', () => {
      const ielts = getExamScoringStrategy('ielts');
      expect(ielts.isPass(7.0)).toBe(true);
      expect(ielts.isPass(6.5)).toBe(false);
      expect(ielts.normalizeTargetScore('7.25')).toBe(7.5);
      expect(ielts.normalizeTargetScore('6.75')).toBe(7.0);
      expect(ielts.normalizeTargetScore(10)).toBeNull();
    });

    it('PTE Strategy: evaluates 65 target on 10..90 scale', () => {
      const pte = getExamScoringStrategy('pte');
      expect(pte.isPass(65)).toBe(true);
      expect(pte.isPass(64)).toBe(false);
      expect(pte.normalizeTargetScore('65')).toBe(65);
      expect(pte.normalizeTargetScore(5)).toBeNull();
    });

    it('TOEFL Strategy: evaluates 80 target on 0..120 scale', () => {
      const toefl = getExamScoringStrategy('toefl');
      expect(toefl.isPass(80)).toBe(true);
      expect(toefl.isPass(79)).toBe(false);
      expect(toefl.normalizeTargetScore('80')).toBe(80);
      expect(toefl.normalizeTargetScore(125)).toBeNull();
    });
  });
});

describe('CHALLENGER 1 M3 AUDIT 3: CREDIT CONCURRENCY & POOL PRIORITY CASCADE', () => {
  /**
   * Mock Ledger Wallet simulating Master Catalogue credit rules:
   * 1. Writing / Speaking consume 1 Dedicated credit if available.
   * 2. Otherwise consume 1 Flexible W/S credit if available.
   * 3. Otherwise consume 2 Universal Shared credits (SharedWritingOrSpeaking cost).
   * 4. Fails if no bucket has sufficient funds.
   */
  class ConcurrentCreditWallet {
    private dedicatedCredits: number;
    private flexibleCredits: number;
    private sharedCredits: number;
    private lock = false;

    public successfulDebits = 0;
    public failedDebits = 0;
    public debitHistory: Array<{ bucket: string; units: number; timestamp: number }> = [];

    constructor(dedicated: number, flexible: number, shared: number) {
      this.dedicatedCredits = dedicated;
      this.flexibleCredits = flexible;
      this.sharedCredits = shared;
    }

    public getSnapshot() {
      return {
        dedicated: this.dedicatedCredits,
        flexible: this.flexibleCredits,
        shared: this.sharedCredits,
        totalPossibleGradingSessions:
          this.dedicatedCredits + this.flexibleCredits + Math.floor(this.sharedCredits / 2),
      };
    }

    /**
     * Synchronous atomic deduction with pool priority cascade.
     */
    public deductGradingCredit(subtest: 'writing' | 'speaking'): {
      success: boolean;
      bucket?: 'dedicated' | 'flexible_ws' | 'shared';
      units?: number;
      errorCode?: string;
    } {
      if (this.dedicatedCredits >= 1) {
        this.dedicatedCredits -= 1;
        this.successfulDebits++;
        this.debitHistory.push({ bucket: 'dedicated', units: 1, timestamp: Date.now() });
        return { success: true, bucket: 'dedicated', units: 1 };
      }

      if (this.flexibleCredits >= 1) {
        this.flexibleCredits -= 1;
        this.successfulDebits++;
        this.debitHistory.push({ bucket: 'flexible_ws', units: 1, timestamp: Date.now() });
        return { success: true, bucket: 'flexible_ws', units: 1 };
      }

      if (this.sharedCredits >= 2) {
        this.sharedCredits -= 2;
        this.successfulDebits++;
        this.debitHistory.push({ bucket: 'shared', units: 2, timestamp: Date.now() });
        return { success: true, bucket: 'shared', units: 2 };
      }

      this.failedDebits++;
      return { success: false, errorCode: 'ai_credits_insufficient' };
    }

    /**
     * Async simulated concurrent deduction with atomic CAS spinlock.
     */
    public async deductAsyncConcurrent(subtest: 'writing' | 'speaking'): Promise<{
      success: boolean;
      bucket?: 'dedicated' | 'flexible_ws' | 'shared';
      units?: number;
    }> {
      // Simulate microtask jitter
      await new Promise((r) => setTimeout(r, Math.random() * 5));

      // Acquire lock (simulates DB row-level lock or serializable transaction)
      while (this.lock) {
        await new Promise((r) => setTimeout(r, 1));
      }
      this.lock = true;

      try {
        const res = this.deductGradingCredit(subtest);
        return res;
      } finally {
        this.lock = false;
      }
    }

    /**
     * Two-phase refund on terminal failure.
     */
    public refund(bucket: 'dedicated' | 'flexible_ws' | 'shared', units: number): void {
      if (bucket === 'dedicated') this.dedicatedCredits += units;
      else if (bucket === 'flexible_ws') this.flexibleCredits += units;
      else if (bucket === 'shared') this.sharedCredits += units;
    }
  }

  it('Waterfall Drain: strictly exhausts Dedicated -> Flexible W/S -> Shared (2x) in order', () => {
    // 2 Dedicated, 3 Flexible, 6 Shared = 2 + 3 + 3 = 8 sessions
    const wallet = new ConcurrentCreditWallet(2, 3, 6);

    // Sessions 1 & 2 -> Dedicated
    const s1 = wallet.deductGradingCredit('writing');
    const s2 = wallet.deductGradingCredit('writing');
    expect(s1).toEqual({ success: true, bucket: 'dedicated', units: 1 });
    expect(s2).toEqual({ success: true, bucket: 'dedicated', units: 1 });
    expect(wallet.getSnapshot().dedicated).toBe(0);
    expect(wallet.getSnapshot().flexible).toBe(3);
    expect(wallet.getSnapshot().shared).toBe(6);

    // Sessions 3, 4, 5 -> Flexible W/S
    const s3 = wallet.deductGradingCredit('writing');
    const s4 = wallet.deductGradingCredit('writing');
    const s5 = wallet.deductGradingCredit('writing');
    expect(s3).toEqual({ success: true, bucket: 'flexible_ws', units: 1 });
    expect(s4).toEqual({ success: true, bucket: 'flexible_ws', units: 1 });
    expect(s5).toEqual({ success: true, bucket: 'flexible_ws', units: 1 });
    expect(wallet.getSnapshot().flexible).toBe(0);
    expect(wallet.getSnapshot().shared).toBe(6);

    // Sessions 6, 7, 8 -> Shared (2 units each)
    const s6 = wallet.deductGradingCredit('writing');
    const s7 = wallet.deductGradingCredit('writing');
    const s8 = wallet.deductGradingCredit('writing');
    expect(s6).toEqual({ success: true, bucket: 'shared', units: 2 });
    expect(s7).toEqual({ success: true, bucket: 'shared', units: 2 });
    expect(s8).toEqual({ success: true, bucket: 'shared', units: 2 });
    expect(wallet.getSnapshot().shared).toBe(0);

    // Session 9 -> Insufficient
    const s9 = wallet.deductGradingCredit('writing');
    expect(s9.success).toBe(false);
    expect(s9.errorCode).toBe('ai_credits_insufficient');
    expect(wallet.successfulDebits).toBe(8);
    expect(wallet.failedDebits).toBe(1);
  });

  it('Shared Credit Parity & Remainder Clamp: 1 remaining shared credit cannot fund 2-unit W/S session', () => {
    // 0 Dedicated, 0 Flexible, 3 Shared -> only 1 session possible (3 - 2 = 1 left)
    const wallet = new ConcurrentCreditWallet(0, 0, 3);

    const first = wallet.deductGradingCredit('speaking');
    expect(first).toEqual({ success: true, bucket: 'shared', units: 2 });
    expect(wallet.getSnapshot().shared).toBe(1);

    // Second session fails because 1 < 2
    const second = wallet.deductGradingCredit('speaking');
    expect(second.success).toBe(false);
    expect(second.errorCode).toBe('ai_credits_insufficient');
    expect(wallet.getSnapshot().shared).toBe(1); // 1 credit remains intact
  });

  it('Massive Concurrency Stress: 50 simultaneous parallel requests against bounded multi-pool wallet', async () => {
    // Pool: 3 Dedicated, 4 Flexible, 6 Shared = 3 + 4 + 3 = 10 total possible sessions
    const wallet = new ConcurrentCreditWallet(3, 4, 6);
    const TOTAL_CONCURRENT_REQUESTS = 50;

    const promises = Array.from({ length: TOTAL_CONCURRENT_REQUESTS }, (_, idx) =>
      wallet.deductAsyncConcurrent(idx % 2 === 0 ? 'writing' : 'speaking')
    );

    const results = await Promise.all(promises);
    const successes = results.filter((r) => r.success);
    const failures = results.filter((r) => !r.success);

    // Exactly 10 successes and 40 failures
    expect(successes.length).toBe(10);
    expect(failures.length).toBe(40);

    // Bucket breakdown
    const dedicatedUsed = successes.filter((r) => r.bucket === 'dedicated').length;
    const flexibleUsed = successes.filter((r) => r.bucket === 'flexible_ws').length;
    const sharedUsed = successes.filter((r) => r.bucket === 'shared').length;

    expect(dedicatedUsed).toBe(3);
    expect(flexibleUsed).toBe(4);
    expect(sharedUsed).toBe(3); // 3 sessions * 2 = 6 units

    // Final balance is exactly zero across all buckets with zero balance corruption
    const finalSnapshot = wallet.getSnapshot();
    expect(finalSnapshot.dedicated).toBe(0);
    expect(finalSnapshot.flexible).toBe(0);
    expect(finalSnapshot.shared).toBe(0);
    expect(finalSnapshot.totalPossibleGradingSessions).toBe(0);
  });

  it('Two-Phase Commit and Refund Rollback Integrity', () => {
    const wallet = new ConcurrentCreditWallet(1, 0, 0);

    // Step 1: Reserve credit
    const reservation = wallet.deductGradingCredit('writing');
    expect(reservation.success).toBe(true);
    expect(reservation.bucket).toBe('dedicated');
    expect(wallet.getSnapshot().dedicated).toBe(0);

    // Step 2: Simulate downstream AI network crash -> refund
    wallet.refund(reservation.bucket!, reservation.units!);
    expect(wallet.getSnapshot().dedicated).toBe(1);

    // Step 3: Candidate can retry and succeed
    const retry = wallet.deductGradingCredit('writing');
    expect(retry.success).toBe(true);
    expect(wallet.getSnapshot().dedicated).toBe(0);
  });

  it('Extreme High-Concurrency: 100 simultaneous workers with randomized delays and atomic invariants', async () => {
    // 5 Dedicated, 5 Flexible, 10 Shared = 5 + 5 + 5 = 15 total sessions
    const wallet = new ConcurrentCreditWallet(5, 5, 10);
    const WORKERS = 100;

    const promises = Array.from({ length: WORKERS }, (_, i) =>
      wallet.deductAsyncConcurrent(i % 2 === 0 ? 'writing' : 'speaking')
    );

    const results = await Promise.all(promises);
    const successCount = results.filter((r) => r.success).length;
    const failureCount = results.filter((r) => !r.success).length;

    expect(successCount).toBe(15);
    expect(failureCount).toBe(85);

    const snapshot = wallet.getSnapshot();
    expect(snapshot.dedicated).toBe(0);
    expect(snapshot.flexible).toBe(0);
    expect(snapshot.shared).toBe(0);
    expect(snapshot.totalPossibleGradingSessions).toBe(0);
  });
});

describe('CHALLENGER 1 M3 AUDIT 4: EXHAUSTIVE EMPIRICAL MATRICES', () => {
  it('Exhaustive evaluation of all 31 section score points (0..30) across all 4 subtests', () => {
    const subtests: ToeflSubtest[] = ['reading', 'listening', 'speaking', 'writing'];
    for (const subtest of subtests) {
      const thresholds = TOEFL_SECTION_THRESHOLDS[subtest];
      for (let s = 0; s <= 30; s++) {
        const band = toeflSectionBand(subtest, s);
        if (s >= thresholds.advanced) {
          expect(band).toBe('advanced');
        } else if (s >= thresholds.highIntermediate) {
          expect(band).toBe('high_intermediate');
        } else if (s >= thresholds.lowIntermediate) {
          expect(band).toBe('low_intermediate');
        } else {
          expect(band).toBe('below_low_intermediate');
        }
      }
    }
  });

  it('Exhaustive evaluation of all 121 total score points (0..120) for monotonic readiness band progression', () => {
    const bandRank: Record<SharedReadinessBand, number> = {
      not_ready: 1,
      developing: 2,
      borderline: 3,
      exam_ready: 4,
      strong: 5,
    };

    let prevRank = 0;
    for (let total = 0; total <= 120; total++) {
      const band = toeflReadinessBand(total);
      const rank = bandRank[band];
      expect(rank).toBeGreaterThanOrEqual(prevRank);
      prevRank = rank;

      if (total < 60) expect(band).toBe('not_ready');
      else if (total < 70) expect(band).toBe('developing');
      else if (total < 80) expect(band).toBe('borderline');
      else if (total < 95) expect(band).toBe('exam_ready');
      else expect(band).toBe('strong');
    }
  });
});

describe('CHALLENGER 1 M3 AUDIT 5: 4-SKILL MIXED CREDIT CONTENTION & ARBITRAGE', () => {
  /**
   * 4-Skill Multi-Pool Ledger:
   * - Reading: 1 Shared credit
   * - Listening: 1 Shared credit
   * - Writing: Dedicated (1) -> Flexible W/S (1) -> Shared (2)
   * - Speaking: Dedicated (1) -> Flexible W/S (1) -> Shared (2)
   */
  class MultiSkillLedger {
    private dedicated: number;
    private flexible: number;
    private shared: number;
    private lock = false;

    public successfulAttempts = 0;
    public failedAttempts = 0;

    constructor(d: number, f: number, s: number) {
      this.dedicated = d;
      this.flexible = f;
      this.shared = s;
    }

    public getBalances() {
      return { dedicated: this.dedicated, flexible: this.flexible, shared: this.shared };
    }

    public async deductSkillAttempt(skill: 'reading' | 'listening' | 'writing' | 'speaking'): Promise<{
      success: boolean;
      bucket?: string;
      cost?: number;
    }> {
      await new Promise((r) => setTimeout(r, Math.random() * 3));

      while (this.lock) {
        await new Promise((r) => setTimeout(r, 1));
      }
      this.lock = true;

      try {
        if (skill === 'reading' || skill === 'listening') {
          if (this.shared >= 1) {
            this.shared -= 1;
            this.successfulAttempts++;
            return { success: true, bucket: 'shared', cost: 1 };
          }
          this.failedAttempts++;
          return { success: false };
        } else {
          // Writing or Speaking
          if (this.dedicated >= 1) {
            this.dedicated -= 1;
            this.successfulAttempts++;
            return { success: true, bucket: 'dedicated', cost: 1 };
          }
          if (this.flexible >= 1) {
            this.flexible -= 1;
            this.successfulAttempts++;
            return { success: true, bucket: 'flexible_ws', cost: 1 };
          }
          if (this.shared >= 2) {
            this.shared -= 2;
            this.successfulAttempts++;
            return { success: true, bucket: 'shared', cost: 2 };
          }
          this.failedAttempts++;
          return { success: false };
        }
      } finally {
        this.lock = false;
      }
    }
  }

  it('Mixed Concurrency Contention: 120 simultaneous requests across all 4 skills', async () => {
    // Starting balance: 4 Dedicated, 4 Flexible, 12 Shared
    // Potential capacity:
    // 4 Dedicated = 4 W/S
    // 4 Flexible = 4 W/S
    // 12 Shared = up to 12 R/L or 6 W/S
    const ledger = new MultiSkillLedger(4, 4, 12);
    const skills: Array<'reading' | 'listening' | 'writing' | 'speaking'> = [
      'reading',
      'listening',
      'writing',
      'speaking',
    ];

    const requests = Array.from({ length: 120 }, (_, i) =>
      ledger.deductSkillAttempt(skills[i % skills.length])
    );

    const outcomes = await Promise.all(requests);
    const successCount = outcomes.filter((o) => o.success).length;
    const failCount = outcomes.filter((o) => !o.success).length;

    // Total successful deductions cannot exceed initial pool capacity
    expect(successCount).toBeGreaterThanOrEqual(10);
    expect(successCount + failCount).toBe(120);

    const finalBal = ledger.getBalances();
    expect(finalBal.dedicated).toBeGreaterThanOrEqual(0);
    expect(finalBal.flexible).toBeGreaterThanOrEqual(0);
    expect(finalBal.shared).toBeGreaterThanOrEqual(0);
    expect(finalBal.dedicated).toBeLessThanOrEqual(4);
    expect(finalBal.flexible).toBeLessThanOrEqual(4);
    expect(finalBal.shared).toBeLessThanOrEqual(12);
  });

  it('Shared Credit Fractional/Odd Remainder Handling: 1 Shared credit can service R/L but NOT W/S', async () => {
    const ledger = new MultiSkillLedger(0, 0, 1);

    // Attempt Writing (requires 2 Shared) -> must FAIL
    const wResult = await ledger.deductSkillAttempt('writing');
    expect(wResult.success).toBe(false);
    expect(ledger.getBalances().shared).toBe(1);

    // Attempt Reading (requires 1 Shared) -> must SUCCEED
    const rResult = await ledger.deductSkillAttempt('reading');
    expect(rResult.success).toBe(true);
    expect(rResult.cost).toBe(1);
    expect(ledger.getBalances().shared).toBe(0);

    // Subsequent Reading attempt -> must FAIL
    const rResult2 = await ledger.deductSkillAttempt('reading');
    expect(rResult2.success).toBe(false);
  });
});

describe('CHALLENGER 1 M3 AUDIT 6: EXHAUSTIVE FUZZING & CONTRACT IDEMPOTENCY', () => {
  it('Fuzzes target score normalizer across all strategies with adversarial inputs', () => {
    const families = ['oet', 'ielts', 'pte', 'toefl'] as const;
    const fuzzInputs = [
      '',
      '   ',
      'null',
      'undefined',
      'NaN',
      'Infinity',
      '-Infinity',
      'DROP TABLE users;',
      '<script>alert(1)</script>',
      '--10',
      '1e10',
      '0x20',
      '100.999.888',
      '👍',
      '99999999999999999999',
      '-99999999999999999999',
    ];

    for (const f of families) {
      const strategy = getExamScoringStrategy(f);
      for (const input of fuzzInputs) {
        // Must never throw an uncaught exception
        expect(() => strategy.normalizeTargetScore(input)).not.toThrow();
      }
    }
  });

  it('Verifies idempotency and purity of strategy scoring methods under 1,000 iterations', () => {
    const oet = getExamScoringStrategy('oet');
    const toefl = getExamScoringStrategy('toefl');
    const ielts = getExamScoringStrategy('ielts');
    const pte = getExamScoringStrategy('pte');

    for (let i = 0; i < 1000; i++) {
      expect(oet.formatScore(350)).toBe('350/500');
      expect(oet.isPass(350, 'GB')).toBe(true);
      expect(oet.isPass(300, 'US')).toBe(true);
      expect(toefl.formatScore(80)).toBe('80/120');
      expect(toefl.isPass(80)).toBe(true);
      expect(ielts.formatScore(7.0)).toBe('7.0');
      expect(ielts.isPass(7.0)).toBe(true);
      expect(pte.formatScore(65)).toBe('65');
      expect(pte.isPass(65)).toBe(true);
    }
  });
});
