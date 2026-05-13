'use client';

import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { AlertTriangle, BarChart3, RefreshCw, ShieldAlert } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminGetWritingRuleViolationDashboard,
  type AdminWritingRuleViolationDashboard,
} from '@/lib/api';

const WINDOW_OPTIONS = [
  { label: 'Last 7 days', value: 7 },
  { label: 'Last 30 days', value: 30 },
  { label: 'Last 90 days', value: 90 },
  { label: 'Last 365 days', value: 365 },
];

const PROFESSION_OPTIONS = [
  { label: 'All professions', value: '' },
  { label: 'Doctor', value: 'doctor' },
  { label: 'Nurse', value: 'nurse' },
  { label: 'Pharmacist', value: 'pharmacist' },
  { label: 'Dentist', value: 'dentist' },
  { label: 'Physiotherapist', value: 'physiotherapist' },
  { label: 'Veterinarian', value: 'veterinarian' },
];

function severityVariant(label: 'critical' | 'major' | 'minor' | 'info'): 'danger' | 'warning' | 'info' | 'muted' {
  switch (label) {
    case 'critical':
      return 'danger';
    case 'major':
      return 'warning';
    case 'minor':
      return 'info';
    default:
      return 'muted';
  }
}

export default function WritingRuleViolationDashboardPage() {
  const [data, setData] = useState<AdminWritingRuleViolationDashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [days, setDays] = useState(30);
  const [profession, setProfession] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await adminGetWritingRuleViolationDashboard({
        days,
        profession: profession || undefined,
      });
      setData(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard.');
    } finally {
      setLoading(false);
    }
  }, [days, profession]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <ShieldAlert className="h-6 w-6 text-primary" />
            <h1 className="text-2xl font-semibold">Writing rule violations</h1>
          </div>
          <p className="mt-1 text-sm text-muted">
            Aggregate view of OET Writing rulebook + AI-grader violations persisted from learner attempts.
            Use this to spot rules that disproportionately fail learners and to triage the rulebook.
          </p>
        </div>
        <Button
          variant="primary"
          onClick={() => void load()}
          disabled={loading}
          aria-label="Refresh dashboard"
        >
          <RefreshCw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </header>

      <Card className="p-4">
        <div className="flex flex-wrap items-end gap-4">
          <div>
            <label htmlFor="window-select" className="block text-xs font-medium text-muted">
              Window
            </label>
            <select
              id="window-select"
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              className="mt-1 rounded-md border border-border bg-surface px-3 py-2 text-sm"
            >
              {WINDOW_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="profession-select" className="block text-xs font-medium text-muted">
              Profession
            </label>
            <select
              id="profession-select"
              value={profession}
              onChange={(e) => setProfession(e.target.value)}
              className="mt-1 rounded-md border border-border bg-surface px-3 py-2 text-sm"
            >
              {PROFESSION_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          {data && (
            <div className="ml-auto text-xs text-muted">
              Generated {new Date(data.generatedAt).toLocaleString()} · window {data.windowDays}d
              {data.professionFilter ? ` · profession=${data.professionFilter}` : ''}
            </div>
          )}
        </div>
      </Card>

      {error && (
        <Card className="border-danger/40 bg-danger/5 p-4">
          <div className="flex items-start gap-2">
            <AlertTriangle className="mt-0.5 h-4 w-4 text-danger" />
            <div>
              <p className="text-sm font-medium text-danger">Failed to load</p>
              <p className="mt-1 text-xs text-muted">{error}</p>
            </div>
          </div>
        </Card>
      )}

      {loading && !data ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full" />
          ))}
        </div>
      ) : data ? (
        <>
          <section aria-label="Summary" className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
            <SummaryTile label="Total violations" value={data.summary.totalViolations} />
            <SummaryTile label="Distinct rules" value={data.summary.distinctRules} />
            <SummaryTile label="Distinct attempts" value={data.summary.distinctAttempts} />
            <SummaryTile label="Distinct learners" value={data.summary.distinctLearners} />
            <SummaryTile label="From rulebook" value={data.summary.ruleEngineCount} />
            <SummaryTile label="From AI grader" value={data.summary.aiCount} />
          </section>

          <motion.section
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.2 }}
            aria-label="Top rules"
          >
            <Card className="p-4">
              <div className="mb-3 flex items-center gap-2">
                <BarChart3 className="h-5 w-5 text-primary" />
                <h2 className="text-lg font-semibold">Top {data.topRules.length} rules</h2>
              </div>
              {data.topRules.length === 0 ? (
                <p className="text-sm text-muted">No violations recorded in this window.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-border text-left text-xs text-muted">
                        <th className="py-2 pr-3">Rule ID</th>
                        <th className="py-2 pr-3 text-right">Total</th>
                        <th className="py-2 pr-3 text-right">Attempts</th>
                        <th className="py-2 pr-3 text-right">Learners</th>
                        <th className="py-2 pr-3 text-right">Critical</th>
                        <th className="py-2 pr-3 text-right">Major</th>
                        <th className="py-2 pr-3 text-right">Minor</th>
                        <th className="py-2 pr-3 text-right">Info</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.topRules.map((row) => (
                        <tr key={row.ruleId} className="border-b border-border/50">
                          <td className="py-2 pr-3 font-mono text-xs">{row.ruleId}</td>
                          <td className="py-2 pr-3 text-right font-medium">{row.totalCount}</td>
                          <td className="py-2 pr-3 text-right">{row.distinctAttempts}</td>
                          <td className="py-2 pr-3 text-right">{row.distinctLearners}</td>
                          <td className="py-2 pr-3 text-right">
                            <SeverityCount label="critical" count={row.criticalCount} />
                          </td>
                          <td className="py-2 pr-3 text-right">
                            <SeverityCount label="major" count={row.majorCount} />
                          </td>
                          <td className="py-2 pr-3 text-right">
                            <SeverityCount label="minor" count={row.minorCount} />
                          </td>
                          <td className="py-2 pr-3 text-right">
                            <SeverityCount label="info" count={row.infoCount} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </motion.section>

          <section className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <BreakdownCard
              title="By profession"
              rows={data.professionBreakdown.map((r) => ({ label: r.profession, count: r.count }))}
            />
            <BreakdownCard
              title="By letter type"
              rows={data.letterTypeBreakdown.map((r) => ({ label: r.letterType, count: r.count }))}
            />
          </section>
        </>
      ) : null}
    </div>
  );
}

