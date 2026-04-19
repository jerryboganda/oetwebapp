# Design System: OET Prep Platform

**Project ID:** jerryboganda/oetwebapp
**Source-of-truth:** This document. Every UI change — learner, expert, admin, or sponsor — must conform.

The **learner dashboard** is the canonical reference. Every other portal (admin, expert, sponsor) must feel like the same product, just with different density or focus. The admin console in particular is held to strict parity: no raw HTML controls, no raw Tailwind colour classes outside the token set, no bespoke hero/card/table layouts.

---

## 1. Visual Theme & Atmosphere

| Quality | Direction |
| ------- | --------- |
| Mood | Warm clinical calm, not sterile |
| Canvas | Airy cream (`#f7f5ef`), never dense dark chrome |
| Tone | Premium academic workspace, not a marketing landing page |
| Motion | Supportive and gentle, soft cards, 160–320ms springs |
| Density | Data-rich but never cold or cluttered |

The overall feeling is a guided study environment. Surfaces are soft, spacing is generous, and the interface should reduce anxiety rather than create it. The admin console inherits the same warmth even though it is operational.

---

## 2. Color System

### 2.1 CSS variables (`app/globals.css`)

| Token | Light | Dark | Role |
| ----- | ----- | ---- | ---- |
| `--color-primary` | `#7c3aed` | `#a78bfa` | Primary actions, active nav, focus ring, hero accent |
| `--color-primary-dark` | `#6d28d9` | `#8b5cf6` | Hover / pressed states |
| `--color-lavender` | `#ede9fe` | `#1e1b4b` | Soft tints, chips, subtle backgrounds |
| `--color-navy` | `#0f172a` | `#e5eef9` | Headings, primary text, important icons |
| `--color-muted` | `#526072` | `#94a3b8` | Supporting text, labels, metadata |
| `--color-surface` | `#fffefb` | `#0f172a` | Cards, panels, hero container |
| `--color-background-light` | `#f7f5ef` | `#07111d` | App backdrop |
| `--color-background-dark` | `#07111d` | `#030712` | Inverted backdrop |
| `--color-border` | `#d8e0e8` | `#1f2937` | Card borders, dividers, field outlines |
| `--color-border-hover` | `#b9c6d1` | `#334155` | Hover border elevation |
| `--color-success` | `#10b981` | — | Streaks, completion, positive trends |
| `--color-warning` | `#d97706` | — | Freeze, caution, pending attention |
| `--color-danger` | `#ef4444` | — | Errors, destructive actions, critical alerts |
| `--color-info` | `#2563eb` | — | Informational states, chart series |
| `--color-disabled` | `#94a3b8` | — | Disabled controls |

### 2.2 Tailwind class mapping

| Use | Correct classes | Forbidden classes |
| --- | --------------- | ----------------- |
| App backdrop | `bg-background-light` | `bg-gray-50`, `bg-neutral-50`, hex |
| Card / panel surface | `bg-surface` | `bg-white`, `bg-gray-50` |
| Primary text | `text-navy` | `text-gray-900`, `text-black` |
| Secondary text | `text-muted` | `text-gray-500`, `text-gray-600` |
| Borders | `border-border`, `border-border-hover` | `border-gray-200`, `border-gray-300` (except inside `DataTable`/`FilterBar` which are legacy tokenised) |
| Brand accent fills | `bg-primary`, `bg-primary/10`, `bg-primary/90` | raw `bg-purple-600`, `bg-violet-500` |
| Brand accent text | `text-primary`, `text-primary-dark` | raw `text-purple-600` |
| Success | `text-emerald-700 bg-emerald-50 border-emerald-200` (or `Badge variant="success"`) | raw `text-green-500`, `bg-green-600` |
| Warning | `text-amber-700 bg-amber-50 border-amber-200` (or `Badge variant="warning"`) | raw `text-yellow-500` |
| Danger | `text-red-700 bg-red-50 border-red-200` (or `Badge variant="danger"`, `Button variant="destructive"`) | raw `text-red-500`, `text-red-600`, `bg-red-600` |
| Info | `text-blue-700 bg-blue-50 border-blue-200` (or `Badge variant="info"`) | raw `text-blue-500` |

