'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '@/lib/query/hooks';
import { fetchTourState, markTour as markTourApi, type OnboardingTourState } from '@/lib/api';
import type { TourStatus } from './tour-types';

/**
 * Server-backed tour state (cross-device, survives reinstall/cache-clear).
 * The backend is the source of truth; this just wraps it in React Query so the
 * dashboard checklist, Help center, and auto-trigger all read one cached value.
 */
export function useTourStateQuery(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.onboardingTours.state,
    queryFn: fetchTourState,
    enabled,
    staleTime: 60_000,
  });
}

export function useMarkTourMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { tourId: string; status: TourStatus; role?: string }) =>
      markTourApi(input.tourId, input.status, input.role),
    onSuccess: (data: OnboardingTourState) => {
      queryClient.setQueryData(queryKeys.onboardingTours.state, data);
    },
  });
}
