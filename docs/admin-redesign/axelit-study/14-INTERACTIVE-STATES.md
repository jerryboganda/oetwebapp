# 14 — INTERACTIVE STATES: Complete capture + May 2026 industry guidelines

**Gap closed**: Hover/focus/active/disabled/loading states (was LOW confidence)
**Method**: Direct CSS-rule extraction from Axelit's stylesheet — 495 `:hover` rules, 303 `:focus`, 99 `:focus-visible`, 11 `:active`, 116 `:disabled`, 6 `::placeholder`.
**Confidence**: **HIGH** ✅

---

## 1 · Axelit's state model (extracted from CSS)

### Button states — the canonical Bootstrap 5 pattern

```css
/* Default — values come from --bs-btn-* tokens */
.btn {
  color: var(--bs-btn-color);
  background-color: var(--bs-btn-bg);
  border-color: var(--bs-btn-border-color);
}

/* HOVER — swap to hover-prefixed tokens */
.btn:hover {
  color: var(--bs-btn-hover-color);
  background-color: var(--bs-btn-hover-bg);
  border-color: var(--bs-btn-hover-border-color);
}

/* FOCUS-VISIBLE — keyboard focus, adds the ring */
.btn:focus-visible {
  color: var(--bs-btn-hover-color);          /* same as hover */
  background-color: var(--bs-btn-hover-bg);
  border-color: var(--bs-btn-hover-border-color);
  outline: 0px;                              /* kill default browser outline */
  box-shadow: var(--bs-btn-focus-box-shadow); /* custom 4px tinted ring */
}

/* ACTIVE — pressed state, comes from .btn:active + .active class */
.btn:first-child:active,
:not(.btn-check) + .btn:active {
  color: var(--bs-btn-active-color);
  background-color: var(--bs-btn-active-bg);
  border-color: var(--bs-btn-active-border-color);
}

/* ACTIVE + FOCUS-VISIBLE — focused while pressed */
.btn.active:focus-visible {
  box-shadow: var(--bs-btn-focus-box-shadow);
}

/* DISABLED */
.btn:disabled,
.btn.disabled {
  pointer-events: none;
  opacity: var(--bs-btn-disabled-opacity);   /* default 0.65 */
}
```

### Per-variant hover/active token computation (Bootstrap formula)

For `.btn-primary` (bg = `rgb(140, 118, 240)` violet):

| State | Color formula |
| ----- | ------------- |
| Hover bg | Mix bg with **black** at 15% → `mix(violet, black, 15%)` ≈ `rgb(119, 100, 204)` |
| Hover border | Mix border with black at 20% → `rgb(112, 94, 192)` |
| Active bg | Mix bg with **black** at 20% → `rgb(112, 94, 192)` |
| Active border | Mix border with black at 25% → `rgb(105, 89, 180)` |
| Focus ring | `rgba(140, 118, 240, 0.5)` at 4px spread |
| Disabled opacity | 0.65 (default) |

For `.btn-outline-primary` (transparent + violet border):

| State | Effect |
| ----- | ------ |
| Hover | Border becomes solid (fill with the brand color, text flips to white) |
| Focus-visible | Same fill + 4px tinted ring |
| Active | Slightly darker fill |

For `.btn-light-primary` (violet @ 30% bg + dark-primary text):

| State | Effect |
| ----- | ------ |
| Hover | Bg goes from 30% to ~45% alpha (denser tint) |
| Focus-visible | Adds 4px tinted ring |
| Active | Bg ~55% alpha |

### Form input states

```css
/* DEFAULT */
.form-control {
  color: var(--bs-body-color);
  background-color: var(--bs-body-bg);
  border: 1px solid var(--bs-border-color);  /* #dee2e6 */
  border-radius: var(--bs-border-radius);    /* 1.8rem in Axelit */
}

/* FOCUS */
.form-control:focus {
  color: var(--bs-body-color);
  background-color: var(--bs-body-bg);
  border-color: rgb(134, 183, 254);           /* light blue */
  outline: 0;
  box-shadow: rgba(13, 110, 253, 0.25) 0 0 0 0.25rem;  /* 4px tinted ring */
}

/* DISABLED */
.form-control:disabled {
  background-color: var(--bs-secondary-bg);   /* #e9ecef grey */
  opacity: 1;                                  /* don't fade text */
}

/* PLACEHOLDER */
.form-control::placeholder {
  color: var(--bs-secondary-color);            /* rgba(33,37,41, 0.75) */
}
```

### Form-check (checkbox/radio/switch) states

