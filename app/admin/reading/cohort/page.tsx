'use client';

import { useEffect, useState } from 'react';
import { BarChart3, Loader2 } from 'lucide-react';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { ReadingCohortTable } from '@/components/domain/reading/reading-cohort-table';
import { listContentPapers, type ContentPaperDto } from '@/lib/content-upload-api';
import {
  getReadingCohortAnalytics,
  type ReadingCohortAnalytics,
} from '@/lib/reading-tutor-api';

const PART_LABELS: Record<string, string> = {
  A: 'Part A',
  B: 'Part B',
  C: 'Part C',
};

function parseUserIds(value: string): string[] {
  return value
    .split(/[\s,]+/)
    .map((id) => id.trim())
    .filter((id) => id.length > 0);
}

export default function AdminReadingCohortPage() {
  const [papers, setPapers] = useState<ContentPaperDto[]>([]);
  const [papersError, setPapersError] = useState(false);
  const [paperId, setPaperId] = useState('');
  const [userIdsText, setUserIdsText] = useState('');

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [analytics, setAnalytics] = useState<ReadingCohortAnalytics | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await listContentPapers({ subtest: 'reading', pageSize: 500 });
        if (!cancelled) setPapers(data);
      } catch {
        if (!cancelled) setPapersError(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleRun(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setAnalytics(null);

    if (!paperId) {
      setError('Select a reading paper.');
      return;
    }
    const userIds = parseUserIds(userIdsText);
    if (userIds.length === 0) {
      setError('Enter at least one learner id.');
      return;
    }

    setLoading(true);
    try {
      const data = await getReadingCohortAnalytics({ paperId, userIds }, 'admin');
      setAnalytics(data);
    } catch {
      setError('Failed to load cohort analytics.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AdminPageShell mainAriaLabel="Reading cohort analytics">
      <PageHeader
        eyebrow="Reading"
        title="Cohort analytics"
        description="Compare a group of learners on a single reading paper: class averages, hardest questions, common distractors, and per-student RAG verdicts."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Cohort analytics' },
        ]}
      />

      <form
        onSubmit={handleRun}
        aria-label="Cohort filters"
        className="space-y-4 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 sm:p-5"
      >
        <div className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="cohort-paper" className="text-sm font-medium text-admin-fg-default">
              Reading paper
            </label>
            <select
              id="cohort-paper"
              value={paperId}
              onChange={(event) => setPaperId(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            >
              <option value="">Select a paper…</option>
              {papers.map((paper) => (
                <option key={paper.id} value={paper.id}>
                  {paper.title}
                </option>
              ))}
            </select>
            {papersError ? (
              <p className="text-xs text-[var(--admin-danger)]">Failed to load papers.</p>
            ) : null}
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="cohort-users" className="text-sm font-medium text-admin-fg-default">
              Learner ids
            </label>
            <textarea
              id="cohort-users"
              rows={3}
              value={userIdsText}
              placeholder="Paste learner ids separated by spaces, commas, or new lines"
              onChange={(event) => setUserIdsText(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>
        </div>

        {error ? (
          <p role="alert" className="text-sm text-[var(--admin-danger)]">
            {error}
          </p>
        ) : null}

        <button
          type="submit"
          disabled={loading}
          className="inline-flex items-center gap-2 rounded-admin-lg bg-[var(--admin-primary)] px-4 py-2 text-sm font-medium text-[var(--admin-primary-fg)] transition-colors hover:bg-[var(--admin-primary-hover)] disabled:opacity-50"
        >
          {loading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <BarChart3 className="h-4 w-4" aria-hidden="true" />}
          Load analytics
        </button>
      </form>

      {analytics ? (
        <div className="space-y-6">
          <p className="text-sm text-admin-fg-muted">
            {analytics.studentCount} learner{analytics.studentCount === 1 ? '' : 's'} analysed.
          </p>

          <div className="grid gap-4 lg:grid-cols-2">
            <section className="space-y-2">
              <h2 className="text-sm font-semibold text-admin-fg-strong">Class averages by part</h2>
              <div className="overflow-x-auto rounded-admin-lg border border-admin-border">
                <table className="w-full border-collapse text-sm">
                  <thead>
                    <tr className="border-b border-admin-border bg-admin-bg-subtle text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                      <th scope="col" className="px-4 py-2.5">Part</th>
                      <th scope="col" className="px-4 py-2.5 text-right">Avg raw</th>
                      <th scope="col" className="px-4 py-2.5 text-right">Avg accuracy</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analytics.partAverages.length === 0 ? (
                      <tr>
                        <td colSpan={3} className="px-4 py-6 text-center text-admin-fg-muted">No data.</td>
                      </tr>
                    ) : (
                      analytics.partAverages.map((part) => (
                        <tr key={part.partCode} className="border-b border-admin-border last:border-b-0">
                          <th scope="row" className="px-4 py-2.5 text-left font-medium text-admin-fg-default">
                            {PART_LABELS[part.partCode] ?? `Part ${part.partCode}`}
                          </th>
                          <td className="px-4 py-2.5 text-right tabular-nums text-admin-fg-default">
                            {part.averageRawScore.toFixed(1)}/{part.maxRawScore}
                          </td>
                          <td className="px-4 py-2.5 text-right tabular-nums text-admin-fg-default">
                            {part.averageAccuracyPercent.toFixed(0)}%
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            <section className="space-y-2">
              <h2 className="text-sm font-semibold text-admin-fg-strong">Class averages by skill</h2>
              <div className="overflow-x-auto rounded-admin-lg border border-admin-border">
                <table className="w-full border-collapse text-sm">
                  <thead>
                    <tr className="border-b border-admin-border bg-admin-bg-subtle text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                      <th scope="col" className="px-4 py-2.5">Skill</th>
                      <th scope="col" className="px-4 py-2.5 text-right">Avg accuracy</th>
                      <th scope="col" className="px-4 py-2.5 text-right">Questions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analytics.skillAverages.length === 0 ? (
                      <tr>
                        <td colSpan={3} className="px-4 py-6 text-center text-admin-fg-muted">No data.</td>
                      </tr>
                    ) : (
                      analytics.skillAverages.map((skill) => (
                        <tr key={skill.skill} className="border-b border-admin-border last:border-b-0">
                          <th scope="row" className="px-4 py-2.5 text-left font-medium text-admin-fg-default">
                            {skill.skill}
                          </th>
                          <td className="px-4 py-2.5 text-right tabular-nums text-admin-fg-default">
                            {skill.averageAccuracyPercent.toFixed(0)}%
                          </td>
                          <td className="px-4 py-2.5 text-right tabular-nums text-admin-fg-muted">
                            {skill.questionCount}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </section>
          </div>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold text-admin-fg-strong">Hardest questions</h2>
            {analytics.hardestQuestions.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No question data.</p>
            ) : (
              <ul className="space-y-2">
                {analytics.hardestQuestions.map((question) => (
                  <li
                    key={question.questionId}
                    className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-4 py-3"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-xs font-bold uppercase tracking-widest text-admin-fg-muted">
                          {question.partCode} · Q{question.displayOrder}
                        </p>
                        <p className="mt-0.5 text-sm text-admin-fg-default line-clamp-2">{question.stem}</p>
                      </div>
                      <span className="shrink-0 text-sm font-semibold tabular-nums text-admin-fg-strong">
                        {(question.correctRate * 100).toFixed(0)}%
                      </span>
                    </div>
                    <p className="mt-1 text-xs text-admin-fg-muted">
                      {question.correctCount}/{question.answerCount} correct
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold text-admin-fg-strong">Top distractors</h2>
            {analytics.topDistractors.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No distractor data.</p>
            ) : (
              <div className="overflow-x-auto rounded-admin-lg border border-admin-border">
                <table className="w-full border-collapse text-sm">
                  <thead>
                    <tr className="border-b border-admin-border bg-admin-bg-subtle text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                      <th scope="col" className="px-4 py-2.5">Question</th>
                      <th scope="col" className="px-4 py-2.5">Option</th>
                      <th scope="col" className="px-4 py-2.5">Category</th>
                      <th scope="col" className="px-4 py-2.5 text-right">Selected</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analytics.topDistractors.map((row, index) => (
                      <tr
                        key={`${row.questionId}-${row.optionKey}-${index}`}
                        className="border-b border-admin-border last:border-b-0"
                      >
                        <td className="px-4 py-2.5 font-mono text-xs text-admin-fg-muted">{row.questionId}</td>
                        <td className="px-4 py-2.5 text-admin-fg-default">{row.optionKey}</td>
                        <td className="px-4 py-2.5 text-admin-fg-default">{row.category}</td>
                        <td className="px-4 py-2.5 text-right tabular-nums text-admin-fg-default">{row.selectedCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold text-admin-fg-strong">Per-student results</h2>
            <ReadingCohortTable students={analytics.students} />
          </section>
        </div>
      ) : null}
    </AdminPageShell>
  );
}
