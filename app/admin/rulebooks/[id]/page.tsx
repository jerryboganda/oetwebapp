'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Plus, Trash2, Edit3, CheckCircle2, X, Copy, Download, Undo2 } from 'lucide-react';
import {
  adminGetRulebook,
  adminUpdateRulebookMeta,
  adminPublishRulebook,
  adminUnpublishRulebook,
  adminCloneRulebook,
  adminDeleteRulebook,
  adminExportRulebook,
  adminCreateRulebookSection,
  adminUpdateRulebookSection,
  adminDeleteRulebookSection,
  adminCreateRulebookRule,
  adminUpdateRulebookRule,
  adminDeleteRulebookRule,
  type AdminRulebookDetail,
  type AdminRulebookRule,
  type AdminRulebookSection,
} from '@/lib/api';
import { Card } from '@/components/ui/card';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const SEVERITIES = ['critical', 'major', 'minor', 'info'];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

type RuleFormState = Partial<AdminRulebookRule> & {
  code: string; sectionCode: string; title: string; body: string; severity: string;
};

const emptyRuleForm = (sectionCode: string): RuleFormState => ({
  code: '',
  sectionCode,
  title: '',
  body: '',
  severity: 'major',
  appliesToJson: '"all"',
  turnStage: '',
  exemplarPhrasesJson: '',
  forbiddenPatternsJson: '',
  checkId: '',
  paramsJson: '',
  examplesJson: '',
});

