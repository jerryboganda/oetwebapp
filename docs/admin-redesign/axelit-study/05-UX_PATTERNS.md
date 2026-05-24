# UX_PATTERNS.md — Axelit's interaction & UX vocabulary

**Source**: Axelit pages crawled + DOM probe + accessibility tree.

---

## 1 · Information hierarchy philosophy

**Three layers, by visual weight:**

1. **KPI strip** at top — biggest numbers (H2, 32-45px), least context. Scannable in 2 seconds.
2. **Mid-section cards** — visualization (charts) + structured data (tables). H5 (18px) headings, body 14px.
3. **Activity rail / supplementary** at right or bottom — recent items, timelines, tasks. H6 (16px).

This matches the **F-pattern reading flow**: eye lands top-left → scans top KPIs → drops into mid-section detail → exits via right rail.

## 2 · Scanning behaviour aids

- **Status colour-coding everywhere**: green = success, magenta = danger, blue/violet = info/active.
- **Trend arrows** (▲ ▼) next to KPI numbers — green up = good, red down = bad.
- **Pill badges** rather than long descriptions ("In Progress" not "This project is currently in active development").
- **Avatar stacks** rather than name lists ("👤👤👤 + 10 more").
- **Emoji in status copy** (🔥 1H left, ✨ 2D left) — fast emotional cue.

## 3 · Cognitive-load reduction patterns

| Pattern | Where used | Why |
| ------- | ---------- | --- |
| Pre-loaded skeleton states | "Loading FilePond…", "Loading Chart…" | Slow widgets show their slot before mounting |
| Pagination with "Showing X to Y of Z" caption | All data tables | Anchors the user in the dataset |
| Sticky header (header-main 80px) | Universal | Top-level actions always reachable |
| Sidebar group separators (uppercase plain text) | Sidebar nav | Lets eye chunk 80+ links into 7-8 groups |
| Right-side customizer panel | Theme settings | Settings live OFF the main canvas, not as page chrome |

## 4 · State communication patterns

| State | Visual treatment |
| ----- | ---------------- |
| Idle / default | Plain text + neutral icon |
| Selected | Light-primary background tint + dark-primary text |
| Hover (interactive) | Subtle background change (3.5% black alpha on table rows) |
| Active (clicked) | Primary background + white text |
| Loading | Skeleton text + spinner |
| Error | Red/danger badge + ⚠ icon + brief copy |
| Success | Green/success badge + ✓ icon |
| Disabled | Reduced opacity (0.5-0.6) + cursor `not-allowed` |
| Empty | Centered illustration + 1-line copy + CTA button |

## 5 · Action-discovery patterns

- **Top-right of every card** = card-level menu (`⋯` kebab) for card actions (refresh, expand, hide)
- **Top-right of every page** = page-level CTA (Create / Import / Export)
- **Top-right of every table** = bulk actions when rows selected (Delete, Archive, Export)
- **Inline row actions**: small icon buttons at the rightmost cell (Edit ✎, Delete 🗑, View 👁)
- **Floating customizer** bottom-right — global cross-route action

## 6 · Onboarding / empty-state handling

- **Welcome modal on first page load** — full overlay with hero illustration + CTA "Get Started" (anti-pattern: shows every refresh)
- **Promo cards** ("Get started Effortlessly.") — full-width card with copy + logo grid + image, intended for cross-sell / feature promotion
- **Tooltip-driven tour** — `/advance-ui/tour` route implies driver.js or Shepherd tour pattern

## 7 · Navigation patterns

**Three navigation surfaces**:

1. **Vertical sidebar** — primary route hierarchy, multi-level (2-3 deep)
2. **Header action strip** — utility actions (search, notifications, settings, profile)
3. **Tabs inside `<main>`** — page-level sub-navigation (e.g. Settings tabs: Profile / Notifications / Privacy / Security)

**Breadcrumbs**: Implied present on detail pages but not captured in this pass.

## 8 · Dashboard scanning patterns

Axelit's dashboards are designed for **glance > drill > act**:
- **Glance** (3 sec): top KPI strip — scan numbers, spot anomalies
- **Drill** (15 sec): mid-section chart trends + status table
- **Act** (30+ sec): row-level actions on the data table, or click into detail page

## 9 · Form patterns (inferred from Settings page screenshot)

- **Label above input** (not inline)
- **Full-width inputs** on mobile, half-width or third-width on desktop (grid-based)
- **Action row at bottom right** ("Save Changes" primary button + "Cancel" outline/link)
- **Section dividers** (hairline rules between form sections within a card)
- **Helper text under inputs** (small, muted)
- **Inline validation** (error message below input, red text, ⚠ icon)

## 10 · Data table patterns

- **Sticky header** (sortable columns)
- **Row hover** — subtle 3.5% darken
- **Row click** — typically navigates to detail page (not "select row" by default)
- **Row checkbox** (when present) — leftmost cell, enables bulk actions
- **Inline action cell** — rightmost, small icon buttons
- **Status as badge** (not text)
- **Avatar in TeamLead cell** (visual, links to person)
- **Pagination at bottom**: `Previous | 1 2 3 | Next` + "Showing X to Y of Z entries"

## 11 · Modal / drawer patterns

- **Modal**: blocking centered dialog with backdrop. Use for: confirmations, single-step forms, welcomes.
- **Off-canvas drawer** (right side, e.g. customizer): non-blocking, used for: settings, filters, detail-preview.
- **Sweet Alert**: skinned confirm/alert/prompt for transactional actions.

## 12 · Accessibility patterns observed

- **Landmark roles** present: `<nav>`, `<header>`, `<main>`, `<footer>`, `[role="banner"]`, `[role="region"]`
- **`aria-label` on icon buttons** (not consistent — some are bare)
- **`alt` text on `<img>` avatars** (present but always literally "avatar" — placeholder, not meaningful)
- **Focus states** — Bootstrap default focus ring (`--bs-focus-ring-color: rgba(13,110,253, 0.25)` — 4px blue ring). Not customized for Axelit's violet brand.
- **Heading hierarchy** — H5 used as primary card title (not H2/H3) — *accessible if H1 exists elsewhere (page-level), but H1 not visible on most observed pages*.
- **`sr-only` text** — present (e.g. "Admin Dashboard" hidden H1 in OET equivalent)
- **`prefers-reduced-motion`** — not declared in observed CSS variables; animate.css respects this by default
- **Keyboard nav** — not tested in this pass

## 13 · Enterprise UX consistency patterns

- **Same chrome on every route** (sidebar + header + footer)
- **H5 as universal card title** (not mixed with H4 or H6)
- **Bootstrap row/col grid throughout** — no CSS grid, no flex-only layouts
- **One radius for surfaces (28.8px), one for buttons (5px)** — predictable
- **One spinner style** (Bootstrap default, customizable color via spinner variants)
- **One toast style** (placement: top-right, dismissible)
