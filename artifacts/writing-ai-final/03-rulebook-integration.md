# 03 — Rulebook Integration (Source of Truth)

## Approved source used

`C:\Users\Dr Faisal Maqsood PC\Desktop\01_FINAL_RULEBOOK_PDFS`
(FINAL MASTER Writing ×6 + Speaking ×6 + Master Index & Governance, canonical
release 31 Aug 2026, "Canonical v1.0"). No internet advice, no Speaking
content, and no invented rules were used. All rule-body corrections below are
verbatim audit decisions from the PDFs.

## Measured fidelity (scripted comparison, PyMuPDF + JSON)

- Medicine PDF: 172 R-rules (R01.1–R16.8) — ID set EXACTLY matches
  `rulebooks/writing/medicine/rulebook.v1.json` (172/172).
- §8 legacy audit semantics (verified by reading full rows): the audit lists
  LEGACY wordings as provenance-only ("The active canonical rules earlier in
  this book are the only rules the AI should enforce"). Statuses:
  HISTORICAL_TEACHING_RECAST 104, TEACHER_PREFERENCE_ONLY 35,
  OVERRIDDEN_OR_CORRECTED 26, SUPPORTED_CORE_RECAST 7.
- DEFECT FOUND: all 26 OVERRIDDEN_OR_CORRECTED rules were present in the
  runtime JSONs with the RETIRED legacy wording as active critical/major
  rules (e.g. R01.5 "suspected cancer ALWAYS → urgent" vs canonical "does not
  automatically define task type"; R03.4 "smoking/drinking ALWAYS include" vs
  canonical "relevance and reader needs control content"; full 26-row mapping
  in `rulebook-corrections.csv`).
- FIX APPLIED: all 26 bodies replaced verbatim with the canonical audit
  decisions in all 13 `rulebooks/writing/*/rulebook.v1.json`; version
  1.0.0 → 1.0.1. IDs, titles, severities, checkIds, enforcement untouched.
  `other-allied-health` hand edits (49 rules/tables) preserved — propagation
  was per-file, not regenerat
...[truncated 3010 chars]