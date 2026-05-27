/**
 * Speaking module analytics event catalog (Agent P spec).
 *
 * This is the single source of truth for every analytics event fired by the
 * OET Speaking module — warm-up, prep, role-play, AI assessment, tutor
 * assessment, micro-drills, course pathway, recordings, mock orchestrator,
 * and LiveKit live rooms.
 *
 * - The const map `SPEAKING_EVENTS` declares each event name and the exact
 *   set of properties it accepts (as a `readonly` tuple of literal strings).
 * - The union type `SpeakingEventName` is derived from the map keys so any
 *   `trackSpeaking(name, ...)` call site is checked at compile time.
 * - The helper `trackSpeaking` wraps the global `analytics.track` from
 *   `@/lib/analytics`, narrowing the `properties` parameter to the exact
 *   keys declared for the given event.
 *
 * For human-readable definitions, when each event fires, and which
 * dashboards / experiments consume it, see
 * `docs/analytics/speaking-events.md`.
 *
 * NOTE: Some event names below are not yet registered in
 * `lib/analytics.ts#TRACKED_EVENTS`. Calling `analytics.track` with an
 * unregistered name will trigger a TypeScript error because
 * `AnalyticsEvent` is a union of registered names. To keep this catalog
 * usable today without forcing edits to the existing analytics file
 * (which other agents own), `trackSpeaking` casts the event name through
 * `unknown` when invoking the underlying `analytics.track`. The runtime
 * behaviour is unchanged — the backend `/v1/analytics/events` endpoint
 * accepts any event name string.
 */

import { analytics, type EventProperties } from '@/lib/analytics';

/**
 * Catalog of Speaking-module analytics events. Each entry's `props` field
 * is a tuple of property names that callers MUST supply when tracking
 * that event. Adding a new event to this map automatically makes it
 * available to `trackSpeaking` and to `SpeakingEventName`.
 */
export const SPEAKING_EVENTS = {
  // ---------------------------------------------------------------------------
  // Module entry + profession gate
  // ---------------------------------------------------------------------------

  /** Learner lands on the Speaking module entry page (`/speaking`). */
  module_entry: { props: ['from'] as const },

  /**
   * Learner sets / changes their active profession via the
   * `/speaking/select-profession` gate.
   */
  profession_set: { props: ['professionId'] as const },

  // ---------------------------------------------------------------------------
  // Session lifecycle: warm-up
  // ---------------------------------------------------------------------------

  /** Warm-up conversation begins (state machine: `WarmUp` entered). */
  warmup_started: { props: ['sessionId'] as const },

  /** Warm-up finished — either by timer expiry or learner-clicked "Continue". */
  warmup_finished: { props: ['sessionId', 'durationSeconds'] as const },

  // ---------------------------------------------------------------------------
  // Session lifecycle: prep
  // ---------------------------------------------------------------------------

  /** Learner enters the 3-minute role-play card preparation phase. */
  prep_started: { props: ['sessionId', 'cardId'] as const },

  // ---------------------------------------------------------------------------
  // Session lifecycle: role-play (AI patient or live tutor)
  // ---------------------------------------------------------------------------

  /** Role-play begins — `Active` state, mic/cam wired, timer started. */
  roleplay_started: { props: ['sessionId', 'cardId'] as const },

  /**
   * Hub broadcasts `TimeNearlyUp` (30s remaining). Used to verify the
   * audible warning is reaching learners.
   */
  roleplay_time_nearly_up: { props: ['sessionId', 'secondsLeft'] as const },

  /**
   * Role-play ended. `reason` is one of `'timer'`, `'manual'`,
   * `'disconnect'`, `'tutor_ended'`. See spec for `SpeakingSession.End*Reason`.
   */
  roleplay_ended: {
    props: ['sessionId', 'durationSeconds', 'reason'] as const,
  },

  // ---------------------------------------------------------------------------
  // Assessment surfaces (AI advisory + tutor authoritative)
  // ---------------------------------------------------------------------------

  /** AI assessment view rendered to the learner. `estimatedBand` is the readiness band. */
  ai_assessment_viewed: {
    props: ['sessionId', 'estimatedBand'] as const,
  },

  /** Tutor (human) assessment view rendered to the learner. */
  tutor_assessment_viewed: { props: ['sessionId', 'estimatedBand'] as const },

  // ---------------------------------------------------------------------------
  // Micro-drills (course pathway recommendations)
  // ---------------------------------------------------------------------------

  /** Drill recorder opens for a specific drill. `criterion` is the OET criterion tag. */
  drill_started: { props: ['drillId', 'criterion'] as const },

  /** Drill scored — `score` is the 0–9 single-criterion score for the attempt. */
  drill_scored: { props: ['drillId', 'score'] as const },

  // ---------------------------------------------------------------------------
  // Course pathway (weakest-criterion-first practice queue)
  // ---------------------------------------------------------------------------

  /** Learner opens `/speaking/pathway`. */
  pathway_viewed: { props: [] as const },

  // ---------------------------------------------------------------------------
  // Recordings (self-management / GDPR)
  // ---------------------------------------------------------------------------

  /**
   * Learner deletes one of their own recordings from
   * `/speaking/recordings`. Backend writes the matching `AuditEvent`.
   */
  recording_deleted: { props: ['recordingId'] as const },

  speaking_pdf_download_requested: { props: ['resultId'] as const },
  speaking_pdf_download_succeeded: { props: ['resultId'] as const },
  speaking_pdf_download_failed: { props: ['resultId', 'errorName'] as const },

  // ---------------------------------------------------------------------------
  // Two-role-play mock orchestrator
  // ---------------------------------------------------------------------------

  /** Learner starts a Speaking Mock Set (`/speaking/mocks/[id]`). */
  mock_started: { props: ['mockSetId'] as const },

  /** Learner lands on the 60-second bridge between role-play 1 and role-play 2. */
  mock_bridge_viewed: { props: ['mockSetId'] as const },

  /**
   * Combined estimated band is computed and persisted on
   * `SpeakingMockSession`. Fired from the results page.
   */
  mock_aggregated: {
    props: ['mockSetId', 'estimatedBand'] as const,
  },

  // ---------------------------------------------------------------------------
  // LiveKit live tutor rooms
  // ---------------------------------------------------------------------------

  /** Participant joined the LiveKit room. `role` is `'learner' | 'tutor'`. */
  live_room_joined: {
    props: ['liveRoomId', 'role'] as const,
  },

  /**
   * Live room ended. `reason` is one of `'tutor_ended'`, `'learner_ended'`,
   * `'timer'`, `'disconnect'`, `'admin_kicked'`.
   */
  live_room_ended: {
    props: ['liveRoomId', 'durationSeconds', 'reason'] as const,
  },

  /**
   * Tutor raised an interlocutor cue from the tutor cue panel. `cueId`
   * maps to `InterlocutorScript.Prompt1..3` / `OpeningResponse` /
   * `ClosingCue`.
   */
  cue_raised: {
    props: ['liveRoomId', 'cueId'] as const,
  },
} as const;

