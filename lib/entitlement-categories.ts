// ============================================================================
// Entitlement Category System — Server-Side Enforcement Contract
// ============================================================================
//
// Per docs/product-strategy/07_subscription_pricing_and_entitlements_strategy.md:
// "All entitlement enforcement MUST be server-side. Client-side gating is for UX
// convenience only and MUST NOT be trusted for security."
//
// This module defines the entitlement category taxonomy used by both frontend
// (for UI gating and copy) and backend (for strict enforcement). The frontend
// copy MUST match the server-side rules — drift between client and server
// entitlement logic is a critical bug.
//
// OET Packaging Tiers (Document 07):
//   Free  → diagnostics only, limited AI eval, community
//   Core  → full practice + AI eval, 2 mocks/month, no expert review
//   Plus  → Core + 4 expert reviews/month, unlimited mocks, analytics
//   Review→ Plus + unlimited reviews, 1-on-1 coaching, WhatsApp support
// ============================================================================

/** Canonical entitlement categories. */
export type EntitlementCategory =
  | 'diagnostic'
  | 'practice_questions'
  | 'ai_evaluation'
  | 'mock_exams'
  | 'expert_review'
  | 'speaking_coaching'
  | 'personal_study_coach'
  | 'priority_support'
  | 'analytics_readiness'
  | 'compare_attempts'
  | 'content_marketplace_submit'
  | 'score_guarantee';

/** Tier code (matches billing plan codes). */
export type OetTierCode = 'free' | 'core' | 'plus' | 'review';

/** Human-readable entitlement labels for learner-facing copy. */
export const ENTITLEMENT_LABELS: Record<EntitlementCategory, string> = {
  diagnostic: 'Diagnostic tests',
  practice_questions: 'Practice questions',
  ai_evaluation: 'AI evaluation & feedback',
  mock_exams: 'Mock exams',
  expert_review: 'Expert human review',
  speaking_coaching: '1-on-1 speaking coaching',
  personal_study_coach: 'Personal study coach',
  priority_support: 'Priority support',
  analytics_readiness: 'Readiness analytics & blockers',
  compare_attempts: 'Compare-attempt analytics',
  content_marketplace_submit: 'Submit content to marketplace',
  score_guarantee: 'Score guarantee program',
};

/**
 * Which entitlements are included in each OET tier.
 * TRUE = included, FALSE = not included, NUMBER = included with a monthly limit.
 *
 * This table is the SINGLE SOURCE OF TRUTH for tier-to-entitlement mapping.
 * Both frontend and backend should reference this contract.
 */
export const TIER_ENTITLEMENTS: Readonly<
  Record<OetTierCode, Partial<Record<EntitlementCategory, boolean | number>>>
> = {
  free: {
    diagnostic: true,
    practice_questions: false, // limited, not unlimited
    ai_evaluation: 1, // 1 per month
    mock_exams: false,
    expert_review: false,
    speaking_coaching: false,
    personal_study_coach: false,
    priority_support: false,
    analytics_readiness: false,
    compare_attempts: false,
    content_marketplace_submit: true,
    score_guarantee: false,
  },
  core: {
    diagnostic: true,
    practice_questions: true,
    ai_evaluation: true,
    mock_exams: 2, // 2 per month
    expert_review: false,
    speaking_coaching: false,
    personal_study_coach: false,
    priority_support: false,
    analytics_readiness: true,
    compare_attempts: false,
    content_marketplace_submit: true,
    score_guarantee: false,
  },
  plus: {
    diagnostic: true,
    practice_questions: true,
    ai_evaluation: true,
    mock_exams: true, // unlimited
    expert_review: 4, // 4 per month
    speaking_coaching: false,
    personal_study_coach: false,
    priority_support: true, // email
    analytics_readiness: true,
    compare_attempts: true,
    content_marketplace_submit: true,
    score_guarantee: true,
  },
  review: {
    diagnostic: true,
    practice_questions: true,
    ai_evaluation: true,
    mock_exams: true, // unlimited
    expert_review: true, // unlimited
    speaking_coaching: 2, // 2 sessions per month
    personal_study_coach: true,
    priority_support: true, // WhatsApp / phone
    analytics_readiness: true,
    compare_attempts: true,
    content_marketplace_submit: true,
    score_guarantee: true,
  },
};

/**
 * Check whether a tier includes an entitlement.
 * Returns true if included (even if limited by number).
 */
export function tierHasEntitlement(
  tier: OetTierCode,
  category: EntitlementCategory,
): boolean {
  const value = TIER_ENTITLEMENTS[tier]?.[category];
  if (value === undefined) return false;
  if (typeof value === 'boolean') return value;
  return value > 0;
}

/**
 * Get the numeric limit for an entitlement in a tier.
 * Returns null if unlimited (true boolean), 0 if not included, or the limit.
 */
export function tierEntitlementLimit(
  tier: OetTierCode,
  category: EntitlementCategory,
): number | null {
  const value = TIER_ENTITLEMENTS[tier]?.[category];
  if (value === undefined || value === false) return 0;
  if (value === true) return null; // unlimited
  return value;
}

/** Human-readable description of a tier's entitlement limit for display. */
export function formatEntitlementLimit(
  tier: OetTierCode,
  category: EntitlementCategory,
): string {
  const limit = tierEntitlementLimit(tier, category);
  if (limit === null) return 'Unlimited';
  if (limit === 0) return 'Not included';
  if (limit === 1) return '1 per month';
  return `${limit} per month`;
}

/**
 * Exam-family-specific entitlement adjustments.
 * IELTS and PTE share the same backbone but may have reduced mock/test coverage
 * until their dedicated engines are fully built.
 */
export const EXAM_FAMILY_ENTITLEMENT_OVERRIDES: Readonly<
  Partial<Record<string, Partial<Record<EntitlementCategory, boolean | number>>>>
> = {
  ielts: {
    // IELTS mocks are limited while the exam-family engine is still expanding
    mock_exams: 2,
    // Speaking coaching uses OET rubric until IELTS-specific coaching is ready
    speaking_coaching: false,
  },
  pte: {
    // PTE is deferred — most productive-skill entitlements are disabled
    ai_evaluation: false,
    mock_exams: false,
    expert_review: false,
    speaking_coaching: false,
    analytics_readiness: false,
    compare_attempts: false,
    score_guarantee: false,
  },
};

/**
 * Get effective entitlements for a (tier, examFamily) combination.
 * Applies exam-family overrides on top of the base tier mapping.
 */
export function effectiveEntitlements(
  tier: OetTierCode,
  examFamily: string,
): Record<EntitlementCategory, boolean | number> {
  const base = { ...TIER_ENTITLEMENTS[tier] } as Record<EntitlementCategory, boolean | number>;
  const overrides = EXAM_FAMILY_ENTITLEMENT_OVERRIDES[examFamily.toLowerCase()];
  if (!overrides) return base;
  for (const [key, value] of Object.entries(overrides)) {
    base[key as EntitlementCategory] = value as boolean | number;
  }
  return base;
}
