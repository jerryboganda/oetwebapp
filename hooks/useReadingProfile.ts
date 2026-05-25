import { useCallback, useEffect, useState } from 'react';
import { getReadingProfile, type LearnerReadingProfileDto } from '@/lib/reading-pathway-api';

export function useReadingProfile() {
  const [profile, setProfile] = useState<LearnerReadingProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  const mutate = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getReadingProfile();
      setProfile(data);
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    mutate();
  }, [mutate]);

  return { profile, isLoading, error, mutate };
}
