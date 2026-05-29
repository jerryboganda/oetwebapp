import { describe, it, expect } from 'vitest';
import {
  addVariant,
  removeVariant,
  parseAcceptedVariants,
  serializeAcceptedVariants,
  type AcceptedVariant,
} from './accepted-variants';

describe('accepted-variants manager logic', () => {
  describe('addVariant', () => {
    it('adds a trimmed variant with the given category', () => {
      const next = addVariant([], '  colour ', 'uk-spelling');
      expect(next).toHaveLength(1);
      expect(next[0].value).toBe('colour');
      expect(next[0].category).toBe('uk-spelling');
      expect(next[0].id).toBeTruthy();
    });

    it('defaults the category to "other"', () => {
      const next = addVariant([], 'mg');
      expect(next[0].category).toBe('other');
    });

    it('ignores empty / whitespace-only input', () => {
      expect(addVariant([], '   ')).toEqual([]);
      expect(addVariant([], '')).toEqual([]);
    });

    it('de-duplicates case-insensitively', () => {
      const first = addVariant([], 'Colour', 'uk-spelling');
      const second = addVariant(first, 'colour', 'us-spelling');
      expect(second).toHaveLength(1);
      expect(second).toBe(first);
    });

    it('appends distinct variants without mutating the source', () => {
      const first = addVariant([], 'colour');
      const second = addVariant(first, 'color');
      expect(second).toHaveLength(2);
      expect(first).toHaveLength(1);
      expect(second.map((v) => v.value)).toEqual(['colour', 'color']);
    });
  });

  describe('removeVariant', () => {
    it('removes the matching variant by id', () => {
      const list: AcceptedVariant[] = [
        { id: 'a', value: 'colour', category: 'uk-spelling' },
        { id: 'b', value: 'color', category: 'us-spelling' },
      ];
      const next = removeVariant(list, 'a');
      expect(next).toHaveLength(1);
      expect(next[0].id).toBe('b');
    });

    it('returns an equivalent list when id is not found', () => {
      const list: AcceptedVariant[] = [{ id: 'a', value: 'colour', category: 'other' }];
      expect(removeVariant(list, 'missing')).toEqual(list);
    });
  });

  describe('serialize / parse round-trip', () => {
    it('serialises to a plain string array', () => {
      const list: AcceptedVariant[] = [
        { id: 'a', value: 'colour', category: 'uk-spelling' },
        { id: 'b', value: 'color', category: 'us-spelling' },
      ];
      expect(serializeAcceptedVariants(list)).toBe('["colour","color"]');
    });

    it('serialises an empty list to null', () => {
      expect(serializeAcceptedVariants([])).toBeNull();
    });

    it('parses a legacy string array', () => {
      const parsed = parseAcceptedVariants('["colour","color"]');
      expect(parsed.map((v) => v.value)).toEqual(['colour', 'color']);
      expect(parsed.every((v) => v.category === 'other')).toBe(true);
    });

    it('parses an object array with category hints', () => {
      const parsed = parseAcceptedVariants('[{"value":"colour","category":"uk-spelling"}]');
      expect(parsed).toHaveLength(1);
      expect(parsed[0].value).toBe('colour');
      expect(parsed[0].category).toBe('uk-spelling');
    });

    it('returns an empty list for invalid JSON or null', () => {
      expect(parseAcceptedVariants(null)).toEqual([]);
      expect(parseAcceptedVariants('not json')).toEqual([]);
      expect(parseAcceptedVariants('{}')).toEqual([]);
    });
  });
});
