# ADMIN_DESIGN_SYSTEM.md — Forensic spec of Axelit's design system

**Source**: `https://axelit-next.vercel.app` (Axelit by la-themes, ThemeForest commercial)
**Extracted**: 2026-05-24
**Purpose**: Comprehensive reference for the OET admin panel rebuild. Use this as the source-of-truth document when designing the OET admin's design system.

---

## 1 · System identity

| Attribute | Value | Confidence |
| --------- | ----- | ---------- |
| **Genre** | Enterprise SaaS dashboard, friendly-corporate | High |
| **Tone** | Playful-professional (heavy emoji, soft pillow cards, rounded everything) | High |
| **Density** | Medium (not Bloomberg-dense, not Apple-airy) | High |
| **Asymmetry** | Centred-symmetric inside cards; full-page reads as a Bento grid | High |
| **Hallmark macrostructure equivalent** | **Bento Grid** (the workbench variant — KPI strip + tables + activity rail) | High |
| **Theme cluster** | Closest catalog match: **Quiet** for layout discipline, but Axelit's chromatic-six-accent palette is closer to **Plume** | High |
| **Stack** | Next.js (App Router) + Bootstrap 5 + custom CSS variables + Tabler & Phosphor icons + ApexCharts + DataTables | High |

## 2 · The 8-token system

Axelit defines its full theme as **8 chromatic role tokens + 8 darkened variants + 5 surface tokens + 4 text tokens + 2 border tokens**. The "8 role" pattern is identical to Bootstrap 5 but with substituted values:

| Role | Bootstrap default | Axelit value | OET recommendation |
| ---- | ----------------- | ------------ | ------------------ |
| `--primary`   | `#0d6efd` blue  | `rgb(140, 118, 240)` violet  | **OET clinical blue** (e.g. `#1c3264` navy already in OET tokens) |
| `--secondary` | `#6c757d` grey  | `rgb(100, 100, 100)` grey    | Keep neutral grey |
| `--success`   | `#198754` green | `rgb(20, 120, 52)` deep green | Keep similar — `#198754` |
| `--danger`    | `#dc3545` red   | `rgb(240, 10, 200)` magenta  | **REVERT to red** (`#dc3545`) — magenta is template-marketing voice, not clinical |
| `--warning`   | `#ffc107` amber | `rgb(215, 220, 65)` chartreuse | **REVERT to amber** (`#ffc107`) — chartreuse is too playful |
| `--info`      | `#0dcaf0` cyan  | `rgb(46, 94, 231)` royal blue | Keep royal blue |
| `--light`     | `#f8f9fa` cool  | `rgb(215, 208, 200)` warm beige | Pick **either** cool or warm and commit — OET is currently inconsistent |
| `--dark`      | `#212529` black | `rgb(40, 38, 50)` purple-black | Use the OET navy `#1c3264` as dark anchor |

**Rule extracted**: every role token has a `-dark` companion used for *text-on-tinted-bg* (the "light button" pattern). The two-value-per-role system supports the entire "Light variant" treatment without inventing new colours mid-render.

## 3 · The surface hierarchy

Axelit uses **5 surfaces** stacked from deepest to lightest:

```
┌──────────────────────────────────────────────────┐
│  --bg-body         #f6f6f6   ← page background  │ outer wrap
│  ┌────────────────────────────────────────────┐  │
│  │ --bg-card        #ffffff  ← card body      │  │ raised tile
│  │   ┌────────────────────────────────────┐   │  │
│  │   │ --bg-tertiary   #f8f9fa            │   │  │ inside-card
│  │   │   (e.g. table header strip)        │   │  │ section
│  │   └────────────────────────────────────┘   │  │
│  └────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘

Sidebar uses --bg-card (#ffffff) directly — white-on-grey-body pattern.
Header is transparent → inherits --bg-body underneath.
```

**OET application**: The OET admin uses `bg-admin-surface` and `bg-admin-base` tokens in `app/admin/page.tsx` — verify these map cleanly to the 5-layer hierarchy.

## 4 · Spacing & layout grid

| Token | Value | Use |
| ----- | ----- | --- |
| `--sidebar-width` | `17rem` (272px) | Fixed left rail (expanded) |
| `--semi-nav` | `4.5rem` (72px) | Collapsed icon-only sidebar |
| `--header-height` | observed `80px` | Top action bar |
| `--main-padding-top` | observed `32px` | Above first card |
| Card gap (inter-card) | observed `~16-24px` | Bootstrap `.row` gutter |
| Inside-card padding | observed `16-20px` | Standard `.card-body` |
| `--app-transition` | `all 0.3s ease` | (anti-pattern, see Notes) |

**Bootstrap breakpoints in use** (verbatim from CSS):
- `--bs-breakpoint-xs: 0`
- `--bs-breakpoint-sm: 576px`
- `--bs-breakpoint-md: 768px`
- `--bs-breakpoint-lg: 992px` ← **sidebar collapses below this**
- `--bs-breakpoint-xl: 1200px`
- `--bs-breakpoint-xxl: 1400px`

## 5 · Typography ladder

Single-family system: **Montserrat** for every text role. Bootstrap defaults are *declared* but overridden by `--font-Montserrat: "Montserrat", system-ui`.

