'use client';

/** Mock-set wizard — step 4: review & publish. */

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Check, X, ExternalLink } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { publishAdminSpeakingMockSet, type AdminSpeakingMockSetRow } from '@/lib/api';

export function StepMockReview() {
  const wizard = useAdminWizard<AdminSpeakingMockSetRow>();
  const router = useRouter();
  const row = wizard.entity;

  const [publishing, setPublishing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isPublished = row.status === 'published';
  const isArchived = row.status === 'archived';

  const checks = [
    { label: 'Title set', ok: Boolean(row.title.trim()) },
    { label: 'Role-play 1 selected & speaking', ok: Boolean(row.rolePlay1?.contentId) && row.rolePlay1.isSpeaking },
    { label: 'Role-play 2 selected & speaking', ok: Boolean(row.rolePlay2?.contentId) && row.rolePlay2.isSpeaking },
    { label: 'Role-plays are distinct', ok: Boolean(row.rolePlay1?.contentId) && row.rolePlay1.contentId !== row.rolePlay2.contentId },
  ];
  const ready = checks.every((c) => c.ok);

  async function handlePublish() {
    setPublishing(true);
    setError(null);
    try {
      await publishAdminSpeakingMockSet(row.mockSetId);
      await wizard.refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not publish this mock set.');
    } finally {
      setPublishing(false);
    }
  }

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="space-y-1">
          <h2 className="text-lg font-bold text-navy">Review &amp; publish</h2>
          <p className="text-sm text-muted">Confirm the mock set, then publish it for learners.</p>
        </div>
        <Badge variant={isPublished ? 'success' : 'muted'}>{row.status}</Badge>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-2 rounded-2xl border border-border bg-background-light p-4 sm:grid-cols-2">
        <div><p className="text-[10px] font-bold uppercase tracking-widest text-muted">Title</p><p className="truncate text-sm text-navy">{row.title || '—'}</p></div>
        <div><p className="text-[10px] font-bold uppercase tracking-widest text-muted">Profession / difficulty</p><p className="truncate text-sm text-navy">{row.professionId} · {row.difficulty}</p></div>
        <div><p className="text-[10px] font-bold uppercase tracking-widest text-muted">Role-play 1</p><p className="truncate text-sm text-navy">{row.rolePlay1?.title || '—'}</p></div>
        <div><p className="text-[10px] font-bold uppercase tracking-widest text-muted">Role-play 2</p><p className="truncate text-sm text-navy">{row.rolePlay2?.title || '—'}</p></div>
      </div>

      <div className="space-y-2 rounded-2xl border border-border bg-surface p-4">
        <p className="text-sm font-bold text-navy">Publish readiness</p>
        <ul className="space-y-1.5">
          {checks.map((c) => (
            <li key={c.label} className="flex items-center gap-2 text-sm">
              {c.ok ? <Check className="h-4 w-4 text-emerald-600" /> : <X className="h-4 w-4 text-red-600" />}
              <span className={c.ok ? 'text-navy' : 'text-red-700'}>{c.label}</span>
            </li>
          ))}
        </ul>
      </div>

      {isPublished ? (
        <InlineAlert variant="success">This mock set is published and visible to learners.</InlineAlert>
      ) : isArchived ? (
        <InlineAlert variant="warning">This mock set is archived and read-only.</InlineAlert>
      ) : (
        <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
          <Button variant="primary" onClick={() => void handlePublish()} loading={publishing} disabled={publishing || !wizard.canPublish || !ready}>
            Publish mock set
          </Button>
          {!wizard.canPublish ? <span className="text-xs text-muted">You do not have publish permission.</span> : null}
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
