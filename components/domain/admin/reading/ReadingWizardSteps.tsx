'use client';

import Link from 'next/link';
import { Check, FileText, BookOpen, HelpCircle, ShieldCheck } from 'lucide-react';
import { cn } from '@/lib/utils';

export type ReadingWizardStep = 'metadata' | 'texts' | 'questions' | 'validate';

const STEPS: Array<{ key: ReadingWizardStep; label: string; icon: React.ReactNode; href: (id: string) => string }> = [
  { key: 'metadata', label: 'Paper Info', icon: <FileText className="h-4 w-4" />, href: (id) => `/admin/content/reading/${id}` },
  { key: 'texts', label: 'Texts', icon: <BookOpen className="h-4 w-4" />, href: (id) => `/admin/content/reading/${id}/texts` },
  { key: 'questions', label: 'Questions', icon: <HelpCircle className="h-4 w-4" />, href: (id) => `/admin/content/reading/${id}/questions` },
  { key: 'validate', label: 'Validate & Publish', icon: <ShieldCheck className="h-4 w-4" />, href: (id) => `/admin/content/reading/${id}/validate` },
];

interface ReadingWizardStepsProps {
  paperId: string;
  currentStep: ReadingWizardStep;
  completedSteps?: ReadingWizardStep[];
}

export function ReadingWizardSteps({ paperId, currentStep, completedSteps = [] }: ReadingWizardStepsProps) {
  const currentIdx = STEPS.findIndex((s) => s.key === currentStep);

  return (
    <nav aria-label="Reading authoring steps" className="flex items-center gap-1 overflow-x-auto pb-2">
      {STEPS.map((step, idx) => {
        const isCompleted = completedSteps.includes(step.key);
        const isCurrent = step.key === currentStep;
        const isPast = idx < currentIdx;

        return (
          <div key={step.key} className="flex items-center">
            {idx > 0 && (
              <div className={cn(
                'w-6 h-px mx-1',
                isPast || isCompleted ? 'bg-emerald-500' : 'bg-border',
              )} />
            )}
            <Link
              href={step.href(paperId)}
              className={cn(
                'flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors whitespace-nowrap',
                isCurrent && 'bg-primary/10 text-primary border border-primary/30',
                !isCurrent && isCompleted && 'text-emerald-400 hover:bg-emerald-500/10',
                !isCurrent && !isCompleted && 'text-muted-foreground hover:bg-muted',
              )}
            >
              {isCompleted ? <Check className="h-4 w-4 text-emerald-500" /> : step.icon}
              {step.label}
            </Link>
          </div>
        );
      })}
    </nav>
  );
}
