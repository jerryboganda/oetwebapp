import { TARGET_COUNTRY_OPTIONS } from './target-countries';

export interface ExamType {
  id: string;
  label: string;
  code: string;
  description: string;
}

export interface Profession {
  id: string;
  label: string;
  countryTargets: string[];
  examTypeIds: string[];
  description: string;
  /** Archived professions stay resolvable for labelling but must not be offered as a choice. */
  isActive: boolean;
}

export const examTypes: ExamType[] = [
  {
    id: "oet",
    label: "OET",
    code: "OET",
    description: "Occupational English Test preparation and enrollment.",
  },
];

/**
 * Offline/SSR fallback for the canonical profession taxonomy.
 *
 * The source of truth is the backend `SignupProfessionCatalog` table, served by
 * `GET /v1/professions/catalog` (see `lib/api/professions.ts`). This list must
 * mirror the seeded catalog (`SeedData.cs`) id-for-id — a divergence means a
 * learner can register under an id the backend validator rejects, or an id the
 * discipline filters cannot join on.
 */
export const PROFESSION_CATALOG: Profession[] = [
  {
    id: "nursing",
    label: "Nursing",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Registered nurse and clinical nursing candidates.",
    isActive: true,
  },
  {
    id: "medicine",
    label: "Medicine",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Doctors and physicians preparing for healthcare pathways.",
    isActive: true,
  },
  {
    id: "pharmacy",
    label: "Pharmacy",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Pharmacists and pharmacy practice candidates.",
    isActive: true,
  },
  {
    id: "dentistry",
    label: "Dentistry",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Dental professionals and dentistry applicants.",
    isActive: true,
  },
  {
    id: "physiotherapy",
    label: "Physiotherapy",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Physiotherapists and physical therapy candidates.",
    isActive: true,
  },
  {
    id: "radiography",
    label: "Radiography",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Radiographers and medical imaging candidates.",
    isActive: true,
  },
  {
    id: "other-allied-health",
    label: "Other Allied health profession",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Other allied health professionals (occupational therapy, dietetics, speech pathology, podiatry, optometry, etc.).",
    isActive: true,
  },
  {
    id: "academic-english",
    label: "Academic / General English",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["ielts"],
    description: "General academic and migration IELTS candidates.",
    isActive: true,
  },
];

/**
 * The OET slice of the canonical catalog — every profession an OET rulebook,
 * discipline filter, or plan can be authored against. `academic-english` is
 * deliberately outside this list: it is an IELTS pathway with no OET content.
 */
export const professions: Profession[] = PROFESSION_CATALOG.filter((item) =>
  item.examTypeIds.includes('oet'),
);

/** Human label for a profession id, falling back to the raw id for unknown/legacy values. */
export function professionLabel(professionId: string | null | undefined): string {
  if (!professionId) return '';
  return PROFESSION_CATALOG.find((item) => item.id === professionId)?.label ?? professionId;
}
