'use client';

import { ExternalLink, FileText, Paperclip } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { cn } from '@/lib/utils';

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
      <EmptyState
        icon={<Paperclip className="h-7 w-7" />}
        title="No materials shared."
        description="Tutors can attach slides, PDFs, and links to a class."
      />
    );
  }

  return (
    <ul className="space-y-2">
      {materials.map((material) => {
        const label = visibilityLabel(material.visibility);
        return (
          <li
            key={material.id}
            className={cn(cardClassName({ padding: 'sm' }), 'flex items-center justify-between gap-3')}
          >
            <div className="flex min-w-0 items-start gap-3">
              <FileText className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-navy">{material.title}</p>
                <div className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-muted">
                  {material.mimeType ? <span>{material.mimeType}</span> : null}
                  {label ? <span className="rounded-full bg-background-light px-2 py-0.5">{label}</span> : null}
                </div>
              </div>
            </div>
            <Button asChild variant="ghost" size="sm" className="shrink-0 text-primary">
              <a href={material.fileUrl} target="_blank" rel="noreferrer">
                Open <ExternalLink className="h-3 w-3" aria-hidden="true" />
              </a>
            </Button>
          </li>
        );
      })}
    </ul>
  );
}