### 2.3 Tint-based backgrounds

Reserve **saturated fills** (`bg-primary`, `bg-navy`) for actions and selected states. Use **tint backgrounds** (`bg-primary/10`, `bg-lavender/40`, `bg-*-50`) for:

- Chips
- Icon tiles
- Status chips
- Soft banners
- Hero and card accent frames

Never apply a saturated fill to a large surface.

---

## 3. Typography

### 3.1 Families

- **Primary:** Manrope — used for all UI text.
- **Display:** Fraunces (`font-display` utility) — used only for brand lockups. Never on utility pages.

### 3.2 Hierarchy (exact classes)

| Level | Classes |
| ----- | ------- |
| Page `<h1>` hero title | `text-xl font-semibold tracking-tight text-navy sm:text-[1.75rem]` |
| Section `<h2>` | `text-xl font-bold text-navy` |
| Panel `<h2>` (AdminRoutePanel) | `text-xl font-bold tracking-tight text-navy` |
| Card `<h3>` | `text-xl font-bold text-navy` (surface card) or `text-lg font-bold text-navy` (CardTitle) |
| Modal / Drawer title | `text-lg font-bold text-navy` |
| EmptyState / ErrorState title | `text-lg font-bold tracking-tight text-navy` |
| Body (hero description) | `text-sm leading-6 text-muted` |
| Body (card description) | `text-sm leading-relaxed text-muted` |
| Eyebrow — hero | `text-[11px] font-semibold uppercase tracking-[0.18em] text-muted` |
| Eyebrow — section header | `text-sm font-black text-muted uppercase tracking-widest` |
| Eyebrow — card chip | `text-xs font-bold uppercase tracking-wider` |
| Meta row | `text-sm font-semibold text-muted` |
| Highlight label | `text-[11px] font-semibold uppercase tracking-[0.16em] text-muted` |
| Highlight value | `text-sm font-semibold text-navy break-words` |
| Data figure | `text-2xl font-bold text-navy leading-tight` |
| Form label | `text-sm font-semibold tracking-tight text-navy` |
| Table header | `text-[11px] font-semibold uppercase tracking-[0.16em] text-muted` |
| Table cell | `text-sm text-navy` |

Never use `text-3xl`+ for inline values, or `font-bold` + `text-2xl` for anything that is not a KPI figure.

---

## 4. Spacing Rhythm

### 4.1 Vertical rhythm

| Context | Pattern |
| ------- | ------- |
| Top-level page blocks | `space-y-6` (default) or `space-y-10` for hero-split layouts |
| Section interior | `space-y-4` / `space-y-5` |
| Panel content | `space-y-4` / `space-y-5` |
| Hero → first panel | gap handled by `space-y-6` parent |
| Card-link list | `flex flex-col gap-3` |

### 4.2 Grid gaps

| Layout | Pattern |
| ------ | ------- |
| Primary cards pair | `grid grid-cols-1 gap-6 lg:grid-cols-2` |
| Main / side rail | `grid grid-cols-1 gap-6 lg:grid-cols-12` with `lg:col-span-8` + `lg:col-span-4` |
| 2-up tiles | `grid grid-cols-1 gap-3 sm:grid-cols-2` |
| 3-up tiles | `grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3` |
| 4-up KPI | `grid gap-4 md:grid-cols-2 xl:grid-cols-4` |
| 3-up StatRow | `grid gap-5 sm:grid-cols-3` |
| Meta row | `gap-x-4 gap-y-2` |

### 4.3 Padding

| Surface | Padding |
| ------- | ------- |
| `Card padding="sm"` | `p-4` |
| `Card padding="md"` (default) | `p-5` |
| `Card padding="lg"` | `p-6` |
| Hero | `px-4 py-4 sm:px-6 sm:py-6` |
| Workspace (learner) | `max-w-[1200px] px-4 sm:px-6 lg:px-8 py-2 sm:py-4 lg:py-6` |
| Workspace (admin) | `max-w-[1440px] px-4 sm:px-6 lg:px-8 py-2 sm:py-4 lg:py-6` |

---

## 5. Component Contracts

### 5.1 Button (`components/ui/button.tsx`)

