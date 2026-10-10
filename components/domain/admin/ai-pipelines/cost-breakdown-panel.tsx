'use client';

/**
 * Writing and Speaking cost by processing stage (owner directive 2026-10-10).
 *
 * Labelling rules, kept deliberately strict so a figure can never be mistaken for something it is not:
 *  - a subscription route shows "Subscription — $0 incremental API cost" (never a bare $0 or a blank);
 *  - every USD figure is an internally tracked estimate, and each row names its basis;
 *  - live voice is estimated from connected minutes x a rate; an unset rate is shown as an assumption, never as $0;
 *  - promotional credits are shown as gross consumption, credits consumed, estimated remaining and estimated
 *    out-of-pocket — they are four different numbers.
 */

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Tabs } from '@/components/ui/tabs';
import { toast } from '@/components/ui/toaster';
import {
  fetchCostBreakdown,
  fetchCostReconciliation,
  saveLiveVoiceRate,
  type CostBreakdownResponse,
  type CostComponent,
  type CostReconciliation,
  type CostRow,
  type CostRun,
  type CostRunPart,
  type LiveVoiceRateView,
  type OverviewWindow,
} from '@/lib/api/ai-pipelines';
import { InfoTip } from './info-tip';

const WINDOWS: Array<{ id: OverviewWindow; label: string }> = [
  { id: 'today', label: 'Today' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
  { id: 'all', label: 'All time' },
];

export const SUBSCRIPTION_LABEL = 'Subscription — $0 incremental API cost';

function usd(n: number | null | undefined): string {
  if (n === null || n === undefined) return '—';
  const abs = Math.abs(n);
  const digits = abs >= 100 ? 0 : abs >= 1 ? 2 : 4;
  return `$${n.toFixed(digits)}`;
}

function num(n: number): string {
  return n.toLocaleString();
}

function tokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`;
  return String(n);
}

function errorText(err: unknown): string {
  const e = err as { userMessage?: string; message?: string };
  return e?.userMessage || e?.message || 'Something went wrong.';
}

const BASIS_LABEL: Record<CostRow['basis'], string> = {
  subscription: 'Subscription',
  rate_card: 'Estimated: reported tokens × list price',
  stored_estimate: 'Estimated: stored per-call estimate',
  duration_estimate: 'Estimated: connected minutes × your rate',
  duration_assumed: 'Assumed: connected minutes × starting rate (not yet set by you)',
  reported_tokens: 'Estimated: provider-reported tokens × your token rates',
  mixed: 'Estimated: reported tokens where available, otherwise connected minutes × your rate',
  unpriced: 'Not priced: the provider of these sessions is no longer recorded',
};

function KindBadge({ row }: { row: CostRow }) {
  if (row.kind === 'subscription') return <Badge variant="info">{SUBSCRIPTION_LABEL}</Badge>;
  if (row.kind === 'live_voice') {
    if (row.basis === 'unpriced') return <Badge variant="warning">Not priced</Badge>;
    if (row.basis === 'duration_assumed') return <Badge variant="warning">Assumed estimate</Badge>;
    return <Badge variant="muted">{row.basis === 'reported_tokens' ? 'From reported tokens' : 'Estimated'}</Badge>;
  }
  return <Badge variant="muted">Paid API</Badge>;
}

function ComponentTable({ component, emptyText }: { component: CostComponent; emptyText: string }) {
  const live = component.key === 'speaking-live-voice';
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="font-semibold text-admin-fg-strong">{component.label}</p>
        <p className="text-sm tabular-nums text-admin-fg-default">
          {usd(component.apiUsd)}
          <span className="ml-2 text-2xs text-admin-fg-muted">
            {num(component.requests)} {live ? 'sessions' : 'requests'} · {num(component.failedAttempts)} failed · {num(component.retries)} retries
          </span>
        </p>
      </div>
      {component.subscriptionRequests > 0 && (
        <p className="text-2xs text-admin-fg-muted">
          {num(component.subscriptionRequests)} of these requests were served by a subscription: {SUBSCRIPTION_LABEL}.
          {component.subscriptionApiEquivalentUsd > 0 && ` For comparison only, the same tokens at paid-API list prices would be ${usd(component.subscriptionApiEquivalentUsd)} (not charged).`}
        </p>
      )}
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-2xs uppercase tracking-wider text-admin-fg-muted">
              <th className="px-3 py-2 text-start">Provider and model</th>
              <th className="px-3 py-2 text-start">Route</th>
              <th className="px-3 py-2 text-start">{live ? 'Sessions' : 'Requests'}</th>
              <th className="px-3 py-2 text-start">{live ? 'Minutes' : 'Failed / retries'}</th>
              <th className="px-3 py-2 text-start">{live ? 'Method' : 'Tokens in / out / cache'}</th>
              <th className="px-3 py-2 text-start">Cost (est.)</th>
            </tr>
          </thead>
          <tbody>
            {component.rows.length === 0 && (
              <tr><td colSpan={6} className="px-3 py-3 text-admin-fg-muted">{emptyText}</td></tr>
            )}
            {component.rows.map((row) => (
              <tr key={`${row.providerId}|${row.model}`} className="border-t border-admin-border align-top">
                <td className="px-3 py-2">
                  <span className="font-medium text-admin-fg-strong">{row.providerName}</span>
                  <span className="block text-2xs text-admin-fg-muted">{row.model}</span>
                </td>
                <td className="px-3 py-2"><KindBadge row={row} /></td>
                <td className="px-3 py-2 tabular-nums">{num(row.requests)}</td>
                <td className="px-3 py-2 tabular-nums">
                  {live
                    ? (row.minutes ?? 0).toFixed(1)
                    : `${num(row.failedAttempts)} / ${num(row.retries)}`}
                </td>
                <td className="px-3 py-2 text-2xs text-admin-fg-muted">
                  {live ? (
                    <>
                      {BASIS_LABEL[row.basis]}
                      {row.reportedSessions > 0 && (
                        <span className="block">
                          {tokens(row.promptTokens)} in / {tokens(row.completionTokens)} out reported by the provider in{' '}
                          {num(row.reportedSessions)} of {num(row.requests)} sessions
                        </span>
                      )}
                    </>
                  ) : (
                    `${tokens(row.promptTokens)} / ${tokens(row.completionTokens)} / ${tokens(row.cacheTokens)}`
                  )}
                </td>
                <td className="px-3 py-2 tabular-nums">
                  {row.kind === 'subscription' ? (
                    <>
                      $0
                      {row.apiEquivalentUsd !== null && (
                        <span className="block text-2xs text-admin-fg-muted">API-equivalent {usd(row.apiEquivalentUsd)}, not charged</span>
                      )}
                    </>
                  ) : row.basis === 'unpriced' ? (
                    <span className="text-admin-fg-muted">Not priced</span>
                  ) : (
                    <>
                      {usd(row.costUsd)}
                      <span className="block text-2xs text-admin-fg-muted">{row.basis === 'rate_card' ? 'list price' : row.basis === 'stored_estimate' ? 'stored estimate' : ''}</span>
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function partSummary(part: CostRunPart): string {
  if (part.legs.length === 0) return '—';
  return part.legs
    .map((l) => {
      const route = l.kind === 'subscription' ? 'Subscription $0' : usd(l.costUsd);
      const failed = l.failed > 0 ? `, ${l.failed} failed` : '';
      return `${l.providerName} · ${l.model} ×${l.calls}${failed} — ${route}`;
    })
    .join('\n');
}

const RUN_KIND: Record<CostRun['kind'], string> = {
  writing: 'Writing letter',
  speaking_card: 'Speaking (one card)',
  speaking_mock: 'Speaking full mock (two cards)',
};

function RunsTable({ runs }: { runs: CostRun[] }) {
  if (runs.length === 0) {
    return <p className="text-sm text-admin-fg-muted">No graded runs in this window.</p>;
  }
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-2xs uppercase tracking-wider text-admin-fg-muted">
            <th className="px-3 py-2 text-start">When</th>
            <th className="px-3 py-2 text-start">Type</th>
            <th className="px-3 py-2 text-start">Grading</th>
            <th className="px-3 py-2 text-start">Reviewer</th>
            <th className="px-3 py-2 text-start">Live voice</th>
            <th className="px-3 py-2 text-start">Total</th>
          </tr>
        </thead>
        <tbody>
          {runs.map((run, i) => (
            <tr key={`${run.at}|${run.learner}|${i}`} className="border-t border-admin-border align-top">
              <td className="px-3 py-2 whitespace-nowrap">
                {new Date(run.at).toLocaleString()}
                <span className="block text-2xs text-admin-fg-muted">learner {run.learner}…</span>
              </td>
              <td className="px-3 py-2">
                {RUN_KIND[run.kind]}
                {!run.succeeded && <Badge variant="warning" className="ml-2">No grade</Badge>}
              </td>
              <td className="px-3 py-2 whitespace-pre-line text-2xs">
                <span className="font-medium text-admin-fg-strong tabular-nums">{usd(run.grading.costUsd)}</span>
                <br />
                {partSummary(run.grading)}
                {run.grading.retries > 0 && <><br />{run.grading.retries} retr{run.grading.retries === 1 ? 'y' : 'ies'}</>}
              </td>
              <td className="px-3 py-2 whitespace-pre-line text-2xs">
                <span className="font-medium text-admin-fg-strong tabular-nums">{usd(run.reviewer.costUsd)}</span>
                <br />
                {partSummary(run.reviewer)}
              </td>
              <td className="px-3 py-2 whitespace-pre-line text-2xs">
                {run.kind === 'writing' ? (
                  <span className="text-admin-fg-muted">not used</span>
                ) : (
                  <>
                    <span className="font-medium text-admin-fg-strong tabular-nums">{usd(run.liveVoice.costUsd)}</span>
                    <br />
                    {partSummary(run.liveVoice)}
                  </>
                )}
              </td>
              <td className="px-3 py-2 font-semibold tabular-nums">{usd(run.totalUsd)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RateEditor({ rate, onSaved }: { rate: LiveVoiceRateView; onSaved: () => void }) {
  const [value, setValue] = useState(String(rate.perMinuteUsd));
  const [inRate, setInRate] = useState(rate.inputPerMillionUsd === null ? '' : String(rate.inputPerMillionUsd));
  const [outRate, setOutRate] = useState(rate.outputPerMillionUsd === null ? '' : String(rate.outputPerMillionUsd));
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setValue(String(rate.perMinuteUsd));
    setInRate(rate.inputPerMillionUsd === null ? '' : String(rate.inputPerMillionUsd));
    setOutRate(rate.outputPerMillionUsd === null ? '' : String(rate.outputPerMillionUsd));
  }, [rate.perMinuteUsd, rate.inputPerMillionUsd, rate.outputPerMillionUsd]);

  async function save() {
    const n = Number(value);
    if (!Number.isFinite(n) || n < 0 || n > 100) {
      toast.error('Enter a rate between 0 and 100 USD per minute.');
      return;
    }
    const hasIn = inRate.trim() !== '';
    const hasOut = outRate.trim() !== '';
    if (hasIn !== hasOut) {
      toast.error('Enter both token rates (input and output), or leave both blank.');
      return;
    }
    const inN = hasIn ? Number(inRate) : null;
    const outN = hasOut ? Number(outRate) : null;
    if ((inN !== null && (!Number.isFinite(inN) || inN < 0)) || (outN !== null && (!Number.isFinite(outN) || outN < 0))) {
      toast.error('Token rates must be zero or more USD per million tokens.');
      return;
    }
    setBusy(true);
    try {
      await saveLiveVoiceRate(rate.provider, n, { inputPerMillionUsd: inN, outputPerMillionUsd: outN });
      toast.success(`${rate.name} rates saved. Live voice costs are recomputed with them.`);
      onSaved();
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-3 rounded-admin-lg border border-admin-border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-[10rem]">
          <p className="font-medium text-admin-fg-strong">{rate.name}</p>
          <p className="text-2xs text-admin-fg-muted">{rate.model}</p>
        </div>
        {rate.ownerSet ? (
          <Badge variant="success">Set by you{rate.updatedBy ? ` (${rate.updatedBy})` : ''}</Badge>
        ) : (
          <Badge variant="warning">Assumed starting estimate — replace with your own figure</Badge>
        )}
      </div>
      <div className="flex flex-wrap items-end gap-3">
        <Input
          label="USD per connected minute"
          type="number"
          min={0}
          max={100}
          step="0.01"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          className="w-44"
        />
        <Input
          label="USD per 1M input tokens (optional)"
          type="number"
          min={0}
          step="0.01"
          value={inRate}
          onChange={(e) => setInRate(e.target.value)}
          className="w-52"
        />
        <Input
          label="USD per 1M output tokens (optional)"
          type="number"
          min={0}
          step="0.01"
          value={outRate}
          onChange={(e) => setOutRate(e.target.value)}
          className="w-52"
        />
        <Button size="sm" disabled={busy} onClick={() => void save()}>
          {busy ? 'Saving…' : 'Save rates'}
        </Button>
      </div>
      <p className="text-2xs text-admin-fg-muted">
        Token rates are blended (audio and text together). When both are set, sessions whose provider-reported usage
        was received are priced from tokens; every other session is priced from connected minutes.
      </p>
    </div>
  );
}

function ReconciliationCard({ period }: { period: OverviewWindow }) {
  const [result, setResult] = useState<CostReconciliation | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setResult(null);
  }, [period]);

  async function run() {
    setBusy(true);
    try {
      setResult(await fetchCostReconciliation(period));
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardContent className="space-y-3 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <p className="text-base font-semibold text-admin-fg-strong">Reconciliation check</p>
            <InfoTip label="the reconciliation check">
              Recomputes the totals through independent paths on your production data and compares them: the stage
              components against the full usage ledger, call counts, rate-card coverage, letters against completed
              Writing evaluations, and live voice attribution. Read-only; nothing is written.
            </InfoTip>
          </div>
          <Button variant="outline" size="sm" disabled={busy} onClick={() => void run()}>
            {busy ? 'Checking…' : 'Run reconciliation'}
          </Button>
        </div>
        {result ? (
          <ul className="space-y-2 text-sm">
            {result.checks.map((c) => (
              <li key={c.name} className="flex flex-wrap items-start gap-2">
                {c.ok ? <Badge variant="success">Pass</Badge> : <Badge variant="warning">Review</Badge>}
                <span className="min-w-0 flex-1 text-admin-fg-default">
                  <span className="font-medium">{c.name.replace(/_/g, ' ')}</span>: {c.detail}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-admin-fg-muted">Not run for this window yet.</p>
        )}
      </CardContent>
    </Card>
  );
}

export function CostBreakdownPanel() {
  const [period, setPeriod] = useState<OverviewWindow>('7d');
  const [data, setData] = useState<CostBreakdownResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (w: OverviewWindow) => {
    try {
      setData(await fetchCostBreakdown(w));
      setError(null);
    } catch (err) {
      setError(errorText(err));
    }
  }, []);

  useEffect(() => {
    void load(period);
  }, [load, period]);

  const b = data?.selected ?? null;
  const anyAssumed = !!data?.liveVoiceRates.some((r) => !r.ownerSet);
  const primaryGrant = b?.money.grants[0] ?? null;

  return (
    <SettingsSection
      id="cost-breakdown"
      title="Cost by stage — Writing and Speaking"
      description="Each Writing letter and Speaking assessment split into the stages that spend money: Writing grading + reviewer, Speaking live voice + grading + reviewer. All figures are internally tracked estimates, not provider invoices."
      actions={
        <Tabs
          tabs={WINDOWS}
          activeTab={period}
          onChange={(id) => setPeriod(id as OverviewWindow)}
          ariaLabel="Cost window"
        />
      }
    >
      {error ? (
        <Card surface="tinted-danger">
          <CardContent className="flex items-start gap-2 p-4 text-sm text-admin-danger" role="alert">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{error}</span>
          </CardContent>
        </Card>
      ) : !data || !b ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <div className="space-y-6">
          {/* Legend: what each label means. */}
          <div className="flex flex-wrap items-center gap-2 text-2xs text-admin-fg-muted">
            <Badge variant="info">{SUBSCRIPTION_LABEL}</Badge>
            <span>= served by a Claude Max or ChatGPT/Codex subscription. It is not an API call that never happened and not a missing measurement.</span>
            <Badge variant="muted">Paid API</Badge>
            <span>= billed per token (estimated here at list price).</span>
            <Badge variant="warning">Assumed estimate</Badge>
            <span>= live voice priced with a starting rate you have not set yet.</span>
          </div>

          {/* The four windows side by side. */}
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="mb-2 text-start text-2xs uppercase tracking-wider text-admin-fg-muted">
                Headline figures — Today, 7 days, 30 days, all time
              </caption>
              <thead>
                <tr className="text-2xs uppercase tracking-wider text-admin-fg-muted">
                  <th className="px-3 py-2 text-start">&nbsp;</th>
                  {data.summary.map((s) => (
                    <th key={s.window} className="px-3 py-2 text-end">{s.label}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                <SummaryHeader label="WRITING" span={data.summary.length + 1} />
                <SummaryRow label="Grading cost" values={data.summary.map((s) => usd(s.writingGradingUsd))} />
                <SummaryRow label="Reviewer cost" values={data.summary.map((s) => usd(s.writingReviewerUsd))} />
                <SummaryRow label="Total Writing cost" strong values={data.summary.map((s) => usd(s.writingTotalUsd))} />
                <SummaryRow label="Average cost per letter" values={data.summary.map((s) => `${usd(s.writingAvgPerLetterUsd)} (${num(s.letters)} letters)`)} />
                <SummaryHeader label="SPEAKING" span={data.summary.length + 1} />
                <SummaryRow label="Live voice cost" values={data.summary.map((s) => usd(s.speakingLiveVoiceUsd))} />
                <SummaryRow label="Grading cost" values={data.summary.map((s) => usd(s.speakingGradingUsd))} />
                <SummaryRow label="Reviewer cost" values={data.summary.map((s) => usd(s.speakingReviewerUsd))} />
                <SummaryRow label="Audio model (acoustic half of grading)" values={data.summary.map((s) => usd(s.speakingAudioUsd))} />
                <SummaryRow label="Total Speaking cost" strong values={data.summary.map((s) => usd(s.speakingTotalUsd))} />
                <SummaryRow label="Average per Speaking assessment" values={data.summary.map((s) => `${usd(s.speakingAvgPerAssessmentUsd)} (${num(s.singleCards + s.fullMocks)} graded)`)} />
                <SummaryRow label="Average per full two-card mock" values={data.summary.map((s) => `${usd(s.speakingAvgPerFullMockUsd)} (${num(s.fullMocks)} mocks)`)} />
                <SummaryHeader label="MONEY" span={data.summary.length + 1} />
                <SummaryRow label="Gross API consumption" values={data.summary.map((s) => usd(s.grossApiUsd))} />
                <SummaryRow label="Promotional credits consumed" values={data.summary.map((s) => usd(s.promoConsumedUsd))} />
                <SummaryRow label="Estimated out-of-pocket API expense" strong values={data.summary.map((s) => usd(s.outOfPocketUsd))} />
              </tbody>
            </table>
          </div>

          {anyAssumed && (
            <p className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm" role="status">
              <strong className="font-semibold">Live voice is priced with an assumed starting rate.</strong>{' '}
              The realtime audio does not pass through this server, so OpenAI/Gemini do not report a per-session cost
              here. Divide the provider&apos;s invoice for live voice by the metered minutes below and enter that rate in
              &ldquo;Live voice rate&rdquo; to replace the assumption.
            </p>
          )}

          {/* Writing */}
          <Card>
            <CardContent className="space-y-4 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-base font-semibold text-admin-fg-strong">Writing</p>
                <InfoTip label="Writing cost">
                  Total Writing cost = Grading cost + Reviewer cost. It includes every attempt: retries, failed calls
                  and paid fallbacks. Subscription calls add $0 but are still counted as requests.
                </InfoTip>
              </div>
              <div className="grid gap-3 sm:grid-cols-4">
                <KpiTile label="Grading cost" value={usd(b.writing.grading.apiUsd)} hint={`${num(b.writing.grading.requests)} requests`} />
                <KpiTile label="Reviewer cost" value={usd(b.writing.reviewer.apiUsd)} hint={`${num(b.writing.reviewer.requests)} requests`} />
                <KpiTile label="Total Writing cost" value={usd(b.writing.totalUsd)} hint={`${num(b.writing.letters)} letters graded`} />
                <KpiTile label="Average per letter" value={usd(b.writing.avgPerLetterUsd)} hint="grading + reviewer ÷ letters" />
              </div>
              <ComponentTable component={b.writing.grading} emptyText="No Writing grading calls in this window." />
              <ComponentTable component={b.writing.reviewer} emptyText="No Writing reviewer calls in this window." />
            </CardContent>
          </Card>

          {/* Speaking */}
          <Card>
            <CardContent className="space-y-4 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-base font-semibold text-admin-fg-strong">Speaking</p>
                <InfoTip label="Speaking cost">
                  Total Speaking cost = Live voice + Grading + Reviewer (+ the audio model that judges
                  intelligibility). A full two-card mock = combined grading + one review + two live sessions + two
                  audio judgements; each shared item is counted once.
                </InfoTip>
              </div>
              <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
                <KpiTile label="Live voice" value={usd(b.speaking.liveVoice.apiUsd)} hint={`${num(b.speaking.liveVoiceSessions)} sessions · ${b.speaking.liveVoiceMinutes.toFixed(0)} min`} />
                <KpiTile label="Grading" value={usd(b.speaking.grading.apiUsd)} hint={`${num(b.speaking.grading.requests)} requests`} />
                <KpiTile label="Reviewer" value={usd(b.speaking.reviewer.apiUsd)} hint={`${num(b.speaking.reviewer.requests)} requests`} />
                <KpiTile label="Total Speaking" value={usd(b.speaking.totalUsd)} hint={`${num(b.speaking.singleCards)} cards · ${num(b.speaking.fullMocks)} mocks`} />
                <KpiTile label="Avg per assessment" value={usd(b.speaking.avgPerAssessmentUsd)} hint="all components ÷ graded" />
                <KpiTile label="Avg per full mock" value={usd(b.speaking.avgPerFullMockUsd)} hint={`single card ${usd(b.speaking.avgPerSingleCardUsd)}`} />
              </div>
              <ComponentTable component={b.speaking.liveVoice} emptyText="No live voice sessions in this window." />
              {b.speaking.liveVoiceUnpricedMinutes > 0 && (
                <p className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm" role="status">
                  <strong className="font-semibold">Total Speaking cost is partial.</strong>{' '}
                  {b.speaking.liveVoiceUnpricedMinutes.toFixed(1)} of {b.speaking.liveVoiceMinutes.toFixed(1)} live voice minutes
                  belong to sessions older than the retention window whose provider was erased before it was kept, so they
                  are not priced. Newer sessions keep their provider and are priced.
                </p>
              )}
              <ComponentTable component={b.speaking.grading} emptyText="No Speaking grading calls in this window." />
              <ComponentTable component={b.speaking.reviewer} emptyText="No Speaking reviewer calls in this window." />
              <ComponentTable component={b.speaking.audioModel} emptyText="The audio model is off or made no calls in this window." />
            </CardContent>
          </Card>

          {/* Money */}
          <Card>
            <CardContent className="space-y-4 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-base font-semibold text-admin-fg-strong">Promotional credits and real expense</p>
                <InfoTip label="credit accounting">
                  Four different numbers: gross API consumption (what the API would bill), promotional credits
                  consumed (covered by the grant), estimated credits remaining, and estimated out-of-pocket (gross
                  minus credits consumed). Spending $20 of a $200 grant is $20 of API usage but $0 out of pocket.
                </InfoTip>
              </div>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
                <KpiTile label="Gross API consumption" value={usd(b.money.grossApiUsd)} hint={`pipeline ${usd(b.money.pipelineApiUsd)} · other ${usd(b.money.otherFeaturesApiUsd)}`} />
                <KpiTile label="Credits consumed" value={usd(b.money.promoConsumedUsd)} hint="in this window" />
                <KpiTile label="Estimated credits remaining" value={usd(b.money.promoRemainingUsd)} hint="all grants, now" />
                <KpiTile label="Estimated out-of-pocket" value={usd(b.money.outOfPocketUsd)} hint="gross − credits consumed" />
                <KpiTile label="Subscription API-equivalent" value={usd(b.money.subscriptionApiEquivalentUsd)} hint="not charged" />
              </div>
              <p className="text-sm text-admin-fg-default">
                {primaryGrant
                  ? `Example from your data: the ${primaryGrant.providerCode} grant of ${usd(primaryGrant.grantUsd)} has had ${usd(primaryGrant.consumedTotalUsd)} of API usage so far — that is ${usd(primaryGrant.consumedTotalUsd)} consumed from credits, ${usd(primaryGrant.remainingUsd)} estimated remaining and ${usd(primaryGrant.overflowUsd)} paid out of pocket on this provider.`
                  : 'Example: with a $200 promotional grant, consuming $20 of grading through the Claude API is $20 of gross API consumption, $20 of credits consumed, $180 remaining and $0 out of pocket.'}
              </p>
              <p className="text-2xs text-admin-fg-muted">
                Credits are tracked against the provider of the grant (for example <code>anthropic</code>). The
                remaining balance is computed from internally tracked usage; Anthropic does not publish a balance API,
                so check Anthropic&apos;s console for the authoritative number. Prices are Anthropic list prices verified
                on {data.rateCardVerifiedOn}.
              </p>
            </CardContent>
          </Card>

          {/* Live voice rate */}
          <Card>
            <CardContent className="space-y-3 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-base font-semibold text-admin-fg-strong">Live voice rate</p>
                <InfoTip label="live voice rate">
                  OpenAI and Gemini live voice audio goes straight from the candidate&apos;s browser to the provider,
                  so the server meters connected minutes (session start to last saved turn) and multiplies by the rate
                  you set here. Calibrate it once: provider invoice for live voice ÷ metered minutes.
                </InfoTip>
              </div>
              {data.liveVoiceRates.map((rate) => (
                <RateEditor key={rate.provider} rate={rate} onSaved={() => void load(period)} />
              ))}
            </CardContent>
          </Card>

          <ReconciliationCard period={period} />

          {/* Runs */}
          <Card>
            <CardContent className="space-y-3 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-base font-semibold text-admin-fg-strong">Recent graded runs, cost per submission</p>
                <InfoTip label="recent runs">
                  Usage rows record the learner and the time, not a submission id, so a run is every grading, review and
                  live voice row of one learner within 45 minutes. Each number is exact per row; only the grouping of
                  rows into one submission is inferred.
                </InfoTip>
              </div>
              <RunsTable runs={data.runs} />
            </CardContent>
          </Card>
        </div>
      )}
    </SettingsSection>
  );
}

function SummaryHeader({ label, span }: { label: string; span: number }) {
  return (
    <tr className="border-t border-admin-border bg-admin-bg-subtle">
      <th colSpan={span} className="px-3 py-1.5 text-start text-2xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</th>
    </tr>
  );
}

function SummaryRow({ label, values, strong }: { label: string; values: string[]; strong?: boolean }) {
  return (
    <tr className="border-t border-admin-border">
      <th scope="row" className={`px-3 py-2 text-start ${strong ? 'font-semibold text-admin-fg-strong' : 'font-normal text-admin-fg-default'}`}>{label}</th>
      {values.map((v, i) => (
        <td key={i} className={`px-3 py-2 text-end tabular-nums ${strong ? 'font-semibold text-admin-fg-strong' : ''}`}>{v}</td>
      ))}
    </tr>
  );
}
