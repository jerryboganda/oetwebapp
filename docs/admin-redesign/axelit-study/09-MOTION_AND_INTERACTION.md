# MOTION_AND_INTERACTION.md — Axelit's motion philosophy

**Source**: Animation libraries loaded, motion tokens declared, hover-class signatures.

---

## 1 · Motion libraries observed

- **animate.css** (tokens: `--animate-duration: 1s`, `--animate-delay: 1s`, `--animate-repeat: 1`) — page entrance animations
- **Tawk.to JS bundle** — chat widget has its own motion
- **No framer-motion / GSAP / Motion One / Lottie** detected in script tags
- **Bootstrap transitions** (collapse, dropdown, modal, offcanvas) inherited

## 2 · Motion tokens

```css
:root {
  --animate-duration: 1s;     /* 1000ms — VERY slow for UI motion */
  --animate-delay:    1s;
  --animate-repeat:   1;
  --app-transition:   all 0.3s ease;
}
```

**Diagnosis**:
- `--app-transition: all 0.3s ease` — the **single most-applied transition** on the page. Animates ALL properties for 300ms with the browser's default `ease` curve.
  - Anti-pattern flag 1: Animating `all` includes layout properties (height, width, padding) which trigger reflows.
  - Anti-pattern flag 2: `ease` is `cubic-bezier(0.25, 0.1, 0.25, 1)` — symmetric, robotic, the worst default.
- `--animate-duration: 1s` — 1000ms is **too slow** for UI feedback. The literature (Material, HIG) recommends 150-300ms for UI transitions.

## 3 · Hover affordances

**Card hover**: `--hover-shadow: 0 0.5rem 2rem #f4f7f8` — soft lift.

**Button hover**:
- Solid buttons: bg darkens via Bootstrap's `:hover` (mix-in white with `--bs-btn-hover-bg`)
- Outline buttons: bg fills with the outline color
- Light buttons: bg darkens slightly within the same hue

**Sidebar nav hover**: light tint background appears

**Table row hover**: `rgba(0, 0, 0, 0.035)` — 3.5% black overlay (subtle)

## 4 · Animate.css usage pattern

Cards likely enter with `animate__fadeIn` / `animate__fadeInUp` on page load. Stagger via `animation-delay` per child.

```css
.card.animate-on-mount {
  animation: fadeInUp var(--animate-duration) var(--animate-delay);
}
```

**Trade-off**: animate.css is 70KB minified — pays for itself only if many distinct animations are used. Axelit likely uses 2-3 (`fadeIn`, `fadeInUp`, `bounceIn`).

## 5 · Microinteraction inventory

| Element | Interaction | Library |
| ------- | ----------- | ------- |
| Sidebar parent expand/collapse | Slide down/up via Bootstrap collapse | Bootstrap JS |
| Dropdown open | Fade + slight slide via Bootstrap dropdown | Bootstrap JS |
| Modal open | Fade backdrop + slide-down content via Bootstrap modal | Bootstrap JS |
| Off-canvas drawer | Slide from edge | Bootstrap JS |
| Tab switch | Cross-fade between tab panes | Bootstrap JS |
| Tooltip | Show on hover w/ 200ms delay | Bootstrap JS |
| Progress bar fill | CSS `transition: width` | CSS |
| Card hover lift | `transition: box-shadow 0.3s ease` | CSS |
| Button press | `:active { transform: scale(0.98) }` — INFERRED, not captured | CSS |

## 6 · Loading states

- **Spinner**: Bootstrap default (`.spinner-border`, `.spinner-grow`). Color via spinner variant classes.
- **Skeleton placeholder**: Bootstrap `.placeholder` with `.placeholder-glow` (pulsing) or `.placeholder-wave` (shimmer).
- **Loading text**: "Loading FilePond...", "Loading Chart..." — static text placeholder.
- **Button loading**: `/ui-kit/buttons` has a "Loading Buttons" section — likely shows spinner inline.