```
<Button variant="primary" size="md">Label</Button>
```

| Variant | Classes | Use |
| ------- | ------- | --- |
| `primary` (default) | `bg-primary text-white hover:bg-primary/90 shadow-sm` | Main CTA |
| `secondary` | `bg-navy text-white hover:bg-navy/90 shadow-sm` | Brand dark CTA |
| `outline` | `border border-border text-navy hover:bg-surface hover:border-border-hover` | Neutral action |
| `ghost` | `text-navy hover:bg-lavender/40 dark:hover:bg-white/5` | In-section / nav |
| `destructive` | `bg-danger text-white hover:bg-danger/90 shadow-sm` | Destructive confirm |

**NEVER use `variant="default"`** — it does not exist on `Button`. See AGENTS.md.
Sizes: `sm` (`min-h-11 px-3 py-2 text-xs`), `md` (default, `min-h-11 px-5 py-2.5 text-sm`), `lg` (`min-h-12 px-6 py-3 text-base`).

### 5.2 Badge (`components/ui/badge.tsx`)

```
<Badge variant="danger">Failed</Badge>
```

Variants: `default` (primary tint), `success`, `warning`, `danger`, `info`, `muted`, `outline`.
**NEVER use `variant="destructive"`** — use `danger`. See AGENTS.md.

### 5.3 Card (`components/ui/card.tsx`)

Base: `rounded-2xl border border-border bg-surface shadow-sm`. Props: `padding` (`none|sm|md|lg`), `hoverable` (adds `hover:border-border-hover hover:shadow-clinical`). Subparts: `CardHeader`, `CardTitle`, `CardContent`, `CardFooter`, `CardLink`.

### 5.4 Alert (`components/ui/alert.tsx`)

`InlineAlert` for banners, `Toast` for transient. Variants: `info`, `success`, `warning`, `error`. Never hand-roll colored banners; always use `InlineAlert`.

### 5.5 DataTable (`components/ui/data-table.tsx`)

Auto dual-layout (mobile card list + desktop table). Props include `data`, `columns`, `onRowClick`, `mobileCardRender`, `density` (`default|compact`).

- `density="default"`: cells `px-5 py-4`.
- `density="compact"` (admin): cells `px-4 py-2.5`, headers `py-2.5`. Use for data-heavy admin pages (users, audit-logs, ai-usage, billing).

### 5.6 FilterBar (`components/ui/filter-bar.tsx`)

Desktop popover pattern + mobile sheet. Wrap every admin list view's filter strip in `FilterBar`. Never build raw `<button>` filter pills.

### 5.7 Modal (`components/ui/modal.tsx`)

Use for all overlays. Contains `Modal`, `ConfirmModal`, `Drawer`.

### 5.8 Tabs (`components/ui/tabs.tsx`)

Shared layout with animated pill. Use for any tab / section switcher. Admin variant: `AdminRouteTabs`.

### 5.9 Stepper (`components/ui/stepper.tsx`)

Multi-step flows (onboarding, imports). Never roll raw step dots.

### 5.10 Progress (`components/ui/progress.tsx`)

`ProgressBar` for task completion. `BarMeter` (new) for labelled horizontal data bars (revenue, usage, capacity).

### 5.11 Skeleton (`components/ui/skeleton.tsx`)

`Skeleton`, `CardSkeleton`, `PageSkeleton`. Drive from `AsyncStateWrapper`.

### 5.12 Form controls (`components/ui/form-controls.tsx`)

`Input`, `Textarea`, `Select`, `Checkbox`, `RadioGroup`. **All form controls inside admin or learner pages MUST use these primitives.** Raw `<input>`/`<select>`/`<textarea>` are banned.

### 5.13 Switch (`components/ui/switch.tsx`)

`<Switch label="…" checked onCheckedChange />` — accessible toggle for all boolean admin settings. Replaces ad-hoc `<input type="checkbox">` or ToggleLeft/Right icons.

### 5.14 SegmentedControl (`components/ui/segmented-control.tsx`)

`<SegmentedControl value onChange options={[{value,label}…]} />` — animated pill group for time-range / type filters. Replaces all raw `<button>` filter rows.

