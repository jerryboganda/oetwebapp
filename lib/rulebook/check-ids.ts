/**
 * ============================================================================
 * Rulebook Check-ID Registry
 * ============================================================================
 *
 * A single source of truth mapping each `RuleKind` to the set of `checkId`s
 * that have a backing deterministic detector / enforcer. Before this module,
 * check-id literals were scattered across the coverage matrix, the engines and
 * the tests, so it was easy for a rule to claim a `checkId` that no detector
 * actually implements.
 *
 * The generic coverage-matrix validator (`coverage-matrix.ts`) and the
 * browser-safe dashboard classifier (`coverage.ts`) both ask this registry the
 * one question that matters: *"is this checkId backed by a real enforcer for
 * this kind?"* — so a rule can never silently claim deterministic enforcement
 * it does not have.
 *
 * This module RE-EXPORTS the existing source-of-truth sets; it never
 * duplicates them. Add a new kind's detector set here when its engine lands
 * (Phase 2 adds `listening`/`reading` authoring detectors).
 *
 * Browser-safe: imports only static rule modules (no `node:fs`/`registry.ts`),
 * so the admin dashboard can import it client-side.
 * ============================================================================
 */

import type { RuleKind } from './types';
import { SUPPORTED_WRITING_CHECK_IDS } from './writing-rules';
import { SUPPORTED_SPEAKING_CHECK_IDS } from './speaking-rules';
import { SUPPORTED_LISTENING_CHECK_IDS } from './listening-rules';
import { SUPPORTED_READING_CHECK_IDS } from './reading-rules';
import { LISTENING_EXAM_MODE_ENFORCERS, READING_EXAM_MODE_ENFORCERS } from './exam-mode-rules';

/** Writing rules with a deterministic detector in `writing-rules.ts`. */
export const WRITING_CHECK_IDS: ReadonlySet<string> = new Set(SUPPORTED_WRITING_CHECK_IDS);

/** Speaking rules with a deterministic detector in `speaking-rules.ts`. */
export const SPEAKING_CHECK_IDS: ReadonlySet<string> = new Set(SUPPORTED_SPEAKING_CHECK_IDS);

/** Listening exam-mode rules with a backing enforcer in `exam-mode-rules.ts`. */
export const LISTENING_EXAM_MODE_CHECK_IDS: ReadonlySet<string> = new Set(
  Object.keys(LISTENING_EXAM_MODE_ENFORCERS),
);

/** Reading exam-mode rules with a backing enforcer in `exam-mode-rules.ts`. */
export const READING_EXAM_MODE_CHECK_IDS: ReadonlySet<string> = new Set(
  Object.keys(READING_EXAM_MODE_ENFORCERS),
);

/** Listening authoring rules with a deterministic detector in `listening-rules.ts`. */
export const LISTENING_AUTHORING_CHECK_IDS: ReadonlySet<string> = new Set(SUPPORTED_LISTENING_CHECK_IDS);

/** Reading authoring rules with a deterministic detector in `reading-rules.ts`. */
export const READING_AUTHORING_CHECK_IDS: ReadonlySet<string> = new Set(SUPPORTED_READING_CHECK_IDS);

const EMPTY: ReadonlySet<string> = new Set<string>();

/**
 * RuleKind → supported deterministic check-ids. Kinds without a deterministic
 * engine yet map to the empty set (NOT missing) so callers can ask about any
 * kind without a guard. Phase 2 fills in `listening`/`reading` authoring sets.
 */
export const CHECK_ID_REGISTRY: Readonly<Record<RuleKind, ReadonlySet<string>>> = Object.freeze({
  writing: WRITING_CHECK_IDS,
  speaking: SPEAKING_CHECK_IDS,
  'listening-exam-mode': LISTENING_EXAM_MODE_CHECK_IDS,
  'reading-exam-mode': READING_EXAM_MODE_CHECK_IDS,
  listening: LISTENING_AUTHORING_CHECK_IDS,
  reading: READING_AUTHORING_CHECK_IDS,
  // No deterministic detector set registered yet:
  grammar: EMPTY,
  pronunciation: EMPTY,
  vocabulary: EMPTY,
  conversation: EMPTY,
  remediation: EMPTY,
}) as Record<RuleKind, ReadonlySet<string>>;

/** The deterministic check-ids supported for a kind (empty set if none yet). */
export function supportedCheckIds(kind: RuleKind): ReadonlySet<string> {
  return CHECK_ID_REGISTRY[kind] ?? EMPTY;
}

/** True when `checkId` has a backing deterministic detector/enforcer for `kind`. */
export function isSupportedCheckId(kind: RuleKind, checkId: string): boolean {
  return supportedCheckIds(kind).has(checkId);
}
