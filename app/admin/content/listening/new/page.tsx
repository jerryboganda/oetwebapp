'use client';

import { useCallback, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Headphones, ArrowLeft, Loader2, Plus } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { createContentPaper } from '@/lib/content-upload-api';

const PROFESSIONS = [
  { value: '', label: 'All professions' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'occupational-therapy', label: 'Occupational Therapy' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'veterinary', label: 'Veterinary' },
  { value: 'speech-pathology', label: 'Speech Pathology' },
  { value: 'dietetics', label: 'Dietetics' },
];

const DIFFICULTIES = [
  { value: 'easy', label: 'Easy' },
  { value: 'standard', label: 'Standard' },
  { value: 'hard', label: 'Hard' },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function NewListeningPaperPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [title, setTitle] = useState('');
  const [profession, setProfession] = useState('');
  const [difficulty, setDifficulty] = useState('standard');
  const [examCode, setExamCode] = useState('');
  const [aiSeed, setAiSeed] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const submit = useCallback(async () => {
    if (!canWrite) return;
    if (!title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    setSubmitting(true);
    try {
      const tagsCsv = [examCode.trim() ? `exam:${examCode.trim()}` : null, aiSeed.trim() ? `ai-seed:${aiSeed.trim().slice(0, 64)}` : null]
        .filter(Boolean)
        .join(',');
      const created = await createContentPaper({
        subtestCode: 'listening',
        title: title.trim(),
        professionId: profession || null,
        appliesToAllProfessions: !profession,
        difficulty,
        estimatedDurationMinutes: 45,
        priority: 0,
        tagsCsv: tagsCsv || null,
        sourceProvenance: 'admin-ui',
      });
      setToast({ variant: 'success', message: `Created ${created.slug}.` });
      router.push(`/admin/content/listening/${created.id}`);
    } catch (e) {
      setToast({ variant: 'error', message: `Create failed: ${(e as Error).message}` });
    } finally {
      setSubmitting(false);
    }
  }, [aiSeed, canWrite, difficulty, examCode, profession, router, title]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="New Listening Paper">
      <div className="mb-2">
        <Link
          href="/admin/content/listening"
          className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-3 w-3" /> Back to Listening papers
        </Link>
      </div>

      <AdminRouteSectionHeader
        icon={<Headphones className="w-6 h-6" />}
        title="Create Listening paper"
        description="Creates a Draft paper. After creation you'll be redirected to the workspace to author structure, extracts, and upload assets."
      />

      <AdminRoutePanel title="Paper details">
        <div className="grid gap-3 md:grid-cols-2">
          <Input
            label="Title *"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="e.g. Listening Practice — Cardiology Consult"
          />
          <Select
            label="Profession"
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            options={PROFESSIONS}
            hint="Leave blank to make this paper available to all professions."
          />
          <Select
            label="Difficulty"
            value={difficulty}
            onChange={(e) => setDifficulty(e.target.value)}
            options={DIFFICULTIES}
          />
          <Input
            label="Exam code"
            value={examCode}
            onChange={(e) => setExamCode(e.target.value)}
            placeholder="optional — internal tag"
          />
          <div className="md:col-span-2">
            <Textarea
              label="AI seed topic"
              value={aiSeed}
              onChange={(e) => setAiSeed(e.target.value)}
              placeholder="Optional. Stored as a tag and shown to the AI extraction prompt."
            />
          </div>
        </div>

        <div className="mt-4 flex justify-end">
          <Button variant="primary" onClick={() => void submit()} disabled={submitting || !canWrite}>
            {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
            Create draft & open workspace
          </Button>
        </div>
      </AdminRoutePanel>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
