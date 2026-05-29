import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';

// ── Mock the API client so the panel's logic is the only unit under test. ──
const importListeningManifest = vi.fn();
const exportListeningManifest = vi.fn();
vi.mock('@/lib/listening-authoring-api', () => ({
  importListeningManifest: (...args: unknown[]) => importListeningManifest(...args),
  exportListeningManifest: (...args: unknown[]) => exportListeningManifest(...args),
}));

// ── Lightweight stand-ins for the design-system pieces (mirrors how the
//    reading page test stubs the admin layout). Keeps Radix portals + token
//    plumbing out of the assertions. ──
vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  SettingsSection: ({ title, actions, children }: { title?: ReactNode; actions?: ReactNode; children?: ReactNode }) => (
    <section aria-label={typeof title === 'string' ? title : 'section'}>
      {actions}
      {children}
    </section>
  ),
}));
vi.mock('@/components/admin/ui/button', () => ({
  Button: ({ children, onClick, disabled, startIcon }: { children?: ReactNode; onClick?: () => void; disabled?: boolean; startIcon?: ReactNode }) => (
    <button onClick={onClick} disabled={disabled}>{startIcon}{children}</button>
  ),
  buttonVariants: () => 'btn',
}));
vi.mock('@/components/admin/ui/badge', () => ({
  Badge: ({ children }: { children?: ReactNode }) => <span data-testid="badge">{children}</span>,
}));
vi.mock('@/components/ui/form-controls', () => ({
  Textarea: ({ label, value, onChange, placeholder }: { label?: string; value?: string; onChange?: (e: unknown) => void; placeholder?: string }) => (
    <textarea aria-label={label} value={value} onChange={onChange} placeholder={placeholder} />
  ),
}));
vi.mock('@/components/admin/ui/alert-dialog', () => ({
  AlertDialog: ({ open, children }: { open?: boolean; children?: ReactNode }) => (open ? <div role="alertdialog">{children}</div> : null),
  AlertDialogAction: ({ children, onClick }: { children?: ReactNode; onClick?: () => void }) => <button onClick={onClick}>{children}</button>,
  AlertDialogCancel: ({ children }: { children?: ReactNode }) => <button>{children}</button>,
  AlertDialogContent: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  AlertDialogDescription: ({ children }: { children?: ReactNode }) => <p>{children}</p>,
  AlertDialogFooter: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  AlertDialogHeader: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  AlertDialogTitle: ({ children }: { children?: ReactNode }) => <h2>{children}</h2>,
}));
vi.mock('@/lib/utils', () => ({ cn: (...c: unknown[]) => c.filter(Boolean).join(' ') }));

import { ListeningManifestPanel } from '@/components/domain/listening/ListeningManifestPanel';

const VALID_MANIFEST = JSON.stringify({
  testTitle: 'Mock 01',
  partA: { extracts: [{ extractNumber: 1, questions: [{ number: 1, noteTextBeforeGap: 'x', correctAnswer: 'y' }] }] },
});

const IMPORT_RESULT = {
  structure: { questions: [], counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 } },
  report: {
    isPublishReady: false,
    counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 },
    issues: [{ code: 'listening_source_provenance', severity: 'error', message: 'Provenance required.' }],
  },
};

function renderPanel(overrides: Partial<Parameters<typeof ListeningManifestPanel>[0]> = {}) {
  const onImported = vi.fn();
  const onNotify = vi.fn();
  render(
    <ListeningManifestPanel
      paperId="paper-1"
      paperTitle="Mock 01"
      onImported={onImported}
      onNotify={onNotify}
      {...overrides}
    />,
  );
  return { onImported, onNotify };
}

