import { apiRequest } from './client';
import { type ExpertDashboardData, ExpertMe } from '../types/expert';

/**
 * Expert self + dashboard.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchExpertMe(): Promise<ExpertMe> {
  return apiRequest<ExpertMe>('/v1/expert/me');
}

export async function fetchExpertDashboard(): Promise<ExpertDashboardData> {
  return apiRequest<ExpertDashboardData>('/v1/expert/dashboard');
}

// ── Expert Onboarding ─────────────────────────────────────
