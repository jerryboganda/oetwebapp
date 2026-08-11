'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, LockKeyhole, RefreshCw, ShieldAlert } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import {
  approveSpeakingSimulationV11Approval,
  approveSpeakingSimulationV11RubricRelease,
  approveSpeakingSimulationV11SpecRelease,
  createSpeakingSimulationV11Approval,
  createSpeakingSimulationV11RubricRelease,
  createSpeakingSimulationV11SpecRelease,
  getSpeakingSimulationV11AdminStatus,
  rejectSpeakingSimulationV11Approval,
  type SpeakingSimulationV11AdminRubricCriterion,
  type SpeakingSimulationV11AdminStatus,
} from '@/lib/api/speaking-simulation-v11-admin';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Simulation v1.1 governance' },
];

const APPROVAL_OPTIONS = [
  { value: 'calibration_approval', label: 'Calibration approval' },
  { value: 'concurrency_budget', label: 'Concurrency budget' },
  { value: 'cost_ceiling', label: 'Cost ceiling (USD)' },
  { value: 'latency_sla_ms', label: 'p95 latency SLA (ms)' },
  { value: 'stt_cost_per_minute', label: 'Batch STT cost per minute (USD)' },
  { value: 'tts_cost_per_1000_characters', label: 'TTS cost per 1,000 characters (USD)' },
  { value: 'retention_approval', label: 'Retention approval' },
  { value: 'graph_approval', label: 'Score graph legal/brand approval' },
  { value: 'profession_pack_approval', label: 'Profession pack approval' },
  { value: 'audio_assessment_approval', label: 'Azure phoneme audio approval' },
  { value: 'silence_prompt_approval', label: 'Neutral silence-prompt approval' },
  { value: 'retention_days', label: 'Retention period (days)' },
  { value: 'silence_prompt_threshold_ms', label: 'Silence prompt threshold (ms)' },
];

const DEFAULT_CRITERIA: SpeakingSimulationV11AdminRubricCriterion[] = [
  ['intelligibility_pronunciation', 'Intelligibility & pronunciation', 10, ['R49', 'R50']],
  ['fluency_continuity', 'Fluency & continuity', 12, ['R49', 'R51']],
  ['grammar_vocabulary', 'Grammar & vocabulary', 8, ['R06', 'R07']],
  ['appropriateness_plain_language', 'Appropriateness / plain language', 10, ['R06', 'R08', 'R12']],
  ['relationship_building_empathy', 'Relationship building & empathy', 14, ['R13', 'R15', 'R16']],
  ['patient_perspective', 'Patient perspective', 10, ['R17', 'R18']],
  ['information_gathering', 'Information gathering', 10, ['R09', 'R10', 'R14']],
  ['information_giving_checking', 'Information giving & checking', 12, ['R11', 'R18', 'R40']],
  ['structure_task_management', 'Structure & task management', 9, ['R20', 'R21', 'R22']],
  ['closure_time_management', 'Closure & time management', 5, ['R20', 'R22', 'R49']],
].map(([criterionCode, label, weight, enabledRuleIds]) => ({
  criterionCode: criterionCode as string,
  label: label as string,
  weight: weight as number,
  enabledRuleIds: enabledRuleIds as string[],
}));

function statusTone(status: string): 'success' | 'warning' | 'danger' | 'default' {
  const value = status.toLowerCase();
  if (value === 'approved') return 'success';
  if (value === 'rejected' || value === 'retired') return 'danger';
  if (value === 'pending') return 'warning';
  return 'default';
}

