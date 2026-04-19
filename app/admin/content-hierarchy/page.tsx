'use client';

import { useEffect, useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace, AdminTableCellLink } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { SegmentedControl } from '@/components/ui/segmented-control';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminContentPrograms,
  fetchAdminContentPackages,
  createAdminProgram,
  updateAdminProgram,
  fetchAdminTracks,
  createAdminTrack,
  updateAdminTrack,
  fetchAdminModules,
  createAdminModule,
  updateAdminModule,
  fetchAdminLessons,
  createAdminLesson,
  createAdminPackage,
  updateAdminPackage,
} from '@/lib/api';
import type {
  ContentProgram,
  ContentTrack,
  ContentModule,
  ContentLesson,
  ContentPackage,
  ContentStatus,
  ProgramType,
  PackageType,
  LessonType,
} from '@/lib/types/content-hierarchy';
import { BookOpen, ChevronRight, GitBranch, Layers, Package, Plus, RefreshCw } from 'lucide-react';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type EntityTab = 'programs' | 'packages';

// ── Form state types ──

interface ProgramFormState {
  code: string;
  title: string;
  description: string;
  professionId: string;
  instructionLanguage: string;
  programType: ProgramType;
  status: ContentStatus;
  displayOrder: string;
  estimatedDurationMinutes: string;
}

interface TrackFormState {
  programId: string;
  subtestCode: string;
  title: string;
  description: string;
  displayOrder: string;
  status: ContentStatus;
}

interface ModuleFormState {
  trackId: string;
  title: string;
  description: string;
  displayOrder: string;
  estimatedDurationMinutes: string;
  status: ContentStatus;
}

interface LessonFormState {
  moduleId: string;
  title: string;
  lessonType: LessonType;
  displayOrder: string;
  status: ContentStatus;
}

interface PackageFormState {
  code: string;
  title: string;
  description: string;
  packageType: PackageType;
  professionId: string;
  instructionLanguage: string;
  status: ContentStatus;
  displayOrder: string;
  comparisonFeaturesText: string;
}

// ── Defaults ──

const defaultProgramForm: ProgramFormState = {
  code: '', title: '', description: '', professionId: '', instructionLanguage: 'en',
  programType: 'full_course', status: 'Draft', displayOrder: '0', estimatedDurationMinutes: '0',
};

const defaultTrackForm: TrackFormState = {
  programId: '', subtestCode: '', title: '', description: '', displayOrder: '0', status: 'Draft',
};

const defaultModuleForm: ModuleFormState = {
  trackId: '', title: '', description: '', displayOrder: '0', estimatedDurationMinutes: '0', status: 'Draft',
};

const defaultLessonForm: LessonFormState = {
  moduleId: '', title: '', lessonType: 'video_lesson', displayOrder: '0', status: 'Draft',
};

const defaultPackageForm: PackageFormState = {
  code: '', title: '', description: '', packageType: 'full_course', professionId: '',
  instructionLanguage: 'en', status: 'Draft', displayOrder: '0', comparisonFeaturesText: '',
};

