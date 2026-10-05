// Pure response-shape helpers for content discovery and the exam flows (no k6 imports; node-tested).
// They are deliberately tolerant: the harness must not crash on a payload that grew a field.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Ids of question objects found under any array whose key ends in "question(s)", at any depth. */
export function extractQuestionIds(value, found = [], depth = 0) {
  if (depth > 8 || value === null || typeof value !== 'object') return found;
  if (Array.isArray(value)) {
    for (const item of value) extractQuestionIds(item, found, depth + 1);
    return found;
  }
  for (const key of Object.keys(value)) {
    const child = value[key];
    if (/questions?$/i.test(key) && Array.isArray(child)) {
      for (const q of child) {
        if (q && typeof q.id === 'string' && found.indexOf(q.id) === -1) found.push(q.id);
      }
    } else {
      extractQuestionIds(child, found, depth + 1);
    }
  }
  return found;
}

/** Every string `id` property (at any depth) that is a GUID; used for Writing scenario ids. */
export function extractGuidIds(value, found = [], depth = 0) {
  if (depth > 8 || value === null || typeof value !== 'object') return found;
  if (Array.isArray(value)) {
    for (const item of value) extractGuidIds(item, found, depth + 1);
    return found;
  }
  if (typeof value.id === 'string' && GUID.test(value.id) && found.indexOf(value.id) === -1) found.push(value.id);
  for (const key of Object.keys(value)) extractGuidIds(value[key], found, depth + 1);
  return found;
}

/** Papers the learner may open from GET /v1/reading-papers/home: `papers[].id` with an allowed entitlement. */
export function readingPaperIds(home) {
  const papers = home && Array.isArray(home.papers) ? home.papers : [];
  const ids = [];
  for (const paper of papers) {
    if (!paper || typeof paper.id !== 'string') continue;
    const allowed = paper.entitlement ? paper.entitlement.allowed !== false : true;
    if (allowed) ids.push(paper.id);
  }
  return ids;
}

/** Part A question ids from GET /v1/reading-papers/papers/{id}/structure (sections and part level, deduplicated). */
export function readingPartAQuestionIds(structure) {
  const parts = structure && Array.isArray(structure.parts) ? structure.parts : [];
  const ids = [];
  const add = (question) => {
    if (question && typeof question.id === 'string' && ids.indexOf(question.id) === -1) ids.push(question.id);
  };
  for (const part of parts) {
    if (!part || String(part.partCode).toUpperCase() !== 'A') continue;
    for (const section of Array.isArray(part.sections) ? part.sections : []) {
      for (const question of Array.isArray(section.questions) ? section.questions : []) add(question);
    }
    for (const question of Array.isArray(part.questions) ? part.questions : []) add(question);
  }
  return ids;
}

/** Role-play card ids from GET /v1/speaking/role-play-cards (a bare array, or { items: [...] }). */
export function cardIds(payload) {
  const list = Array.isArray(payload) ? payload : (payload && Array.isArray(payload.items) ? payload.items : []);
  const ids = [];
  for (const card of list) if (card && typeof card.id === 'string') ids.push(card.id);
  return ids;
}

/** Paper ids from GET /v1/papers (a bare array of { id }). */
export function paperIds(payload) {
  return Array.isArray(payload) ? payload.filter((p) => p && typeof p.id === 'string').map((p) => p.id) : [];
}
