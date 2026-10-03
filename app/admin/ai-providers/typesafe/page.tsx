'use client';

/**
 * TypeSafe / Jev status — read-only. Jev is the typed-judgment layer that sits
 * beside Claude Max and Codex (it never grades). Every flag and threshold here
 * is an environment setting read at API start, so this page only reports; the
 * key itself is managed on the `typesafe-jev` row of /admin/ai-providers.
 * The API never returns the key: presence flags and the row's last-4 hint only.
 * See docs/env/typesafe.md.
 */

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Activity, AlertTriangle, RefreshCw } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  fetchTypeSafeStatus,
  type TypeSafeSetting,
  type TypeSafeStatus,
  type TypeSafeUsage,
} from '@/lib/ai-management-api';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'AI Providers', href: '/admin/ai-providers' },
  { label: 'TypeSafe / Jev' },
];

// docs/env/typesafe.md "Flag-flip order": one surface at a time, each after a green jev-calibrate run.
const FLIP_ORDER = [
  'TYPESAFE__ENABLED=true with the key provisioned and every surface flag still off. Confirm a clean start and the model-pin log line.',
  'Owner console triage (no learner impact).',
  'Reading / Listening explanation review (explanation features only, time-boxed).',
  'Writing citation verify and criteria advisory (shadow: tutor flags and advisory fields only).',
  'AI-patient turn advisory (Speaking shadow).',
  'Remaining advisory surfaces one at a time after a green calibration run (conversation and pronunciation checks, Writing helper routing, Listening gaps, mock weakness, answer-key triage, extraction verification, Model Answer review).',
  'Writing submission guard, last: the only surface that can stop a submission.',
];

const fmtInt = (n: number) => n.toLocaleString();
const fmtUsd = (n: number) => `$${n.toFixed(4)}`;
const fmtMs = (n: number) => (n > 0 ? `${fmtInt(n)} ms` : '-');

function OnOff({ on }: { on: boolean }) {
  return on ? <Badge variant="success">On</Badge> : <Badge variant="muted">Off</Badge>;
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-2xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</dt>
      <dd className="mt-1 flex flex-wrap items-center gap-2 text-sm text-admin-fg-strong">{children}</dd>
    </div>
  );
}

const th = 'px-3 py-2 text-start text-2xs font-semibold uppercase tracking-wider text-admin-fg-muted';
const thNum = `${th} text-end`;
const td = 'px-3 py-2 align-middle';
const tdNum = `${td} text-end tabular-nums`;

function keyNote(key: TypeSafeStatus['key']): string {
  if (!key.effectiveKeyAvailable) {
    return 'No usable key: every call returns "disabled" and callers carry on without Jev.';
  }
  const inactiveRowKey = key.providerRowKeyConfigured && !key.providerRowActive;
  if (key.providerRowKeyConfigured && key.providerRowActive) return 'Calls use the provider-row key.';
  return inactiveRowKey
    ? 'The provider row holds a key but is inactive, so calls skip it and use the environment key.'
    : 'Calls use the environment key.';
}

