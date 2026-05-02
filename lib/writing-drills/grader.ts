/**
 * ============================================================================
 * Writing Drills — Pure Deterministic Graders
 * ============================================================================
 *
 * Each grader is pure: (drill, submission) -> DrillGradeResult.
 * No I/O, no AI calls, fully synchronous, fully testable.
 *
 * Design notes:
 *   - Per-item correctness is decided by the AUTHORED ground truth in the JSON.
 *   - Pass threshold is 70% by default (0.8 for difficulty=exam) — chosen to
 *     align with the rulebook's "Grade B = 5/7 on most criteria" guidance.
 *   - Error tags map to WRITING markdown section 20.
 * ============================================================================
 */

import type {
  AbbreviationDrill,
  Drill,
  DrillFindingPerItem,
  DrillGradeResult,
  DrillSubmission,
  ExpansionDrill,
  ExpansionTarget,
  OpeningDrill,
  OrderingDrill,
  RelevanceDrill,
  ToneDrill,
} from './types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const passThreshold = (d: Drill): number => (d.difficulty === 'exam' ? 0.8 : 0.7);

function normalise(s: string): string {
  return s.toLowerCase().replace(/\s+/g, ' ').trim();
}

function containsToken(haystack: string, needle: string): boolean {
  // Word-boundary contains for short tokens; substring otherwise.
  const n = normalise(needle);
  const h = normalise(haystack);
  if (n.length <= 4) {
    const re = new RegExp(`\\b${n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`);
    return re.test(h);
  }
  return h.includes(n);
}

function buildResult(
  drill: Drill,
  findings: DrillFindingPerItem[],
  errorTags: string[],
  summaryWhenPassed: string,
  summaryWhenFailed: string,
): DrillGradeResult {
  const correct = findings.filter((f) => f.correct).length;
  const score = findings.length === 0 ? 0 : correct / findings.length;
  const passed = score >= passThreshold(drill);
  return {
    drillId: drill.id,
    type: drill.type,
    score,
    scorePercent: Math.round(score * 100),
    passed,
    findings,
    errorTags: Array.from(new Set(errorTags)),
    summary: passed ? summaryWhenPassed : summaryWhenFailed,
  };
}

// ---------------------------------------------------------------------------
// 1. Relevance
// ---------------------------------------------------------------------------

export function gradeRelevance(
  drill: RelevanceDrill,
  submission: Extract<DrillSubmission, { type: 'relevance' }>,
): DrillGradeResult {
  const findings: DrillFindingPerItem[] = [];
  const tags: string[] = [];

  for (const note of drill.notes) {
    const chosen = submission.selections[note.id];
    let correct = false;

    if (note.expected === 'optional') {
      // Either choice is acceptable — credit the learner for engaging.
      correct = chosen === 'relevant' || chosen === 'irrelevant';
    } else {
      correct = chosen === note.expected;
    }

    if (!correct) {
      if (note.expected === 'relevant') tags.push('missing_key_content');
      if (note.expected === 'irrelevant') tags.push('irrelevant_content');
    }

    findings.push({
      itemId: note.id,
      correct,
      feedback: correct
        ? `Correct — ${note.rationale}`
        : `Expected: ${note.expected}. ${note.rationale}`,
    });
  }

  return buildResult(
    drill,
    findings,
    tags,
    'Strong content selection — you correctly distinguished relevant case-note information from noise.',
    'Review the notes you mis-classified. Content selection is the single most important OET Writing skill.',
  );
}

// ---------------------------------------------------------------------------
// 2. Opening
// ---------------------------------------------------------------------------

