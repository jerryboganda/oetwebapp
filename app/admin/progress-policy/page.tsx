'use client';

import { useCallback, useEffect, useState } from 'react';
import { TrendingUp } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getProgressPolicy, updateProgressPolicy, type ProgressPolicyDto } from '@/lib/progress-policy-admin-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const RANGE_OPTIONS = [
  { value: '14d', label: '14 days' },
  { value: '30d', label: '30 days' },
  { value: '90d', label: '90 days' },
  { value: 'all', label: 'All time' },
];

export default function ProgressPolicyAdminPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [policy, setPolicy] = useState<ProgressPolicyDto | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [saving, setSaving] = useState(false);

  const [defaultTimeRange, setDefaultTimeRange] = useState('90d');
  const [smoothingWindow, setSmoothingWindow] = useState('3');
  const [minCohortSize, setMinCohortSize] = useState('30');
  const [minEvaluationsForTrend, setMinEvaluationsForTrend] = useState('2');
  const [mockDistinctStyle, setMockDistinctStyle] = useState(true);
  const [showScoreGuaranteeStrip, setShowScoreGuaranteeStrip] = useState(true);
  const [showCriterionConfidenceBand, setShowCriterionConfidenceBand] = useState(true);
  const [exportPdfEnabled, setExportPdfEnabled] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const p = await getProgressPolicy('oet');
      setPolicy(p);
      setDefaultTimeRange(p.defaultTimeRange);
      setSmoothingWindow(String(p.smoothingWindow));
      setMinCohortSize(String(p.minCohortSize));
      setMinEvaluationsForTrend(String(p.minEvaluationsForTrend));
      setMockDistinctStyle(p.mockDistinctStyle);
      setShowScoreGuaranteeStrip(p.showScoreGuaranteeStrip);
      setShowCriterionConfidenceBand(p.showCriterionConfidenceBand);
      setExportPdfEnabled(p.exportPdfEnabled);
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
      await updateProgressPolicy('oet', {
        defaultTimeRange: defaultTimeRange as ProgressPolicyDto['defaultTimeRange'],
        smoothingWindow: parseInt(smoothingWindow, 10),
        minCohortSize: parseInt(minCohortSize, 10),
        minEvaluationsForTrend: parseInt(minEvaluationsForTrend, 10),
        mockDistinctStyle,
        showScoreGuaranteeStrip,
        showCriterionConfidenceBand,
        exportPdfEnabled,
      });
      setToast({ variant: 'success', message: 'Progress policy saved.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<TrendingUp className="w-6 h-6" />}
        title="Progress Policy"
        description="Tune the learner Progress dashboard: default time range, cohort gating, AI signals, and the PDF export kill-switch."
      />

      <AsyncStateWrapper status={status}>
        {policy && (
          <>
            <AdminRoutePanel eyebrow="Defaults" title="Time range and trend behaviour" dense>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                <Select
                  label="Default time range"
                  value={defaultTimeRange}
                  onChange={(e) => setDefaultTimeRange(e.target.value)}
                  options={RANGE_OPTIONS}
                />
                <div>
                  <Input
                    label="Trend smoothing window"
                    type="number"
                    value={smoothingWindow}
                    onChange={(e) => setSmoothingWindow(e.target.value)}
                  />
                  <p className="mt-1 text-[11px] text-muted">Rolling average over the last N attempts. 0 disables smoothing.</p>
                </div>
                <div>
                  <Input
                    label="Min evaluations to unlock trend"
                    type="number"
                    value={minEvaluationsForTrend}
                    onChange={(e) => setMinEvaluationsForTrend(e.target.value)}
                  />
                  <p className="mt-1 text-[11px] text-muted">Below this count, the trend chart shows the empty state.</p>
                </div>
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel eyebrow="Comparative" title="Cohort gating" dense>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div>
                  <Input
                    label="Minimum cohort size"
                    type="number"
                    value={minCohortSize}
                    onChange={(e) => setMinCohortSize(e.target.value)}
                  />
                  <p className="mt-1 text-[11px] text-muted">Below this number of peers we show &lsquo;cohort insights unlock at N peers&rsquo; instead of stats.</p>
                </div>
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel eyebrow="Toggles" title="Display and feature kill-switches" dense>
              <div className="space-y-3">
                <Switch label="Show mocks as a dashed series" checked={mockDistinctStyle} onChange={setMockDistinctStyle} />
                <Switch label="Show Score Guarantee strip" checked={showScoreGuaranteeStrip} onChange={setShowScoreGuaranteeStrip} />
                <Switch label="Show 95% confidence band on criterion charts" checked={showCriterionConfidenceBand} onChange={setShowCriterionConfidenceBand} />
                <Switch label="Enable PDF export endpoint (legal kill-switch)" checked={exportPdfEnabled} onChange={setExportPdfEnabled} />
              </div>
            </AdminRoutePanel>

            <div className="flex justify-end">
              <Button variant="primary" onClick={() => void onSave()} loading={saving}>Save policy</Button>
            </div>

            <p className="text-xs text-muted">
              Updated {new Date(policy.updatedAt).toLocaleString()} {policy.updatedByAdminId ? `by ${policy.updatedByAdminId}` : ''}.
              Changes apply on the next learner reload of <code>/progress</code>.
            </p>
          </>
        )}
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}

function Switch({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex items-center gap-2 text-sm">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  );
}
