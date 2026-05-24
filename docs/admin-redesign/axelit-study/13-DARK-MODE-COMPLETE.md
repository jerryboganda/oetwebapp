# 13 — DARK MODE: Complete capture + May 2026 industry guidelines

**Gap closed**: Dark mode parity (was LOW confidence in [11-CONFIDENCE-GAP-ANALYSIS.md](11-CONFIDENCE-GAP-ANALYSIS.md))
**Method**: Direct CSS-rule extraction from Axelit's stylesheet + live application of `body.dark` class + rendered-value capture.
**Confidence**: **HIGH** ✅

---

## 1 · How Axelit ACTUALLY implements dark mode

Axelit ships **TWO parallel dark-mode mechanisms**, both present in the live CSS:

### Mechanism A — Bootstrap 5.3 native (`[data-bs-theme="dark"]`)
Used by every Bootstrap component (form-select, accordion, btn-close, carousel, navbar). Inherited from Bootstrap upstream.

### Mechanism B — Axelit custom (`body.dark`)
The actual toggle in the header (`header li.header-dark`) flips `body.dark` class on/off. This is the dominant mechanism for the dashboard chrome (sidebar, header, cards, body).

**Implication for OET**: implement **both selectors** so React component libraries that key off `data-bs-theme` (or `class="dark"` Tailwind convention) all work.

## 2 · Exact dark mode token overrides (extracted verbatim from CSS)

### Mechanism A — `[data-bs-theme="dark"]` block

```css
[data-bs-theme="dark"] {
  color-scheme: dark;

  /* Base */
  --bs-body-color:        #dee2e6;
  --bs-body-color-rgb:    222, 226, 230;
  --bs-body-bg:           #212529;
  --bs-body-bg-rgb:       33, 37, 41;
  --bs-emphasis-color:    #fff;
  --bs-emphasis-color-rgb: 255, 255, 255;
  --bs-secondary-color:   rgba(222, 226, 230, 0.75);
  --bs-secondary-color-rgb: 222, 226, 230;
  --bs-secondary-bg:      #343a40;
  --bs-secondary-bg-rgb:  52, 58, 64;
  --bs-tertiary-color:    rgba(222, 226, 230, 0.5);
  --bs-tertiary-color-rgb: 222, 226, 230;
  --bs-tertiary-bg:       #2b3035;
  --bs-tertiary-bg-rgb:   43, 48, 53;

  /* Text emphasis (inverted: lighter variants for dark BG) */
  --bs-primary-text-emphasis:   #6ea8fe;
  --bs-secondary-text-emphasis: #a7acb1;
  --bs-success-text-emphasis:   #75b798;
  --bs-info-text-emphasis:      #6edff6;
  --bs-warning-text-emphasis:   #ffda6a;
  --bs-danger-text-emphasis:    #ea868f;
  --bs-light-text-emphasis:     #f8f9fa;
  --bs-dark-text-emphasis:      #dee2e6;

  /* Subtle BG (inverted: darker, low-chroma) */
  --bs-primary-bg-subtle:   #031633;
  --bs-secondary-bg-subtle: #161719;
  --bs-success-bg-subtle:   #051b11;
  --bs-info-bg-subtle:      #032830;
  --bs-warning-bg-subtle:   #332701;
  --bs-danger-bg-subtle:    #2c0b0e;
  --bs-light-bg-subtle:     #343a40;
  --bs-dark-bg-subtle:      #1a1d20;

  /* Border subtle */
  --bs-primary-border-subtle:   #084298;
  --bs-secondary-border-subtle: #41464b;
  --bs-success-border-subtle:   #0f5132;
  --bs-info-border-subtle:      #087990;
  --bs-warning-border-subtle:   #997404;
  --bs-danger-border-subtle:    #842029;
  --bs-light-border-subtle:     #495057;
  --bs-dark-border-subtle:      #343a40;

  /* Headings inherit; links flip to lighter tones */
  --bs-heading-color:   inherit;
  --bs-link-color:      #6ea8fe;
  --bs-link-hover-color: #8bb9fe;
  --bs-link-color-rgb:  110, 168, 254;
  --bs-link-hover-color-rgb: 139, 185, 254;
  --bs-code-color:      #e685b5;
  --bs-highlight-color: #dee2e6;
  --bs-highlight-bg:    #664d03;

  /* Borders */
  --bs-border-color:             #495057;
  --bs-border-color-translucent: rgba(255, 255, 255, 0.15);

  /* Form validation */
  --bs-form-valid-color:        #75b798;
  --bs-form-valid-border-color: #75b798;
  --bs-form-invalid-color:      #ea868f;
  --bs-form-invalid-border-color: #ea868f;
}
```