export default function SpeakingSimulationV11GovernancePage() {
  const [professionId, setProfessionId] = useState('medicine');
  const [status, setStatus] = useState<SpeakingSimulationV11AdminStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [specReleaseVersion, setSpecReleaseVersion] = useState('v1.1-initial');
  const [rubricVersion, setRubricVersion] = useState('speaking-simulation-v1.1-rubric');
  const [calibrationVersion, setCalibrationVersion] = useState('speaking-simulation-v1.1-calibration');
  const [approvalKey, setApprovalKey] = useState('calibration_approval');
  const [approvalScope, setApprovalScope] = useState('global');
  const [approvalValue, setApprovalValue] = useState('');
  const [approvalEvidence, setApprovalEvidence] = useState('{"owner":"","evidence":"","provider":"azure-phoneme"}');

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setStatus(await getSpeakingSimulationV11AdminStatus(professionId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load v1.1 governance state.');
    } finally {
      setBusy(false);
    }
  }, [professionId]);

  useEffect(() => { void load(); }, [load]);

  const criteria = status?.rubricCriteria?.length ? status.rubricCriteria : DEFAULT_CRITERIA;
  const weightTotal = useMemo(() => criteria.reduce((total, criterion) => total + criterion.weight, 0), [criteria]);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Governance action failed.');
      setBusy(false);
    }
  }

  return (
    <AdminOperationsLayout
      title="Speaking simulation v1.1 governance"
      description="Versioned card/persona release, exact rubric, owner approvals, technical budgets, audio-provider governance, and fail-closed launch status. No approval is implicit."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Release control"
      icon={<LockKeyhole className="h-5 w-5" />}
      actions={<Button variant="outline" onClick={() => void load()} disabled={busy}><RefreshCw className="mr-2 h-4 w-4" /> Refresh</Button>}
    >
      {error ? <InlineAlert variant="error" title="Governance action failed">{error}</InlineAlert> : null}

      <Card className={status?.gate.isReleased ? 'border-emerald-300' : 'border-amber-300'}>
        <CardHeader><CardTitle className="flex items-center gap-2 text-base">
          {status?.gate.isReleased ? <CheckCircle2 className="h-5 w-5 text-emerald-600" /> : <ShieldAlert className="h-5 w-5 text-amber-600" />}
          {status?.gate.isReleased ? 'Released for this profession' : 'Release blocked by explicit owner gates'}
        </CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <Input label="Profession scope" value={professionId} onChange={(event) => setProfessionId(event.target.value)} />
            <div className="text-xs text-muted">Spec: <span className="font-mono">{status?.gate.specVersion ?? '—'}</span></div>
            <div className="text-xs text-muted">Rubric: <span className="font-mono">{status?.gate.rubricVersion ?? '—'}</span></div>
          </div>
          {status?.gate.blockingReasons?.length ? (
            <div className="flex flex-wrap gap-2">
              {status.gate.blockingReasons.map((reason) => <Badge key={reason} variant="warning">{reason}</Badge>)}
            </div>
          ) : <p className="text-sm text-emerald-700">Every configured gate is approved for this profession.</p>}
        </CardContent>
      </Card>

      <div className="grid gap-5 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle className="text-base">Create spec release</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            <Input label="Spec version" value="speaking-simulation-v1.1" readOnly />
            <Input label="Release version" value={specReleaseVersion} onChange={(event) => setSpecReleaseVersion(event.target.value)} />
            <Button disabled={busy || !specReleaseVersion.trim()} onClick={() => void run(() => createSpeakingSimulationV11SpecRelease({ specVersion: 'speaking-simulation-v1.1', releaseVersion: specReleaseVersion }))}>Create draft</Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle className="text-base">Create exact rubric release</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            <Input label="Rubric version" value={rubricVersion} onChange={(event) => setRubricVersion(event.target.value)} />
            <Input label="Calibration version" value={calibrationVersion} onChange={(event) => setCalibrationVersion(event.target.value)} />
            <p className="text-xs text-muted">The ten released criteria total <span className={weightTotal === 100 ? 'font-semibold text-emerald-700' : 'font-semibold text-rose-700'}>{weightTotal}%</span>. Rule 55 is excluded.</p>
            <Button disabled={busy || weightTotal !== 100} onClick={() => void run(() => createSpeakingSimulationV11RubricRelease({ rubricVersion, calibrationVersion, criteria }))}>Create draft</Button>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader><CardTitle className="text-base">Create owner approval request</CardTitle></CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2">
          <Select label="Approval gate" value={approvalKey} onChange={(event) => setApprovalKey(event.target.value)} options={APPROVAL_OPTIONS} />
          <Input label="Scope (global or profession id)" value={approvalScope} onChange={(event) => setApprovalScope(event.target.value)} />
          <Input label="Numeric value (required for budget/SLA gates)" type="number" step="0.01" value={approvalValue} onChange={(event) => setApprovalValue(event.target.value)} />
          <Textarea label="Evidence JSON (audio approval must name azure-phoneme)" rows={3} value={approvalEvidence} onChange={(event) => setApprovalEvidence(event.target.value)} />
          <div className="md:col-span-2"><Button disabled={busy || !status?.gate.specVersion || !status?.gate.rubricVersion} onClick={() => void run(() => createSpeakingSimulationV11Approval({ approvalKey, scopeKey: approvalScope || 'global', specVersion: status?.gate.specVersion ?? 'speaking-simulation-v1.1', rubricVersion: status?.gate.rubricVersion ?? 'speaking-simulation-v1.1-rubric', numericValue: approvalValue ? Number(approvalValue) : null, evidenceJson: approvalEvidence || null }))}>Create pending approval</Button></div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Release and approval history</CardTitle></CardHeader>
        <CardContent className="space-y-6">
          <div className="overflow-x-auto">
            <h3 className="mb-2 text-sm font-semibold">Spec releases</h3>
            <table className="min-w-full text-sm"><tbody>
              {status?.specReleases.map((row) => <tr key={row.id} className="border-b border-border"><td className="py-2 pr-3 font-mono text-xs">{row.releaseVersion}</td><td className="py-2 pr-3"><Badge variant={statusTone(row.status)}>{row.status}</Badge></td><td className="py-2 text-right">{row.status.toLowerCase() === 'draft' ? <Button size="sm" onClick={() => void run(() => approveSpeakingSimulationV11SpecRelease(row.id))}>Approve</Button> : null}</td></tr>)}
            </tbody></table>
          </div>
          <div className="overflow-x-auto">
            <h3 className="mb-2 text-sm font-semibold">Rubric releases</h3>
            <table className="min-w-full text-sm"><tbody>
              {status?.rubricReleases.map((row) => <tr key={row.id} className="border-b border-border"><td className="py-2 pr-3 font-mono text-xs">{row.rubricVersion}</td><td className="py-2 pr-3"><Badge variant={statusTone(row.status)}>{row.status}</Badge></td><td className="py-2 text-right">{row.status.toLowerCase() === 'draft' ? <Button size="sm" onClick={() => void run(() => approveSpeakingSimulationV11RubricRelease(row.id))}>Approve</Button> : null}</td></tr>)}
            </tbody></table>
          </div>
          <div className="overflow-x-auto">
            <h3 className="mb-2 text-sm font-semibold">Owner approvals</h3>
            <table className="min-w-full text-sm"><thead><tr className="border-b border-border text-left text-xs uppercase text-muted"><th className="py-2 pr-3">Gate</th><th className="py-2 pr-3">Scope</th><th className="py-2 pr-3">Value</th><th className="py-2 pr-3">Status</th><th className="py-2 text-right">Action</th></tr></thead><tbody>
              {status?.approvals.map((row) => <tr key={row.id} className="border-b border-border"><td className="py-2 pr-3 font-mono text-xs">{row.approvalKey}</td><td className="py-2 pr-3">{row.scopeKey}</td><td className="py-2 pr-3">{row.numericValue ?? '—'}</td><td className="py-2 pr-3"><Badge variant={statusTone(row.status)}>{row.status}</Badge></td><td className="flex justify-end gap-2 py-2 text-right">{row.status.toLowerCase() === 'pending' ? <><Button size="sm" variant="outline" onClick={() => void run(() => rejectSpeakingSimulationV11Approval(row.id))}>Reject</Button><Button size="sm" onClick={() => void run(() => approveSpeakingSimulationV11Approval(row.id))}>Approve</Button></> : null}</td></tr>)}
            </tbody></table>
          </div>
        </CardContent>
      </Card>
    </AdminOperationsLayout>
  );
}
