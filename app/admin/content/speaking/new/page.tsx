'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Mic2, Plus } from 'lucide-react';
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
import { adminCreateSpeakingPaper } from '@/lib/api';

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

export default function NewSpeakingPaperPage() {
  const router = useRouter();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [title, setTitle] = useState('');
  const [profession, setProfession] = useState('all');
  const [difficulty, setDifficulty] = useState('standard');
  const [examCode, setExamCode] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canWrite || !title.trim()) return;
    setSubmitting(true);
    try {
      const created = await adminCreateSpeakingPaper({
        title: title.trim(),
        professionId: profession === 'all' ? null : profession,
        appliesToAllProfessions: profession === 'all',
        difficulty,
        examCode: examCode.trim() || null,
      });
      setToast({ variant: 'success', message: 'Speaking paper created.' });
      router.push(`/admin/content/speaking/${created.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create failed.' });
      setSubmitting(false);
    }
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="New speaking paper">
      <AdminRouteHero
        eyebrow="New"
        icon={Mic2}
        accent="navy"
        title="Create speaking paper"
        description="Add a new speaking scenario. Structure, role card and warm-up questions can be filled in after the paper is created."
        aside={(
          <Button variant="ghost" asChild>
            <Link href="/admin/content/speaking">
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
                placeholder="e.g. Discharge planning for post-op patient"
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
                value={examCode}
                onChange={(e) => setExamCode(e.target.value)}
                label="Exam code (optional)"
                placeholder="e.g. SPK-2024-09"
              />
            </div>
            <div className="sm:col-span-2 flex justify-end gap-2">
              <Button variant="ghost" type="button" asChild>
                <Link href="/admin/content/speaking">Cancel</Link>
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
