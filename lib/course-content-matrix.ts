export const COURSE_PROFESSIONS = [
  { id: 'medicine', label: 'Medicine' },
  { id: 'nursing', label: 'Nursing' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'physiotherapy', label: 'Physiotherapy' },
  { id: 'dentistry', label: 'Dentistry' },
  { id: 'radiography', label: 'Radiography' },
] as const;

export const COURSE_SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;
export type CourseProfessionId = (typeof COURSE_PROFESSIONS)[number]['id'];
export type CourseSubtest = (typeof COURSE_SUBTESTS)[number];

export function expectedVideoTargets(
  language: 'en' | 'ar',
  subtest: CourseSubtest,
  profession: CourseProfessionId,
): string[] | null {
  if (language === 'en' || subtest === 'listening' || subtest === 'reading') return [];
  // Each profession is targeted individually by default — admins can add more
  // professions afterwards from the Access step's checkbox list.
  return [profession];
}
