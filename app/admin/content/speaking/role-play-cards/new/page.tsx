'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Two-step wizard for authoring a new role-play card:
 *   Step 1: candidate card (RolePlayCardEditor) — saves as Draft.
 *   Step 2: hidden interlocutor script (InterlocutorScriptEditor) — once
 *           saved we redirect to the card's detail page where the admin
 *           can publish.
 *
 * Admins can also skip step 2 if they need to come back later — the
 * detail page exposes a sidebar link to /interlocutor for finishing the
 * pair up.
 */

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';

import { Toast } from '@/components/ui/alert';
import { Stepper } from '@/components/ui/stepper';
import { RolePlayCardEditor, type RolePlayCardEditorValue } from '@/components/domain/speaking/RolePlayCardEditor';
import { InterlocutorScriptEditor } from '@/components/domain/speaking/InterlocutorScriptEditor';
import {
  adminCreateRolePlayCard,
  adminUpsertInterlocutorScript,
  adminListSpeakingCardTypes,
  type RolePlayCardDetail,
  type UpsertInterlocutorScriptInput,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
  { label: 'New' },
];

export default function NewSpeakingRolePlayCardPage() {
  const router = useRouter();
  const [step, setStep] = useState<1 | 2>(1);
  const [savedCard, setSavedCard] = useState<RolePlayCardDetail | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [cardTypes, setCardTypes] = useState<SpeakingCardTypeDetail[]>([]);

  useEffect(() => {
    adminListSpeakingCardTypes(true)
      .then(setCardTypes)
      .catch(() => setCardTypes([]));
  }, []);

  async function handleCardSubmit(value: RolePlayCardEditorValue) {
    setSubmitting(true);
    try {
      const created = await adminCreateRolePlayCard(value);
      setSavedCard(created);
      setToast({ variant: 'success', message: `Draft "${created.scenarioTitle}" saved.` });
      setStep(2);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleInterlocutorSubmit(cardId: string, value: UpsertInterlocutorScriptInput) {
    setSubmitting(true);
    try {
      await adminUpsertInterlocutorScript(cardId, value);
      setToast({ variant: 'success', message: 'Interlocutor script saved.' });
      router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}`);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AdminCatalogLayout
      title="New role-play card"
      description="Two steps: write the candidate card, then the hidden interlocutor script. You can return to step 2 later if needed."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      backHref="/admin/content/speaking/role-play-cards"
      hideViewModeToggle
      actions={
        <Button variant="outline" onClick={() => router.push('/admin/content/speaking/role-play-cards')}>
          <ArrowLeft className="mr-1 h-4 w-4" /> Back to list
        </Button>
      }
    >
      <div className="col-span-full">
        <Stepper
          steps={[
            { id: 'candidate', label: 'Candidate card' },
            { id: 'interlocutor', label: 'Interlocutor script' },
          ]}
          currentStep={step - 1}
        />
      </div>

      {step === 1 ? (
        <Card className="col-span-full">
          <CardContent className="p-6">
            <RolePlayCardEditor
              mode="create"
              submitting={submitting}
              onSubmit={handleCardSubmit}
              cardTypes={cardTypes}
            />
          </CardContent>
        </Card>
      ) : savedCard ? (
        <Card className="col-span-full">
          <CardContent className="p-6">
            <InterlocutorScriptEditor
              cardId={savedCard.cardId}
              value={null}
              mode="create"
              submitting={submitting}
              onSubmit={handleInterlocutorSubmit}
              secondaryAction={
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() =>
                    router.push(
                      `/admin/content/speaking/role-play-cards/${encodeURIComponent(savedCard.cardId)}`,
                    )
                  }
                >
                  Skip for now <ArrowRight className="ml-1 h-4 w-4" />
                </Button>
              }
            />
          </CardContent>
        </Card>
      ) : null}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}