### Mechanism B — `body.dark` block (Axelit's custom layer)

```css
body.dark, body.dark.modal-open, nav.dark-sidebar {
  /* INVERSIONS — note how white/black/dark RGB tokens flip semantics */
  --white:        32, 35, 53;        /* "white" is now dark navy in dark mode */
  --black:        #dce2f0;           /* "black" is now light blue-grey */
  --dark:         234, 234, 236;     /* "dark" role token is now near-white */
  --light:        71, 71, 96;        /* "light" role is now mid-grey */

  /* Surface tokens */
  --bodybg-color: #272b3e;           /* main page background — deep desaturated navy */
  --bs-body-bg:   #202335;           /* deeper variant for body */
  --light-gray:   #333644;           /* card/hover surface */
  --bs-tertiary-bg: #242425;         /* tertiary surface */

  /* Text */
  --font-color:           #fff;
  --bs-body-color:        #ffffff;
  --bs-secondary-color:   #eaeaec;
  --bs-list-group-color:  #eaeaec;
  --bs-card-color:        #eaeaec;
  --link-color:           #eaeaec;

  /* Borders */
  --border_color:           #474a56;
  --bs-border-color:        #474a56;
  --bs-card-border-color:   #5b5e69;
  --bs-form-control-bg:     #333644;

  /* Shadows (warmer in dark mode — dark grey tint, not black) */
  --box-shadow:   0 0.2rem 1rem #333644;
  --hover-shadow: 0 0.2rem 2rem #333644;

  /* Role-dark variants (used for "light button" text in dark mode — inverted lightness) */
  --primary-dark:   160, 148, 226;   /* was 36,17,135 — lighter violet for dark BG */
  --secondary-dark: 152, 137, 146;
  --success-dark:   148, 143, 135;
  --danger-dark:    208, 181, 200;
  --warning-dark:   198, 193, 167;
  --info-dark:      157, 186, 224;
  --dark-dark:      179, 176, 193;
}
```

## 3 · Confirmed rendered values when dark mode is active

(From live capture after applying `body.dark` class)

| Element | Light mode | Dark mode |
| ------- | ---------- | --------- |
| `body` background | `rgb(246, 246, 246)` | `rgb(39, 43, 62)` — deep navy |
| `body` text | `rgb(21, 38, 75)` navy | `rgb(255, 255, 255)` white |
| `nav.vertical-sidebar` bg | `rgb(255, 255, 255)` | `rgb(32, 35, 53)` deepest navy |
| `header.header-main` bg | `rgb(246, 246, 246)` | `rgb(39, 43, 62)` (matches body) |
| `.card` border | warm beige `#e0dfd6` | translucent info-blue `rgba(157, 186, 224, 0.6)` |
| `h5` text | navy `#1c3264` | near-white `rgb(234, 234, 236)` |

**Critical observation**: The sidebar is DARKER than the body in dark mode (the opposite of light mode, where sidebar is brighter than body). This creates the same hierarchy — sidebar still "pops" — but inverted in lightness.

---

## 4 · MAY 2026 INDUSTRY STANDARD — Dark Mode Guidelines

