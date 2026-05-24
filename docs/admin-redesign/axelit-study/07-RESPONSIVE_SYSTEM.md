# RESPONSIVE_SYSTEM.md — Axelit's responsive strategy

**Source**: live capture at 1920×893 desktop + 390×844 mobile (iPhone 14 Pro) viewports.

---

## 1 · Breakpoint definitions (Bootstrap 5 standard)

| Token | Value | Bracket | Behavior trigger |
| ----- | ----- | ------- | ---------------- |
| `--bs-breakpoint-xs` | `0` | < 576 | Phones (portrait) |
| `--bs-breakpoint-sm` | `576px` | 576-767 | Phones (landscape), small phones |
| `--bs-breakpoint-md` | `768px` | 768-991 | Tablets |
| `--bs-breakpoint-lg` | `992px` | 992-1199 | **Sidebar appears expanded above this** |
| `--bs-breakpoint-xl` | `1200px` | 1200-1399 | Standard desktop |
| `--bs-breakpoint-xxl` | `1400px` | ≥ 1400 | Wide desktop |

## 2 · Sidebar collapse behavior

**Above 992px (lg)**:
- Sidebar visible, 272px wide, fixed-position left
- Header starts at `padding-left: 292px` (272 + 20)
- Main content takes `100vw - 272px`

**Below 992px (md, sm, xs)**:
- Sidebar collapses to width 0
- An `[class*="overlay"]` element appears in the DOM (drawer pattern)
- Hamburger trigger (location not pinpointed in capture — likely header-left icon)
- `body { overflow: hidden }` when drawer opens (scroll lock observed)
- Main content takes full viewport width

**Captured evidence on 390×844 mobile**:
```js
{
  viewport: { w: 390, h: 844 },
  sidebarVisible: { x: 0, w: 0, display: "block" },   // present in DOM, zero-width
  overlay: true,                                       // drawer overlay element exists
  bodyOverflow: "hidden",                              // scroll locked
  mainWidth: 355,                                      // 390 - ~35px padding
  headerHeight: 80,                                    // header DOES NOT shrink on mobile
  horizontalScroll: false                              // no horizontal overflow — good
}
```

## 3 · Card row breakpoints (Bootstrap grid in action)

Observed class signature on `/dashboard/ecommerce`:
```html
<div class="row">
  <div class="order--1-lg col-sm-6 col-lg-4 col-xxl-2">…</div>
</div>
```

Decoded: this KPI card is:
- **xs / sm (< 576)**: full width (`col-12` implicit when no class applies)
- **sm (576-767)**: half width (`col-sm-6` → 2 per row)
- **lg (992-1399)**: third width (`col-lg-4` → 3 per row)
- **xxl (≥ 1400)**: 6th width (`col-xxl-2` → 6 per row)

**Universal pattern**: cards reflow via `col-{breakpoint}-{span}` classes. No custom media queries needed.

## 4 · Header responsive behavior

| Element | Desktop ≥ 992 | Mobile < 992 |
| ------- | ------------- | ------------ |
| Logo | Above sidebar (inside sidebar cap) | Top-left of header (sidebar hidden) |
| Hamburger trigger | Not visible | Top-left of header |
| Weather widget | Visible | Hidden (per Bootstrap `.d-none .d-md-block` convention) |
| Language picker | Visible | Hidden |
| Search | Visible | Visible (icon only) |
| Fullscreen | Visible | Hidden |
| Notifications | Visible | Visible |
| Theme toggle | Visible | Visible |
| Settings | Visible | Visible |
| Avatar | Visible (with name) | Visible (avatar only) |

Header height stays **80px** at every breakpoint (no shrinkage).

## 5 · Typography responsive scaling

**Bootstrap inherits `font-size` from `<html>`** at 16px base — **Axelit does not declare any responsive root font-size**. This means H1 stays at `2.5rem` (40px) on mobile too — *which is too large for a 390px viewport*.

**Captured rendered H1 on dashboard**: 44.96px on desktop. On mobile, this would consume nearly half the viewport width — text wrap or font-size-clamp is required.

**Recommendation for OET**: Use `clamp(2rem, 4vw + 1rem, 2.5rem)` for H1; pull H5 (card titles) down to 16px on mobile for hierarchy.

## 6 · Table responsive strategy

**Observed**: tables horizontally scroll inside their card on mobile (`overflow-x: auto` on table wrapper). No "stack on mobile" pattern.

```html
<div class="table-responsive">
  <table>…</table>
</div>
```

**Trade-off**: Preserves columns but creates a sub-scroll within the page — workable for power-user admin but ugly for casual mobile use.

**Recommendation for OET**: For high-traffic mobile admin views, use card-per-row pattern on `< md` (each table row becomes a vertically-stacked card). Reserve horizontal scroll for read-only data dumps.

## 7 · Modal responsive behavior

Bootstrap default: modals are full-width minus margin on mobile (`.modal-fullscreen-sm-down` for full-bleed). Welcome modal observed on mobile fills most of the viewport.

## 8 · Touch-target sizing

| Element | Observed size | Apple HIG min (44pt) | WCAG min (24px) | Verdict |
| ------- | ------------- | -------------------- | --------------- | ------- |
| Sidebar nav item | ~48px tall | ✓ | ✓ | OK |
| Header icon button | ~36px square | ✗ (below 44) | ✓ | Borderline |
| Table row checkbox | ~16px | ✗ | ✗ | **Fail** |
| Inline row action icon | ~24px | ✗ | ✓ | Borderline |
| Tag chip | ~24px tall | ✗ | ✓ | Borderline |

**Recommendation for OET**: Enforce **40px minimum** for any interactive element on mobile. Use `min-h-[44px]` Tailwind utility on touch targets.

## 9 · Image responsive strategy

- All avatar `<img>` tags have fixed dimensions (no `srcset` observed)
- KPI card illustrations (`<img class="img" />`) are loaded at full size, scaled via CSS
- No `<picture>` elements observed

**Recommendation for OET**: Use Next.js `<Image>` component for all admin imagery (covered by `next-best-practices` skill).

## 10 · Reduced-motion respect

Not declared in observed CSS variables. animate.css **does** respect `@media (prefers-reduced-motion: reduce)` internally. Custom `--app-transition: all 0.3s ease` does NOT.

**Recommendation for OET**: Wrap motion utilities in `@media (prefers-reduced-motion: no-preference)`:

```css
@media (prefers-reduced-motion: no-preference) {
  :root { --transition-default: transform 200ms var(--ease-out), opacity 150ms var(--ease-out); }
}
@media (prefers-reduced-motion: reduce) {
  :root { --transition-default: opacity 100ms linear; }
}
```

## 11 · Container width strategy

No `max-width` declared on `<main>` — it fills its column. On 1920px, main is 1613px wide; on 4K (3840px), it would fill 3568px — *which is too wide for comfortable reading*.

**Observed widest card** (chart): 654px on 1920 viewport.
**Observed table card**: spans full `<main>` width.

**Recommendation for OET**: Wrap content in `max-w-[1440px] mx-auto` for sane wide-screen behavior, or `container` Bootstrap class.

## 12 · RTL support

Customizer panel exposes "RTL" as a layout option (LTR / RTL / Box). Visible class on body: `class="ltr"`.

When RTL is enabled, `--app-transition` reverses, layout mirrors. Bootstrap 5's RTL build is the underlying mechanism.

**OET application**: If OET serves Arabic-speaking medical professionals, RTL support is plausible. Use `dir="rtl"` on `<html>` + Tailwind's `rtl:` variants.
