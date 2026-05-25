'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { RefreshCw, BookOpen, Eye } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import {
  getStudyPlanTemplate,
  previewStudyPlanTemplate,
  type StudyPlanTemplateDetail,
  type StudyPlanTemplatePreview,
} from '@/lib/study-plan-admin-api';

const SUBTEST_COLORS: Record<string, string> = {
  reading: 'bg-blue-100 text-blue-800',
  listening: 'bg-purple-100 text-purple-800',
  writing: 'bg-green-100 text-green-800',
  speaking: 'bg-orange-100 text-orange-800',
  vocabulary: 'bg-yellow-100 text-yellow-800',
  pronunciation: 'bg-pink-100 text-pink-800',
  mock: 'bg-red-100 text-red-800',
};

const KIND_LABELS: Record<string, string> = {
  'next-unattempted-paper': 'Next Paper',
  'drill-by-tag': 'Tag Drill',
  'spaced-rep-review': 'Spaced Review',
  'weak-skill-focus': 'Weak-Skill Focus',
  'full-mock': 'Full Mock',
  'mini-mock': 'Mini Mock',
  'expert-review-submission': 'Expert Review',
  'pronunciation-drill': 'Pronunciation',
  'vocabulary-flashcards': 'Flashcards',
  'custom-content': 'Custom',
};

