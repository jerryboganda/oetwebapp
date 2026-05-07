/**
 * Wizard state shapes, zod schemas and step helpers.
 *
 * The wizard does not persist a custom backend record — every step writes
 * through to existing admin endpoints (paper create, asset attach, structure
 * PUT, section add). This module just types the in-flight UI state and
 * computes "can advance" gates per step.
 */

import { z } from 'zod';

export const WIZARD_STEPS = [
  'bundle',
  'listening',
  'reading',
  'writing',
  'speaking',
  'review',
] as const;

export type WizardStep = (typeof WIZARD_STEPS)[number];

export const WIZARD_STEP_LABELS: Record<WizardStep, string> = {
  bundle: 'Bundle metadata',
  listening: 'Listening',
  reading: 'Reading',
  writing: 'Writing',
  speaking: 'Speaking',
  review: 'Review & publish',
};

export const SUBTEST_BY_STEP: Record<
  Exclude<WizardStep, 'bundle' | 'review'>,
  'listening' | 'reading' | 'writing' | 'speaking'
> = {
  listening: 'listening',
  reading: 'reading',
  writing: 'writing',
  speaking: 'speaking',
};

export const SECTION_DEFAULTS: Record<
  Exclude<WizardStep, 'bundle' | 'review'>,
  { order: number; minutes: number }
> = {
  listening: { order: 1, minutes: 40 },
  reading: { order: 2, minutes: 60 },
  writing: { order: 3, minutes: 45 },
  speaking: { order: 4, minutes: 20 },
};

export function getCurrentStep(pathname: string | null): WizardStep {
  if (!pathname) return 'bundle';
  const parts = pathname.split('/').filter(Boolean);
  const last = parts[parts.length - 1] as WizardStep;
  return (WIZARD_STEPS as readonly string[]).includes(last) ? last : 'bundle';
}

export function getNextStep(step: WizardStep): WizardStep | null {
  const idx = WIZARD_STEPS.indexOf(step);
  if (idx < 0 || idx === WIZARD_STEPS.length - 1) return null;
  return WIZARD_STEPS[idx + 1];
}

export function getPrevStep(step: WizardStep): WizardStep | null {
  const idx = WIZARD_STEPS.indexOf(step);
  if (idx <= 0) return null;
  return WIZARD_STEPS[idx - 1];
}

// ── Bundle metadata ────────────────────────────────────────────────────

export const BundleMetadataSchema = z.object({
  title: z.string().min(3, 'Title must be at least 3 characters.'),
  mockType: z.enum([
    'full',
    'lrw',
    'sub',
    'part',
    'diagnostic',
    'final_readiness',
    'remedial',
  ]),
  appliesToAllProfessions: z.boolean(),
  professionId: z.string().nullable().optional(),
  sourceProvenance: z
    .string()
    .min(20, 'Provenance is required and must describe the source clearly.'),
  priority: z.number().int().nonnegative(),
  difficulty: z.string(),
  releasePolicy: z.string(),
  topicTagsCsv: z.string().optional().default(''),
  skillTagsCsv: z.string().optional().default(''),
  watermarkEnabled: z.boolean(),
  randomiseQuestions: z.boolean(),
});
export type BundleMetadata = z.infer<typeof BundleMetadataSchema>;

// ── Per-step "can advance" gates ───────────────────────────────────────

export interface BundleSectionState {
  paperId: string;
  contentPaperTitle?: string | null;
  contentPaperStatus?: string | null;
  subtestCode: string;
}

export interface WizardSectionsByStep {
  listening: BundleSectionState | null;
  reading: BundleSectionState | null;
  writing: BundleSectionState | null;
  speaking: BundleSectionState | null;
}

export function canAdvanceBundle(meta: Partial<BundleMetadata>): boolean {
  const result = BundleMetadataSchema.safeParse(meta);
  return result.success;
}

export function canAdvanceSection(section: BundleSectionState | null): boolean {
  return Boolean(section?.paperId);
}

export function canPublishReview(sections: WizardSectionsByStep): boolean {
  return Boolean(
    sections.listening?.paperId &&
      sections.reading?.paperId &&
      sections.writing?.paperId &&
      sections.speaking?.paperId,
  );
}
