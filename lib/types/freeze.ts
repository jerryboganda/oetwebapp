// ── Freeze Feature Types ────────────────────────────────
// Shared between admin and learner freeze pages.

export interface FreezePolicy {
  isEnabled: boolean;
  selfServiceEnabled: boolean;
  approvalMode: string;
  accessMode: string;
  minDurationDays: number;
  maxDurationDays: number;
  entitlementPauseMode?: string;
}

export interface FreezeCounts {
  active: number;
  pending: number;
  scheduled: number;
  ended: number;
}

export interface FreezeRecord {
  id: string;
  userId: string;
  status: string;
  requestedAt: string | null;
  startedAt: string | null;
  endedAt: string | null;
  reason?: string | null;
}

export interface FreezeEligibility {
  eligible: boolean;
  reason?: string | null;
}

/** Shape returned by GET /v1/admin/freeze/overview and admin freeze mutation endpoints. */
export interface AdminFreezeOverview {
  policy: FreezePolicy;
  counts: FreezeCounts;
  records: FreezeRecord[];
}

/** Shape returned by GET /v1/freeze (learner freeze status). */
export interface LearnerFreezeStatus {
  policy: FreezePolicy;
  currentFreeze: FreezeRecord | null;
  eligibility: FreezeEligibility;
  history: FreezeRecord[];
}