export default function StudyPlanTemplatePreviewPage() {
  const params = useParams();
  const id = params && typeof params.id === 'string' ? params.id : Array.isArray(params?.id) ? params.id[0] : '';

  const [template, setTemplate] = useState<StudyPlanTemplateDetail | null>(null);
  const [preview, setPreview] = useState<StudyPlanTemplatePreview | null>(null);
  const [loading, setLoading] = useState(true);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [targetBand, setTargetBand] = useState('B');
  const [professionId, setProfessionId] = useState('');
  const [weeksToPreview, setWeeksToPreview] = useState(4);

  useEffect(() => {
    if (!id) return;
    getStudyPlanTemplate(id)
      .then((t) => {
        setTemplate(t);
        setWeeksToPreview(Math.min(t.maxWeeks, 4));
      })
      .catch((e: unknown) => {
        const err = e as { userMessage?: string; message?: string };
        setError(err.userMessage ?? err.message ?? 'Failed to load template');
      })
      .finally(() => setLoading(false));
  }, [id]);

  const runPreview = async () => {
    if (!id) return;
    setPreviewLoading(true);
    setError(null);
    try {
      const result = await previewStudyPlanTemplate(id, {
        targetBand: targetBand || null,
        professionId: professionId || null,
        weeksToPreview,
      });
      setPreview(result);
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setError(err.userMessage ?? err.message ?? 'Preview failed');
    } finally {
      setPreviewLoading(false);
    }
  };

  if (loading) {
    return (
      <AdminSettingsLayout title="Preview template" breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Study plan templates', href: '/admin/study-plan-templates' }, { label: 'Preview' }]}>
        <div className="p-8 text-center text-admin-fg-muted">Loading template…</div>
      </AdminSettingsLayout>
    );
  }

  if (!template) {
    return (
      <AdminSettingsLayout title="Preview template" breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Study plan templates', href: '/admin/study-plan-templates' }, { label: 'Preview' }]}>
        <div className="p-8 text-center text-admin-danger">Template not found.</div>
      </AdminSettingsLayout>
    );
  }

  const weekGroups = preview
    ? Array.from(new Set(preview.days.map((d) => d.weekIndex))).sort((a, b) => a - b)
    : [];

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Study plan templates', href: '/admin/study-plan-templates' },
    { label: template.name, href: `/admin/study-plan-templates/${id}` },
    { label: 'Preview' },
  ];

  const inputCls = 'w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong';

  return (
    <AdminSettingsLayout
      title={template.name}
      description="Preview — dry-run of the plan materialisation for a hypothetical learner"
      breadcrumbs={breadcrumbs}
      eyebrow="Study planning"
      icon={<Eye className="h-5 w-5" />}
      backHref={`/admin/study-plan-templates/${id}`}
    >
      <SettingsSection title="Preview parameters">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="space-y-1">
            <label className="text-sm font-medium text-admin-fg-strong">Target OET Band</label>
            <select
              value={targetBand}
              onChange={(e) => setTargetBand(e.target.value)}
              className={inputCls}
            >
              <option value="">Any</option>
              <option value="A">A</option>
              <option value="B">B</option>
              <option value="C">C</option>
              <option value="D">D</option>
              <option value="E">E</option>
            </select>
          </div>
          <div className="space-y-1">
            <label className="text-sm font-medium text-admin-fg-strong">Profession ID</label>
            <input
              type="text"
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
              placeholder="e.g. nursing (optional)"
              className={inputCls}
            />
          </div>
          <div className="space-y-1">
            <label className="text-sm font-medium text-admin-fg-strong">
              Weeks to Preview ({template.minWeeks}–{template.maxWeeks})
            </label>
            <input
              type="number"
              value={weeksToPreview}
              min={1}
              max={template.maxWeeks}
              onChange={(e) => setWeeksToPreview(Number(e.target.value))}
              className={inputCls}
            />
          </div>
        </div>
        <Button onClick={runPreview} disabled={previewLoading} loading={previewLoading} className="mt-4">
          <RefreshCw className="w-4 h-4 mr-1" />
          {previewLoading ? 'Running preview…' : 'Run Preview'}
        </Button>
        {error && (
          <p className="mt-2 text-sm text-admin-danger">{error}</p>
        )}
      </SettingsSection>

      {/* Preview results */}
      {preview && (
        <SettingsSection title="Preview output">
          <div className="flex items-center gap-2 text-sm text-admin-fg-muted mb-4">
            <BookOpen className="w-4 h-4" />
            <span>
              Showing {preview.days.length} days across {weekGroups.length} week(s) for template{' '}
              <span className="font-mono">{preview.slug}</span>
            </span>
          </div>

          {weekGroups.map((weekIndex) => {
            const days = preview.days.filter((d) => d.weekIndex === weekIndex);
            return (
              <div key={weekIndex} className="space-y-3 mb-6">
                <h3 className="text-base font-semibold text-admin-fg-strong">Week {weekIndex + 1}</h3>
                <div className="grid gap-3">
                  {days.map((day) => (
                    <Card key={`${weekIndex}-${day.dayOfWeek}`}>
                      <CardContent className="p-0 overflow-hidden">
                        <div className="px-4 py-2 bg-admin-bg-subtle flex items-center justify-between border-b border-admin-border">
                          <span className="font-medium capitalize text-sm text-admin-fg-strong">{day.dayOfWeek}</span>
                          <span className="text-xs text-admin-fg-muted">
                            {day.slots.reduce((sum, s) => sum + s.minutes, 0)} min total
                          </span>
                        </div>
                        <div className="divide-y divide-admin-border">
                          {day.slots.map((slot, i) => (
                            <div
                              key={i}
                              className="px-4 py-3 flex items-center justify-between gap-4"
                            >
                              <div className="flex items-center gap-3 min-w-0">
                                <span
                                  className={`text-xs px-2 py-0.5 rounded-full font-medium shrink-0 ${
                                    SUBTEST_COLORS[slot.subtest] ?? 'bg-admin-bg-subtle text-admin-fg-muted'
                                  }`}
                                >
                                  {slot.subtest}
                                </span>
                                <div className="min-w-0">
                                  <p className="text-sm font-medium truncate text-admin-fg-strong">{slot.title}</p>
                                  <p className="text-xs text-admin-fg-muted">
                                    {KIND_LABELS[slot.kind] ?? slot.kind}
                                    {slot.route && (
                                      <>
                                        {' · '}
                                        <Link
                                          href={slot.route}
                                          className="underline hover:no-underline text-[var(--admin-primary)]"
                                          target="_blank"
                                        >
                                          {slot.route}
                                        </Link>
                                      </>
                                    )}
                                  </p>
                                </div>
                              </div>
                              <span className="text-xs text-admin-fg-muted shrink-0">
                                {slot.minutes} min
                              </span>
                            </div>
                          ))}
                        </div>
                      </CardContent>
                    </Card>
                  ))}
                </div>
              </div>
            );
          })}
        </SettingsSection>
      )}

      {!preview && !previewLoading && (
        <Card>
          <CardContent className="p-12 text-center text-admin-fg-muted text-sm">
            Set parameters above and click <strong>Run Preview</strong> to see how this template materialises.
          </CardContent>
        </Card>
      )}
    </AdminSettingsLayout>
  );
}
