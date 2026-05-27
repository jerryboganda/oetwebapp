'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { useProfessions } from '@/lib/hooks/use-professions';
import { setActiveProfession } from '@/lib/api';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

export default function SelectSpeakingProfessionPage() {
  const router = useRouter();
  const { user, refreshSession, loading: authLoading } = useAuth();
  const { options, isLoading: professionsLoading } = useProfessions();
  const [selected, setSelected] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && user?.activeProfessionId) {
      router.replace('/speaking');
    }
  }, [authLoading, user?.activeProfessionId, router]);

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
      const message = err instanceof Error ? err.message : 'Could not save your profession. Please try again.';
      setError(message);
      setSaving(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-10">
        <header className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight text-foreground">Choose your healthcare profession</h1>
          <p className="text-sm text-muted-foreground">
            OET Speaking is profession-specific. Pick the profession you will sit the exam in so we can show role-play
            scenarios that match your real workplace.
          </p>
        </header>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card className="space-y-3 p-4">
          {professionsLoading ? (
            <p className="text-sm text-muted-foreground">Loading professions…</p>
          ) : options.length === 0 ? (
            <p className="text-sm text-muted-foreground">No professions available. Please contact support.</p>
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