| Token | Value | Rendered as | Use |
| ----- | ----- | ----------- | --- |
| `--h1-font-size` | `2.5rem` (40px) | `44.96px / 62.94px / 600` (page override) | Page hero (rare in admin) |
| `--h2-font-size` | `2rem` (32px) | `45px / 63px / 700` | Stat-card primary value (`2.36k`) |
| `--h3-font-size` | `1.75rem` (28px) | (not observed) | Section heading |
| `--h4-font-size` | `1.25rem` (20px) | `(observed in KPI numbers)` | Secondary stat value |
| `--h5-font-size` | `1.125rem` (18px) | (observed on every card) | **Card title (dominant heading)** |
| `--h6-font-size` | `1rem` (16px) | (observed on rows / tasks) | Row item / sub-heading |
| `--p-font-size` | `14px` | `13px / 20.8px / 500` (override) | Body paragraph |
| `--font-size` (body) | `14px` | `14px / 21px / 400` | Default body |
| `--btn-font-size` | `15px` | `15px / 600` | Button label |

**Rule extracted**: H5 is the *dominant* card heading — every panel in the captured dashboard uses an H5 ("Total Hours", "Tracker", "Project Status", "Today Tasks"). H1/H2 are reserved for KPI numbers, not titles.

## 6 · The radius philosophy — "polished pillow"

87 instances of `28.8px` (== `1.8rem`) on the dashboard page alone. This is **the single biggest visual choice in Axelit's design system**.

| Radius | Frequency | Use observed |
| ------ | --------- | ------------ |
| `28.8px` (`1.8rem`) | 87× | Cards, KPI tiles, large containers — **primary** |
| `50px` | 50× | Avatar circles, pill buttons, status chips |
| `16px` (`1rem`) | 21× | Inputs, secondary surfaces |
| `5px` | 13× | Standard buttons (the UI-kit shows `.btn` = `5px`) |
| `10px` | 9× | Tertiary chips |
| `8px` | 7× | Small surfaces |
| `50%` | 6× | Circles (avatars, status dots) |
| `18px` | 3× | Icon buttons |

**Rule extracted**: Axelit uses *one big radius* (28.8px) for visual identity + a smaller pragmatic radius (5px) for buttons. The two don't mix — buttons stay sharp, surfaces stay pillowy. Don't blend.

**OET recommendation**: Pick **one** dominant radius for cards (recommend `1rem` / 16px — less template-y than 28.8px), `0.5rem` / 8px for buttons, full-pill for chips/avatars. Three values total.

## 7 · Shadow philosophy — ambient, not directional

| Token | Value | Use |
| ----- | ----- | --- |
| `--box-shadow` | `0px 0px 21px 3px rgba(100,100,100,0.05)` | Default card — **omnidirectional, 5% alpha** |
| `--hover-shadow` | `0 0.5rem 2rem #f4f7f8` | On-hover lift |
| `--bottom-shadow` | `0 8px 6px -5px #f4f7f8` | Sticky header below shadow |

**Rule extracted**: Axelit's shadows are *ambient haloes*, not Material directional drops. The default card has zero Y-offset and a 21px spread at 5% alpha — the card "glows" rather than "lifts". This is the modern enterprise neutral.

## 8 · Gradient system

Five branded gradients defined, used sparingly:

| Token | Composition | Where used |
| ----- | ----------- | ---------- |
| `--primary-gradient` | violet (10%) → magenta (50%) → chartreuse (100%) | Signature brand gradient, used on auth pages |
| `--secondary-gradient` | violet → green → violet | Wallpaper/onboarding cards |
| `--dark-gradient` | violet → dark → violet | Dark-mode hero accents |
| `--body-bg-gradient` | violet@9% → white → violet@9% | Subtle body backdrop |
| `--app-gradient` | violet → green → violet → chartreuse | Featured callouts |

**OET application**: Gradients are highly template-coded. Use AT MOST ONE gradient (anchored on the OET primary), in AT MOST ONE place per page (typically the auth hero or empty-state illustration).

## 9 · Icon library stack

Axelit loads THREE icon systems in parallel:
1. **Tabler Icons** webfont (`cdn.jsdelivr.net/npm/@tabler/icons-webfont`)
2. **Phosphor Icons** web v2.0.3 — *all 6 weight variants* (regular, thin, light, bold, fill, duotone)
3. **Lucide React** (inferred from React component output) — partially used in some custom React components

**OET application**: Standardise on **lucide-react only** (already imported in `app/admin/layout.tsx`). Drop Tabler + Phosphor; they're template scaffolding.

## 10 · Chart + table library

- **Charts**: ApexCharts (every chart container is `<svg>` without canvas; markup matches Apex's signature). Loaded lazily ("Loading Chart..." placeholder visible).
- **Tables**: DataTables.net (visible from `--dt-row-selected`, `--dt-row-hover`, `--dt-column-ordering-alpha` custom properties — jQuery DataTables fingerprint).
- **File upload**: FilePond (visible "Loading FilePond..." placeholder).
- **Sweet Alert**: Used for modal confirmations (`/advance-ui/sweet-alert` route exists).
- **Animation**: animate.css (`--animate-duration`, `--animate-delay`, `--animate-repeat` tokens).
- **Chat widget**: Tawk.to (third-party live chat).

**OET application**: For the OET rebuild:
- Charts → **Recharts** (already in OET's stack per memory notes).
- Tables → **TanStack Table** (React-native, no jQuery).
- File upload → **react-dropzone** or **uploadthing**.
- Modal → **Radix UI Dialog** or **shadcn/ui Dialog**.
- Animation → **framer-motion / motion** (already in OET per memory notes — package `motion` v12).
- Chat → **remove entirely** unless OET requires customer chat in admin (rare).
