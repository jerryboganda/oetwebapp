'use client';

import { useEffect, useMemo, useState } from 'react';
import { ClipboardList, Edit2, Plus } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  activateAdminSignupExamType,
  activateAdminSignupProfession,
  archiveAdminSignupExamType,
  archiveAdminSignupProfession,
  createAdminSignupExamType,
  createAdminSignupProfession,
  fetchAdminSignupCatalog,
  updateAdminSignupExamType,
  updateAdminSignupProfession,
  type AdminSignupExamTypePayload,
  type AdminSignupProfessionPayload,
} from '@/lib/api';
import { TARGET_COUNTRY_OPTIONS } from '@/lib/auth/target-countries';
import type { AdminSignupCatalogResponse, AdminSignupExamTypeCatalogItem, AdminSignupProfessionCatalogItem } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type CatalogTab = 'exam-types' | 'professions';

const targetCountryOptions = [...TARGET_COUNTRY_OPTIONS];

const blankExamForm: AdminSignupExamTypePayload = { id: '', code: '', label: '', description: '', sortOrder: 0, isActive: true };
const blankProfessionForm: AdminSignupProfessionPayload = { id: '', label: '', description: '', examTypeIds: [], countryTargets: targetCountryOptions, sortOrder: 0, isActive: true };

export default function AdminSignupCatalogPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [catalog, setCatalog] = useState<AdminSignupCatalogResponse>({ examTypes: [], professions: [] });
  const [activeTab, setActiveTab] = useState<CatalogTab>('exam-types');
  const [reloadNonce, setReloadNonce] = useState(0);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [examModalOpen, setExamModalOpen] = useState(false);
  const [professionModalOpen, setProfessionModalOpen] = useState(false);
  const [editingExamType, setEditingExamType] = useState<AdminSignupExamTypeCatalogItem | null>(null);
  const [editingProfession, setEditingProfession] = useState<AdminSignupProfessionCatalogItem | null>(null);
  const [examForm, setExamForm] = useState<AdminSignupExamTypePayload>(blankExamForm);
  const [professionForm, setProfessionForm] = useState<AdminSignupProfessionPayload>(blankProfessionForm);

  useEffect(() => {
    let cancelled = false;
    async function loadCatalog() {
      try {
        setPageStatus('loading');
        const response = await fetchAdminSignupCatalog();
        if (cancelled) return;
        const nextCatalog = normalizeCatalog(response);
        setCatalog(nextCatalog);
        setPageStatus(nextCatalog.examTypes.length || nextCatalog.professions.length ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load signup catalog.' });
        }
      }
    }
    void loadCatalog();
    return () => {
      cancelled = true;
    };
  }, [reloadNonce]);

  const examTypeLabelById = useMemo(
    () => new Map(catalog.examTypes.map((item) => [item.id, item.label])),
    [catalog.examTypes],
  );

  const examColumns: Column<AdminSignupExamTypeCatalogItem>[] = [
    { key: 'label', header: 'Exam Type', render: (row) => <span className="font-medium text-navy">{row.label}</span> },
    { key: 'code', header: 'Code', render: (row) => <span className="font-mono text-xs text-muted">{row.code}</span> },
    { key: 'sortOrder', header: 'Order', render: (row) => row.sortOrder },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.isActive ? 'success' : 'muted'}>{row.isActive ? 'active' : 'archived'}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => openExamEditor(row)} className="gap-2"><Edit2 className="h-4 w-4" /> Edit</Button>
          <Button variant="outline" size="sm" onClick={() => void toggleExamType(row)}>{row.isActive ? 'Archive' : 'Activate'}</Button>
        </div>
      ),
    },
  ];

  const professionColumns: Column<AdminSignupProfessionCatalogItem>[] = [
    { key: 'label', header: 'Profession', render: (row) => <span className="font-medium text-navy">{row.label}</span> },
    { key: 'id', header: 'ID', render: (row) => <span className="font-mono text-xs text-muted">{row.id}</span> },
    { key: 'examTypes', header: 'Exam Types', render: (row) => row.examTypeIds.map((id) => examTypeLabelById.get(id) ?? id).join(', ') || 'None' },
    { key: 'countries', header: 'Countries', render: (row) => row.countryTargets.length ? row.countryTargets.join(', ') : 'All' },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.isActive ? 'success' : 'muted'}>{row.isActive ? 'active' : 'archived'}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => openProfessionEditor(row)} className="gap-2"><Edit2 className="h-4 w-4" /> Edit</Button>
          <Button variant="outline" size="sm" onClick={() => void toggleProfession(row)}>{row.isActive ? 'Archive' : 'Activate'}</Button>
        </div>
      ),
    },
  ];

  function openExamEditor(row?: AdminSignupExamTypeCatalogItem) {
    setEditingExamType(row ?? null);
    setExamForm(row ? { ...row } : blankExamForm);
    setExamModalOpen(true);
  }

  function openProfessionEditor(row?: AdminSignupProfessionCatalogItem) {
    setEditingProfession(row ?? null);
    setProfessionForm(row ? { ...row } : { ...blankProfessionForm, examTypeIds: catalog.examTypes.filter((item) => item.isActive).map((item) => item.id).slice(0, 1) });
    setProfessionModalOpen(true);
  }

  async function submitExamType() {
    try {
      if (editingExamType) {
        await updateAdminSignupExamType(editingExamType.id, examForm);
      } else {
        await createAdminSignupExamType(examForm);
      }
      setToast({ variant: 'success', message: `${examForm.label} saved.` });
      setExamModalOpen(false);
      setReloadNonce((current) => current + 1);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save exam type.' });
    }
  }

  async function submitProfession() {
    try {
      if (editingProfession) {
        await updateAdminSignupProfession(editingProfession.id, professionForm);
      } else {
        await createAdminSignupProfession(professionForm);
      }
      setToast({ variant: 'success', message: `${professionForm.label} saved.` });
      setProfessionModalOpen(false);
      setReloadNonce((current) => current + 1);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save profession.' });
    }
  }

  async function toggleExamType(row: AdminSignupExamTypeCatalogItem) {
    await (row.isActive ? archiveAdminSignupExamType(row.id) : activateAdminSignupExamType(row.id));
    setToast({ variant: 'success', message: `${row.label} ${row.isActive ? 'archived' : 'activated'}.` });
    setReloadNonce((current) => current + 1);
  }

  async function toggleProfession(row: AdminSignupProfessionCatalogItem) {
    await (row.isActive ? archiveAdminSignupProfession(row.id) : activateAdminSignupProfession(row.id));
    setToast({ variant: 'success', message: `${row.label} ${row.isActive ? 'archived' : 'activated'}.` });
    setReloadNonce((current) => current + 1);
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Signup catalog">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
      <AdminRouteSectionHeader
        title="Signup Catalog"
        description="Manage the exam types and professions shown on learner registration. Changes are served from the backend catalog immediately."
        icon={ClipboardList}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant={activeTab === 'exam-types' ? 'primary' : 'outline'} onClick={() => setActiveTab('exam-types')}>Exam Types</Button>
            <Button variant={activeTab === 'professions' ? 'primary' : 'outline'} onClick={() => setActiveTab('professions')}>Professions</Button>
          </div>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setReloadNonce((current) => current + 1)}
        emptyContent={<EmptyState icon={<ClipboardList className="h-10 w-10 text-muted" />} title="No signup catalog entries" description="Add exam types and professions to power registration." />}
      >
        {activeTab === 'exam-types' ? (
          <AdminRoutePanel
            title="Exam Types"
            description="These values populate the Exam Type field during account registration."
            actions={<Button onClick={() => openExamEditor()} className="gap-2"><Plus className="h-4 w-4" /> Add Exam Type</Button>}
          >
            <DataTable columns={examColumns} data={catalog.examTypes} keyExtractor={(row) => row.id} />
          </AdminRoutePanel>
        ) : (
          <AdminRoutePanel
            title="Professions"
            description="These values populate the Current Profession field and can be limited by exam type and target country."
            actions={<Button onClick={() => openProfessionEditor()} className="gap-2"><Plus className="h-4 w-4" /> Add Profession</Button>}
          >
            <DataTable columns={professionColumns} data={catalog.professions} keyExtractor={(row) => row.id} />
          </AdminRoutePanel>
        )}
      </AsyncStateWrapper>

      <Modal open={examModalOpen} onClose={() => setExamModalOpen(false)} title={editingExamType ? `Edit ${editingExamType.label}` : 'Add Exam Type'}>
        <div className="space-y-4">
          <Input label="ID" value={examForm.id} disabled={Boolean(editingExamType)} onChange={(event) => setExamForm((current) => ({ ...current, id: event.target.value }))} hint="Lowercase ID used by registration, for example oet." />
          <Input label="Code" value={examForm.code} onChange={(event) => setExamForm((current) => ({ ...current, code: event.target.value }))} />
          <Input label="Label" value={examForm.label} onChange={(event) => setExamForm((current) => ({ ...current, label: event.target.value }))} />
          <Input label="Sort Order" type="number" value={examForm.sortOrder ?? 0} onChange={(event) => setExamForm((current) => ({ ...current, sortOrder: Number(event.target.value) }))} />
          <Textarea label="Description" value={examForm.description ?? ''} onChange={(event) => setExamForm((current) => ({ ...current, description: event.target.value }))} />
          <label className="flex items-center gap-2 text-sm font-semibold text-navy"><input type="checkbox" checked={examForm.isActive ?? true} onChange={(event) => setExamForm((current) => ({ ...current, isActive: event.target.checked }))} /> Active</label>
          <ModalActions onCancel={() => setExamModalOpen(false)} onSave={() => void submitExamType()} saveLabel={editingExamType ? 'Save Changes' : 'Create Exam Type'} />
        </div>
      </Modal>

      <Modal open={professionModalOpen} onClose={() => setProfessionModalOpen(false)} title={editingProfession ? `Edit ${editingProfession.label}` : 'Add Profession'}>
        <div className="space-y-4">
          <Input label="ID" value={professionForm.id} disabled={Boolean(editingProfession)} onChange={(event) => setProfessionForm((current) => ({ ...current, id: event.target.value }))} hint="Lowercase ID used by registration, for example nursing." />
          <Input label="Label" value={professionForm.label} onChange={(event) => setProfessionForm((current) => ({ ...current, label: event.target.value }))} />
          <Input label="Sort Order" type="number" value={professionForm.sortOrder ?? 0} onChange={(event) => setProfessionForm((current) => ({ ...current, sortOrder: Number(event.target.value) }))} />
          <Textarea label="Description" value={professionForm.description ?? ''} onChange={(event) => setProfessionForm((current) => ({ ...current, description: event.target.value }))} />
          <CheckboxGroup label="Available Exam Types" values={professionForm.examTypeIds ?? []} options={catalog.examTypes.map((item) => ({ value: item.id, label: item.label }))} onChange={(values) => setProfessionForm((current) => ({ ...current, examTypeIds: values }))} />
          <CheckboxGroup label="Target Countries" values={professionForm.countryTargets ?? []} options={targetCountryOptions.map((item) => ({ value: item, label: item }))} onChange={(values) => setProfessionForm((current) => ({ ...current, countryTargets: values }))} />
          <label className="flex items-center gap-2 text-sm font-semibold text-navy"><input type="checkbox" checked={professionForm.isActive ?? true} onChange={(event) => setProfessionForm((current) => ({ ...current, isActive: event.target.checked }))} /> Active</label>
          <ModalActions onCancel={() => setProfessionModalOpen(false)} onSave={() => void submitProfession()} saveLabel={editingProfession ? 'Save Changes' : 'Create Profession'} />
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}