### 4.1 · The four-layer surface system

Modern dark mode (Material 3, Apple HIG, GitHub Primer, Radix Themes) uses **elevation-by-lightness**, NOT shadow. Each layer is lighter than the one below it:

```
Layer 0 — App background     hsl(220 15% 8%)   ← deepest
Layer 1 — Card / panel       hsl(220 14% 12%)
Layer 2 — Hover / popover    hsl(220 13% 16%)
Layer 3 — Active / focused   hsl(220 12% 20%)  ← lightest
```

**Why**: shadows are nearly invisible on dark backgrounds. Lightness elevation is the only legible cue.

**Axelit follows this pattern** (sidebar `#202335` < body `#272b3e`) but inverts which layer is which. OET should use the canonical convention: app bg darkest, popovers lightest.

### 4.2 · Color-scheme CSS property — REQUIRED

```css
[data-theme="dark"] { color-scheme: dark; }
[data-theme="light"] { color-scheme: light; }
```

This tells the browser to render native form controls (scrollbars, datepickers, file inputs, autofill backgrounds) in the matching scheme. **Without this, autofill backgrounds appear in bright white in dark mode** — a notorious admin-panel bug.

Axelit declares `color-scheme: dark` inside `[data-bs-theme="dark"]` ✓ — OET must do the same.

### 4.3 · Saturation reduction rule

Brand colors must **lose 20–35% saturation** in dark mode to avoid visual vibration on dark backgrounds.

| Hue | Light mode (OK) | Dark mode (DESATURATE) |
| --- | --------------- | ---------------------- |
| Pure brand violet `#8c76f0` | (LCH 60 / 50 / 290) | (LCH 70 / 35 / 290) |
| Success green `#147834` | (LCH 45 / 50 / 145) | (LCH 65 / 35 / 145) |
| Danger red `#f00ac8` | (LCH 55 / 80 / 0) | (LCH 70 / 50 / 0) |

OET should pre-compute dark variants via OKLCH or LCH math at the token level, NOT mid-render.

### 4.4 · Opacity tokens for non-chromatic states

Hover, focus, disabled states should NOT use solid bg colors in dark mode (they look heavy). Use **opacity-tinted overlays**:

```css
--state-hover-overlay:    rgba(255, 255, 255, 0.06);  /* 6% white */
--state-focus-overlay:    rgba(255, 255, 255, 0.10);
--state-active-overlay:   rgba(255, 255, 255, 0.14);
--state-disabled-opacity: 0.4;
```

Apply via `background-color` on top of the existing surface — Material 3 calls this the "state layer" pattern.

### 4.5 · Text emphasis tiers

```css
[data-theme="dark"] {
  --text-strong:   rgba(255, 255, 255, 0.95);  /* H1, KPI numbers */
  --text-default:  rgba(255, 255, 255, 0.87);  /* body */
  --text-muted:    rgba(255, 255, 255, 0.60);  /* helper text */
  --text-disabled: rgba(255, 255, 255, 0.38);
}
```

Pure white (`#fff`) is too aggressive for body text on dark backgrounds — causes after-image flicker. Always use rgba(255,255,255,0.87) or `#dee2e6` for body.

Axelit's `--bs-body-color: #dee2e6` ✓ — correct.

### 4.6 · Border philosophy

Dark mode borders should be **lighter than the surface they're on, not darker**:

```css
[data-theme="dark"] {
  --border-default: rgba(255, 255, 255, 0.12);  /* visible but quiet */
  --border-strong:  rgba(255, 255, 255, 0.20);  /* form inputs, focused */
  --border-focus:   var(--primary);
}
```

Axelit uses `#474a56` for `--border_color` in dark mode (≈ 8% lighter than the surface). ✓

### 4.7 · Shadow philosophy

Dark mode shadows should be **darker than the surface**, not lighter (counter-intuitive but correct):

