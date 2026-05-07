/**
 * Internal helper used by section steps to lazily create the underlying
 * `ContentPaper`, attach uploaded assets, then add the bundle section.
 * Lives next to the step components because it is wizard-specific glue
 * (no other surface should need it).
 */

import { addAdminMockBundleSection } from '@/lib/api';
import {
  attachPaperAsset,
  createPaperDraft,
  fetchAdminContentPaper,
} from '@/lib/mock-wizard/api';
import type {
  ContentPaperDto,
  PaperAssetRole,
} from '@/lib/content-upload-api';
import type { WizardMockBundle } from './WizardShell';
import { SECTION_DEFAULTS } from '@/lib/mock-wizard/state';

export interface PendingAsset {
  role: PaperAssetRole;
  mediaAssetId: string;
}

export interface EnsurePaperArgs {
  bundle: WizardMockBundle;
  step: 'listening' | 'reading' | 'writing' | 'speaking';
  /** If a section already exists, we resolve to its paper. */
  existingPaperId: string | null;
  paperTitleSuffix: string;
  estimatedDurationMinutes: number;
  letterType?: string | null;
  cardType?: string | null;
  pendingAssets: PendingAsset[];
}

export interface EnsuredPaperResult {
  paper: ContentPaperDto;
  /** True when this call created the paper for the first time. */
  created: boolean;
}

export async function ensurePaperWithAssets({
  bundle,
  step,
  existingPaperId,
  paperTitleSuffix,
  estimatedDurationMinutes,
  letterType,
  cardType,
  pendingAssets,
}: EnsurePaperArgs): Promise<EnsuredPaperResult> {
  let paper: ContentPaperDto;
  let created = false;
  if (existingPaperId) {
    paper = await fetchAdminContentPaper(existingPaperId);
  } else {
    paper = await createPaperDraft({
      subtestCode: step,
      title: `${bundle.title} — ${paperTitleSuffix}`,
      appliesToAllProfessions: bundle.appliesToAllProfessions,
      professionId: bundle.appliesToAllProfessions ? null : bundle.professionId,
      difficulty: bundle.difficulty ?? 'exam_ready',
      estimatedDurationMinutes,
      cardType: cardType ?? null,
      letterType: letterType ?? null,
      priority: bundle.priority ?? 0,
      tagsCsv: bundle.topicTagsCsv ?? null,
      sourceProvenance: bundle.sourceProvenance,
    });
    created = true;
  }

  for (const asset of pendingAssets) {
    await attachPaperAsset(paper.id, {
      role: asset.role,
      mediaAssetId: asset.mediaAssetId,
      displayOrder: 0,
      makePrimary: true,
    });
  }

  return { paper, created };
}

export async function ensureBundleSection(
  bundle: WizardMockBundle,
  step: 'listening' | 'reading' | 'writing' | 'speaking',
  paperId: string,
): Promise<void> {
  const existing = bundle.sections.find((s) => s.contentPaperId === paperId);
  if (existing) return;
  const { order, minutes } = SECTION_DEFAULTS[step];
  // Writing & Speaking are tutor-graded sections — wallet-credit-backed reservation
  // hangs off this flag, so it MUST be true for those subtests. Listening & Reading
  // are auto-marked, so reviewEligible stays false there.
  const reviewEligible = step === 'writing' || step === 'speaking';
  await addAdminMockBundleSection(bundle.id, {
    contentPaperId: paperId,
    sectionOrder: order,
    timeLimitMinutes: minutes,
    reviewEligible,
  });
}
