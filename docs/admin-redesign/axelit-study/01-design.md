<!--
Hallmark · macrostructure: Bento Grid (KPI strip → mid-grid → table → activity rail)
 nav: N3 (left-rail vertical-sidebar 272px, grouped, multi-level) · footer: Ft7 (legal + version + help)
 theme: studied-DNA (source: url) · studied: yes · DNA-source: https://axelit-next.vercel.app/dashboard/project
 paper: oklch(97% 0.003 90) [#f6f6f6] · accent: oklch(64% 0.18 290) [rgb(140,118,240) violet]
 display: Montserrat 600/700 · body: Montserrat 400/500 · label: Montserrat uppercase
 motion: animate.css (transition all 0.3s ease, fade entrances)
-->

# design.md — Axelit-derived admin design system

> **PROVENANCE** — Extracted from `https://axelit-next.vercel.app` on 2026-05-24 as a **public reference for the OET admin panel rebuild**.
> Axelit is a commercial ThemeForest template by *la-themes / teqlathemes*. This file captures **the DNA — macrostructure, archetypes, type roles, colour roles, density, motion stance** — not pixel-faithful copies.
> Per Hallmark §Provenance (URL-mode): tokens are exact (extracted from source CSS); fonts are exact (Montserrat declared via Next.js `next/font`); rhythm is observable from the live capture (URL-mode rhythm blind spot mitigated by Playwright snapshots).
> OET-specific tokens (brand hues, copy, type voice) MUST be regenerated to fit OET's brand identity — the source's violet/magenta/chartreuse palette is *theme-marketplace voice*, not OET's clinical-education voice.

---

## System

- **Macrostructure family**: Bento Grid (workbench variant) — KPI strip across the top, mid-section split between an upload zone and a wide chart, a data table, and a "today" activity rail.
- **Sidebar archetype**: N3 — fixed 272px left rail, grouped, multi-level expandable, white surface, badge support per item, group separators with uppercase plain-text labels.
- **Header archetype**: Slim 80px top bar — logo cap (above sidebar), then a horizontal action strip (weather, language, search, fullscreen, notifications, theme, settings, avatar) right-aligned. Sits *outside* the sidebar column.
- **Footer archetype**: Ft7 — three-cell legal/version/help line at the bottom of `main`.
- **Density**: Medium — generous gap (16–24px) between cards, comfortable cell height in tables (~52px row), 14px body text. Not "dense" like a trading dashboard; not "generous" like an editorial page.
- **Asymmetry**: Centred-symmetric within each card; whole page reads as a Bento grid where row 1 = 4 equal tiles, row 2 = 2 unequal (upload narrow + chart wide), row 3 = 1 wide table, row 4 = sidebar list.
- **Voice**: Friendly enterprise — emoji in labels (📱 ⚡ 💼 🔥 ✨ 💖), playful badges, but Bootstrap-rigid spacing. This is the **single biggest mismatch with OET** — OET's voice should be clinical/educational, not playful-corporate.

## Provenance

- **Source mode**: URL (live page via Playwright)
- **Source URL**: https://axelit-next.vercel.app/dashboard/project
- **Source date**: 2026-05-24
- **Attestation**: (b) — public reference for the OET admin panel rebuild
- **Vendor**: la-themes / teqlathemes (ThemeForest)
- **Confidence**: Tokens *exact* (extracted from `:root` CSS custom properties). Fonts *exact* (Montserrat declared in `--font-Montserrat`). Rhythm *observed* (full-page screenshot captured). Dark-mode parity *partial* (Bootstrap `[data-bs-theme="dark"]` selector exists; live toggle not verified — see [05-CONFIDENCE-GAP-ANALYSIS.md](05-CONFIDENCE-GAP-ANALYSIS.md)).

## Tokens

```css
/*
 * Adapted from Axelit (la-themes), source: axelit-next.vercel.app
 * DO NOT ship these literal values to OET — recolor per OET brand identity.
 * These are the *role tokens*; remap the values to OET-appropriate hues.
 */

:root {
  /* ─── Brand role tokens (REMAP for OET) ─────────────────────────── */
  --color-primary:    rgb(140, 118, 240);   /* Axelit: violet — REMAP */
  --color-secondary:  rgb(100, 100, 100);   /* neutral grey */
  --color-success:    rgb(20, 120, 52);     /* deep green */
  --color-danger:     rgb(240, 10, 200);    /* Axelit: magenta — REMAP for OET (use red) */
  --color-warning:    rgb(215, 220, 65);    /* Axelit: chartreuse — REMAP for OET (use amber) */
  --color-info:       rgb(46, 94, 231);     /* royal blue */
  --color-light:      rgb(215, 208, 200);   /* warm beige */
  --color-dark:       rgb(40, 38, 50);      /* near-black, slight purple */

  /* ─── Dark-text-on-light-bg variants (for "light" button pattern) ─ */
  --color-primary-dark:   rgb(36, 17, 135);
  --color-secondary-dark: rgb(106, 90, 100);
  --color-success-dark:   rgb(52, 50, 46);
  --color-danger-dark:    rgb(102, 15, 106);
  --color-warning-dark:   rgb(99, 89, 29);
  --color-info-dark:      rgb(8, 60, 128);

  /* ─── Surface tokens ──────────────────────────────────────────── */
  --bg-body:          #f6f6f6;
  --bg-body-alt:      #f9f9f9;
  --bg-card:          #ffffff;
  --bg-light-gray:    #f4f7f8;
  --bg-tertiary:      #f8f9fa;

  /* ─── Text tokens ─────────────────────────────────────────────── */
  --text-default:     #15264b;  /* navy body */
  --text-title:       #1c3264;  /* navy heading (1pt darker) */
  --text-secondary:   #22242c;
  --text-muted:       #a0a0b0;
  --text-on-primary:  #ffffff;

  /* ─── Border tokens ───────────────────────────────────────────── */
  --border-default:   #e0dfd6;  /* warm beige — DELIBERATELY warm, not cool */
  --border-grid:      rgba(144, 164, 246, 0.21);  /* cool violet at 21% */

  /* ─── Radius scale ────────────────────────────────────────────── */
  --radius-sm:        0.25rem;   /* 4px  — tight UI (form labels) */
  --radius-md:        0.5rem;    /* 8px  — secondary cards */
  --radius-lg:        1rem;      /* 16px — input fields */
  --radius-xl:        1.8rem;    /* 28.8px — PRIMARY card radius (87 instances on dashboard) */
  --radius-pill:      50rem;     /* fully rounded — badges, avatars */

  /* ─── Shadow scale ────────────────────────────────────────────── */
  --shadow-ambient:   0 0 21px 3px rgba(100, 100, 100, 0.05);  /* default card */
  --shadow-hover:     0 0.5rem 2rem #f4f7f8;                   /* lift on hover */
  --shadow-bottom:    0 8px 6px -5px #f4f7f8;                  /* sticky header below shadow */
  --shadow-sm:        0 0.125rem 0.25rem rgba(0,0,0,0.075);
  --shadow-lg:        0 1rem 3rem rgba(0,0,0,0.175);

  /* ─── Layout tokens ───────────────────────────────────────────── */
  --sidebar-width:    17rem;    /* 272px expanded */
  --sidebar-semi:     4.5rem;   /* 72px icon-only */
  --header-height:    80px;
  --main-padding-top: 32px;

  /* ─── Type tokens ─────────────────────────────────────────────── */
  --font-display: "Montserrat", system-ui, sans-serif;
  --font-body:    "Montserrat", system-ui, sans-serif;  /* single-family system */
  --font-mono:    SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace;

  --text-h1:   2.5rem;    /* 40px */
  --text-h2:   2rem;      /* 32px */
  --text-h3:   1.75rem;   /* 28px */
  --text-h4:   1.25rem;   /* 20px */
  --text-h5:   1.125rem;  /* 18px — card heading */
  --text-h6:   1rem;      /* 16px — sub-heading inside cards */
  --text-body: 14px;
  --text-btn:  15px;
  --text-micro: 12px;

  /* ─── Motion tokens ───────────────────────────────────────────── */
  --transition-default: all 0.3s ease;   /* Axelit's sweeping default — Hallmark flags this as `transition-all` anti-pattern; OET should narrow to (transform, opacity) */
  --ease-out: cubic-bezier(0.25, 0.46, 0.45, 0.94);
  --ease-in:  cubic-bezier(0.55, 0.06, 0.68, 0.19);

  /* ─── Bootstrap breakpoints (inherited) ───────────────────────── */
  --bp-sm:  576px;
  --bp-md:  768px;
  --bp-lg:  992px;    /* sidebar collapses below this */
  --bp-xl:  1200px;
  --bp-xxl: 1400px;
}

[data-bs-theme="dark"] {
  /* Inferred from Bootstrap convention — verify on next study pass */
  --bg-body:          #1c1d24;
  --bg-card:          #282632;     /* --color-dark */
  --text-default:     #e9ecef;
  --text-title:       #ffffff;
  --text-muted:       #6c757d;
  --border-default:   #343a40;
  /* Accent role tokens unchanged across themes */
}
```

## Notes — anti-patterns to NOT carry over from Axelit

These were observed in Axelit and SHOULD NOT be inherited by OET admin:

1. **`transition: all 0.3s ease` everywhere** — Hallmark slop-test gate. Animating `all` triggers layout-property transitions (height/width/etc.) and causes jank. OET should narrow to `transform, opacity` and use specific easings.
2. **Six chromatic accents used together** (violet + magenta + chartreuse + blue + green + beige). OET should anchor on ONE primary + a desaturated neutral palette + status colours (success/warning/danger) only.
3. **Emoji-as-content in headings** (`💼 Core Teams`, `🔥 1H left`, `📱 Mobile app`). Educational/clinical voice should not lean on emoji for hierarchy.
4. **Soft-tinted "light" buttons everywhere** (`btn-light-primary` with color@30% bg). They look friendly but make hierarchy hard to read in a data-dense admin. Reserve for low-emphasis context-actions inside cards.
5. **28.8px radius on every card** — reads polished but blurs hierarchy. Mix radii: data tables = sharp (4–8px), KPI tiles = medium (12–16px), promotional/onboarding cards = pillowy (24px+).
6. **Tawk.to live chat injected globally** — third-party chat in an enterprise admin is rare; remove for OET.
7. **Customizer panel as default UX** — a runtime theme switcher is template-marketing chrome, not production enterprise feature. Remove.
8. **Welcome modal as default page-load behaviour** — annoying on every refresh. Use a once-only onboarding flow keyed to user state.
9. **Phosphor Icons + Tabler Icons + lucide-react simultaneously** — three icon libraries is template bloat. OET should standardise on `lucide-react` (already in the OET codebase per `app/admin/layout.tsx`).
10. **DataTables.net** — heavy jQuery legacy. OET should use TanStack Table or a Next.js-native alternative.

## Variants

(reserved — populate as future Hallmark runs in this OET project propose system variations)

## Exports

(populated by `hallmark default` runs once OET-specific tokens replace the Axelit values)
