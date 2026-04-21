'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Radio } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchAdminConversationSessionDetail } from '@/lib/api';

interface SessionDetail {
  session: {
    id: string;
    userId: string;
    taskTypeCode: string;
    profession: string;
    state: string;
    turnCount: number;
    durationSeconds: number;
    templateId: string | null;
    lastErrorCode: string | null;
    createdAt: string;
    startedAt: string | null;
    completedAt: string | null;
    scenarioJson: string;
  };
  turns: Array<{
    turnNumber: number;
    role: string;
    content: string;
    audioUrl: string | null;
    durationMs: number;
    timestampMs: number;
    confidenceScore: number | null;
    aiFeatureCode: string | null;
    createdAt: string;
  }>;
  evaluation: {
    id: string;
    overallScaled: number;
    overallGrade: string;
    passed: boolean;
    criteriaJson: string;
    strengthsJson: string;
    improvementsJson: string;
    suggestedPracticeJson: string;
    appliedRuleIdsJson: string;
    rulebookVersion: string;
    advisory: string | null;
    createdAt: string;
  } | null;
  annotations: Array<{
    turnNumber: number;
    type: string;
    category: string | null;
    ruleId: string | null;
    evidence: string;
    suggestion: string | null;
  }>;
}

function safeParse<T>(json: string, fallback: T): T {
  try {
    return (JSON.parse(json) as T) ?? fallback;
  } catch {
    return fallback;
  }
}

