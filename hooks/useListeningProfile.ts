import { useCallback, useEffect, useState } from 'react';
import { getListeningProfile, type ListeningProfile } from '@/lib/listening-pathway-api';

export function useListeningProfile() {
  const [profile, setProfile] = useState<ListeningProfile | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  const mutate = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getListeningProfile();
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
