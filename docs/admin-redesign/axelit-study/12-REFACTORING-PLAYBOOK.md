# REFACTORING-PLAYBOOK.md — Phase 6 deliverable

How to apply the extracted DNA to the existing OET admin panel. Concrete, sequenced, file-by-file.

---

## 0 · Pre-flight — read the current state

Before touching anything, the implementing agent must read:
1. [AGENTS.md](../../../AGENTS.md) — full project doc (skip to §"Skill Routing" + §"Project Structure")
2. [app/admin/layout.tsx](../../../app/admin/layout.tsx) — current admin shell (513 lines)
3. [app/admin/page.tsx](../../../app/admin/page.tsx) — current dashboard (235 lines)
4. [components/layout/sidebar.tsx](../../../components/layout/sidebar.tsx) — current sidebar
5. [components/domain/admin/](../../../components/domain/admin/) — domain components (READ-ONLY scan)
6. [components/admin/ui/](../../../components/admin/ui/) — `Panel`, `PulseTile`, `DenseTable`, `StatusBadge`, `MetricGrid2x2`

OET's admin already has **better** primitives than Axelit (PulseTile, DenseTable). The rebuild is *re-skinning + filling gaps*, not starting from zero.

---

## 1 · Migration strategy — three waves

### Wave 1 — Token system (foundation, ~1 day)

Goal: replace ad-hoc Tailwind classes with a coherent token system inspired by Axelit's structure, with **OET-appropriate hues**.

**Files to create**:
- `app/admin/_design/tokens.css` — the OET admin token sheet (NEW)
- `app/admin/_design/tokens.json` — DTCG format mirror for tools (NEW)

**Files to edit**:
- `app/globals.css` — import `_design/tokens.css` in admin context only (`.admin-compact-theme`)
- `tailwind.config.ts` — extend theme with token references

**Token sheet structure** (see [`01-design.md`](01-design.md) for full draft):
- 8 brand role colors + 8 dark variants (renamed for OET)
- 5 surface tokens
- 4 text tokens
- 2 border tokens
- 5-step radius scale (`4 / 8 / 12 / 16 / 999px pill`)
- 3-step shadow scale (`ambient / hover / sticky`)
- 5-step type scale (modular `1.25×`)
- 5-step spacing scale (`4 / 8 / 12 / 16 / 24px`)
- Motion duration + easing tokens

**Validation**: run `npx tsc --noEmit` + verify no visual regression on `/admin` route via Playwright screenshot diff.

### Wave 2 — Component re-skin (~3 days)

Goal: bring existing OET admin components (`Panel`, `PulseTile`, `DenseTable`, `StatusBadge`) into alignment with the token system. Build any missing components.

**Component-by-component map**:

| OET file | Axelit pattern to mirror | Action |
| -------- | ------------------------ | ------ |
| [components/admin/ui/panel.tsx](../../../components/admin/ui/panel.tsx) | Card with H5 title + body | Already aligned — verify radius matches `--radius-card` |
| [components/admin/ui/pulse-tile.tsx](../../../components/admin/ui/pulse-tile.tsx) | KPI tile (orders-provided-card pattern) | Verify number is largest type, label is muted micro |
| [components/admin/ui/dense-table.tsx](../../../components/admin/ui/dense-table.tsx) | Project Status table | Add: row hover (3.5% darken), sortable headers, sticky thead |
| [components/admin/ui/status-badge.tsx](../../../components/admin/ui/status-badge.tsx) | Bootstrap `.badge bg-*` + `.bg-light-*` | Add: `intensity` prop (`solid` \| `tinted`) |
| [components/admin/ui/metric-grid-2x2.tsx](../../../components/admin/ui/metric-grid-2x2.tsx) | 2×2 mini-stats inside a Panel | Verify gap matches `--space-sm` (8px) |
| `Button` (NEW or shadcn) | btn-primary / btn-outline / ghost | Pull from shadcn-ui or write minimal |
| `Modal` / `Dialog` (Radix) | Bootstrap modal | Use Radix Dialog primitive |
| `Drawer` (Vaul) | Bootstrap offcanvas | Use vaul or Radix Dialog with side variant |
| `Tabs` (Radix) | Bootstrap nav-tabs | Use shadcn Tabs |
| `Tooltip` (Radix) | Bootstrap tooltip | Use Radix Tooltip |
| `Toast` (Sonner) | Bootstrap toast | Use sonner |
| `Avatar` / `AvatarGroup` (Radix) | `<img>` + overlap CSS | Use Radix Avatar + custom group |
| `Combobox` (shadcn) | Bootstrap select | Use cmdk-based combobox |

