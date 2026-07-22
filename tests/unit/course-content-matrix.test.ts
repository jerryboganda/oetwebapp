import { describe, expect, it } from 'vitest';
import { COURSE_PROFESSIONS, COURSE_SUBTESTS, expectedVideoTargets } from '@/lib/course-content-matrix';

describe('profession-first course content matrix', () => {
  it('contains only the six course professions at the root', () => {
    expect(COURSE_PROFESSIONS.map((p) => p.label)).toEqual([
      'Medicine', 'Nursing', 'Pharmacy', 'Physiotherapy', 'Dentistry', 'Radiography',
    ]);
    expect(COURSE_PROFESSIONS.map((p) => p.label)).not.toContain('Listening');
  });

  it('maps all profession/language/subtest combinations deterministically', () => {
    for (const profession of COURSE_PROFESSIONS) {
      for (const language of ['en', 'ar'] as const) {
        for (const subtest of COURSE_SUBTESTS) {
          const targets = expectedVideoTargets(language, subtest, profession.id);
          if ((profession.id === 'dentistry' || profession.id === 'radiography') && (subtest === 'writing' || subtest === 'speaking')) {
            expect(targets).toBeNull();
          } else if (language === 'en' || subtest === 'listening' || subtest === 'reading') {
            expect(targets).toEqual([]);
          } else {
            expect(targets).not.toBeNull();
          }
        }
      }
    }
  });
});
