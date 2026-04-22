# Phase 7 — Design System & Motion Consistency

## Artifacts
- `token-audit.md` — grep findings of raw hex / px usages bypassing tokens.
- `component-inventory.md` — catalog of `components/ui/*`, variant usage, drift (e.g., `Badge` using `destructive` instead of `danger`).
- `motion-consistency.md` — timings vs. `motion-system` skill; list routes that deviate.
- `figma-library-sync.md` — components to build/update in Figma to match code truth; one entry per mismatch.

## Forbidden patterns (add to lint rules)
- `import ... from 'framer-motion'` → must be `motion/react`.
- `<Badge variant="destructive">` → must be `danger`.
- `<Button variant="default">` → must be `primary`.
- Inline colors outside token set.

## Exit criteria
- 0 drift instances on T0 routes.
- Figma library equals code truth; every public component has a Figma counterpart.
