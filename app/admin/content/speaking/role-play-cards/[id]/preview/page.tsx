'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Preview view for a role-play card. Toggles between two views:
 *   - Learner view: shows ONLY the candidate-facing card (proves the
 *     learner serializer never leaks interlocutor data).
 *   - Tutor view: shows both candidate card AND interlocutor script
 *     side-by-side so calibration and content review can happen
 *     before publish.
 */

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, ShieldAlert } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';

import { Toast } from '@/components/ui/alert';
import {
  adminGetInterlocutorScript,
  adminGetRolePlayCard,
  type InterlocutorScriptDetail,
  type RolePlayCardDetail,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type Mode = 'learner' | 'tutor';

const BREADCRUMBS_BASE = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
];

export default function PreviewSpeakingRolePlayCardPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const cardId = params?.id ?? '';

  const [mode, setMode] = useState<Mode>('learner');
  const [card, setCard] = useState<RolePlayCardDetail | null>(null);
  const [script, setScript] = useState<InterlocutorScriptDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);

  const reload = useCallback(async () => {
    if (!cardId) return;
    setLoading(true);
    try {
      const [detail, existing] = await Promise.all([
        adminGetRolePlayCard(cardId),
        adminGetInterlocutorScript(cardId),
      ]);
      setCard(detail);
      setScript(existing);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [cardId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const breadcrumbs = [
    ...BREADCRUMBS_BASE,
    { label: card?.scenarioTitle ?? 'Card', href: `/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}` },
    { label: 'Preview' },
  ];

  return (
    <AdminCatalogLayout
      title={card ? `Preview: ${card.scenarioTitle}` : 'Preview role-play card'}
      description="Compare what a learner sees against what a tutor sees. The learner view confirms no interlocutor data leaks."
      breadcrumbs={breadcrumbs}
      eyebrow="CMS · Preview"
      backHref={`/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}`}
      hideViewModeToggle
      actions={
        <Button
          variant="outline"
          onClick={() =>
            router.push(
              `/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}`,
            )
          }
        >
          <ArrowLeft className="mr-1 h-4 w-4" /> Back to card
        </Button>
      }
    >
      <Card className="col-span-full">
        <CardContent className="p-4">
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant={mode === 'learner' ? 'primary' : 'outline'}
              size="sm"
              onClick={() => setMode('learner')}
            >
              Learner view
            </Button>
            <Button
              variant={mode === 'tutor' ? 'primary' : 'outline'}
              size="sm"
              onClick={() => setMode('tutor')}
            >
              Tutor view
            </Button>
          </div>
        </CardContent>
      </Card>

      {loading ? (
        <div className="col-span-full space-y-3">
          <Skeleton className="h-64" />
        </div>
      ) : card ? (
        mode === 'learner' ? (
          <div className="col-span-full">
            <CandidateCardView card={card} />
          </div>
        ) : (
          <div className="col-span-full grid gap-4 lg:grid-cols-2">
            <CandidateCardView card={card} />
            <InterlocutorScriptView script={script} />
          </div>
        )
      ) : (
        <Card className="col-span-full">
          <CardContent className="p-8 text-center text-admin-fg-muted">Card not found.</CardContent>
        </Card>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}

function CandidateCardView({ card }: { card: RolePlayCardDetail }) {
  return (
    <Card>
      <CardContent className="p-6">
        <header className="mb-4 flex items-start justify-between gap-3">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.16em] text-admin-fg-muted">Candidate card</p>
            <h2 className="text-xl font-bold text-admin-fg-strong">{card.scenarioTitle}</h2>
          </div>
          <div className="flex flex-wrap items-center gap-1">
            <Badge variant="default" intensity="tinted">{card.professionId}</Badge>
            <Badge variant="default" intensity="tinted">{card.difficulty}</Badge>
          </div>
        </header>

        <dl className="grid gap-3 text-sm">
          <Row label="Setting" value={card.setting} />
          <Row label="Candidate role" value={card.candidateRole} />
          <Row label="Interlocutor role" value={card.interlocutorRole} />
          {card.patientName ? (
            <Row label="Patient" value={`${card.patientName}${card.patientAge ? `, ${card.patientAge}` : ''}`} />
          ) : null}
          <Row label="Clinical topic" value={card.clinicalTopic} />
          <Row label="Background" value={card.background} multiline />
        </dl>

        {card.tasks.length > 0 ? (
          <section className="mt-4 space-y-1">
            <p className="text-xs font-bold uppercase tracking-wider text-admin-fg-muted">Tasks</p>
            <ol className="ml-5 list-decimal space-y-1 text-sm text-admin-fg-strong">
              {card.tasks.map((t, i) => (
                <li key={i}>{t}</li>
              ))}
            </ol>
          </section>
        ) : null}

        <footer className="mt-4 border-t border-admin-border pt-3 text-xs text-admin-fg-muted">
          <p>
            Prep {card.prepTimeSeconds}s - Role-play {card.rolePlayTimeSeconds}s -{' '}
            {card.allowedNotes ? 'Notes allowed' : 'No notes'}
          </p>
          <p className="mt-1 italic">{card.disclaimer}</p>
        </footer>
      </CardContent>
    </Card>
  );
}

function InterlocutorScriptView({ script }: { script: InterlocutorScriptDetail | null }) {
  if (!script) {
    return (
      <Card>
        <CardContent className="p-6">
          <header className="mb-2 flex items-center gap-2">
            <ShieldAlert className="h-4 w-4 text-admin-warning" />
            <p className="text-xs font-bold uppercase tracking-[0.16em] text-admin-warning">
              Interlocutor script
            </p>
          </header>
          <p className="text-sm text-admin-fg-muted">
            No interlocutor script authored yet. The card cannot be published without one.
          </p>
        </CardContent>
      </Card>
    );
  }
  return (
    <Card className="border-2 border-admin-warning">
      <CardContent className="p-6">
        <header className="mb-4 flex items-center gap-2">
          <ShieldAlert className="h-4 w-4 text-admin-warning" />
          <p className="text-xs font-bold uppercase tracking-[0.16em] text-admin-warning">
            Interlocutor script (hidden)
          </p>
        </header>
        <dl className="grid gap-3 text-sm">
          <Row label="Opening response" value={script.openingResponse} multiline />
          {script.prompt1 ? <Row label="Prompt 1" value={script.prompt1} multiline /> : null}
          {script.prompt2 ? <Row label="Prompt 2" value={script.prompt2} multiline /> : null}
          {script.prompt3 ? <Row label="Prompt 3" value={script.prompt3} multiline /> : null}
          <Row label="Hidden information" value={script.hiddenInformation} multiline />
          <Row label="Resistance level" value={script.resistanceLevel} />
          <Row label="Closing cue" value={script.closingCue} multiline />
          <Row label="Emotional state" value={script.emotionalState} />
          {script.professionRoleNotes ? (
            <Row label="Profession role notes" value={script.professionRoleNotes} multiline />
          ) : null}
          {script.layLanguageTriggers.length > 0 ? (
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
                Lay-language triggers
              </dt>
              <dd className="mt-1 flex flex-wrap gap-1">
                {script.layLanguageTriggers.map((t) => (
                  <Badge key={t} variant="default" intensity="tinted">
                    {t}
                  </Badge>
                ))}
              </dd>
            </div>
          ) : null}
        </dl>
      </CardContent>
    </Card>
  );
}

function Row({ label, value, multiline }: { label: string; value: string; multiline?: boolean }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</dt>
      <dd className={`mt-0.5 ${multiline ? 'whitespace-pre-line' : ''} text-admin-fg-strong`}>{value}</dd>
    </div>
  );
}
