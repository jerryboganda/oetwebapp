import { apiClient } from '@/lib/api';

// Free Mocks (owner 2026-09-22): the learner's ONE free AI-graded Writing and
// ONE free AI-graded Speaking sample. The server decides what is free (the
// profession's designated item, once per learner) — this client only asks what
// is on offer and never sends a "free" flag with any start/submit request.

export type FreeSampleSubtest = 'writing' | 'speaking';

/** available = not started · in_progress = claimed, may continue · used = spent. */
export type FreeSampleState = 'available' | 'in_progress' | 'used';

export interface FreeSampleOption {
  /** Normalised profession id (lower-case, hyphenated). */
  professionId: string;
  /** Writing: scenario id. Speaking: role-play card id. */
  contentId: string;
  state: FreeSampleState;
  /** App route that opens the sample (server-built). */
  route: string;
}

/**
 * Every profession that has a free sample on offer right now (only the
 * learner's claimed profession once they have started). An empty list means
 * "nothing to show" — the feature is off or no profession has live content yet.
 */
export const listFreeSamples = (subtest: FreeSampleSubtest) =>
  apiClient.get<FreeSampleOption[]>(`/v1/free-samples/${subtest}`);
