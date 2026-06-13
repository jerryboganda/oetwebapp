'use client';

/**
 * Card wizard — step 4: hidden interlocutor script (optional / deferrable).
 *
 * Reuses the existing `InterlocutorScriptEditor` wholesale. The editor keeps
 * its own Save button (which acts as "save & continue" here); the wizard's
 * Next is always enabled because this step is marked `optional`.
 */

import { useCallback, useEffect, useState } from 'react';
import { ArrowRight, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { InterlocutorScriptEditor } from '@/components/domain/speaking/InterlocutorScriptEditor';
import {
  adminGetInterlocutorScript,
  adminUpsertInterlocutorScript,
  type InterlocutorScriptDetail,
  type RolePlayCardDetail,
  type UpsertInterlocutorScriptInput,
} from '@/lib/api/speaking-role-play-cards';

export function StepInterlocutor() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;

  const [script, setScript] = useState<InterlocutorScriptDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    adminGetInterlocutorScript(card.cardId)
      .then((s) => {
        if (active) setScript(s);
      })
      .catch(() => {
        if (active) setScript(null);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [card.cardId]);

  async function handleSubmit(cardId: string, value: UpsertInterlocutorScriptInput) {
    setSubmitting(true);
    setError(null);
    try {
      await adminUpsertInterlocutorScript(cardId, value);
      await wizard.refresh();
      wizard.goToStep('scoring');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the interlocutor script.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Hidden interlocutor script</h2>
        <p className="text-sm text-muted">
          The AI patient persona and tutor cues. A card cannot be published without this — but you can
          come back to it later (use Next to skip for now).
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {loading ? (
        <p className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading script…
        </p>
      ) : (
        <InterlocutorScriptEditor
          cardId={card.cardId}
          value={script}
          mode={script ? 'edit' : 'create'}
          submitting={submitting}
          onSubmit={handleSubmit}
          secondaryAction={
            <Button type="button" variant="ghost" onClick={() => wizard.goToStep('scoring')} disabled={submitting}>
              Skip for now <ArrowRight className="ml-1 h-4 w-4" />
            </Button>
          }
        />
      )}
    </div>
  );
}
