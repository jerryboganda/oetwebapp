// Custom k6 metrics. Names and tags here are the contract with thresholds.mjs and the report
// generator (tests/load/report/k6-load-report.mjs): change them together.

import { Counter, Rate, Trend } from 'k6/metrics';

// Reliability. `phase` (and `stage` during capacity holds) are tagged on every sample.
export const unexpectedFailure = new Rate('oet_unexpected_failure');
export const collapse = new Rate('oet_collapse');
export const establishedOk = new Rate('oet_established_ok');
export const gracefulShed = new Counter('oet_graceful_shed');
export const domainReject = new Counter('oet_domain_reject');

// Per-class / per-endpoint status counts (the contract probe) and per-class request counts.
export const statusTotal = new Counter('oet_status_total');
export const classTotal = new Counter('oet_class_total');

// Correctness: all of these must stay at zero.
export const lostAckSave = new Counter('oet_lost_ack_save');
export const idempotencyViolation = new Counter('oet_idempotency_violation');
export const creditConsumedWhileQueued = new Counter('oet_credit_consumed_while_queued');
export const timerStartedWhileQueued = new Counter('oet_timer_started_while_queued');

// Coverage.
export const flowStarted = new Counter('oet_flow_started');
export const flowCompleted = new Counter('oet_flow_completed');
export const flowSkipped = new Counter('oet_flow_skipped');
export const sessionsStarted = new Counter('oet_sessions_started');
export const sessionsEnded = new Counter('oet_sessions_ended');
export const savesAcked = new Counter('oet_saves_acked');
export const savesVerified = new Counter('oet_saves_verified');

// Realtime.
export const signalrConnectFailed = new Rate('oet_signalr_connect_failed');
export const signalrReconnects = new Counter('oet_signalr_reconnects');
export const signalrMessages = new Counter('oet_signalr_messages');
export const cueSent = new Counter('oet_cue_sent');
export const cueRoundtripMs = new Trend('oet_cue_roundtrip_ms', true);

// Live AI speaking and grading (informational unless thresholds.mjs says otherwise).
export const aiAssessMs = new Trend('oet_ai_assess_ms', true);
export const liveQueueWaitMs = new Trend('oet_live_queue_wait_ms', true);
export const liveQueuedSessions = new Counter('oet_live_queued_sessions');
