'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, RefreshCw, BookOpen } from 'lucide-react';
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
  const router = useRouter();

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
      <div className="p-8 text-center text-muted-foreground">Loading template…</div>
    );
  }

  if (!template) {
    return (
      <div className="p-8 text-center text-danger">Template not found.</div>
    );
  }

  const weekGroups = preview
    ? Array.from(new Set(preview.days.map((d) => d.weekIndex))).sort((a, b) => a - b)
    : [];

  return (
    <div className="max-w-5xl mx-auto p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <button
          onClick={() => router.push(`/admin/study-plan-templates/${id}`)}
          className="p-2 rounded-lg hover:bg-muted transition-colors"
        >
          <ArrowLeft className="w-5 h-5" />
        </button>
        <div>
          <h1 className="text-2xl font-bold">{template.name}</h1>
          <p className="text-sm text-muted-foreground">
            Preview — dry-run of the plan materialisation for a hypothetical learner
          </p>
        </div>
      </div>

      {/* Controls */}
      <div className="bg-muted/30 border rounded-xl p-4 space-y-4">
        <h2 className="font-semibold text-sm uppercase tracking-wide text-muted-foreground">
          Preview Parameters
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="space-y-1">
            <label className="text-sm font-medium">Target OET Band</label>
            <select
              value={targetBand}
              onChange={(e) => setTargetBand(e.target.value)}
              className="w-full border rounded-lg px-3 py-2 text-sm bg-background"
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
            <label className="text-sm font-medium">Profession ID</label>
            <input
              type="text"
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
              placeholder="e.g. nursing (optional)"
              className="w-full border rounded-lg px-3 py-2 text-sm bg-background"
            />
          </div>
          <div className="space-y-1">
            <label className="text-sm font-medium">
              Weeks to Preview ({template.minWeeks}–{template.maxWeeks})
            </label>
            <input
              type="number"
              value={weeksToPreview}
              min={1}
              max={template.maxWeeks}
              onChange={(e) => setWeeksToPreview(Number(e.target.value))}
              className="w-full border rounded-lg px-3 py-2 text-sm bg-background"
            />
          </div>
        </div>
        <button
          onClick={runPreview}
          disabled={previewLoading}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 disabled:opacity-50 transition-colors"
        >
          <RefreshCw className={`w-4 h-4 ${previewLoading ? 'animate-spin' : ''}`} />
          {previewLoading ? 'Running preview…' : 'Run Preview'}
        </button>
        {error && (
          <p className="text-sm text-danger">{error}</p>
        )}
      </div>

      {/* Preview results */}
      {preview && (
        <div className="space-y-6">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <BookOpen className="w-4 h-4" />
            <span>
              Showing {preview.days.length} days across {weekGroups.length} week(s) for template{' '}
              <span className="font-mono">{preview.slug}</span>
            </span>
          </div>

          {weekGroups.map((weekIndex) => {
            const days = preview.days.filter((d) => d.weekIndex === weekIndex);
            return (
              <div key={weekIndex} className="space-y-3">
                <h3 className="text-base font-semibold">Week {weekIndex + 1}</h3>
                <div className="grid gap-3">
                  {days.map((day) => (
                    <div
                      key={`${weekIndex}-${day.dayOfWeek}`}
                      className="border rounded-xl overflow-hidden"
                    >
                      <div className="px-4 py-2 bg-muted/50 flex items-center justify-between">
                        <span className="font-medium capitalize text-sm">{day.dayOfWeek}</span>
                        <span className="text-xs text-muted-foreground">
                          {day.slots.reduce((sum, s) => sum + s.minutes, 0)} min total
                        </span>
                      </div>
                      <div className="divide-y">
                        {day.slots.map((slot, i) => (
                          <div
                            key={i}
                            className="px-4 py-3 flex items-center justify-between gap-4"
                          >
                            <div className="flex items-center gap-3 min-w-0">
                              <span
                                className={`text-xs px-2 py-0.5 rounded-full font-medium shrink-0 ${
                                  SUBTEST_COLORS[slot.subtest] ?? 'bg-muted text-muted-foreground'
                                }`}
                              >
                                {slot.subtest}
                              </span>
                              <div className="min-w-0">
                                <p className="text-sm font-medium truncate">{slot.title}</p>
                                <p className="text-xs text-muted-foreground">
                                  {KIND_LABELS[slot.kind] ?? slot.kind}
                                  {slot.route && (
                                    <>
                                      {' · '}
                                      <Link
                                        href={slot.route}
                                        className="underline hover:no-underline"
                                        target="_blank"
                                      >
                                        {slot.route}
                                      </Link>
                                    </>
                                  )}
                                </p>
                              </div>
                            </div>
                            <span className="text-xs text-muted-foreground shrink-0">
                              {slot.minutes} min
                            </span>
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {!preview && !previewLoading && (
        <div className="text-center py-12 text-muted-foreground text-sm">
          Set parameters above and click <strong>Run Preview</strong> to see how this template materialises.
        </div>
      )}
    </div>
  );
}
