/**
 * Shared types for the SUBAGENT_E bulk admin operations:
 * - bulk mock-bundle generation
 * - vocabulary publish-batch (with optional AI backfill)
 * - generic "republish drafts" page for any subtest
 *
 * Used by:
 *  - components/admin/bulk-runner/BulkRunner.tsx
 *  - app/admin/content/mocks/bulk/page.tsx
 *  - app/admin/content/vocabulary/publish-batch/page.tsx
 *  - app/admin/content/papers/republish-drafts/page.tsx
 *  - lib/api.ts (SUBAGENT_E marker block)
 */

export type BulkSubtest = 'listening' | 'reading' | 'writing' | 'speaking';
export type BulkDifficulty = 'easy' | 'medium' | 'hard';

/**
 * Result shape returned by every {@link BulkRunner} `run(item)` callback.
 * `ok` is the only mandatory field; warnings/error are surfaced per-row.
 */
export interface BulkRunnerOutcome {
  ok: boolean;
  warnings?: string[];
  error?: string;
  /** Optional human-readable extra detail (e.g. created bundle title). */
  detail?: string;
}

export type BulkRunnerRowState =
  | { status: 'pending' }
  | { status: 'running' }
  | { status: 'done'; outcome: BulkRunnerOutcome };

/**
 * Mock-bundle matrix cell: one row (profession) × one column (difficulty),
 * value = count of bundles to create with that pair.
 */
export interface BulkMockMatrixCell {
  profession: string;
  difficulty: BulkDifficulty;
  count: number;
}

export interface BulkMockPlanItem {
  /** Stable per-plan id for keys / progress rows. */
  id: string;
  /** 1-based ordinal within the run (used to build the title). */
  ordinal: number;
  title: string;
  profession: string;
  difficulty: BulkDifficulty;
}
