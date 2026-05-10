/**
 * Rulebook public surface.
 *
 * Import from `@/lib/rulebook` — never deep-import individual files from
 * downstream code. Keeps the engine layer swappable.
 */

export * from './types';
export * from './loader';
export * from './context';
export * from './writing-rules';
export * from './writing-coverage';
export * from './speaking-rules';
export * from './ai-prompt';