export default function AdminSessionDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const id = params?.id as string | undefined;
  const [detail, setDetail] = useState<SessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    (async () => {
      try {
        const data = (await fetchAdminConversationSessionDetail(id)) as SessionDetail;
        setDetail(data);
      } catch {
        setError('Failed to load session.');
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  if (loading) {
    return (
      <AdminDashboardShell>
        <AdminRouteWorkspace>
          <AdminRoutePanel>
            <Skeleton className="h-48 rounded-2xl" />
          </AdminRoutePanel>
        </AdminRouteWorkspace>
      </AdminDashboardShell>
    );
  }

  if (error || !detail) {
    return (
      <AdminDashboardShell>
        <AdminRouteWorkspace>
          <AdminRoutePanel>
            <p className="text-sm text-muted">{error ?? 'Session not found.'}</p>
            <Link href="/admin/content/conversation/sessions" className="mt-3 inline-flex items-center gap-1 text-sm text-primary">
              <ArrowLeft className="h-4 w-4" /> Back to sessions
            </Link>
          </AdminRoutePanel>
        </AdminRouteWorkspace>
      </AdminDashboardShell>
    );
  }

  const s = detail.session;
  const scenario = safeParse<Record<string, unknown>>(s.scenarioJson, {});
  const criteria = detail.evaluation ? safeParse<Array<{ id: string; score06: number; maxScore: number; evidence: string; quotes?: string[] }>>(detail.evaluation.criteriaJson, []) : [];
  const strengths = detail.evaluation ? safeParse<string[]>(detail.evaluation.strengthsJson, []) : [];
  const improvements = detail.evaluation ? safeParse<string[]>(detail.evaluation.improvementsJson, []) : [];
  const suggestedPractice = detail.evaluation ? safeParse<string[]>(detail.evaluation.suggestedPracticeJson, []) : [];
  const appliedRules = detail.evaluation ? safeParse<string[]>(detail.evaluation.appliedRuleIdsJson, []) : [];

  return (
    <AdminDashboardShell>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Session"
            title={s.id}
            description={`User ${s.userId} · ${s.taskTypeCode} · ${s.profession}`}
            icon={Radio}
            actions={
              <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/sessions')}>
                <ArrowLeft className="mr-1 h-4 w-4" /> Back
              </Button>
            }
          />

          <section className="mb-6 rounded-2xl border border-border bg-surface p-4">
            <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Session</h3>
            <div className="grid grid-cols-2 gap-2 text-sm md:grid-cols-4">
              <div><span className="text-muted">State:</span> <Badge variant={s.state === 'evaluated' ? 'success' : s.state === 'failed' ? 'danger' : 'info'} size="sm">{s.state}</Badge></div>
              <div><span className="text-muted">Turns:</span> {s.turnCount}</div>
              <div><span className="text-muted">Duration:</span> {s.durationSeconds}s</div>
              <div><span className="text-muted">Template:</span> {s.templateId ?? '—'}</div>
              <div><span className="text-muted">Created:</span> {new Date(s.createdAt).toLocaleString()}</div>
              <div><span className="text-muted">Started:</span> {s.startedAt ? new Date(s.startedAt).toLocaleString() : '—'}</div>
              <div><span className="text-muted">Completed:</span> {s.completedAt ? new Date(s.completedAt).toLocaleString() : '—'}</div>
              <div><span className="text-muted">Last error:</span> {s.lastErrorCode ?? '—'}</div>
            </div>
          </section>

          {detail.evaluation && (
            <section className="mb-6 rounded-2xl border border-border bg-surface p-4">
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Evaluation</h3>
              <div className="mb-2 text-sm">
                <strong>Scaled:</strong> {detail.evaluation.overallScaled}/500 ·{' '}
                <strong>Grade:</strong> {detail.evaluation.overallGrade} ·{' '}
                <Badge variant={detail.evaluation.passed ? 'success' : 'warning'} size="sm">
                  {detail.evaluation.passed ? 'PASSED' : 'below pass'}
                </Badge>
                {' '}· Rulebook v{detail.evaluation.rulebookVersion}
              </div>
              {detail.evaluation.advisory && <p className="mb-2 text-xs text-muted">{detail.evaluation.advisory}</p>}
              <div className="mb-3 space-y-1">
                {criteria.map((c) => (
                  <div key={c.id} className="text-xs">
                    <span className="font-semibold text-navy">{c.id}:</span> {c.score06}/{c.maxScore} — {c.evidence}
                  </div>
                ))}
              </div>
              {strengths.length > 0 && (<div className="mb-2 text-xs"><strong>Strengths:</strong> {strengths.join(' | ')}</div>)}
              {improvements.length > 0 && (<div className="mb-2 text-xs"><strong>Improvements:</strong> {improvements.join(' | ')}</div>)}
              {suggestedPractice.length > 0 && (<div className="mb-2 text-xs"><strong>Practice:</strong> {suggestedPractice.join(' | ')}</div>)}
              {appliedRules.length > 0 && (<div className="text-[10px] text-muted">Rules applied: {appliedRules.join(', ')}</div>)}
            </section>
          )}

          <section className="mb-6 rounded-2xl border border-border bg-surface p-4">
            <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Scenario</h3>
            <pre className="overflow-x-auto rounded-lg bg-background-light p-3 text-[11px]">{JSON.stringify(scenario, null, 2)}</pre>
          </section>

          <section className="rounded-2xl border border-border bg-surface p-4">
            <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Transcript</h3>
            <div className="space-y-3">
              {detail.turns.map((t) => {
                const anns = detail.annotations.filter((a) => a.turnNumber === t.turnNumber);
                return (
                  <div key={t.turnNumber} className="rounded-xl border border-border/50 p-3">
                    <div className="mb-1 text-[10px] font-semibold uppercase text-muted">
                      Turn {t.turnNumber} · {t.role} · {t.durationMs}ms{t.confidenceScore != null ? ` · conf ${(t.confidenceScore * 100).toFixed(0)}%` : ''}
                      {t.aiFeatureCode && ` · ${t.aiFeatureCode}`}
                    </div>
                    <p className="text-sm text-navy">{t.content}</p>
                    {anns.map((a, i) => (
                      <div key={i} className="mt-2 flex items-start gap-2 text-xs">
                        <Badge variant={a.type === 'strength' ? 'success' : a.type === 'error' ? 'danger' : 'warning'} size="sm">{a.type}</Badge>
                        <div className="flex-1">
                          {a.evidence}
                          {a.ruleId && (<span className="ml-2 font-mono text-[10px] text-primary">{a.ruleId}</span>)}
                          {a.suggestion && (<div className="mt-0.5 text-[11px] text-primary">💡 {a.suggestion}</div>)}
                        </div>
                      </div>
                    ))}
                  </div>
                );
              })}
            </div>
          </section>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </AdminDashboardShell>
  );
}
