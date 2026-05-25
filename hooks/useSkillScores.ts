import { useCallback, useEffect, useState } from 'react';
import { getSkillRadar, type SkillRadarDto } from '@/lib/reading-pathway-api';

export function useSkillScores() {
  const [skillRadar, setSkillRadar] = useState<SkillRadarDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refetch = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await getSkillRadar();
      setSkillRadar(data);
    } catch {
      // silently swallow — caller can check skillRadar === null
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    refetch();
  }, [refetch]);

  /** Optimistically apply a score delta to a skill by code. */
  function updateLocalScore(code: string, delta: number) {
    setSkillRadar((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        skills: prev.skills.map((s) =>
          s.code === code
            ? { ...s, current: Math.min(100, Math.max(0, s.current + delta)) }
            : s,
        ),
      };
    });
  }

  return { skills: skillRadar?.skills ?? [], skillRadar, isLoading, refetch, updateLocalScore };
}
