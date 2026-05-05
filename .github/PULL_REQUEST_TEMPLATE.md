## Summary

- What changed?
- Which route tier and persona does this affect?
- Is this behind a feature flag or rollback path?

## User-Facing Checklist

- [ ] The route still has one obvious primary action.
- [ ] Loading, empty, error, partial, and success states are covered where relevant.
- [ ] Copy is learner/operator-facing and does not expose developer notes, placeholders, or internal tags.
- [ ] The change preserves mission-critical scoring, rulebook, AI grounding, content upload, result card, reading, grammar, pronunciation, and conversation invariants.
- [ ] H7 design-system consistency is checked: approved tokens, component variants, direct imports, and `motion/react` are used where relevant.
- [ ] Frontend/backend calls use approved API helpers or a documented exception.
- [ ] Keyboard navigation, focus visibility, accessible names, contrast, and 360 px mobile behavior were checked for changed controls.
- [ ] The change does not silently display placeholder, stub, sandbox, or mock data in production.
- [ ] Focused tests, E2E/manual checks, or screenshots are linked below.

## Evidence

- Typecheck/lint/tests:
- E2E/manual smoke:
- Accessibility checks:
- Screenshots or recordings:

## Risk And Rollback

- Primary risk:
- Rollback or feature-flag path:
- Follow-up backlog IDs: