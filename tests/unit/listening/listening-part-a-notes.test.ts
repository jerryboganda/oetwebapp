import { describe, it, expect } from 'vitest';
import {
  PART_A_GAP_MARKER,
  countGaps,
  parseNotesDocument,
  detectPastedGaps,
} from '@/lib/listening-part-a-notes';

// ── 1. countGaps ─────────────────────────────────────────────────────────────

describe('countGaps', () => {
  it('counts two separated gap markers', () => {
    expect(countGaps('a ____ b ____')).toBe(2);
  });

  it('counts a contiguous run as one gap', () => {
    expect(countGaps('x ________ y')).toBe(1);
  });

  it('returns 0 for empty string', () => {
    expect(countGaps('')).toBe(0);
  });

  it('returns 0 for null', () => {
    expect(countGaps(null)).toBe(0);
  });

  it('returns 0 for undefined', () => {
    expect(countGaps(undefined)).toBe(0);
  });

  it('counts exactly four underscores as one gap', () => {
    expect(countGaps('start ____ end')).toBe(1);
  });

  it('counts three gaps', () => {
    expect(countGaps('____ text ____ more text ____')).toBe(3);
  });
});

// ── 2. parseNotesDocument ─────────────────────────────────────────────────────

describe('parseNotesDocument', () => {
  it('parses a heading line', () => {
    const nodes = parseNotesDocument('## Background to condition');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('heading');
    if (nodes[0].kind === 'heading') {
      expect(nodes[0].segments).toEqual([{ kind: 'text', text: 'Background to condition' }]);
    }
  });

  it('parses a level-1 bullet', () => {
    const nodes = parseNotesDocument('- foo');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('bullet');
    if (nodes[0].kind === 'bullet') {
      expect(nodes[0].level).toBe(1);
      expect(nodes[0].segments).toEqual([{ kind: 'text', text: 'foo' }]);
    }
  });

  it('parses a level-2 sub-bullet with two-space indent', () => {
    const nodes = parseNotesDocument('  - bar');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('bullet');
    if (nodes[0].kind === 'bullet') {
      expect(nodes[0].level).toBe(2);
      expect(nodes[0].segments).toEqual([{ kind: 'text', text: 'bar' }]);
    }
  });

  it('parses a level-2 sub-bullet with tab indent', () => {
    const nodes = parseNotesDocument('\t- baz');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('bullet');
    if (nodes[0].kind === 'bullet') {
      expect(nodes[0].level).toBe(2);
      expect(nodes[0].segments).toEqual([{ kind: 'text', text: 'baz' }]);
    }
  });

  it('parses a divider line', () => {
    const nodes = parseNotesDocument('---');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('divider');
  });

  it('parses a divider with surrounding whitespace', () => {
    const nodes = parseNotesDocument('  ---  ');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('divider');
  });

  it('parses a context line (plain text)', () => {
    const nodes = parseNotesDocument('Patient reports pain');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('context');
  });

  it('skips blank / whitespace-only lines', () => {
    const nodes = parseNotesDocument('- foo\n\n- bar');
    // only two non-empty nodes
    expect(nodes).toHaveLength(2);
    expect(nodes.every((n) => n.kind === 'bullet')).toBe(true);
  });

  it('assigns global gap ordinals across multiple lines', () => {
    const body = [
      '## Heading with ____',
      '- bullet with ____',
      'context line ____ more',
    ].join('\n');
    const nodes = parseNotesDocument(body);
    // Collect all gap segments across all nodes
    const gapSegments: Array<{ kind: 'gap'; gapIndex: number }> = [];
    for (const node of nodes) {
      if (node.kind !== 'divider') {
        for (const seg of node.segments) {
          if (seg.kind === 'gap') gapSegments.push(seg);
        }
      }
    }
    expect(gapSegments).toHaveLength(3);
    expect(gapSegments[0].gapIndex).toBe(0);
    expect(gapSegments[1].gapIndex).toBe(1);
    expect(gapSegments[2].gapIndex).toBe(2);
  });

  it('returns empty array for null/undefined/empty', () => {
    expect(parseNotesDocument(null)).toEqual([]);
    expect(parseNotesDocument(undefined)).toEqual([]);
    expect(parseNotesDocument('')).toEqual([]);
  });

  it('parses a heading line containing a gap', () => {
    const nodes = parseNotesDocument('## Diagnosis of ____');
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('heading');
    if (nodes[0].kind === 'heading') {
      const gapSeg = nodes[0].segments.find((s) => s.kind === 'gap');
      expect(gapSeg).toBeDefined();
      if (gapSeg?.kind === 'gap') {
        expect(gapSeg.gapIndex).toBe(0);
      }
    }
  });
});

// ── 3. detectPastedGaps — realistic OET Extract-1 ────────────────────────────

const EXTRACT_1_RAW = `Patient    Hayley Dove

Background to condition
- endometriosis for many years following birth of son
  - discomfort from episodes of bloating, (1)__________ and fatigue
- developed (2)__________ pain
- worsening condition affected her work as a (3)__________
- diagnosis of (4)__________
- underwent (5)__________ - procedure eliminated symptoms
- set up business as a (6)__________

Development of new symptoms and treatment
- particularly noticeable after (7)__________
- (8)__________ initially suspected
- n.b. (9)__________ as an infant
- treated via venesection - has some (10)__________ as a result

Current concerns
- now experiencing stiffness in joints and (11)__________
- tendency to become excessively (12)__________`;

describe('detectPastedGaps', () => {
  it('finds 12 gaps in the realistic OET extract', () => {
    const { gapCount } = detectPastedGaps(EXTRACT_1_RAW);
    expect(gapCount).toBe(12);
  });

  it('removes (n) notation from the body', () => {
    const { body } = detectPastedGaps(EXTRACT_1_RAW);
    expect(/\(\d+\)/.test(body)).toBe(false);
  });

  it('preserves the heading text "Background to condition"', () => {
    const { body } = detectPastedGaps(EXTRACT_1_RAW);
    expect(body).toContain('Background to condition');
  });

  it('preserves the heading text "Current concerns"', () => {
    const { body } = detectPastedGaps(EXTRACT_1_RAW);
    expect(body).toContain('Current concerns');
  });

  it('security: strips script tags and yields 1 gap', () => {
    const raw = '<p>Dose 5mg</p><script>steal()</script> (1)____';
    const { body, gapCount } = detectPastedGaps(raw);
    expect(gapCount).toBe(1);
    expect(/<script/i.test(body)).toBe(false);
    expect(/steal\(\)/.test(body)).toBe(false);
  });

  it('canonical gap marker is 4 underscores', () => {
    expect(PART_A_GAP_MARKER).toBe('____');
  });

  it('normalises a plain run of 8 underscores to the canonical marker', () => {
    const { body, gapCount } = detectPastedGaps('Pain in ________ area');
    expect(gapCount).toBe(1);
    expect(body).toContain('____');
    // Should not contain 8 underscores (normalised to 4)
    expect(/_{5,}/.test(body)).toBe(false);
  });
});