export function gradeOpening(
  drill: OpeningDrill,
  submission: Extract<DrillSubmission, { type: 'opening' }>,
): DrillGradeResult {
  const choice = drill.choices.find((c) => c.id === submission.choiceId);
  if (!choice) {
    return buildResult(
      drill,
      [
        {
          itemId: 'opening',
          correct: false,
          feedback: 'No opening selected.',
        },
      ],
      ['unclear_purpose'],
      '',
      'No opening selected — pick the option that states the purpose first.',
    );
  }

  const correct = choice.quality === 'best';
  const acceptable = choice.quality === 'acceptable';
  const findings: DrillFindingPerItem[] = [
    {
      itemId: choice.id,
      correct: correct || acceptable,
      feedback: choice.rationale,
    },
  ];
  const tags = correct ? [] : choice.flags;

  // Treat 'acceptable' as a partial credit pass: 0.7 score so the drill is
  // marked passed but errorTags surface flags so the student still learns.
  const overrideResult: DrillGradeResult = {
    drillId: drill.id,
    type: drill.type,
    score: correct ? 1 : acceptable ? 0.7 : 0,
    scorePercent: correct ? 100 : acceptable ? 70 : 0,
    passed: correct || acceptable,
    findings,
    errorTags: Array.from(new Set(tags)),
    summary: correct
      ? 'Excellent — your opening states the purpose immediately and matches the reader.'
      : acceptable
        ? 'Acceptable opening, but a stronger version foregrounds the purpose more directly.'
        : 'This opening does not state the purpose clearly. Re-read section 7 of the rulebook.',
  };
  return overrideResult;
}

// ---------------------------------------------------------------------------
// 3. Ordering
// ---------------------------------------------------------------------------

export function gradeOrdering(
  drill: OrderingDrill,
  submission: Extract<DrillSubmission, { type: 'ordering' }>,
): DrillGradeResult {
  const findings: DrillFindingPerItem[] = drill.expectedOrder.map((expectedId, idx) => {
    const submittedId = submission.order[idx];
    const correct = submittedId === expectedId;
    return {
      itemId: expectedId,
      correct,
      feedback: correct
        ? `Position ${idx + 1}: correct.`
        : `Position ${idx + 1} should be the "${drill.items.find((i) => i.id === expectedId)?.role}" paragraph.`,
    };
  });

  const tags = findings.some((f) => !f.correct) ? ['poor_paragraphing'] : [];

  return buildResult(
    drill,
    findings,
    tags,
    'Logical structure — your ordering moves the reader from purpose to action.',
    'Re-order so the reader gets purpose first, then context, then the action you want them to take.',
  );
}

// ---------------------------------------------------------------------------
// 4. Sentence expansion
// ---------------------------------------------------------------------------

function gradeExpansionTarget(
  target: ExpansionTarget,
  answer: string | undefined,
): { finding: DrillFindingPerItem; tags: string[] } {
  const tags: string[] = [];
  const trimmed = (answer ?? '').trim();
  if (!trimmed) {
    tags.push('missing_key_content');
    return {
      finding: { itemId: target.id, correct: false, feedback: 'No answer provided.' },
      tags,
    };
  }

  const reasons: string[] = [];

  for (const must of target.mustInclude) {
    if (!containsToken(trimmed, must)) {
      reasons.push(`Missing required information: "${must}".`);
      tags.push('inaccurate_transfer');
    }
  }

  for (const banned of target.mustNotInclude) {
    if (containsToken(trimmed, banned)) {
      reasons.push(`Avoid: "${banned}" (note-form not appropriate in a letter).`);
      tags.push('grammar_articles');
    }
  }

  // Note-form heuristic: standalone slashes and dashes commonly indicate notes.
  const hasNoteFormGlyphs = /[\\/]\s|\s[\\/]\s|^- |\n- /.test(trimmed);
  if (hasNoteFormGlyphs) {
    reasons.push('Looks like note form — convert to full sentences.');
    tags.push('grammar_articles');
  }

  const correct = reasons.length === 0;
  return {
    finding: {
      itemId: target.id,
      correct,
      feedback: correct
        ? `Strong expansion. Exemplar: ${target.exemplar}`
        : `${reasons.join(' ')} Exemplar: ${target.exemplar}`,
    },
    tags,
  };
}

export function gradeExpansion(
  drill: ExpansionDrill,
  submission: Extract<DrillSubmission, { type: 'expansion' }>,
): DrillGradeResult {
  const findings: DrillFindingPerItem[] = [];
  const tags: string[] = [];

  for (const target of drill.targets) {
    const { finding, tags: t } = gradeExpansionTarget(target, submission.answers[target.id]);
    findings.push(finding);
    tags.push(...t);
  }

  return buildResult(
    drill,
    findings,
    tags,
    'Excellent — note-form ideas converted into clinically appropriate full sentences.',
    'Some answers still read like notes. Use complete sentences with subject + verb and proper articles.',
  );
}

