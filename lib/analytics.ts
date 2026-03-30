import { ensureFreshAccessToken } from './auth-client';
import { env } from './env';

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
  'expert_dashboard_viewed',
  'expert_learners_viewed',
  'review_started',
  'review_draft_saved',
  'review_submitted',
  'calibration_viewed',
  'calibration_case_started',
  'calibration_case_completed',
  'expert_calibration_case_viewed',
  'expert_calibration_case_submitted',
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

type AnalyticsProvider = (event: string, properties?: EventProperties) => Promise<void> | void;

const MAX_BUFFER_SIZE = 1000;
const ANALYTICS_EVENTS_PATH = '/v1/analytics/events';

class AnalyticsService {
  private enabled = true;
  private provider: AnalyticsProvider | null = null;
  private buffer: Array<{ event: AnalyticsEvent; properties: EventProperties }> = [];
  private transportInitialized = false;

  setProvider(provider: AnalyticsProvider) {
    this.provider = provider;
    // Flush buffered events
    for (const entry of this.buffer) {
      void Promise.resolve(provider(entry.event, entry.properties)).catch(() => undefined);
    }
    this.buffer = [];
  }

  initializeBrowserTransport() {
    if (this.transportInitialized || typeof window === 'undefined') {
      return;
    }

    this.transportInitialized = true;
    this.setProvider(async (event, properties) => {
      const accessToken = await ensureFreshAccessToken();
      if (!accessToken) {
        return;
      }

      const response = await fetch(`${env.apiBaseUrl}${ANALYTICS_EVENTS_PATH}`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${accessToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          eventName: event,
          properties,
        }),
      });

      if (!response.ok) {
        throw new Error(`Analytics event submission failed with status ${response.status}.`);
      }
    });
  }

  track(event: AnalyticsEvent, properties?: EventProperties) {
    if (!this.enabled) return;

    const enriched: EventProperties = {
      ...properties,
      timestamp: new Date().toISOString(),
      deviceType: typeof window !== 'undefined' && window.innerWidth < 768 ? 'mobile' : 'desktop',
    };

    if (this.provider) {
      void Promise.resolve(this.provider(event, enriched)).catch(() => undefined);
    } else {
      // Buffer events until a provider is set
      if (this.buffer.length < MAX_BUFFER_SIZE) {
        this.buffer.push({ event, properties: enriched });
      }
    }
  }

  identify(userId: string, traits?: EventProperties) {
    void userId;
    void traits;
  }

  disable() { this.enabled = false; }
  enable() { this.enabled = true; }
}

export const analytics = new AnalyticsService();

export function initializeAnalyticsTransport() {
  analytics.initializeBrowserTransport();
}
