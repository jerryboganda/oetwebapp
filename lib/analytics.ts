/**
 * Stub analytics service — logs to console in dev.
 * Replace with real analytics provider (Mixpanel, GA4, etc.) when ready.
 */

type EventProperties = Record<string, string | number | boolean | null | undefined>;

const TRACKED_EVENTS = [
  'onboarding_started',
  'onboarding_completed',
  'goals_saved',
  'diagnostic_started',
  'diagnostic_completed',
  'diagnostic_subtest_started',
  'diagnostic_subtest_completed',
  'task_started',
  'task_submitted',
  'evaluation_viewed',
  'revision_started',
  'revision_submitted',
  'mock_started',
  'mock_completed',
  'review_requested',
  'readiness_viewed',
  'progress_viewed',
  'plan_item_completed',
  'plan_item_skipped',
  'plan_item_rescheduled',
  'subscription_started',
  'subscription_changed',
  'module_entry',
  'content_view',
  // Expert Console events
  'review_queue_viewed',
  'review_started',
  'review_draft_saved',
  'review_submitted',
  'calibration_viewed',
  'calibration_case_started',
  'calibration_case_completed',
  'expert_schedule_saved',
  'expert_metrics_viewed',
  'learner_profile_viewed',
  // Admin / CMS events
  'admin_content_created',
  'admin_content_updated',
  'admin_content_published',
  'admin_content_archived',
  'admin_revision_restored',
  'admin_taxonomy_changed',
  'admin_criteria_changed',
  'admin_ai_config_changed',
  'admin_flag_changed',
  'admin_user_role_changed',
  'admin_billing_action',
  'admin_audit_log_viewed',
  'admin_review_ops_action',
  'admin_quality_analytics_viewed',
] as const;

export type AnalyticsEvent = typeof TRACKED_EVENTS[number];

class AnalyticsService {
  private enabled = true;

  track(event: AnalyticsEvent, properties?: EventProperties) {
    if (!this.enabled) return;

    const enriched = {
      ...properties,
      timestamp: new Date().toISOString(),
      deviceType: typeof window !== 'undefined' && window.innerWidth < 768 ? 'mobile' : 'desktop',
    };

    if (process.env.NODE_ENV === 'development') {
      console.log(`[Analytics] ${event}`, enriched);
    }
    // In production, send to analytics provider here
  }

  identify(userId: string, traits?: EventProperties) {
    if (process.env.NODE_ENV === 'development') {
      console.log(`[Analytics] identify`, { userId, ...traits });
    }
  }

  disable() { this.enabled = false; }
  enable() { this.enabled = true; }
}

export const analytics = new AnalyticsService();
