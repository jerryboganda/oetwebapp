// ── Freeze Feature Types ────────────────────────────────
// Shared between admin and learner freeze pages.

export interface FreezePolicy {
  id?: string;
  isEnabled: boolean;
  selfServiceEnabled: boolean;
  approvalMode: string;
  accessMode: string;
  minDurationDays: number;
  maxDurationDays: number;
  allowScheduling: boolean;
  entitlementPauseMode?: string;
  requireReason: boolean;
  requireInternalNotes: boolean;
  allowActivePaid: boolean;
  allowGracePeriod: boolean;
  allowTrial: boolean;
  allowComplimentary: boolean;
  allowCancelled: boolean;
  allowExpired: boolean;
  allowReviewOnly: boolean;
  allowPastDue: boolean;
  allowSuspended: boolean;
  policyNotes?: string | null;
  eligibilityReasonCodesJson?: string | null;
  updatedAt?: string | null;
  version?: number;
}

export interface FreezeCounts {
  active: number;
  pending: number;
  scheduled: number;
  cancelled?: number;
  rejected?: number;
  ended: number;
}

export interface FreezeRecord {
  id: string;
  userId: string;
  status: string;
  requestedAt: string | null;
  scheduledStartAt?: string | null;
  startedAt: string | null;
  endedAt: string | null;
  durationDays?: number | null;
  reason?: string | null;
  internalNotes?: string | null;
  isSelfService?: boolean;
  entitlementConsumed?: boolean;
  entitlementReset?: boolean;
  rejectionReason?: string | null;
  endReason?: string | null;
  cancellationReason?: string | null;
  updatedAt?: string | null;
}

export interface FreezeEligibility {
  eligible: boolean;
  canRequest?: boolean;
  canSchedule?: boolean;
  maxDurationDays?: number;
  minDurationDays?: number;
  subscriptionState?: string;
  reasonCodes?: string[];
  policyVersion?: number;
  reason?: string | null;
}

export interface FreezeEntitlement {
  id: string;
  userId: string;
  freezeRecordId?: string | null;
  consumedAt?: string | null;
  resetAt?: string | null;
  resetByAdminId?: string | null;
  resetByAdminName?: string | null;
  resetReason?: string | null;
  used: boolean;
}

/** Shape returned by GET /v1/admin/freeze/overview and admin freeze mutation endpoints. */
export interface AdminFreezeOverview {
  generatedAt?: string;
  policy: FreezePolicy;
  counts: FreezeCounts;
  records: FreezeRecord[];
}

/** Shape returned by GET /v1/freeze (learner freeze status). */
export interface LearnerFreezeStatus {
  policy: FreezePolicy;
  currentFreeze: FreezeRecord | null;
  entitlement?: FreezeEntitlement | null;
  eligibility: FreezeEligibility;
  history: FreezeRecord[];
}
