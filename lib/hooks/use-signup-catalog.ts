'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import {
  enrollmentSessions as fallbackSessions,
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import type { SignupExamType, SignupProfession, SignupSession } from '@/lib/types/auth';

export function useSignupCatalog() {
  const [examTypes, setExamTypes] = useState<SignupExamType[]>(fallbackExamTypes);
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [enrollmentSessions, setEnrollmentSessions] = useState<SignupSession[]>(fallbackSessions);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      try {
        const catalog = await fetchSignupCatalog();

        if (cancelled) {
          return;
        }

        setExamTypes(catalog.examTypes);
        setProfessions(catalog.professions);
        setEnrollmentSessions(catalog.sessions);
      } catch {
        if (cancelled) {
          return;
        }

        setExamTypes(fallbackExamTypes);
        setProfessions(fallbackProfessions);
        setEnrollmentSessions(fallbackSessions);
      }
    };

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, []);

  return {
    enrollmentSessions,
    examTypes,
    professions,
  };
}
