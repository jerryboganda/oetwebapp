'use client';

/**
 * Admin · Writing · Case-note audit.
 *
 * Read-only, advisory. Lists the Writing tasks whose STORED case-note rows look like a value was lost in extraction
 * (no date-of-birth line, a DOB or range-of-movement label without a value, no degree values for a physiotherapy task,
 * OCR debris, a very short or truncated extraction). It cannot see the source PDF, so a flag means "open the task and
 * compare with the PDF", never "this task is wrong". Repair in the task's case-notes editor. Backed by
 * `GET /v1/admin/writing/tasks/case-note-audit`.
 */

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';

import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Select } from '@/components/ui/form-controls';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getWritingCaseNoteAudit } from '@/lib/writing/exam-api';
import type { WritingCaseNoteAuditDto } from '@/lib/writing/types';

type Scope = 'published' | 'draft' | 'archived' | 'all';

const SCOPE_OPTIONS: Array<{ value: Scope; label: string }> = [
  { value: 'published', label: 'Published tasks' },
  { value: 'draft', label: 'Draft tasks' },
  { value: 'archived', label: 'Archived tasks' },
  { value: 'all', label: 'All tasks' },
];

export default function WritingCaseNoteAuditPage() {
  const router = useRouter();
  useAdminAuth(); // enforces admin access + redirect

  const [scope, setScope] = useState<Scope>('published');
  const [report, setReport] = useState<WritingCaseNoteAuditDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setReport(await getWritingCaseNoteAudit(scope));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load the case-note audit');
    } finally {
      setLoading(false);
    }
  }, [scope]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <AdminSettingsLayout
      title="Case-note audit"
      description="Tasks whose stored case notes look like a value (date of birth, range of movement ...) was lost in extraction."
      eyebrow="Writing"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Writing', href: '/admin/writing' },
        { label: 'Tasks', href: '/admin/writing/tasks' },
        { label: 'Case-note audit' },
      ]}
      actions={(
        <Button variant="secondary" size="sm" onClick={() => void load()} disabled={loading}>
          Refresh
        </Button>
      )}
    >
      <SettingsSection
        title="Scope"
        description="Advisory only: a flag means compare the task with its source PDF. Repair a lost value in the task's case-notes editor, not by re-running the PDF extraction in bulk."
      >
        <div className="max-w-xs">
          <Select
            label="Tasks to check"
            value={scope}
            onChange={(e) => setScope(e.target.value as Scope)}
            options={SCOPE_OPTIONS}
          />
        </div>
      </SettingsSection>

      <SettingsSection
        title="Flagged tasks"
        description={
          report
            ? `${report.flagged} of ${report.scanned} ${report.scope} task${report.scanned === 1 ? '' : 's'} flagged.`
            : 'Checking the stored case notes...'
        }
      >
        {error ? (
          <p role="alert" className="text-sm text-[var(--admin-danger)]">{error}</p>
        ) : loading && !report ? (
          <p className="text-sm text-admin-fg-muted">Loading...</p>
        ) : report && report.rows.length === 0 ? (
          <p className="text-sm text-admin-fg-muted">No task in this scope looks like it lost a value.</p>
        ) : report ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--admin-border)] text-xs uppercase tracking-wide text-admin-fg-muted">
                  <th scope="col" className="py-2 pr-4 font-medium">Task</th>
                  <th scope="col" className="py-2 pr-4 font-medium">Profession</th>
                  <th scope="col" className="py-2 pr-4 font-medium">Rows</th>
                  <th scope="col" className="py-2 pr-4 font-medium">What looks lost</th>
                  <th scope="col" className="py-2 font-medium"><span className="sr-only">Open</span></th>
                </tr>
              </thead>
              <tbody>
                {report.rows.map((row) => (
                  <tr key={row.scenarioId} className="border-b border-[var(--admin-border)] align-top">
                    <td className="py-2 pr-4">
                      <div className="font-medium text-admin-fg-strong">{row.title || 'Untitled task'}</div>
                      <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-admin-fg-muted">
                        {row.internalCode && <span className="font-mono">{row.internalCode}</span>}
                        <Badge variant={row.status === 'published' ? 'success' : 'default'} size="sm" className="capitalize">
                          {row.status}
                        </Badge>
                      </div>
                    </td>
                    <td className="py-2 pr-4 capitalize">{row.profession.replace(/_/g, ' ')}</td>
                    <td className="py-2 pr-4 tabular-nums">{row.caseNoteRowCount}</td>
                    <td className="py-2 pr-4">
                      <ul className="space-y-1">
                        {row.messages.map((message, index) => (
                          <li key={`${row.scenarioId}-${row.warnings[index] ?? index}`} className="text-xs">
                            <span className="font-mono text-admin-fg-muted">{row.warnings[index]}</span>
                            <span className="ml-2">{message}</span>
                          </li>
                        ))}
                      </ul>
                    </td>
                    <td className="py-2 text-right">
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => router.push(`/admin/writing/tasks/${row.scenarioId}/edit`)}
                      >
                        Open
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
