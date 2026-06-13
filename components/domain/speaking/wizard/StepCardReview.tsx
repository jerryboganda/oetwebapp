'use client';

/**
 * Card wizard — step 6: review & publish.
 * Read-only recap, a readiness checklist (hard rule: interlocutor script;
 * soft rules: tasks/criteria/fields), and the publish action.
 */

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Check, X, AlertTriangle, ExternalLink } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { adminPublishRolePlayCard, type RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';
import { getCardReadiness } from './use-card-readiness';

export function StepCardReview() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const router = useRouter();
  const card = wizard.entity;
  const readiness = getCardReadiness(card);

  const [publishing, setPublishing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const status = (card.status ?? '').toLowerCase();
  const isPublished = status === 'published';
  const isArchived = status === 'archived';

  async function handlePublish() {
    setPublishing(true);
    setError(null);
    try {
      await adminPublishRolePlayCard(card.cardId);
      await wizard.refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not publish this card.');
    } finally {
      setPublishing(false);
    }
  }

  const summary: Array<{ label: string; value: string }> = [
    { label: 'Scenario', value: card.scenarioTitle || '—' },
    { label: 'Profession', value: card.professionId || '—' },
    { label: 'Difficulty', value: card.difficulty || '—' },
    { label: 'Setting', value: card.setting || '—' },
    { label: 'Tasks', value: String((card.tasks ?? []).filter((t) => t.trim()).length) },
    { label: 'Criteria', value: String((card.criteriaFocus ?? []).length) },
  ];

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="space-y-1">
          <h2 className="text-lg font-bold text-navy">Review &amp; publish</h2>
          <p className="text-sm text-muted">Confirm the card is complete, then publish it for learners.</p>
        </div>
        <Badge variant={isPublished ? 'success' : 'muted'}>{card.status}</Badge>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-2 rounded-2xl border border-border bg-background-light p-4 sm:grid-cols-2 lg:grid-cols-3">
        {summary.map((s) => (
          <div key={s.label}>
            <p className="text-[10px] font-bold uppercase tracking-widest text-muted">{s.label}</p>
            <p className="truncate text-sm text-navy">{s.value}</p>
          </div>
        ))}
      </div>

      <div className="space-y-2 rounded-2xl border border-border bg-surface p-4">
        <p className="text-sm font-bold text-navy">Publish readiness</p>
        <ul className="space-y-1.5">
          {readiness.items.map((item) => (
            <li key={item.label} className="flex items-center gap-2 text-sm">
              {item.ok ? (
                <Check className="h-4 w-4 text-emerald-600" />
              ) : item.hard ? (
                <X className="h-4 w-4 text-red-600" />
              ) : (
                <AlertTriangle className="h-4 w-4 text-amber-600" />
              )}
              <span className={item.ok ? 'text-navy' : item.hard ? 'text-red-700' : 'text-amber-700'}>
                {item.label}
                {!item.ok && !item.hard ? ' (recommended)' : ''}
              </span>
            </li>
          ))}
        </ul>
      </div>

      {isPublished ? (
        <InlineAlert variant="success">
          This card is published and visible to learners.
        </InlineAlert>
      ) : isArchived ? (
        <InlineAlert variant="warning">This card is archived and read-only.</InlineAlert>
      ) : (
        <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
          <Button
            variant="primary"
            onClick={() => void handlePublish()}
            loading={publishing}
            disabled={publishing || !wizard.canPublish || !readiness.hardReady}
            title={readiness.hardReady ? 'Publish card' : 'Add an interlocutor script before publishing'}
          >
            Publish card
          </Button>
          {!wizard.canPublish ? (
            <span className="text-xs text-muted">You do not have publish permission.</span>
          ) : !readiness.hardReady ? (
            <span className="text-xs text-amber-700">Add the interlocutor script (step 4) to enable publishing.</span>
          ) : null}
        </div>
      )}

      <div>
        <Button variant="ghost" size="sm" onClick={() => router.push('/admin/speaking')}>
          <ExternalLink className="mr-1 h-4 w-4" /> Back to Speaking hub
        </Button>
      </div>
    </div>
  );
}
