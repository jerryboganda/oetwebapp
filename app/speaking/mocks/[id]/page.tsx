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

  // Bootstrap: either resume an existing session via ?session=… or start a
  // new one. Cap-exceeded errors land here as a fail-loud message rather
  // than silently looping.
  useEffect(() => {
    if (!mockSetId) return;
    let active = true;
    (async () => {
      try {
        if (sessionParam) {
          const fresh = await fetchSpeakingMockSession(sessionParam);
          if (!active) return;
          setSession(fresh);
        } else {
          const started = await startSpeakingMockSet(mockSetId, 'exam');
          if (!active) return;
          setSession(started);
          analytics.track('speaking_mock_set_started_orchestrator', {
            mockSetId,
            sessionId: started.mockSessionId,
          });
          // Pin the session id into the URL so refresh resumes.
          router.replace(`/speaking/mocks/${mockSetId}?session=${started.mockSessionId}`);
        }
      } catch (e: unknown) {
        if (!active) return;
        setError(e instanceof Error ? e.message : 'Could not start the mock set.');
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
        <div className="mx-auto max-w-3xl p-6">
          <EmptyState title="Session not found" description="Try starting a fresh mock set." />
        </div>
      </LearnerDashboardShell>
    );
  }

  const role1Url = `/speaking/task/${session.rolePlay1.contentId}?mode=exam&mockSession=${session.mockSessionId}`;
  const role2Url = `/speaking/task/${session.rolePlay2.contentId}?mode=exam&mockSession=${session.mockSessionId}`;

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
              Finish the recording on the task page. We&apos;ll bring you back here automatically when grading completes.
            </p>
            <Link href={role1Url} className="mt-3 inline-block">
              <Button variant="primary">Open role-play 1</Button>
            </Link>
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
              <Link href={role2Url}>
                <Button variant="primary" disabled={!bridgeAck}>Begin role-play 2</Button>
              </Link>
            </div>
          </section>
        )}

        {stage === 'roleplay-2' && (
          <section className="rounded-2xl border border-info/30 bg-info/5 p-5">
            <h2 className="text-lg font-black">Role-play 2 in progress</h2>
            <p className="mt-1 text-sm text-muted">
              Finish the recording on the task page. We&apos;ll show your combined readiness band as soon as both are graded.
            </p>
            <Link href={role2Url} className="mt-3 inline-block">
              <Button variant="primary">Open role-play 2</Button>
            </Link>
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
