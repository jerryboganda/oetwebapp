'use client';

import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, History, Save } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  adminGetScoringPolicy,
  adminUpdateScoringPolicy,
  adminActivateScoringPolicy,
  adminListScoringPolicyHistory,
  type ScoringPolicyDto,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const DEFAULT_POLICY_JSON = `{
  "listening": { "passing": { "default": 350 }, "rawToScaled": [{ "raw": 30, "scaled": 350, "grade": "B" }] },
  "reading":   { "passing": { "default": 350 }, "rawToScaled": [{ "raw": 30, "scaled": 350, "grade": "B" }] },
  "writing":   { "passing": { "uk": 350, "ie": 350, "au": 350, "nz": 350, "ca": 350, "us": 300, "qa": 300 } },
  "speaking":  { "passing": { "default": 350 } }
}`;

const DEFAULT_BODY = `# How am I graded?

OET reports a scaled score from 0 to 500 per sub-test, with these passing thresholds:

| Sub-test | Passing score | Grade |
|---|---|---|
| Listening | 350 / 500 | Grade B (30 / 42 raw) |
| Reading | 350 / 500 | Grade B (30 / 42 raw) |
| Writing | 350 / 500 (UK, IE, AU, NZ, CA); 300 / 500 (US, QA) | Grade B / C+ |
| Speaking | 350 / 500 | Grade B |

A raw score of 30 / 42 on Listening / Reading is equivalent to **350 / 500** scaled.

Sources: edubenchmark.com, geniusclass.co.uk
`;

export default function AdminScoringSystemPage() {
  const [current, setCurrent] = useState<ScoringPolicyDto | null>(null);
  const [body, setBody] = useState('');
  const [policyJson, setPolicyJson] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [history, setHistory] = useState<ScoringPolicyDto[]>([]);
  const [showHistory, setShowHistory] = useState(false);
  const [jsonError, setJsonError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminGetScoringPolicy();
      setCurrent(data);
      setBody(data?.bodyMarkdown ?? DEFAULT_BODY);
      setPolicyJson(data?.policyJson ?? DEFAULT_POLICY_JSON);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void reload(); }, [reload]);

  function validateJson(text: string): boolean {
    try { JSON.parse(text); setJsonError(null); return true; }
    catch (e) { setJsonError((e as Error).message); return false; }
  }

  async function save() {
    if (!validateJson(policyJson)) {
      setToast({ variant: 'error', message: 'Policy JSON is invalid. Fix the errors before saving.' });
      return;
    }
    setSaving(true);
    try {
      const updated = await adminUpdateScoringPolicy({ bodyMarkdown: body, policyJson });
      setCurrent(updated);
      setToast({ variant: 'success', message: 'Scoring policy saved and is now live.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  }

  async function openHistory() {
    setShowHistory(true);
    try {
      const rows = await adminListScoringPolicyHistory();
      setHistory(rows ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }

  async function activateHistoryRow(id: string) {
    setSaving(true);
    try {
      const updated = await adminActivateScoringPolicy(id);
      setCurrent(updated);
      setBody(updated.bodyMarkdown);
      setPolicyJson(updated.policyJson);
      setHistory((rows) => rows.map((row) => ({ ...row, isActive: row.id === id })));
      setToast({ variant: 'success', message: 'Scoring policy activated.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  }

  return (
    <AdminSettingsLayout
      title="Scoring System"
      description="Edit the 'How am I graded?' reference shown on every learner dashboard. Markdown body + structured passing thresholds."
      eyebrow="CMS"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Scoring System' },
      ]}
      actions={
        <>
          <Button variant="outline" onClick={() => void openHistory()} startIcon={<History className="h-4 w-4" />}>
            History
          </Button>
          <Button onClick={() => void save()} disabled={saving} loading={saving} startIcon={!saving ? <Save className="h-4 w-4" /> : undefined}>
            {saving ? 'Saving...' : 'Save & Publish'}
          </Button>
        </>
      }
    >
      <SettingsSection title="Policy editor" description="Markdown body + structured passing thresholds JSON.">
        {loading ? (
          <Skeleton className="h-40" />
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div>
              <h2 className="text-sm font-semibold mb-2">Body (markdown - shown to learners)</h2>
              <Textarea
                value={body}
                onChange={(e) => setBody(e.target.value)}
                rows={20}
                aria-label="Scoring body markdown"
              />
            </div>
            <div>
              <div className="flex items-center justify-between mb-2">
                <h2 className="text-sm font-semibold">Policy JSON (structured thresholds)</h2>
                {jsonError ? <Badge variant="danger">invalid JSON</Badge> : <Badge variant="success">valid</Badge>}
              </div>
              <Textarea
                value={policyJson}
                onChange={(e) => { setPolicyJson(e.target.value); validateJson(e.target.value); }}
                rows={20}
                aria-label="Scoring policy JSON"
              />
              {jsonError ? (
                <p className="text-xs text-[var(--admin-danger)] mt-1">{jsonError}</p>
              ) : (
                <p className="text-xs text-admin-fg-muted mt-1">Used by the dashboard to render per-country passing thresholds.</p>
              )}
            </div>
          </div>
        )}

        {current ? (
          <p className="text-xs text-admin-fg-muted mt-3">
            Active version <code>{current.id.slice(0, 12)}...</code>
            {' - '}updated {new Date(current.updatedAt).toLocaleString()}
            {current.updatedByUserId ? ` by ${current.updatedByUserId.slice(0, 12)}...` : ''}
          </p>
        ) : (
          <p className="text-xs text-admin-fg-muted mt-3">No scoring policy has been saved yet - defaults shown above; click Save &amp; Publish to create the first version.</p>
        )}
      </SettingsSection>

      {showHistory ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Recent versions</CardTitle>
            <div className="ml-auto">
              <Button size="sm" variant="ghost" onClick={() => setShowHistory(false)}>Close</Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-2">
          {history.length === 0 ? (
            <p className="text-sm text-admin-fg-muted">No previous versions.</p>
          ) : (
            <ul className="space-y-2">
              {history.map((row) => (
                <li key={row.id} className="text-sm flex items-center justify-between">
                  <span><code className="font-mono">{row.id.slice(0, 12)}...</code> - updated {new Date(row.updatedAt).toLocaleString()}</span>
                  <div className="flex items-center gap-2">
                    {row.isActive ? <Badge variant="success">active</Badge> : <Badge variant="default">draft</Badge>}
                    {!row.isActive ? (
                      <Button size="sm" variant="outline" onClick={() => void activateHistoryRow(row.id)} disabled={saving} startIcon={<CheckCircle2 className="h-3.5 w-3.5" />}>
                        Activate
                      </Button>
                    ) : null}
                  </div>
                </li>
              ))}
            </ul>
          )}
          </CardContent>
        </Card>
      ) : null}

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminSettingsLayout>
  );
}
