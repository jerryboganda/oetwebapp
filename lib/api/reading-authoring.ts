/**
 * Reading authoring admin surface — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 *
 * Typed wrappers for the Reading Authoring admin surface backed by
 * `ReadingAuthoringAdminEndpoints` under `/v1/admin/papers/{paperId}/reading`.
 * All correct-answer / explanation / synonym fields here are intentionally
 * included — these helpers MUST only be called from admin UI behind
 * `AdminContentWrite`.
 */
import { apiRequest } from './client';
import type {
  ReadingDistractorsPayload,
  ReadingExtractionDraft,
  ReadingPartUpsertDto,
  ReadingPartView,
  ReadingQuestionDto,
  ReadingQuestionReviewLogEntry,
  ReadingQuestionUpsertDto,
  ReadingReviewTransitionPayload,
  ReadingStructure,
  ReadingStructureImportResult,
  ReadingStructureManifest,
  ReadingStructureManifestImportPayload,
  ReadingTextDto,
  ReadingTextUpsertDto,
  ReadingValidationReport,
  ReorderDto,
} from '../types/admin/reading-authoring';

const readingAdminBase = (paperId: string) =>
  `/v1/admin/papers/${encodeURIComponent(paperId)}/reading`;

export async function adminReadingGetStructure(paperId: string): Promise<ReadingStructure> {
  return apiRequest<ReadingStructure>(`${readingAdminBase(paperId)}/structure`);
}

export async function adminReadingGetManifest(paperId: string): Promise<ReadingStructureManifest> {
  return apiRequest<ReadingStructureManifest>(`${readingAdminBase(paperId)}/manifest`);
}

export async function adminReadingImportManifest(
  paperId: string,
  payload: ReadingStructureManifestImportPayload,
): Promise<ReadingStructureImportResult> {
  return apiRequest<ReadingStructureImportResult>(`${readingAdminBase(paperId)}/manifest`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminReadingEnsureCanonical(paperId: string): Promise<void> {
  await apiRequest<void>(`${readingAdminBase(paperId)}/ensure-canonical`, {
    method: 'POST',
  });
}

export async function adminReadingUpsertPart(
  paperId: string,
  partCode: 'A' | 'B' | 'C',
  dto: ReadingPartUpsertDto,
): Promise<ReadingPartView> {
  return apiRequest<ReadingPartView>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partCode)}`,
    { method: 'PUT', body: JSON.stringify(dto) },
  );
}

export async function adminReadingUpsertText(
  paperId: string,
  dto: ReadingTextUpsertDto,
): Promise<ReadingTextDto> {
  return apiRequest<ReadingTextDto>(`${readingAdminBase(paperId)}/texts`, {
    method: 'POST',
    body: JSON.stringify(dto),
  });
}

export async function adminReadingDeleteText(paperId: string, textId: string): Promise<void> {
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/texts/${encodeURIComponent(textId)}`,
    { method: 'DELETE' },
  );
}

export async function adminReadingUpsertQuestion(
  paperId: string,
  dto: ReadingQuestionUpsertDto,
): Promise<ReadingQuestionDto> {
  return apiRequest<ReadingQuestionDto>(`${readingAdminBase(paperId)}/questions`, {
    method: 'POST',
    body: JSON.stringify(dto),
  });
}

export async function adminReadingDeleteQuestion(paperId: string, questionId: string): Promise<void> {
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}`,
    { method: 'DELETE' },
  );
}

export async function adminReadingReorderTexts(
  paperId: string,
  partId: string,
  orderedIds: string[],
): Promise<void> {
  const body: ReorderDto = { orderedIds };
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partId)}/reorder-texts`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

export async function adminReadingReorderQuestions(
  paperId: string,
  partId: string,
  orderedIds: string[],
): Promise<void> {
  const body: ReorderDto = { orderedIds };
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partId)}/reorder-questions`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

export async function adminReadingValidate(paperId: string): Promise<ReadingValidationReport> {
  return apiRequest<ReadingValidationReport>(`${readingAdminBase(paperId)}/validate`);
}

export async function adminReadingSetDistractors(
  paperId: string,
  questionId: string,
  payload: ReadingDistractorsPayload,
): Promise<{ id: string; optionDistractorsJson: string | null }> {
  return apiRequest<{ id: string; optionDistractorsJson: string | null }>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/distractors`,
    { method: 'PUT', body: JSON.stringify(payload) },
  );
}

export async function adminReadingGetReviewHistory(
  paperId: string,
  questionId: string,
): Promise<ReadingQuestionReviewLogEntry[]> {
  return apiRequest<ReadingQuestionReviewLogEntry[]>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/review-history`,
  );
}

export async function adminReadingTransitionReview(
  paperId: string,
  questionId: string,
  payload: ReadingReviewTransitionPayload,
): Promise<unknown> {
  return apiRequest<unknown>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/review-transition`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
}

export async function adminReadingGetAnalytics(paperId: string): Promise<unknown> {
  return apiRequest<unknown>(`${readingAdminBase(paperId)}/analytics`);
}

export async function adminReadingCreateExtraction(
  paperId: string,
  mediaAssetId?: string | null,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions`,
    { method: 'POST', body: JSON.stringify({ mediaAssetId: mediaAssetId ?? null }) },
  );
}

export async function adminReadingListExtractions(paperId: string): Promise<ReadingExtractionDraft[]> {
  return apiRequest<ReadingExtractionDraft[]>(`${readingAdminBase(paperId)}/extractions`);
}

export async function adminReadingGetExtraction(
  paperId: string,
  draftId: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}`,
  );
}

export async function adminReadingApproveExtraction(
  paperId: string,
  draftId: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}/approve`,
    { method: 'POST' },
  );
}

export async function adminReadingRejectExtraction(
  paperId: string,
  draftId: string,
  reason?: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}/reject`,
    { method: 'POST', body: JSON.stringify({ reason: reason ?? null }) },
  );
}
