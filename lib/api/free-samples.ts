import { apiClient } from '@/lib/api';

// Free Mocks (owner 2026-09-22, retry addendum 2026-09-23): each learner gets
// TWO successful AI-graded results per subtest on the SAME designated free item
// of their own profession (Speaking: attempt + retry of the same card; Writing:
// graded submission + revise & resubmit of the same letter). The server decides
// what is free and counts successes — this client only asks what is on offer
// and never sends a "free" flag with any start/submit request.

export type FreeSampleSubtest = 'writing' | 'speaking';

/**
 * available = 0 successes · retry_available = 1 success, nothing in flight ·
 * in_progress = a use is being graded (Writing route: its grading page) ·
 * grading_failed = the latest use's grading failed and nothing newer exists
 * (Writing route: that SAME letter's grading page, where Retry costs no free use) ·
 * completed = 2 successes · unavailable = claim pinned to another profession,
 * or no eligible content.
 */
export type FreeSampleState = 'available' | 'retry_available' | 'in_progress' | 'grading_failed' | 'completed' | 'unavailable';

export interface FreeSampleOption {
  /** Normalised profession id (lower-case, hyphenated). */
  professionId: string;
  /** Writing: scenario id. Speaking: role-play card id. Null when unavailable. */
  contentId: string | null;
  state: FreeSampleState;
  /** App route that opens the sample (server-built). Writing retry: the revise route. Null when unavailable. */
  route: string | null;
  /** Successful results allowed (2). */
  limit: number;
  successfulCount: number;
  remaining: number;
  /** Result page of the latest use, when there is one. */
  lastResultRoute: string | null;
  /** Writing: the submission a free revision is made from. */
  lastSubmissionId: string | null;
}

/**
 * The learner's own-profession free sample (0 or 1 rows). An empty list means
 * "nothing to show" — the feature is off or no profession has live content yet.
 */
export const listFreeSamples = (subtest: FreeSampleSubtest) =>
  apiClient.get<FreeSampleOption[]>(`/v1/free-samples/${subtest}`);
