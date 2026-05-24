'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  createStudyPlanTemplate,
  emptyTemplateBody,
} from '@/lib/study-plan-admin-api';

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

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <div className="mb-6">
        <Link href="/admin/study-plan-templates" className="text-blue-600 hover:underline text-sm">
          ← Back to templates
        </Link>
        <h1 className="text-2xl font-bold mt-2">New Study Plan Template</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Create a skeleton plan. After this step you can edit week structure, day slots, and
          checkpoints in the full editor.
        </p>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 text-red-800 rounded p-3 mb-4">{error}</div>
      )}

      <div className="space-y-4">
        <div>
          <label className="block text-sm font-medium mb-1">Name *</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. 8-Week Standard for B-Band"
            className="w-full border rounded px-3 py-2"
          />
        </div>

        <div>
          <label className="block text-sm font-medium mb-1">Slug * (lowercase, hyphenated)</label>
          <input
            value={slug}
            onChange={(e) => setSlug(e.target.value.toLowerCase().replace(/\s+/g, '-'))}
            placeholder="8-week-standard"
            className="w-full border rounded px-3 py-2 font-mono text-sm"
          />
        </div>

        <div>
          <label className="block text-sm font-medium mb-1">Description</label>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            className="w-full border rounded px-3 py-2"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium mb-1">Min Weeks</label>
            <input
              type="number"
              min={1}
              value={minWeeks}
              onChange={(e) => setMinWeeks(parseInt(e.target.value) || 1)}
              className="w-full border rounded px-3 py-2"
            />
          </div>
          <div>
            <label className="block text-sm font-medium mb-1">Max Weeks</label>
            <input
              type="number"
              min={1}
              value={maxWeeks}
              onChange={(e) => setMaxWeeks(parseInt(e.target.value) || 1)}
              className="w-full border rounded px-3 py-2"
            />
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium mb-1">Target Band (optional)</label>
            <select
              value={targetBand}
              onChange={(e) => setTargetBand(e.target.value)}
              className="w-full border rounded px-3 py-2"
            >
              <option value="">Any</option>
              <option value="A">A</option>
              <option value="B">B</option>
              <option value="C+">C+</option>
              <option value="C">C</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium mb-1">Profession (optional)</label>
            <input
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
              placeholder="e.g. nurse, doctor"
              className="w-full border rounded px-3 py-2"
            />
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium mb-1">Default Minutes per Day</label>
          <input
            type="number"
            min={5}
            max={480}
            value={defaultMinutesPerDay}
            onChange={(e) => setDefaultMinutesPerDay(parseInt(e.target.value) || 60)}
            className="w-32 border rounded px-3 py-2"
          />
        </div>

        <div>
          <label className="block text-sm font-medium mb-2">Available to Tiers</label>
          <div className="flex gap-4">
            {['free', 'premium', 'elite'].map((t) => (
              <label key={t} className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={tierCodes.includes(t)}
                  onChange={() => toggleTier(t)}
                />
                <span className="capitalize">{t}</span>
              </label>
            ))}
          </div>
        </div>

        <div className="flex gap-3 pt-4 border-t">
          <button
            onClick={submit}
            disabled={submitting}
            className="px-6 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
          >
            {submitting ? 'Creating...' : 'Create & Edit Structure'}
          </button>
          <Link
            href="/admin/study-plan-templates"
            className="px-6 py-2 border rounded hover:bg-muted"
          >
            Cancel
          </Link>
        </div>
      </div>
    </div>
  );
}
