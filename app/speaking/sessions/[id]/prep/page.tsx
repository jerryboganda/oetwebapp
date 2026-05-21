'use client';

/**
 * Prep page for a Speaking session (plan C.2 / C.3).
 *
 * Mounted at `/speaking/sessions/[id]/prep`. The session has already
 * been created by an upstream flow (selection or mock test launcher),
 * so the only thing this route does is:
 *
 *   1. Fetch the session detail (includes the learner-safe role-play card
 *      copy via `card`).
 *   2. Render the prep countdown alongside a read-only candidate card
 *      and a local-only notes editor.
 *   3. On countdown completion (or explicit skip) call
 *      `startRolePlay()` and navigate to the recording room.
 *
 * No recordings happen here — consent is captured downstream in the
 * recording room via `<SpeakingConsentBanner>`.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowRight, BookOpen, Loader2, NotebookPen, SkipForward } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { PrepCountdown } from '@/components/domain/speaking/PrepCountdown';
import {
  getSpeakingSession,
  startRolePlay,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import { ApiError } from '@/lib/api';

const QUICK_PICK_PROMPTS: { label: string; insert: string }[] = [
  {
    label: 'Identify patient emotion',
    insert: 'Patient emotion: ',
  },
  {
    label: "What's my goal?",
    insert: 'My communication goal: ',
  },
  {
    label: 'Simplify medical terms',
    insert: 'Plain-language terms to use: ',
  },
];

export default function SpeakingSessionPrepPage() {
  const params = useParams<{ id: string }>();
  const sessionId = params?.id ?? '';
  const router = useRouter();

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [notes, setNotes] = useState('');
  const [confirmSkipOpen, setConfirmSkipOpen] = useState(false);
  const [transitioning, setTransitioning] = useState(false);
  const [transitionError, setTransitionError] = useState<string | null>(null);

  // ── Load session ─────────────────────────────────────────────────────────
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    window.queueMicrotask(() => {
      if (!cancelled) setLoading(true);
    });
    getSpeakingSession(sessionId)
      .then((s) => {
        if (cancelled) return;
        setSession(s);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const msg =
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not load session.';
        setLoadError(msg);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  const beginRolePlay = useCallback(async () => {
    if (!sessionId) return;
    setTransitioning(true);
    setTransitionError(null);
    try {
      await startRolePlay(sessionId);
      router.push(`/speaking/sessions/${sessionId}`);
    } catch (err) {
      const msg =
        err instanceof ApiError
          ? err.userMessage
          : err instanceof Error
            ? err.message
            : 'Could not start the role-play. Please try again.';
      setTransitionError(msg);
      setTransitioning(false);
    }
  }, [router, sessionId]);

  const handleSkipConfirm = useCallback(() => {
    setConfirmSkipOpen(false);
    void beginRolePlay();
  }, [beginRolePlay]);

  const insertSnippet = useCallback(
    (snippet: string) => {
      setNotes((current) => {
        const trimmed = current.replace(/\s+$/u, '');
        if (trimmed.length === 0) return snippet;
        return `${trimmed}\n${snippet}`;
      });
    },
    [],
  );

  const prepSeconds = useMemo(() => {
    if (!session) return 180;
    return Math.max(30, Math.floor(session.card.prepTimeSeconds || 180));
  }, [session]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-slate-500">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Loading session…
        </span>
      </div>
    );
  }

  if (loadError || !session) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-rose-200 bg-rose-50 p-6 text-sm text-rose-900">
        <h2 className="text-base font-semibold">Could not load this session</h2>
        <p className="mt-1">{loadError ?? 'Session not available.'}</p>
        <Button
          type="button"
          variant="outline"
          className="mt-4"
          onClick={() => router.push('/speaking')}
        >
          Back to speaking
        </Button>
      </div>
    );
  }

  const { card } = session;

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wider text-slate-500">
            Speaking · Prep
          </p>
          <h1 className="text-2xl font-bold text-slate-900">{card.scenarioTitle}</h1>
          <p className="text-sm text-slate-600">
            {card.setting} · {card.candidateRole}
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setConfirmSkipOpen(true)}
          disabled={transitioning}
        >
          <SkipForward className="mr-2 h-4 w-4" aria-hidden /> Skip prep
        </Button>
      </header>

      <div className="grid gap-6 lg:grid-cols-[1fr_minmax(280px,420px)]">
        {/* Candidate card */}
        <section
          aria-labelledby="candidate-card-heading"
          className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm"
        >
          <div className="flex items-center gap-2">
            <BookOpen className="h-4 w-4 text-slate-500" aria-hidden />
            <h2
              id="candidate-card-heading"
              className="text-sm font-semibold uppercase tracking-wide text-slate-600"
            >
              Candidate card
            </h2>
          </div>
          <dl className="mt-4 space-y-3 text-sm text-slate-800">
            <Row label="Setting">{card.setting}</Row>
            <Row label="Your role">{card.candidateRole}</Row>
            <Row label="You are speaking with">{card.interlocutorRole}</Row>
            {card.patientName ? <Row label="Patient name">{card.patientName}</Row> : null}
            {card.patientAge ? <Row label="Patient age">{card.patientAge}</Row> : null}
            <Row label="Background">
              <p className="whitespace-pre-wrap leading-relaxed">{card.background}</p>
            </Row>
            {card.tasks.length > 0 ? (
              <Row label="Tasks">
                <ol className="list-decimal space-y-1 pl-5">
                  {card.tasks.map((task, idx) => (
                    <li key={`${idx}-${task.slice(0, 12)}`} className="leading-relaxed">
                      {task}
                    </li>
                  ))}
                </ol>
              </Row>
            ) : null}
            <Row label="Communication goal">{card.communicationGoal}</Row>
            {card.disclaimer ? (
              <p className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-900">
                {card.disclaimer}
              </p>
            ) : null}
          </dl>
        </section>

        {/* Prep sidebar */}
        <aside className="flex flex-col gap-4">
          <PrepCountdown
            durationSeconds={prepSeconds}
            onComplete={() => void beginRolePlay()}
          />

          <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="mb-2 flex items-center gap-2">
              <NotebookPen className="h-4 w-4 text-slate-500" aria-hidden />
              <h2 className="text-sm font-semibold text-slate-700">
                {card.allowedNotes ? 'Your notes' : 'Mental prep'}
              </h2>
            </div>
            {!card.allowedNotes ? (
              <p className="mb-2 rounded-md bg-slate-50 px-2 py-1 text-xs text-slate-600">
                This card simulates exam conditions — notes will not be visible during the role-play.
              </p>
            ) : null}
            <div className="flex flex-wrap gap-1.5">
              {QUICK_PICK_PROMPTS.map((p) => (
                <button
                  key={p.label}
                  type="button"
                  onClick={() => insertSnippet(p.insert)}
                  className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs text-slate-700 transition-colors hover:border-slate-300 hover:bg-slate-100"
                  data-testid={`prep-quick-pick-${p.label.toLowerCase().replace(/\s+/g, '-')}`}
                >
                  + {p.label}
                </button>
              ))}
            </div>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Jot any quick reminders for yourself…"
              className="mt-3 h-32 w-full resize-y rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30"
              aria-label="Prep notes"
            />
            <p className="mt-1 text-xs text-slate-500">
              Notes are local to this device and never sent to the server.
            </p>
          </div>

          {transitionError ? (
            <p
              role="alert"
              className="rounded-md border border-rose-300 bg-rose-50 p-2 text-xs text-rose-800"
            >
              {transitionError}
            </p>
          ) : null}
        </aside>
      </div>

      {/* Confirm skip modal */}
      <Modal
        open={confirmSkipOpen}
        onClose={() => setConfirmSkipOpen(false)}
        title="Skip preparation time?"
        size="sm"
      >
        <p className="text-sm text-slate-700">
          You&apos;ll move straight to the role-play with no further chance to plan. Some
          candidates find this useful for exam practice — but a quick pause is usually
          worth it.
        </p>
        <div className="mt-4 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            type="button"
            variant="ghost"
            onClick={() => setConfirmSkipOpen(false)}
          >
            Keep preparing
          </Button>
          <Button
            type="button"
            variant="primary"
            onClick={handleSkipConfirm}
            disabled={transitioning}
          >
            {transitioning ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Starting…
              </>
            ) : (
              <>
                <ArrowRight className="mr-2 h-4 w-4" aria-hidden /> Start role-play
              </>
            )}
          </Button>
        </div>
      </Modal>
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1 sm:flex-row sm:gap-3">
      <dt className="w-40 shrink-0 text-xs font-semibold uppercase tracking-wide text-slate-500">
        {label}
      </dt>
      <dd className="flex-1 text-sm text-slate-900">{children}</dd>
    </div>
  );
}
