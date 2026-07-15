'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { useProfessions } from '@/lib/hooks/use-professions';
import { ApiError, setActiveProfession } from '@/lib/api';
import { fetchSupportWhatsApp, normalizeWhatsAppNumber, PLATFORM_WHATSAPP } from '@/lib/billing/whatsapp';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

export default function SelectSpeakingProfessionPage() {
  const router = useRouter();
  const { user, refreshSession, loading: authLoading } = useAuth();
  const { options, isLoading: professionsLoading } = useProfessions();
  const [selected, setSelected] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [locked, setLocked] = useState(false);
  const [supportNumber, setSupportNumber] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && user?.activeProfessionId) {
      router.replace('/speaking');
    }
  }, [authLoading, user?.activeProfessionId, router]);

  // Only fetched once the backend has actually locked the learner out — the
  // number is a public support channel, but there is no reason to call for it
  // on the happy path.
  useEffect(() => {
    if (!locked) return;
    let cancelled = false;
    void fetchSupportWhatsApp().then((settings) => {
      if (!cancelled) setSupportNumber(settings.whatsAppNumber);
    });
    return () => {
      cancelled = true;
    };
  }, [locked]);

  async function handleConfirm() {
    if (!selected) return;
    setSaving(true);
    setError(null);
    try {
      await setActiveProfession(selected);
      await refreshSession();
      trackSpeaking('profession_set', { professionId: selected });
      router.replace('/speaking');
    } catch (err) {
      // The backend locks profession to whatever the learner owned access
      // against once anything is purchased or admin-granted, so retrying can
      // never succeed — swap the picker for the support route instead.
      if (err instanceof ApiError && err.code === 'profession_locked') {
        setLocked(true);
        setSaving(false);
        return;
      }
      const message = err instanceof Error ? err.message : 'Could not save your profession. Please try again.';
      setError(message);
      setSaving(false);
    }
  }

  // Built from the fallback number until the settings read resolves, so the CTA
  // is never a dead link on first paint.
  const professionChangeHref = `https://wa.me/${normalizeWhatsAppNumber(supportNumber) ?? PLATFORM_WHATSAPP}?text=${encodeURIComponent(
    [
      'Hello OET team, I need to change the profession on my account.',
      '',
      `Registered email: ${user?.email ?? ''}`,
      `Requested profession: ${options.find((option) => option.value === selected)?.label ?? '(please advise)'}`,
      '',
      'Please move my access to this profession.',
    ].join('\n'),
  )}`;

  if (locked) {
    return (
      <LearnerDashboardShell>
        <div className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-10">
          <header className="space-y-2">
            <h1 className="text-2xl font-semibold tracking-tight text-foreground">Your profession is locked</h1>
            <p className="text-sm text-muted">
              Your access was granted for a specific profession — the courses, videos and materials you can open are
              tied to it, so it can no longer be changed from here once a package is on your account.
            </p>
          </header>

          <Card className="space-y-3 p-4">
            <p className="text-sm text-foreground">
              Our team can move you to a different profession and re-point your access. Message us with the profession
              you need and we will sort it out.
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <Button asChild>
                <a href={professionChangeHref} target="_blank" rel="noopener noreferrer">
                  Request a change on WhatsApp
                </a>
              </Button>
              <Button variant="outline" onClick={() => router.push('/dashboard')}>
                Back to dashboard
              </Button>
            </div>
          </Card>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-10">
        <header className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight text-foreground">Choose your healthcare profession</h1>
          <p className="text-sm text-muted">
            OET Speaking is profession-specific. Pick the profession you will sit the exam in so we can show role-play
            scenarios that match your real workplace.
          </p>
        </header>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card className="space-y-3 p-4">
          {professionsLoading ? (
            <p className="text-sm text-muted">Loading professions…</p>
          ) : options.length === 0 ? (
            <p className="text-sm text-muted">No professions available. Please contact support.</p>
          ) : (
            <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {options.map((option) => {
                const isActive = selected === option.value;
                return (
                  <li key={option.value}>
                    <button
                      type="button"
                      onClick={() => setSelected(option.value)}
                      className={`w-full rounded-lg border px-3 py-2 text-left text-sm transition ${
                        isActive
                          ? 'border-primary bg-primary/10 text-primary'
                          : 'border-border hover:border-primary/40 hover:bg-muted/40'
                      }`}
                    >
                      {option.label}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </Card>

        <div className="flex items-center justify-end gap-2">
          <Button variant="outline" onClick={() => router.push('/dashboard')} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={handleConfirm} disabled={!selected || saving}>
            {saving ? 'Saving…' : 'Save and continue'}
          </Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