### 5.15 StickyActionBar (`components/ui/sticky-action-bar.tsx`)

Bottom-pinned save/cancel footer, bottom-nav-aware (`sticky bottom-[calc(var(--bottom-nav-height)+env(safe-area-inset-bottom))]`). Use for any edit page with batched changes (roles, free-tier, content editor).

### 5.16 BarMeter (`components/ui/bar-meter.tsx`)

Labelled horizontal bar, optional stacked series, optional trailing value. Replaces raw `<div class="h-4 bg-muted"><div class="bg-primary"/></div>`.

### 5.17 CommandPalette (`components/ui/command-palette.tsx`)

⌘K / Ctrl+K overlay. Registers in admin layout. Fuzzy filter of all admin sections. Recent-items persistence in `localStorage` under `admin.command-palette.recents`.

### 5.18 Breadcrumbs (`components/ui/breadcrumbs.tsx`)

Chevron-separated trail. Last item non-clickable. Used in admin TopNav for nested pages.

---

## 6. Layout Primitives

### 6.1 Shell

| Shell | File | Composition |
| ----- | ---- | ----------- |
| `AppShell` | `components/layout/app-shell.tsx` | `TopNav` + `Sidebar` + `BottomNav` + ambient backdrop |
| `LearnerDashboardShell` | `components/layout/learner-dashboard-shell.tsx` | Wraps `AppShell` with learner nav + `LearnerWorkspaceContainer maxWidth="learner"` |
| `AdminDashboardShell` | `components/layout/admin-dashboard-shell.tsx` | Wraps `AppShell` with admin sectioned nav + `LearnerWorkspaceContainer maxWidth="admin"` + `CommandPalette` |
| `ExpertDashboardShell` | `components/layout/expert-dashboard-shell.tsx` | Expert workspace |
| `SponsorDashboardShell` | `components/layout/sponsor-dashboard-shell.tsx` | Sponsor workspace |

All shells share the same ambient blooms, typography, and motion. Only the sidebar navigation + optional chrome differ.

### 6.2 Sidebar sections (admin)

Admin has 27 nav items. They are grouped into 5 collapsible sections via `sectionedItems` on `Sidebar`:

1. **Operations** — Dashboard, Review Ops, Notifications, Escalations
2. **Content** — Library, Papers, Taxonomy, Rubrics, Hierarchy, Import, Media, Generation, Freeze, Dedup, Marketplace Review, Publish Requests
3. **Governance** — AI Config, AI Usage, Quality Analytics, Community, Permissions, Audit Logs
4. **People & Billing** — Users, Experts, Billing, Private Speaking
5. **System** — Feature Flags, Webhooks

Section collapse state persists in `localStorage` under `admin.sidebar.collapsed-sections`.

### 6.3 Workspace container

| Variant | Max width | Use |
| ------- | --------- | --- |
| `maxWidth="learner"` (default) | `max-w-[1200px]` | All learner pages |
| `maxWidth="admin"` | `max-w-[1440px]` | Admin console (more room for data tables) |
| `maxWidth="full"` | full-width | Rare — content editor |

---

## 7. Hero Pattern

`LearnerPageHero` (the shared primitive). Admin re-exports it via `AdminRouteHero`.

```
<AdminRouteHero
  eyebrow="Operational Control"
  icon={Sparkles}
  accent="navy"
  title="Platform health, review risk, and rollout in one place"
  description="Start from the highest-signal summaries…"
  highlights={[
    { icon: Inbox, label: 'Review backlog', value: '1 at risk' },
    { icon: CreditCard, label: 'Billing risk', value: '0 failed' },
    { icon: BarChart3, label: 'Agreement', value: '83.3%' },
  ]}
  aside={<QuickActionsBlock />}
/>
```

- **Outer:** `rounded-[20px] sm:rounded-[24px] border border-border bg-surface px-4 py-4 sm:px-6 sm:py-6 shadow-sm`.
- **Icon tile:** `h-10 w-10 sm:h-12 sm:w-12 rounded-xl sm:rounded-2xl` with accent palette.
- **Highlight chips:** `rounded-2xl border border-border bg-background-light px-3 py-2`. Icon tile `h-8 w-8 rounded-xl`. Max 3 highlights (sanitized).
- **Accent map:** `primary | navy | amber | blue | indigo | purple | rose | emerald | slate`.
- **Aside:** right column, `shrink-0 lg:max-w-sm`.