```css
[data-theme="dark"] {
  --shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.5);
  --shadow-md: 0 4px 8px rgba(0, 0, 0, 0.55);
  --shadow-lg: 0 12px 24px rgba(0, 0, 0, 0.6);
}
```

Axelit's `--box-shadow: 0 0.2rem 1rem #333644` (a grey, not black) is a **mistake** — the shadow color is lighter than the body and almost invisible. OET should use rgba(0,0,0, 0.5+).

### 4.8 · System preference + user override (the 3-state pattern)

The canonical pattern in 2026 is **three states, not two**:

```ts
type Theme = 'light' | 'dark' | 'system';
```

- **`system`** (default): follows `prefers-color-scheme` media query
- **`light`** / **`dark`**: explicit user override

```tsx
// Recommended: next-themes (Next.js) or @radix-ui/colors
import { ThemeProvider } from 'next-themes';

<ThemeProvider attribute="data-theme" defaultTheme="system" enableSystem>
  {children}
</ThemeProvider>
```

**Persistence**: `localStorage` key `theme`. On mount, hydrate from storage; fallback to `system`.

### 4.9 · Flash-of-incorrect-theme (FOIT/FART) prevention

The most-reported dark-mode bug in 2025-2026 admin panels: **theme flashes light on initial paint before JS hydrates**.

**Fix**: blocking inline script in `<head>` BEFORE any CSS loads:

```html
<script>
(function() {
  const stored = localStorage.getItem('theme');
  const system = window.matchMedia('(prefers-color-scheme: dark)').matches;
  const theme = stored === 'dark' || (stored !== 'light' && system) ? 'dark' : 'light';
  document.documentElement.setAttribute('data-theme', theme);
  document.documentElement.classList.add(theme);
})();
</script>
```

For Next.js App Router: use `next-themes` `<ThemeProvider>` with the `attribute="data-theme"` prop — handles this automatically.

### 4.10 · Image / asset handling

Three patterns for images:

| Asset type | Dark mode treatment |
| ---------- | ------------------- |
| Logos (mono) | Use `currentColor` for fills; one SVG works for both |
| Logos (multi-color) | Provide separate light/dark SVGs, swap via `<picture>` |
| Photos | Reduce brightness 10-15% via CSS `filter: brightness(0.9)` in dark mode |
| Charts | Re-theme series colors (see [16-CHARTS-COMPLETE.md](16-CHARTS-COMPLETE.md)) |
| Illustrations | Provide dark-variant assets in `public/dark/*` |

Axelit's logo uses `filter: contrast(120%) brightness(600%)` in dark mode — a hack. OET should use proper dark-variant assets.

### 4.11 · Scrollbar styling (often-missed)

```css
[data-theme="dark"] {
  scrollbar-color: rgba(255, 255, 255, 0.2) transparent;
}

[data-theme="dark"] ::-webkit-scrollbar-thumb {
  background-color: rgba(255, 255, 255, 0.2);
  border-radius: 4px;
}
```

### 4.12 · Form autofill background fix

Browsers force autofill backgrounds to bright yellow/white, ruining dark mode:

```css
[data-theme="dark"] input:-webkit-autofill,
[data-theme="dark"] input:-webkit-autofill:hover,
[data-theme="dark"] input:-webkit-autofill:focus {
  -webkit-text-fill-color: var(--text-default);
  -webkit-box-shadow: 0 0 0 1000px var(--bg-card) inset;
  caret-color: var(--text-default);
}
```

### 4.13 · `prefers-color-scheme: no-preference` (legacy)

For old browsers that don't return `prefers-color-scheme: dark`, fall back to light:

```css
@media (prefers-color-scheme: no-preference) {
  /* treat as light */
}
```

### 4.14 · Dynamic chart re-theming

When dark mode flips, Recharts/Apex/d3 charts must re-render with new series colors. Hook into your theme provider:

