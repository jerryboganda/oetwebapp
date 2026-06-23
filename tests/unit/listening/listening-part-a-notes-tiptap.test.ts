/**
 * Round-trip tests for the Part A grammar <-> TipTap document converters.
 *
 * The canonical persisted format stays the `____`/`## `/`- ` notes grammar
 * (`lib/listening-part-a-notes.ts`). The WYSIWYG editor is a *projection*: we
 * deserialize grammar -> TipTap JSON on load and serialize back on every change.
 * Therefore the load->serialize round-trip MUST be the identity on canonical
 * bodies, or authoring would silently mutate the 5 production papers.
 */
import { describe, it, expect } from 'vitest';
import { countGaps } from '@/lib/listening-part-a-notes';
import { grammarToTiptapDoc, tiptapDocToGrammar } from '@/lib/listening-part-a-notes-tiptap';

// Mirrors components/domain/listening/admin/PartANotesBuilder.tsx PART_A_SCAFFOLD.
const SCAFFOLD = [
  'You hear a [professional, e.g. primary-care doctor] talking to a patient called [patient name]. For questions 1–12, complete the notes with a word or short phrase that you hear.',
  '',
  'You now have thirty seconds to look at the notes.',
  '',
  'Patient: [patient name]',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '- [note detail], ____ and [trailing detail]',
  '  - [sub-detail] ____',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '',
].join('\n');

// Shaped like a real production Part A extract body (post detectPastedGaps).
const PROD_BODY = [
  'Patient: Hayley Dove',
  '',
  '## Background to condition',
  '- endometriosis for many years following birth of son',
  '  - discomfort from episodes of bloating, ____ and fatigue',
  '- developed ____ pain',
  '- worsening condition affected her work as a ____',
  '- diagnosis of ____',
  '---',
  '## Current concerns',
  '- now experiencing stiffness in joints and ____',
  '- tendency to become excessively ____',
].join('\n');

// Exercises the new `### ` sub-heading branch + a gap inside a context line.
const SUBHEADING_BODY = [
  '## History',
  '- condition: ____',
  '  - onset: ____',
  '### Sub-section',
  'context line with a gap ____ then more text',
].join('\n');

describe('grammarToTiptapDoc / tiptapDocToGrammar round-trip', () => {
  it('round-trips the authoring scaffold unchanged', () => {
    expect(tiptapDocToGrammar(grammarToTiptapDoc(SCAFFOLD))).toBe(SCAFFOLD);
  });

  it('round-trips a realistic production body unchanged', () => {
    expect(tiptapDocToGrammar(grammarToTiptapDoc(PROD_BODY))).toBe(PROD_BODY);
  });

  it('round-trips a body with a ### sub-heading and a context-line gap', () => {
    expect(tiptapDocToGrammar(grammarToTiptapDoc(SUBHEADING_BODY))).toBe(SUBHEADING_BODY);
  });

  it('round-trips an empty body to an empty string', () => {
    expect(tiptapDocToGrammar(grammarToTiptapDoc(''))).toBe('');
  });
});

describe('inline blank with trailing text', () => {
  it('keeps text flowing after a blank on the same line', () => {
    const body = 'episodes of bloating, ____ and fatigue';
    const out = tiptapDocToGrammar(grammarToTiptapDoc(body));
    expect(out).toBe(body);
    expect(out).toContain('____ and fatigue');
  });
});

describe('gap fidelity', () => {
  it('emits exactly one partAGap inline node per source gap', () => {
    const body = '- a ____ b ____\n- c ____';
    const doc = grammarToTiptapDoc(body);
    const gapNodes = JSON.stringify(doc).match(/"type":"partAGap"/g);
    expect(gapNodes).toHaveLength(3);
    expect(countGaps(tiptapDocToGrammar(doc))).toBe(3);
  });

  it('never turns literal "(1)(2)(3)" text into gaps (the original bug)', () => {
    const body = '- item (1)(2)(3) text';
    const doc = grammarToTiptapDoc(body);
    expect(tiptapDocToGrammar(doc)).toBe(body);
    expect(countGaps(tiptapDocToGrammar(doc))).toBe(0);
    expect(JSON.stringify(doc)).not.toContain('"type":"partAGap"');
  });
});
