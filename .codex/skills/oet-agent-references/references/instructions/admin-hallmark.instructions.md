---
name: "Admin Hallmark"
description: "Use when touching app/admin, components/domain/admin, components/admin, admin panel UX, admin dashboards, admin content management, or admin visual design."
applyTo: "app/admin/**,components/domain/admin/**,components/admin/**"
---

# Admin Hallmark

Admin is an operational control surface, not a marketing page. Keep it dense, restrained, accessible,
and fast to scan.

## Source of guidance

- If a dedicated Hallmark/admin design system is present in the workspace, follow it.
- Otherwise follow `docs/admin-redesign/` (including the Axelit study) and `DESIGN.md`.

## Principles

- Favor information density and clarity over decoration. Tables, filters, and forms over hero banners.
- Reuse existing admin primitives in `components/domain/admin/**` and `components/admin/**` before
  building new UI.
- Maintain consistent spacing, type scale, and color tokens; support light and dark via the standard
  class-based `dark:` theming (see `frontend.instructions.md`).
- Keep destructive and high-impact admin actions explicit, confirmable, and clearly labeled.
- Meet accessibility basics: keyboard navigation, focus states, sufficient contrast, and labeled controls.
- Do not apply generic landing-page treatment to admin tools.

Admin endpoints and permissions are high-risk — see `security-ai.instructions.md`.