Every page's first block is a hero. Sub-pages may use `AdminRouteSectionHeader` (a lighter title row) but most should still use the hero for consistency.

---

## 8. Surface Card Pattern

`LearnerSurfaceCard` (learner) / `AdminRouteSummaryCard` (admin KPI) — same contract. Structure:

1. Eyebrow badge (tinted pill with icon + uppercase label)
2. Status badge (optional, right-aligned `<Badge variant="muted">`)
3. Title (`text-xl font-bold text-navy`, mt-4)
4. Description (`text-sm leading-relaxed text-muted`, mt-2)
5. Meta row (up to 3 icon+label items)
6. Children slot
7. Footer: custom → primary action (full-width) → secondary action (full-width), gap-3 stack

Always use the shared primitive. Never hand-roll card inner layout.

---

## 9. Section Headers

`LearnerSurfaceSectionHeader` — eyebrow + title + description + right-action.

```
<LearnerSurfaceSectionHeader
  eyebrow="Today"
  title="Today's Study Plan"
  description="2 of 4 scheduled tasks completed."
  action={<Button variant="ghost" size="sm">View Full Plan <ArrowRight/></Button>}
/>
```

Layout: `flex flex-col sm:flex-row sm:items-end justify-between gap-4`.

Admin variant: `AdminRouteSectionHeader` adds `accent`, `highlights`, `meta`, `actions` slots (delegates to hero).

---

## 10. Data Patterns

### 10.1 Table

```
<DataTable
  data={rows}
  columns={columns}
  density="compact"           // admin
  mobileCardRender={…}
  onRowClick={…}
/>
```

- Desktop wrapper: `rounded-[24px] border border-gray-200 bg-surface shadow-sm`.
- Header: `bg-background-light`, uppercase micro-caps, `tracking-[0.16em]`.
- Rows: `divide-y divide-gray-100/90`. Clickable rows: `hover:bg-primary/[0.03]`.
- Cell text: `text-sm text-navy`.
- Table-cell links: `<AdminTableCellLink href>Label</AdminTableCellLink>` — **never** raw `<button className="text-primary hover:underline">`.

### 10.2 Filters

- Desktop: `FilterBar` with popover groups.
- Mobile: auto-swaps to `MobileFilterSheet` (drawer).
- Time / segmented filters: `SegmentedControl`, not `FilterBar`.

### 10.3 Pagination

Below the table:

```
<div className="mt-4 flex items-center justify-between text-sm text-muted">
  <span>Showing {from}–{to} of {total}</span>
  <div className="flex items-center gap-2">
    <Button variant="outline" size="sm" disabled={page===0} onClick={prev}>Previous</Button>
    <Button variant="outline" size="sm" disabled={isLast} onClick={next}>Next</Button>
  </div>
</div>
```

---

## 11. State Patterns

### 11.1 AsyncStateWrapper

```
<AsyncStateWrapper status={status} onRetry={reload} emptyContent={…}>
  {children}
</AsyncStateWrapper>
```

Status = `'loading' | 'error' | 'empty' | 'partial' | 'success'`.

### 11.2 Skeleton

- `<Skeleton />` for inline placeholders.
- `<CardSkeleton />` for single-card loading.
- `<PageSkeleton />` (default loading fallback in `AsyncStateWrapper`) for full-page.

### 11.3 Empty / Error

- `<EmptyState icon={Icon} title description action />` for "no data yet" states.
- `<ErrorState title description onRetry />` for fetch failures.
- `<InlineAlert variant="warning|info|success|error" title>…</InlineAlert>` for dismissable banners.

Never render a blank surface without a next-action prompt.

---

## 12. Motion Presets

Canonical source: `lib/motion.ts`.

| Surface | Use | Spring |
| ------- | --- | ------ |
| `route` | Page transitions | stiffness 360, damping 34 |
| `section` | Section reveals | 420 / 38 |
| `list` | Row collections | item-default |
| `item` | Cards, rows | 520 / 42 |
| `overlay` | Modal, Drawer, BottomNav | 300 / 30 |
| `state` | AsyncStateWrapper cross-fade | short |
| `skeleton` | Pulse cycle | opacity |

