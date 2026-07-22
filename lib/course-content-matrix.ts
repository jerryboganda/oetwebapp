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
  if ((profession === 'dentistry' || profession === 'radiography') && (subtest === 'writing' || subtest === 'speaking')) return null;
  if (language === 'en' || subtest === 'listening' || subtest === 'reading') return [];
  if (profession === 'medicine' || profession === 'physiotherapy') return ['medicine', 'physiotherapy'];
  if (profession === 'nursing') return ['nursing'];
  if (profession === 'pharmacy') return ['pharmacy'];
  return null;
}
