'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, BookOpen, Plus } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { createContentPaper } from '@/lib/content-upload-api';
import { adminReadingEnsureCanonical } from '@/lib/api';

const PROFESSION_OPTIONS = [
  { value: 'all', label: 'All professions' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'optometry', label: 'Optometry' },
];

const DIFFICULTY_OPTIONS = [
  { value: 'easy', label: 'Easy' },
  { value: 'standard', label: 'Standard' },
  { value: 'hard', label: 'Hard' },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function NewReadingPaperPage() {
  const router = useRouter();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [title, setTitle] = useState('');
  const [profession, setProfession] = useState('all');
  const [difficulty, setDifficulty] = useState('standard');
  const [tags, setTags] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canWrite || !title.trim()) return;
    setSubmitting(true);
    try {
      const created = await createContentPaper({
        subtestCode: 'reading',
        title: title.trim(),
        professionId: profession === 'all' ? null : profession,
        appliesToAllProfessions: profession === 'all',
        difficulty,
        estimatedDurationMinutes: 60,
        priority: 0,
        tagsCsv: tags.trim() || null,
        sourceProvenance: 'admin-ui',
      });
      // Best-effort: pre-seed canonical Part A / B / C so the workspace
      // opens with the three parts already present. Non-fatal if it fails.
      try {
        await adminReadingEnsureCanonical(created.id);
      } catch {
        // Workspace will offer a manual "Ensure canonical" button as a fallback.
      }
      setToast({ variant: 'success', message: 'Reading paper created.' });
      router.push(`/admin/content/reading/${created.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create failed.' });
      setSubmitting(false);
    }
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="New reading paper">
      <AdminRouteHero
        eyebrow="New"
        icon={BookOpen}
        accent="navy"
        title="Create reading paper"
        description="Add a new reading paper. Canonical Part A (20 items), Part B (6 items) and Part C (16 items) will be pre-seeded; you can fill in texts and questions in the workspace."
        aside={(
          <Button variant="ghost" asChild>
            <Link href="/admin/content/reading">
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to list
            </Link>
          </Button>
        )}
      />

      <AdminRoutePanel>
        <Card className="p-6">
          <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <Input
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                label="Title"
                placeholder="e.g. Reading practice set 14 — cardiology"
                required
              />
            </div>
            <Select
              value={profession}
              onChange={(e) => setProfession(e.target.value)}
              label="Profession"
              options={PROFESSION_OPTIONS}
            />
            <Select
              value={difficulty}
              onChange={(e) => setDifficulty(e.target.value)}
              label="Difficulty"
              options={DIFFICULTY_OPTIONS}
            />
            <div className="sm:col-span-2">
              <Input
                value={tags}
                onChange={(e) => setTags(e.target.value)}
                label="Tags (optional, comma-separated)"
                placeholder="cardiology, infection-control"
              />
            </div>
            <div className="sm:col-span-2 flex justify-end gap-2">
              <Button variant="ghost" type="button" asChild>
                <Link href="/admin/content/reading">Cancel</Link>
              </Button>
              <Button
                type="submit"
                disabled={!canWrite || !title.trim() || submitting}
                className="inline-flex items-center gap-2"
              >
                <Plus className="h-4 w-4" /> {submitting ? 'Creating…' : 'Create paper'}
              </Button>
            </div>
          </form>
        </Card>
      </AdminRoutePanel>

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
