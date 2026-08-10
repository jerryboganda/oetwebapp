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
  createAssessmentScoreTable,
  listAssessmentScoreTables,
  makeAssessmentScoreTableEffective,
  type AssessmentScoreConversionRowDto,
  type AssessmentScoreConversionTableDto,
  createAssessmentMarkingPolicy,
  listAssessmentMarkingPolicies,
  makeAssessmentMarkingPolicyEffective,
  createAssessmentRationale,
  listAssessmentRationales,
  makeAssessmentRationaleEffective,
  listAssessmentReMarkJobs,
  createAssessmentReMarkJob,
  approveAssessmentReMarkJob,
  executeAssessmentReMarkJob,
  type AssessmentMarkingPolicyDto,
  type AssessmentRationaleDto,
  type AssessmentReMarkJobDto,
} from '@/lib/assessment-governance-api';
import {
  adminGetScoringPolicy,
  adminUpdateScoringPolicy,
  adminActivateScoringPolicy,
  adminListScoringPolicyHistory,
  type ScoringPolicyDto,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const DEFAULT_POLICY_JSON = JSON.stringify({
  listening: { conversionTable: 'owner-managed' },
  reading: { conversionTable: 'owner-managed' },
  writing: { passing: { uk: 350, ie: 350, au: 350, nz: 350, ca: 350, us: 300, qa: 300 } },
  speaking: { passing: { default: 350 } },
}, null, 2);


const DEFAULT_MARKING_POLICY_JSON = JSON.stringify({
  trimLeadingTrailingWhitespace: true,
  collapseInternalWhitespace: false,
  caseSensitive: true,
  readingPartAMatchingPartialCredit: false,
  listeningAudioReplayAllowed: false,
  audioLockMode: 'exam',
  technicalRequirementsGuidanceOnly: true,
}, null, 2);
const DEFAULT_BODY = `# How am I graded?

OET reports a scaled score from 0 to 500 per sub-test. Listening and Reading
conversion tables are managed separately below and remain unavailable until
the owner publishes a complete approved table.

| Sub-test | Passing score | Grade |
|---|---|---|
| Writing | 350 / 500 (UK, IE, AU, NZ, CA); 300 / 500 (US, QA) | Grade B / C+ |
| Speaking | 350 / 500 | Grade B |

Listening/Reading tables are versioned and shown on the result page.
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
  const [assessment, setAssessment] = useState<'listening' | 'reading'>('listening');
  const [scoreTables, setScoreTables] = useState<AssessmentScoreConversionTableDto[]>([]);
  const [scoreTableVersion, setScoreTableVersion] = useState('');
  const [scoreTableJson, setScoreTableJson] = useState('[]');
  const [scoreTableError, setScoreTableError] = useState<string | null>(null);
  const [scoreTableSaving, setScoreTableSaving] = useState(false);
  const [markingPolicies, setMarkingPolicies] = useState<AssessmentMarkingPolicyDto[]>([]);
  const [markingPolicyVersion, setMarkingPolicyVersion] = useState('');
  const [markingPolicyJson, setMarkingPolicyJson] = useState(DEFAULT_MARKING_POLICY_JSON);
  const [markingPolicySaving, setMarkingPolicySaving] = useState(false);
  const [rationales, setRationales] = useState<AssessmentRationaleDto[]>([]);
  const [rationaleQuestionId, setRationaleQuestionId] = useState('');
  const [rationaleSource, setRationaleSource] = useState('');
  const [rationaleText, setRationaleText] = useState('');
  const [rationaleEvidenceCount, setRationaleEvidenceCount] = useState('1');
  const [rationaleSaving, setRationaleSaving] = useState(false);
  const [remarkJobs, setRemarkJobs] = useState<AssessmentReMarkJobDto[]>([]);
  const [remarkAttemptId, setRemarkAttemptId] = useState('');
  const [remarkQuestionId, setRemarkQuestionId] = useState('');
  const [remarkReason, setRemarkReason] = useState('');
  const [remarkOriginalKey, setRemarkOriginalKey] = useState('{}');
  const [remarkNewKey, setRemarkNewKey] = useState('{}');
  const [remarkSaving, setRemarkSaving] = useState(false);

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

  const reloadScoreTables = useCallback(async () => {
    try {
      setScoreTables(await listAssessmentScoreTables({ assessment }));
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, [assessment]);

  useEffect(() => { void reloadScoreTables(); }, [reloadScoreTables]);

  const reloadGovernance = useCallback(async () => {
    try {
      const [policies, loadedRationales, loadedJobs] = await Promise.all([
        listAssessmentMarkingPolicies({ assessment }),
        listAssessmentRationales({ assessment }),
        listAssessmentReMarkJobs({ assessment }),
      ]);
      setMarkingPolicies(policies);
      setRationales(loadedRationales);
      setRemarkJobs(loadedJobs);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, [assessment]);

  useEffect(() => { void reloadGovernance(); }, [reloadGovernance]);

  async function saveMarkingPolicyDraft() {
    try {
      const parsed: unknown = JSON.parse(markingPolicyJson);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Marking policy must be a JSON object.');
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
      return;
    }
    if (!markingPolicyVersion.trim()) {
      setToast({ variant: 'error', message: 'Marking policy version is required.' });
      return;
    }
    setMarkingPolicySaving(true);
    try {
      await createAssessmentMarkingPolicy({
        assessment,
        scopeKey: 'default',
        versionKey: markingPolicyVersion.trim(),
        policyJson: markingPolicyJson,
      });
      setMarkingPolicyVersion('');
      setToast({ variant: 'success', message: `${assessment} marking policy draft created.` });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setMarkingPolicySaving(false);
    }
  }

  async function makeMarkingPolicyEffective(id: string) {
    setMarkingPolicySaving(true);
    try {
      await makeAssessmentMarkingPolicyEffective(id);
      setToast({ variant: 'success', message: 'Marking policy is now effective and locks after first attempt uses it.' });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setMarkingPolicySaving(false);
    }
  }
  async function saveRationale() {
    const evidenceCount = Number.parseInt(rationaleEvidenceCount, 10);
    if (!rationaleQuestionId.trim() || !rationaleSource.trim() || !rationaleText.trim() || !Number.isInteger(evidenceCount) || evidenceCount < 1) {
      setToast({ variant: 'error', message: 'Question revision, source sentence, rationale, and positive evidence count are required.' });
      return;
    }
    setRationaleSaving(true);
    try {
      await createAssessmentRationale({
        assessment,
        questionRevisionId: rationaleQuestionId.trim(),
        sourceSentence: rationaleSource.trim(),
        rationaleText: rationaleText.trim(),
        evidenceCount,
      });
      setRationaleQuestionId('');
      setRationaleSource('');
      setRationaleText('');
      setRationaleEvidenceCount('1');
      setToast({ variant: 'success', message: `${assessment} rationale draft created.` });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setRationaleSaving(false);
    }
  }
  async function makeRationaleEffective(id: string) {
    setRationaleSaving(true);
    try {
      await makeAssessmentRationaleEffective(id);
      setToast({ variant: 'success', message: 'Approved rationale is now effective for grounded explanations.' });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setRationaleSaving(false);
    }
  }
  async function saveReMarkJob() {
    if (!remarkAttemptId.trim() || !remarkQuestionId.trim() || !remarkReason.trim()) {
      setToast({ variant: 'error', message: 'Attempt ID, question revision, and re-mark reason are required.' });
      return;
    }
    if (!validateJson(remarkOriginalKey) || !validateJson(remarkNewKey)) {
      setToast({ variant: 'error', message: 'Both answer-key snapshots must be valid JSON.' });
      return;
    }
    setRemarkSaving(true);
    try {
      await createAssessmentReMarkJob({
        assessment,
        attemptId: remarkAttemptId.trim(),
        questionRevisionId: remarkQuestionId.trim(),
        reason: remarkReason.trim(),
        originalKeySnapshotJson: remarkOriginalKey,
        newKeySnapshotJson: remarkNewKey,
      });
      setRemarkAttemptId('');
      setRemarkQuestionId('');
      setRemarkReason('');
      setToast({ variant: 'success', message: 'Controlled re-mark job created and awaiting separate approval.' });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setRemarkSaving(false);
    }
  }
  async function transitionReMarkJob(id: string, action: 'approve' | 'execute') {
    setRemarkSaving(true);
    try {
      if (action === 'approve') await approveAssessmentReMarkJob(id);
      else await executeAssessmentReMarkJob(id);
      setToast({ variant: 'success', message: action === 'approve' ? 'Re-mark job approved.' : 'Re-mark job executed.' });
      await reloadGovernance();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setRemarkSaving(false);
    }
  }





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

  function parseScoreTableRows(): AssessmentScoreConversionRowDto[] | null {
    try {
      const parsed: unknown = JSON.parse(scoreTableJson);
      if (!Array.isArray(parsed)) throw new Error('Rows must be a JSON array.');
      const rows = parsed.map((row, index) => {
        if (!row || typeof row !== 'object') throw new Error(`Row ${index + 1} is not an object.`);
        const candidate = row as Record<string, unknown>;
        if (typeof candidate.rawScore !== 'number' || typeof candidate.convertedScore !== 'number') {
          throw new Error(`Row ${index + 1} needs numeric rawScore and convertedScore.`);
        }
        return {
          rawScore: candidate.rawScore,
          convertedScore: candidate.convertedScore,
          grade: typeof candidate.grade === 'string' ? candidate.grade : null,
          passed: typeof candidate.passed === 'boolean' ? candidate.passed : null,
        } satisfies AssessmentScoreConversionRowDto;
      });
      setScoreTableError(rows.length === 43 ? null : 'The owner table must contain exactly 43 rows covering raw scores 0 through 42.');
      return rows;
    } catch (e) {
      setScoreTableError((e as Error).message);
      return null;
    }
  }

  async function saveScoreTableDraft() {
    const rows = parseScoreTableRows();
    if (!rows || rows.length !== 43 || !scoreTableVersion.trim()) {
      if (!scoreTableVersion.trim()) setScoreTableError('Version key is required.');
      return;
    }
    setScoreTableSaving(true);
    try {
      await createAssessmentScoreTable({
        assessment,
        scopeKey: 'default',
        versionKey: scoreTableVersion.trim(),
        rows,
      });
      setScoreTableVersion('');
      setScoreTableJson('[]');
      setToast({ variant: 'success', message: `${assessment} score table draft created. It is not live until made effective.` });
      await reloadScoreTables();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setScoreTableSaving(false);
    }
  }

  async function makeScoreTableEffective(id: string) {
    setScoreTableSaving(true);
    try {
      await makeAssessmentScoreTableEffective(id);
      setToast({ variant: 'success', message: 'Score table is now effective and will be locked after first use.' });
      await reloadScoreTables();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setScoreTableSaving(false);
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
      <SettingsSection
        title="Listening / Reading conversion tables"
        description="Owner-approved 0–42 lookup tables only. No interpolation or formula fallback is permitted."
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="text-sm font-medium">
              Assessment
              <select
                className="mt-1 block rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm"
                value={assessment}
                onChange={(event) => setAssessment(event.target.value as 'listening' | 'reading')}
              >
                <option value="listening">Listening</option>
                <option value="reading">Reading</option>
              </select>
            </label>
            <label className="min-w-56 flex-1 text-sm font-medium">
              New version key
              <input
                className="mt-1 block w-full rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm"
                value={scoreTableVersion}
                onChange={(event) => setScoreTableVersion(event.target.value)}
                placeholder="owner-approved-version"
              />
            </label>
            <Button variant="outline" onClick={() => void reloadScoreTables()} disabled={scoreTableSaving}>
              Refresh tables
            </Button>
          </div>

          <div>
            <label className="text-sm font-medium" htmlFor="score-table-json">43 exact rows (JSON)</label>
            <Textarea
              id="score-table-json"
              value={scoreTableJson}
              onChange={(event) => { setScoreTableJson(event.target.value); setScoreTableError(null); }}
              rows={10}
              aria-label="Score table rows JSON"
              placeholder='[{"rawScore":0,"convertedScore":0,"grade":"E"}, ...]'
              className="mt-1 font-mono text-xs"
            />
            {scoreTableError ? (
              <p className="mt-1 text-xs text-[var(--admin-danger)]">{scoreTableError}</p>
            ) : (
              <p className="mt-1 text-xs text-admin-fg-muted">Do not enter guessed values. Populate this from the owner-approved conversion table.</p>
            )}
          </div>

          <Button onClick={() => void saveScoreTableDraft()} disabled={scoreTableSaving} loading={scoreTableSaving}>
            {scoreTableSaving ? 'Saving…' : 'Create draft table'}
          </Button>

          <div className="overflow-x-auto rounded-md border border-admin-border">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-admin-surface-muted text-xs uppercase tracking-wide text-admin-fg-muted">
                <tr>
                  <th className="px-3 py-2">Version</th>
                  <th className="px-3 py-2">Rows</th>
                  <th className="px-3 py-2">Status</th>
                  <th className="px-3 py-2">Effective from</th>
                  <th className="px-3 py-2">Action</th>
                </tr>
              </thead>
              <tbody>
                {scoreTables.map((table) => (
                  <tr key={table.id} className="border-t border-admin-border">
                    <td className="px-3 py-2 font-mono">{table.versionKey}</td>
                    <td className="px-3 py-2">{table.rows.length}/43</td>
                    <td className="px-3 py-2"><Badge variant={table.status === 'Effective' ? 'success' : table.status === 'Locked' ? 'default' : 'warning'}>{table.status}</Badge></td>
                    <td className="px-3 py-2">{new Date(table.effectiveFrom).toLocaleString()}</td>
                    <td className="px-3 py-2">
                      {table.status === 'Draft' ? (
                        <Button size="sm" variant="outline" onClick={() => void makeScoreTableEffective(table.id)} disabled={scoreTableSaving}>
                          Make effective
                        </Button>
                      ) : table.hasBeenUsed ? (
                        <span className="text-xs text-admin-fg-muted">Locked after use</span>
                      ) : null}
                    </td>
                  </tr>
                ))}
                {scoreTables.length === 0 ? (
                  <tr><td className="px-3 py-4 text-admin-fg-muted" colSpan={5}>No governed {assessment} table exists. Scaled results remain unavailable.</td></tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </div>
      </SettingsSection>

      <SettingsSection
        title="Governed marking, rationales, and re-mark"
        description="Versioned owner controls are the only source for strict marking, grounded explanations, and controlled corrections."
      >
        <div className="space-y-6">
          <div className="space-y-3">
            <h2 className="text-sm font-semibold">Marking policy version</h2>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
              <input
                className="rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm"
                value={markingPolicyVersion}
                onChange={(event) => setMarkingPolicyVersion(event.target.value)}
                placeholder="owner-approved-marking-v1"
                aria-label="Marking policy version"
              />
              <Textarea
                value={markingPolicyJson}
                onChange={(event) => setMarkingPolicyJson(event.target.value)}
                rows={7}
                className="font-mono text-xs"
                aria-label="Marking policy JSON"
              />
            </div>
            <Button onClick={() => void saveMarkingPolicyDraft()} disabled={markingPolicySaving} loading={markingPolicySaving}>
              Create marking policy draft
            </Button>
            <div className="overflow-x-auto rounded-md border border-admin-border">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-admin-surface-muted text-xs uppercase tracking-wide text-admin-fg-muted">
                  <tr><th className="px-3 py-2">Version</th><th className="px-3 py-2">Status</th><th className="px-3 py-2">Action</th></tr>
                </thead>
                <tbody>
                  {markingPolicies.map((policy) => (
                    <tr key={policy.id} className="border-t border-admin-border">
                      <td className="px-3 py-2 font-mono">{policy.versionKey}</td>
                      <td className="px-3 py-2"><Badge variant={policy.status === 'Effective' ? 'success' : policy.status === 'Locked' ? 'default' : 'warning'}>{policy.status}</Badge></td>
                      <td className="px-3 py-2">
                        {policy.status === 'Draft' ? (
                          <Button size="sm" variant="outline" onClick={() => void makeMarkingPolicyEffective(policy.id)} disabled={markingPolicySaving}>Make effective</Button>
                        ) : policy.hasBeenUsed ? (
                          <span className="text-xs text-admin-fg-muted">Locked after use</span>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                  {markingPolicies.length === 0 ? <tr><td colSpan={3} className="px-3 py-3 text-admin-fg-muted">No governed marking policy exists for this assessment.</td></tr> : null}
                </tbody>
              </table>
            </div>
          </div>

          <div className="space-y-3 border-t border-admin-border pt-5">
            <h2 className="text-sm font-semibold">Approved rationale evidence</h2>
            <div className="grid gap-3 md:grid-cols-2">
              <input className="rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={rationaleQuestionId} onChange={(event) => setRationaleQuestionId(event.target.value)} placeholder="Question revision ID" aria-label="Rationale question revision ID" />
              <input className="rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={rationaleEvidenceCount} onChange={(event) => setRationaleEvidenceCount(event.target.value)} inputMode="numeric" placeholder="Evidence count" aria-label="Rationale evidence count" />
            </div>
            <input className="w-full rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={rationaleSource} onChange={(event) => setRationaleSource(event.target.value)} placeholder="Source sentence or transcript evidence" aria-label="Rationale source sentence" />
            <Textarea value={rationaleText} onChange={(event) => setRationaleText(event.target.value)} rows={4} placeholder="Why the approved answer is correct" aria-label="Rationale text" />
            <Button onClick={() => void saveRationale()} disabled={rationaleSaving} loading={rationaleSaving}>Create rationale draft</Button>
            <div className="overflow-x-auto rounded-md border border-admin-border">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-admin-surface-muted text-xs uppercase tracking-wide text-admin-fg-muted">
                  <tr><th className="px-3 py-2">Question revision</th><th className="px-3 py-2">Evidence</th><th className="px-3 py-2">Status</th><th className="px-3 py-2">Action</th></tr>
                </thead>
                <tbody>
                  {rationales.map((rationale) => (
                    <tr key={rationale.id} className="border-t border-admin-border">
                      <td className="px-3 py-2 font-mono">{rationale.questionRevisionId}</td>
                      <td className="px-3 py-2">{rationale.evidenceCount}</td>
                      <td className="px-3 py-2"><Badge variant={rationale.status === 'Effective' ? 'success' : 'warning'}>{rationale.status}</Badge></td>
                      <td className="px-3 py-2">{rationale.status === 'Draft' ? <Button size="sm" variant="outline" onClick={() => void makeRationaleEffective(rationale.id)} disabled={rationaleSaving}>Make effective</Button> : null}</td>
                    </tr>
                  ))}
                  {rationales.length === 0 ? <tr><td colSpan={4} className="px-3 py-3 text-admin-fg-muted">No rationale records exist for this assessment.</td></tr> : null}
                </tbody>
              </table>
            </div>
          </div>

          <div className="space-y-3 border-t border-admin-border pt-5">
            <h2 className="text-sm font-semibold">Controlled re-mark job</h2>
            <p className="text-xs text-admin-fg-muted">Create, separately approve, and then execute a deterministic re-mark. Both answer-key snapshots are retained for audit.</p>
            <div className="grid gap-3 md:grid-cols-2">
              <input className="rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={remarkAttemptId} onChange={(event) => setRemarkAttemptId(event.target.value)} placeholder="Submitted attempt ID" aria-label="Re-mark attempt ID" />
              <input className="rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={remarkQuestionId} onChange={(event) => setRemarkQuestionId(event.target.value)} placeholder="Question revision ID" aria-label="Re-mark question revision ID" />
            </div>
            <input className="w-full rounded-md border border-admin-border bg-admin-surface px-3 py-2 text-sm" value={remarkReason} onChange={(event) => setRemarkReason(event.target.value)} placeholder="Reason for controlled re-mark" aria-label="Re-mark reason" />
            <div className="grid gap-3 lg:grid-cols-2">
              <Textarea value={remarkOriginalKey} onChange={(event) => setRemarkOriginalKey(event.target.value)} rows={6} className="font-mono text-xs" aria-label="Original answer key snapshot JSON" placeholder="Original answer key snapshot JSON" />
              <Textarea value={remarkNewKey} onChange={(event) => setRemarkNewKey(event.target.value)} rows={6} className="font-mono text-xs" aria-label="New answer key snapshot JSON" placeholder="New answer key snapshot JSON" />
            </div>
            <Button onClick={() => void saveReMarkJob()} disabled={remarkSaving} loading={remarkSaving}>Create re-mark job</Button>
            <div className="overflow-x-auto rounded-md border border-admin-border">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-admin-surface-muted text-xs uppercase tracking-wide text-admin-fg-muted">
                  <tr><th className="px-3 py-2">Attempt</th><th className="px-3 py-2">Question</th><th className="px-3 py-2">Status</th><th className="px-3 py-2">Action</th></tr>
                </thead>
                <tbody>
                  {remarkJobs.map((job) => (
                    <tr key={job.id} className="border-t border-admin-border">
                      <td className="px-3 py-2 font-mono">{job.attemptId}</td>
                      <td className="px-3 py-2 font-mono">{job.questionRevisionId}</td>
                      <td className="px-3 py-2"><Badge variant={job.status === 'Completed' ? 'success' : job.status === 'Approved' ? 'default' : 'warning'}>{job.status}</Badge></td>
                      <td className="px-3 py-2">
                        {job.status === 'Draft' ? <Button size="sm" variant="outline" onClick={() => void transitionReMarkJob(job.id, 'approve')} disabled={remarkSaving}>Approve</Button> : null}
                        {job.status === 'Approved' ? <Button size="sm" variant="outline" onClick={() => void transitionReMarkJob(job.id, 'execute')} disabled={remarkSaving}>Execute</Button> : null}
                      </td>
                    </tr>
                  ))}
                  {remarkJobs.length === 0 ? <tr><td colSpan={4} className="px-3 py-3 text-admin-fg-muted">No controlled re-mark jobs exist for this assessment.</td></tr> : null}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </SettingsSection>

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
