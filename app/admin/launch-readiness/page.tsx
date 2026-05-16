'use client';

import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, Rocket, Save, ShieldAlert, type LucideIcon } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import {
  fetchAdminLaunchReadinessSettings,
  updateAdminLaunchReadinessSettings,
  type AdminLaunchReadinessSettings,
} from '@/lib/api';

type LaunchField = keyof AdminLaunchReadinessSettings;

const statusOptions = [
  { value: '', label: 'Not set' },
  { value: 'pending', label: 'Pending' },
  { value: 'blocked', label: 'Blocked' },
  { value: 'evidence-required', label: 'Evidence required' },
  { value: 'approved', label: 'Approved' },
  { value: 'complete', label: 'Complete' },
  { value: 'failed', label: 'Failed' },
];

const sections: Array<{
  title: string;
  description: string;
  fields: Array<{ key: LaunchField; label: string; kind?: 'text' | 'url' | 'version' | 'status' | 'textarea' | 'checkbox' }>;
}> = [
  {
    title: 'Mobile release policy',
    description: 'Server-driven version gates and public store links used by native clients.',
    fields: [
      { key: 'mobileMinSupportedVersion', label: 'Minimum supported mobile version', kind: 'version' },
      { key: 'mobileLatestVersion', label: 'Latest mobile version', kind: 'version' },
      { key: 'mobileForceUpdate', label: 'Force mobile update', kind: 'checkbox' },
      { key: 'iosAppStoreUrl', label: 'iOS App Store URL', kind: 'url' },
      { key: 'androidPlayStoreUrl', label: 'Android Play Store URL', kind: 'url' },
    ],
  },
  {
    title: 'Mobile signing and associations',
    description: 'Evidence references only. Raw certificates, profiles, private keys, and store secrets stay outside the database.',
    fields: [
      { key: 'iosBundleId', label: 'iOS bundle ID' },
      { key: 'appleTeamId', label: 'Apple Team ID' },
      { key: 'appleAssociatedDomainStatus', label: 'Apple associated domains status', kind: 'status' },
      { key: 'appleUniversalLinksStatus', label: 'Apple universal links status', kind: 'status' },
      { key: 'iosSigningProfileReference', label: 'iOS signing profile reference' },
      { key: 'iosIapStatus', label: 'iOS IAP status', kind: 'status' },
      { key: 'iosPushStatus', label: 'iOS push status', kind: 'status' },
      { key: 'androidPackageName', label: 'Android package name' },
      { key: 'androidSha256Fingerprints', label: 'Android SHA-256 fingerprints', kind: 'textarea' },
      { key: 'androidSigningKeyReference', label: 'Android signing key reference' },
      { key: 'androidAssetLinksStatus', label: 'Android assetlinks status', kind: 'status' },
      { key: 'androidIapStatus', label: 'Android IAP status', kind: 'status' },
      { key: 'androidPushStatus', label: 'Android push status', kind: 'status' },
    ],
  },
  {
    title: 'Desktop release policy',
    description: 'Version gates, signed update feed location, and platform signing evidence.',
    fields: [
      { key: 'desktopMinSupportedVersion', label: 'Minimum supported desktop version', kind: 'version' },
      { key: 'desktopLatestVersion', label: 'Latest desktop version', kind: 'version' },
      { key: 'desktopForceUpdate', label: 'Force desktop update', kind: 'checkbox' },
      { key: 'desktopUpdateFeedUrl', label: 'Desktop update feed URL', kind: 'url' },
      { key: 'desktopUpdateChannel', label: 'Desktop update channel' },
      { key: 'windowsSigningStatus', label: 'Windows signing status', kind: 'status' },
      { key: 'macSigningStatus', label: 'macOS signing status', kind: 'status' },
      { key: 'linuxSigningStatus', label: 'Linux signing status', kind: 'status' },
    ],
  },
  {
    title: 'Realtime STT release evidence',
    description: 'Operational approvals that must exist before enabling real provider-backed speech for production audiences.',
    fields: [
      { key: 'realtimeLegalApprovalStatus', label: 'Legal approval status', kind: 'status' },
      { key: 'realtimePrivacyApprovalStatus', label: 'Privacy approval status', kind: 'status' },
      { key: 'realtimeProtectedSmokeStatus', label: 'Protected live smoke status', kind: 'status' },
      { key: 'realtimeEvidenceUrl', label: 'Realtime evidence URL', kind: 'url' },
      { key: 'realtimeSpendCapApproved', label: 'Spend cap approved', kind: 'checkbox' },
      { key: 'realtimeTopologyApproved', label: 'Topology approved', kind: 'checkbox' },
    ],
  },
  {
    title: 'Launch signoff',
    description: 'Final device validation and owner approval references.',
    fields: [
      { key: 'deviceValidationEvidenceUrl', label: 'Device validation evidence URL', kind: 'url' },
      { key: 'deviceValidationNotes', label: 'Device validation notes', kind: 'textarea' },
      { key: 'releaseOwnerApprovalStatus', label: 'Release owner approval status', kind: 'status' },
      { key: 'launchNotes', label: 'Launch notes', kind: 'textarea' },
    ],
  },
];

