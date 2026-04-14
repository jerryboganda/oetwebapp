// ── Score Guarantee ──────────────────────────────────

export interface ScoreGuaranteePledge {
  id: string;
  userId: string;
  subscriptionId: string;
  baselineScore: number;
  guaranteedImprovement: number;
  actualScore: number | null;
  status: 'active' | 'claim_submitted' | 'claim_approved' | 'claim_rejected' | 'expired';
  proofDocumentUrl: string | null;
  claimNote: string | null;
  reviewNote: string | null;
  activatedAt: string;
  expiresAt: string;
}

// ── Score Equivalences ──────────────────────────────

export interface ScoreEquivalenceRow {
  oetGrade: string;
  oetScore: string;
  ielts: string;
  pte: string;
  cefr: string;
}

export interface InstitutionRequirement {
  institution: string;
  country: string;
  minimumOetGrade: string;
  profession: string;
}

export interface ScoreEquivalencesData {
  equivalences: ScoreEquivalenceRow[];
  institutions: InstitutionRequirement[];
}

// ── Study Commitment ────────────────────────────────

export interface StudyCommitment {
  id: string;
  userId: string;
  dailyMinutes: number;
  freezeProtections: number;
  freezeProtectionsUsed: number;
  isActive: boolean;
}

// ── Certificates ────────────────────────────────────

export interface LearnerCertificate {
  id: string;
  userId: string;
  certificateType: 'study_plan_complete' | 'mock_exam' | 'readiness_threshold' | 'streak_milestone';
  title: string;
  description: string;
  downloadUrl: string;
  metadataJson: string | null;
  issuedAt: string;
}

// ── Referral ────────────────────────────────────────

export interface ReferralInfo {
  referralCode: string | null;
  totalReferrals: number;
  activatedReferrals: number;
  totalCreditsEarned: number;
  referrals: Array<{
    id: string;
    referredUserId: string;
    status: 'pending' | 'activated' | 'rewarded' | 'expired';
    createdAt: string;
  }>;
}

// ── Expert Annotation Templates ─────────────────────

export interface ExpertAnnotationTemplate {
  id: string;
  createdByExpertId: string;
  subtestCode: string;
  criterionCode: string;
  label: string;
  templateText: string;
  usageCount: number;
  isShared: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Learner Escalations (Disputes) ──────────────────

export type EscalationStatus = 'Pending' | 'InReview' | 'Resolved' | 'Rejected';

export interface LearnerEscalation {
  id: string;
  submissionId: string;
  reason: string;
  details: string;
  status: EscalationStatus;
  createdAt: string;
  updatedAt: string | null;
  resolutionNote: string | null;
}