// ---------------------------------------------------------------------------
// 5. Tone converter
// ---------------------------------------------------------------------------

export function gradeTone(
  drill: ToneDrill,
  submission: Extract<DrillSubmission, { type: 'tone' }>,
): DrillGradeResult {
  const findings: DrillFindingPerItem[] = [];
  const tags: string[] = [];

  for (const item of drill.items) {
    const ans = (submission.answers[item.id] ?? '').trim();
    if (!ans) {
      findings.push({ itemId: item.id, correct: false, feedback: 'No answer provided.' });
      tags.push('informal_tone');
      continue;
    }
    const matched = item.acceptableFormal.some((acc) => containsToken(ans, acc));
    const containsForbidden = item.forbidden.some((f) => containsToken(ans, f));
    const correct = matched && !containsForbidden;
    if (!correct) tags.push('informal_tone');
    findings.push({
      itemId: item.id,
      correct,
      feedback: correct
        ? `Good — professional register. Exemplar: ${item.exemplar}`
        : containsForbidden
          ? `Still informal. Avoid words like "${item.forbidden.find((f) => containsToken(ans, f))}". Exemplar: ${item.exemplar}`
          : `Acceptable formal phrasing not detected. Exemplar: ${item.exemplar}`,
    });
  }

  return buildResult(
    drill,
    findings,
    tags,
    'Tone is consistently professional and reader-appropriate.',
    'Lift the register: replace casual verbs and contractions with neutral clinical phrasing.',
  );
}

// ---------------------------------------------------------------------------
// 6. Abbreviation
// ---------------------------------------------------------------------------

export function gradeAbbreviation(
  drill: AbbreviationDrill,
  submission: Extract<DrillSubmission, { type: 'abbreviation' }>,
): DrillGradeResult {
  const findings: DrillFindingPerItem[] = [];
  const tags: string[] = [];

  for (const item of drill.items) {
    const chosen = submission.answers[item.id];
    const correct = chosen === item.expected;
    if (!correct) tags.push('abbreviation_issue');
    findings.push({
      itemId: item.id,
      correct,
      feedback: correct
        ? `Correct — ${item.rationale}`
        : `Expected: ${item.expected === 'expand' ? `expand to "${item.expansion}"` : 'keep abbreviated'}. ${item.rationale}`,
    });
  }

  return buildResult(
    drill,
    findings,
    tags,
    'Good judgment — abbreviations matched the reader.',
    'Always ask: would this reader recognise the abbreviation? Expand when in doubt.',
  );
}

// ---------------------------------------------------------------------------
// Dispatcher
// ---------------------------------------------------------------------------

export function gradeDrill(drill: Drill, submission: DrillSubmission): DrillGradeResult {
  if (drill.type !== submission.type) {
    throw new Error(`Drill type mismatch: drill="${drill.type}" submission="${submission.type}"`);
  }
  switch (drill.type) {
    case 'relevance':
      return gradeRelevance(drill, submission as Extract<DrillSubmission, { type: 'relevance' }>);
    case 'opening':
      return gradeOpening(drill, submission as Extract<DrillSubmission, { type: 'opening' }>);
    case 'ordering':
      return gradeOrdering(drill, submission as Extract<DrillSubmission, { type: 'ordering' }>);
    case 'expansion':
      return gradeExpansion(drill, submission as Extract<DrillSubmission, { type: 'expansion' }>);
    case 'tone':
      return gradeTone(drill, submission as Extract<DrillSubmission, { type: 'tone' }>);
    case 'abbreviation':
      return gradeAbbreviation(
        drill,
        submission as Extract<DrillSubmission, { type: 'abbreviation' }>,
      );
    default: {
      const _exhaustive: never = drill;
      throw new Error(`Unhandled drill type: ${JSON.stringify(_exhaustive)}`);
    }
  }
}
