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
      <p className="rounded-xl border border-dashed border-border-hover px-4 py-6 text-center text-sm text-muted">
        No candidates selected.
      </p>
    );
  }

  return (
    <div className={cn('overflow-x-auto rounded-xl border border-border', className)}>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">Per-candidate reading results and assignment completion</caption>
        <thead>
          <tr className="border-b border-border bg-background-light text-left eyebrow text-muted">
            <th scope="col" className="px-4 py-2.5">Candidate</th>
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
              className="border-b border-border last:border-b-0"
            >
              <th scope="row" className="px-4 py-2.5 text-left font-medium text-navy">
                {student.userId}
              </th>
              <td className="px-4 py-2.5">
                {student.hasAttempt ? (
                  <ReadingRagChip rag={student.rag} />
                ) : (
                  <ReadingRagChip rag="unknown" label="No attempt" />
                )}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-navy">
                {student.rawScore ?? '-'}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-navy">
                {student.scaledScore ?? '-'}
              </td>
              <td className="px-4 py-2.5 text-center text-navy">
                {student.gradeLetter || '-'}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-navy">
                {student.assignmentsCompleted}/{student.assignmentsAssigned}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
