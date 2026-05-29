'use client';

import { cn } from '@/lib/utils';
import type { ReadingCohortStudent } from '@/lib/reading-tutor-api';
import { ReadingRagChip } from './reading-rag-chip';

/**
 * ReadingCohortTable — per-student rollup for a reading paper. RAG verdicts come
 * straight from the API (`rag`); no thresholds are computed on the client.
 */

export interface ReadingCohortTableProps {
  students: ReadingCohortStudent[];
  className?: string;
}

export function ReadingCohortTable({ students, className }: ReadingCohortTableProps) {
  if (students.length === 0) {
    return (
      <p className="rounded-xl border border-dashed border-slate-300 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-700 dark:text-slate-400">
        No students selected.
      </p>
    );
  }

  return (
    <div className={cn('overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-700', className)}>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">Per-student reading results and assignment completion</caption>
        <thead>
          <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500 dark:border-slate-700 dark:bg-slate-800/60 dark:text-slate-400">
            <th scope="col" className="px-4 py-2.5">Student</th>
            <th scope="col" className="px-4 py-2.5">Status</th>
            <th scope="col" className="px-4 py-2.5 text-right">Raw</th>
            <th scope="col" className="px-4 py-2.5 text-right">Scaled</th>
            <th scope="col" className="px-4 py-2.5 text-center">Grade</th>
            <th scope="col" className="px-4 py-2.5 text-right">Assignments</th>
          </tr>
        </thead>
        <tbody>
          {students.map((student) => (
            <tr
              key={student.userId}
              className="border-b border-slate-100 last:border-b-0 dark:border-slate-800"
            >
              <th scope="row" className="px-4 py-2.5 text-left font-medium text-slate-900 dark:text-slate-100">
                {student.userId}
              </th>
              <td className="px-4 py-2.5">
                {student.hasAttempt ? (
                  <ReadingRagChip rag={student.rag} />
                ) : (
                  <ReadingRagChip rag="unknown" label="No attempt" />
                )}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                {student.rawScore ?? '-'}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                {student.scaledScore ?? '-'}
              </td>
              <td className="px-4 py-2.5 text-center text-slate-700 dark:text-slate-300">
                {student.gradeLetter || '-'}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                {student.assignmentsCompleted}/{student.assignmentsAssigned}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