export default function AdminLaunchReadinessPage() {
  const [settings, setSettings] = useState<AdminLaunchReadinessSettings | null>(null);
  const [draft, setDraft] = useState<Partial<AdminLaunchReadinessSettings>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setSettings(await fetchAdminLaunchReadinessSettings());
      setDraft({});
    } catch (error) {
      setToast({ variant: 'error', message: error instanceof Error ? error.message : 'Failed to load launch settings.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const value = <K extends LaunchField>(key: K): AdminLaunchReadinessSettings[K] | undefined =>
    (draft[key] as AdminLaunchReadinessSettings[K] | undefined) ?? settings?.[key];

  function setDraftField(key: LaunchField, next: string | boolean | null) {
    setDraft((prev) => ({ ...prev, [key]: next }));
  }

  async function save() {
    setSaving(true);
    try {
      const updated = await updateAdminLaunchReadinessSettings(draft);
      setSettings(updated);
      setDraft({});
      setToast({ variant: 'success', message: 'Launch readiness settings saved.' });
    } catch (error) {
      setToast({ variant: 'error', message: error instanceof Error ? error.message : 'Save failed.' });
    } finally {
      setSaving(false);
    }
  }

  const completedCount = settings
    ? [
        settings.releaseOwnerApprovalStatus,
        settings.appleAssociatedDomainStatus,
        settings.androidAssetLinksStatus,
        settings.windowsSigningStatus,
        settings.realtimeLegalApprovalStatus,
        settings.realtimePrivacyApprovalStatus,
        settings.realtimeProtectedSmokeStatus,
      ].filter((x) => x === 'approved' || x === 'complete').length
    : 0;

  return (
    <>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="System admin"
            title="Launch Readiness"
            description="Typed, admin-editable release gates for mobile, desktop, and realtime STT. This page never stores signing keys, certificates, provider secrets, or private app-store credentials."
            icon={Rocket}
            actions={
              <Button variant="primary" onClick={save} disabled={saving || loading || Object.keys(draft).length === 0}>
                <Save className="mr-1 h-4 w-4" />
                {saving ? 'Saving...' : 'Save changes'}
              </Button>
            }
          />

          {loading || !settings ? (
            <p className="text-sm text-muted">Loading launch readiness settings...</p>
          ) : (
            <div className="space-y-8">
              <div className="grid gap-3 md:grid-cols-3">
                <SummaryCard label="Evidence gates approved/complete" value={`${completedCount}/7`} icon={CheckCircle2} />
                <SummaryCard label="Mobile force update" value={value('mobileForceUpdate') ? 'Enabled' : 'Disabled'} icon={ShieldAlert} />
                <SummaryCard label="Desktop force update" value={value('desktopForceUpdate') ? 'Enabled' : 'Disabled'} icon={ShieldAlert} />
              </div>

              {sections.map((section) => (
                <section key={section.title} className="rounded-3xl border border-border bg-surface/70 p-5 shadow-sm">
                  <div className="mb-4">
                    <h2 className="text-lg font-semibold text-navy">{section.title}</h2>
                    <p className="mt-1 text-sm text-muted">{section.description}</p>
                  </div>
                  <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                    {section.fields.map((field) => (
                      <LaunchInput
                        key={field.key}
                        field={field}
                        value={value(field.key)}
                        onChange={(next) => setDraftField(field.key, next)}
                      />
                    ))}
                  </div>
                </section>
              ))}

              <p className="text-xs text-muted">
                Last updated {new Date(settings.updatedAt).toLocaleString()} by {settings.updatedByAdminName || settings.updatedByAdminId || 'system'}.
              </p>
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}

function LaunchInput({
  field,
  value,
  onChange,
}: {
  field: { key: LaunchField; label: string; kind?: 'text' | 'url' | 'version' | 'status' | 'textarea' | 'checkbox' };
  value: AdminLaunchReadinessSettings[LaunchField] | undefined;
  onChange: (next: string | boolean | null) => void;
}) {
  if (field.kind === 'checkbox') {
    return (
      <Checkbox
        label={field.label}
        checked={Boolean(value)}
        onChange={(event) => onChange(event.target.checked)}
      />
    );
  }

  if (field.kind === 'status') {
    return (
      <Select
        label={field.label}
        options={statusOptions}
        value={String(value ?? '')}
        onChange={(event) => onChange(event.target.value || null)}
      />
    );
  }

  if (field.kind === 'textarea') {
    return (
      <Textarea
        label={field.label}
        value={String(value ?? '')}
        rows={4}
        onChange={(event) => onChange(event.target.value || null)}
      />
    );
  }

  return (
    <Input
      label={field.label}
      type={field.kind === 'url' ? 'url' : 'text'}
      value={String(value ?? '')}
      inputMode={field.kind === 'version' ? 'decimal' : undefined}
      placeholder={field.kind === 'version' ? '1.0.0' : undefined}
      onChange={(event) => onChange(event.target.value || null)}
    />
  );
}

function SummaryCard({ label, value, icon: Icon }: { label: string; value: string; icon: LucideIcon }) {
  return (
    <div className="rounded-3xl border border-border bg-background-light p-4 shadow-sm">
      <div className="flex items-center gap-3">
        <span className="rounded-2xl bg-primary/10 p-2 text-primary">
          <Icon className="h-5 w-5" />
        </span>
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted">{label}</p>
          <p className="mt-1 text-lg font-semibold text-navy">{value}</p>
        </div>
      </div>
    </div>
  );
}
