import {
  fetchScoreGuarantee,
  fetchScoreEquivalences,
  fetchStudyCommitment,
  fetchCertificates,
  fetchReferralInfo,
  fetchAnnotationTemplates,
} from './api';
import type {
  ScoreGuaranteePledge,
  ScoreEquivalencesData,
  ScoreEquivalenceRow,
  InstitutionRequirement,
  StudyCommitment,
  LearnerCertificate,
  ReferralInfo,
  ExpertAnnotationTemplate,
} from './types/learner';

type ApiRecord = Record<string, unknown>;

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

function toStringValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

function toNullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function toNumberValue(value: unknown, fallback = 0): number {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function toBooleanValue(value: unknown): boolean {
  return value === true;
}

// ── Score Guarantee ─────────────────────────────────

export async function getScoreGuaranteeData(): Promise<ScoreGuaranteePledge | null> {
  const raw = asRecord(await fetchScoreGuarantee());
  const id = raw.id ?? raw.pledgeId;
  if (!id) return null;
  return {
    id: toStringValue(id),
    userId: toStringValue(raw.userId),
    subscriptionId: toStringValue(raw.subscriptionId),
    baselineScore: toNumberValue(raw.baselineScore),
    guaranteedImprovement: toNumberValue(raw.guaranteedImprovement),
    actualScore: raw.actualScore != null ? toNumberValue(raw.actualScore) : null,
    status: normalizeGuaranteeStatus(raw.status),
    proofDocumentUrl: toNullableString(raw.proofDocumentUrl),
    claimNote: toNullableString(raw.claimNote),
    reviewNote: toNullableString(raw.reviewNote),
    activatedAt: toStringValue(raw.activatedAt),
    expiresAt: toStringValue(raw.expiresAt),
  };
}

function normalizeGuaranteeStatus(
  value: unknown,
): 'active' | 'claim_submitted' | 'claim_approved' | 'claim_rejected' | 'expired' {
  const s = toStringValue(value, 'active').toLowerCase();
  if (s === 'claim_submitted') return 'claim_submitted';
  if (s === 'claim_approved') return 'claim_approved';
  if (s === 'claim_rejected') return 'claim_rejected';
  if (s === 'expired') return 'expired';
  return 'active';
}

// ── Score Equivalences ──────────────────────────────

export async function getScoreEquivalencesData(): Promise<ScoreEquivalencesData> {
  const raw = asRecord(await fetchScoreEquivalences());
  return {
    equivalences: asArray(raw.equivalences).map(
      (e): ScoreEquivalenceRow => ({
        oetGrade: toStringValue(e.oetGrade),
        oetScore: toStringValue(e.oetScore),
        ielts: toStringValue(e.ielts),
        pte: toStringValue(e.pte),
        cefr: toStringValue(e.cefr),
      }),
    ),
    institutions: asArray(raw.institutions).map(
      (i): InstitutionRequirement => ({
        institution: toStringValue(i.institution),
        country: toStringValue(i.country),
        minimumOetGrade: toStringValue(i.minimumOetGrade),
        profession: toStringValue(i.profession),
      }),
    ),
  };
}

// ── Study Commitment ────────────────────────────────

export async function getStudyCommitmentData(): Promise<StudyCommitment | null> {
  const raw = asRecord(await fetchStudyCommitment());
  if (!raw.id) return null;
  return {
    id: toStringValue(raw.id),
    userId: toStringValue(raw.userId),
    dailyMinutes: toNumberValue(raw.dailyMinutes),
    freezeProtections: toNumberValue(raw.freezeProtections),
    freezeProtectionsUsed: toNumberValue(raw.freezeProtectionsUsed),
    isActive: toBooleanValue(raw.isActive),
  };
}

// ── Certificates ────────────────────────────────────

export async function getCertificatesData(): Promise<LearnerCertificate[]> {
  const raw = asArray(await fetchCertificates());
  return raw.map((c) => ({
    id: toStringValue(c.id),
    userId: toStringValue(c.userId),
    certificateType: normalizeCertificateType(c.certificateType),
    title: toStringValue(c.title),
    description: toStringValue(c.description),
    downloadUrl: toStringValue(c.downloadUrl),
    metadataJson: toNullableString(c.metadataJson),
    issuedAt: toStringValue(c.issuedAt),
  }));
}

function normalizeCertificateType(
  value: unknown,
): 'study_plan_complete' | 'mock_exam' | 'readiness_threshold' | 'streak_milestone' {
  const s = toStringValue(value, 'study_plan_complete').toLowerCase();
  if (s === 'mock_exam') return 'mock_exam';
  if (s === 'readiness_threshold') return 'readiness_threshold';
  if (s === 'streak_milestone') return 'streak_milestone';
  return 'study_plan_complete';
}

// ── Referral ────────────────────────────────────────

export async function getReferralData(): Promise<ReferralInfo> {
  const raw = asRecord(await fetchReferralInfo());
  return {
    referralCode: toNullableString(raw.referralCode),
    totalReferrals: toNumberValue(raw.totalReferrals),
    activatedReferrals: toNumberValue(raw.activatedReferrals),
    totalCreditsEarned: toNumberValue(raw.totalCreditsEarned),
    referrals: asArray(raw.referrals).map((r) => ({
      id: toStringValue(r.id),
      referredUserId: toStringValue(r.referredUserId),
      status: normalizeReferralStatus(r.status),
      createdAt: toStringValue(r.createdAt),
    })),
  };
}

function normalizeReferralStatus(
  value: unknown,
): 'pending' | 'activated' | 'rewarded' | 'expired' {
  const s = toStringValue(value, 'pending').toLowerCase();
  if (s === 'activated') return 'activated';
  if (s === 'rewarded') return 'rewarded';
  if (s === 'expired') return 'expired';
  return 'pending';
}

// ── Expert Annotation Templates ─────────────────────

export async function getAnnotationTemplatesData(
  params?: Parameters<typeof fetchAnnotationTemplates>[0],
): Promise<ExpertAnnotationTemplate[]> {
  const raw = asArray(await fetchAnnotationTemplates(params));
  return raw.map((t) => ({
    id: toStringValue(t.id),
    createdByExpertId: toStringValue(t.createdByExpertId),
    subtestCode: toStringValue(t.subtestCode),
    criterionCode: toStringValue(t.criterionCode),
    label: toStringValue(t.label),
    templateText: toStringValue(t.templateText),
    usageCount: toNumberValue(t.usageCount),
    isShared: toBooleanValue(t.isShared),
    createdAt: toStringValue(t.createdAt),
    updatedAt: toStringValue(t.updatedAt),
  }));
}
