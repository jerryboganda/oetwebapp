import { describe, expect, it } from 'vitest';
import {
  cleanExampleSentence,
  cleanExampleSentencesForList,
  isUsefulExampleSentence,
  repeatedExampleSentenceKeys,
} from '../vocabulary-example-sentence';

describe('isUsefulExampleSentence', () => {
  it('accepts real clinical example sentences', () => {
    expect(
      isUsefulExampleSentence('dyspnoea', 'She presented with acute dyspnoea on exertion.'),
    ).toBe(true);
    expect(
      isUsefulExampleSentence('hypertension', 'He has hypertension.'),
    ).toBe(true);
    expect(
      isUsefulExampleSentence('advance care plan', 'An advance care plan was reviewed with the family.'),
    ).toBe(true);
  });

  it('rejects the §3A filler sentence verbatim, with the word substituted', () => {
    expect(
      isUsefulExampleSentence(
        'dyspnoea',
        'The term dyspnoea was reviewed as part of OET vocabulary practice.',
      ),
    ).toBe(false);
    expect(
      isUsefulExampleSentence(
        'hypertension',
        'The term hypertension was reviewed as part of OET vocabulary practice.',
      ),
    ).toBe(false);
  });

  it('rejects the unsubstituted template token form', () => {
    expect(
      isUsefulExampleSentence('dyspnoea', 'The term [word] was reviewed as part of OET vocabulary practice.'),
    ).toBe(false);
  });

  it('rejects the VocabularyGlossService deterministic fallback', () => {
    expect(isUsefulExampleSentence('dyspnoea', "The patient's notes referenced dyspnoea.")).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'The patient notes referenced dyspnoea.')).toBe(false);
  });

  it('rejects authoring-script fixture templates', () => {
    expect(isUsefulExampleSentence('dyspnoea', 'Example using dyspnoea.')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'Example sentence for dyspnoea.')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'Practice sentence for dyspnoea.')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'Dyspnoea is a word in the OET vocabulary list.')).toBe(false);
  });

  it('rejects unsubstituted placeholders of every flavour', () => {
    for (const raw of [
      'The patient reported {word} overnight.',
      'The patient reported {{word}} overnight.',
      'The patient reported <word> overnight.',
      'The patient reported [term] overnight.',
      'The patient reported %s overnight.',
    ]) {
      expect(isUsefulExampleSentence('dyspnoea', raw)).toBe(false);
    }
  });

  it('rejects junk markers and non-answers', () => {
    for (const raw of [
      'Lorem ipsum dolor sit amet.',
      'Coming soon for this word.',
      'No example sentence available yet.',
      'TODO',
      'N/A',
      'undefined',
      '---',
    ]) {
      expect(isUsefulExampleSentence('dyspnoea', raw)).toBe(false);
    }
  });

  it('rejects blank, whitespace-only and too-short text', () => {
    expect(isUsefulExampleSentence('dyspnoea', '')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', '   ')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', null)).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', undefined)).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'Dyspnoea.')).toBe(false);
    expect(isUsefulExampleSentence('dyspnoea', 'the term dyspnoea')).toBe(false);
  });

  it('does not suppress a legitimate sentence that merely mentions "term"', () => {
    expect(
      isUsefulExampleSentence('gestation', 'The pregnancy reached full term without complication.'),
    ).toBe(true);
  });
});

describe('cleanExampleSentence', () => {
  it('returns the trimmed sentence when it is useful', () => {
    expect(cleanExampleSentence('dyspnoea', '  She presented with acute dyspnoea on exertion.  ')).toBe(
      'She presented with acute dyspnoea on exertion.',
    );
  });

  it('strips wrapping quotes and collapses internal whitespace', () => {
    expect(cleanExampleSentence('dyspnoea', '"\u201CShe had   dyspnoea overnight.\u201D"')).toBe(
      'She had dyspnoea overnight.',
    );
  });

  it('returns an empty string when there is nothing worth showing', () => {
    expect(
      cleanExampleSentence('dyspnoea', 'The term dyspnoea was reviewed as part of OET vocabulary practice.'),
    ).toBe('');
    expect(cleanExampleSentence('dyspnoea', '')).toBe('');
  });
});

describe('repeatedExampleSentenceKeys', () => {
  it('flags a sentence shared by two or more different words', () => {
    const keys = repeatedExampleSentenceKeys([
      { term: 'dyspnoea', exampleSentence: 'The term dyspnoea was reviewed as part of OET vocabulary practice.' },
      { term: 'hypertension', exampleSentence: 'The term hypertension was reviewed as part of OET vocabulary practice.' },
    ]);
    // Same template shape but different words ⇒ different keys; the template
    // regex catches these. The repeat rule targets *identical* text.
    expect(keys.size).toBe(0);

    const identical = repeatedExampleSentenceKeys([
      { term: 'dyspnoea', exampleSentence: 'Reviewed for vocabulary practice.' },
      { term: 'hypertension', exampleSentence: 'Reviewed for vocabulary practice.' },
    ]);
    expect(identical.size).toBe(1);
  });

  it('does not flag an identical sentence reused on the same word', () => {
    const keys = repeatedExampleSentenceKeys([
      { term: 'dyspnoea', exampleSentence: 'She had dyspnoea overnight.' },
      { term: 'dyspnoea', exampleSentence: 'She had dyspnoea overnight.' },
    ]);
    expect(keys.size).toBe(0);
  });

  it('does not flag unique authored sentences', () => {
    const keys = repeatedExampleSentenceKeys([
      { term: 'dyspnoea', exampleSentence: 'She had dyspnoea overnight.' },
      { term: 'hypertension', exampleSentence: 'He has hypertension.' },
    ]);
    expect(keys.size).toBe(0);
  });
});

describe('cleanExampleSentencesForList', () => {
  it('suppresses filler, repeated text, and keeps unique real examples', () => {
    const result = cleanExampleSentencesForList([
      { term: 'dyspnoea', exampleSentence: 'She had dyspnoea overnight.' },
      { term: 'hypertension', exampleSentence: 'He has hypertension.' },
      { term: 'tachycardia', exampleSentence: 'The term tachycardia was reviewed as part of OET vocabulary practice.' },
      { term: 'oedema', exampleSentence: 'Generic filler text for every card.' },
      { term: 'cyanosis', exampleSentence: 'Generic filler text for every card.' },
    ]);

    expect(result[0]).toBe('She had dyspnoea overnight.');
    expect(result[1]).toBe('He has hypertension.');
    expect(result[2]).toBe('');
    expect(result[3]).toBe('');
    expect(result[4]).toBe('');
  });
});
