'use client';

import { useCallback, useState } from 'react';
import { useRouter } from 'next/navigation';

import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/admin/ui/button';
import { importWritingTask } from '@/lib/writing/exam-api';
import type { WritingTaskImportJson } from '@/lib/writing/types';

interface WritingTaskImportDialogProps {
  open: boolean;
  onClose: () => void;
  /** Called with a human-readable error/success so the host can toast it. */
  onError?: (message: string) => void;
  onSuccess?: (message: string) => void;
}

/**
 * Paste-to-import dialog for a writing task (spec §18).
 *
 * The author pastes a `WritingTaskImportJson` envelope; on submit we POST it via
 * `importWritingTask` and redirect into the new task's edit route. A
 * "Download sample JSON" affordance writes a fully-shaped example to disk so
 * authors have a starting template.
 */
export function WritingTaskImportDialog({
  open,
  onClose,
  onError,
  onSuccess,
}: WritingTaskImportDialogProps) {
  const router = useRouter();
  const [raw, setRaw] = useState('');
  const [busy, setBusy] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  const reset = useCallback(() => {
    setRaw('');
    setLocalError(null);
    setBusy(false);
  }, []);

  const handleClose = useCallback(() => {
    if (busy) return;
    reset();
    onClose();
  }, [busy, reset, onClose]);

  const handleImport = useCallback(async () => {
    setLocalError(null);
    let parsed: WritingTaskImportJson;
    try {
      parsed = JSON.parse(raw) as WritingTaskImportJson;
    } catch {
      setLocalError('That is not valid JSON. Check for trailing commas or quotes.');
      return;
    }
    if (!parsed || typeof parsed !== 'object') {
      setLocalError('The JSON must be an object matching the import shape.');
      return;
    }
    setBusy(true);
    try {
      const dto = await importWritingTask(parsed);
      onSuccess?.('Imported — opening task');
      reset();
      onClose();
      router.push(`/admin/writing/tasks/${dto.id}/edit`);
    } catch (err) {
      const message =
        err instanceof Error ? `Import failed: ${err.message}` : 'Import failed';
      setLocalError(message);
      onError?.(message);
      setBusy(false);
    }
  }, [raw, router, onClose, onError, onSuccess, reset]);

  const handleDownloadSample = useCallback(() => {
    const blob = new Blob([JSON.stringify(SAMPLE_IMPORT_JSON, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'writing-task-sample.json';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }, []);

  return (
    <Modal
      open={open}
      onClose={handleClose}
      title="Import writing task"
      size="lg"
    >
      <div className="space-y-3">
        <label
          htmlFor="writing-task-import-json"
          className="block text-sm font-medium text-admin-fg-default"
        >
          Task JSON
        </label>
        <textarea
          id="writing-task-import-json"
          value={raw}
          onChange={(e) => setRaw(e.target.value)}
          rows={16}
          spellCheck={false}
          placeholder='{ "taskTitle": "…", "profession": "medicine", "taskType": "LT-RR", … }'
          className="block w-full rounded-admin border border-admin-border bg-admin-bg-canvas px-3 py-2 font-mono text-xs leading-relaxed text-admin-fg-strong outline-none transition-[border-color,box-shadow] duration-150 placeholder:text-admin-fg-muted focus-visible:border-[var(--admin-primary)] focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] motion-reduce:transition-none"
        />
        {localError ? (
          <p role="alert" className="text-sm text-admin-danger">
            {localError}
          </p>
        ) : (
          <p className="text-xs text-admin-fg-muted">
            Tip: download the sample to see the full shape (task prompt, fixed
            instructions, and word guide).
          </p>
        )}
        <div className="flex flex-wrap justify-end gap-2 pt-2">
          <Button variant="secondary" size="sm" onClick={handleDownloadSample}>
            Download sample JSON
          </Button>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={handleImport}
            loading={busy}
            disabled={busy || raw.trim().length === 0}
          >
            Import
          </Button>
        </div>
      </div>
    </Modal>
  );
}

/** A fully-shaped §18 example used by "Download sample JSON". */
const SAMPLE_IMPORT_JSON: WritingTaskImportJson = {
  taskTitle: 'Discharge — elderly patient post hip replacement',
  internalCode: 'MED-DIS-014',
  profession: 'medicine',
  taskType: 'LT-DG',
  duration: { readingTimeSeconds: 300, writingTimeSeconds: 2400 },
  caseNotes: {
    todayDate: '14 March 2026',
    candidateRole: 'the doctor on the orthopaedic ward',
  },
  writingTask: {
    instruction:
      'Using the information in the case notes, write a discharge letter to the patient’s general practitioner, Dr Helen Marsh.',
    fixedInstructions: [
      'Expand the relevant notes into complete sentences',
      'Do not use note form',
      'Use letter format',
      'The body of the letter should be approximately 180-200 words',
    ],
    wordGuide: { min: 180, max: 200 },
  },
  marking: {
    expectedPurpose: 'Hand over ongoing care of the patient to the GP after discharge',
    expectedAction:
      'Review the patient, oversee the care package, and arrange district-nurse wound checks',
  },
};
