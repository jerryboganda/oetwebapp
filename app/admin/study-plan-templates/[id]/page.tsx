'use client';

import { useEffect, useState, useCallback } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Edit3 } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import {
  getStudyPlanTemplate,
  updateStudyPlanTemplate,
  softDeleteStudyPlanTemplate,
  duplicateStudyPlanTemplate,
  validateStudyPlanTemplate,
  previewStudyPlanTemplate,
  type StudyPlanTemplateDetail,
  type StudyPlanTemplateBody,
  type StudyPlanTemplateWeek,
  type StudyPlanTemplateDay,
  type StudyPlanTemplateSlot,
  type StudyPlanTemplatePreview,
  type StudyPlanSlotKind,
} from '@/lib/study-plan-admin-api';

type Tab = 'metadata' | 'tiers' | 'weeks' | 'checkpoints' | 'validation' | 'preview';

const SLOT_KINDS: StudyPlanSlotKind[] = [
  'next-unattempted-paper',
  'drill-by-tag',
  'spaced-rep-review',
  'weak-skill-focus',
  'full-mock',
  'mini-mock',
  'expert-review-submission',
  'pronunciation-drill',
  'vocabulary-flashcards',
  'custom-content',
];

const SUBTESTS = ['reading', 'listening', 'writing', 'speaking', 'vocabulary', 'pronunciation', 'mock'];
const DAYS_OF_WEEK = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];

export default function StudyPlanTemplateEditorPage() {
  const params = useParams();
  const router = useRouter();
  const id = params && typeof params.id === 'string' ? params.id : Array.isArray(params?.id) ? params.id[0] : '';
  const [template, setTemplate] = useState<StudyPlanTemplateDetail | null>(null);
  const [body, setBody] = useState<StudyPlanTemplateBody>({ weeks: [], checkpoints: [] });
  const [tab, setTab] = useState<Tab>('metadata');
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [validation, setValidation] = useState<{ isValid: boolean; errors: string[] } | null>(null);
  const [preview, setPreview] = useState<StudyPlanTemplatePreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);

  // Metadata mirror state
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [minWeeks, setMinWeeks] = useState(1);
  const [maxWeeks, setMaxWeeks] = useState(12);
  const [targetBand, setTargetBand] = useState('');
  const [professionId, setProfessionId] = useState('');
  const [defaultMinutesPerDay, setDefaultMinutesPerDay] = useState(60);
  const [focusTagsText, setFocusTagsText] = useState('');
  const [isActive, setIsActive] = useState(false);
  const [tierCodes, setTierCodes] = useState<string[]>([]);

  const load = useCallback(async () => {
    setError(null);
    try {
      const t = await getStudyPlanTemplate(id);
      setTemplate(t);
      setBody(t.body ?? { weeks: [], checkpoints: [] });
      setName(t.name);
      setDescription(t.description ?? '');
      setMinWeeks(t.minWeeks);
      setMaxWeeks(t.maxWeeks);
      setTargetBand(t.targetBand ?? '');
      setProfessionId(t.professionId ?? '');
      setDefaultMinutesPerDay(t.defaultMinutesPerDay);
      setFocusTagsText((t.focusTags ?? []).join(', '));
      setIsActive(t.isActive);
      setTierCodes(t.tierCodes);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Failed to load template.');
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  const saveAll = async () => {
    setSaving(true);
    setError(null);
    try {
      const updated = await updateStudyPlanTemplate(id, {
        slug: template!.slug,
        name,
        description,
        examTypeCode: template!.examTypeCode,
        minWeeks,
        maxWeeks,
        targetBand: targetBand || null,
        professionId: professionId || null,
        focusTags: focusTagsText.split(',').map((t) => t.trim()).filter(Boolean),
        defaultMinutesPerDay,
        isActive,
        tierCodes,
        body,
      });
      setTemplate(updated);
      setToast(`Saved. Version ${updated.version}.`);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Save failed.');
    } finally {
      setSaving(false);
    }
  };

  const onValidate = async () => {
    try {
      const result = await validateStudyPlanTemplate(id);
      setValidation(result);
      setTab('validation');
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Validate failed.');
    }
  };

  const onPreview = async () => {
    setPreviewLoading(true);
    try {
      const result = await previewStudyPlanTemplate(id, {
        professionId: professionId || null,
        targetBand: targetBand || null,
        weeksToPreview: 2,
      });
      setPreview(result);
      setTab('preview');
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Preview failed.');
    } finally {
      setPreviewLoading(false);
    }
  };

  const onDuplicate = async () => {
    try {
      const created = await duplicateStudyPlanTemplate(id);
      router.push(`/admin/study-plan-templates/${created.id}`);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Duplicate failed.');
    }
  };

  const onSoftDelete = async () => {
    if (!confirm(`Soft-delete "${template?.name}"? It will be hidden but kept in history.`)) return;
    try {
      await softDeleteStudyPlanTemplate(id);
      router.push('/admin/study-plan-templates');
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Delete failed.');
    }
  };

  // ── Week / Day / Slot mutations ──
  const addWeek = () => {
    setBody((b) => ({
      ...b,
      weeks: [
        ...b.weeks,
        { weekIndex: b.weeks.length, label: `Week ${b.weeks.length + 1}`, days: [] },
      ],
    }));
  };

  const removeWeek = (idx: number) => {
    setBody((b) => ({ ...b, weeks: b.weeks.filter((_, i) => i !== idx) }));
  };

  const updateWeek = (idx: number, patch: Partial<StudyPlanTemplateWeek>) => {
    setBody((b) => ({
      ...b,
      weeks: b.weeks.map((w, i) => (i === idx ? { ...w, ...patch } : w)),
    }));
  };

  const addDay = (weekIdx: number) => {
    setBody((b) => ({
      ...b,
      weeks: b.weeks.map((w, i) => {
        if (i !== weekIdx) return w;
        const used = new Set(w.days.map((d) => d.dayOfWeek));
        const next = DAYS_OF_WEEK.find((d) => !used.has(d)) ?? 'mon';
        return { ...w, days: [...w.days, { dayOfWeek: next, slots: [] }] };
      }),
    }));
  };

  const removeDay = (weekIdx: number, dayIdx: number) => {
    setBody((b) => ({
      ...b,
      weeks: b.weeks.map((w, i) =>
        i === weekIdx ? { ...w, days: w.days.filter((_, di) => di !== dayIdx) } : w,
      ),
    }));
  };

  const updateDay = (weekIdx: number, dayIdx: number, patch: Partial<StudyPlanTemplateDay>) => {
    setBody((b) => ({
      ...b,
      weeks: b.weeks.map((w, i) =>
        i === weekIdx
          ? {
              ...w,
              days: w.days.map((d, di) => (di === dayIdx ? { ...d, ...patch } : d)),
            }
          : w,
      ),
    }));
  };

  const addSlot = (weekIdx: number, dayIdx: number) => {
    const newSlot: StudyPlanTemplateSlot = {
      subtest: 'reading',
      kind: 'next-unattempted-paper',
      minutes: 30,
    };
    updateDay(weekIdx, dayIdx, {
      slots: [...(body.weeks[weekIdx]?.days[dayIdx]?.slots ?? []), newSlot],
    });
  };

  const removeSlot = (weekIdx: number, dayIdx: number, slotIdx: number) => {
    const day = body.weeks[weekIdx]?.days[dayIdx];
    if (!day) return;
    updateDay(weekIdx, dayIdx, { slots: day.slots.filter((_, i) => i !== slotIdx) });
  };

  const updateSlot = (
    weekIdx: number,
    dayIdx: number,
    slotIdx: number,
    patch: Partial<StudyPlanTemplateSlot>,
  ) => {
    const day = body.weeks[weekIdx]?.days[dayIdx];
    if (!day) return;
    updateDay(weekIdx, dayIdx, {
      slots: day.slots.map((s, i) => (i === slotIdx ? { ...s, ...patch } : s)),
    });
  };

  const toggleTier = (t: string) =>
    setTierCodes((prev) => (prev.includes(t) ? prev.filter((x) => x !== t) : [...prev, t]));

  if (!template) {
    return (
      <AdminSettingsLayout title="Study plan template" breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Study plan templates', href: '/admin/study-plan-templates' }, { label: 'Editor' }]}>
        {error ? <div className="text-admin-danger">{error}</div> : 'Loading...'}
      </AdminSettingsLayout>
    );
  }

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Study plan templates', href: '/admin/study-plan-templates' },
    { label: name || template.slug },
  ];

  return (
    <AdminSettingsLayout
      title={name || template.slug}
      description={`${template.slug} · v${template.version}`}
      breadcrumbs={breadcrumbs}
      eyebrow="Study planning"
      icon={<Edit3 className="h-5 w-5" />}
      backHref="/admin/study-plan-templates"
      actions={(
        <div className="flex gap-2 items-center">
          {isActive ? <Badge variant="success">Active</Badge> : <Badge variant="default">Inactive</Badge>}
          <Button variant="outline" size="sm" onClick={onValidate}>Validate</Button>
          <Button variant="outline" size="sm" onClick={onPreview} disabled={previewLoading} loading={previewLoading}>
            Preview
          </Button>
          <Button variant="outline" size="sm" onClick={onDuplicate}>Duplicate</Button>
          <Button variant="destructive" size="sm" onClick={onSoftDelete}>Soft-delete</Button>
          <Button onClick={saveAll} disabled={saving} loading={saving}>Save</Button>
        </div>
      )}
    >
      {toast && (
        <Card surface="tinted-success">
          <CardContent className="p-3 text-sm text-admin-success">{toast}</CardContent>
        </Card>
      )}
      {error && (
        <Card surface="tinted-danger">
          <CardContent className="p-3 text-sm text-admin-danger">{error}</CardContent>
        </Card>
      )}

      {/* Tabs */}
      <Card>
        <CardContent className="p-0">
          <div className="border-b border-admin-border flex gap-1 px-4">
            {(['metadata', 'tiers', 'weeks', 'checkpoints', 'validation', 'preview'] as Tab[]).map((t) => (
              <button
                key={t}
                onClick={() => setTab(t)}
                className={`px-4 py-2 text-sm capitalize border-b-2 ${
                  tab === t ? 'border-[var(--admin-primary)] text-[var(--admin-primary)]' : 'border-transparent text-admin-fg-muted hover:text-admin-fg-strong'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
          <div className="p-4">

      {/* Metadata tab */}
      {tab === 'metadata' && (
        <div className="space-y-4 max-w-2xl">
          <div>
            <label className="block text-sm font-medium mb-1">Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} className="w-full border rounded px-3 py-2" />
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
              <label className="block text-sm font-medium mb-1">Min weeks</label>
              <input
                type="number"
                min={1}
                value={minWeeks}
                onChange={(e) => setMinWeeks(parseInt(e.target.value) || 1)}
                className="w-full border rounded px-3 py-2"
              />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Max weeks</label>
              <input
                type="number"
                min={1}
                value={maxWeeks}
                onChange={(e) => setMaxWeeks(parseInt(e.target.value) || 1)}
                className="w-full border rounded px-3 py-2"
              />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Target band</label>
              <select
                value={targetBand}
                onChange={(e) => setTargetBand(e.target.value)}
                className="w-full border rounded px-3 py-2"
              >
                <option value="">Any</option>
                <option>A</option>
                <option>B</option>
                <option>C+</option>
                <option>C</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Profession</label>
              <input
                value={professionId}
                onChange={(e) => setProfessionId(e.target.value)}
                placeholder="any"
                className="w-full border rounded px-3 py-2"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium mb-1">Default minutes/day</label>
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
            <label className="block text-sm font-medium mb-1">
              Focus tags (comma-separated, e.g. weak-writing, retake-rescue)
            </label>
            <input
              value={focusTagsText}
              onChange={(e) => setFocusTagsText(e.target.value)}
              className="w-full border rounded px-3 py-2"
            />
          </div>
          <div>
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              <span>Active (visible to plan generator)</span>
            </label>
          </div>
        </div>
      )}

      {/* Tiers tab */}
      {tab === 'tiers' && (
        <div className="max-w-md">
          <p className="text-sm text-muted-foreground mb-4">
            A learner can be matched to this template only if their subscription tier is checked
            below. Free is always available so the planner never returns empty.
          </p>
          {['free', 'premium', 'elite'].map((t) => (
            <label key={t} className="flex items-center gap-3 py-2 border-b">
              <input
                type="checkbox"
                checked={tierCodes.includes(t)}
                onChange={() => toggleTier(t)}
              />
              <span className="capitalize font-medium">{t}</span>
            </label>
          ))}
        </div>
      )}

      {/* Weeks tab */}
      {tab === 'weeks' && (
        <div>
          <div className="flex justify-between mb-4">
            <p className="text-sm text-muted-foreground">
              {body.weeks.length} week(s). Each week has days, each day has slots.
            </p>
            <button onClick={addWeek} className="px-3 py-1 bg-blue-600 text-white rounded text-sm">
              + Add week
            </button>
          </div>

          <div className="space-y-3">
            {body.weeks.map((week, weekIdx) => (
              <div key={weekIdx} className="border rounded p-4">
                <div className="flex items-center gap-3 mb-3">
                  <span className="font-medium">Week {weekIdx + 1}</span>
                  <input
                    value={week.label ?? ''}
                    placeholder="label"
                    onChange={(e) => updateWeek(weekIdx, { label: e.target.value })}
                    className="border rounded px-2 py-1 text-sm flex-1"
                  />
                  <button
                    onClick={() => addDay(weekIdx)}
                    className="px-2 py-1 border rounded text-xs"
                  >
                    + Day
                  </button>
                  <button
                    onClick={() => removeWeek(weekIdx)}
                    className="px-2 py-1 border border-red-300 text-red-700 rounded text-xs"
                  >
                    Delete week
                  </button>
                </div>

                <div className="space-y-2 ml-4">
                  {week.days.map((day, dayIdx) => (
                    <div key={dayIdx} className="border-l-2 border-border pl-3">
                      <div className="flex items-center gap-2 mb-2">
                        <select
                          value={day.dayOfWeek}
                          onChange={(e) => updateDay(weekIdx, dayIdx, { dayOfWeek: e.target.value })}
                          className="border rounded px-2 py-1 text-sm"
                        >
                          {DAYS_OF_WEEK.map((d) => (
                            <option key={d} value={d}>
                              {d}
                            </option>
                          ))}
                        </select>
                        <button
                          onClick={() => addSlot(weekIdx, dayIdx)}
                          className="px-2 py-1 border rounded text-xs"
                        >
                          + Slot
                        </button>
                        <button
                          onClick={() => removeDay(weekIdx, dayIdx)}
                          className="px-2 py-1 border border-red-300 text-red-700 rounded text-xs"
                        >
                          Remove day
                        </button>
                      </div>

                      <div className="space-y-1">
                        {day.slots.map((slot, slotIdx) => (
                          <div
                            key={slotIdx}
                            className="flex items-center gap-2 bg-muted p-2 rounded text-sm"
                          >
                            <select
                              value={slot.subtest}
                              onChange={(e) =>
                                updateSlot(weekIdx, dayIdx, slotIdx, { subtest: e.target.value })
                              }
                              className="border rounded px-2 py-1 text-xs"
                            >
                              {SUBTESTS.map((s) => (
                                <option key={s}>{s}</option>
                              ))}
                            </select>
                            <select
                              value={slot.kind}
                              onChange={(e) =>
                                updateSlot(weekIdx, dayIdx, slotIdx, {
                                  kind: e.target.value as StudyPlanSlotKind,
                                })
                              }
                              className="border rounded px-2 py-1 text-xs"
                            >
                              {SLOT_KINDS.map((k) => (
                                <option key={k}>{k}</option>
                              ))}
                            </select>
                            <input
                              type="number"
                              min={1}
                              value={slot.minutes}
                              onChange={(e) =>
                                updateSlot(weekIdx, dayIdx, slotIdx, {
                                  minutes: parseInt(e.target.value) || 0,
                                })
                              }
                              className="w-16 border rounded px-2 py-1 text-xs"
                              title="minutes"
                            />
                            <input
                              value={slot.rationaleHint ?? ''}
                              placeholder="rationale hint"
                              onChange={(e) =>
                                updateSlot(weekIdx, dayIdx, slotIdx, { rationaleHint: e.target.value })
                              }
                              className="flex-1 border rounded px-2 py-1 text-xs"
                            />
                            {slot.kind === 'drill-by-tag' && (
                              <input
                                value={(slot.tags ?? []).join(',')}
                                placeholder="tags,comma"
                                onChange={(e) =>
                                  updateSlot(weekIdx, dayIdx, slotIdx, {
                                    tags: e.target.value
                                      .split(',')
                                      .map((t) => t.trim())
                                      .filter(Boolean),
                                  })
                                }
                                className="w-32 border rounded px-2 py-1 text-xs"
                              />
                            )}
                            {slot.kind === 'custom-content' && (
                              <input
                                value={slot.contentId ?? ''}
                                placeholder="contentId"
                                onChange={(e) =>
                                  updateSlot(weekIdx, dayIdx, slotIdx, { contentId: e.target.value })
                                }
                                className="w-40 border rounded px-2 py-1 text-xs font-mono"
                              />
                            )}
                            <button
                              onClick={() => removeSlot(weekIdx, dayIdx, slotIdx)}
                              className="px-2 py-1 text-red-700 text-xs"
                              title="Remove slot"
                            >
                              ✕
                            </button>
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Checkpoints tab */}
      {tab === 'checkpoints' && (
        <div>
          <div className="flex justify-between mb-4">
            <p className="text-sm text-muted-foreground">
              Checkpoints inject mocks or expert-review prompts after specific weeks.
            </p>
            <button
              onClick={() =>
                setBody((b) => ({
                  ...b,
                  checkpoints: [
                    ...b.checkpoints,
                    { afterWeek: 0, kind: 'mini-mock', subtests: ['reading'] },
                  ],
                }))
              }
              className="px-3 py-1 bg-blue-600 text-white rounded text-sm"
            >
              + Add checkpoint
            </button>
          </div>

          <div className="space-y-2">
            {body.checkpoints.map((cp, idx) => (
              <div key={idx} className="border rounded p-3 flex items-center gap-2">
                <span className="text-sm">After week</span>
                <input
                  type="number"
                  min={0}
                  value={cp.afterWeek}
                  onChange={(e) =>
                    setBody((b) => ({
                      ...b,
                      checkpoints: b.checkpoints.map((c, i) =>
                        i === idx ? { ...c, afterWeek: parseInt(e.target.value) || 0 } : c,
                      ),
                    }))
                  }
                  className="w-20 border rounded px-2 py-1"
                />
                <select
                  value={cp.kind}
                  onChange={(e) =>
                    setBody((b) => ({
                      ...b,
                      checkpoints: b.checkpoints.map((c, i) =>
                        i === idx ? { ...c, kind: e.target.value } : c,
                      ),
                    }))
                  }
                  className="border rounded px-2 py-1"
                >
                  <option value="mini-mock">mini-mock</option>
                  <option value="full-mock">full-mock</option>
                  <option value="expert-review-submission">expert-review</option>
                </select>
                <input
                  value={cp.subtests.join(',')}
                  onChange={(e) =>
                    setBody((b) => ({
                      ...b,
                      checkpoints: b.checkpoints.map((c, i) =>
                        i === idx
                          ? {
                              ...c,
                              subtests: e.target.value.split(',').map((s) => s.trim()).filter(Boolean),
                            }
                          : c,
                      ),
                    }))
                  }
                  placeholder="subtests,comma"
                  className="flex-1 border rounded px-2 py-1 text-sm"
                />
                <button
                  onClick={() =>
                    setBody((b) => ({
                      ...b,
                      checkpoints: b.checkpoints.filter((_, i) => i !== idx),
                    }))
                  }
                  className="px-2 py-1 text-red-700 text-sm"
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Validation tab */}
      {tab === 'validation' && (
        <div>
          {validation === null ? (
            <p className="text-muted-foreground">Click &quot;Validate&quot; above to check the template structure.</p>
          ) : validation.isValid ? (
            <div className="bg-green-50 border border-green-200 text-green-800 rounded p-4">
              ✓ Template structure is valid.
            </div>
          ) : (
            <div className="bg-red-50 border border-red-200 rounded p-4">
              <div className="font-medium text-red-800 mb-2">
                {validation.errors.length} issue(s):
              </div>
              <ul className="list-disc pl-5 text-sm text-red-700 space-y-1">
                {validation.errors.map((err, i) => (
                  <li key={i}>{err}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Preview tab */}
      {tab === 'preview' && (
        <div>
          {preview === null ? (
            <p className="text-muted-foreground">Click &quot;Preview&quot; above to dry-run the generator on this template.</p>
          ) : (
            <div className="space-y-2">
              {preview.days.map((d, idx) => (
                <div key={idx} className="border rounded p-3">
                  <div className="text-sm font-medium mb-2">
                    Week {d.weekIndex + 1} · {d.dayOfWeek}
                  </div>
                  <div className="space-y-1">
                    {d.slots.map((s, si) => (
                      <div key={si} className="text-sm flex gap-3">
                        <span className="font-mono text-xs px-2 py-0.5 bg-muted rounded">
                          {s.subtest}
                        </span>
                        <span className="text-muted-foreground">{s.kind}</span>
                        <span className="text-muted-foreground">{s.minutes}m</span>
                        <span className="flex-1">{s.title}</span>
                        <a href={s.route} className="text-blue-600 hover:underline text-xs">
                          {s.route}
                        </a>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
          </div>
        </CardContent>
      </Card>
    </AdminSettingsLayout>
  );
}