```css
/* FOCUS — same 4px ring */
.form-check-input:focus {
  border-color: rgb(134, 183, 254);
  outline: 0;
  box-shadow: rgba(13, 110, 253, 0.25) 0 0 0 0.25rem;
}

/* DISABLED — opacity drop */
.form-check-input:disabled {
  pointer-events: none;
  filter: none;
  opacity: 0.5;
}

/* ACTIVE — slight darken */
.form-check-input:active {
  filter: brightness(90%);
}
```

### Table row hover

```css
.table-hover > tbody > tr:hover > * {
  --bs-table-color-state: var(--bs-table-hover-color);
  --bs-table-bg-state: var(--bs-table-hover-bg);   /* 3.5% black overlay */
}
```

### Nav link focus

```css
.nav-link:focus-visible {
  outline: 0px;
  box-shadow: rgba(13, 110, 253, 0.25) 0 0 0 0.25rem;
}
```

### Range slider thumb active

```css
.form-range::-webkit-slider-thumb:active {
  background-color: rgb(182, 212, 254);   /* lighter blue when held */
}
```

### Form-select disabled

```css
.form-select:disabled {
  background-color: var(--bs-secondary-bg);
}
```

### Custom Axelit pattern — option selected
```css
.app-form .form-select option:active,
.app-form .form-select option:checked {
  background-color: rgb(var(--primary), 1);
  color: rgba(var(--white), 1);
}
```

## 2 · The full 8-state contract — what Axelit does per component

| Component | Default | Hover | Focus-visible | Active | Disabled | Loading | Error | Success |
| --------- | :-----: | :---: | :-----------: | :----: | :------: | :-----: | :---: | :-----: |
| Button (solid) | ✓ | ✓ darker | ✓ tinted ring | ✓ darkest | ✓ opacity 0.65 | ✓ (spinner inline) | ✗ (not implemented) | ✗ |
| Button (outline) | ✓ | ✓ fill | ✓ tinted ring | ✓ darker fill | ✓ opacity | ✓ | ✗ | ✗ |
| Button (light) | ✓ | ✓ denser tint | ✓ tinted ring | ✓ densest | ✓ opacity | ✓ | ✗ | ✗ |
| Input | ✓ | (no separate hover) | ✓ blue border + ring | (n/a) | ✓ grey bg | ✗ | ✓ (.is-invalid red border) | ✓ (.is-valid green border) |
| Checkbox/Radio | ✓ | ✓ | ✓ ring | ✓ brightness 90% | ✓ opacity 0.5 | ✗ | ✓ | ✓ |
| Select | ✓ | (no hover) | ✓ blue border + ring | ✓ option selected | ✓ grey bg | ✗ | ✓ | ✓ |
| Switch | ✓ | ✓ | ✓ ring | ✓ | ✓ opacity | ✗ | ✓ | ✓ |
| Range | ✓ | ✓ thumb | ✓ ring | ✓ lighter thumb | ✓ opacity | ✗ | ✗ | ✗ |
| Nav link | ✓ | ✓ tinted bg | ✓ ring | ✓ primary bg | (n/a) | ✗ | ✗ | ✗ |
| Table row | ✓ | ✓ 3.5% overlay | (none) | (none) | (n/a) | ✗ (rare) | (n/a) | (n/a) |
| Dropdown item | ✓ | ✓ tinted bg | ✓ ring | ✓ primary bg | ✓ opacity | ✗ | ✗ | ✗ |

