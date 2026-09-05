import { describe, expect, it } from 'vitest';

import {
  createSubmitIdempotencyKey,
  toCandidateSafeWritingErrorMessage,
} from './submit-keys';

describe('createSubmitIdempotencyKey', () => {
  it('mints a unique key per submit action', () => {
    const first = createSubmitIdempotencyKey();
    const second = createSubmitIdempotencyKey();

    expect(first).not.toBe('');
    expect(second).not.toBe(first);
  });
});

describe('toCandidateSafeWritingErrorMessage', () => {
  const fallback = 'Something went wrong. Please try again.';

  it('maps release-blocked codes to controlled copy without internal identifiers', () => {
    for (const code of [
      'writing_assessment_missing_input',
      'writing_assessment_release_blocked',
      'writing_assessment_requires_review',
      'writing_rubric_failed',
      'writing_rubric_unavailable',
    ]) {
      const err = Object.assign(new Error('ignored'), { code });
      const message = toCandidateSafeWritingErrorMessage(err, fallback);
      expect(message).not.toMatch(/profession_pack|letter_type_pack|recipient_unknown|case_note_pages/);
      expect(message).toMatch(/not available|try another task/);
    }
  });

  it('keeps the already-grading conflict actionable', () => {
    const err = Object.assign(new Error('ignored'), { code: 'writing_rubric_already_in_progress' });
    expect(toCandidateSafeWritingErrorMessage(err, fallback)).toMatch(/already being graded/);
  });

  it('sanitises raw internal identifiers leaking through generic errors', () => {
    const err = new Error('Writing assessment is not released: profession_pack_not_approved');
    expect(toCandidateSafeWritingErrorMessage(err, fallback)).toBe(fallback);
  });

  it('passes through ordinary network errors unchanged', () => {
    const err = new Error('Failed to fetch');
    expect(toCandidateSafeWritingErrorMessage(err, fallback)).toBe('Failed to fetch');
  });
});
