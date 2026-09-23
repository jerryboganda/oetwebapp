/**
 * Admin Documentation Center — evidence-grade platform documentation
 * (Master Dossier + 15 specialist reports), owner-only. Mirrors the
 * request/blob-download conventions in `./admin-platform.ts`.
 */
import { ApiError, apiRequest, getHeaders, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export type DocumentationExportMode = 'internal' | 'external';

export async function fetchDocumentationModules() {
  return apiRequest('/v1/admin/documentation-center/modules');
}

export async function fetchDocumentationModuleDetail(code: string) {
  return apiRequest(`/v1/admin/documentation-center/modules/${encodeURIComponent(code)}`);
}

export async function fetchDocumentationEvidence(params?: { q?: string; moduleId?: string }) {
  const qs = new URLSearchParams();
  if (params?.q) qs.set('q', params.q);
  if (params?.moduleId) qs.set('moduleId', params.moduleId);
  const q = qs.toString();
  return apiRequest(`/v1/admin/documentation-center/evidence${q ? `?${q}` : ''}`);
}

export async function fetchDocumentationExports() {
  return apiRequest('/v1/admin/documentation-center/exports');
}

async function downloadPdf(path: string, fallbackFileName: string) {
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'documentation_pdf_export_failed', 'Failed to generate the PDF.', response.status >= 500);
  }

  return {
    blob: await response.blob(),
    fileName: response.headers.get('content-disposition')?.match(/filename="?([^"]+)"?/)?.[1] ?? fallbackFileName,
    sha256: response.headers.get('x-document-sha256') ?? undefined,
  };
}

export async function downloadDocumentationModulePdf(code: string, mode: DocumentationExportMode) {
  return downloadPdf(`/v1/admin/documentation-center/modules/${encodeURIComponent(code)}/pdf?mode=${mode}`, `${code}.pdf`);
}

export async function downloadDocumentationMasterPdf(mode: DocumentationExportMode) {
  return downloadPdf(`/v1/admin/documentation-center/master/pdf?mode=${mode}`, 'oet-master-evidence-pack.pdf');
}

export async function downloadDocumentationEvidencePdf(mode: DocumentationExportMode) {
  return downloadPdf(`/v1/admin/documentation-center/evidence/pdf?mode=${mode}`, 'oet-evidence-annex.pdf');
}

/** Re-downloads a previously generated pack byte-for-byte (spec: prior versions "must remain retrievable"). */
export async function downloadDocumentationExportById(exportId: string) {
  return downloadPdf(`/v1/admin/documentation-center/exports/${encodeURIComponent(exportId)}/download`, `${exportId}.pdf`);
}
