import { useEffect, useState } from 'react';
import { getReadingDashboard } from '@/lib/reading-pathway-api';

export function useStreak() {
  const [currentStreak, setCurrentStreak] = useState<number>(0);
  const [longestStreak, setLongestStreak] = useState<number>(0);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const dashboard = await getReadingDashboard();
        if (!cancelled) {
          setCurrentStreak(dashboard.streak);
          setLongestStreak(dashboard.longestStreak);
        }
      } catch {
        // silently swallow
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return { currentStreak, longestStreak, isLoading };
}