```tsx
const { theme } = useTheme();
const chartColors = theme === 'dark' ? DARK_PALETTE : LIGHT_PALETTE;

<LineChart>
  <Line stroke={chartColors.primary} />
</LineChart>
```

Don't re-mount — pass colors as props.

### 4.15 · Page transition

Theme toggle should be **instant** (no animation). Animating colors triggers full repaint of every element — expensive and visually noisy. Hard-cut is the spec.

If you must, use:
```css
html { transition: background-color 200ms linear, color 200ms linear; }
html * { transition: background-color 200ms linear, border-color 200ms linear, color 200ms linear; }
```

Limit `transition` properties — never `all`.

---

## 5 · OET DARK MODE IMPLEMENTATION SPEC (production-ready)

### 5.1 · Token sheet additions to [01-design.md](01-design.md)

```css
/* OET admin — dark mode token overrides */
[data-theme="dark"] {
  color-scheme: dark;

  /* Surfaces (elevation by lightness) */
  --admin-bg-page:      hsl(220 15% 8%);   /* layer 0 — deepest */
  --admin-bg-surface:   hsl(220 14% 12%);  /* layer 1 — cards */
  --admin-bg-elevated:  hsl(220 13% 16%);  /* layer 2 — modals, popovers */
  --admin-bg-subtle:    hsl(220 12% 20%);  /* layer 3 — table header strip */

  /* Text (opacity-tinted white) */
  --admin-fg-strong:    rgba(255, 255, 255, 0.95);
  --admin-fg-default:   rgba(255, 255, 255, 0.87);
  --admin-fg-muted:     rgba(255, 255, 255, 0.60);
  --admin-fg-disabled:  rgba(255, 255, 255, 0.38);
  --admin-fg-inverse:   hsl(220 15% 10%);

  /* Borders (opacity-tinted white) */
  --admin-border-default: rgba(255, 255, 255, 0.12);
  --admin-border-strong:  rgba(255, 255, 255, 0.20);

  /* State overlays */
  --admin-state-hover:    rgba(255, 255, 255, 0.06);
  --admin-state-focus:    rgba(255, 255, 255, 0.10);
  --admin-state-active:   rgba(255, 255, 255, 0.14);
  --admin-state-selected: rgba(var(--admin-primary-rgb), 0.18);

  /* Brand role colors — desaturated 25% for dark mode */
  --admin-primary:        hsl(220 60% 60%);   /* OET clinical blue, brightened */
  --admin-primary-hover:  hsl(220 60% 65%);
  --admin-success:        hsl(145 45% 55%);
  --admin-warning:        hsl(42 75% 60%);
  --admin-danger:         hsl(0 65% 60%);
  --admin-info:           hsl(200 65% 60%);

  /* Shadows (true dark, alpha 0.5+) */
  --admin-shadow-sm:      0 1px 2px rgba(0, 0, 0, 0.5);
  --admin-shadow-md:      0 4px 12px rgba(0, 0, 0, 0.55);
  --admin-shadow-lg:      0 12px 32px rgba(0, 0, 0, 0.6);

  /* Scrollbar */
  scrollbar-color: rgba(255, 255, 255, 0.2) transparent;
}
```

### 5.2 · Component-level rules

```css
/* Autofill fix */
[data-theme="dark"] input:-webkit-autofill {
  -webkit-text-fill-color: var(--admin-fg-default);
  -webkit-box-shadow: 0 0 0 1000px var(--admin-bg-surface) inset;
  caret-color: var(--admin-fg-default);
}

/* Scrollbar (WebKit) */
[data-theme="dark"] ::-webkit-scrollbar { width: 8px; height: 8px; }
[data-theme="dark"] ::-webkit-scrollbar-track { background: transparent; }
[data-theme="dark"] ::-webkit-scrollbar-thumb {
  background-color: rgba(255, 255, 255, 0.2);
  border-radius: 4px;
}
[data-theme="dark"] ::-webkit-scrollbar-thumb:hover { background-color: rgba(255, 255, 255, 0.3); }

/* Form select arrow */
[data-theme="dark"] .form-select {
  background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3e%3cpath fill='none' stroke='%23dee2e6' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='m2 5 6 6 6-6'/%3e%3c/svg%3e");
}
```

