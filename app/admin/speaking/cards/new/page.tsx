'use client';

/**
 * Entry point for authoring a new role-play card. Creates a blank Draft (so
 * every wizard step is a partial PATCH against a real id — mirroring the
 * mock-bundle wizard) and routes into the wizard. The create is an explicit
 * button click to avoid orphaned/duplicate drafts on refresh.
 */

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight, Loader2, Mic } from 'lucide-react';
import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { professions } from '@/lib/auth/enrollment';
import { adminCreateRolePlayCard } from '@/lib/api/speaking-role-play-cards';
import { buildCardDraftSeed, buildCardStepHref } from '@/components/domain/speaking/wizard/card-wizard-config';

export default function NewSpeakingCardPage() {
  const router = useRouter();
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Deliberately starts empty: the operator must pick a profession before a
  // draft row can exist, so a card can never be silently filed under a default.
  const [professionId, setProfessionId] = useState('');

  async function handleStart() {
    if (!professionId) {
      setError('Choose the profession this card belongs to first.');
      return;
    }
    setCreating(true);
    setError(null);
    try {
      const created = await adminCreateRolePlayCard(buildCardDraftSeed(professionId));
      router.replace(buildCardStepHref(created.cardId, 'classification'));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create a draft card.');
      setCreating(false);
    }
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        eyebrow="Speaking"
        title="New role-play card"
        description="Author a complete two-card role-play in a guided, step-by-step flow: classify, write the candidate card, add tasks, the hidden interlocutor script, scoring & timing, then review and publish."
        icon={Mic}
        accent="primary"
      />
      <AdminRoutePanel>
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        <div className="max-w-sm py-2">
          <label htmlFor="card-profession" className="mb-1 block text-sm font-medium">
            Profession <span aria-hidden="true">*</span>
          </label>
          <select
            id="card-profession"
            required
            className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
            value={professionId}
            onChange={(e) => setProfessionId(e.target.value)}
            disabled={creating}
          >
            <option value="">Select a profession…</option>
            {professions.map((p) => (
              <option key={p.id} value={p.id}>{p.label}</option>
            ))}
          </select>
          <p className="mt-1 text-xs text-muted-foreground">
            Set once, up front — every learner-facing filter keys off it.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3 py-2">
          <Button variant="primary" onClick={() => void handleStart()} disabled={creating || !professionId}>
            {creating ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
            Create draft &amp; open wizard <ArrowRight className="ml-1 h-4 w-4" />
          </Button>
          <Button variant="ghost" onClick={() => router.push('/admin/speaking')} disabled={creating}>
            <ArrowLeft className="mr-1 h-4 w-4" /> Back to Speaking hub
          </Button>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