function SettingsTable({ caption, rows, format }: { caption: string; rows: TypeSafeSetting[]; format: (n: number) => string }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="border-b border-admin-border">
          <tr>
            <th scope="col" className={th}>Setting</th>
            <th scope="col" className={thNum}>Value</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border">
          {rows.map((r) => (
            <tr key={r.name}>
              <td className={`${td} font-mono text-xs text-admin-fg-default`}>{r.envVar}</td>
              <td className={`${tdNum} text-admin-fg-strong`}>{format(r.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function TypeSafeStatusPage() {
  const [status, setStatus] = useState<TypeSafeStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setStatus(await fetchTypeSafeStatus());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load TypeSafe status');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const actions = (
    <div className="flex items-center gap-2">
      <Button variant="outline" size="sm" onClick={() => void load()} disabled={loading}>
        <RefreshCw className="mr-1.5 h-4 w-4" aria-hidden="true" />
        Refresh
      </Button>
      <Button variant="primary" size="sm" asChild>
        <Link href="/admin/ai-providers">Manage key and Test connection</Link>
      </Button>
    </div>
  );

  const layoutProps = {
    title: 'TypeSafe / Jev',
    eyebrow: 'AI & Automation',
    breadcrumbs: BREADCRUMBS,
    icon: <Activity className="h-5 w-5" />,
    actions,
  };

  if (!status) {
    return (
      <AdminSettingsLayout {...layoutProps}>
        {error ? (
          <Card surface="tinted-danger">
            <CardContent className="p-4">
              <div role="alert" className="flex items-start gap-2 text-sm text-admin-danger">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <span>{error}</span>
              </div>
            </CardContent>
          </Card>
        ) : (
          <>
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-64 w-full" />
          </>
        )}
      </AdminSettingsLayout>
    );
  }

  const usageRows: Array<TypeSafeUsage & { name: string; envVar: string | null; enabled: boolean | null }> = [
    ...status.surfaces,
    ...status.otherUsage.map((u) => ({ ...u, name: 'Other Jev code (no flag)', envVar: null, enabled: null })),
  ];
  const totals = usageRows.reduce(
    (acc, r) => ({ calls: acc.calls + r.calls, failures: acc.failures + r.failures, costUsd: acc.costUsd + r.costUsd }),
    { calls: 0, failures: 0, costUsd: 0 },
  );
  const flagsWithoutMaster = !status.enabled && status.surfaces.some((s) => s.enabled);

  return (
    <AdminSettingsLayout
      {...layoutProps}
      description="Read-only status of the typed-judgment layer beside Claude Max and Codex. Jev flags and advises; it never grades or changes a score."
    >
      {error && (
        <Card surface="tinted-danger">
          <CardContent className="p-4">
            <div role="alert" className="flex items-start gap-2 text-sm text-admin-danger">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>{error}</span>
            </div>
          </CardContent>
        </Card>
      )}

      <SettingsSection
        title="Key and provider"
        description="The key lives on the typesafe-jev provider row (or the server environment) and is never shown here."
      >
        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
          <Fact label="Master switch">
            <OnOff on={status.enabled} />
            <span className="font-mono text-xs text-admin-fg-muted">TYPESAFE__ENABLED</span>
          </Fact>
          <Fact label="Pinned model">
            <code className="font-mono text-xs">{status.model}</code>
          </Fact>
          <Fact label="Writing guard Block">
            {status.guardEnforced ? (
              <Badge variant="warning">May skip the paid grade</Badge>
            ) : (
              <Badge variant="muted">Record and flag only</Badge>
            )}
          </Fact>
          <Fact label="Environment key">
            {status.key.envKeyConfigured ? <Badge variant="success">Configured</Badge> : <Badge variant="muted">Not set</Badge>}
          </Fact>
          <Fact label={`Provider row (${status.providerCode})`}>
            {!status.key.providerRowExists ? (
              <Badge variant="muted">No row</Badge>
            ) : (
              <>
                {status.key.providerRowKeyConfigured ? <Badge variant="success">Key set</Badge> : <Badge variant="muted">No key</Badge>}
                {status.key.providerRowActive ? <Badge variant="success">Active</Badge> : <Badge variant="warning">Inactive</Badge>}
                {status.key.apiKeyHint ? <code className="font-mono text-xs text-admin-fg-muted">{status.key.apiKeyHint}</code> : null}
              </>
            )}
          </Fact>
          <Fact label="Key used at call time">
            {status.key.effectiveKeyAvailable ? <Badge variant="success">Available</Badge> : <Badge variant="danger">Missing</Badge>}
          </Fact>
        </dl>
        <p className="mt-3 text-xs text-admin-fg-muted">{keyNote(status.key)}</p>
      </SettingsSection>

      <SettingsSection
        title={`Surfaces (last ${status.usageWindowDays} days)`}
        description="One row per Jev feature code, in the recommended flip order. Usage counts typesafe-jev calls only."
      >
        {flagsWithoutMaster && (
          <p role="status" className="mb-3 flex items-start gap-2 text-xs text-admin-warning">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>A surface flag is on but the master switch is off, so every flag is a no-op until TYPESAFE__ENABLED is true.</span>
          </p>
        )}
        <div className="overflow-x-auto">
          <table className="w-full text-sm" data-testid="typesafe-surfaces">
            <caption className="sr-only">Jev surfaces, flag state and recent usage</caption>
            <thead className="border-b border-admin-border">
              <tr>
                <th scope="col" className={th}>Surface</th>
                <th scope="col" className={th}>Flag</th>
                <th scope="col" className={th}>State</th>
                <th scope="col" className={th}>Feature code</th>
                <th scope="col" className={thNum}>Calls</th>
                <th scope="col" className={thNum}>Failures</th>
                <th scope="col" className={thNum}>Avg latency</th>
                <th scope="col" className={thNum}>Est. cost</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-admin-border">
              {usageRows.map((r) => (
                <tr key={r.featureCode}>
                  <td className={`${td} font-medium text-admin-fg-strong`}>{r.name}</td>
                  <td className={`${td} font-mono text-xs text-admin-fg-muted`}>{r.envVar ?? '-'}</td>
                  <td className={td}>{r.enabled === null ? <span className="text-admin-fg-muted">-</span> : <OnOff on={r.enabled} />}</td>
                  <td className={`${td} font-mono text-xs text-admin-fg-default`}>{r.featureCode}</td>
                  <td className={tdNum}>{fmtInt(r.calls)}</td>
                  <td className={`${tdNum} ${r.failures > 0 ? 'text-admin-danger' : ''}`}>{fmtInt(r.failures)}</td>
                  <td className={tdNum}>{fmtMs(r.avgLatencyMs)}</td>
                  <td className={tdNum}>{fmtUsd(r.costUsd)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot className="border-t border-admin-border">
              <tr className="font-medium text-admin-fg-strong">
                <td className={td} colSpan={4}>Total</td>
                <td className={tdNum}>{fmtInt(totals.calls)}</td>
                <td className={tdNum}>{fmtInt(totals.failures)}</td>
                <td className={tdNum} />
                <td className={tdNum}>{fmtUsd(totals.costUsd)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </SettingsSection>

      <div className="grid gap-6 lg:grid-cols-2">
        <SettingsSection title="Thresholds" description="Code-owned decisions (0 to 1). The model only supplies probabilities.">
          <SettingsTable caption="Jev thresholds" rows={status.thresholds} format={(n) => n.toFixed(2)} />
        </SettingsSection>
        <SettingsSection title="Limits and circuit breaker" description="Timeouts, retries and the platform-wide breaker.">
          <SettingsTable caption="Jev limits" rows={status.limits} format={fmtInt} />
        </SettingsSection>
      </div>

      <SettingsSection
        title="Changing a flag"
        description="Flags and thresholds are environment settings, read when the API starts. They cannot be toggled here."
      >
        <p className="text-sm text-admin-fg-default">
          Edit the value in <code className="font-mono text-xs">.env.production</code> on the VPS (through <code className="font-mono text-xs">oet-env-edit</code>),
          then run Build &amp; Deploy; the change takes effect on the next API start. For a hot per-surface stop with no deploy, add the
          surface&apos;s feature code to the disabled-features list under Budget &amp; Kill-switch on{' '}
          <Link href="/admin/ai-usage" className="font-medium text-[var(--admin-primary)] underline underline-offset-2">AI/API Usage &amp; Billing</Link>.
        </p>
        <h3 className="mt-4 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Recommended flip order</h3>
        <p className="mt-1 text-xs text-admin-fg-muted">One surface at a time, each after a green jev-calibrate run.</p>
        <ol className="mt-2 list-decimal space-y-1 ps-5 text-sm text-admin-fg-default">
          {FLIP_ORDER.map((step) => (
            <li key={step}>{step}</li>
          ))}
        </ol>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
