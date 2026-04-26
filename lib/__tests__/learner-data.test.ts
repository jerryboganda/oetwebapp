import { describe, it, expect, vi, beforeEach } from 'vitest';

const {
  mockFetchScoreGuarantee,
  mockFetchScoreEquivalences,
  mockFetchStudyCommitment,
  mockFetchCertificates,
  mockFetchReferralInfo,
  mockFetchAnnotationTemplates,
} = vi.hoisted(() => ({
  mockFetchScoreGuarantee: vi.fn(),
  mockFetchScoreEquivalences: vi.fn(),
  mockFetchStudyCommitment: vi.fn(),
  mockFetchCertificates: vi.fn(),
  mockFetchReferralInfo: vi.fn(),
  mockFetchAnnotationTemplates: vi.fn(),
}));

vi.mock('../api', () => ({
  fetchScoreGuarantee: mockFetchScoreGuarantee,
  fetchScoreEquivalences: mockFetchScoreEquivalences,
  fetchStudyCommitment: mockFetchStudyCommitment,
  fetchCertificates: mockFetchCertificates,
  fetchReferralInfo: mockFetchReferralInfo,
  fetchAnnotationTemplates: mockFetchAnnotationTemplates,
}));

import {
  getScoreGuaranteeData,
  getScoreEquivalencesData,
  getStudyCommitmentData,
  getCertificatesData,
  getReferralData,
  getAnnotationTemplatesData,
} from '../learner-data';

beforeEach(() => {
  [
    mockFetchScoreGuarantee,
    mockFetchScoreEquivalences,
    mockFetchStudyCommitment,
    mockFetchCertificates,
    mockFetchReferralInfo,
    mockFetchAnnotationTemplates,
  ].forEach((m) => m.mockReset());
});

describe('getScoreGuaranteeData', () => {
  it('returns null when payload has no id', async () => {
    mockFetchScoreGuarantee.mockResolvedValue({});
    expect(await getScoreGuaranteeData()).toBeNull();
  });

  it('returns null when payload is not an object', async () => {
    mockFetchScoreGuarantee.mockResolvedValue('garbage');
    expect(await getScoreGuaranteeData()).toBeNull();
  });

  it('maps a complete payload', async () => {
    mockFetchScoreGuarantee.mockResolvedValue({
      id: 'g1',
      userId: 'u1',
      subscriptionId: 's1',
      baselineScore: 320,
      guaranteedImprovement: 30,
      actualScore: 360,
      status: 'claim_submitted',
      proofDocumentUrl: 'https://x',
      claimNote: 'note',
      reviewNote: null,
      activatedAt: '2026-01-01',
      expiresAt: '2026-12-31',
    });
    const r = await getScoreGuaranteeData();
    expect(r).toMatchObject({
      id: 'g1',
      baselineScore: 320,
      actualScore: 360,
      status: 'claim_submitted',
      proofDocumentUrl: 'https://x',
      reviewNote: null,
    });
  });

  it('actualScore null is preserved (not coerced to 0)', async () => {
    mockFetchScoreGuarantee.mockResolvedValue({
      id: 'g1',
      userId: 'u',
      subscriptionId: 's',
      baselineScore: 320,
      guaranteedImprovement: 30,
      actualScore: null,
      status: 'active',
      activatedAt: '',
      expiresAt: '',
    });
    expect((await getScoreGuaranteeData())?.actualScore).toBeNull();
  });

  it('falls back to "active" for unknown status', async () => {
    mockFetchScoreGuarantee.mockResolvedValue({ id: 'g1', status: 'wibble' });
    expect((await getScoreGuaranteeData())?.status).toBe('active');
  });

  it.each([
    'claim_submitted',
    'claim_approved',
    'claim_rejected',
    'expired',
    'active',
  ])('preserves recognised status %s', async (status) => {
    mockFetchScoreGuarantee.mockResolvedValue({ id: 'g1', status });
    expect((await getScoreGuaranteeData())?.status).toBe(status);
  });

  it('coerces non-finite numeric inputs to 0', async () => {
    mockFetchScoreGuarantee.mockResolvedValue({
      id: 'g1',
      baselineScore: 'NaN-thing',
      guaranteedImprovement: undefined,
    });
    const r = await getScoreGuaranteeData();
    expect(r?.baselineScore).toBe(0);
    expect(r?.guaranteedImprovement).toBe(0);
  });
});

describe('getScoreEquivalencesData', () => {
  it('returns empty arrays when payload missing', async () => {
    mockFetchScoreEquivalences.mockResolvedValue(null);
    const r = await getScoreEquivalencesData();
    expect(r.equivalences).toEqual([]);
    expect(r.institutions).toEqual([]);
  });

  it('maps equivalence + institution rows, defaulting missing strings to ""', async () => {
    mockFetchScoreEquivalences.mockResolvedValue({
      equivalences: [{ oetGrade: 'B', oetScore: '350', ielts: '7.0' }],
      institutions: [{ institution: 'GMC', country: 'UK' }],
    });
    const r = await getScoreEquivalencesData();
    expect(r.equivalences[0]).toEqual({
      oetGrade: 'B',
      oetScore: '350',
      ielts: '7.0',
      pte: '',
      cefr: '',
    });
    expect(r.institutions[0]).toEqual({
      institution: 'GMC',
      country: 'UK',
      minimumOetGrade: '',
      profession: '',
    });
  });

  it('drops non-array equivalences/institutions silently', async () => {
    mockFetchScoreEquivalences.mockResolvedValue({
      equivalences: 'not-array',
      institutions: { not: 'array' },
    });
    const r = await getScoreEquivalencesData();
    expect(r.equivalences).toEqual([]);
    expect(r.institutions).toEqual([]);
  });
});

