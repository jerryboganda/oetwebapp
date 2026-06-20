'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { UploadCloud, FileText, X } from 'lucide-react';
import { cn } from '@/lib/utils';

const MAX_BYTES = 10 * 1024 * 1024;

export interface ProofDropzoneProps {
  value: File | null;
  onChange: (file: File | null) => void;
  error?: string | null;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function ProofDropzone({ value, onChange, error }: ProofDropzoneProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragActive, setDragActive] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    if (value && value.type.startsWith('image/')) {
      const url = URL.createObjectURL(value);
      setPreviewUrl(url);
      return () => URL.revokeObjectURL(url);
    }
    setPreviewUrl(null);
  }, [value]);

  const accept = useCallback((file: File | undefined | null) => {
    if (!file) return;
    const ok = file.type.startsWith('image/') || file.type === 'application/pdf' || file.type === '';
    if (!ok) {
      setLocalError('Please upload an image (JPG, PNG, GIF, WEBP) or a PDF.');
      return;
    }
    if (file.size > MAX_BYTES) {
      setLocalError('File is too large — maximum size is 10 MB.');
      return;
    }
    setLocalError(null);
    onChange(file);
  }, [onChange]);

  const shownError = localError ?? error ?? null;

  return (
    <div>
      <div
        role="button"
        tabIndex={0}
        onClick={() => inputRef.current?.click()}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); inputRef.current?.click(); } }}
        onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
        onDragLeave={(e) => { e.preventDefault(); setDragActive(false); }}
        onDrop={(e) => { e.preventDefault(); setDragActive(false); accept(e.dataTransfer.files?.[0]); }}
        className={cn(
          'flex cursor-pointer flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed px-4 py-8 text-center transition',
          dragActive
            ? 'border-primary bg-primary/5'
            : shownError
              ? 'border-red-400 bg-red-50/40'
              : 'border-border bg-background-light hover:border-primary/60 hover:bg-primary/5',
        )}
      >
        {value ? (
          previewUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={previewUrl} alt="Payment screenshot preview" className="max-h-40 rounded-lg border border-border object-contain" />
          ) : (
            <FileText className="h-10 w-10 text-primary" />
          )
        ) : (
          <UploadCloud className="h-10 w-10 text-primary" />
        )}
        {value ? (
          <div className="flex items-center gap-2 text-sm">
            <span className="max-w-[14rem] truncate font-medium text-navy">{value.name}</span>
            <span className="text-xs text-muted">({formatSize(value.size)})</span>
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); onChange(null); setLocalError(null); if (inputRef.current) inputRef.current.value = ''; }}
              className="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs font-medium text-red-600 hover:bg-red-50"
            >
              <X className="h-3.5 w-3.5" /> Remove
            </button>
          </div>
        ) : (
          <div className="space-y-0.5">
            <p className="text-sm font-semibold text-navy">Drag &amp; drop your payment screenshot here</p>
            <p className="text-xs text-muted">or click to upload · JPG, PNG or PDF · up to 10 MB</p>
          </div>
        )}
        <input
          ref={inputRef}
          type="file"
          accept="image/*,application/pdf"
          className="hidden"
          onChange={(e) => accept(e.target.files?.[0])}
        />
      </div>
      {shownError ? <p className="mt-1.5 text-xs text-red-600">{shownError}</p> : null}
    </div>
  );
}