**Critical gaps in Axelit** that OET must fix:
- **Buttons have NO error/success state styling** (only Bootstrap's `.is-invalid` on inputs)
- **Buttons have a `Loading Buttons` section in the UI Kit** but the styling is inline/manual — needs codifying
- **Table rows have no error/loading state** for async row operations
- **Focus rings everywhere are Bootstrap-blue** (`rgba(13, 110, 253, 0.25)`), not Axelit-violet — a visible bug

---

## 3 · MAY 2026 INDUSTRY STANDARD — Interactive States Spec

### 3.1 · The non-negotiable: `:focus-visible`, not `:focus`

`:focus-visible` only triggers for **keyboard** focus, not mouse clicks. This is the modern standard — mouse-click focus rings are visual noise.

```css
/* ❌ OLD (pre-2022) — fires on every mouse click */
.btn:focus { outline: 2px solid blue; }

/* ✅ MODERN — only for keyboard users */
.btn:focus-visible { outline: 2px solid blue; }
.btn:focus:not(:focus-visible) { outline: none; }
```

Browser support: Safari 15.4+ (March 2022), Chrome/Edge 86+, Firefox 85+. Now universal.

### 3.2 · The 8-state required-by-Hallmark contract

Every interactive component in OET admin MUST implement all 8 states. From [`microinteractions.md`](../../../.claude/skills/hallmark/references/microinteractions.md):

```
1. Default      — idle, base styling
2. Hover        — pointer over (device-scoped via @media (hover: hover))
3. Focus-visible — keyboard focus, NEVER mouse focus
4. Active       — pressed/held
5. Disabled     — non-interactive, reduced opacity + cursor: not-allowed
6. Loading      — async in progress, spinner inline or replace label
7. Error        — failed action, red border + ⚠ icon + helper text
8. Success      — completed action, green border + ✓ icon (transient)
```

### 3.3 · Hover discipline — `@media (hover: hover)` scoping

Mobile devices fire `:hover` on tap and **leave it stuck** until next tap. The fix:

```css
@media (hover: hover) and (pointer: fine) {
  .btn:hover { background-color: var(--btn-hover-bg); }
}
```

On touch devices, the rule never applies, eliminating "stuck hover" — a classic admin-panel bug.

### 3.4 · Focus ring spec (2026 best practice)

| Element | Ring spec |
| ------- | --------- |
| Buttons | `box-shadow: 0 0 0 4px rgba(var(--primary-rgb), 0.4)`, outline:none |
| Inputs | Border color → primary; `box-shadow: 0 0 0 4px rgba(var(--primary-rgb), 0.3)` |
| Links | `outline: 2px solid var(--primary); outline-offset: 2px` |
| Cards (interactive) | `outline: 2px solid var(--primary); outline-offset: 2px` |
| Custom widgets | Same as links |

**Rules**:
- Ring color matches the brand primary
- Ring is **4px wide** (Bootstrap default — passes WCAG 2.2's 3px-minimum contrast outline rule)
- Ring contrast vs adjacent color ≥ 3:1 (WCAG 2.4.11)
- Ring appears INSTANTLY (no `transition` on `box-shadow` for ring — flickers)
- Ring is dropped on `:focus:not(:focus-visible)` for mouse users

**Axelit's bug**: Focus ring is `rgba(13, 110, 253, 0.25)` (Bootstrap blue) — but Axelit's primary is violet `rgb(140, 118, 240)`. OET must fix this — match ring to brand primary.

### 3.5 · Active state spec

The active state (`:active`) fires for the duration of the mouse-down or keyboard-Enter press. Subtle visual:

- **Buttons**: `transform: scale(0.98)` — tactile press feedback
- **Cards** (interactive): no transform (would shift content)
- **Toggles**: state-flip animation (not the active state itself)

```css
.btn:active {
  transform: scale(0.98);
  transition: transform 75ms cubic-bezier(0.4, 0, 0.6, 1);
}
```

### 3.6 · Disabled state spec

Three things must happen for disabled:

```css
.btn:disabled,
.btn[aria-disabled="true"] {
  opacity: 0.5;                  /* 0.4-0.6 range — Material says 0.38 */
  cursor: not-allowed;
  pointer-events: none;          /* prevents click handlers firing */
}
```

**Critical**: include `[aria-disabled="true"]` selector. Some forms use aria-disabled instead of the disabled attribute (e.g. when you want focus to still land on the disabled control for screen reader announcement).

**Don't** use `pointer-events: none` if you want disabled buttons to still show a tooltip on hover explaining WHY they're disabled. Use `aria-disabled` instead and handle click prevention in JS.

### 3.7 · Loading state spec

Three patterns:

**Pattern A — Inline spinner, label stays**:
```tsx
<Button loading>
  {loading && <Spinner className="mr-2" />}
  Save
</Button>
```

**Pattern B — Spinner replaces label**:
```tsx
<Button loading>
  {loading ? <Spinner /> : 'Save'}
</Button>
```

**Pattern C — Skeleton placeholder** (for content blocks, not buttons):
```tsx
<Skeleton className="h-32 w-full" />
```

**Rules**:
- Spinner is centered, matches text color
- Width does NOT collapse (preserve layout — `min-width: <pre-load-width>`)
- Cursor becomes `progress` (not `wait`)
- All `onClick` handlers are no-op while loading
- ARIA: `aria-busy="true"` on the parent

### 3.8 · Error state spec

Inputs:
```css
.input.is-invalid {
  border-color: var(--danger);
  background-image: url(/icons/alert-circle.svg);
  background-position: right 12px center;
  background-repeat: no-repeat;
  padding-right: 40px;
}
.input.is-invalid:focus {
  box-shadow: 0 0 0 4px rgba(var(--danger-rgb), 0.3);
}
```

Helper text:
```tsx
<div className="form-field">
  <label htmlFor="email">Email</label>
  <input id="email" aria-invalid={!!error} aria-describedby={error ? 'email-error' : undefined} />
  {error && (
    <p id="email-error" className="text-sm text-danger flex items-center gap-1.5 mt-1">
      <AlertCircle className="h-4 w-4" /> {error}
    </p>
  )}
</div>
```

**ARIA contract**:
- `aria-invalid="true"` on the input when invalid
- `aria-describedby` points at the error message id
- Error message appears IMMEDIATELY below the input (not in a tooltip)
- Error text uses the danger color BUT also has a leading icon (color alone fails WCAG 1.4.1)

### 3.9 · Success state spec

Use sparingly. Buttons should fire toast on success, NOT linger in a "success" state. Inputs CAN linger:

```css
.input.is-valid {
  border-color: var(--success);
  background-image: url(/icons/check.svg);
  background-position: right 12px center;
  background-repeat: no-repeat;
}
```

For buttons: prefer a transient toast (`Sonner`) over a sticky success state.

### 3.10 · Touch target sizing (WCAG 2.5.5 + 2.5.8)

| Standard | Minimum |
| -------- | ------- |
| WCAG 2.1 (Level AAA) 2.5.5 | 44 × 44 CSS px |
| WCAG 2.2 (Level AA) 2.5.8 | 24 × 24 CSS px |
| Apple HIG | 44 × 44 pt |
| Material 3 | 48 × 48 dp |
| **OET admin recommendation** | **40 × 40 px minimum**, exception only for inline icon buttons inside table cells (then min 32×32 with 4px padding) |

### 3.11 · Cursor type per state

| State | Cursor |
| ----- | ------ |
| Default interactive | `pointer` |
| Default text input | `text` |
| Disabled | `not-allowed` |
| Loading | `progress` (NOT `wait` — wait blocks entire app) |
| Drag handle | `grab` (default) / `grabbing` (active) |
| Resize column | `col-resize` |
| Help text | `help` |

### 3.12 · Transition spec per state change

```css
.btn {
  /* Narrow property list — never `all` */
  transition:
    background-color 150ms cubic-bezier(0.4, 0, 0.6, 1),
    border-color 150ms cubic-bezier(0.4, 0, 0.6, 1),
    color 150ms cubic-bezier(0.4, 0, 0.6, 1),
    box-shadow 75ms cubic-bezier(0.4, 0, 0.6, 1),
    transform 75ms cubic-bezier(0.4, 0, 0.6, 1);
}
```

**Critical**:
- Focus ring transitions are SHORT (75ms) or instant (0ms) — long ring animations look broken
- Color transitions are MEDIUM (150-200ms) — too fast = harsh, too slow = sluggish
- Transform transitions are SHORT (75ms) — taps should feel immediate

`prefers-reduced-motion: reduce` collapses all of these to 0ms via `<meta>` overrides.

### 3.13 · ARIA live regions for state announcement

When a button transitions to loading or error, screen readers need to know:

```tsx
<div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
  {loading && 'Saving…'}
  {error && `Error: ${error}`}
  {success && 'Saved successfully'}
</div>
```

Or use a single global toast region:
```tsx
<Toaster /> {/* Sonner — handles aria-live automatically */}
```

### 3.14 · Selected vs Active (the confusing pair)

| Term | Meaning |
| ---- | ------- |
| `:active` | Pseudo-class — the brief moment of being pressed |
| `.active` | Class — the persistent "this is the current selection" state (nav link, tab, current page) |
| `[aria-current="page"]` | ARIA — the WAY to indicate current nav location |
| `aria-selected="true"` | ARIA — for selectable items (tabs, listbox, gridcell) |

Don't mix these. The `.active` class + `aria-current` is for nav; `aria-selected` is for selectable widgets.

### 3.15 · Hover delay on tooltips (the 800ms / 0ms split)

Material guideline (still standard in 2026):

```ts
const TOOLTIP_HOVER_DELAY = 800;  // ms before showing on hover
const TOOLTIP_FOCUS_DELAY = 0;    // immediate on keyboard focus
const TOOLTIP_HIDE_DELAY = 200;   // before hiding after blur/leave
```

Radix UI Tooltip handles this; configure via `delayDuration={800}`.

### 3.16 · "Stuck hover" prevention on touch

```css
/* Trick: only fire :hover styles when the device CAN hover */
@media (hover: hover) {
  .card:hover { box-shadow: var(--shadow-hover); }
  .btn:hover { background-color: var(--btn-hover-bg); }
}

/* Always include :focus-visible — works on touch AND keyboard */
.btn:focus-visible { box-shadow: var(--ring); }
```

### 3.17 · State priority cascade

When multiple states apply simultaneously (e.g. hover + focus-visible + active), specificity wins. Order CSS so that:

```
default
  ↓
:hover
  ↓
:focus-visible
  ↓
:active
  ↓
:disabled (overrides everything)
```

Bootstrap follows this order. Hallmark requires it.

### 3.18 · The "Loading" button — full reference spec

```tsx
export function Button({
  loading,
  loadingText,
  children,
  disabled,
  ...props
}: ButtonProps) {
  const isDisabled = disabled || loading;

  return (
    <button
      {...props}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      aria-live={loading ? 'polite' : undefined}
      className={cn(
        'btn',
        loading && 'cursor-progress',
        props.className,
      )}
      style={{ minWidth: '5rem' /* preserve width during state swap */ }}
    >
      {loading ? (
        <>
          <Spinner className="mr-2 h-4 w-4" />
          {loadingText ?? children}
        </>
      ) : (
        children
      )}
    </button>
  );
}
```

---

## 4 · OET COMPONENT STATE CONTRACT (production-ready)

### 4.1 · Button (8 states)

```tsx
type ButtonProps = {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'destructive' | 'link';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  disabled?: boolean;
  fullWidth?: boolean;
  startIcon?: React.ReactNode;
  endIcon?: React.ReactNode;
  children: React.ReactNode;
} & React.ButtonHTMLAttributes<HTMLButtonElement>;
```

States visible via prop combinations:
- Default: `<Button>Save</Button>`
- Hover: CSS-driven on `:hover @media (hover: hover)`
- Focus: CSS-driven on `:focus-visible`
- Active: CSS-driven on `:active`
- Disabled: `<Button disabled>Save</Button>`
- Loading: `<Button loading>Save</Button>` (or `loadingText="Saving..."`)
- Error: parent wraps with `.is-invalid` (rare — toasts preferred)
- Success: parent fires toast (no in-button state)

### 4.2 · Input (8 states)

```tsx
type InputProps = {
  label: string;
  hint?: string;
  error?: string;            // triggers .is-invalid state
  success?: boolean;          // triggers .is-valid state
  loading?: boolean;          // shows inline spinner
  required?: boolean;
  startIcon?: React.ReactNode;
  endIcon?: React.ReactNode;
} & React.InputHTMLAttributes<HTMLInputElement>;
```

States:
- Default + hint
- Focus-visible: blue border + 4px ring
- Disabled: grey bg + cursor not-allowed
- Loading: spinner in endIcon slot
- Error: red border + ⚠ icon + error text + `aria-invalid`
- Success: green border + ✓ icon (transient — fade out 2s)

### 4.3 · Table row (5 states)

- Default
- Hover: 3.5% darken overlay (light mode); 6% lighten (dark)
- Selected: primary-tinted bg + checkbox checked
- Loading: row-level skeleton (e.g. async edit in progress)
- Error: row gets red left-border + retry button in last cell

### 4.4 · Sidebar nav item (5 states)

- Default
- Hover: light tint background
- Focus-visible: 4px primary ring (NOT the Bootstrap blue)
- Active (current route): primary tint bg + primary text + `aria-current="page"`
- Expanded (parent of submenu): chevron rotated 90°

---

## 5 · QA checklist for every component

- [ ] All 8 states have explicit CSS rules
- [ ] `:focus-visible` used (not bare `:focus`)
- [ ] Focus ring color matches brand primary (not Bootstrap default blue)
- [ ] Hover scoped with `@media (hover: hover)`
- [ ] Touch targets ≥ 40px on any element used on mobile
- [ ] Disabled adds `cursor: not-allowed` + `pointer-events: none`
- [ ] Loading shows spinner + preserves width + `aria-busy="true"`
- [ ] Error shows icon + text (not color-only) + `aria-invalid="true"` + `aria-describedby`
- [ ] Success is transient (toast) OR icon + text
- [ ] Active state uses `transform: scale(0.98)` for buttons
- [ ] Transitions use narrow property list, never `all`
- [ ] `prefers-reduced-motion` zeroes all transitions
- [ ] Screen reader announces state changes via `role="status"` or toast region

---

## 6 · Confidence upgrade

**Was**: LOW — "not scripted per-element"
**Now**: **HIGH** ✅ — direct CSS rule extraction revealed Bootstrap's complete state model + Axelit's customizations + identified the specific bugs (focus ring color mismatch, missing button error/success states, no `prefers-reduced-motion` discipline).
