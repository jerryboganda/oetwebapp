import { describe, expect, it } from 'vitest';
import { isEditableEventTarget } from '@/lib/is-editable-target';

describe('isEditableEventTarget', () => {
  it('is true for text inputs', () => {
    const input = document.createElement('input');
    input.type = 'text';
    expect(isEditableEventTarget(input)).toBe(true);
  });

  it('is true for textareas and selects', () => {
    expect(isEditableEventTarget(document.createElement('textarea'))).toBe(true);
    expect(isEditableEventTarget(document.createElement('select'))).toBe(true);
  });

  it('is true for contenteditable elements', () => {
    const editable = document.createElement('div');
    editable.setAttribute('contenteditable', 'true');
    // jsdom does not derive isContentEditable from the attribute.
    Object.defineProperty(editable, 'isContentEditable', { value: true });
    expect(isEditableEventTarget(editable)).toBe(true);
  });

  it('is false for the card containers that own Space/Enter shortcuts', () => {
    expect(isEditableEventTarget(document.createElement('article'))).toBe(false);
    expect(isEditableEventTarget(document.createElement('button'))).toBe(false);
    expect(isEditableEventTarget(document.createElement('div'))).toBe(false);
  });

  it('is false for a null or non-element target', () => {
    expect(isEditableEventTarget(null)).toBe(false);
    expect(isEditableEventTarget(document)).toBe(false);
    expect(isEditableEventTarget(window)).toBe(false);
  });
});