const statusOptions = [
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const programTypeOptions = [
  { value: 'full_course', label: 'Full Course' },
  { value: 'crash_course', label: 'Crash Course' },
  { value: 'foundation', label: 'Foundation' },
  { value: 'combo', label: 'Combo' },
];

const packageTypeOptions = [
  { value: 'full_course', label: 'Full Course' },
  { value: 'crash_course', label: 'Crash Course' },
  { value: 'combo', label: 'Combo' },
  { value: 'foundation', label: 'Foundation' },
  { value: 'standalone', label: 'Standalone' },
];

const lessonTypeOptions = [
  { value: 'video_lesson', label: 'Video Lesson' },
  { value: 'strategy_guide', label: 'Strategy Guide' },
  { value: 'session_replay', label: 'Session Replay' },
  { value: 'reading_material', label: 'Reading Material' },
  { value: 'practice_task', label: 'Practice Task' },
];

function toNumber(value: string, fallback = 0): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function splitList(value: string): string[] {
  return value.split(',').map((s) => s.trim()).filter(Boolean);
}

function toProgramForm(p: ContentProgram): ProgramFormState {
  return {
    code: p.code, title: p.title, description: p.description ?? '', professionId: p.professionId ?? '',
    instructionLanguage: p.instructionLanguage, programType: p.programType, status: p.status,
    displayOrder: String(p.displayOrder), estimatedDurationMinutes: String(p.estimatedDurationMinutes),
  };
}

function toTrackForm(t: ContentTrack): TrackFormState {
  return {
    programId: t.programId, subtestCode: t.subtestCode ?? '', title: t.title,
    description: t.description ?? '', displayOrder: String(t.displayOrder), status: t.status,
  };
}

function toModuleForm(m: ContentModule): ModuleFormState {
  return {
    trackId: m.trackId, title: m.title, description: m.description ?? '',
    displayOrder: String(m.displayOrder), estimatedDurationMinutes: String(m.estimatedDurationMinutes), status: m.status,
  };
}

function toLessonForm(l: ContentLesson): LessonFormState {
  return {
    moduleId: l.moduleId, title: l.title, lessonType: l.lessonType,
    displayOrder: String(l.displayOrder), status: l.status,
  };
}

function toPackageForm(p: ContentPackage): PackageFormState {
  return {
    code: p.code, title: p.title, description: p.description ?? '', packageType: p.packageType,
    professionId: p.professionId ?? '', instructionLanguage: p.instructionLanguage, status: p.status,
    displayOrder: String(p.displayOrder), comparisonFeaturesText: (p.comparisonFeatures ?? []).join(', '),
  };
}

export default function AdminContentHierarchyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [tab, setTab] = useState<EntityTab>('programs');
  const [reloadNonce, setReloadNonce] = useState(0);
  const [toast, setToast] = useState<ToastState>(null);

  // ── Data ──
  const [programs, setPrograms] = useState<ContentProgram[]>([]);
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [tracks, setTracks] = useState<ContentTrack[]>([]);
  const [modules, setModules] = useState<ContentModule[]>([]);
  const [lessons, setLessons] = useState<ContentLesson[]>([]);

  // ── Drill-down selection ──
  const [selectedProgramId, setSelectedProgramId] = useState<string | null>(null);
  const [selectedTrackId, setSelectedTrackId] = useState<string | null>(null);
  const [selectedModuleId, setSelectedModuleId] = useState<string | null>(null);

  // ── Modal state ──
  const [isProgramModalOpen, setIsProgramModalOpen] = useState(false);
  const [isTrackModalOpen, setIsTrackModalOpen] = useState(false);
  const [isModuleModalOpen, setIsModuleModalOpen] = useState(false);
  const [isLessonModalOpen, setIsLessonModalOpen] = useState(false);
  const [isPackageModalOpen, setIsPackageModalOpen] = useState(false);
  const [isArchiveConfirmOpen, setIsArchiveConfirmOpen] = useState(false);

  // ── Form state ──
  const [programForm, setProgramForm] = useState<ProgramFormState>(defaultProgramForm);
  const [trackForm, setTrackForm] = useState<TrackFormState>(defaultTrackForm);
  const [moduleForm, setModuleForm] = useState<ModuleFormState>(defaultModuleForm);
  const [lessonForm, setLessonForm] = useState<LessonFormState>(defaultLessonForm);
  const [packageForm, setPackageForm] = useState<PackageFormState>(defaultPackageForm);

  // ── Edit IDs ──
  const [editingProgramId, setEditingProgramId] = useState<string | null>(null);
  const [editingTrackId, setEditingTrackId] = useState<string | null>(null);
  const [editingModuleId, setEditingModuleId] = useState<string | null>(null);
  const [editingPackageId, setEditingPackageId] = useState<string | null>(null);

  // ── Saving flags ──
  const [isSaving, setIsSaving] = useState(false);

  // ── Archive confirm state ──
  const [archiveTarget, setArchiveTarget] = useState<{ type: string; id: string; title: string } | null>(null);

  // ── Load programs and packages ──
  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [progRes, pkgRes] = await Promise.all([
          fetchAdminContentPrograms({ pageSize: 100 }),
          fetchAdminContentPackages({ pageSize: 100 }),
        ]);
        if (cancelled) return;
        const progs = progRes?.items ?? progRes ?? [];
        const pkgs = pkgRes?.items ?? pkgRes ?? [];
        setPrograms(Array.isArray(progs) ? progs : []);
        setPackages(Array.isArray(pkgs) ? pkgs : []);
        setPageStatus(progs.length === 0 && pkgs.length === 0 ? 'empty' : 'success');
      } catch {
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Failed to load content hierarchy.' });
        }
      }
    }
    if (isAuthenticated) void load();
    return () => { cancelled = true; };
  }, [isAuthenticated, reloadNonce]);

  // ── Load tracks when program selected ──
  useEffect(() => {
    if (!selectedProgramId) { setTracks([]); return; }
    let cancelled = false;
    async function loadTracks() {
      try {
        const res = await fetchAdminTracks(selectedProgramId!);
        if (!cancelled) setTracks(Array.isArray(res) ? res : res?.items ?? []);
      } catch {
        if (!cancelled) setToast({ variant: 'error', message: 'Failed to load tracks.' });
      }
    }
    void loadTracks();
    return () => { cancelled = true; };
  }, [selectedProgramId, reloadNonce]);

  // ── Load modules when track selected ──
  useEffect(() => {
    if (!selectedTrackId) { setModules([]); return; }
    let cancelled = false;
    async function loadModules() {
      try {
        const res = await fetchAdminModules(selectedTrackId!);
        if (!cancelled) setModules(Array.isArray(res) ? res : res?.items ?? []);
      } catch {
        if (!cancelled) setToast({ variant: 'error', message: 'Failed to load modules.' });
      }
    }
    void loadModules();
    return () => { cancelled = true; };
  }, [selectedTrackId, reloadNonce]);

  // ── Load lessons when module selected ──
  useEffect(() => {
    if (!selectedModuleId) { setLessons([]); return; }
    let cancelled = false;
    async function loadLessons() {
      try {
        const res = await fetchAdminLessons(selectedModuleId!);
        if (!cancelled) setLessons(Array.isArray(res) ? res : res?.items ?? []);
      } catch {
        if (!cancelled) setToast({ variant: 'error', message: 'Failed to load lessons.' });
      }
    }
    void loadLessons();
    return () => { cancelled = true; };
  }, [selectedModuleId, reloadNonce]);

  // ── Editor openers ──
  function openProgramEditor(program?: ContentProgram) {
    setEditingProgramId(program?.id ?? null);
    setProgramForm(program ? toProgramForm(program) : defaultProgramForm);
    setIsProgramModalOpen(true);
  }

  function openTrackEditor(track?: ContentTrack, parentProgramId?: string) {
    setEditingTrackId(track?.id ?? null);
    if (track) {
      setTrackForm(toTrackForm(track));
    } else {
      setTrackForm({ ...defaultTrackForm, programId: parentProgramId ?? selectedProgramId ?? '' });
    }
    setIsTrackModalOpen(true);
  }

  function openModuleEditor(module?: ContentModule, parentTrackId?: string) {
    setEditingModuleId(module?.id ?? null);
    if (module) {
      setModuleForm(toModuleForm(module));
    } else {
      setModuleForm({ ...defaultModuleForm, trackId: parentTrackId ?? selectedTrackId ?? '' });
    }
    setIsModuleModalOpen(true);
  }

  function openLessonEditor(parentModuleId?: string) {
    setLessonForm({ ...defaultLessonForm, moduleId: parentModuleId ?? selectedModuleId ?? '' });
    setIsLessonModalOpen(true);
  }

  function openPackageEditor(pkg?: ContentPackage) {
    setEditingPackageId(pkg?.id ?? null);
    setPackageForm(pkg ? toPackageForm(pkg) : defaultPackageForm);
    setIsPackageModalOpen(true);
  }

  function openArchiveConfirm(type: string, id: string, title: string) {
    setArchiveTarget({ type, id, title });
    setIsArchiveConfirmOpen(true);
  }

  // ── Save handlers ──
  async function handleSaveProgram() {
    if (!programForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    setIsSaving(true);
    try {
      const payload = {
        code: programForm.code || programForm.title.toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, 64),
        title: programForm.title,
        description: programForm.description || null,
        professionId: programForm.professionId || null,
        instructionLanguage: programForm.instructionLanguage,
        programType: programForm.programType,
        status: programForm.status,
        displayOrder: toNumber(programForm.displayOrder),
        estimatedDurationMinutes: toNumber(programForm.estimatedDurationMinutes),
      };
      if (editingProgramId) {
        await updateAdminProgram(editingProgramId, payload);
      } else {
        await createAdminProgram(payload);
      }
      setIsProgramModalOpen(false);
      setProgramForm(defaultProgramForm);
      setEditingProgramId(null);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: editingProgramId ? 'Program updated.' : 'Program created.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save program.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSaveTrack() {
    if (!trackForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!trackForm.programId) {
      setToast({ variant: 'error', message: 'Parent program is required.' });
      return;
    }
    setIsSaving(true);
    try {
      const payload = {
        programId: trackForm.programId,
        subtestCode: trackForm.subtestCode || null,
        title: trackForm.title,
        description: trackForm.description || null,
        displayOrder: toNumber(trackForm.displayOrder),
        status: trackForm.status,
      };
      if (editingTrackId) {
        await updateAdminTrack(editingTrackId, payload);
      } else {
        await createAdminTrack(payload);
      }
      setIsTrackModalOpen(false);
      setTrackForm(defaultTrackForm);
      setEditingTrackId(null);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: editingTrackId ? 'Track updated.' : 'Track created.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save track.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSaveModule() {
    if (!moduleForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!moduleForm.trackId) {
      setToast({ variant: 'error', message: 'Parent track is required.' });
      return;
    }
    setIsSaving(true);
    try {
      const payload = {
        trackId: moduleForm.trackId,
        title: moduleForm.title,
        description: moduleForm.description || null,
        displayOrder: toNumber(moduleForm.displayOrder),
        estimatedDurationMinutes: toNumber(moduleForm.estimatedDurationMinutes),
        status: moduleForm.status,
      };
      if (editingModuleId) {
        await updateAdminModule(editingModuleId, payload);
      } else {
        await createAdminModule(payload);
      }
      setIsModuleModalOpen(false);
      setModuleForm(defaultModuleForm);
      setEditingModuleId(null);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: editingModuleId ? 'Module updated.' : 'Module created.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save module.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSaveLesson() {
    if (!lessonForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!lessonForm.moduleId) {
      setToast({ variant: 'error', message: 'Parent module is required.' });
      return;
    }
    setIsSaving(true);
    try {
      const payload = {
        moduleId: lessonForm.moduleId,
        title: lessonForm.title,
        lessonType: lessonForm.lessonType,
        displayOrder: toNumber(lessonForm.displayOrder),
        status: lessonForm.status,
      };
      await createAdminLesson(payload);
      setIsLessonModalOpen(false);
      setLessonForm(defaultLessonForm);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: 'Lesson created.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save lesson.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSavePackage() {
    if (!packageForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    setIsSaving(true);
    try {
      const payload = {
        code: packageForm.code || packageForm.title.toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, 64),
        title: packageForm.title,
        description: packageForm.description || null,
        packageType: packageForm.packageType,
        professionId: packageForm.professionId || null,
        instructionLanguage: packageForm.instructionLanguage,
        status: packageForm.status,
        displayOrder: toNumber(packageForm.displayOrder),
        comparisonFeaturesJson: JSON.stringify(splitList(packageForm.comparisonFeaturesText)),
      };
      if (editingPackageId) {
        await updateAdminPackage(editingPackageId, payload);
      } else {
        await createAdminPackage(payload);
      }
      setIsPackageModalOpen(false);
      setPackageForm(defaultPackageForm);
      setEditingPackageId(null);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: editingPackageId ? 'Package updated.' : 'Package created.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save package.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleArchive() {
    if (!archiveTarget) return;
    setIsSaving(true);
    try {
      const { type, id } = archiveTarget;
      if (type === 'program') await updateAdminProgram(id, { status: 'Archived' });
      else if (type === 'track') await updateAdminTrack(id, { status: 'Archived' });
      else if (type === 'module') await updateAdminModule(id, { status: 'Archived' });
      else if (type === 'package') await updateAdminPackage(id, { status: 'Archived' });
      setIsArchiveConfirmOpen(false);
      setArchiveTarget(null);
      setReloadNonce((n) => n + 1);
      setToast({ variant: 'success', message: `${type.charAt(0).toUpperCase() + type.slice(1)} archived.` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to archive.' });
    } finally {
      setIsSaving(false);
    }
  }

  // ── Column definitions ──
  const programColumns: Column<ContentProgram>[] = [
    {
      key: 'title', header: 'Title',
      render: (row) => (
        <AdminTableCellLink onClick={() => {
          setSelectedProgramId(row.id);
          setSelectedTrackId(null);
          setSelectedModuleId(null);
        }}>
          {row.title}
        </AdminTableCellLink>
      ),
    },
    { key: 'programType', header: 'Type', render: (row) => <Badge variant="info">{row.programType}</Badge> },
    { key: 'instructionLanguage', header: 'Language', render: (row) => row.instructionLanguage },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'danger' : 'muted'}>{row.status}</Badge> },
    { key: 'estimatedDurationMinutes', header: 'Duration (min)', render: (row) => row.estimatedDurationMinutes },
    {
      key: 'actions', header: '',
      render: (row) => (
        <div className="flex gap-1">
          <Button variant="outline" size="sm" onClick={() => openProgramEditor(row)}>Edit</Button>
          {row.status !== 'Archived' && (
            <Button variant="outline" size="sm" onClick={() => openArchiveConfirm('program', row.id, row.title)}>Archive</Button>
          )}
        </div>
      ),
    },
  ];

  const trackColumns: Column<ContentTrack>[] = [
    {
      key: 'title', header: 'Title',
      render: (row) => (
        <AdminTableCellLink onClick={() => {
          setSelectedTrackId(row.id);
          setSelectedModuleId(null);
        }}>
          {row.title}
        </AdminTableCellLink>
      ),
    },
    { key: 'subtestCode', header: 'Subtest', render: (row) => row.subtestCode ?? '—' },
    { key: 'displayOrder', header: 'Order', render: (row) => row.displayOrder },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'danger' : 'muted'}>{row.status}</Badge> },
    {
      key: 'actions', header: '',
      render: (row) => (
        <div className="flex gap-1">
          <Button variant="outline" size="sm" onClick={() => openTrackEditor(row)}>Edit</Button>
          {row.status !== 'Archived' && (
            <Button variant="outline" size="sm" onClick={() => openArchiveConfirm('track', row.id, row.title)}>Archive</Button>
          )}
        </div>
      ),
    },
  ];

  const moduleColumns: Column<ContentModule>[] = [
    {
      key: 'title', header: 'Title',
      render: (row) => (
        <AdminTableCellLink onClick={() => setSelectedModuleId(row.id)}>
          {row.title}
        </AdminTableCellLink>
      ),
    },
    { key: 'displayOrder', header: 'Order', render: (row) => row.displayOrder },
    { key: 'estimatedDurationMinutes', header: 'Duration (min)', render: (row) => row.estimatedDurationMinutes },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'danger' : 'muted'}>{row.status}</Badge> },
    {
      key: 'actions', header: '',
      render: (row) => (
        <div className="flex gap-1">
          <Button variant="outline" size="sm" onClick={() => openModuleEditor(row)}>Edit</Button>
          {row.status !== 'Archived' && (
            <Button variant="outline" size="sm" onClick={() => openArchiveConfirm('module', row.id, row.title)}>Archive</Button>
          )}
        </div>
      ),
    },
  ];

  const lessonColumns: Column<ContentLesson>[] = [
    { key: 'title', header: 'Title', render: (row) => <span className="font-medium">{row.title}</span> },
    { key: 'lessonType', header: 'Type', render: (row) => <Badge variant="info">{row.lessonType}</Badge> },
    { key: 'displayOrder', header: 'Order', render: (row) => row.displayOrder },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'danger' : 'muted'}>{row.status}</Badge> },
  ];

  const packageColumns: Column<ContentPackage>[] = [
    { key: 'title', header: 'Title', render: (row) => <span className="font-medium">{row.title}</span> },
    { key: 'code', header: 'Code', render: (row) => <span className="text-xs uppercase tracking-wide text-muted">{row.code}</span> },
    { key: 'packageType', header: 'Type', render: (row) => <Badge variant="info">{row.packageType}</Badge> },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'danger' : 'muted'}>{row.status}</Badge> },
    { key: 'displayOrder', header: 'Order', render: (row) => row.displayOrder },
    {
      key: 'actions', header: '',
      render: (row) => (
        <div className="flex gap-1">
          <Button variant="outline" size="sm" onClick={() => openPackageEditor(row)}>Edit</Button>
          {row.status !== 'Archived' && (
            <Button variant="outline" size="sm" onClick={() => openArchiveConfirm('package', row.id, row.title)}>Archive</Button>
          )}
        </div>
      ),
    },
  ];

  // ── Breadcrumb helper ──
  const selectedProgram = programs.find((p) => p.id === selectedProgramId);
  const selectedTrack = tracks.find((t) => t.id === selectedTrackId);
  const selectedModule = modules.find((m) => m.id === selectedModuleId);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Content hierarchy management">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Content Hierarchy"
        description="Manage programs, tracks, modules, lessons, and packages"
        icon={GitBranch}
        actions={
          <div className="flex gap-2">
            <Button onClick={() => tab === 'programs' ? openProgramEditor() : openPackageEditor()} className="gap-2">
              <Plus className="h-4 w-4" />
              {tab === 'programs' ? 'New Program' : 'New Package'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setReloadNonce((n) => n + 1)}>
              <RefreshCw className="h-4 w-4" /> Refresh
            </Button>
          </div>
        }
      />

      {/* Tab selector */}
      <div className="mb-4">
        <SegmentedControl
          value={tab}
          onChange={(next) => {
            setTab(next as 'programs' | 'packages');
            setSelectedProgramId(null);
            setSelectedTrackId(null);
            setSelectedModuleId(null);
          }}
          namespace="admin-content-hierarchy"
          options={[
            { value: 'programs', label: `Programs (${programs.length})` },
            { value: 'packages', label: `Packages (${packages.length})` },
          ]}
          aria-label="Content hierarchy view"
        />
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        errorMessage="Failed to load content hierarchy"
        onRetry={() => setReloadNonce((n) => n + 1)}
        emptyContent={
          <EmptyState
            title="No content hierarchy yet"
            description="Create a program or package to get started."
            icon={<GitBranch className="w-8 h-8 text-muted" />}
            action={{ label: 'Create Program', onClick: () => openProgramEditor() }}
          />
        }
      >
        {tab === 'programs' ? (
          <>
            {/* Breadcrumb */}
            {selectedProgramId && (
              <div className="mb-4 flex flex-wrap items-center gap-1 text-sm text-muted">
                <AdminTableCellLink muted onClick={() => { setSelectedProgramId(null); setSelectedTrackId(null); setSelectedModuleId(null); }}>
                  Programs
                </AdminTableCellLink>
                {selectedProgram && (
                  <>
                    <ChevronRight className="h-3 w-3" aria-hidden />
                    <AdminTableCellLink muted onClick={() => { setSelectedTrackId(null); setSelectedModuleId(null); }}>
                      {selectedProgram.title}
                    </AdminTableCellLink>
                  </>
                )}
                {selectedTrack && (
                  <>
                    <ChevronRight className="h-3 w-3" aria-hidden />
                    <AdminTableCellLink muted onClick={() => setSelectedModuleId(null)}>
                      {selectedTrack.title}
                    </AdminTableCellLink>
                  </>
                )}
                {selectedModule && (
                  <>
                    <ChevronRight className="h-3 w-3" aria-hidden />
                    <span className="text-navy">{selectedModule.title}</span>
                  </>
                )}
              </div>
            )}

            {/* Program list (no drill-down) */}
            {!selectedProgramId && (
              <AdminRoutePanel title="Programs" actions={<Button variant="outline" size="sm" onClick={() => openProgramEditor()}>Create Program</Button>}>
                <DataTable columns={programColumns} data={programs} keyExtractor={(row) => row.id} />
              </AdminRoutePanel>
            )}

            {/* Tracks (program selected) */}
            {selectedProgramId && !selectedTrackId && (
              <AdminRoutePanel
                title={`Tracks in "${selectedProgram?.title ?? ''}"`}
                actions={<Button variant="outline" size="sm" onClick={() => openTrackEditor(undefined, selectedProgramId)}>Create Track</Button>}
              >
                {tracks.length === 0 ? (
                  <EmptyState title="No tracks" description="Create a track for this program." icon={<Layers className="w-8 h-8 text-muted" />} />
                ) : (
                  <DataTable columns={trackColumns} data={tracks} keyExtractor={(row) => row.id} />
                )}
              </AdminRoutePanel>
            )}

            {/* Modules (track selected) */}
            {selectedTrackId && !selectedModuleId && (
              <AdminRoutePanel
                title={`Modules in "${selectedTrack?.title ?? ''}"`}
                actions={<Button variant="outline" size="sm" onClick={() => openModuleEditor(undefined, selectedTrackId)}>Create Module</Button>}
              >
                {modules.length === 0 ? (
                  <EmptyState title="No modules" description="Create a module for this track." icon={<Layers className="w-8 h-8 text-muted" />} />
                ) : (
                  <DataTable columns={moduleColumns} data={modules} keyExtractor={(row) => row.id} />
                )}
              </AdminRoutePanel>
            )}

            {/* Lessons (module selected) */}
            {selectedModuleId && (
              <AdminRoutePanel
                title={`Lessons in "${selectedModule?.title ?? ''}"`}
                actions={<Button variant="outline" size="sm" onClick={() => openLessonEditor(selectedModuleId)}>Create Lesson</Button>}
              >
                {lessons.length === 0 ? (
                  <EmptyState title="No lessons" description="Create a lesson for this module." icon={<BookOpen className="w-8 h-8 text-muted" />} />
                ) : (
                  <DataTable columns={lessonColumns} data={lessons} keyExtractor={(row) => row.id} />
                )}
              </AdminRoutePanel>
            )}
          </>
        ) : (
          <AdminRoutePanel title="Packages" actions={<Button variant="outline" size="sm" onClick={() => openPackageEditor()}>Create Package</Button>}>
            <DataTable columns={packageColumns} data={packages} keyExtractor={(row) => row.id} />
          </AdminRoutePanel>
        )}
      </AsyncStateWrapper>

      {/* ── Program Modal ── */}
      <Modal
        open={isProgramModalOpen}
        onClose={() => { setIsProgramModalOpen(false); setEditingProgramId(null); setProgramForm(defaultProgramForm); }}
        title={editingProgramId ? 'Edit Program' : 'Create Program'}
      >
        <div className="space-y-4 py-2">
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Code" value={programForm.code} onChange={(e) => setProgramForm((c) => ({ ...c, code: e.target.value }))} hint="Auto-generated from title if empty" />
            <Input label="Title *" value={programForm.title} onChange={(e) => setProgramForm((c) => ({ ...c, title: e.target.value }))} />
          </div>
          <Textarea label="Description" value={programForm.description} onChange={(e) => setProgramForm((c) => ({ ...c, description: e.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Select label="Type" value={programForm.programType} onChange={(e) => setProgramForm((c) => ({ ...c, programType: e.target.value as ProgramType }))} options={programTypeOptions} />
            <Input label="Language" value={programForm.instructionLanguage} onChange={(e) => setProgramForm((c) => ({ ...c, instructionLanguage: e.target.value }))} hint="en, ar, ar+en" />
            <Input label="Profession ID" value={programForm.professionId} onChange={(e) => setProgramForm((c) => ({ ...c, professionId: e.target.value }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-3">
            <Select label="Status" value={programForm.status} onChange={(e) => setProgramForm((c) => ({ ...c, status: e.target.value as ContentStatus }))} options={statusOptions} />
            <Input label="Display Order" type="number" min={0} value={programForm.displayOrder} onChange={(e) => setProgramForm((c) => ({ ...c, displayOrder: e.target.value }))} />
            <Input label="Est. Duration (min)" type="number" min={0} value={programForm.estimatedDurationMinutes} onChange={(e) => setProgramForm((c) => ({ ...c, estimatedDurationMinutes: e.target.value }))} />
          </div>
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsProgramModalOpen(false); setEditingProgramId(null); setProgramForm(defaultProgramForm); }}>Cancel</Button>
            <Button onClick={handleSaveProgram} loading={isSaving}>{editingProgramId ? 'Update Program' : 'Save Program'}</Button>
          </div>
        </div>
      </Modal>

      {/* ── Track Modal ── */}
      <Modal
        open={isTrackModalOpen}
        onClose={() => { setIsTrackModalOpen(false); setEditingTrackId(null); setTrackForm(defaultTrackForm); }}
        title={editingTrackId ? 'Edit Track' : 'Create Track'}
      >
        <div className="space-y-4 py-2">
          <Select
            label="Parent Program *"
            value={trackForm.programId}
            onChange={(e) => setTrackForm((c) => ({ ...c, programId: e.target.value }))}
            options={programs.map((p) => ({ value: p.id, label: p.title }))}
          />
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Title *" value={trackForm.title} onChange={(e) => setTrackForm((c) => ({ ...c, title: e.target.value }))} />
            <Input label="Subtest Code" value={trackForm.subtestCode} onChange={(e) => setTrackForm((c) => ({ ...c, subtestCode: e.target.value }))} hint="writing, speaking, etc." />
          </div>
          <Textarea label="Description" value={trackForm.description} onChange={(e) => setTrackForm((c) => ({ ...c, description: e.target.value }))} />
          <div className="grid gap-4 md:grid-cols-2">
            <Select label="Status" value={trackForm.status} onChange={(e) => setTrackForm((c) => ({ ...c, status: e.target.value as ContentStatus }))} options={statusOptions} />
            <Input label="Display Order" type="number" min={0} value={trackForm.displayOrder} onChange={(e) => setTrackForm((c) => ({ ...c, displayOrder: e.target.value }))} />
          </div>
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsTrackModalOpen(false); setEditingTrackId(null); setTrackForm(defaultTrackForm); }}>Cancel</Button>
            <Button onClick={handleSaveTrack} loading={isSaving}>{editingTrackId ? 'Update Track' : 'Save Track'}</Button>
          </div>
        </div>
      </Modal>

      {/* ── Module Modal ── */}
      <Modal
        open={isModuleModalOpen}
        onClose={() => { setIsModuleModalOpen(false); setEditingModuleId(null); setModuleForm(defaultModuleForm); }}
        title={editingModuleId ? 'Edit Module' : 'Create Module'}
      >
        <div className="space-y-4 py-2">
          <Select
            label="Parent Track *"
            value={moduleForm.trackId}
            onChange={(e) => setModuleForm((c) => ({ ...c, trackId: e.target.value }))}
            options={tracks.map((t) => ({ value: t.id, label: t.title }))}
          />
          <Input label="Title *" value={moduleForm.title} onChange={(e) => setModuleForm((c) => ({ ...c, title: e.target.value }))} />
          <Textarea label="Description" value={moduleForm.description} onChange={(e) => setModuleForm((c) => ({ ...c, description: e.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Select label="Status" value={moduleForm.status} onChange={(e) => setModuleForm((c) => ({ ...c, status: e.target.value as ContentStatus }))} options={statusOptions} />
            <Input label="Display Order" type="number" min={0} value={moduleForm.displayOrder} onChange={(e) => setModuleForm((c) => ({ ...c, displayOrder: e.target.value }))} />
            <Input label="Est. Duration (min)" type="number" min={0} value={moduleForm.estimatedDurationMinutes} onChange={(e) => setModuleForm((c) => ({ ...c, estimatedDurationMinutes: e.target.value }))} />
          </div>
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsModuleModalOpen(false); setEditingModuleId(null); setModuleForm(defaultModuleForm); }}>Cancel</Button>
            <Button onClick={handleSaveModule} loading={isSaving}>{editingModuleId ? 'Update Module' : 'Save Module'}</Button>
          </div>
        </div>
      </Modal>

      {/* ── Lesson Modal ── */}
      <Modal
        open={isLessonModalOpen}
        onClose={() => { setIsLessonModalOpen(false); setLessonForm(defaultLessonForm); }}
        title="Create Lesson"
      >
        <div className="space-y-4 py-2">
          <Select
            label="Parent Module *"
            value={lessonForm.moduleId}
            onChange={(e) => setLessonForm((c) => ({ ...c, moduleId: e.target.value }))}
            options={modules.map((m) => ({ value: m.id, label: m.title }))}
          />
          <Input label="Title *" value={lessonForm.title} onChange={(e) => setLessonForm((c) => ({ ...c, title: e.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Select label="Lesson Type" value={lessonForm.lessonType} onChange={(e) => setLessonForm((c) => ({ ...c, lessonType: e.target.value as LessonType }))} options={lessonTypeOptions} />
            <Select label="Status" value={lessonForm.status} onChange={(e) => setLessonForm((c) => ({ ...c, status: e.target.value as ContentStatus }))} options={statusOptions} />
            <Input label="Display Order" type="number" min={0} value={lessonForm.displayOrder} onChange={(e) => setLessonForm((c) => ({ ...c, displayOrder: e.target.value }))} />
          </div>
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsLessonModalOpen(false); setLessonForm(defaultLessonForm); }}>Cancel</Button>
            <Button onClick={handleSaveLesson} loading={isSaving}>Save Lesson</Button>
          </div>
        </div>
      </Modal>

      {/* ── Package Modal ── */}
      <Modal
        open={isPackageModalOpen}
        onClose={() => { setIsPackageModalOpen(false); setEditingPackageId(null); setPackageForm(defaultPackageForm); }}
        title={editingPackageId ? 'Edit Package' : 'Create Package'}
      >
        <div className="space-y-4 py-2">
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Code" value={packageForm.code} onChange={(e) => setPackageForm((c) => ({ ...c, code: e.target.value }))} hint="Auto-generated from title if empty" />
            <Input label="Title *" value={packageForm.title} onChange={(e) => setPackageForm((c) => ({ ...c, title: e.target.value }))} />
          </div>
          <Textarea label="Description" value={packageForm.description} onChange={(e) => setPackageForm((c) => ({ ...c, description: e.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Select label="Type" value={packageForm.packageType} onChange={(e) => setPackageForm((c) => ({ ...c, packageType: e.target.value as PackageType }))} options={packageTypeOptions} />
            <Input label="Language" value={packageForm.instructionLanguage} onChange={(e) => setPackageForm((c) => ({ ...c, instructionLanguage: e.target.value }))} hint="en, ar, ar+en" />
            <Input label="Profession ID" value={packageForm.professionId} onChange={(e) => setPackageForm((c) => ({ ...c, professionId: e.target.value }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Select label="Status" value={packageForm.status} onChange={(e) => setPackageForm((c) => ({ ...c, status: e.target.value as ContentStatus }))} options={statusOptions} />
            <Input label="Display Order" type="number" min={0} value={packageForm.displayOrder} onChange={(e) => setPackageForm((c) => ({ ...c, displayOrder: e.target.value }))} />
          </div>
          <Input label="Comparison Features" value={packageForm.comparisonFeaturesText} onChange={(e) => setPackageForm((c) => ({ ...c, comparisonFeaturesText: e.target.value }))} hint="Comma-separated feature strings" />
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsPackageModalOpen(false); setEditingPackageId(null); setPackageForm(defaultPackageForm); }}>Cancel</Button>
            <Button onClick={handleSavePackage} loading={isSaving}>{editingPackageId ? 'Update Package' : 'Save Package'}</Button>
          </div>
        </div>
      </Modal>

      {/* ── Archive Confirmation Modal ── */}
      <Modal
        open={isArchiveConfirmOpen}
        onClose={() => { setIsArchiveConfirmOpen(false); setArchiveTarget(null); }}
        title="Confirm Archive"
      >
        <div className="space-y-4 py-2">
          <p>Are you sure you want to archive <strong>{archiveTarget?.title}</strong>? This will set its status to Archived.</p>
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => { setIsArchiveConfirmOpen(false); setArchiveTarget(null); }}>Cancel</Button>
            <Button variant="destructive" onClick={handleArchive} loading={isSaving}>Archive</Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
