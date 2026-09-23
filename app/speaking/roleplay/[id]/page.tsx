'use client';

/**
 * Role-play card entry (Practice Library card and the free Speaking sample).
 *
 * 23 Sep 2026 owner flow, identical for practice and free:
 *   Rules + consent (no timer) → 3-min prep with the card → 5-min speaking →
 *   Finish & submit → processing → result.
 *
 * This page shows the exam-style card and the ONE Rules + consent step. On
 * Start it creates the shared-engine session (the server decides whether it
 * is the free sample — `?free=1` is only a hint for the copy), records
 * consent, skips the warm-up and opens the prep timer.
 */
import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { SpeakingRulesConsent } from '@/components/domain/speaking/SpeakingRulesConsent';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { ApiError, fetchRoleCard } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { listFreeSamples, type FreeSampleOption } from '@/lib/api/free-samples';
import {
  createSpeakingSession,
  finishSpeakingWarmup,
  recordConsent,
  startSpeakingWarmup,
} from '@/lib/api/speaking-sessions';
import type { RoleCard } from '@/lib/mock-data';

const CONSENT_VERSION = 'recording.v1';

/** A repeat call after a partial failure may hit an already-advanced state. */
function ignoreConflict(err: unknown): null {
  if (err instanceof ApiError && err.status === 409) return null;
  throw err;
}

export default function RoleCardPreview() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';
  const requestedFreeCard = searchParams?.get('free') === '1';

  const [card, setCard] = useState<RoleCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [freeRow, setFreeRow] = useState<FreeSampleOption | null>(null);
  const [freeKnown, setFreeKnown] = useState(!requestedFreeCard);
  // Reused if a later step fails, so a retry never creates a second session.
  const sessionIdRef = useRef<string | null>(null);

  useEffect(() => {
    fetchRoleCard(id, { freeSample: requestedFreeCard })
      .then(setCard)
      .catch(() => setCard(null))
      .finally(() => setLoading(false));
  }, [id, requestedFreeCard]);

  useEffect(() => {
    if (!requestedFreeCard) return;
    let active = true;
    listFreeSamples('speaking')
      .then((rows) => {
        if (active) setFreeRow((Array.isArray(rows) ? rows : []).find((row) => row.contentId === id) ?? null);
      })
      .catch(() => {
        if (active) setFreeRow(null);
      })
      .finally(() => {
        if (active) setFreeKnown(true);
      });
    return () => {
      active = false;
    };
  }, [id, requestedFreeCard]);

  const isFreeCard = requestedFreeCard && freeRow !== null;
  const freeCompleted = freeRow?.state === 'completed';

  const handleStart = async () => {
    try {
      let sessionId = sessionIdRef.current;
      if (!sessionId) {
        const created = await createSpeakingSession({
          rolePlayCardId: id,
          mode: 'ai_self_practice',
          consentVersion: CONSENT_VERSION,
        });
        sessionId = created.sessionId;
        sessionIdRef.current = sessionId;
      }
      // Consent BEFORE any timer; the server also records the account-level
      // Recording + AiProcessing + Retention consents in this call.
      await recordConsent(sessionId, CONSENT_VERSION);
      // Warm-up is skipped for practice/free: finish-warmup starts prep.
      await startSpeakingWarmup(sessionId).catch(ignoreConflict);
      const prep = await finishSpeakingWarmup(sessionId).catch(ignoreConflict);
      showCreditFeedback(prep?.feedbackMessage);
      analytics.track('task_started', { taskId: id, subtest: 'speaking', mode: 'self' });
      router.push(`/speaking/sessions/${encodeURIComponent(sessionId)}/prep`);
    } catch (err) {
      throw new Error(err instanceof ApiError ? err.userMessage : err instanceof Error ? err.message : 'Could not start the Speaking session.');
    }
  };

  if (loading || !freeKnown) {
    return (
      <LearnerDashboardShell pageTitle="Role Card">
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:gap-8">
          <Skeleton className="h-[280px] rounded-xl sm:h-[340px] lg:h-96" />
          <Skeleton className="h-[280px] rounded-xl sm:h-[340px] lg:h-96" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!card) {
    return (
      <LearnerDashboardShell pageTitle="Role Card">
        <InlineAlert variant="error">Role card not found for this task.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={card.title}>
      <div className="space-y-4">
        <header>
          <p className="text-xs font-bold uppercase tracking-widest text-muted">
            {isFreeCard ? 'Free Speaking Mock' : 'Practice Library'} · 3 min prep · 5 min role-play
          </p>
          <h1 className="mt-1 text-xl font-bold text-navy sm:text-2xl">{card.title}</h1>
        </header>

        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:gap-8">
          <SpeakingRoleCard
            role={card.profession}
            setting={card.setting}
            patient={card.patient}
            background={card.background}
            tasks={card.tasks}
            prepTimeSeconds={card.prepTimeSeconds ?? 180}
            roleplayTimeSeconds={card.roleplayTimeSeconds ?? 300}
            disclaimer={card.disclaimer}
            sourceAttribution={card.sourceAttribution}
          />

          <div className="space-y-3">
            {requestedFreeCard && !isFreeCard ? (
              <InlineAlert variant="error">This is not the designated free Speaking card.</InlineAlert>
            ) : freeCompleted ? (
              <InlineAlert variant="info" title="Free sample completed">
                You have used both free graded attempts. Open the Practice Library to keep practising.
              </InlineAlert>
            ) : (
              <>
                <SpeakingRulesConsent freeSample={isFreeCard} onStart={handleStart} />
                <p className="text-center text-xs font-semibold text-muted" data-testid="speaking-card-credit-cost">
                  {isFreeCard ? 'No AI credit required for the free sample' : '1 card = 2 AI credits · charged only when graded'}
                </p>
              </>
            )}
          </div>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
