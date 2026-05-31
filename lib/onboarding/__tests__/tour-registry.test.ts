import { describe, expect, it } from 'vitest';
import { allTours, getTour, toursForRole } from '../tour-registry';
import type { TourCompletionKey, TourId, TourRole } from '../tour-types';

const EXPECTED_IDS: TourId[] = [
  'learner-dashboard',
  'listening',
  'reading',
  'writing',
  'speaking',
  'admin',
  'expert',
  'tutor',
];

const VALID_ROLES: TourRole[] = ['learner', 'expert', 'admin', 'tutor'];
const VALID_KEYS: TourCompletionKey[] = [
  'intro',
  'dashboard',
  'listening',
  'reading',
  'writing',
  'speaking',
  'admin',
  'expert',
];

describe('tour registry', () => {
  it('registers exactly the expected tours with unique ids', () => {
    const ids = allTours().map((tour) => tour.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids.sort()).toEqual([...EXPECTED_IDS].sort());
  });

  it('every tour is well-formed (role, completionKey, steps, copy)', () => {
    for (const tour of allTours()) {
      expect(VALID_ROLES).toContain(tour.role);
      expect(VALID_KEYS).toContain(tour.completionKey);
      expect(tour.title.length).toBeGreaterThan(0);
      expect(tour.description.length).toBeGreaterThan(0);
      expect(tour.steps.length).toBeGreaterThanOrEqual(3);
      for (const step of tour.steps) {
        expect(step.title.trim().length).toBeGreaterThan(0);
        expect(step.body.trim().length).toBeGreaterThan(0);
        if (step.target !== undefined) {
          expect(step.target.trim().length).toBeGreaterThan(0);
        }
      }
    }
  });

  it('learner module tours map to their own completion key and trigger on the hub', () => {
    for (const id of ['listening', 'reading', 'writing', 'speaking'] as const) {
      const tour = getTour(id);
      expect(tour).toBeDefined();
      expect(tour?.role).toBe('learner');
      expect(tour?.completionKey).toBe(id);
      expect(tour?.triggerRoute).toBe(`/${id}`);
    }
  });

  it('getTour returns undefined for an unknown id', () => {
    expect(getTour('does-not-exist' as TourId)).toBeUndefined();
  });

  it('scopes tours by role; experts also see tutor tours', () => {
    expect(toursForRole('learner').every((tour) => tour.role === 'learner')).toBe(true);
    expect(toursForRole('admin').map((tour) => tour.id)).toEqual(['admin']);

    const expertIds = toursForRole('expert').map((tour) => tour.id);
    expect(expertIds).toContain('expert');
    expect(expertIds).toContain('tutor');
    expect(expertIds).not.toContain('admin');
    expect(expertIds).not.toContain('learner-dashboard');
  });
});
