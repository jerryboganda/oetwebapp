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
  'mock_booking_created',
  'writing_pdf_download_requested',
  'writing_pdf_download_succeeded',
  'writing_pdf_download_failed',
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
  'expert_calibration_draft_saved',
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
  // Learner feature events
  'achievements_viewed',
  'leaderboard_viewed',
  'review_page_viewed',
  'vocabulary_home_viewed',
  'flashcards_viewed',
  'vocab_quiz_viewed',
  'vocab_browse_viewed',
  'vocab_term_detail_viewed',
  'vocab_added',
  'vocab_removed',
  'flashcard_rated',
  'vocab_quiz_started',
  'vocab_quiz_submitted',
  'vocab_daily_set_completed',
  'vocab_saved_from_reading',
  'vocab_saved_from_writing',
  'vocab_saved_from_speaking',
  'vocab_saved_from_listening',
  'vocab_saved_from_mock',
  'vocab_gloss_requested',
  'vocab_quiz_history_viewed',
  'grammar_page_viewed',
  'grammar_lesson_viewed',
  'grammar_topic_viewed',
  'grammar_recommendation_clicked',
  'grammar_recommendation_dismissed',
  'grammar_lesson_started',
  'grammar_lesson_completed',
  'grammar_lesson_mastered',
  'grammar_exercise_submitted',
  'grammar_paywall_shown',
  'grammar_paywall_upgrade_clicked',
  'grammar_draft_generated',
  'grammar_draft_rejected',
  'lessons_page_viewed',
  'video_lesson_viewed',
  'strategies_page_viewed',
  'strategy_guide_viewed',
  'community_page_viewed',
  'forum_thread_viewed',
  'new_thread_page_viewed',
  'study_groups_viewed',
  'pronunciation_page_viewed',
  'pronunciation_drill_viewed',
  'pronunciation_attempt_scored',
  'pronunciation_discrimination_started',
  'pronunciation_discrimination_completed',
  'tutoring_page_viewed',
  'exam_booking_page_viewed',
  // AI Conversation events
  'conversation_page_viewed',
  'conversation_started',
  'conversation_active',
  'conversation_ended',
  'conversation_results_viewed',
  'conversation_transcript_exported',
  // Writing Coach events
  'writing_coach_enabled',
  'writing_coach_suggestion_accepted',
  'writing_coach_suggestion_dismissed',
  // Writing module phase events
  'writing_reading_window_ended',
  // Writing drills (case-note selection trainer + letter-building practice)
  'writing_drill_index_viewed',
  'writing_drill_type_viewed',
  'writing_drill_started',
  'writing_drill_graded',
  // Writing revision — rewrite improvement scoring
  'writing_revision_score_computed',
  // Writing expert review — criterion-tagged inline annotations
  'writing_expert_annotation_added',
  // Writing weakness analytics dashboard
  'writing_analytics_viewed',
  // Speaking module — CBT at-home exam environment events
  'speaking_cbt_environment_confirmed',
  'speaking_cbt_paper_acknowledged',
  'speaking_cbt_paper_destroyed',
  'speaking_recording_consent_accepted',
  // Speaking module — strict exam-mode time enforcement
  // (Wave 2 of docs/SPEAKING-MODULE-PLAN.md). Fires once per attempt
  // when the audible 30-second warning plays.
  'speaking_time_warning',
  // Speaking module — mock-set orchestrator events (Wave 3 of
  // docs/SPEAKING-MODULE-PLAN.md). Two role-plays attempted as one mock.
  'speaking_mock_set_card_opened',
  'speaking_mock_set_started_orchestrator',
  'speaking_mock_set_card_opened',
  // Marketplace events
  'marketplace_page_viewed',
  'marketplace_submission_created',
  // Content discovery & hierarchy events
  'discover_page_viewed',
  'program_browser_viewed',
  'packages_page_viewed',
  // Admin analytics events
  'admin_cohort_analysis_viewed',
  'admin_content_analytics_viewed',
  'admin_content_effectiveness_viewed',
  'admin_credit_lifecycle_viewed',
  'admin_expert_efficiency_viewed',
  'admin_flag_toggled',
  'admin_free_tier_saved',
  'admin_free_tier_viewed',
  'admin_permissions_updated',
  'admin_playbook_viewed',
  'admin_roles_viewed',
  'admin_sla_health_viewed',
  'admin_subscription_health_viewed',
  'admin_view',
  // Billing events
  'billing_upgrade_path_viewed',
  // Certificate events
  'certificate_downloaded',
  'certificates_viewed',
  // Comparative analytics
  'comparative_analytics_viewed',
  // Exam & feedback events
  'exam_guide_viewed',
  'exam_simulation_viewed',
  'feedback_guide_viewed',
  // Expert extended events
  'expert_ai_prefill_viewed',
  'expert_ask_an_expert_viewed',
  'expert_mobile_review_opened',
  'expert_mobile_review_submitted',
  'expert_queue_priority_viewed',
  'expert_rubric_reference_viewed',
  'expert_verified_reply_posted',
  // Fluency events
  'fluency_timeline_viewed',
  // Interleaved practice
  'interleaved_practice_viewed',
  // Learner ask-an-expert events
  'learner_ask_an_expert_viewed',
  'learner_expert_question_posted',
  // Onboarding tour events
  'onboarding_tour_completed',
  'onboarding_tour_started',
  // Peer review events
  'peer_review_claimed',
  'peer_review_viewed',
  // Practice events
  'practice_page_viewed',
  // Phrase suggestions events
  'phrase_suggestion_resolved',
  'phrase_suggestions_check',
  // Private speaking events
  'private_speaking_booking_created',
  'private_speaking_page_viewed',
  // Quick session events
  'quick_session_completed',
  'quick_session_started',
  // Referral events
  'referral_code_copied',
  'referral_code_generated',
  'referral_page_viewed',
  'referral_shared_native',
  // Study & reminders events
  'reminders_preferences_saved',
  'score_calculator_viewed',
  'score_guarantee_activated',
  'score_guarantee_claim_submitted',
  'study_commitment_set',
  'study_plan_drift_viewed',
  // Test day events
  'test_day_checklist_toggle',
  // Community events
  'community_threads_viewed',
  'community_thread_viewed',
  'community_thread_created',
  'community_thread_updated',
  'community_thread_deleted',
  'community_reply_posted',
  'community_my_threads_viewed',
  'community_new_thread_viewed',
  'community_edit_thread_viewed',
  'admin_community_thread_deleted',
  // Expert console extended events
  'expert_onboarding_started',
  'expert_onboarding_completed',
  'expert_onboarding_profile_saved',
  'expert_onboarding_qualifications_saved',
  'expert_onboarding_schedule_saved',
  'expert_onboarding_rates_saved',
  'expert_schedule_exception_created',
  // Escalation events
  'escalation_submitted',
  // Achievements extended
  'streak_freeze_used',
  // Generic page view
  'page_viewed',
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
