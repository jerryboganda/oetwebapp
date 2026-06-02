'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ChevronRight,
  ChevronDown,
  FolderOpen,
  FolderPlus,
  FileText,
  Music,
  Plus,
  Pencil,
  Trash2,
  Users,
  RotateCcw,
} from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Modal } from '@/components/ui/modal';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AudiencePicker } from '@/components/admin/audience-picker';
import {
  adminListMaterialFolders,
  adminCreateMaterialFolder,
  adminUpdateMaterialFolder,
  adminDeleteMaterialFolder,
  adminSetFolderAudience,
  adminListMaterialFiles,
  adminCreateMaterialFile,
  adminUpdateMaterialFile,
  adminDeleteMaterialFile,
  type MaterialFolderDto,
  type MaterialFileDto,
  type MaterialAudienceMode,
  type AudienceRow,
} from '@/lib/materials-api';
import { uploadFileChunked, type PaperAssetRole } from '@/lib/content-upload-api';

// ── Constants ────────────────────────────────────────────────────────────────

const SUBTEST_OPTIONS = [
  { value: '', label: 'All subtests' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
];

const STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Materials' },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

// ── Folder form defaults ──────────────────────────────────────────────────────

const defaultFolderForm = {
  name: '',
  description: '',
  subtestCode: '',
  audienceMode: 'Inherit' as MaterialAudienceMode,
  audiences: [] as AudienceRow[],
};

// ── File form defaults ────────────────────────────────────────────────────────

const defaultFileForm = {
  title: '',
  description: '',
  subtestCode: 'listening',
  folderId: '',
};

// ── Component ─────────────────────────────────────────────────────────────────

export default function AdminMaterialsPage() {
  const [tree, setTree] = useState<MaterialFolderDto[]>([]);
  const [selectedFolder, setSelectedFolder] = useState<MaterialFolderDto | null>(null);
  const [files, setFiles] = useState<MaterialFileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [filesLoading, setFilesLoading] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  // Folder modal
  const [folderModalOpen, setFolderModalOpen] = useState(false);
  const [editingFolder, setEditingFolder] = useState<MaterialFolderDto | null>(null);
  const [parentFolderIdForNew, setParentFolderIdForNew] = useState<string | null>(null);
  const [folderForm, setFolderForm] = useState(defaultFolderForm);
  const [savingFolder, setSavingFolder] = useState(false);

  // Audience modal
  const [audienceModalOpen, setAudienceModalOpen] = useState(false);
  const [audienceTargetFolder, setAudienceTargetFolder] = useState<MaterialFolderDto | null>(null);
  const [audienceForm, setAudienceForm] = useState({
    audienceMode: 'Inherit' as MaterialAudienceMode,
    audiences: [] as AudienceRow[],
  });
  const [savingAudience, setSavingAudience] = useState(false);

  // File modal
  const [fileModalOpen, setFileModalOpen] = useState(false);
  const [editingFile, setEditingFile] = useState<MaterialFileDto | null>(null);
  const [fileForm, setFileForm] = useState(defaultFileForm);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [savingFile, setSavingFile] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // ── Data loading ─────────────────────────────────────────────────────────

  const loadTree = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListMaterialFolders();
      setTree(Array.isArray(data) ? data : []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, []);

  const loadFiles = useCallback(async (folderId: string | null) => {
    setFilesLoading(true);
    try {
      const data = await adminListMaterialFiles({ folderId: folderId ?? undefined, pageSize: 100 });
      setFiles(data.items ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setFilesLoading(false);
    }
  }, []);

  useEffect(() => { void loadTree(); }, [loadTree]);

  useEffect(() => {
    void loadFiles(selectedFolder?.id ?? null);
  }, [selectedFolder, loadFiles]);

  // ── Folder actions ────────────────────────────────────────────────────────

  function openCreateFolder(parentId: string | null = null) {
    setEditingFolder(null);
    setParentFolderIdForNew(parentId);
    setFolderForm(defaultFolderForm);
    setFolderModalOpen(true);
  }

  function openEditFolder(folder: MaterialFolderDto) {
    setEditingFolder(folder);
    setFolderForm({
      name: folder.name,
      description: folder.description ?? '',
      subtestCode: folder.subtestCode ?? '',
      audienceMode: folder.audienceMode,
      audiences: folder.audiences?.map((a) => ({ targetType: a.targetType as AudienceRow['targetType'], targetId: a.targetId })) ?? [],
    });
    setFolderModalOpen(true);
  }

  async function saveFolder() {
    if (!folderForm.name.trim()) {
      setToast({ variant: 'error', message: 'Folder name is required.' });
      return;
    }
    setSavingFolder(true);
    try {
      if (editingFolder) {
        await adminUpdateMaterialFolder(editingFolder.id, {
          name: folderForm.name.trim(),
          description: folderForm.description || null,
          subtestCode: folderForm.subtestCode || null,
          audienceMode: folderForm.audienceMode,
        });
        setToast({ variant: 'success', message: 'Folder updated.' });
      } else {
        await adminCreateMaterialFolder({
          parentFolderId: parentFolderIdForNew,
          name: folderForm.name.trim(),
          description: folderForm.description || null,
          subtestCode: folderForm.subtestCode || null,
          audienceMode: folderForm.audienceMode,
        });
        setToast({ variant: 'success', message: 'Folder created.' });
      }
      setFolderModalOpen(false);
      await loadTree();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSavingFolder(false);
    }
  }

  async function deleteFolder(folder: MaterialFolderDto) {
    if (!confirm(`Delete folder "${folder.name}"? This will also delete all its contents.`)) return;
    setBusyId(folder.id);
    try {
      await adminDeleteMaterialFolder(folder.id, true);
      setToast({ variant: 'success', message: `Deleted "${folder.name}".` });
      if (selectedFolder?.id === folder.id) setSelectedFolder(null);
      await loadTree();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  // ── Audience actions ──────────────────────────────────────────────────────

  function openAudienceModal(folder: MaterialFolderDto) {
    setAudienceTargetFolder(folder);
    setAudienceForm({
      audienceMode: folder.audienceMode,
      audiences: folder.audiences?.map((a) => ({ targetType: a.targetType as AudienceRow['targetType'], targetId: a.targetId })) ?? [],
    });
    setAudienceModalOpen(true);
  }

  async function saveAudience() {
    if (!audienceTargetFolder) return;
    setSavingAudience(true);
    try {
      await adminSetFolderAudience(audienceTargetFolder.id, {
        audienceMode: audienceForm.audienceMode,
        audiences: audienceForm.audiences.map((a) => ({ targetType: a.targetType, targetId: a.targetId })),
      });
      setToast({ variant: 'success', message: 'Audience updated.' });
      setAudienceModalOpen(false);
      await loadTree();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSavingAudience(false);
    }
  }

  // ── File actions ──────────────────────────────────────────────────────────

  function openCreateFile() {
    setEditingFile(null);
    setFileForm({
      ...defaultFileForm,
      folderId: selectedFolder?.id ?? '',
    });
    setUploadFile(null);
    setUploadProgress(0);
    if (fileInputRef.current) fileInputRef.current.value = '';
    setFileModalOpen(true);
  }

  function openEditFile(file: MaterialFileDto) {
    setEditingFile(file);
    setFileForm({
      title: file.title,
      description: file.description ?? '',
      subtestCode: file.subtestCode,
      folderId: file.folderId ?? '',
    });
    setUploadFile(null);
    setUploadProgress(0);
    setFileModalOpen(true);
  }

  async function saveFile() {
    if (!fileForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!editingFile && !uploadFile) {
      setToast({ variant: 'error', message: 'Please select a file to upload.' });
      return;
    }

    setSavingFile(true);
    setUploadProgress(0);
    try {
      let mediaAssetId = editingFile?.mediaAssetId ?? '';

      if (uploadFile) {
        const isAudio = /\.(mp3|m4a|wav|ogg)$/i.test(uploadFile.name);
        const role: PaperAssetRole = isAudio ? 'Audio' : 'Supplementary';
        const result = await uploadFileChunked(uploadFile, role, setUploadProgress);
        mediaAssetId = result.mediaAssetId;
      }

      if (editingFile) {
        await adminUpdateMaterialFile(editingFile.id, {
          title: fileForm.title.trim(),
          description: fileForm.description || null,
          subtestCode: fileForm.subtestCode,
          folderId: fileForm.folderId || null,
          ...(uploadFile ? { mediaAssetId } : {}),
        });
        setToast({ variant: 'success', message: 'File updated.' });
      } else {
        await adminCreateMaterialFile({
          folderId: fileForm.folderId || null,
          mediaAssetId,
          subtestCode: fileForm.subtestCode,
          title: fileForm.title.trim(),
          description: fileForm.description || null,
        });
        setToast({ variant: 'success', message: 'File created.' });
      }

      setFileModalOpen(false);
      await loadFiles(selectedFolder?.id ?? null);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSavingFile(false);
      setUploadProgress(0);
    }
  }

  async function publishFile(file: MaterialFileDto) {
    setBusyId(file.id);
    try {
      await adminUpdateMaterialFile(file.id, { status: 'Published' });
      setToast({ variant: 'success', message: `Published "${file.title}".` });
      await loadFiles(selectedFolder?.id ?? null);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function unpublishFile(file: MaterialFileDto) {
    setBusyId(file.id);
    try {
      await adminUpdateMaterialFile(file.id, { status: 'Draft' });
      setToast({ variant: 'success', message: `Unpublished "${file.title}".` });
      await loadFiles(selectedFolder?.id ?? null);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function deleteFile(file: MaterialFileDto) {
    if (!confirm(`Delete file "${file.title}"?`)) return;
    setBusyId(file.id);
    try {
      await adminDeleteMaterialFile(file.id);
      setToast({ variant: 'success', message: `Deleted "${file.title}".` });
      await loadFiles(selectedFolder?.id ?? null);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────

  function renderFolderTree(folders: MaterialFolderDto[], depth = 0): React.ReactNode {
    return folders.map((folder) => (
      <FolderTreeNode
        key={folder.id}
        folder={folder}
        depth={depth}
        selected={selectedFolder?.id === folder.id}
        busyId={busyId}
        onSelect={() => setSelectedFolder(folder)}
        onEdit={() => openEditFolder(folder)}
        onDelete={() => void deleteFolder(folder)}
        onAudience={() => openAudienceModal(folder)}
        onCreateChild={() => openCreateFolder(folder.id)}
      >
        {folder.folders && folder.folders.length > 0 && renderFolderTree(folder.folders, depth + 1)}
      </FolderTreeNode>
    ));
  }


  return (
    <AdminCatalogLayout
      title="Materials Library"
      description="Upload and organise downloadable study materials for candidates. Assign folders to specific plans or cohorts to control access."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      hideViewModeToggle
      actions={
        <Button onClick={() => openCreateFolder(null)}>
          <FolderPlus className="h-4 w-4 mr-1" /> New Root Folder
        </Button>
      }
    >
      <div className="col-span-full grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Folder tree */}
        <div className="lg:col-span-1 space-y-2">
          <div className="flex items-center justify-between mb-2">
            <h2 className="text-sm font-semibold text-admin-fg-strong">Folders</h2>
            <button
              type="button"
              onClick={() => void loadTree()}
              className="text-xs text-admin-fg-muted hover:text-admin-fg-strong"
              aria-label="Refresh"
            >
              <RotateCcw className="w-3.5 h-3.5" />
            </button>
          </div>

          {loading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-10 rounded-lg" />)}
            </div>
          ) : tree.length === 0 ? (
            <div className="rounded-lg border border-dashed border-border p-4 text-center text-xs text-admin-fg-muted">
              No folders yet. Create one to get started.
            </div>
          ) : (
            <div className="space-y-1">
              {/* Root-level "all files" entry */}
              <button
                type="button"
                onClick={() => setSelectedFolder(null)}
                className={[
                  'w-full flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-left transition-colors',
                  selectedFolder === null
                    ? 'bg-primary/10 text-primary-dark font-semibold'
                    : 'text-admin-fg hover:bg-admin-hover',
                ].join(' ')}
              >
                <FolderOpen className="w-4 h-4 shrink-0" /> All files
              </button>
              {renderFolderTree(tree)}
            </div>
          )}
        </div>

        {/* File list */}
        <div className="lg:col-span-2 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-admin-fg-strong">
              {selectedFolder ? selectedFolder.name : 'All files'}
            </h2>
            <Button size="sm" onClick={openCreateFile}>
              <Plus className="h-3.5 w-3.5 mr-1" /> Add file
            </Button>
          </div>

          {filesLoading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 rounded-lg" />)}
            </div>
          ) : files.length === 0 ? (
            <EmptyState
              illustration={<FileText />}
              title="No files"
              description={selectedFolder ? `No files in "${selectedFolder.name}" yet.` : 'No files yet.'}
            />
          ) : (
            <div className="space-y-2">
              {files.map((file) => (
                <Card key={file.id}>
                  <CardContent className="p-3">
                    <div className="flex items-start gap-3">
                      <span className="mt-0.5 shrink-0 text-muted">
                        {file.kind === 'audio'
                          ? <Music className="w-5 h-5 text-blue-500" />
                          : <FileText className="w-5 h-5 text-red-400" />}
                      </span>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="text-sm font-semibold text-admin-fg-strong truncate">{file.title}</span>
                          <Badge variant={file.status === 'Published' ? 'success' : file.status === 'Archived' ? 'danger' : 'secondary'}>{file.status}</Badge>
                          <Badge variant="outline">{file.subtestCode}</Badge>
                          <Badge variant="outline">{file.kind}</Badge>
                        </div>
                        {file.description && (
                          <p className="text-xs text-admin-fg-muted mt-0.5 truncate">{file.description}</p>
                        )}
                        {file.media && (
                          <p className="text-xs text-admin-fg-muted mt-0.5">
                            {file.media.originalFilename} &middot; {formatBytes(file.media.sizeBytes)}
                          </p>
                        )}
                      </div>
                      <div className="flex items-center gap-1 shrink-0">
                        {file.status === 'Draft' && (
                          <Button
                            size="sm"
                            disabled={busyId === file.id}
                            onClick={() => void publishFile(file)}
                          >
                            Publish
                          </Button>
                        )}
                        {file.status === 'Published' && (
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={busyId === file.id}
                            onClick={() => void unpublishFile(file)}
                          >
                            Unpublish
                          </Button>
                        )}
                        <Button size="sm" variant="ghost" onClick={() => openEditFile(file)}>
                          <Pencil className="w-3.5 h-3.5" />
                        </Button>
                        <Button
                          size="sm"
                          variant="destructive"
                          disabled={busyId === file.id}
                          onClick={() => void deleteFile(file)}
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Folder create/edit modal */}
      <Modal open={folderModalOpen} onClose={() => setFolderModalOpen(false)} title={editingFolder ? 'Edit Folder' : 'New Folder'}>
        <div className="space-y-4 p-4">
          <Input
            label="Name"
            value={folderForm.name}
            onChange={(e) => setFolderForm((f) => ({ ...f, name: e.target.value }))}
            placeholder="e.g. Mock Test 1"
            maxLength={200}
          />
          <Input
            label="Description (optional)"
            value={folderForm.description}
            onChange={(e) => setFolderForm((f) => ({ ...f, description: e.target.value }))}
            placeholder="Short description"
            maxLength={1024}
          />
          <Select
            label="Subtest hint (optional)"
            value={folderForm.subtestCode}
            onChange={(e) => setFolderForm((f) => ({ ...f, subtestCode: e.target.value }))}
            options={SUBTEST_OPTIONS}
          />
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setFolderModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveFolder()} disabled={savingFolder}>
              {savingFolder ? 'Saving…' : editingFolder ? 'Save changes' : 'Create folder'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Audience modal */}
      <Modal open={audienceModalOpen} onClose={() => setAudienceModalOpen(false)} title={`Audience — ${audienceTargetFolder?.name ?? ''}`}>
        <div className="p-4 space-y-4">
          <p className="text-xs text-admin-fg-muted">
            Control which learners can see this folder and its contents.
            Child folders set to &quot;Inherit&quot; will use this setting.
          </p>
          <AudiencePicker
            audienceMode={audienceForm.audienceMode}
            audiences={audienceForm.audiences}
            onChange={(mode, audiences) => setAudienceForm({ audienceMode: mode, audiences })}
          />
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setAudienceModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveAudience()} disabled={savingAudience}>
              {savingAudience ? 'Saving…' : 'Save audience'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* File create/edit modal */}
      <Modal open={fileModalOpen} onClose={() => setFileModalOpen(false)} title={editingFile ? 'Edit File' : 'Add File'}>
        <div className="p-4 space-y-4">
          <Input
            label="Title"
            value={fileForm.title}
            onChange={(e) => setFileForm((f) => ({ ...f, title: e.target.value }))}
            placeholder="e.g. Listening Practice — Case 3"
            maxLength={200}
          />
          <Input
            label="Description (optional)"
            value={fileForm.description}
            onChange={(e) => setFileForm((f) => ({ ...f, description: e.target.value }))}
            maxLength={1024}
          />
          <Select
            label="Subtest"
            value={fileForm.subtestCode}
            onChange={(e) => setFileForm((f) => ({ ...f, subtestCode: e.target.value }))}
            options={SUBTEST_OPTIONS.filter((o) => o.value !== '')}
          />
          <div>
            <label className="block text-xs font-semibold text-admin-fg-muted mb-1.5">
              {editingFile ? 'Replace file (leave blank to keep current)' : 'File (PDF or audio)'}
            </label>
            <input
              ref={fileInputRef}
              type="file"
              accept=".pdf,.mp3,.m4a,.wav,.ogg"
              onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-admin-fg file:mr-3 file:rounded-md file:border-0 file:bg-admin-hover file:px-3 file:py-1.5 file:text-xs file:font-semibold"
            />
            {uploadFile && (
              <p className="mt-1 text-xs text-admin-fg-muted">{uploadFile.name} ({formatBytes(uploadFile.size)})</p>
            )}
            {savingFile && uploadProgress > 0 && uploadProgress < 1 && (
              <div className="mt-2 h-1.5 rounded-full bg-border overflow-hidden">
                <div className="h-full bg-primary transition-all" style={{ width: `${Math.round(uploadProgress * 100)}%` }} />
              </div>
            )}
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setFileModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveFile()} disabled={savingFile}>
              {savingFile ? 'Saving…' : editingFile ? 'Save changes' : 'Add file'}
            </Button>
          </div>
        </div>
      </Modal>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminCatalogLayout>
  );
}

// ── FolderTreeNode ────────────────────────────────────────────────────────────

function FolderTreeNode({
  folder,
  depth,
  selected,
  busyId,
  onSelect,
  onEdit,
  onDelete,
  onAudience,
  onCreateChild,
  children,
}: {
  folder: MaterialFolderDto;
  depth: number;
  selected: boolean;
  busyId: string | null;
  onSelect: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onAudience: () => void;
  onCreateChild: () => void;
  children?: React.ReactNode;
}) {
  const [expanded, setExpanded] = useState(depth === 0);
  const hasChildren = (folder.folders?.length ?? 0) > 0;

  return (
    <div className={depth > 0 ? 'ml-4 border-l border-border/40 pl-2' : ''}>
      <div
        className={[
          'group flex items-center gap-1 rounded-lg px-2 py-1.5 text-sm transition-colors',
          selected ? 'bg-primary/10 text-primary-dark font-semibold' : 'hover:bg-admin-hover text-admin-fg',
        ].join(' ')}
      >
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="shrink-0 text-admin-fg-muted"
          disabled={!hasChildren}
          aria-label={expanded ? 'Collapse' : 'Expand'}
        >
          {hasChildren
            ? (expanded ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />)
            : <span className="w-3.5 inline-block" />}
        </button>

        <button type="button" onClick={onSelect} className="flex-1 truncate text-left">
          <FolderOpen className="inline w-3.5 h-3.5 mr-1.5 shrink-0" />
          {folder.name}
        </button>

        {/* Audience badge */}
        <span className="shrink-0 text-[10px] text-admin-fg-muted hidden group-hover:inline">
          {folder.audienceMode === 'Everyone' ? '🌐' : folder.audienceMode === 'Restricted' ? '🔒' : '↑'}
        </span>

        {/* Actions (shown on hover) */}
        <div className="hidden group-hover:flex items-center gap-0.5 shrink-0">
          <button type="button" title="Set audience" onClick={onAudience} className="rounded p-0.5 hover:bg-primary/10 text-admin-fg-muted hover:text-primary">
            <Users className="w-3 h-3" />
          </button>
          <button type="button" title="Add subfolder" onClick={onCreateChild} className="rounded p-0.5 hover:bg-primary/10 text-admin-fg-muted hover:text-primary">
            <FolderPlus className="w-3 h-3" />
          </button>
          <button type="button" title="Edit" onClick={onEdit} className="rounded p-0.5 hover:bg-primary/10 text-admin-fg-muted hover:text-primary">
            <Pencil className="w-3 h-3" />
          </button>
          <button
            type="button"
            title="Delete"
            onClick={onDelete}
            disabled={busyId === folder.id}
            className="rounded p-0.5 hover:bg-red-50 text-admin-fg-muted hover:text-red-500 disabled:opacity-50"
          >
            <Trash2 className="w-3 h-3" />
          </button>
        </div>
      </div>

      {expanded && hasChildren && (
        <div className="mt-0.5">{children}</div>
      )}
    </div>
  );
}

function formatBytes(bytes?: number | null): string {
  if (bytes == null || bytes === 0) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
