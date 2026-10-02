// UI geometry for the Writing production QA: COLLECTORS run inside the page (page.evaluate(fn, arg), so each is
// self-contained and returns plain rects), DETECTORS are pure functions over those rects. The live harness
// (browser.mjs), the hermetic Playwright spec (tests/e2e/learner/writing-qa-detectors.spec.ts) and the unit
// tests all use these same functions. Rects are viewport CSS px: { left, top, right, bottom }.

// ---- Collectors (browser context) ---------------------------------------------------------------------------------

/** Desktop containment of the score card: every text box, the card's and each tile's scroll box, the document. */
export function collectContainment({ root, tiles }) {
  const box = (r) => ({ left: r.left, top: r.top, right: r.right, bottom: r.bottom });
  const card = document.querySelector(root);
  if (!card) return { found: false };
  const texts = [];
  const walker = document.createTreeWalker(card, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent.trim();
    if (!text) continue;
    const range = document.createRange();
    range.selectNodeContents(node);
    for (const r of range.getClientRects()) if (r.width > 0 && r.height > 0) texts.push({ text: text.slice(0, 40), rect: box(r) });
  }
  const scroll = [card, ...card.querySelectorAll(tiles)].map((el, i) => ({
    name: i === 0 ? 'card' : 'tile ' + i, scrollWidth: el.scrollWidth, clientWidth: el.clientWidth,
  }));
  const doc = { scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth };
  return { found: true, card: box(card.getBoundingClientRect()), texts, scroll, doc };
}

/**
 * Scrolls the page's scroller (the content element when it scrolls itself, else the window) to `fraction`
 * (0..1) and measures every visible text box / control of the content plus the fixed obstacles.
 * obstacles: [{ name, selector }]. Elements inside an obstacle are the obstacle's own content, not items.
 */
export function collectOverlap({ content, obstacles, fraction }) {
  const box = (r) => ({ left: r.left, top: r.top, right: r.right, bottom: r.bottom });
  const shown = (el) => {
    const s = getComputedStyle(el);
    const r = el.getBoundingClientRect();
    return s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity) > 0 && r.width > 0 && r.height > 0;
  };
  const root = document.querySelector(content) ?? document.body;
  const scroller = root.scrollHeight > root.clientHeight + 1 && /(auto|scroll)/.test(getComputedStyle(root).overflowY)
    ? root : document.scrollingElement;
  const max = scroller.scrollHeight - scroller.clientHeight;
  scroller.scrollTo({ top: Math.max(0, max) * fraction, behavior: 'instant' });
  const found = [];
  for (const o of obstacles) for (const el of document.querySelectorAll(o.selector)) if (shown(el)) found.push({ name: o.name, el });
  const inObstacle = (node) => found.some((o) => o.el.contains(node));
  const onScreen = (r) => r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth;
  const items = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent.trim();
    if (!text || inObstacle(node) || !node.parentElement || !shown(node.parentElement)) continue;
    const range = document.createRange();
    range.selectNodeContents(node);
    for (const r of range.getClientRects()) if (r.width > 0 && r.height > 0 && onScreen(r)) items.push({ kind: 'text', label: text.slice(0, 40), rect: box(r) });
  }
  for (const el of root.querySelectorAll('a[href], button, input, select, textarea, [role="button"]')) {
    const r = el.getBoundingClientRect();
    if (shown(el) && !inObstacle(el) && onScreen(r)) items.push({ kind: 'control', label: (el.getAttribute('aria-label') || el.textContent || '').trim().slice(0, 40), rect: box(r) });
  }
  const lastContentBottom = items.length ? Math.max(...items.map((i) => i.rect.bottom)) : null;
  return {
    atEnd: scroller.scrollTop >= max - 1,
    viewport: { width: innerWidth, height: innerHeight },
    obstacles: found.map((o) => ({ name: o.name, rect: box(o.el.getBoundingClientRect()) })),
    items,
    lastContentBottom,
  };
}

