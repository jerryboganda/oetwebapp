import { describe, it, expect } from 'vitest';
import { oetRawToScaled, OET_LR_RAW_PASS, OET_SCALED_PASS_B } from '@/lib/scoring';

describe('E2E Test Runner Verification', () => {
  it('verifies vitest runner fast execution and scoring anchor', () => {
    expect(oetRawToScaled(OET_LR_RAW_PASS)).toBe(OET_SCALED_PASS_B);
  });
});
