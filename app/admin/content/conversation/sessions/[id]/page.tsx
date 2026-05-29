'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Radio } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Conversation', href: '/admin/content/conversation' },
    { label: 'Sessions', href: '/admin/content/conversation/sessions' },
    { label: id ?? 'Session' },
  ];

  if (loading) {
    return (
      <AdminSettingsLayout
        title="Loading session…"
        breadcrumbs={breadcrumbs}
      >
        <Skeleton className="h-48 rounded-admin-lg" />
      </AdminSettingsLayout>
    );
  }

  if (error || !detail) {
    return (
      <AdminSettingsLayout
        title="Session not found"
        breadcrumbs={breadcrumbs}
      >
        <Card>
          <CardContent className="p-6 pt-6">
            <p className="text-sm text-admin-fg-muted">{error ?? 'Session not found.'}</p>
            <Link href="/admin/content/conversation/sessions" className="mt-3 inline-flex items-center gap-1 text-sm text-[var(--admin-primary)]">
              <ArrowLeft className="h-4 w-4" /> Back to sessions
            </Link>
          </CardContent>
        </Card>
      </AdminSettingsLayout>
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
    <AdminSettingsLayout
      eyebrow="Session"
      title={s.id}
      description={`User ${s.userId} · ${s.taskTypeCode} · ${s.profession}`}
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/sessions')}>
          <ArrowLeft className="mr-1 h-4 w-4" /> Back
        </Button>
      }
    >
      <SettingsSection title="Session">
        <div className="grid grid-cols-2 gap-2 text-sm md:grid-cols-4">
          <div><span className="text-admin-fg-muted">State:</span> <Badge variant={s.state === 'evaluated' ? 'success' : s.state === 'failed' ? 'danger' : 'info'} size="sm">{s.state}</Badge></div>
          <div><span className="text-admin-fg-muted">Turns:</span> {s.turnCount}</div>
          <div><span className="text-admin-fg-muted">Duration:</span> {s.durationSeconds}s</div>
          <div><span className="text-admin-fg-muted">Template:</span> {s.templateId ?? '—'}</div>
          <div><span className="text-admin-fg-muted">Created:</span> {new Date(s.createdAt).toLocaleString()}</div>
          <div><span className="text-admin-fg-muted">Started:</span> {s.startedAt ? new Date(s.startedAt).toLocaleString() : '—'}</div>
          <div><span className="text-admin-fg-muted">Completed:</span> {s.completedAt ? new Date(s.completedAt).toLocaleString() : '—'}</div>
          <div><span className="text-admin-fg-muted">Last error:</span> {s.lastErrorCode ?? '—'}</div>
        </div>
      </SettingsSection>

      {detail.evaluation && (
        <SettingsSection title="Evaluation">
          <div className="mb-2 text-sm text-admin-fg-default">
            <strong>Scaled:</strong> {detail.evaluation.overallScaled}/500 ·{' '}
            <strong>Grade:</strong> {detail.evaluation.overallGrade} ·{' '}
            <Badge variant={detail.evaluation.passed ? 'success' : 'warning'} size="sm">
              {detail.evaluation.passed ? 'PASSED' : 'below pass'}
            </Badge>
            {' '}· Rulebook v{detail.evaluation.rulebookVersion}
          </div>
          {detail.evaluation.advisory && <p className="mb-2 text-xs text-admin-fg-muted">{detail.evaluation.advisory}</p>}
          <div className="mb-3 space-y-1">
            {criteria.map((c) => (
              <div key={c.id} className="text-xs">
                <span className="font-semibold text-admin-fg-strong">{c.id}:</span> {c.score06}/{c.maxScore}: {c.evidence}
              </div>
            ))}
          </div>
          {strengths.length > 0 && (<div className="mb-2 text-xs"><strong>Strengths:</strong> {strengths.join(' | ')}</div>)}
          {improvements.length > 0 && (<div className="mb-2 text-xs"><strong>Improvements:</strong> {improvements.join(' | ')}</div>)}
          {suggestedPractice.length > 0 && (<div className="mb-2 text-xs"><strong>Practice:</strong> {suggestedPractice.join(' | ')}</div>)}
          {appliedRules.length > 0 && (<div className="text-[10px] text-admin-fg-muted">Rules applied: {appliedRules.join(', ')}</div>)}
        </SettingsSection>
      )}

      <SettingsSection title="Scenario">
        <pre className="overflow-x-auto rounded-admin-lg border border-admin-border bg-[var(--admin-bg-subtle)] p-3 text-[11px] text-admin-fg-default">{JSON.stringify(scenario, null, 2)}</pre>
      </SettingsSection>

      <SettingsSection title="Transcript">
        <div className="space-y-3">
          {detail.turns.map((t) => {
            const anns = detail.annotations.filter((a) => a.turnNumber === t.turnNumber);
            return (
              <div key={t.turnNumber} className="rounded-admin-lg border border-admin-border p-3">
                <div className="mb-1 text-[10px] font-semibold uppercase text-admin-fg-muted">
                  Turn {t.turnNumber} · {t.role} · {t.durationMs}ms{t.confidenceScore != null ? ` · conf ${(t.confidenceScore * 100).toFixed(0)}%` : ''}
                  {t.aiFeatureCode && ` · ${t.aiFeatureCode}`}
                </div>
                <p className="text-sm text-admin-fg-strong">{t.content}</p>
                {anns.map((a, i) => (
                  <div key={i} className="mt-2 flex items-start gap-2 text-xs">
                    <Badge variant={a.type === 'strength' ? 'success' : a.type === 'error' ? 'danger' : 'warning'} size="sm">{a.type}</Badge>
                    <div className="flex-1">
                      {a.evidence}
                      {a.ruleId && (<span className="ml-2 font-mono text-[10px] text-[var(--admin-primary)]">{a.ruleId}</span>)}
                      {a.suggestion && (<div className="mt-0.5 text-[11px] text-[var(--admin-primary)]">{a.suggestion}</div>)}
                    </div>
                  </div>
                ))}
              </div>
            );
          })}
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