**Validation per component**: write a Storybook story (or `.preview.tsx` per Hallmark's 8-state demo pattern) showing all 8 states. Capture screenshot of the preview page for design-team review.

### Wave 3 — Layout shell rebuild (~2 days)

Goal: replace `AdminDashboardShell` with a new shell aligned to Axelit's pattern but using OET's `next-best-practices` (RSC where possible, motion v12, Tailwind v4).

**New files**:
- `components/layout/admin-shell.tsx` (replaces `AdminDashboardShell`)
- `components/layout/admin-sidebar.tsx` (replaces current `Sidebar`)
- `components/layout/admin-header.tsx` (NEW — currently inlined in shell)
- `components/layout/admin-footer.tsx` (NEW)
- `components/layout/admin-page-header.tsx` (NEW — title + breadcrumb + page-level CTA)

**Behavior contract**:
- Sidebar: fixed-left, 272px expanded / 72px collapsed via toggle button. Mobile: hidden, opens as drawer.
- Header: 80px tall, fixed-top, transparent over body. Contains: hamburger (mobile only), breadcrumb, right-aligned action strip (search, notifications, theme toggle, avatar menu).
- Main: padded 32px top, max-width 1440px or full-width depending on page.
- Footer: copyright + version + help — small, muted.

**Migration order** (do not refactor in-place — feature-flag the new shell):
1. Create new shell at `components/layout/admin-shell-v2.tsx`
2. Add env flag `NEXT_PUBLIC_ADMIN_SHELL_V2=true`
3. In `app/admin/layout.tsx`, conditionally render v1 or v2 based on flag
4. Test on staging with flag enabled
5. Once verified, flip default, delete v1, remove flag

---

## 2 · Component standardization strategy

### Drop entirely (template bloat for OET)
- `Tawk.to` chat widget injection
- `customizer-btn` and customizer offcanvas panel
- Welcome modal on page-load
- Tabler Icons CDN load
- Phosphor Icons CDN loads (all 6 weights)
- DataTables.net + jQuery
- Sweet Alert (use Radix AlertDialog instead)
- FilePond (use react-dropzone or uploadthing)
- animate.css (use motion v12)
- All `--dt-*` design tokens
- All `--*-gradient` tokens except possibly `--primary-gradient` for auth hero only
- Social color tokens (`--facebook`, `--twitter`, etc.)

### Keep and align (token system mappings)
- 8-role color system → remapped to OET-appropriate hues
- 5 surface bg tokens → keep
- 4 text tokens → keep
- 2 border tokens → keep
- 3 shadow tokens → keep
- Type scale → re-pitched to 1.25× modular

### Build new (gaps Axelit doesn't address)
- Empty-state component (universal pattern)
- Skeleton component (per shadcn)
- Command palette (Cmdk) — admin users will love it
- Audit log viewer specialized component
- Bulk action toolbar (transforms table header when rows selected)
- Density toggle per role (`compact / comfortable / spacious`)

---

## 3 · Token standardization strategy

### Naming convention
Use `--admin-*` prefix for ALL admin tokens (no Bootstrap leakage). Example:

```css
.admin-compact-theme {
  /* Role colors — REMAP THESE TO OET BRAND */
  --admin-primary:       /* OET clinical blue */;
  --admin-primary-fg:    /* white */;
  --admin-primary-tint:  /* primary @ 12% alpha for soft backgrounds */;

  --admin-success:       /* clinical green */;
  --admin-warning:       /* clinical amber */;
  --admin-danger:        /* clinical red */;
  --admin-info:          /* clinical info blue */;

  /* Surfaces */
  --admin-bg-page:       /* outer body */;
  --admin-bg-surface:    /* card body */;
  --admin-bg-elevated:   /* modal, drawer */;
  --admin-bg-subtle:     /* table header strip */;

  /* Text */
  --admin-fg-default:    /* body text */;
  --admin-fg-strong:     /* headings */;
  --admin-fg-muted:      /* helper text */;
  --admin-fg-inverse:    /* white-on-dark */;

  /* Borders */
  --admin-border-default;
  --admin-border-strong;

  /* Radii */
  --admin-radius-sm: 4px;
  --admin-radius-md: 8px;
  --admin-radius-lg: 12px;     /* primary card radius — NOT 28.8px */
  --admin-radius-xl: 16px;
  --admin-radius-pill: 9999px;

  /* Shadows */
  --admin-shadow-ambient;
  --admin-shadow-hover;
  --admin-shadow-sticky;

  /* Type scale (1.25× modular) */
  --admin-text-xs:    11.2px;
  --admin-text-sm:    12.5px;
  --admin-text-base:  14px;
  --admin-text-lg:    17.5px;
  --admin-text-xl:    21.9px;
  --admin-text-2xl:   27.3px;
  --admin-text-3xl:   34.2px;

  /* Spacing (4px grid) */
  --admin-space-1: 4px;
  --admin-space-2: 8px;
  --admin-space-3: 12px;
  --admin-space-4: 16px;
  --admin-space-5: 20px;
  --admin-space-6: 24px;
  --admin-space-8: 32px;

  /* Motion */
  --admin-dur-fast: 100ms;
  --admin-dur-base: 200ms;
  --admin-dur-slow: 300ms;
  --admin-ease-out: cubic-bezier(0.16, 1, 0.3, 1);
}

[data-bs-theme="dark"] .admin-compact-theme {
  /* Dark-mode overrides */
  --admin-bg-page:       /* deep neutral 950 */;
  --admin-bg-surface:    /* neutral 900 */;
  /* Brand colors UNCHANGED across themes */
  --admin-fg-default:    /* neutral 100 */;
  --admin-fg-strong:     /* white */;
  --admin-fg-muted:      /* neutral 400 */;
  --admin-border-default;
}
```

### Tailwind integration

```ts
// tailwind.config.ts (additions)
export default {
  theme: {
    extend: {
      colors: {
        'admin-bg': 'var(--admin-bg-page)',
        'admin-surface': 'var(--admin-bg-surface)',
        'admin-elevated': 'var(--admin-bg-elevated)',
        'admin-fg': 'var(--admin-fg-default)',
        'admin-fg-strong': 'var(--admin-fg-strong)',
        'admin-fg-muted': 'var(--admin-fg-muted)',
        'admin-border': 'var(--admin-border-default)',
        'admin-primary': 'var(--admin-primary)',
        // …
      },
      borderRadius: {
        'admin-sm': 'var(--admin-radius-sm)',
        'admin': 'var(--admin-radius-lg)',
        'admin-xl': 'var(--admin-radius-xl)',
      },
      // …
    },
  },
};
```

Then in components: `<div className="bg-admin-surface text-admin-fg rounded-admin shadow-admin-ambient">` instead of `bg-white text-slate-900 rounded-2xl shadow-md`.

---

## 4 · Layout standardization strategy

Codify Axelit's 4 universal templates as React layout primitives:

### `<AdminOperationsLayout>` — Template A
```tsx
<AdminPage title="Operations" breadcrumb={['Admin', 'Operations']}>
  <KpiStrip>
    <PulseTile label="Backlog" value={…} />
    <PulseTile label="Overdue" value={…} />
    {/* 4 tiles */}
  </KpiStrip>

  <BentoGrid>
    <BentoCell span={{ default: 12, lg: 8 }}>
      <Panel title="Quality Trend"><Chart /></Panel>
    </BentoCell>
    <BentoCell span={{ default: 12, lg: 4 }}>
      <Panel title="Live Flags"><MetricGrid2x2 /></Panel>
    </BentoCell>
  </BentoGrid>

  <Panel title="Review Operations" href="/admin/review-ops">
    <DenseTable {…} />
  </Panel>
</AdminPage>
```

### `<AdminCatalogLayout>` — Template B
```tsx
<AdminPage title="Content Papers" actions={<Button>Create</Button>}>
  <FilterBar
    search={…}
    filters={[…]}
    viewMode="grid"
  />
  <ItemGrid>
    {papers.map(p => <PaperCard key={p.id} paper={p} />)}
  </ItemGrid>
  <Pagination total={total} current={page} />
</AdminPage>
```

### `<AdminTableLayout>` — Template C
```tsx
<AdminPage title="Users" breadcrumb={['Admin', 'People', 'Users']}>
  <Panel>
    <PanelToolbar
      search={…}
      filters={…}
      bulkActions={selectedIds.length > 0}
      actions={<Button>Invite User</Button>}
    />
    <DataTable
      columns={…}
      data={…}
      sortable
      selectable
    />
    <Pagination />
  </Panel>
</AdminPage>
```

### `<AdminSettingsLayout>` — Template D
```tsx
<AdminPage title="Settings">
  <Tabs defaultValue="general">
    <TabsList>
      <TabsTrigger value="general">General</TabsTrigger>
      <TabsTrigger value="brevo">Brevo</TabsTrigger>
      {/* … */}
    </TabsList>
    <TabsContent value="general">
      <SettingsForm sections={…} />
    </TabsContent>
  </Tabs>
</AdminPage>
```

---

## 5 · Responsive standardization strategy

- **Sidebar collapse breakpoint**: 1024px (`lg`) — sidebar hidden below this
- **Mobile drawer**: opens from left edge, 280px wide, scroll-locks body
- **Touch targets**: minimum 44×44px for any interactive element on touch devices
- **Tables on mobile**: convert to card-per-row layout for primary entity tables; preserve `overflow-x: auto` for read-only data dumps
- **Modals on mobile**: full-screen (`.modal-fullscreen-sm-down` equivalent)
- **Header on mobile**: 56px tall (smaller than desktop 80px), shows hamburger + page title + 1-2 critical actions max

---

## 6 · Dashboard consistency strategy

Apply these rules universally across all 49 OET admin routes:

1. **Every page MUST have `<AdminPage title=… breadcrumb=…>`** at the top.
2. **Every page MUST emit a layout template** (A/B/C/D above). No bespoke layouts.
3. **Every interactive element MUST hit all 8 Hallmark states** (default, hover, focus-visible, active, disabled, loading, error, success).
4. **Every data-display MUST have an empty state** keyed to the underlying data source.
5. **Every long-running action MUST show a skeleton or progress**.
6. **Every destructive action MUST require confirmation** (AlertDialog with explicit "Yes, delete X" copy).
7. **Every page MUST render correctly at 320 / 768 / 1024 / 1920 widths** (verified via Playwright snapshot tests).
8. **Every page MUST pass axe-core accessibility scan** (no critical issues).

---

## 7 · Sequenced refactor plan (~10 day estimate)

| Day | Work | Validation |
| --- | ---- | ---------- |
| 1 | Wave 1 — token system | tsc + visual diff on `/admin` |
| 2-3 | Wave 2 — re-skin Panel, PulseTile, DenseTable, StatusBadge, MetricGrid2x2 | Storybook stories for each component, screenshot review |
| 4 | Wave 2 — Button, Modal, Drawer, Tabs, Tooltip, Toast | Same |
| 5 | Wave 2 — Avatar, AvatarGroup, Combobox, Skeleton, EmptyState, ProgressBar | Same |
| 6-7 | Wave 3 — new admin shell behind feature flag | Manual QA + Playwright e2e |
| 8 | Apply layout templates to top-10 admin routes (most-visited) | Visual regression + Lighthouse |
| 9 | Apply to remaining ~39 routes | Same |
| 10 | Flip feature flag, delete old shell, ship | Final QA + dark mode verification |

---

## 8 · Risks + mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Visual regression on production admin during migration | Feature-flag the whole rebuild; QA on staging; flip flag per route incrementally |
| Performance regression (more components, more CSS) | Monitor Core Web Vitals via `chrome-devtools-mcp:debug-optimize-lcp` skill; Tailwind purge ensures dead CSS doesn't ship |
| OET-specific business logic accidentally broken during component refactor | All domain components in `components/domain/admin/` stay untouched; only `components/admin/ui/` primitives change |
| Designer revisits Axelit, wants pixel-faithful copy | Decline — per Hallmark §Theme drift is allowed (DNA travels, dress doesn't); show 11-CONFIDENCE-GAP-ANALYSIS to demonstrate forensic depth |
| Hidden ThemeForest license violation (using their CSS verbatim) | We're NOT copying CSS — we're extracting DNA (patterns, ratios, archetypes). All shipped code will be original OET CSS using OET tokens. Provenance documented in `01-design.md`. |

---

## 9 · What to do FIRST (Monday morning checklist)

1. **Read this entire study folder** — all 13 files (`00-…` through `12-…`).
2. **Compare side-by-side**: open `screenshots/axelit-01-dashboard-project-full.png` next to the current OET `/admin` route screenshot. List the visual deltas.
3. **Pick the brand colors** — meet with OET design (or decide solo): what's OET's `--primary`? `--success`? `--danger`? **Don't proceed to Wave 1 without these answers.**
4. **Decide font** — Montserrat (admin-only) or Geist (full-app consistency)? **Don't proceed without answer.**
5. **Define the primary radius** — 12px / 16px / 20px / 28.8px? Recommend 12px for OET's clinical voice.
6. **Get sign-off on the 4 layout templates** (A/B/C/D from §4).
7. **Start Wave 1** — create `app/admin/_design/tokens.css` with the agreed values.

---

## 10 · Acceptance criteria for "rebuild complete"

The OET admin rebuild is complete when:

- [ ] All 49 admin routes render using one of 4 layout templates (no bespoke layouts).
- [ ] All UI tokens are referenced by name (no inline hex/rgb/rem values in component code).
- [ ] All components pass the 8-state Hallmark contract.
- [ ] Dark mode parity verified at every route.
- [ ] Mobile responsive verified at 320 / 414 / 768 / 1024 / 1920.
- [ ] Lighthouse Performance ≥ 90 and Accessibility = 100 on a representative dashboard page.
- [ ] Axe-core scan returns zero critical issues across top-10 routes.
- [ ] All admin routes load in < 2s on cable connection (Lighthouse LCP < 2.5s).
- [ ] No DataTables / jQuery / Bootstrap JS / Tawk.to / animate.css / Tabler / Phosphor in the admin bundle.
- [ ] The Hallmark `audit` verb (run via the installed skill) reports `0 critical, 0 major` findings on the dashboard page.