export default function AdminRulebookDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const id = decodeURIComponent(params.id);

  const [data, setData] = useState<AdminRulebookDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);

  // Section form
  const [newSection, setNewSection] = useState({ code: '', title: '' });
  const [editingSection, setEditingSection] = useState<AdminRulebookSection | null>(null);

  // Rule form (modal-ish inline)
  const [ruleForm, setRuleForm] = useState<RuleFormState | null>(null);
  const [editingRuleId, setEditingRuleId] = useState<string | null>(null);
  const [filterSection, setFilterSection] = useState<string>('');

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const d = await adminGetRulebook(id);
      setData(d);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Failed to load.' });
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { queueMicrotask(() => void reload()); }, [reload]);

  const filteredRules = useMemo(() => {
    if (!data) return [];
    return filterSection ? data.rules.filter((r) => r.sectionCode === filterSection) : data.rules;
  }, [data, filterSection]);

  // ── Meta + Publish ──
  async function handlePublish() {
    if (!confirm('Publish this rulebook? Any other Published version for the same kind/profession will be archived.')) return;
    try {
      const d = await adminPublishRulebook(id);
      setData(d);
      setToast({ variant: 'success', message: 'Rulebook published.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleUnpublish() {
    if (!confirm('Unpublish this rulebook? It will revert to Draft and stop being used for grading.')) return;
    try {
      const d = await adminUnpublishRulebook(id);
      setData(d);
      setToast({ variant: 'success', message: 'Rulebook unpublished (now Draft).' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleClone() {
    const newVersion = prompt('Clone into a new version label:', `${data?.version || ''}-copy`);
    if (!newVersion?.trim()) return;
    try {
      const cloned = await adminCloneRulebook(id, { version: newVersion.trim() });
      setToast({ variant: 'success', message: 'Cloned.' });
      router.push(`/admin/rulebooks/${encodeURIComponent(cloned.id)}`);
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleDelete() {
    if (data?.status === 'Published') {
      setToast({ variant: 'error', message: 'Unpublish before deleting.' });
      return;
    }
    if (!confirm(`Delete rulebook "${id}"? This is permanent.`)) return;
    if (prompt('Type the rulebook id to confirm:') !== id) {
      setToast({ variant: 'error', message: 'Confirmation did not match. Aborted.' });
      return;
    }
    try {
      await adminDeleteRulebook(id);
      setToast({ variant: 'success', message: 'Deleted.' });
      router.push('/admin/rulebooks');
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleExport() {
    try {
      const json = await adminExportRulebook(id);
      const blob = new Blob([JSON.stringify(json, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${id}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleVersionEdit() {
    const newVersion = prompt('New version label:', data?.version || '');
    if (!newVersion || newVersion === data?.version) return;
    try {
      const d = await adminUpdateRulebookMeta(id, { version: newVersion });
      setData(d);
      setToast({ variant: 'success', message: 'Version updated.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  // ── Sections ──
  async function handleCreateSection() {
    if (!newSection.code.trim() || !newSection.title.trim()) {
      setToast({ variant: 'error', message: 'Code and title required.' });
      return;
    }
    try {
      await adminCreateRulebookSection(id, { code: newSection.code.trim(), title: newSection.title.trim() });
      setNewSection({ code: '', title: '' });
      await reload();
      setToast({ variant: 'success', message: 'Section created.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleUpdateSection(s: AdminRulebookSection) {
    try {
      await adminUpdateRulebookSection(id, s.id, { title: s.title, orderIndex: s.orderIndex });
      setEditingSection(null);
      await reload();
      setToast({ variant: 'success', message: 'Section updated.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleDeleteSection(s: AdminRulebookSection) {
    if (!confirm(`Delete section "${s.code}"? It must contain no rules.`)) return;
    try {
      await adminDeleteRulebookSection(id, s.id);
      await reload();
      setToast({ variant: 'success', message: 'Section deleted.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  // ── Rules ──
  function openRuleEditor(rule?: AdminRulebookRule) {
    if (rule) {
      setEditingRuleId(rule.id);
      setRuleForm({
        code: rule.code,
        sectionCode: rule.sectionCode,
        title: rule.title,
        body: rule.body,
        severity: rule.severity,
        appliesToJson: rule.appliesToJson,
        turnStage: rule.turnStage ?? '',
        exemplarPhrasesJson: rule.exemplarPhrasesJson ?? '',
        forbiddenPatternsJson: rule.forbiddenPatternsJson ?? '',
        checkId: rule.checkId ?? '',
        paramsJson: rule.paramsJson ?? '',
        examplesJson: rule.examplesJson ?? '',
      });
    } else {
      setEditingRuleId(null);
      setRuleForm(emptyRuleForm(filterSection || data?.sections[0]?.code || ''));
    }
  }

  async function handleSaveRule() {
    if (!ruleForm) return;
    if (!ruleForm.code.trim() || !ruleForm.title.trim() || !ruleForm.body.trim() || !ruleForm.sectionCode) {
      setToast({ variant: 'error', message: 'Code, section, title, and body are required.' });
      return;
    }
    // Validate optional JSON fields.
    for (const [field, val] of [
      ['appliesToJson', ruleForm.appliesToJson],
      ['exemplarPhrasesJson', ruleForm.exemplarPhrasesJson],
      ['forbiddenPatternsJson', ruleForm.forbiddenPatternsJson],
      ['paramsJson', ruleForm.paramsJson],
      ['examplesJson', ruleForm.examplesJson],
    ] as const) {
      if (val && val.trim()) {
        try { JSON.parse(val); } catch {
          setToast({ variant: 'error', message: `Invalid JSON in ${field}.` });
          return;
        }
      }
    }
    try {
      if (editingRuleId) {
        await adminUpdateRulebookRule(id, editingRuleId, {
          sectionCode: ruleForm.sectionCode,
          title: ruleForm.title,
          body: ruleForm.body,
          severity: ruleForm.severity,
          appliesToJson: ruleForm.appliesToJson || '"all"',
          turnStage: ruleForm.turnStage || null,
          exemplarPhrasesJson: ruleForm.exemplarPhrasesJson || null,
          forbiddenPatternsJson: ruleForm.forbiddenPatternsJson || null,
          checkId: ruleForm.checkId || null,
          paramsJson: ruleForm.paramsJson || null,
          examplesJson: ruleForm.examplesJson || null,
        });
      } else {
        await adminCreateRulebookRule(id, {
          code: ruleForm.code.trim(),
          sectionCode: ruleForm.sectionCode,
          title: ruleForm.title.trim(),
          body: ruleForm.body,
          severity: ruleForm.severity,
          appliesToJson: ruleForm.appliesToJson || '"all"',
          turnStage: ruleForm.turnStage || null,
          exemplarPhrasesJson: ruleForm.exemplarPhrasesJson || null,
          forbiddenPatternsJson: ruleForm.forbiddenPatternsJson || null,
          checkId: ruleForm.checkId || null,
          paramsJson: ruleForm.paramsJson || null,
          examplesJson: ruleForm.examplesJson || null,
        });
      }
      setRuleForm(null);
      setEditingRuleId(null);
      await reload();
      setToast({ variant: 'success', message: editingRuleId ? 'Rule updated.' : 'Rule created.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  async function handleDeleteRule(rule: AdminRulebookRule) {
    if (!confirm(`Delete rule "${rule.code}"?`)) return;
    try {
      await adminDeleteRulebookRule(id, rule.id);
      await reload();
      setToast({ variant: 'success', message: 'Rule deleted.' });
    } catch (e) { setToast({ variant: 'error', message: (e as Error).message }); }
  }

  if (loading) {
    return <div className="p-6 space-y-3">{[1, 2, 3].map((i) => <Skeleton key={i} className="h-24" />)}</div>;
  }
  if (!data) {
    return <div className="p-6"><Card className="p-6">Rulebook not found.</Card></div>;
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Rulebook editor">
      <Link href="/admin/rulebooks" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy">
        <ArrowLeft className="h-4 w-4" /> Back to rulebooks
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={BookOpen}
        accent="navy"
        title={`${data.kind} · ${data.profession}`}
        description={`Authority source: ${data.authoritySource || 'unspecified'}.`}
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap items-center gap-2">
              <button onClick={handleVersionEdit} className="font-mono text-sm hover:underline">v{data.version}</button>
              <Badge variant={data.status === 'Published' ? 'success' : 'muted'}>{data.status}</Badge>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="outline" onClick={handleExport}>
                <Download className="h-4 w-4 mr-1" /> Export
              </Button>
              <Button variant="outline" onClick={handleClone}>
                <Copy className="h-4 w-4 mr-1" /> Clone
              </Button>
              {data.status === 'Published' ? (
                <Button variant="outline" onClick={handleUnpublish}>
                  <Undo2 className="h-4 w-4 mr-1" /> Unpublish
                </Button>
              ) : (
                <Button onClick={handlePublish}>
                  <CheckCircle2 className="h-4 w-4 mr-1" /> Publish
                </Button>
              )}
              {data.status !== 'Published' && (
                <Button variant="outline" onClick={handleDelete}>
                  <Trash2 className="h-4 w-4 mr-1" /> Delete
                </Button>
              )}
            </div>
          </div>
        )}
      />

      {/* Sections */}
      <AdminRoutePanel title={`Sections (${data.sections.length})`}>
        <div className="space-y-2 mb-4">
          {data.sections.map((s) => (
            <div key={s.id} className="flex items-center gap-2 p-2 rounded border">
              {editingSection?.id === s.id ? (
                <>
                  <span className="font-mono text-xs text-gray-500 w-32">{s.code}</span>
                  <Input value={editingSection.title} onChange={(e) => setEditingSection({ ...editingSection, title: e.target.value })} className="flex-1" />
                  <Input type="number" value={editingSection.orderIndex} onChange={(e) => setEditingSection({ ...editingSection, orderIndex: Number(e.target.value) })} className="w-20" />
                  <Button size="sm" onClick={() => handleUpdateSection(editingSection)}>Save</Button>
                  <Button size="sm" variant="ghost" onClick={() => setEditingSection(null)}><X className="h-4 w-4" /></Button>
                </>
              ) : (
                <>
                  <span className="font-mono text-xs text-gray-500 w-32">{s.code}</span>
                  <span className="flex-1 font-medium">{s.title}</span>
                  <span className="text-xs text-gray-400">#{s.orderIndex}</span>
                  <Button size="sm" variant="ghost" onClick={() => setEditingSection(s)}><Edit3 className="h-4 w-4" /></Button>
                  <Button size="sm" variant="ghost" onClick={() => handleDeleteSection(s)}><Trash2 className="h-4 w-4 text-red-500" /></Button>
                </>
              )}
            </div>
          ))}
        </div>
        <div className="flex gap-2 items-end pt-2 border-t">
          <Input label="Code" placeholder="e.g. opening" value={newSection.code} onChange={(e) => setNewSection({ ...newSection, code: e.target.value })} className="w-48" />
          <Input label="Title" placeholder="e.g. Opening behaviours" value={newSection.title} onChange={(e) => setNewSection({ ...newSection, title: e.target.value })} className="flex-1" />
          <Button onClick={handleCreateSection}><Plus className="h-4 w-4 mr-1" /> Add Section</Button>
        </div>
      </AdminRoutePanel>

      {/* Rules */}
      <AdminRoutePanel
        title={`Rules (${filteredRules.length}${filterSection ? ` of ${data.rules.length}` : ''})`}
        actions={(
          <div className="flex items-center gap-2">
            <Select
              value={filterSection}
              onChange={(e) => setFilterSection(e.target.value)}
              className="w-48"
              options={[{ value: '', label: 'All sections' }, ...data.sections.map((s) => ({ value: s.code, label: `${s.code} — ${s.title}` }))]}
            />
            <Button onClick={() => openRuleEditor()}><Plus className="h-4 w-4 mr-1" /> New Rule</Button>
          </div>
        )}
      >

        <div className="space-y-2">
          {filteredRules.map((r) => (
            <div key={r.id} className="p-3 rounded border hover:bg-gray-50">
              <div className="flex items-start justify-between gap-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-mono text-xs px-2 py-0.5 bg-gray-100 rounded">{r.code}</span>
                    <Badge variant={r.severity === 'critical' ? 'danger' : r.severity === 'major' ? 'warning' : 'muted'}>
                      {r.severity}
                    </Badge>
                    <span className="text-xs text-gray-500">{r.sectionCode}</span>
                  </div>
                  <h3 className="font-medium mt-1">{r.title}</h3>
                  <p className="text-sm text-gray-600 line-clamp-2 mt-1">{r.body}</p>
                </div>
                <div className="flex gap-1">
                  <Button size="sm" variant="ghost" onClick={() => openRuleEditor(r)}><Edit3 className="h-4 w-4" /></Button>
                  <Button size="sm" variant="ghost" onClick={() => handleDeleteRule(r)}><Trash2 className="h-4 w-4 text-red-500" /></Button>
                </div>
              </div>
            </div>
          ))}
          {filteredRules.length === 0 && (
            <p className="text-sm text-gray-500 text-center py-6">No rules{filterSection ? ' in this section' : ''} yet.</p>
          )}
        </div>
      </AdminRoutePanel>

      {/* Rule Editor Modal */}
      {ruleForm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" onClick={() => setRuleForm(null)}>
          <Card className="p-6 max-w-3xl w-full max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-bold">{editingRuleId ? 'Edit Rule' : 'New Rule'}</h3>
              <Button variant="ghost" size="sm" onClick={() => setRuleForm(null)}><X className="h-4 w-4" /></Button>
            </div>
            <div className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="Code"
                  placeholder="e.g. greeting.warm"
                  value={ruleForm.code}
                  onChange={(e) => setRuleForm({ ...ruleForm, code: e.target.value })}
                  disabled={!!editingRuleId}
                />
                <Select
                  label="Section"
                  value={ruleForm.sectionCode}
                  onChange={(e) => setRuleForm({ ...ruleForm, sectionCode: e.target.value })}
                  options={data.sections.map((s) => ({ value: s.code, label: s.code }))}
                />
              </div>
              <Input label="Title" value={ruleForm.title} onChange={(e) => setRuleForm({ ...ruleForm, title: e.target.value })} />
              <Textarea label="Body" rows={3} value={ruleForm.body} onChange={(e) => setRuleForm({ ...ruleForm, body: e.target.value })} />
              <div className="grid grid-cols-2 gap-3">
                <Select
                  label="Severity"
                  value={ruleForm.severity}
                  onChange={(e) => setRuleForm({ ...ruleForm, severity: e.target.value })}
                  options={SEVERITIES.map((s) => ({ value: s, label: s }))}
                />
                <Input label="Turn Stage (optional)" value={ruleForm.turnStage ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, turnStage: e.target.value })} />
              </div>
              <Input label="Check ID (optional)" value={ruleForm.checkId ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, checkId: e.target.value })} />
              <details>
                <summary className="cursor-pointer text-sm font-medium text-gray-700 mb-2">Advanced JSON fields</summary>
                <div className="space-y-3 pt-2">
                  <Textarea label="Applies To (JSON)" rows={2} value={ruleForm.appliesToJson ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, appliesToJson: e.target.value })} placeholder='"all" or ["nursing","medicine"]' />
                  <Textarea label="Exemplar Phrases (JSON array)" rows={2} value={ruleForm.exemplarPhrasesJson ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, exemplarPhrasesJson: e.target.value })} />
                  <Textarea label="Forbidden Patterns (JSON array)" rows={2} value={ruleForm.forbiddenPatternsJson ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, forbiddenPatternsJson: e.target.value })} />
                  <Textarea label="Params (JSON)" rows={2} value={ruleForm.paramsJson ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, paramsJson: e.target.value })} />
                  <Textarea label="Examples (JSON)" rows={2} value={ruleForm.examplesJson ?? ''} onChange={(e) => setRuleForm({ ...ruleForm, examplesJson: e.target.value })} />
                </div>
              </details>
            </div>
            <div className="flex justify-end gap-2 mt-6">
              <Button variant="outline" onClick={() => setRuleForm(null)}>Cancel</Button>
              <Button onClick={handleSaveRule}>{editingRuleId ? 'Save Changes' : 'Create Rule'}</Button>
            </div>
          </Card>
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
