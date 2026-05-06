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

import { useCallback, useEffect, useMemo, useState } from 'react';
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
  startSpeakingMockSet,
  type SpeakingMockSession,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

type Props = {
  params: Promise<{ id: string }>;
};

type Stage = 'briefing' | 'roleplay-1' | 'bridge' | 'roleplay-2' | 'results';

function deriveStage(session: SpeakingMockSession | null): Stage {
  if (!session) return 'briefing';
  if (session.combined.bothCompleted) return 'results';
  const r1Done = session.rolePlay1.evaluationState === 'completed';
  const r2Done = session.rolePlay2.evaluationState === 'completed';
  if (!r1Done) return 'roleplay-1';
  if (r1Done && !r2Done) return 'bridge';
  return 'roleplay-2';
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
    if (!session || session.combined.bothCompleted) return undefined;
    const interval = window.setInterval(() => {
      void refreshSession(session.mockSessionId).catch(() => undefined);
    }, 6_000);
    return () => window.clearInterval(interval);
  }, [refreshSession, session]);

  const stage = useMemo(() => deriveStage(session), [session]);

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
            <Link href="/speaking/mocks"><Button variant="outline">Back to mock sets</Button></Link>
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
              <Link href="/speaking/mocks"><Button variant="outline">Back</Button></Link>
            </div>
          </section>
        </div>
      </LearnerDashboardShell>
    );
  }

  const role1Url = `/speaking/task/${session.rolePlay1.contentId}?mode=exam&mockSession=${session.mockSessionId}&attemptId=${session.rolePlay1.attemptId}`;
  const role2Url = `/speaking/task/${session.rolePlay2.contentId}?mode=exam&mockSession=${session.mockSessionId}&attemptId=${session.rolePlay2.attemptId}`;

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
            done={session.rolePlay1.evaluationState === 'completed'}
            active={stage === 'roleplay-1'}
            title={session.rolePlay1.title}
            scaled={session.rolePlay1.estimatedScaledScore}
            band={session.rolePlay1.readinessBandLabel}
          />
          <StageStepCard
            label="Role-play 2"
            done={session.rolePlay2.evaluationState === 'completed'}
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
              <Link href={role1Url}>
                <Button variant="primary">Begin role-play 1</Button>
              </Link>
              <Link href="/speaking/mocks"><Button variant="outline">Back</Button></Link>
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
              <Link href={role1Url} className="inline-block">
                <Button variant="outline">Record for evaluation</Button>
              </Link>
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
                <Link href={role2Url}>
                  <Button variant="outline" disabled={!bridgeAck}>Record for evaluation</Button>
                </Link>
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
              <Link href={role2Url} className="inline-block">
                <Button variant="outline">Record for evaluation</Button>
              </Link>
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
                <Link href={`/speaking/results/${session.rolePlay1.evaluationId}`}>
                  <Button variant="outline">Role-play 1 details</Button>
                </Link>
              )}
              {session.rolePlay2.evaluationId && (
                <Link href={`/speaking/results/${session.rolePlay2.evaluationId}`}>
                  <Button variant="outline">Role-play 2 details</Button>
                </Link>
              )}
              <Link href="/speaking/mocks"><Button variant="ghost">All mock sets</Button></Link>
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
  active,
  title,
  scaled,
  band,
}: {
  label: string;
  done: boolean;
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
          : active
            ? 'border-primary/40 bg-primary/5'
            : 'border-border bg-surface'
      }`}
    >
      <div className="flex items-center gap-2">
        <Badge variant={done ? 'success' : active ? 'info' : 'muted'} className="text-[10px] uppercase tracking-wider">
          {label}
        </Badge>
        {done && <span className="text-[10px] font-bold uppercase tracking-widest text-success">Graded</span>}
        {active && !done && <span className="text-[10px] font-bold uppercase tracking-widest text-primary">In progress</span>}
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