Stagger: `getMotionDelay(idx)` → `min(idx × 0.04, 0.18)`.

Microinteractions: `getMicroHover` (scale 1.02 spring) on Button + CardLink; `getMicroTap` (scale 0.97).

Reduced motion: `prefersReducedMotion()` wraps `useReducedMotion()`. When true, all surface motion falls back to opacity-only; CSS `@media (prefers-reduced-motion)` in `globals.css` disables hover transforms and `page-enter` animation.

Shared layout pills: use `getSharedLayoutId(namespace, variant)` — never hand-roll `layoutId` strings.

---

## 13. Responsive Breakpoints

Tailwind defaults (never change): `sm:640`, `md:768`, `lg:1024`, `xl:1280`, `2xl:1536`.

| Pattern | Use |
| ------- | --- |
| Hero internal | `flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between` |
| Section header | `flex flex-col sm:flex-row sm:items-end justify-between gap-4` |
| DataTable | mobile cards `md:hidden`, table `hidden md:block` |
| FilterBar | desktop `hidden md:flex`, mobile sheet `md:hidden` |
| Sidebar | `hidden lg:flex` (w-72 desktop only) |
| BottomNav | `lg:hidden` |
| TopNav title | `hidden sm:flex` |

Mobile touch targets: minimum 44px. Use `.touch-target` or the Button `min-h-11` default.

---

## 14. Dark Mode

- Tokens auto-flip via `:root.dark` override in `globals.css`.
- Components rely on token classes (`bg-surface`, `text-navy`, `border-border`) — these flip automatically.
- Explicit `dark:` variants only for:
  - Gray raw classes (skeletons): `dark:bg-gray-700/50`
  - White opacity hovers: `dark:hover:bg-white/5`
  - Orange/amber fixed chips: `dark:bg-orange-600 dark:text-white`
- Glass panel has `:root.dark .glass-panel` CSS override.
- Admin console must not introduce raw tone classes without a `dark:` counterpart.

---

## 15. Admin Parity Rules (mandatory)

The admin console must feel like the learner dashboard. The following rules are enforced:

### 15.1 Shell & chrome

- Every admin page routes through `AdminDashboardShell` (from `app/admin/layout.tsx`).
- Never use `LearnerDashboardShell` in an admin page.
- Workspace container: `maxWidth="admin"` (`max-w-[1440px]`).
- Admin layout mounts `CommandPalette` globally.
- `TopNav` receives `breadcrumbs` from pathname.

### 15.2 Page structure

Every admin page body MUST open with:

```
<AdminRouteWorkspace>
  <AdminRouteHero … /> {/* or AdminRouteSectionHeader */}
  … panels …
</AdminRouteWorkspace>
```

### 15.3 Primitives (required)

- `AdminRouteWorkspace` — root spacing rhythm.
- `AdminRouteHero` — page opener.
- `AdminRouteSectionHeader` — sub-page title row.
- `AdminRouteSummaryCard` — single KPI.
- `AdminRouteStatRow` — 2-5 labelled figures inside a panel.
- `AdminRoutePanel` — content section card with title + actions.
- `AdminRoutePanelFooter` — standardized `Updated · Window · Source` micro-footer.
- `AdminRouteFreshnessBadge` — compact `Updated …` label.
- `AdminRouteTabs` — tab switcher (wraps `Tabs`).
- `AdminTableCellLink` — table-cell hyperlink.
- `AdminRouteBreadcrumbs` — path trail.

### 15.4 Banned patterns

