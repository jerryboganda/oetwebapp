# Speaking Module — Security Documentation

Reading order:

1. [data-classification.md](data-classification.md) — what data the module holds and how it is labelled.
2. [attack-surface.md](attack-surface.md) — every external surface.
3. [threat-model.md](threat-model.md) — STRIDE per asset.
4. [abuse-cases.md](abuse-cases.md) — adversary playbooks + detect/respond.
5. [checklist.md](checklist.md) — pre-launch security gates.
6. [key-rotation.md](key-rotation.md) — provider key rotation procedure.
7. [penetration-test-scope.md](penetration-test-scope.md) — pentest scope + rules of engagement.

Cross-references: `docs/AI-USAGE-POLICY.md`, `docs/speaking-module-runbook.md`, `docs/speaking/sla.md`, `docs/speaking/incident-runbook.md`, `docs/security/admin-rbac-policy-mapping.md`, `docs/security/ai-provider-secret-redaction.md`.

## Known gaps tracked here

- **Originality watermark for learner recordings** — referenced as a mitigation; not yet shipped. Tracked under abuse case #2.
- **Prompt-injection classifier on user-supplied card text** — grounded-prompt contract enforces grounding but no dedicated jailbreak classifier. Tracked under abuse case #3.
- **MFA enforcement for tutors** — `ExpertOnly` policy does not currently gate a second factor. Tracked under abuse case #5.
- **Post-mortem template** — referenced by `docs/speaking/incident-runbook.md` but not yet authored. Create on first incident.