## 7 · State transitions

| State change | Visual transition |
| ------------ | ----------------- |
| Default → Hover | 300ms ease (sweep `all`) |
| Default → Focus | (none observed — focus ring appears instantly) |
| Default → Active (clicked) | (browser default `:active`) |
| Default → Selected (table row, sidebar nav) | 300ms ease |
| Default → Loading | Spinner appears, text changes |
| Default → Error | Red border + ⚠ icon appear |
| Default → Success | Green border + ✓ icon appear |

## 8 · Page transitions

**Next.js App Router** is the underlying framework. No custom page transition observed — Next.js default (instant route switch + suspense fallback). No view transitions API.

## 9 · Scroll behavior

- **No `scroll-behavior: smooth`** declared globally (anchors jump instantly)
- **No scroll-snap** behavior
- **No parallax** (anti-pattern for admin)
- **No scroll-triggered animations on cards** (animations fire once on mount, not on re-enter)

## 10 · Hover-vs-touch dichotomy

Axelit doesn't appear to differentiate touch vs hover devices. On touch (mobile), `:hover` styles trigger on tap and persist until next tap. This causes the "stuck hover" anti-pattern.

**OET fix**:
```css
@media (hover: hover) {
  .card:hover { box-shadow: var(--shadow-hover); }
}
@media (hover: none) {
  /* No hover effects on touch devices */
}
```

## 11 · `prefers-reduced-motion` respect

- animate.css respects this by default (animations disabled when `reduce` is set)
- `--app-transition: all 0.3s ease` does NOT respect it

## 12 · Recommended OET motion redesign

Replace Axelit's motion stack entirely with **motion v12** (already in OET per memory) + a tight token system:

```css
:root {
  /* DURATIONS */
  --dur-instant: 0ms;
  --dur-fast:    100ms;   /* tooltip, badge state change */
  --dur-base:    200ms;   /* button hover, card lift, dropdown open */
  --dur-medium:  300ms;   /* modal entrance, drawer slide */
  --dur-slow:    500ms;   /* page-level transitions */

  /* EASINGS — named, all asymmetric */
  --ease-out:    cubic-bezier(0.16, 1, 0.3, 1);          /* "exponential out" — Hallmark default */
  --ease-in:     cubic-bezier(0.7, 0, 0.84, 0);
  --ease-in-out: cubic-bezier(0.87, 0, 0.13, 1);

  /* TRANSITION PRESETS — narrow property lists, never `all` */
  --transition-color:   color var(--dur-fast) var(--ease-out),
                        background-color var(--dur-fast) var(--ease-out),
                        border-color var(--dur-fast) var(--ease-out);

  --transition-transform: transform var(--dur-base) var(--ease-out),
                          opacity var(--dur-base) var(--ease-out);

  --transition-shadow:   box-shadow var(--dur-base) var(--ease-out);
}

@media (prefers-reduced-motion: reduce) {
  :root {
    --dur-fast:   0ms;
    --dur-base:   0ms;
    --dur-medium: 0ms;
    --dur-slow:   0ms;
  }
}
```

Use motion v12 for:
- Modal / drawer entrance
- Layout animations (`AnimatePresence` for list reorder)
- Page transitions
- Skeleton → real content fade-in

Drop animate.css entirely.

## 13 · Hallmark-required microinteraction discipline

Per Hallmark `microinteractions.md`, every interactive component must hit ALL 8 states:
1. **Default**
2. **Hover** (`@media (hover: hover)` scoped)
3. **Focus-visible** (visible ring, no animation on ring itself)
4. **Active** (pressed, slight transform)
5. **Disabled** (opacity + `cursor: not-allowed`)
6. **Loading** (spinner or skeleton)
7. **Error** (red border + icon + helper text)
8. **Success** (green border + icon + helper text)

Axelit covers 1-5 OK. 6 (loading), 7 (error), 8 (success) are inconsistent across components. OET should enforce all 8 per component via a state-prop API.
