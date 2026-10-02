// Hermetic layouts that prove the geometry detectors (good ones must pass, bad ones must be flagged). Shared by
// tests/e2e/learner/writing-qa-detectors.spec.ts (Playwright) and geometry.browser.test.ts (vitest + Chrome).

export const OBSTACLES = [
  { name: 'bottom nav', selector: 'nav[aria-label="Mobile navigation"]' },
  { name: 'handle', selector: '[data-testid="shell-controls-handle"]' },
];
export const CARD = { root: '[data-testid="results-score-panel"]', tiles: '[data-testid="results-score-stat"]' };

export function scoreCard(label, width) {
  return '<div data-testid="results-score-panel" style="width:' + width + 'px;overflow:hidden;border:1px solid #333;padding:8px;font:16px sans-serif">'
    + '<div data-testid="results-score-stat" style="overflow:hidden;white-space:nowrap">' + label + '</div>'
    + '<div data-testid="results-score-stat">342/500</div></div>';
}

/** A phone report page: 40 lines, a fixed bottom nav, optional floating handle and a last button. */
export function phonePage({ padding, handle = false, lastButton = false }) {
  const lines = Array.from({ length: 40 }, (_, i) => '<p style="margin:0 0 12px">Correction ' + (i + 1)
    + ': the purpose of the letter should be stated in the first sentence.</p>').join('');
  const last = lastButton ? '<button id="last">View all corrections</button>' : '<p>Last line of the report.</p>';
  const fab = handle ? '<button data-testid="shell-controls-handle" style="position:fixed;right:0;top:65%;width:64px;height:40px">Quick</button>' : '';
  return '<body style="margin:0;font:16px sans-serif"><main id="main-content" style="padding:16px 16px ' + padding + 'px">'
    + lines + last + '</main>' + fab
    + '<nav aria-label="Mobile navigation" style="position:fixed;left:8px;right:8px;bottom:8px;height:56px;background:#eee">Home</nav></body>';
}
