/**
 * Shared outline for every control in the learner header (search, streak,
 * level, bell, theme, account).
 *
 * These were each using `border-border` (#d8e0e8) — a fairly saturated
 * blue-grey that, combined with a drop shadow, made every element look like it
 * was in a heavy box. The reference header uses one neutral, very low-contrast
 * hairline across all of them, so keep this single definition and apply it
 * everywhere rather than restyling controls individually.
 */
export const HEADER_CHIP =
  'border border-black/[0.06] bg-surface shadow-[0_1px_2px_rgba(15,23,42,0.03)] dark:border-white/[0.08]';

/** Hover treatment paired with HEADER_CHIP for interactive controls. */
export const HEADER_CHIP_HOVER = 'transition-colors hover:border-black/[0.12] dark:hover:border-white/[0.16]';
