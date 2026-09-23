import { apiClient } from '@/lib/api';

// Free Mocks (owner 2026-09-22): the learner's ONE free AI-graded Writing and
// ONE free AI-graded Speaking sample. The server decides what is free (the
// profession's designated item, once per learner) — this client only asks what
// is on offer and never sends a "free" flag with any start/submit request.

export type FreeSampleSubtest = 'writing' | 'speaking';

/**
 * 23 Sep 2026 contract (2 successful graded results per subtest, same item):
 * available = 0 successes · retry_available = 1 success · in_progress = a use
 * is grading · completed = 2 successes · unavailable = claim pinned elsewhere /
 * no eligible content. `used` is the pre-contract value, kept until the
 * launcher stops reading it.
 */
export type FreeSampleState =
  | 'available'
  | 'retry_available'
  | 'in_progress'
  | 'completed'
  | 'unavailable'
  | 'used';

export interface FreeSampleOption {
  /** Normalised profession id (lower-case, hyphenated). */
  professionId: string;
  /** Writing: scenario id. Speaking: role-play card id. */
  contentId: string;
  state: FreeSampleState;
  /** App route that opens the sample (server-built). */
  route: string;
  limit?: number;
  successfulCount?: number;
  remaining?: number;
  lastResultRoute?: string | null;
  lastSubmissionId?: string | null;
}

/**
 * Every profession that has a free sample on offer right now (only the
 * learner's claimed profession once they have started). An empty list means
 * "nothing to show" — the feature is off or no profession has live content yet.
 */
export const listFreeSamples = (subtest: FreeSampleSubtest) =>
  apiClient.get<FreeSampleOption[]>(`/v1/free-samples/${subtest}`);
