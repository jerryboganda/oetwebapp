'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Save } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';
import { ensureCanonicalParts } from '@/lib/reading-authoring-api';

const professionOptions = [
  { value: 'all', label: 'All Professions' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'veterinary', label: 'Veterinary' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'occupational-therapy', label: 'Occupational Therapy' },
  { value: 'speech-pathology', label: 'Speech Pathology' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'dietetics', label: 'Dietetics' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'general', label: 'General' },
];

const difficultyOptions = [
  { value: 'easy', label: 'Easy' },
  { value: 'standard', label: 'Medium' },
  { value: 'hard', label: 'Hard' },
];

export default function AdminCreateReadingPaperPage() {
  const router = useRouter();

  const [title, setTitle] = useState('');
  const [profession, setProfession] = useState('all');
  const [difficulty, setDifficulty] = useState('standard');
  const [sourceProvenance, setSourceProvenance] = useState('');
  const [estimatedDuration, setEstimatedDuration] = useState(60);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<{ message: string; variant: 'error' | 'success' } | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!title.trim()) {
      setToast({ message: 'Title is required.', variant: 'error' });
      return;
    }
    if (!sourceProvenance.trim()) {
      setToast({ message: 'Source Provenance is required.', variant: 'error' });
      return;
    }

    setSaving(true);
    setToast(null);

    try {
      const result = await apiClient.post<{ id: string }>('/v1/admin/papers', {
        title: title.trim(),
        professionId: profession === 'all' ? null : profession,
        appliesToAllProfessions: profession === 'all',
        subtestCode: 'reading',
        difficulty,
        sourceProvenance: sourceProvenance.trim(),
        estimatedDurationMinutes: estimatedDuration,
      });

      await ensureCanonicalParts(result.id);
      router.push(`/admin/content/reading/${result.id}/texts`);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to create reading paper.';
      setToast({ message, variant: 'error' });
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <AdminSettingsLayout
        title="Create New Reading Paper"
        description="Set up a new Reading subtest paper. After creating, you'll add passage texts and author questions."
        eyebrow="CMS"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'New' },
        ]}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/content/reading">Cancel</Link>
          </Button>
        }
      >
        <form onSubmit={handleSubmit}>
          <SettingsSection
            title="Paper details"
            description="Required metadata. Source provenance is recorded for audit traceability."
            actions={
              <Button type="submit" variant="primary" size="sm" disabled={saving} startIcon={<Save className="h-4 w-4" />}>
                {saving ? 'Creating…' : 'Create Paper'}
              </Button>
            }
          >
            <div className="space-y-6">
              <Input
                label="Title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="e.g. OET Reading Practice Test, Nursing 2024"
                required
              />

              <div className="grid gap-4 sm:grid-cols-2">
                <Select
                  label="Profession"
                  value={profession}
                  onChange={(e) => setProfession(e.target.value)}
                  options={professionOptions}
                />

                <Select
                  label="Difficulty"
                  value={difficulty}
                  onChange={(e) => setDifficulty(e.target.value)}
                  options={difficultyOptions}
                />
              </div>

              <Textarea
                label="Source Provenance"
                value={sourceProvenance}
                onChange={(e) => setSourceProvenance(e.target.value)}
                placeholder="e.g. OET Official Practice Test 2024, Cambridge Box Hill"
                hint="Describe where this content originates for audit traceability."
                required
              />

              <Input
                label="Estimated Duration (minutes)"
                type="number"
                min={1}
                max={180}
                value={estimatedDuration}
                onChange={(e) => setEstimatedDuration(Number(e.target.value))}
              />
            </div>
          </SettingsSection>
        </form>
      </AdminSettingsLayout>

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </>
  );
}
