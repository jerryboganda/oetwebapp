/**
 * Rulebook Loader
 *
 * Loads the canonical JSON rulebooks from `rulebooks/` and exposes a
 * lookup interface. Loads are deterministic and cached per
 * (kind, profession) tuple.
 *
 * The TypeScript engine mirrors the .NET engine — keep behaviour identical.
 */

import writingMedicineV1 from '../../rulebooks/writing/medicine/rulebook.v1.json';
import speakingMedicineV1 from '../../rulebooks/speaking/medicine/rulebook.v1.json';
import writingAssessment from '../../rulebooks/writing/common/assessment-criteria.json';
import speakingAssessment from '../../rulebooks/speaking/common/assessment-criteria.json';

import type {
  ExamProfession,
  Rulebook,
  RuleKind,
  Rule,
} from './types';

type RulebookKey = `${RuleKind}:${ExamProfession}`;

// Central registry — static imports so Next.js bundlers and
// Vitest both resolve at compile time. Extend here when adding a
// profession's rulebook (e.g. writing/nursing/rulebook.v1.json).
const RULEBOOKS: Record<string, Rulebook> = {
  'writing:medicine': writingMedicineV1 as unknown as Rulebook,
  'speaking:medicine': speakingMedicineV1 as unknown as Rulebook,
};

const ASSESSMENT_CRITERIA = {
  writing: writingAssessment as unknown,
  speaking: speakingAssessment as unknown,
};

export class RulebookNotFoundError extends Error {
  constructor(kind: RuleKind, profession: ExamProfession) {
    super(
      `OET rulebook not found for kind="${kind}" profession="${profession}". ` +
        `Only rulebooks listed in lib/rulebook/loader.ts are available; add the ` +
        `JSON file under rulebooks/${kind}/${profession}/ and register it in the ` +
        `loader before using it.`,
    );
    this.name = 'RulebookNotFoundError';
  }
}

export function loadRulebook(kind: RuleKind, profession: ExamProfession): Rulebook {
  const key: RulebookKey = `${kind}:${profession}`;
  const book = RULEBOOKS[key];
  if (!book) throw new RulebookNotFoundError(kind, profession);
  return book;
}

export function listRulebooks(): Array<Pick<Rulebook, 'kind' | 'profession' | 'version'>> {
  return Object.values(RULEBOOKS).map((book) => ({
    kind: book.kind,
    profession: book.profession,
    version: book.version,
  }));
}

export function findRule(
  kind: RuleKind,
  profession: ExamProfession,
  ruleId: string,
): Rule | undefined {
  const book = loadRulebook(kind, profession);
  return book.rules.find((r) => r.id === ruleId);
}

export function getAssessmentCriteria(kind: RuleKind): unknown {
  return ASSESSMENT_CRITERIA[kind];
}

/** Filter rules that apply to a given letter type or card type. */
export function rulesApplicableTo(
  book: Rulebook,
  context: string,
): Rule[] {
  return book.rules.filter((rule) => {
    if (!rule.appliesTo || rule.appliesTo === 'all') return true;
    if (Array.isArray(rule.appliesTo)) return rule.appliesTo.includes(context);
    return false;
  });
}

/** Convenience getter for the critical rules only (used by AI grounding prompts). */
export function criticalRules(book: Rulebook): Rule[] {
  return book.rules.filter((r) => r.severity === 'critical');
}
