'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Switch } from '@/components/ui/switch';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getDriftPolicy, updateDriftPolicy, type DriftPolicyDto } from '@/lib/study-planner-admin-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function StudyPlannerDriftPolicyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [policy, setPolicy] = useState<DriftPolicyDto | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [saving, setSaving] = useState(false);

  const [mild, setMild] = useState('3');
  const [moderate, setModerate] = useState('7');
  const [severe, setSevere] = useState('14');
  const [mildCopy, setMildCopy] = useState('');
  const [moderateCopy, setModerateCopy] = useState('');
  const [severeCopy, setSevereCopy] = useState('');
  const [onTrackCopy, setOnTrackCopy] = useState('');
  const [autoMod, setAutoMod] = useState(false);
  const [autoSev, setAutoSev] = useState(true);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const p = await getDriftPolicy('oet');
      setPolicy(p);
      setMild(String(p.mildDays)); setModerate(String(p.moderateDays)); setSevere(String(p.severeDays));
      setMildCopy(p.mildCopy); setModerateCopy(p.moderateCopy); setSevereCopy(p.severeCopy); setOnTrackCopy(p.onTrackCopy);
      setAutoMod(p.autoRegenerateOnModerate); setAutoSev(p.autoRegenerateOnSevere);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const onSave = async () => {
    setSaving(true);
    try {
      await updateDriftPolicy('oet', {
        mildDays: parseInt(mild, 10),
        moderateDays: parseInt(moderate, 10),
        severeDays: parseInt(severe, 10),
        mildCopy, moderateCopy, severeCopy, onTrackCopy,
        autoRegenerateOnModerate: autoMod,
        autoRegenerateOnSevere: autoSev,
      });
      setToast({ variant: 'success', message: 'Drift policy saved.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<AlertTriangle className="w-6 h-6" />}
        title="Drift Policy"
        description="Configure when a plan is considered 'drifting'. The learner-facing drift page uses these thresholds and copy."
      />

      <AsyncStateWrapper status={status}>
        {policy && (
          <>
            <AdminRoutePanel eyebrow="Thresholds" title="Days of drift before each level" dense>
              <div className="grid grid-cols-3 gap-3">
                <Input label="Mild (days)" type="number" value={mild} onChange={(e) => setMild(e.target.value)} />
                <Input label="Moderate (days)" type="number" value={moderate} onChange={(e) => setModerate(e.target.value)} />
                <Input label="Severe (days)" type="number" value={severe} onChange={(e) => setSevere(e.target.value)} />
              </div>
              <p className="text-xs text-muted mt-2">Thresholds must be strictly increasing: Mild &lt; Moderate &lt; Severe.</p>
            </AdminRoutePanel>

            <AdminRoutePanel eyebrow="Auto-regeneration" title="When should the planner rebuild without asking?">
              <Switch
                checked={autoMod}
                onCheckedChange={setAutoMod}
                label="Auto-regenerate on moderate drift"
                description="Silently rebuild the plan when drift reaches the moderate threshold."
              />
              <Switch
                checked={autoSev}
                onCheckedChange={setAutoSev}
                label="Auto-regenerate on severe drift"
                description="Silently rebuild the plan when drift reaches the severe threshold."
              />
            </AdminRoutePanel>

            <AdminRoutePanel eyebrow="Copy" title="Messages shown on the learner drift page">
              <div className="space-y-3">
                <CopyField label="On-track copy" value={onTrackCopy} onChange={setOnTrackCopy} />
                <CopyField label="Mild copy" value={mildCopy} onChange={setMildCopy} />
                <CopyField label="Moderate copy" value={moderateCopy} onChange={setModerateCopy} />
                <CopyField label="Severe copy" value={severeCopy} onChange={setSevereCopy} />
              </div>
            </AdminRoutePanel>

            <div className="flex justify-end">
              <Button variant="primary" onClick={() => void onSave()} loading={saving}>Save policy</Button>
            </div>
          </>
        )}
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}

function CopyField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <Textarea
      label={label}
      rows={2}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}
