'use client';

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ChevronLeft, ChevronRight, Loader2, Save } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { fetchAdminMockBundle } from '@/lib/api';
import {
  WIZARD_STEPS,
  WIZARD_STEP_LABELS,
  getCurrentStep,
  getNextStep,
  getPrevStep,
  type BundleSectionState,
  type WizardSectionsByStep,
  type WizardStep,
} from '@/lib/mock-wizard/state';

// ── Bundle shape we consume ───────────────────────────────────────────

export interface WizardMockBundle {
  id: string;
  title: string;
  mockType: string;
  subtestCode: string | null;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  status: 'draft' | 'published' | 'archived' | string;
  sourceProvenance: string | null;
  priority: number;
  difficulty?: string;
  releasePolicy?: string;
  topicTagsCsv?: string;
  skillTagsCsv?: string;
  watermarkEnabled?: boolean;
  randomiseQuestions?: boolean;
  sections: Array<{
    id: string;
    sectionOrder: number;
    subtestCode: string;
    contentPaperId: string;
    contentPaperTitle?: string | null;
    contentPaperStatus?: string | null;
    timeLimitMinutes: number;
    reviewEligible: boolean;
  }>;
}

// ── Context ───────────────────────────────────────────────────────────

interface WizardContextValue {
  bundle: WizardMockBundle;
  refreshBundle: () => Promise<void>;
  sectionsByStep: WizardSectionsByStep;
  setSavingState: (saving: boolean) => void;
  registerCanAdvance: (step: WizardStep, canAdvance: boolean) => void;
  registerStepSubmit: (step: WizardStep, submit: (() => Promise<void>) | null) => void;
}

const WizardCtx = createContext<WizardContextValue | null>(null);

export function useWizard(): WizardContextValue {
  const ctx = useContext(WizardCtx);
  if (!ctx) throw new Error('useWizard must be used inside <WizardShell>');
  return ctx;
}

// ── Shell ─────────────────────────────────────────────────────────────

export interface WizardShellProps {
  bundle: WizardMockBundle;
  children: ReactNode;
}

function deriveSectionsByStep(bundle: WizardMockBundle): WizardSectionsByStep {
  const result: WizardSectionsByStep = {
    listening: null,
    reading: null,
    writing: null,
    speaking: null,
  };
  for (const s of bundle.sections) {
    const k = s.subtestCode as keyof WizardSectionsByStep;
    if (k in result) {
      const candidate: BundleSectionState = {
        paperId: s.contentPaperId,
        contentPaperTitle: s.contentPaperTitle,
        contentPaperStatus: s.contentPaperStatus,
        subtestCode: s.subtestCode,
      };
      result[k] = candidate;
    }
  }
  return result;
}