describe('ListeningManifestPanel', () => {
  beforeEach(() => {
    importListeningManifest.mockReset();
    exportListeningManifest.mockReset();
  });

  it('disables the import button until manifest text is present', () => {
    renderPanel();
    const importButton = screen.getByRole('button', { name: /import manifest/i });
    expect(importButton).toBeDisabled();
  });

  it('rejects invalid manifest JSON without opening the confirm dialog', () => {
    const { onNotify } = renderPanel();
    fireEvent.change(screen.getByLabelText('Manifest JSON'), { target: { value: '{ "nope": true }' } });
    fireEvent.click(screen.getByRole('button', { name: /import manifest/i }));

    expect(onNotify).toHaveBeenCalledWith('error', expect.stringContaining('partA'));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(importListeningManifest).not.toHaveBeenCalled();
  });

  it('opens the confirm dialog for a valid manifest and imports on confirm', async () => {
    importListeningManifest.mockResolvedValue(IMPORT_RESULT);
    const { onImported, onNotify } = renderPanel();

    fireEvent.change(screen.getByLabelText('Manifest JSON'), { target: { value: VALID_MANIFEST } });
    fireEvent.click(screen.getByRole('button', { name: /import manifest/i }));

    // Confirm dialog appears (additive copy since replaceExisting defaults false).
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^import$/i }));

    await waitFor(() => expect(importListeningManifest).toHaveBeenCalledTimes(1));
    expect(importListeningManifest).toHaveBeenCalledWith('paper-1', expect.objectContaining({ testTitle: 'Mock 01' }), false);
    expect(onImported).toHaveBeenCalled();
    expect(onNotify).toHaveBeenCalledWith('success', 'Manifest imported.');
  });

  it('passes replaceExisting=true when the toggle is checked', async () => {
    importListeningManifest.mockResolvedValue(IMPORT_RESULT);
    renderPanel();

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.change(screen.getByLabelText('Manifest JSON'), { target: { value: VALID_MANIFEST } });
    fireEvent.click(screen.getByRole('button', { name: /import manifest/i }));
    fireEvent.click(screen.getByRole('button', { name: /replace test/i }));

    await waitFor(() => expect(importListeningManifest).toHaveBeenCalledTimes(1));
    expect(importListeningManifest).toHaveBeenCalledWith('paper-1', expect.any(Object), true);
  });

  it('renders the publish-gate counts + issues after import', async () => {
    importListeningManifest.mockResolvedValue(IMPORT_RESULT);
    renderPanel();

    fireEvent.change(screen.getByLabelText('Manifest JSON'), { target: { value: VALID_MANIFEST } });
    fireEvent.click(screen.getByRole('button', { name: /import manifest/i }));
    fireEvent.click(screen.getByRole('button', { name: /^import$/i }));

    await waitFor(() => expect(screen.getByText(/42 \/ 42 questions/)).toBeInTheDocument());
    expect(screen.getByText(/Part A 24 \/ 24/)).toBeInTheDocument();
    expect(screen.getByText('listening_source_provenance')).toBeInTheDocument();
    expect(screen.getByText(/Needs review/)).toBeInTheDocument();
  });

  it('surfaces an error notification when import fails', async () => {
    importListeningManifest.mockRejectedValue(new Error('boom'));
    const { onNotify } = renderPanel();

    fireEvent.change(screen.getByLabelText('Manifest JSON'), { target: { value: VALID_MANIFEST } });
    fireEvent.click(screen.getByRole('button', { name: /import manifest/i }));
    fireEvent.click(screen.getByRole('button', { name: /^import$/i }));

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('error', 'boom'));
  });

  it('exports the manifest on demand', async () => {
    exportListeningManifest.mockResolvedValue({ testTitle: 'Mock 01', partA: { extracts: [] } });
    // jsdom lacks URL.createObjectURL — stub it for the download path.
    const createObjectURL = vi.fn(() => 'blob:mock');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });

    const { onNotify } = renderPanel();
    fireEvent.click(screen.getByRole('button', { name: /export manifest/i }));

    await waitFor(() => expect(exportListeningManifest).toHaveBeenCalledWith('paper-1'));
    expect(onNotify).toHaveBeenCalledWith('success', 'Manifest downloaded.');
  });
});
