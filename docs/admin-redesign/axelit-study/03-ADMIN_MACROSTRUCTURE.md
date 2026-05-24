# ADMIN_MACROSTRUCTURE.md — Axelit's structural fingerprint

**Source**: Axelit `/dashboard/project` (primary), `/dashboard/ecommerce`, `/apps/kanban-board`, `/apps/e-shop/orders-list`, `/apps/setting` — cross-checked.
**Hallmark macrostructure code**: **Bento Grid** (workbench variant) with consistent chrome (fixed sidebar + horizontal header).

---

## 1 · Page skeleton (universal across all admin routes)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ┌────────┐  ┌─────────────────────────────────────────────────────────┐ │
│ │  LOGO  │  │  HEADER (80px, transparent over --bg-body)              │ │
│ │ 272px  │  │  ──────────────────────────────────────────────────────  │ │
│ │ sidebar│  │  weather · lang · search · fullscreen · 🔔 · 🌙 · ⚙ · 👤 │ │
│ │ ┌────┐ │  └─────────────────────────────────────────────────────────┘ │
│ │ │NAV │ │  ┌─────────────────────────────────────────────────────────┐ │
│ │ │GRPS│ │  │                                                         │ │
│ │ │    │ │  │  MAIN  (1613px on 1920 viewport, 32px top padding)      │ │
│ │ │ ─  │ │  │  ───────────────────────────────────────────────────     │ │
│ │ │ ─  │ │  │                                                         │ │
│ │ │ ─  │ │  │   Bento grid of cards (see §3)                          │ │
│ │ │ ─  │ │  │                                                         │ │
│ │ │ ─  │ │  │                                                         │ │
│ │ └────┘ │  └─────────────────────────────────────────────────────────┘ │
│ │        │  ┌─────────────────────────────────────────────────────────┐ │
│ │        │  │  FOOTER  © 2025 axelit 💖    V1.0.0    Need Help        │ │
│ │        │  └─────────────────────────────────────────────────────────┘ │
│ └────────┘                                                                │
│                                                       ┌──────────────┐    │
│                                                       │ ⚙ Customizer │ ← floating │
│                                                       └──────────────┘    │
└──────────────────────────────────────────────────────────────────────────┘
```

**Three persistent regions**:
1. **Vertical sidebar** (`<nav class="vertical-sidebar">`) — fixed left, 272px, white, full viewport height.
2. **Header** (`<header class="header-main">`) — 80px tall, padding-left 292px (so it starts AFTER the sidebar).
3. **Main** (`<main>`) — 1613px wide on 1920 viewport (1920 − 272 sidebar − 35 padding). 32px top padding.

**One floating element**: customizer button (bottom-right, violet circle).

## 2 · Sidebar (N3 archetype — multi-level grouped vertical)

**Composition**:

```
┌──────────────────┐
│   ╔══╗  logo     │  ← cap (above sidebar)
├──────────────────┤
│  DASHBOARD  ─ ── │  ← uppercase plain-text group separator
│  ⊡ Dashboard  4 │  ← icon + label + badge (collapsible parent)
│  └ Project       │  ← child (visible because expanded)
│  └ Ecommerce     │  ← child
│                  │
│  ⊡ Apps          │  ← collapsed parent (chevron right)
│                  │
│  ⊡ Widgets       │  ← single-link item (no children)
│                  │
│  COMPONENT  ─ ── │  ← separator
│  ⊡ Ui Kits       │
│  ⊡ Advanced UI 12+│ ← badge can be string "12+" or "new"
│  ⊡ Icons         │
│  ⊡ Misc          │
│                  │
│  MAP & CHARTS ── │
│  ⊡ Map & Charts  │
│  ⊡ Chart         │
│                  │
│  TABLE & FORMS ─ │
│  ⊡ Table         │
│  ⊡ Forms elements│
│  ⊡ Ready to use new│
│                  │
│  PAGES ────────── │
│  ⊡ Auth Pages    │
│  ⊡ Error pages   │
│  ⊡ Other pages   │
│                  │
│  OTHERS ──────── │
│  ⊡ 2 level       │  ← demonstrates multi-level nesting
│  ⊡ Document  ↗   │  ← external link with arrow
│  ⊡ Support   ✉   │  ← mailto link
└──────────────────┘
```

**Item anatomy**:
- 24×24 icon (img tag, dark on white)
- Label text (Montserrat 14px)
- Optional badge (number `4`, or string `new` / `12+`) — pill, primary or accent
- Chevron right (when parent of group) → rotates to down on expand
- Active state: violet text + light-violet tint background (`btn-light-primary` pattern reused)

**Group separator**: a list-item with uppercase plain-text label, no icon, no link. Spaced above and below.

## 3 · Main content macrostructure (Project Dashboard exemplar)

The captured `/dashboard/project` page has **8 distinct grid rows**, each a different Bento configuration:

```
┌────────────────────────────────────────────────────────────────┐
│ Row 1 · KPI strip (4 columns, equal width)                      │
│ ┌──────┐ ┌────────────────┐ ┌────────────────┐ ┌──────────┐    │
│ │Total │ │ Project Alpha  │ │ Project Beta   │ │ 💼 Core   │    │
│ │Hours │ │ (tag chips,    │ │ (same archetype│ │ Teams (1k│    │
│ │ 0H   │ │ avatars,       │ │ different copy)│ │ Members) │    │
│ │ ▮▮▮  │ │ 🔥 1H left)    │ │ ✨ 2D left)    │ │ avatars  │    │
│ └──────┘ └────────────────┘ └────────────────┘ └──────────┘    │
├────────────────────────────────────────────────────────────────┤
│ Row 2 · Mid-section split (4/8 columns)                         │
│ ┌──────────┐  ┌──────────────────────────────────────────┐     │
│ │ FilePond │  │  Chart placeholder                       │     │
│ │ uploader │  │  (Loading Chart...)                      │     │
│ └──────────┘  └──────────────────────────────────────────┘     │
├────────────────────────────────────────────────────────────────┤
│ Row 3 · Promo card (full width, 12 cols)                        │
│ ┌────────────────────────────────────────────────────────────┐ │
│ │  Get started Effortlessly.  [copy] [logos] [image]         │ │
│ └────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────┤
│ Row 4 · Tracker (left) + Project Status table (right)           │
│ ┌──────────┐  ┌──────────────────────────────────────────┐     │
│ │ Tracker  │  │ Project Status                           │     │
│ │ 00:00:00 │  │ Project │ Status   │TeamLead│Priority│   │     │
│ │ ▶ ⏸ ⏹    │  │ ─────── │ ──────── │ ────── │ ────── │   │     │
│ │ Session1 │  │ Web Re.. │ In Prog. │  👤    │  High  │   │     │
│ │ Session2 │  │ Mobile.. │ Compl.   │  👤    │  Med   │   │     │
│ │ Session3 │  │ Campa..  │ Not Strt │  👤    │  Low   │   │     │
│ │ Session4 │  │ E-Comm.. │ In Prog. │  👤    │  High  │   │     │
│ │ Session5 │  │ SEO ..   │ In Prog. │  👤    │  Med   │   │     │
│ │          │  │ UI/UX..  │ Schd.    │  👤    │  Low   │   │     │
│ │          │  │  Pagination: Prev 1 2 3 Next             │     │
│ └──────────┘  └──────────────────────────────────────────┘     │
├────────────────────────────────────────────────────────────────┤
│ Row 5 · Today Tasks (full width, horizontal scroll list)        │
│ ┌─────────────────────────────────────────────────────────────┐│
│ │ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ───→ ││
│ │ │Meet  │ │API   │ │Test  │ │Final.│ │Meet  │ │Design│      ││
│ │ │+0%   │ │+60%  │ │+80%  │ │+68%  │ │+0%   │ │+35%  │      ││
│ │ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘      ││
│ └─────────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────┘
```

**Pattern abstraction (Hallmark macrostructure terms)**:

| Row | Bootstrap cols | Cards | Archetype |
| --- | -------------- | ----- | --------- |
| 1 KPI | `col-lg-3` × 4 | 4 equal | **F1 — even-grid feature row** |
| 2 Mid | `col-lg-4` + `col-lg-8` | 2 unequal | **H2 — split diptych** |
| 3 Promo | `col-12` | 1 wide | **H5 — letter/promo wide** |
| 4 Tracker + Table | `col-lg-4` + `col-lg-8` | 2 unequal | **F2 — sidebar + main split** |
| 5 Tasks | `col-12` (horizontal scroll inside) | 1 wide carousel | **F3 — horizontal track** |

## 4 · Cross-page consistency

| Page | Sidebar | Header | Card archetype | Notes |
| ---- | ------- | ------ | --------------- | ----- |
| `/dashboard/project` | ✓ | ✓ | Bento mix (above) | 5 grid rows |
| `/dashboard/ecommerce` | ✓ | ✓ | Bento heavy (37 cards) | KPI strip + Apex chart row + product table |
| `/apps/projects-page/projects` | ✓ | ✓ | Card grid (uniform tiles) | Each tile = project card |
| `/apps/kanban-board` | ✓ | ✓ | 3-column board | Kanban columns inside `.card` containers |
| `/apps/e-shop/orders-list` | ✓ | ✓ | Single-card data table | Header strip + filters + DataTable + pagination |
| `/apps/setting` | ✓ | ✓ | Tabs + form sections | Tab nav top, form fields below |
| `/ui-kit/buttons` | ✓ | ✓ | Gallery (specimen pattern) | One H5 per variant family |

**Universal rules extracted**:
1. **Every page is a `<main>` of cards**. The sidebar + header are persistent chrome; only `<main>` changes per route.
2. **Every section is a `.card`** with an H5 title.
3. **Cards align to Bootstrap rows + cols** (no CSS grid; no flex-only layouts).
4. **No page-level hero** (this is admin, not marketing).
5. **Card spans follow Bootstrap responsive breakpoints**: `col-lg-X` for desktop, `col-md-X` for tablet, `col-12` for mobile.

## 5 · Reusable macrostructure templates

These four templates cover ~90% of Axelit admin routes. For the OET rebuild, codify these as React layout components:

### Template A — "Operations Overview" (e.g. `/dashboard/project`)
```
Row 1: 4× KPI cards (col-lg-3)
Row 2: Wide chart (col-lg-8) + narrow input/widget (col-lg-4)
Row 3: Full-width data table card (col-12)
Row 4: 1-3 supplementary activity cards
```

### Template B — "Catalog / Index" (e.g. `/apps/projects-page/projects`)
```
Row 1: Filter strip (search + sort + view-mode toggle + Create CTA, col-12)
Row 2..n: Uniform card grid (col-lg-4 × 3 = 9 per page, paginated)
```

### Template C — "Data Table" (e.g. `/apps/e-shop/orders-list`)
```
Row 1: Page title + breadcrumb (col-12)
Row 2: Single card containing:
  - Toolbar (search, filter, export, Create CTA)
  - Table (sticky header)
  - Footer (pagination + "Showing X to Y of Z")
```

### Template D — "Form / Settings" (e.g. `/apps/setting`)
```
Row 1: Page title (col-12)
Row 2: Single card containing tabs
Row 3: Per-tab — vertical form (label-above-input, full-width inputs, action row right-aligned at bottom)
```
