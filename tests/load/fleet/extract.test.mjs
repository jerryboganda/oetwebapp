import assert from 'node:assert/strict';
import test from 'node:test';
import {
  cardIds, extractGuidIds, extractQuestionIds, paperIds, readingPaperIds, readingPartAQuestionIds,
} from './extract.mjs';

test('extractQuestionIds finds question ids under any nesting and deduplicates', () => {
  const session = {
    paper: { id: 'p1' },
    parts: [
      { partCode: 'A', questions: [{ id: 'q1' }, { id: 'q2' }] },
      { partCode: 'B', sections: [{ listeningQuestions: [{ id: 'q3' }, { id: 'q1' }] }] },
    ],
    other: [{ questionText: 'x' }],
  };
  assert.deepEqual(extractQuestionIds(session), ['q1', 'q2', 'q3']);
  assert.deepEqual(extractQuestionIds(null), []);
  assert.deepEqual(extractQuestionIds('text'), []);
});

test('extractQuestionIds stops at a sane depth', () => {
  let deep = { questions: [{ id: 'deep' }] };
  for (let i = 0; i < 20; i += 1) deep = { child: deep };
  assert.deepEqual(extractQuestionIds(deep), []);
});

test('extractGuidIds picks only GUID ids', () => {
  const payload = {
    items: [
      { id: '3f2504e0-4f89-41d3-9a0c-0305e82c3301', title: 'a' },
      { id: 'not-a-guid' },
      { nested: { id: '3F2504E0-4F89-41D3-9A0C-0305E82C3302' } },
    ],
  };
  assert.deepEqual(extractGuidIds(payload), [
    '3f2504e0-4f89-41d3-9a0c-0305e82c3301',
    '3F2504E0-4F89-41D3-9A0C-0305E82C3302',
  ]);
});

test('readingPaperIds keeps only papers the learner may open', () => {
  const home = {
    papers: [
      { id: 'a', entitlement: { allowed: true } },
      { id: 'b', entitlement: { allowed: false } },
      { id: 'c' },
      { title: 'no id' },
    ],
  };
  assert.deepEqual(readingPaperIds(home), ['a', 'c']);
  assert.deepEqual(readingPaperIds(null), []);
});

test('readingPartAQuestionIds reads Part A sections and part-level questions only', () => {
  const structure = {
    parts: [
      { partCode: 'A', sections: [{ questions: [{ id: 'a1' }, { id: 'a2' }] }], questions: [{ id: 'a2' }, { id: 'a3' }] },
      { partCode: 'B', questions: [{ id: 'b1' }] },
      { partCode: 'C', sections: [{ questions: [{ id: 'c1' }] }] },
    ],
  };
  assert.deepEqual(readingPartAQuestionIds(structure), ['a1', 'a2', 'a3']);
  assert.deepEqual(readingPartAQuestionIds({}), []);
});

test('cardIds and paperIds accept the shapes the API returns', () => {
  assert.deepEqual(cardIds([{ id: 'c1' }, { id: 'c2' }, {}]), ['c1', 'c2']);
  assert.deepEqual(cardIds({ items: [{ id: 'c3' }] }), ['c3']);
  assert.deepEqual(cardIds(null), []);
  assert.deepEqual(paperIds([{ id: 'p1' }, { id: 2 }]), ['p1']);
  assert.deepEqual(paperIds({}), []);
});