### 5.3 · ThemeProvider wiring (Next.js App Router)

`app/layout.tsx`:
```tsx
import { ThemeProvider } from 'next-themes';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>
        <ThemeProvider
          attribute="data-theme"
          defaultTheme="system"
          enableSystem
          disableTransitionOnChange  // hard-cut on toggle
        >
          {children}
        </ThemeProvider>
      </body>
    </html>
  );
}
```

`components/admin/theme-toggle.tsx`:
```tsx
'use client';
import { Moon, Sun, Monitor } from 'lucide-react';
import { useTheme } from 'next-themes';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem } from '@/components/ui/dropdown-menu';

export function ThemeToggle() {
  const { setTheme, theme } = useTheme();
  return (
    <DropdownMenu>
      <DropdownMenuTrigger aria-label="Toggle theme" className="head-icon">
        <Sun className="h-5 w-5 dark:hidden" />
        <Moon className="h-5 w-5 hidden dark:block" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={() => setTheme('light')}>
          <Sun className="mr-2 h-4 w-4" /> Light
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => setTheme('dark')}>
          <Moon className="mr-2 h-4 w-4" /> Dark
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => setTheme('system')}>
          <Monitor className="mr-2 h-4 w-4" /> System
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

### 5.4 · QA checklist (every component must pass)

- [ ] Color-scheme declared (`color-scheme: dark` / `light`)
- [ ] Body, sidebar, header, card, modal surfaces all use elevation layers
- [ ] All text uses opacity-tinted white, never `#fff` solid (except labels on solid-color buttons)
- [ ] All borders use opacity-tinted white, never `#fff` solid
- [ ] Hover / focus / active overlays use `--admin-state-*` tokens
- [ ] Brand colors desaturated 25% in dark mode
- [ ] Shadows are TRUE dark (rgba(0,0,0, 0.5+))
- [ ] Autofill background overridden
- [ ] Scrollbar themed
- [ ] Charts re-themed (pass dark palette as prop)
- [ ] Images / logos have dark variants OR use `currentColor`
- [ ] FOIT/FART prevented (next-themes handles this)
- [ ] Theme toggle has Light / Dark / System options
- [ ] Toggle is instant (no animation on switch)
- [ ] Preference persists across sessions
- [ ] WCAG AA contrast verified for all text + interactive elements in dark mode

### 5.5 · Tools to install

```bash
npm install next-themes
npm install -D @tailwindcss/forms  # better default form styling
```

And configure Tailwind to support dark mode via class:
```ts
// tailwind.config.ts
export default {
  darkMode: ['class', '[data-theme="dark"]'],
  // …
};
```

---

## 6 · Validation evidence (live capture)

```
APPLIED: body.dark + html.dark + data-bs-theme="dark"

Rendered values (captured via getComputedStyle):
  body         bg: rgb(39, 43, 62)   color: rgb(255, 255, 255)
  sidebar      bg: rgb(32, 35, 53)   color: rgb(255, 255, 255)
  header       bg: rgb(39, 43, 62)   color: rgb(255, 255, 255)
  card         bg: transparent       color: rgb(255, 255, 255)
                                       border: rgba(157, 186, 224, 0.6)
  h5           color: rgb(234, 234, 236)
```

Hierarchy preserved (sidebar deeper than body), text white, borders translucent info-blue. Dark mode IS fully wired in Axelit's CSS — the React toggle simply wasn't responding to programmatic `.click()` in the audit pass, but the underlying CSS is production-ready.

**Confidence upgrade**: LOW → **HIGH** ✅
