'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { FilePlus2 } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import {
  createStudyPlanTemplate,
  emptyTemplateBody,
} from '@/lib/study-plan-admin-api';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Study plan templates', href: '/admin/study-plan-templates' },
  { label: 'New' },
];

export default function NewStudyPlanTemplatePage() {
  const router = useRouter();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [minWeeks, setMinWeeks] = useState(4);
  const [maxWeeks, setMaxWeeks] = useState(12);
  const [targetBand, setTargetBand] = useState('');
  const [professionId, setProfessionId] = useState('');
  const [defaultMinutesPerDay, setDefaultMinutesPerDay] = useState(60);
  const [tierCodes, setTierCodes] = useState<string[]>(['free']);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const toggleTier = (t: string) => {
    setTierCodes((prev) => (prev.includes(t) ? prev.filter((x) => x !== t) : [...prev, t]));
  };

  const submit = async () => {
    setError(null);
    if (!name || !slug) {
      setError('Name and slug are required.');
      return;
    }
    setSubmitting(true);
    try {
      const created = await createStudyPlanTemplate({
        name,
        slug,
        description: description || null,
        examTypeCode: 'OET',
        minWeeks,
        maxWeeks,
        targetBand: targetBand || null,
        professionId: professionId || null,
        focusTags: [],
        defaultMinutesPerDay,
        isActive: false,
        tierCodes,
        body: emptyTemplateBody(),
      });
      router.push(`/admin/study-plan-templates/${created.id}`);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Create failed.');
      setSubmitting(false);
    }
  };

  const inputCls = 'mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]';
  const labelCls = 'block text-sm font-medium text-admin-fg-strong';

  return (
    <AdminSettingsLayout
      title="New Study Plan Template"
      description="Create a skeleton plan. After this step you can edit week structure, day slots, and checkpoints in the full editor."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Study planning"
      icon={<FilePlus2 className="h-5 w-5" />}
      backHref="/admin/study-plan-templates"
    >
      {error && (
        <Card surface="tinted-danger">
          <CardContent className="p-3 text-sm text-admin-danger">{error}</CardContent>
        </Card>
      )}

      <SettingsSection title="Identity">
        <div className="space-y-4">
          <div>
            <label className={labelCls}>Name *</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. 8-Week Standard for B-Band"
              className={inputCls}
            />
          </div>

          <div>
            <label className={labelCls}>Slug * (lowercase, hyphenated)</label>
            <input
              value={slug}
              onChange={(e) => setSlug(e.target.value.toLowerCase().replace(/\s+/g, '-'))}
              placeholder="8-week-standard"
              className={`${inputCls} font-mono`}
            />
          </div>

          <div>
            <label className={labelCls}>Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className={inputCls}
            />
          </div>
        </div>
      </SettingsSection>

      <SettingsSection title="Plan shape">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className={labelCls}>Min Weeks</label>
            <input
              type="number"
              min={1}
              value={minWeeks}
              onChange={(e) => setMinWeeks(parseInt(e.target.value) || 1)}
              className={inputCls}
            />
          </div>
          <div>
            <label className={labelCls}>Max Weeks</label>
            <input
              type="number"
              min={1}
              value={maxWeeks}
              onChange={(e) => setMaxWeeks(parseInt(e.target.value) || 1)}
              className={inputCls}
            />
          </div>
          <div>
            <label className={labelCls}>Target Band (optional)</label>
            <select
              value={targetBand}
              onChange={(e) => setTargetBand(e.target.value)}
              className={inputCls}
            >
              <option value="">Any</option>
              <option value="A">A</option>
              <option value="B">B</option>
              <option value="C+">C+</option>
              <option value="C">C</option>
            </select>
          </div>
          <div>
            <label className={labelCls}>Profession (optional)</label>
            <input
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
              placeholder="e.g. nurse, doctor"
              className={inputCls}
            />
          </div>
          <div>
            <label className={labelCls}>Default Minutes per Day</label>
            <input
              type="number"
              min={5}
              max={480}
              value={defaultMinutesPerDay}
              onChange={(e) => setDefaultMinutesPerDay(parseInt(e.target.value) || 60)}
              className={`${inputCls} w-32`}
            />
          </div>
        </div>
      </SettingsSection>

      <SettingsSection title="Available to tiers">
        <div className="flex gap-4">
          {['free', 'premium', 'elite'].map((t) => (
            <label key={t} className="flex items-center gap-2 text-sm text-admin-fg-strong">
              <input
                type="checkbox"
                checked={tierCodes.includes(t)}
                onChange={() => toggleTier(t)}
              />
              <span className="capitalize">{t}</span>
            </label>
          ))}
        </div>
      </SettingsSection>

      <SettingsSection title="Actions">
        <div className="flex gap-3">
          <Button onClick={submit} disabled={submitting} loading={submitting}>
            {submitting ? 'Creating...' : 'Create & Edit Structure'}
          </Button>
          <Button variant="outline" onClick={() => router.push('/admin/study-plan-templates')}>
            Cancel
          </Button>
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