/**
 * Discriminated union of every event name in {@link SPEAKING_EVENTS}.
 * Used by `trackSpeaking` to enforce that callers only ship registered
 * Speaking events. Adding a key to the catalog automatically widens this
 * union.
 */
export type SpeakingEventName = keyof typeof SPEAKING_EVENTS;

/**
 * Extract the literal union of property names declared for a given event.
 *
 * @example
 *   type Props = SpeakingEventPropName<'warmup_finished'>;
 *   // => 'sessionId' | 'durationSeconds'
 */
export type SpeakingEventPropName<TName extends SpeakingEventName> =
  (typeof SPEAKING_EVENTS)[TName]['props'][number];

/**
 * Strictly-typed property bag for the given event. Every declared key is
 * required; the value type matches the global `EventProperties` type
 * (string | number | boolean | null | undefined). Events with no
 * properties accept an empty object (or `undefined`).
 */
export type SpeakingEventProps<TName extends SpeakingEventName> =
  SpeakingEventPropName<TName> extends never
    ? Record<string, never>
    : { [K in SpeakingEventPropName<TName>]: EventProperties[string] };

/**
 * Type-safe wrapper around `analytics.track` for Speaking-module events.
 *
 * Usage:
 * ```ts
 * trackSpeaking('warmup_started', { sessionId: '01HX…' });
 * trackSpeaking('roleplay_ended', {
 *   sessionId: '01HX…',
 *   durationSeconds: 312,
 *   reason: 'timer',
 * });
 * trackSpeaking('pathway_viewed', {});
 * ```
 *
 * Each call is forwarded to the shared `analytics` singleton, which
 * handles buffering, enrichment (timestamp + deviceType), and transport
 * to `POST /v1/analytics/events`.
 */
export function trackSpeaking<TName extends SpeakingEventName>(
  name: TName,
  props: SpeakingEventProps<TName>,
): void {
  // The shared `analytics.track` is typed to a `union` of registered
  // analytics event names. Speaking events are validated against
  // `SPEAKING_EVENTS` here, so we widen via `unknown` before forwarding.
  const trackFn = analytics.track.bind(analytics) as (
    event: string,
    properties?: EventProperties,
  ) => void;
  trackFn(name as string, props as EventProperties);
}

/**
 * Static list of every Speaking event name, exported for tooling
 * (lint rules, dashboard generators, completeness tests).
 */
export const SPEAKING_EVENT_NAMES: readonly SpeakingEventName[] =
  Object.keys(SPEAKING_EVENTS) as SpeakingEventName[];