- `<div className="min-h-screen bg-background-light">…</div>` as outer frame — let the shell handle it.
- Raw `<input>` / `<select>` / `<textarea>` — use `form-controls`.
- Raw `<button className="…bg-primary">` — use `Button`.
- Raw `<table>` — use `DataTable`.
- Raw `text-red-600`, `text-green-500`, `bg-green-600`, `bg-red-600`, `text-purple-500`, `bg-emerald-50` (except inside `Badge`/`InlineAlert` internals) — use tokens or `tone` props.
- Raw `<button>` filter pills — use `SegmentedControl`.
- Custom toggles (`ToggleLeft/ToggleRight`, raw `<input type="checkbox">` for settings) — use `Switch`.
- Bespoke bar charts `<div class="bg-muted"><div class="bg-primary"/></div>` — use `BarMeter`.
- Custom empty states with `border-dashed` — use `EmptyState`.
- Custom sticky footers — use `StickyActionBar`.
- `<Badge variant="destructive">` — `danger`.
- `<Button variant="default">` — `primary`.

### 15.5 Freshness / source footer

Every admin panel backed by external data uses:

```
<AdminRoutePanelFooter
  updatedAt={data.generatedAt}
  window="30d"
  source="Analytics pipeline"
/>
```

### 15.6 Density

Admin data-heavy tables (users, audit-logs, ai-usage rows, billing invoices, content library) use `<DataTable density="compact" />`. All other admin tables use default density.

---

## 16. Command Palette

- Trigger: ⌘K on macOS, Ctrl+K on Windows/Linux, `/` from any input-free context.
- Registered globally by `AdminDashboardShell` via `<CommandPalette />`.
- Built on `Modal` + `Input`.
- Commands: all 27 admin routes + common actions (sign out, toggle theme).
- Fuzzy-filters on label, section, keywords.
- Arrow keys + Enter activation.
- Recent items persist in `localStorage`.

---

## 17. Do's and Don'ts

**Do**

- Use the shared primitives for every page, always.
- Keep `bg-primary` for CTAs, `text-navy` for headings, `text-muted` for metadata.
- Start every page with a hero or section header.
- Render explicit empty states when data is missing.
- Respect `space-y-6` at the top level.
- Use `Badge` / `InlineAlert` variants instead of hand-rolled coloured blocks.
- Wrap async pages in `AsyncStateWrapper`.

**Don't**

- Don't introduce new colour palettes or shadow layers.
- Don't use `text-3xl`+ inline unless it is a KPI figure.
- Don't add `min-h-screen` wrappers inside the shell.
- Don't hand-roll tab strips, filter pills, or switches.
- Don't leave charts or panels empty without explanation.
- Don't mix `LearnerDashboardShell` into admin pages.

---

## 18. Agent Prompt Guide

When an AI agent is asked to build or edit a page:

> Build this page in the OET Prep design system. Use `AdminRouteWorkspace` (admin) or `LearnerDashboardShell` body (learner). Start with a hero. All controls must come from `components/ui/*` (Button, Badge, Card, FormControls, Switch, SegmentedControl, DataTable, FilterBar, Alert, Modal, Tabs, Stepper, Progress, Skeleton). Use tokens (`bg-primary`, `text-navy`, `text-muted`, `bg-surface`, `border-border`). Motion via `motion-primitives`. State via `AsyncStateWrapper`. No raw `<input>`, `<select>`, `<table>`, `<button>`, `min-h-screen`, `bg-gray-*`, `text-red-600`, `bg-green-*`.

---

## Appendix A — Common page recipe (admin)

```tsx
export default function MyAdminPage() {
  const { status, data, reload } = useMyData();
  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        eyebrow="Section"
        icon={IconName}
        accent="navy"
        title="Page title"
        description="One-sentence summary."
        highlights={[{ icon, label, value }, …]}
      />

      <AsyncStateWrapper status={status} onRetry={reload}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <AdminRouteSummaryCard label="…" value={…} hint="…" icon={<Icon className="h-5 w-5" />} tone="default" />
          … 4 total
        </div>

        <div className="grid gap-6 lg:grid-cols-2">
          <AdminRoutePanel eyebrow="Foo" title="Foo" description="…">
            <AdminRouteStatRow items={[…]} />
            <AdminRoutePanelFooter updatedAt={data.generatedAt} window="30d" source="Events" />
          </AdminRoutePanel>
          …
        </div>

        <AdminRoutePanel eyebrow="Records" title="Records">
          <FilterBar groups={…} />
          <DataTable density="compact" data={…} columns={…} mobileCardRender={…} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
```

This is the canonical admin page shape.
