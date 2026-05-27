'use client';

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md.
//
// This is the speaking-mock-set orchestrator. A mock set bundles two
// role-plays — the canonical OET Speaking sub-test shape. The page
// stages through:
//   1. Pre-mock briefing (start the session, see both role-plays)
//   2. Role-play 1 (deep-link to /speaking/task/{id} in exam mode)
//   3. Bridge (acknowledge a 60s rest, then advance)
//   4. Role-play 2 (deep-link to /speaking/task/{id} in exam mode)
//   5. Combined results (combined readiness band + per-card scores)
//
// Stage progression is computed from the live attempt/evaluation state
// returned by GET /v1/speaking/mock-sessions/{id}, so a refresh in the
// middle of either role-play will resume cleanly.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { SpeakingSelfPracticeButton } from '@/components/domain/speaking-self-practice-button';
import {
  fetchSpeakingMockSession,
  finishSpeakingMockBridge,
  startSpeakingMockBridge,
  startSpeakingMockSet,
  type SpeakingMockSession,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

type Props = {
  params: Promise<{ id: string }>;
};

type Stage = 'briefing' | 'roleplay-1' | 'bridge' | 'roleplay-2' | 'awaiting-results' | 'scoring-failed' | 'results';

function rolePlaySubmitted(rolePlay: SpeakingMockSession['rolePlay1']): boolean {
  return rolePlay.state === 'submitted'
    || rolePlay.state === 'evaluating'
    || rolePlay.state === 'completed'
    || rolePlay.evaluationState === 'queued'
    || rolePlay.evaluationState === 'processing'
    || rolePlay.evaluationState === 'completed';
}

function rolePlayGraded(rolePlay: SpeakingMockSession['rolePlay1']): boolean {
  return rolePlay.state === 'completed' || rolePlay.evaluationState === 'completed';
}

function rolePlayFailed(rolePlay: SpeakingMockSession['rolePlay1']): boolean {
  return rolePlay.evaluationState === 'failed';
}

function deriveStage(session: SpeakingMockSession | null): Stage {
  if (!session) return 'briefing';
  if (session.combined.bothCompleted) return 'results';
  if (rolePlayFailed(session.rolePlay1) || rolePlayFailed(session.rolePlay2)) return 'scoring-failed';
  const r1Submitted = rolePlaySubmitted(session.rolePlay1);
  const r2Submitted = rolePlaySubmitted(session.rolePlay2);
  if (!r1Submitted) return 'roleplay-1';
  if (!r2Submitted) return 'bridge';
  return 'awaiting-results';
}

export default function SpeakingMockSetOrchestratorPage({ params }: Props) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const sessionParam = searchParams?.get('session') ?? null;

  const [mockSetId, setMockSetId] = useState<string>('');
  const [session, setSession] = useState<SpeakingMockSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [bridgeAck, setBridgeAck] = useState(false);
  const [bridgeSyncing, setBridgeSyncing] = useState(false);
  const trackedBridgeForRef = useRef<string | null>(null);
  const trackedAggregateForRef = useRef<string | null>(null);

  useEffect(() => {
    void params.then(({ id }) => setMockSetId(id));
  }, [params]);

  const refreshSession = useCallback(async (id: string) => {
    const fresh = await fetchSpeakingMockSession(id);
    setSession(fresh);
    return fresh;
  }, []);

  // Bootstrap: resume an existing session via ?session=… only. Starting a
  // fresh mock set now requires an explicit learner click so the weekly
  // entitlement is never consumed by merely opening the page.
  useEffect(() => {
    if (!mockSetId) return;
    let active = true;
    (async () => {
      try {
        if (sessionParam) {
          const fresh = await fetchSpeakingMockSession(sessionParam);
          if (!active) return;
          setSession(fresh);
        }
      } catch (e: unknown) {
        if (!active) return;
        setError(e instanceof Error ? e.message : 'Could not load the mock set.');
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => { active = false; };
  }, [mockSetId, sessionParam, router]);

  // Lightweight polling so finishing a role-play in the task page reflects
  // here without manual refresh.
  useEffect(() => {
    if (!session || session.combined.bothCompleted || rolePlayFailed(session.rolePlay1) || rolePlayFailed(session.rolePlay2)) return undefined;
    const interval = window.setInterval(() => {
      void refreshSession(session.mockSessionId).catch(() => undefined);
    }, 6_000);
    return () => window.clearInterval(interval);
  }, [refreshSession, session]);

  const stage = useMemo(() => deriveStage(session), [session]);
  const bridgeSessionId = stage === 'bridge' ? session?.mockSessionId : null;

  useEffect(() => {
    if (!session || stage !== 'bridge' || trackedBridgeForRef.current === session.mockSessionId) return;
    trackedBridgeForRef.current = session.mockSessionId;
    trackSpeaking('mock_bridge_viewed', { mockSetId: session.mockSetId });
  }, [session, stage]);

  useEffect(() => {
    if (!session || stage !== 'results' || trackedAggregateForRef.current === session.mockSessionId) return;
    trackedAggregateForRef.current = session.mockSessionId;
    trackSpeaking('mock_aggregated', {
      mockSetId: session.mockSetId,
      estimatedBand: session.combined.readinessBandLabel,
    });
  }, [session, stage]);

  useEffect(() => {
    if (!bridgeSessionId) return;
    let active = true;
    void startSpeakingMockBridge(bridgeSessionId)
      .then((fresh) => {
        if (active) setSession(fresh);
      })
      .catch(() => undefined);
    return () => { active = false; };
  }, [bridgeSessionId]);

  const beginMockSet = useCallback(async () => {
    if (!mockSetId || starting) return;
    setStarting(true);
    setError(null);
    try {
      const started = await startSpeakingMockSet(mockSetId, 'exam');
      setSession(started);
      analytics.track('speaking_mock_set_started_orchestrator', {
        mockSetId,
        sessionId: started.mockSessionId,
      });
      trackSpeaking('mock_started', { mockSetId });
      router.replace(`/speaking/mocks/${mockSetId}?session=${started.mockSessionId}`);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Could not start the mock set.');
    } finally {
      setStarting(false);
    }
  }, [mockSetId, router, starting]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking mock set">
        <div className="mx-auto max-w-3xl space-y-4 p-6">
          <Skeleton className="h-32" />
          <Skeleton className="h-44" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Speaking mock set">
        <div className="mx-auto max-w-3xl p-6">
          <EmptyState title="Could not start mock set" description={error} />
          <div className="mt-4 flex justify-center">
            <Button variant="outline" asChild>
<Link href="/speaking/mocks">Back to mock sets</Link>
</Button>
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!session) {
    return (
      <LearnerDashboardShell pageTitle="Speaking mock set">
        <div className="mx-auto max-w-3xl space-y-4 p-6">
          <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <Badge variant="info" className="text-[10px] uppercase tracking-wider">Mock set</Badge>
            <h1 className="mt-3 text-3xl font-black">Ready to begin</h1>
            <p className="mt-2 text-sm text-muted">
              This will create your two-role-play mock session and consume one weekly mock-set allowance on the free plan. You can still go back without using an allowance.
            </p>
            <div className="mt-5 flex flex-wrap gap-2">
              <Button variant="primary" onClick={() => void beginMockSet()} disabled={starting}>
                {starting ? 'Starting...' : 'Start timed mock set'}
              </Button>
              <Button variant="outline" asChild>
<Link href="/speaking/mocks">Back</Link>
</Button>
            </div>
          </section>
        </div>
      </LearnerDashboardShell>
    );
  }

  const role1Url = `/speaking/task/${encodeURIComponent(session.rolePlay1.contentId)}?mode=exam&mockSession=${encodeURIComponent(session.mockSessionId)}&mockSetId=${encodeURIComponent(session.mockSetId)}&attemptId=${encodeURIComponent(session.rolePlay1.attemptId)}`;
  const role2Url = `/speaking/task/${encodeURIComponent(session.rolePlay2.contentId)}?mode=exam&mockSession=${encodeURIComponent(session.mockSessionId)}&mockSetId=${encodeURIComponent(session.mockSetId)}&attemptId=${encodeURIComponent(session.rolePlay2.attemptId)}`;

  const finishBridgeAndRecordRole2 = async () => {
    if (!session || bridgeSyncing) return;
    setBridgeSyncing(true);
    setError(null);
    try {
      const fresh = await finishSpeakingMockBridge(session.mockSessionId);
      setSession(fresh);
      router.push(role2Url);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Could not advance to role-play 2.');
    } finally {
      setBridgeSyncing(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Speaking mock set">
      <div className="mx-auto w-full max-w-3xl space-y-6 p-4 sm:p-6">
        <header className="space-y-2">
          <div className="flex items-center gap-2">
            <Badge variant="info" className="text-[10px] uppercase tracking-wider">
              Mock set
            </Badge>
            <Badge variant="muted" className="text-[10px] uppercase tracking-wider">
              {stage.replace('-', ' ')}
            </Badge>
          </div>
          <h1 className="text-3xl font-black">{session.title}</h1>
          {session.description && (
            <p className="text-sm text-muted">{session.description}</p>
          )}
        </header>

        <ol className="grid gap-3 sm:grid-cols-2">
          <StageStepCard
            label="Role-play 1"
            done={rolePlayGraded(session.rolePlay1)}
            submitted={rolePlaySubmitted(session.rolePlay1)}
            failed={rolePlayFailed(session.rolePlay1)}
            active={stage === 'roleplay-1'}
            title={session.rolePlay1.title}
            scaled={session.rolePlay1.estimatedScaledScore}
            band={session.rolePlay1.readinessBandLabel}
          />
          <StageStepCard
            label="Role-play 2"
            done={rolePlayGraded(session.rolePlay2)}
            submitted={rolePlaySubmitted(session.rolePlay2)}
            failed={rolePlayFailed(session.rolePlay2)}
            active={stage === 'roleplay-2'}
            title={session.rolePlay2.title}
            scaled={session.rolePlay2.estimatedScaledScore}
            band={session.rolePlay2.readinessBandLabel}
          />
        </ol>

        {stage === 'briefing' && (
          <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="text-lg font-black">You&apos;re about to start</h2>
            <p className="mt-1 text-sm text-muted">
              Mock-set rules: <strong>strict 5-minute role-plays</strong>, audible 30-second warning, automatic submission at the cap. The AI patient won&apos;t pause for you.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="primary" asChild>
<Link href={role1Url}>Begin role-play 1</Link>
</Button>
              <Button variant="outline" asChild>
<Link href="/speaking/mocks">Back</Link>
</Button>
            </div>
          </section>
        )}

        {stage === 'roleplay-1' && (
          <section className="rounded-2xl border border-info/30 bg-info/5 p-5">
            <h2 className="text-lg font-black">Role-play 1 in progress</h2>
            <p className="mt-1 text-sm text-muted">
              Start with the AI patient for the interactive role-play, then submit the recorded evaluation when you are ready for scoring.
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <SpeakingSelfPracticeButton taskId={session.rolePlay1.contentId} label="Start AI patient role-play 1" />
              <Button variant="outline" asChild>
<Link href={role1Url} className="inline-block">Record for evaluation</Link>
</Button>
            </div>
          </section>
        )}

        {stage === 'bridge' && (
          <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="text-lg font-black">60-second bridge</h2>
            <p className="mt-1 text-sm text-muted">
              Take 60 seconds to read role-play 2 case notes. The interlocutor is briefly resetting. Tick the box when You&apos;re ready.
            </p>
            <label className="mt-3 flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                checked={bridgeAck}
                onChange={(e) => setBridgeAck(e.target.checked)}
                className="mt-1 h-4 w-4 rounded border-border text-primary focus:ring-primary"
              />
              <span>I&apos;ve read the case notes for role-play 2 and I&apos;m ready.</span>
            </label>
            <div className="mt-4">
              <div className="flex flex-wrap gap-2">
                {bridgeAck ? (
                  <SpeakingSelfPracticeButton taskId={session.rolePlay2.contentId} label="Start AI patient role-play 2" />
                ) : null}
                <Button variant="outline" disabled={!bridgeAck || bridgeSyncing} onClick={() => void finishBridgeAndRecordRole2()}>
                  {bridgeSyncing ? 'Preparing role-play 2...' : 'Record for evaluation'}
                </Button>
              </div>
            </div>
          </section>
        )}

        {stage === 'roleplay-2' && (
          <section className="rounded-2xl border border-info/30 bg-info/5 p-5">
            <h2 className="text-lg font-black">Role-play 2 in progress</h2>
            <p className="mt-1 text-sm text-muted">
              Complete the AI-patient interaction first, then submit the timed recording so the combined readiness band can be calculated.
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <SpeakingSelfPracticeButton taskId={session.rolePlay2.contentId} label="Start AI patient role-play 2" />
              <Button variant="outline" asChild>
<Link href={role2Url} className="inline-block">Record for evaluation</Link>
</Button>
            </div>
          </section>
        )}

        {stage === 'awaiting-results' && (
          <section className="rounded-2xl border border-info/30 bg-info/5 p-5">
            <h2 className="text-lg font-black">Scoring in progress</h2>
            <p className="mt-1 text-sm text-muted">
              Both role-plays have been submitted. Results will appear here as soon as scoring finishes.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="outline" onClick={() => void refreshSession(session.mockSessionId)}>
                Refresh results
              </Button>
              <Button variant="ghost" asChild>
<Link href="/speaking/mocks">All mock sets</Link>
</Button>
            </div>
          </section>
        )}

        {stage === 'scoring-failed' && (
          <section className="rounded-2xl border border-danger/30 bg-danger/5 p-5">
            <h2 className="text-lg font-black">Scoring needs attention</h2>
            <p className="mt-1 text-sm text-muted">
              One role-play could not be scored. Refresh once in case the scorer recovered; if it still fails, start a fresh mock set from the catalogue.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="outline" onClick={() => void refreshSession(session.mockSessionId)}>
                Refresh status
              </Button>
              <Button variant="ghost" asChild>
<Link href="/speaking/mocks">All mock sets</Link>
</Button>
            </div>
          </section>
        )}

        {stage === 'results' && (
          <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <div className="flex flex-wrap items-center gap-3">
              <Badge variant="info">{session.combined.readinessBandLabel}</Badge>
              <span className="text-sm text-muted">
                Estimated <strong className="text-foreground">{session.combined.estimatedScaledScore ?? '—'}/500</strong> · pass threshold {session.combined.passThreshold}
              </span>
            </div>
            <p className="mt-3 text-sm text-muted">
              Combined score is the average of both role-plays. Open each role-play below to see criterion breakdowns and rule citations.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              {session.rolePlay1.evaluationId && (
                <Button variant="outline" asChild>
<Link href={`/speaking/results/${session.rolePlay1.evaluationId}`}>Role-play 1 details</Link>
</Button>
              )}
              {session.rolePlay2.evaluationId && (
                <Button variant="outline" asChild>
<Link href={`/speaking/results/${session.rolePlay2.evaluationId}`}>Role-play 2 details</Link>
</Button>
              )}
              <Button variant="ghost" asChild>
<Link href="/speaking/mocks">All mock sets</Link>
</Button>
            </div>
          </section>
        )}
      </div>
    </LearnerDashboardShell>
  );
}

function StageStepCard({
  label,
  done,
  submitted,
  failed,
  active,
  title,
  scaled,
  band,
}: {
  label: string;
  done: boolean;
  submitted: boolean;
  failed: boolean;
  active: boolean;
  title: string;
  scaled: number | null;
  band: string;
}) {
  return (
    <li
      aria-current={active ? 'step' : undefined}
      className={`rounded-2xl border p-4 transition ${
        done
          ? 'border-success/30 bg-success/5'
          : failed
            ? 'border-danger/30 bg-danger/5'
          : active
            ? 'border-primary/40 bg-primary/5'
            : 'border-border bg-surface'
      }`}
    >
      <div className="flex items-center gap-2">
        <Badge variant={done ? 'success' : failed ? 'danger' : active ? 'info' : 'muted'} className="text-[10px] uppercase tracking-wider">
          {label}
        </Badge>
        {failed && <span className="text-[10px] font-bold uppercase tracking-widest text-danger">Failed</span>}
        {done && <span className="text-[10px] font-bold uppercase tracking-widest text-success">Graded</span>}
        {!failed && !done && submitted && <span className="text-[10px] font-bold uppercase tracking-widest text-info">Submitted</span>}
        {active && !failed && !done && <span className="text-[10px] font-bold uppercase tracking-widest text-primary">In progress</span>}
      </div>
      <p className="mt-2 text-sm font-bold">{title}</p>
      {done && (
        <p className="mt-1 text-xs text-muted">
          {scaled !== null ? `Estimated ${scaled}/500` : 'Awaiting score'} · {band}
        </p>
      )}
    </li>
  );
}
