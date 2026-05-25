'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Save, ShieldCheck, Users } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Checkbox } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Toast } from '@/components/ui/alert';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Experts', href: '/admin/users?tab=tutors' },
  { label: 'Specialties' },
];
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  fetchExpertSpecialties,
  updateExpertSpecialties,
  type ExpertSpecialtiesRow,
} from '@/lib/expert-admin-api';

const KNOWN_PROFESSIONS = [
  'medicine',
  'nursing',
  'dentistry',
  'pharmacy',
  'physiotherapy',
  'veterinary',
  'optometry',
  'radiography',
  'occupational-therapy',
  'speech-pathology',
  'podiatry',
  'dietetics',
] as const;

type LoadStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

function professionLabel(value: string): string {
  return value.replace(/-/g, ' ').replace(/\b\w/g, (m) => m.toUpperCase());
}

export default function ExpertSpecialtiesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [status, setStatus] = useState<LoadStatus>('loading');
  const [rows, setRows] = useState<ExpertSpecialtiesRow[]>([]);
  const [drafts, setDrafts] = useState<Record<string, Set<string>>>({});
  const [saving, setSaving] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await fetchExpertSpecialties();
      setRows(data);
      const initial: Record<string, Set<string>> = {};
      for (const r of data) {
        initial[r.id] = new Set(r.specialties);
      }
      setDrafts(initial);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load experts: ${(e as Error).message}` });
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  const toggleSpecialty = useCallback((expertId: string, profession: string) => {
    setDrafts((prev) => {
      const next = { ...prev };
      const current = new Set(prev[expertId] ?? []);
      if (current.has(profession)) current.delete(profession);
      else current.add(profession);
      next[expertId] = current;
      return next;
    });
  }, []);

  const isDirty = useCallback((expertId: string) => {
    const row = rows.find((r) => r.id === expertId);
    if (!row) return false;
    const draft = drafts[expertId] ?? new Set<string>();
    if (draft.size !== row.specialties.length) return true;
    return row.specialties.some((s) => !draft.has(s));
  }, [drafts, rows]);

  const save = useCallback(async (expertId: string) => {
    if (!canWrite) return;
    const draft = Array.from(drafts[expertId] ?? new Set<string>());
    setSaving(expertId);
    try {
      const result = await updateExpertSpecialties(expertId, draft);
      setRows((prev) => prev.map((r) => (r.id === expertId ? { ...r, specialties: result.specialties } : r)));
      setDrafts((prev) => ({ ...prev, [expertId]: new Set(result.specialties) }));
      setToast({ variant: 'success', message: `Saved specialties for ${result.displayName}.` });
    } catch (e) {
      const err = e as Error & { detail?: { error?: string } };
      setToast({ variant: 'error', message: err.detail?.error || err.message || 'Save failed.' });
    } finally {
      setSaving(null);
    }
  }, [canWrite, drafts]);

  const summary = useMemo(() => {
    let withSpecialties = 0;
    let generalists = 0;
    for (const r of rows) {
      if (r.specialties.length > 0) withSpecialties++;
      else generalists++;
    }
    return { total: rows.length, withSpecialties, generalists };
  }, [rows]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Expert specialties" breadcrumbs={BREADCRUMBS}>
        <p className="text-sm text-admin-fg-muted">Admin access required.</p>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Expert specialties"
      description="Curate per-expert profession lists. The auto-assigner uses these to route writing reviews to a competent reviewer. An empty list means 'generalist' — the expert is eligible for every profession."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Experts"
      icon={<ShieldCheck className="h-5 w-5" />}
      backHref="/admin/users?tab=tutors"
    >
      <SettingsSection title="Coverage">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <KpiTile label="Total experts" value={summary.total} icon={<Users className="h-4 w-4" />} />
          <KpiTile label="With specialties" value={summary.withSpecialties} tone="success" />
          <KpiTile label="Generalists (no list)" value={summary.generalists} tone="warning" />
        </div>
      </SettingsSection>

      {status === 'loading' && (
        <SettingsSection title="Experts">
          <Skeleton className="h-44 rounded-admin-lg" />
        </SettingsSection>
      )}

      <AsyncStateWrapper status={status}>
        {rows.map((row) => {
          const draft = drafts[row.id] ?? new Set<string>();
          const dirty = isDirty(row.id);
          return (
            <Card key={row.id}>
              <CardHeader>
                <CardTitle className="text-sm">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="text-sm font-semibold text-admin-fg-strong">{row.displayName}</div>
                      <div className="text-xs text-admin-fg-muted">{row.email}</div>
                    </div>
                    <div className="flex items-center gap-2">
                      {row.isActive ? <Badge variant="success">Active</Badge> : <Badge variant="default">Inactive</Badge>}
                      {draft.size === 0 ? (
                        <Badge variant="warning">Generalist</Badge>
                      ) : (
                        <Badge variant="info">{draft.size} profession{draft.size === 1 ? '' : 's'}</Badge>
                      )}
                    </div>
                  </div>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex flex-col gap-3">
                  <div className="grid grid-cols-2 gap-2 md:grid-cols-3 lg:grid-cols-4">
                    {KNOWN_PROFESSIONS.map((p) => (
                      <Checkbox
                        key={`${row.id}-${p}`}
                        checked={draft.has(p)}
                        disabled={!canWrite || saving === row.id}
                        onChange={() => toggleSpecialty(row.id, p)}
                        label={professionLabel(p)}
                      />
                    ))}
                  </div>
                  {canWrite && (
                    <div className="flex items-center justify-end gap-2">
                      {dirty && <span className="text-xs text-admin-warning">Unsaved changes</span>}
                      <Button
                        variant="primary"
                        size="sm"
                        disabled={!dirty || saving === row.id}
                        onClick={() => void save(row.id)}
                      >
                        <Save className="mr-1 h-4 w-4" /> Save
                      </Button>
                    </div>
                  )}
                </div>
              </CardContent>
            </Card>
          );
        })}
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}
