'use client';

import { ExternalLink, FileText, Paperclip } from 'lucide-react';

export interface ClassMaterial {
  id: string;
  title: string;
  fileUrl: string;
  mimeType?: string | null;
  visibility?: 'PreClass' | 'DuringClass' | 'PostClass' | string | null;
}

export interface ClassMaterialListProps {
  materials: ClassMaterial[];
}

function visibilityLabel(visibility: ClassMaterial['visibility']): string | null {
  switch (visibility) {
    case 'PreClass':
      return 'Pre-class';
    case 'DuringClass':
      return 'During class';
    case 'PostClass':
      return 'Post-class';
    default:
      return null;
  }
}

export function ClassMaterialList({ materials }: ClassMaterialListProps) {
  if (!materials || materials.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-border bg-surface p-6 text-center">
        <Paperclip className="mx-auto mb-3 h-6 w-6 text-muted/50" />
        <p className="text-sm font-medium text-navy">No materials shared.</p>
        <p className="mt-1 text-xs text-muted">Tutors can attach slides, PDFs, and links to a class.</p>
      </div>
    );
  }

  return (
    <ul className="space-y-2">
      {materials.map((material) => {
        const label = visibilityLabel(material.visibility);
        return (
          <li
            key={material.id}
            className="flex items-start justify-between gap-3 rounded-2xl border border-border bg-surface p-3 shadow-sm"
          >
            <div className="flex min-w-0 items-start gap-3">
              <FileText className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-navy">{material.title}</p>
                <div className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-muted">
                  {material.mimeType ? <span>{material.mimeType}</span> : null}
                  {label ? <span className="rounded-full bg-background-light px-2 py-0.5">{label}</span> : null}
                </div>
              </div>
            </div>
            <a
              href={material.fileUrl}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
            >
              Open <ExternalLink className="h-3 w-3" />
            </a>
          </li>
        );
      })}
    </ul>
  );
}
