import { expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

export interface RunAxeOptions {
  include?: string[];
  exclude?: string[];
  /** Additional WCAG tags. Defaults cover WCAG 2.1 AA + 2.2 AA. */
  tags?: string[];
}

/**
 * Shared axe runner. Fails the test on serious + critical violations only;
 * minor/moderate are logged but do not fail (keeps signal-to-noise sane).
 */
export async function runAxe(page: Page, options: RunAxeOptions = {}): Promise<void> {
  const tags = options.tags ?? ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];
  let builder = new AxeBuilder({ page }).withTags(tags);
  if (options.include?.length) {
    for (const sel of options.include) builder = builder.include(sel);
  }
  if (options.exclude?.length) {
    for (const sel of options.exclude) builder = builder.exclude(sel);
  }

  const results = await builder.analyze();
  const blocking = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');

  if (blocking.length) {
    const summary = blocking
      .map((v) => `  • [${v.impact}] ${v.id} — ${v.help} (${v.nodes.length} node${v.nodes.length === 1 ? '' : 's'})`)
      .join('\n');
    expect(blocking, `axe violations:\n${summary}`).toEqual([]);
  }
}