function SummaryTile({ label, value }: { label: string; value: number }) {
  return (
    <Card className="p-4">
      <div className="text-xs text-muted">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value.toLocaleString()}</div>
    </Card>
  );
}

function SeverityCount({ label, count }: { label: 'critical' | 'major' | 'minor' | 'info'; count: number }) {
  if (count === 0) return <span className="text-muted">0</span>;
  return <Badge variant={severityVariant(label)}>{count}</Badge>;
}

function BreakdownCard({
  title,
  rows,
}: {
  title: string;
  rows: { label: string; count: number }[];
}) {
  const total = rows.reduce((acc, r) => acc + r.count, 0);
  return (
    <Card className="p-4">
      <h3 className="mb-3 text-sm font-semibold">{title}</h3>
      {rows.length === 0 ? (
        <p className="text-sm text-muted">No data in this window.</p>
      ) : (
        <ul className="space-y-2">
          {rows.map((r) => {
            const pct = total > 0 ? Math.round((r.count / total) * 100) : 0;
            return (
              <li key={r.label} className="text-sm">
                <div className="flex items-center justify-between">
                  <span className="font-medium">{r.label}</span>
                  <span className="text-muted">
                    {r.count.toLocaleString()} · {pct}%
                  </span>
                </div>
                <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-border/40">
                  <div
                    className="h-full bg-primary"
                    style={{ width: `${pct}%` }}
                    aria-hidden="true"
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
