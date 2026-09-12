import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Regression guard for the 11 Sep 2026 keyboard / bottom-nav defect.
 *
 * Symptom: in the Android app, tapping the Practice Spelling input made the
 * bottom navigation row (Dashboard / Listening / Reading / Writing / Speaking /
 * Videos) rise up and cover the middle of the screen, over the practice card.
 * The web app was fine.
 *
 * Cause: the nav's `bottom` was offset by the soft-keyboard height
 * (`--app-keyboard-offset`). Capacitor runs `KeyboardResize.Body`, which
 * resizes <body> WITHOUT changing the layout viewport — so
 * `innerHeight - visualViewport.height` reads as a full keyboard height, and
 * adding it to a fixed element's `bottom` throws the element up the screen.
 * Docking to the true bottom is correct under both resize modes.
 *
 * The brief requires this screen in regression testing for every future app
 * update. These assertions read the real stylesheet, so any edit that
 * reintroduces the offset fails CI instead of shipping to candidates.
 */
const CSS_PATH = 'app/globals.css';

/** Body of the first rule whose selector ends at `{`. */
function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  return match?.[1] ?? '';
}

/**
 * The contract, expressed once so the assertions and the negative control
 * below cannot drift apart. Returns a list of human-readable violations.
 */
function navKeyboardViolations(css: string): string[] {
  const violations: string[] = [];

  for (const selector of ['.keyboard-safe-bottom', '.keyboard-safe-floating-bottom']) {
    const body = ruleBody(css, selector);
    if (body === '') {
      violations.push(`${selector} must exist`);
      continue;
    }
    if (/app-keyboard-offset/.test(body)) {
      violations.push(`${selector} must not consume --app-keyboard-offset`);
    }
  }

  if (!/bottom:\s*var\(--safe-area-inset-bottom\)/.test(ruleBody(css, '.keyboard-safe-bottom'))) {
    violations.push('.keyboard-safe-bottom must dock to var(--safe-area-inset-bottom)');
  }
  if (
    !/bottom:\s*calc\(var\(--safe-area-inset-bottom\)\s*\+\s*0\.75rem\)/.test(
      ruleBody(css, '.keyboard-safe-floating-bottom'),
    )
  ) {
    violations.push('.keyboard-safe-floating-bottom must dock to safe area + 0.75rem');
  }

  const hideRule = ruleBody(
    css,
    'html[data-keyboard-visible="true"] .keyboard-safe-floating-bottom',
  );
  if (hideRule === '') {
    violations.push('the keyboard-visible hide rule must exist');
  } else {
    if (!/opacity:\s*0/.test(hideRule)) violations.push('hide rule must set opacity: 0');
    if (!/transform:\s*translateY\(100%\)/.test(hideRule)) {
      violations.push('hide rule must set transform: translateY(100%)');
    }
  }

  return violations;
}

const css = readFileSync(resolve(process.cwd(), CSS_PATH), 'utf8');

describe('bottom-nav keyboard contract (app/globals.css)', () => {
  it('keeps the fixed bottom nav docked to the real bottom, never keyboard-offset', () => {
    expect(navKeyboardViolations(css)).toEqual([]);
  });

  /**
   * Negative control. Without this, a future edit could quietly neuter the
   * guard (e.g. by renaming a class) and CI would still go green while the
   * defect shipped. Reintroducing the 11 Sep 2026 offset MUST be reported.
   */
  it('detects the 11 Sep 2026 defect when it is reintroduced', () => {
    const regressed = css.replace(
      /(\.keyboard-safe-bottom\s*\{)([^}]*)(\})/,
      (_match, open: string, body: string, close: string) =>
        open + body.replace(/bottom:\s*var\(--safe-area-inset-bottom\)/, 'bottom: var(--app-keyboard-offset)') + close,
    );

    expect(regressed, 'negative control must actually mutate the stylesheet').not.toBe(css);

    const violations = navKeyboardViolations(regressed);
    expect(violations).toContain('.keyboard-safe-bottom must not consume --app-keyboard-offset');
    expect(violations).toContain(
      '.keyboard-safe-bottom must dock to var(--safe-area-inset-bottom)',
    );
  });
});