export function WizardShell({ bundle: initialBundle, children }: WizardShellProps) {
  const router = useRouter();
  const pathname = usePathname();
  const currentStep = getCurrentStep(pathname);

  const [bundle, setBundle] = useState<WizardMockBundle>(initialBundle);
  const [saving, setSaving] = useState(false);
  const [navigating, setNavigating] = useState(false);
  const [autoSavedAt, setAutoSavedAt] = useState<number | null>(null);

  const canAdvanceMap = useRef<Partial<Record<WizardStep, boolean>>>({});
  const submitMap = useRef<Partial<Record<WizardStep, (() => Promise<void>) | null>>>({});
  const focusRef = useRef<HTMLDivElement | null>(null);

  // Move focus to the step container on mount / step change for keyboard users.
  // The container has aria-label set below so the step name is announced.
  useEffect(() => {
    focusRef.current?.focus();
  }, [currentStep]);

  const refreshBundle = useCallback(async () => {
    const next = (await fetchAdminMockBundle(bundle.id)) as WizardMockBundle;
    setBundle(next);
    setAutoSavedAt(Date.now());
  }, [bundle.id]);

  const registerCanAdvance = useCallback((step: WizardStep, canAdvance: boolean) => {
    canAdvanceMap.current[step] = canAdvance;
  }, []);

  const registerStepSubmit = useCallback(
    (step: WizardStep, submit: (() => Promise<void>) | null) => {
      submitMap.current[step] = submit;
    },
    [],
  );

  const sectionsByStep = useMemo(() => deriveSectionsByStep(bundle), [bundle]);

  const value = useMemo<WizardContextValue>(
    () => ({
      bundle,
      refreshBundle,
      sectionsByStep,
      setSavingState: setSaving,
      registerCanAdvance,
      registerStepSubmit,
    }),
    [bundle, refreshBundle, sectionsByStep, registerCanAdvance, registerStepSubmit],
  );

  const goTo = useCallback(
    (step: WizardStep) => {
      router.push(`/admin/content/mocks/wizard/${bundle.id}/${step}`);
    },
    [bundle.id, router],
  );

  const handleNext = useCallback(async () => {
    if (saving || navigating) return;
    const next = getNextStep(currentStep);
    const submit = submitMap.current[currentStep];
    setNavigating(true);
    try {
      if (submit) await submit();
      if (next) goTo(next);
    } finally {
      setNavigating(false);
    }
  }, [currentStep, goTo, navigating, saving]);

  const handleBack = useCallback(() => {
    const prev = getPrevStep(currentStep);
    if (prev) goTo(prev);
  }, [currentStep, goTo]);

  const handleSave = useCallback(async () => {
    if (saving || navigating) return;
    const submit = submitMap.current[currentStep];
    if (!submit) return;
    setNavigating(true);
    try {
      await submit();
    } finally {
      setNavigating(false);
    }
  }, [currentStep, navigating, saving]);

  const nextStep = getNextStep(currentStep);
  const prevStep = getPrevStep(currentStep);

  return (
    <WizardCtx.Provider value={value}>
      <div className="space-y-6">
        {/* Stepper */}
        <nav aria-label="Mock wizard steps" className="rounded-2xl border border-border bg-surface p-3">
          <ol className="flex flex-wrap items-center gap-2">
            {WIZARD_STEPS.map((step, idx) => {
              const isCurrent = step === currentStep;
              const isComplete = WIZARD_STEPS.indexOf(currentStep) > idx;
              return (
                <li key={step} className="flex items-center gap-2">
                  <Link
                    href={`/admin/content/mocks/wizard/${bundle.id}/${step}`}
                    aria-current={isCurrent ? 'step' : undefined}
                    className={
                      'inline-flex items-center gap-2 rounded-xl px-3 py-1.5 text-xs font-bold transition-colors ' +
                      (isCurrent
                        ? 'bg-primary text-white'
                        : isComplete
                          ? 'bg-emerald-100 text-emerald-700 hover:bg-emerald-200'
                          : 'bg-background-light text-navy hover:bg-gray-100')
                    }
                  >
                    <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-white/30 text-[11px]">
                      {idx + 1}
                    </span>
                    {WIZARD_STEP_LABELS[step]}
                  </Link>
                  {idx < WIZARD_STEPS.length - 1 ? (
                    <ChevronRight aria-hidden className="h-3 w-3 text-muted" />
                  ) : null}
                </li>
              );
            })}
          </ol>
        </nav>

        {/* Bundle header */}
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-background-light px-4 py-3">
          <div className="text-sm">
            <p className="font-bold text-navy">{bundle.title}</p>
            <p className="text-xs text-muted">
              Bundle id: <code>{bundle.id}</code>
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Badge variant={bundle.status === 'published' ? 'success' : 'muted'}>{bundle.status}</Badge>
            {autoSavedAt ? (
              <span className="text-xs text-muted">
                Saved {new Date(autoSavedAt).toLocaleTimeString()}
              </span>
            ) : null}
            {saving ? (
              <span className="inline-flex items-center gap-1 text-xs text-muted">
                <Loader2 className="h-3 w-3 animate-spin" /> Saving…
              </span>
            ) : null}
          </div>
        </div>

        {/* Step body */}
        <div
          ref={focusRef}
          tabIndex={-1}
          aria-label={`Wizard step: ${WIZARD_STEP_LABELS[currentStep]}`}
          className="outline-none"
        >
          {children}
        </div>

        {/* Footer nav */}
        <div className="flex items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-3">
          <Button variant="outline" onClick={handleBack} disabled={!prevStep || navigating}>
            <ChevronLeft className="mr-1 h-4 w-4" /> Back
          </Button>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              onClick={() => void handleSave()}
              disabled={navigating || !submitMap.current[currentStep]}
            >
              <Save className="mr-1 h-4 w-4" /> Save
            </Button>
            <Button
              variant="primary"
              onClick={() => void handleNext()}
              disabled={navigating || !nextStep || canAdvanceMap.current[currentStep] === false}
            >
              {navigating ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
              {nextStep ? `Next: ${WIZARD_STEP_LABELS[nextStep]}` : 'Done'}
              {nextStep ? <ChevronRight className="ml-1 h-4 w-4" /> : null}
            </Button>
          </div>
        </div>
      </div>
    </WizardCtx.Provider>
  );
}
