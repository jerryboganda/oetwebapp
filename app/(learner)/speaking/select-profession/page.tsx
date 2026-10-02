'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Check, Lock, Stethoscope } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
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
      <>
        <LearnerPageHero
          eyebrow="Speaking"
          icon={Lock}
          accent="speaking"
          title="Your profession is locked"
          description="Your access was granted for a specific profession — the courses, videos and materials you can open are tied to it, so it can no longer be changed from here once a package is on your account."
        />

        <Card padding="md" className="space-y-3">
          <p className="text-sm text-navy">
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
      </>
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Stethoscope}
        accent="speaking"
        title="Choose your healthcare profession"
        description="OET Speaking is profession-specific. Pick the profession you will sit the exam in so we can show role-play scenarios that match your real workplace."
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {!professionsLoading && options.length === 0 ? (
        <EmptyState icon={<Stethoscope className="h-8 w-8" />} title="No professions available." description="Please contact support." />
      ) : (
        <Card padding="md">
          {professionsLoading ? (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3" role="status" aria-busy="true" aria-label="Loading professions…">
              {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-11 rounded-control" />)}
            </div>
          ) : (
            <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {options.map((option) => {
                const isActive = selected === option.value;
                return (
                  <li key={option.value}>
                    <button
                      type="button"
                      aria-pressed={isActive}
                      onClick={() => setSelected(option.value)}
                      className={`flex min-h-11 w-full items-center justify-between gap-2 rounded-control border px-3 py-2 text-start text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                        isActive
                          ? 'border-primary bg-primary/10 font-semibold text-primary'
                          : 'border-border text-navy hover:border-primary/40 hover:bg-background-light'
                      }`}
                    >
                      {option.label}
                      {isActive ? <Check className="h-4 w-4 shrink-0" aria-hidden="true" /> : null}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </Card>
      )}

      <div className="flex flex-wrap items-center justify-end gap-2">
        <Button variant="outline" onClick={() => router.push('/dashboard')} disabled={saving}>
          Cancel
        </Button>
        <Button onClick={handleConfirm} disabled={!selected || saving}>
          {saving ? 'Saving…' : 'Save and continue'}
        </Button>
      </div>
    </>
  );
}