/** Scrolls one target into view and reports whether it is fully on screen and the top-most element there. */
export function probeTarget({ selector, index = 0, label }) {
  const el = document.querySelectorAll(selector)[index];
  if (!el) return { label, found: false };
  el.scrollIntoView({ block: 'center', behavior: 'instant' });
  const r = el.getBoundingClientRect();
  const x = Math.min(innerWidth - 1, Math.max(0, r.left + r.width / 2));
  const y = Math.min(innerHeight - 1, Math.max(0, r.top + r.height / 2));
  const hit = document.elementFromPoint(x, y);
  return {
    label, found: true,
    rect: { left: r.left, top: r.top, right: r.right, bottom: r.bottom },
    viewport: { width: innerWidth, height: innerHeight },
    topmost: Boolean(hit && (hit === el || el.contains(hit))),
  };
}

// ---- Detectors (pure) ---------------------------------------------------------------------------------------------

/** True when two rects overlap by more than `min` px in both directions (touching edges do not count). */
export function rectsIntersect(a, b, min = 1) {
  return Math.min(a.right, b.right) - Math.max(a.left, b.left) > min && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > min;
}

/** Desktop containment: every text box inside the card (+-tol px), no scroll overflow, no document overflow. */
export function containmentProblems(sample, tol = 1) {
  if (!sample?.found) return ['the results score panel was not found'];
  const { card } = sample;
  const problems = [];
  for (const t of sample.texts) {
    const r = t.rect;
    if (r.left < card.left - tol || r.right > card.right + tol || r.top < card.top - tol || r.bottom > card.bottom + tol) {
      problems.push(`text "${t.text}" sticks out of the score card`);
    }
  }
  for (const s of sample.scroll) if (s.scrollWidth > s.clientWidth + tol) problems.push(`${s.name} overflows horizontally (${s.scrollWidth} > ${s.clientWidth})`);
  if (sample.doc.scrollWidth > sample.doc.clientWidth + tol) problems.push(`the page scrolls horizontally (${sample.doc.scrollWidth} > ${sample.doc.clientWidth})`);
  return problems;
}

/**
 * Any text box or control under an obstacle (bottom nav, floating handle); `names` limits the obstacles.
 * @param {any} sample { obstacles, items }
 * @param {any} [names]
 */
export function overlapProblems(sample, names = null) {
  const { obstacles, items } = sample;
  const problems = [];
  for (const o of obstacles) {
    if (names && !names.includes(o.name)) continue;
    for (const item of items) if (rectsIntersect(item.rect, o.rect)) problems.push(`${item.kind} "${item.label}" is under the ${o.name}`);
  }
  return problems;
}

/** At the end of the page the last content must end at least `min` px above the bottom nav. */
export function clearanceProblems({ lastContentBottom, obstacles }, navName = 'bottom nav', min = 8) {
  const nav = obstacles.find((o) => o.name === navName);
  if (!nav || lastContentBottom === null) return [];
  const gap = nav.rect.top - lastContentBottom;
  return gap < min ? [`the last content ends ${Math.round(gap)} px above the ${navName} (${min} px needed)`] : [];
}

/** Each probed target: found, fully inside the viewport, top-most at its centre, clear of every obstacle. */
export function targetProblems(probes, obstacles) {
  const problems = [];
  for (const p of probes) {
    if (!p.found) { problems.push(`${p.label} was not found`); continue; }
    if (p.rect.top < 0 || p.rect.bottom > p.viewport.height || p.rect.left < 0 || p.rect.right > p.viewport.width) problems.push(`${p.label} is not fully on screen`);
    if (!p.topmost) problems.push(`${p.label} is covered by another element`);
    for (const o of obstacles) if (rectsIntersect(p.rect, o.rect)) problems.push(`${p.label} is under the ${o.name}`);
  }
  return problems;
}

/**
 * Native-shell emulation is only proven when a native-only control rendered, otherwise never PASS: at >= lg
 * (1024 px) the quick-access handle; below lg (handle hidden) BOTH native entries of the opened mobile menu.
 */
export function positiveControl({ width, handleVisible, menuEntries }) {
  const proven = width >= 1024 ? handleVisible : menuEntries === 2;
  return proven ? 'PROVEN' : 'NOT_PROVEN';
}

export function mobileVerdict({ control, problems }) {
  if (problems.length) return 'FAIL';
  return control === 'PROVEN' ? 'PASS' : 'NOT_PROVEN';
}