describe('getStudyCommitmentData', () => {
  it('returns null without an id', async () => {
    mockFetchStudyCommitment.mockResolvedValue({ userId: 'u1' });
    expect(await getStudyCommitmentData()).toBeNull();
  });

  it('coerces booleans strictly via === true', async () => {
    mockFetchStudyCommitment.mockResolvedValue({
      id: 'sc1',
      userId: 'u',
      dailyMinutes: 30,
      freezeProtections: 4,
      freezeProtectionsUsed: 1,
      isActive: 'truthy-string',
    });
    expect((await getStudyCommitmentData())?.isActive).toBe(false);
  });

  it('maps a fully populated payload', async () => {
    mockFetchStudyCommitment.mockResolvedValue({
      id: 'sc1',
      userId: 'u',
      dailyMinutes: 45,
      freezeProtections: 4,
      freezeProtectionsUsed: 2,
      isActive: true,
    });
    expect(await getStudyCommitmentData()).toEqual({
      id: 'sc1',
      userId: 'u',
      dailyMinutes: 45,
      freezeProtections: 4,
      freezeProtectionsUsed: 2,
      isActive: true,
    });
  });
});

describe('getCertificatesData', () => {
  it('returns empty array when payload non-array', async () => {
    mockFetchCertificates.mockResolvedValue(null);
    expect(await getCertificatesData()).toEqual([]);
  });

  it('normalizes certificate types', async () => {
    mockFetchCertificates.mockResolvedValue([
      { id: 'c1', certificateType: 'mock_exam' },
      { id: 'c2', certificateType: 'readiness_threshold' },
      { id: 'c3', certificateType: 'streak_milestone' },
      { id: 'c4', certificateType: 'unknown' },
      { id: 'c5' },
    ]);
    const r = await getCertificatesData();
    expect(r.map((c) => c.certificateType)).toEqual([
      'mock_exam',
      'readiness_threshold',
      'streak_milestone',
      'study_plan_complete',
      'study_plan_complete',
    ]);
  });

  it('preserves nullable metadataJson', async () => {
    mockFetchCertificates.mockResolvedValue([
      { id: 'c1', metadataJson: '{"a":1}' },
      { id: 'c2', metadataJson: '' },
      { id: 'c3' },
    ]);
    const r = await getCertificatesData();
    expect(r[0].metadataJson).toBe('{"a":1}');
    expect(r[1].metadataJson).toBeNull();
    expect(r[2].metadataJson).toBeNull();
  });
});

describe('getReferralData', () => {
  it('returns zero-value defaults on empty payload', async () => {
    mockFetchReferralInfo.mockResolvedValue({});
    const r = await getReferralData();
    expect(r).toEqual({
      referralCode: null,
      totalReferrals: 0,
      activatedReferrals: 0,
      totalCreditsEarned: 0,
      referrals: [],
    });
  });

  it('preserves non-empty referralCode but treats empty string as null', async () => {
    mockFetchReferralInfo.mockResolvedValue({ referralCode: '' });
    expect((await getReferralData()).referralCode).toBeNull();

    mockFetchReferralInfo.mockResolvedValue({ referralCode: 'CODE1' });
    expect((await getReferralData()).referralCode).toBe('CODE1');
  });

  it.each([
    ['activated', 'activated'],
    ['rewarded', 'rewarded'],
    ['expired', 'expired'],
    ['unknown', 'pending'],
    [undefined, 'pending'],
  ])('normalizes referral status %s → %s', async (input, expected) => {
    mockFetchReferralInfo.mockResolvedValue({
      referrals: [{ id: 'r1', referredUserId: 'u', status: input, createdAt: '2026-01-01' }],
    });
    const r = await getReferralData();
    expect(r.referrals[0].status).toBe(expected);
  });
});

describe('getAnnotationTemplatesData', () => {
  it('passes params through to the api fn', async () => {
    mockFetchAnnotationTemplates.mockResolvedValue([]);
    await getAnnotationTemplatesData({ subtestCode: 'writing' } as never);
    expect(mockFetchAnnotationTemplates).toHaveBeenCalledWith({ subtestCode: 'writing' });
  });

  it('maps templates with default zero usageCount and false isShared', async () => {
    mockFetchAnnotationTemplates.mockResolvedValue([
      { id: 't1', label: 'Tone', templateText: 'be polite' },
    ]);
    const r = await getAnnotationTemplatesData();
    expect(r[0]).toMatchObject({
      id: 't1',
      label: 'Tone',
      templateText: 'be polite',
      usageCount: 0,
      isShared: false,
    });
  });

  it('returns empty array on non-array payload', async () => {
    mockFetchAnnotationTemplates.mockResolvedValue({ not: 'array' });
    expect(await getAnnotationTemplatesData()).toEqual([]);
  });
});
