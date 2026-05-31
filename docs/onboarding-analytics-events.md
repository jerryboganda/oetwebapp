# Onboarding — Analytics Events

All events flow through the existing layer: `analytics.track(name, props)` (`lib/analytics.ts`) → `POST /v1/analytics/events` → `AnalyticsEventRecord`. New names are added to the `TRACKED_EVENTS` union (type-checked). The backend accepts any name ≤ 64 chars (no allow-list to maintain). `timestamp` and `deviceType` (mobile/desktop) are auto-attached.

> Note: tour completion is **persisted** separately via `/v1/onboarding/tours` (durable flags). These events are the analytics funnel; the server does not duplicate tour events to avoid double counting.

## Events added

| Event | When | Key properties |
| --- | --- | --- |
| `tour_started` | A tour begins (auto or manual) | `tourId, role, route` |
| `tour_replayed` | A tour started from the Help center | `tourId, role, route` |
| `tour_step_viewed` | Each step is highlighted | `tourId, stepId, stepIndex, role, route` |
| `tour_step_completed` | (reserved) advancing past a step | `tourId, stepId, stepIndex` |
| `tour_completed` | Final step finished ("Finish") | `tourId, role, route, stepCount` |
| `tour_skipped` | Tour closed early (X/overlay/ESC) | `tourId, atStepIndex, role, route` |
| `tour_dismissed` | A contextual tip dismissed | `tourId (tip id), role` |
| `checklist_item_completed` | A dashboard checklist item transitions to done | `itemId, role` |
| `help_center_opened` | Help ("?") drawer opened | `role, route` |
| `welcome_exam_mode_set` | Exam mode saved in `/goals` | `mode` |
| `welcome_confidence_set` | Confidence saved in `/goals` | `level` |
| `first_practice_started` | First practice attempt (reserved for module wiring) | `module` |
| `first_mock_started` | First mock attempt (reserved) | `module` |
| `first_submission_completed` | First Writing/Speaking submission (reserved) | `module` |
| `first_feedback_viewed` | First tutor feedback opened (reserved) | `module` |

## Reused existing events

`onboarding_started`, `onboarding_completed`, `onboarding_tour_started`, `onboarding_tour_completed`, `goals_saved`, `module_entry`, `page_viewed`.

## Suggested funnels

- **Activation:** `onboarding_started` → `goals_saved` → `tour_completed (learner-dashboard)` → `first_practice_started`.
- **Tour effectiveness:** `tour_started` → `tour_step_viewed` (drop-off by `stepIndex`) → `tour_completed` vs `tour_skipped`, segmented by `tourId`/`role`/`deviceType`.
- **Checklist:** `checklist_item_completed` by `itemId` over first 7 days.

## Implementation pointers

Typed wrappers: `lib/onboarding/tour-events.ts`. The provider emits start/step/complete/skip; `OnboardingChecklist` emits `checklist_item_completed` only on a real not-done→done transition (no false positives on load); `/goals` emits the `welcome_*` events; `TourLauncher` emits `help_center_opened`. The `first_*` events are declared and ready to fire from the module attempt/submission/feedback flows.