function CheckboxGroup({ label, values, options, onChange }: { label: string; values: string[]; options: { value: string; label: string }[]; onChange: (values: string[]) => void }) {
  return (
    <fieldset className="space-y-2 rounded-2xl border border-gray-200 bg-background-light p-4">
      <legend className="px-1 text-sm font-semibold text-navy">{label}</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {options.map((option) => {
          const checked = values.includes(option.value);
          return (
            <label key={option.value} className="flex items-center gap-2 rounded-xl bg-surface px-3 py-2 text-sm text-navy">
              <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked ? [...values, option.value] : values.filter((value) => value !== option.value))} />
              {option.label}
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}

function ModalActions({ onCancel, onSave, saveLabel }: { onCancel: () => void; onSave: () => void; saveLabel: string }) {
  return (
    <div className="flex justify-end gap-3 border-t border-border pt-4">
      <Button variant="outline" onClick={onCancel}>Cancel</Button>
      <Button onClick={onSave}>{saveLabel}</Button>
    </div>
  );
}

function normalizeCatalog(value: unknown): AdminSignupCatalogResponse {
  const record = value && typeof value === 'object' ? value as Record<string, unknown> : {};
  return {
    examTypes: Array.isArray(record.examTypes) ? record.examTypes.map(normalizeExamType) : [],
    professions: Array.isArray(record.professions) ? record.professions.map(normalizeProfession) : [],
  };
}

function normalizeExamType(value: unknown): AdminSignupExamTypeCatalogItem {
  const record = value && typeof value === 'object' ? value as Record<string, unknown> : {};
  return {
    id: String(record.id ?? ''),
    code: String(record.code ?? ''),
    label: String(record.label ?? ''),
    description: String(record.description ?? ''),
    sortOrder: Number(record.sortOrder ?? 0),
    isActive: Boolean(record.isActive),
  };
}

function normalizeProfession(value: unknown): AdminSignupProfessionCatalogItem {
  const record = value && typeof value === 'object' ? value as Record<string, unknown> : {};
  return {
    id: String(record.id ?? ''),
    label: String(record.label ?? ''),
    description: String(record.description ?? ''),
    examTypeIds: Array.isArray(record.examTypeIds) ? record.examTypeIds.map(String) : [],
    countryTargets: Array.isArray(record.countryTargets) ? record.countryTargets.map(String) : [],
    sortOrder: Number(record.sortOrder ?? 0),
    isActive: Boolean(record.isActive),
  };
}