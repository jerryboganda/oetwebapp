"use client";

import { create } from "zustand";
import { persist } from "zustand/middleware";
import {
  enrollmentSessions,
  examTypes,
  professions,
  targetCountries,
} from "@/Data/OET/mock";
import type {
  EnrollmentSession,
  ExamType,
  Profession,
  TargetCountry,
} from "@/types/oet";

type ExamTypeInput = Omit<ExamType, "id"> & { id?: string };
type ProfessionInput = Omit<Profession, "id"> & { id?: string };
type EnrollmentSessionInput = Omit<EnrollmentSession, "id"> & { id?: string };
type TargetCountryInput = Omit<TargetCountry, "id"> & { id?: string };

function toSlug(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function createId(prefix: string, value: string) {
  const base = toSlug(value) || `${prefix}-${Date.now()}`;
  return `${prefix}-${base}`;
}

interface EnrollmentTaxonomyStoreState {
  examTypes: ExamType[];
  professions: Profession[];
  sessions: EnrollmentSession[];
  targetCountries: TargetCountry[];
  createExamType: (payload: ExamTypeInput) => void;
  updateExamType: (id: string, payload: ExamTypeInput) => void;
  deleteExamType: (id: string) => void;
  createProfession: (payload: ProfessionInput) => void;
  updateProfession: (id: string, payload: ProfessionInput) => void;
  deleteProfession: (id: string) => void;
  createSession: (payload: EnrollmentSessionInput) => void;
  updateSession: (id: string, payload: EnrollmentSessionInput) => void;
  deleteSession: (id: string) => void;
  createTargetCountry: (payload: TargetCountryInput) => void;
  updateTargetCountry: (id: string, payload: TargetCountryInput) => void;
  deleteTargetCountry: (id: string) => void;
  reset: () => void;
}

const initialState = {
  examTypes,
  professions,
  sessions: enrollmentSessions,
  targetCountries,
};

export const useEnrollmentTaxonomyStore =
  create<EnrollmentTaxonomyStoreState>()(
    persist(
      (set) => ({
        ...initialState,
        createExamType: (payload) =>
          set((state) => ({
            examTypes: [
              ...state.examTypes,
              {
                ...payload,
                id: payload.id ?? createId("exam", payload.label),
              },
            ],
          })),
        updateExamType: (id, payload) =>
          set((state) => ({
            examTypes: state.examTypes.map((item) =>
              item.id === id ? { ...item, ...payload, id } : item
            ),
          })),
        deleteExamType: (id) =>
          set((state) => ({
            examTypes: state.examTypes.filter((item) => item.id !== id),
            professions: state.professions.map((profession) => ({
              ...profession,
              examTypeIds: profession.examTypeIds.filter(
                (examTypeId) => examTypeId !== id
              ),
            })),
            sessions: state.sessions.filter(
              (session) => session.examTypeId !== id
            ),
          })),
        createProfession: (payload) =>
          set((state) => ({
            professions: [
              ...state.professions,
              {
                ...payload,
                id: payload.id ?? createId("profession", payload.label),
              },
            ],
          })),
        updateProfession: (id, payload) =>
          set((state) => ({
            professions: state.professions.map((item) =>
              item.id === id ? { ...item, ...payload, id } : item
            ),
          })),
        deleteProfession: (id) =>
          set((state) => ({
            professions: state.professions.filter((item) => item.id !== id),
            sessions: state.sessions.map((session) => ({
              ...session,
              professionIds: session.professionIds.filter(
                (professionId) => professionId !== id
              ),
            })),
          })),
        createSession: (payload) =>
          set((state) => ({
            sessions: [
              ...state.sessions,
              {
                ...payload,
                id: payload.id ?? createId("session", payload.name),
              },
            ],
          })),
        updateSession: (id, payload) =>
          set((state) => ({
            sessions: state.sessions.map((item) =>
              item.id === id ? { ...item, ...payload, id } : item
            ),
          })),
        deleteSession: (id) =>
          set((state) => ({
            sessions: state.sessions.filter((item) => item.id !== id),
          })),
        createTargetCountry: (payload) =>
          set((state) => ({
            targetCountries: [
              ...state.targetCountries,
              {
                ...payload,
                id: payload.id ?? createId("country", payload.label),
              },
            ],
          })),
        updateTargetCountry: (id, payload) =>
          set((state) => ({
            targetCountries: state.targetCountries.map((item) =>
              item.id === id ? { ...item, ...payload, id } : item
            ),
            professions: state.professions.map((profession) => ({
              ...profession,
              countryTargets: profession.countryTargets.map((country) =>
                country ===
                state.targetCountries.find((item) => item.id === id)?.label
                  ? payload.label
                  : country
              ),
            })),
          })),
        deleteTargetCountry: (id) =>
          set((state) => {
            const removedCountry = state.targetCountries.find(
              (item) => item.id === id
            );

            return {
              targetCountries: state.targetCountries.filter(
                (item) => item.id !== id
              ),
              professions: state.professions.map((profession) => ({
                ...profession,
                countryTargets: profession.countryTargets.filter(
                  (country) => country !== removedCountry?.label
                ),
              })),
            };
          }),
        reset: () => set(initialState),
      }),
      {
        merge: (persisted, current) => ({
          ...current,
          ...(persisted as Partial<EnrollmentTaxonomyStoreState>),
          targetCountries:
            (persisted as Partial<EnrollmentTaxonomyStoreState>)
              ?.targetCountries ?? current.targetCountries,
        }),
        name: "oet-enrollment-taxonomy",
      }
    )
  );
