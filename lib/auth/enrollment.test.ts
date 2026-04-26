import { describe, it, expect } from 'vitest';
import {
  examTypes,
  professions,
  enrollmentSessions,
  type ExamType,
  type Profession,
  type EnrollmentSession,
} from './enrollment';

describe('examTypes catalog', () => {
  it('contains at least OET and IELTS', () => {
    const ids = examTypes.map((e) => e.id);
    expect(ids).toContain('oet');
    expect(ids).toContain('ielts');
  });

  it('has unique ids', () => {
    const ids = examTypes.map((e) => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.each(examTypes)('exam type "$id" has all required fields populated', (entry: ExamType) => {
    expect(entry.id).toBeTruthy();
    expect(entry.label).toBeTruthy();
    expect(entry.code).toBeTruthy();
    expect(entry.description).toBeTruthy();
  });

  it('exposes uppercase codes', () => {
    for (const entry of examTypes) {
      expect(entry.code).toBe(entry.code.toUpperCase());
    }
  });
});

describe('professions catalog', () => {
  it('has unique ids', () => {
    const ids = professions.map((p) => p.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.each(professions)(
    'profession "$id" has at least one country target',
    (p: Profession) => {
      expect(p.countryTargets.length).toBeGreaterThan(0);
    },
  );

  it.each(professions)(
    'profession "$id" only references known exam types',
    (p: Profession) => {
      const knownIds = new Set(examTypes.map((e) => e.id));
      for (const examId of p.examTypeIds) {
        expect(knownIds.has(examId)).toBe(true);
      }
    },
  );

  it('every profession has at least one exam type assigned', () => {
    for (const p of professions) {
      expect(p.examTypeIds.length).toBeGreaterThan(0);
    }
  });

  it('contains nursing, medicine, pharmacy, dentistry, and academic english', () => {
    const ids = professions.map((p) => p.id);
    expect(ids).toEqual(
      expect.arrayContaining(['nursing', 'medicine', 'pharmacy', 'dentistry', 'academic-english']),
    );
  });
});

describe('enrollmentSessions catalog', () => {
  it('has unique ids', () => {
    const ids = enrollmentSessions.map((s) => s.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.each(enrollmentSessions)(
    'session "$id" references a known exam type',
    (s: EnrollmentSession) => {
      const knownIds = new Set(examTypes.map((e) => e.id));
      expect(knownIds.has(s.examTypeId)).toBe(true);
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" references only known professions',
    (s: EnrollmentSession) => {
      const knownProfIds = new Set(professions.map((p) => p.id));
      for (const profId of s.professionIds) {
        expect(knownProfIds.has(profId)).toBe(true);
      }
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" professions all support its exam type',
    (s: EnrollmentSession) => {
      const profMap = new Map(professions.map((p) => [p.id, p]));
      for (const profId of s.professionIds) {
        const prof = profMap.get(profId);
        expect(prof).toBeDefined();
        expect(prof!.examTypeIds).toContain(s.examTypeId);
      }
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" startDate precedes endDate',
    (s: EnrollmentSession) => {
      expect(new Date(s.startDate).getTime()).toBeLessThan(new Date(s.endDate).getTime());
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" capacity is positive and seatsRemaining is within capacity',
    (s: EnrollmentSession) => {
      expect(s.capacity).toBeGreaterThan(0);
      expect(s.seatsRemaining).toBeGreaterThanOrEqual(0);
      expect(s.seatsRemaining).toBeLessThanOrEqual(s.capacity);
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" deliveryMode is one of the allowed enum values',
    (s: EnrollmentSession) => {
      expect(['online', 'hybrid', 'in-person']).toContain(s.deliveryMode);
    },
  );

  it.each(enrollmentSessions)(
    'session "$id" priceLabel includes a leading $ sign',
    (s: EnrollmentSession) => {
      expect(s.priceLabel.startsWith('$')).toBe(true);
    },
  );

  it('contains at least one OET session and at least one IELTS session', () => {
    const examIds = new Set(enrollmentSessions.map((s) => s.examTypeId));
    expect(examIds.has('oet')).toBe(true);
    expect(examIds.has('ielts')).toBe(true);
  });

  it('contains at least one fully-booked session (seatsRemaining=0)', () => {
    expect(enrollmentSessions.some((s) => s.seatsRemaining === 0)).toBe(true);
  });

  it('uses ISO YYYY-MM-DD format for dates', () => {
    const isoDate = /^\d{4}-\d{2}-\d{2}$/;
    for (const s of enrollmentSessions) {
      expect(s.startDate).toMatch(isoDate);
      expect(s.endDate).toMatch(isoDate);
    }
  });
});
