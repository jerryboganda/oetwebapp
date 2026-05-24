# Axelit — Route Inventory (Phase 1 deliverable)

**Source**: `https://axelit-next.vercel.app` — Axelit Next.js Admin Dashboard, ThemeForest item by **la-themes / teqlathemes** (confirmed via Customizer's "Buy Now" link → `themeforest.net/user/la-themes/portfolio`).
**Extraction date**: 2026-05-24
**Method**: Playwright (real Chromium browser) — accessibility snapshots + screenshots + computed CSS evaluation + DOM probe.
**Provenance**: Public reference for the OET admin panel rebuild (Hallmark study.md §Refusal — attestation (b)). The DNA is structural; OET-specific tokens MUST be regenerated.

---

## 1 — Routes captured in this study pass (10 pages, full-page screenshots)

| # | URL | Page Title | Archetype | Screenshot |
| - | --- | ---------- | --------- | ---------- |
| 01 | `/dashboard/project` | Project Dashboard | Multi-card KPI + table + activity dashboard | [`axelit-01-dashboard-project-full.png`](screenshots/axelit-01-dashboard-project-full.png) |
| 02 | `/dashboard/project` | (same, dark mode attempt) | Dark mode capture failed — see Phase 5 gap | [`axelit-02-dashboard-project-DARK.png`](screenshots/axelit-02-dashboard-project-DARK.png) |
| 03 | `/dashboard/ecommerce` | E-commerce Dashboard | Stat-led KPIs + Apex charts + order tables + product catalogue | [`axelit-03-dashboard-ecommerce.png`](screenshots/axelit-03-dashboard-ecommerce.png) |
| 04 | `/apps/projects-page/projects` | Projects | Project list (grid of project cards) | [`axelit-04-projects-list.png`](screenshots/axelit-04-projects-list.png) |
| 05 | `/apps/kanban-board` | Kanban Board | Drag-and-drop column board | [`axelit-05-kanban.png`](screenshots/axelit-05-kanban.png) |
| 06 | `/apps/e-shop/orders-list` | Orders List | DataTables-backed bulk table | [`axelit-06-orders-list-datatable.png`](screenshots/axelit-06-orders-list-datatable.png) |
| 07 | `/apps/setting` | Settings | Settings tabs + forms | [`axelit-07-settings.png`](screenshots/axelit-07-settings.png) |
| 08 | `/ui-kit/buttons` | Buttons gallery | UI Kit reference — every button variant | [`axelit-08-buttons-gallery.png`](screenshots/axelit-08-buttons-gallery.png) |
| 09 | `/ui-kit/cards` | Cards gallery | UI Kit reference — every card variant | [`axelit-09-cards-gallery.png`](screenshots/axelit-09-cards-gallery.png) |
| 10 | `/dashboard/project` (390×844) | Mobile capture | Same dashboard, iPhone 14 viewport | [`axelit-10-dashboard-MOBILE-iPhone14.png`](screenshots/axelit-10-dashboard-MOBILE-iPhone14.png) |

## 2 — Full route catalog discovered via sidebar (~80 named, ~300+ implied)

Grouped exactly as the live sidebar renders them:

### Dashboard (badge `4`)
- `/dashboard/project` — Project (the captured page)
- `/dashboard/ecommerce` — Ecommerce
- *(badge `4` implies 4 dashboard variants — only 2 visible in nav; other 2 likely behind ## paywall or removed in demo)*

### Apps (~30 routes)
- `/apps/calendar` · `/apps/profile` · `/apps/setting`
- `/apps/projects-page/projects` · `/apps/projects-page/projects-details`
- `/apps/todo` · `/apps/team` · `/apps/api`
- `/apps/ticket-page/ticket` · `/apps/ticket-page/ticket-details`
- `/apps/email-page/email` · `/apps/email-page/read-email`
- `/apps/e-shop/cart` · `/apps/e-shop/product` · `/apps/e-shop/add-product` · `/apps/e-shop/product-details` · `/apps/e-shop/product-list`
- `/apps/e-shop/orders` · `/apps/e-shop/orders-details` · `/apps/e-shop/orders-list`
- `/apps/e-shop/checkout` · `/apps/e-shop/wishlist`
- `/apps/invoice` · `/apps/chat` · `/apps/file-manager` · `/apps/bookmark`
- `/apps/kanban-board` · `/apps/timeline` · `/apps/faq` · `/apps/pricing` · `/apps/gallery`
- `/apps/blog-page/blog` · `/apps/blog-page/blog-details` · `/apps/blog-page/add-blog`

### Widgets — `/widgets`

### UI Kits (~23 routes)
Alert, Badges, Buttons, Cards, Dropdown, Grid, Avatar, Tabs, Accordions, Progress, Notifications, Lists, Helper Classes, Background, Divider, Ribbons, Editor, Collapse, Shadow, Wrapper, Bullet, Placeholder, Alignment Things — all under `/ui-kit/*`

### Advanced UI (badge `12+`)
Modals, OffCanvas Toggle, Sweet Alert, Scrollbar, Spinners, Animation, Video Embed, Tour, Slider — all under `/advance-ui/*`

### Implied groups (sidebar separators present, leaf routes loaded on demand)
- **Icons** — Phosphor (6 weights) + Tabler webfont
- **Misc** — `/misc`
- **Map & Charts** — Map and chart galleries
- **Tables & Forms** — `Table`, `Forms elements`, `Ready to use (new)`
- **Pages** — Auth Pages, Error pages, Other pages
- **Others** — `2 level` (multi-level nav demo)
- External: **Document** (external docs URL on cloudwaysapps.com), **Support** (mailto:teqlathemes@gmail.com)

## 3 — What was NOT captured in this pass (gap list)

These are limitations of one pass and should be filled if the OET rebuild needs deeper fidelity:

1. **Dark mode** — toggle clicked but `data-bs-theme` did not flip on either `html` or `body`. The customizer panel exposes "Vertical / Horizontal / **Dark**" under "Sidebar option" — labelled as a sidebar style, not a global theme. Bootstrap `[data-bs-theme="dark"]` selector exists in the CSS; the toggle wiring needs a deeper interaction pass to verify full dark-mode parity.
2. **Hover / focus / active states** — not captured. Requires per-component hover scripting via Playwright `browser_hover` per element of interest.
3. **Modal contents** — only the welcome dialog seen. Auth pages, profile edit, item-edit modals not captured.
4. **Form pages** — `/ui-kit/forms-elements` not visited in this pass.
5. **Auth pages** — `/auth/*` not visited; sidebar uses `#auth-pages` placeholder hash → no concrete URL given.
6. **Mobile sidebar interaction** — confirmed sidebar collapses to width 0 with an `overlay` present, but the trigger button (hamburger) wasn't located by class. Likely fires via JS-bound icon in the header on viewport < 992px (Bootstrap `lg` breakpoint).
7. **Chart configurations** — ApexCharts SVG present but config (series, axes, tooltip behaviour) not extracted.
8. **Tablet breakpoint (768–991px)** — only desktop (1920) and mobile (390) captured. Tablet behaviour inferred from Bootstrap `md`/`lg` breakpoints (768/992).
