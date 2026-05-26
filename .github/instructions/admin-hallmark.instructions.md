---
name: "OET Admin Hallmark Discipline"
description: "Use when: touching app/admin, components/domain/admin, admin panel UX, admin dashboards, admin content management, or admin visual design."
applyTo: "app/admin/**,components/domain/admin/**,components/admin/**"
---

# Admin Hallmark Discipline

- Before editing admin UI, load the Hallmark design guidance if the `hallmark` skill is available.
- If Hallmark is unavailable in the current tool surface, fail closed for broad admin redesigns and ask before proceeding with a documented substitute from `docs/admin-redesign/`.
- Admin UI work must preserve light/dark parity, locked design tokens, structural variety, honest copy, and dense operational usability.
- Do not copy marketing-page patterns into admin workflows. Admin surfaces should be restrained, scannable, and efficient for repeated work.
- Keep admin permission, audit, runtime settings, and content publish gates intact.