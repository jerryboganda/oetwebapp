'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import {
  enrollmentSessions as fallbackSessions,
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import type {
  ExternalAuthProvider,
  SignupBillingPlan,
  SignupExamType,
  SignupProfession,
  SignupSession,
} from '@/lib/types/auth';

export function useSignupCatalog() {
  const [examTypes, setExamTypes] = useState<SignupExamType[]>(fallbackExamTypes);
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [enrollmentSessions, setEnrollmentSessions] = useState<SignupSession[]>(fallbackSessions);
  const [billingPlans, setBillingPlans] = useState<SignupBillingPlan[]>([]);
  const [externalAuthProviders, setExternalAuthProviders] = useState<ExternalAuthProvider[]>([]);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      try {
        const catalog = await fetchSignupCatalog();

        if (cancelled) {
          return;
        }

        setExamTypes(Array.isArray(catalog.examTypes) ? catalog.examTypes : fallbackExamTypes);
        setProfessions(Array.isArray(catalog.professions) ? catalog.professions : fallbackProfessions);
        setEnrollmentSessions(Array.isArray(catalog.sessions) ? catalog.sessions : fallbackSessions);
        setBillingPlans(Array.isArray(catalog.billingPlans) ? catalog.billingPlans : []);
        setExternalAuthProviders(Array.isArray(catalog.externalAuthProviders) ? catalog.externalAuthProviders : []);
      } catch {
        if (cancelled) {
          return;
        }

        setExamTypes(fallbackExamTypes);
        setProfessions(fallbackProfessions);
        setEnrollmentSessions(fallbackSessions);
        setBillingPlans([]);
        setExternalAuthProviders([]);
      }
    };

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, []);

  return {
    billingPlans,
    enrollmentSessions,
    examTypes,
    externalAuthProviders,
    professions,
  };
}
