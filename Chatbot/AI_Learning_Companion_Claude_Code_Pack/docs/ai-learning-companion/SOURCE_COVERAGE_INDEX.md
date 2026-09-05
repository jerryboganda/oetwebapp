# Source Coverage Index

This index proves that the Claude Code pack carries every major section of **AI Learning Companion Master Specification v3.0** into implementation guidance.

| Source section | Converted implementation destination |
|---|---|
| 1. Executive Summary | `FULL_REQUIREMENTS_TO_IMPLEMENT.md`, `ARCHITECTURE_AND_INTEGRATION.md` |
| 2. Product Principles and Non-Negotiables | `CLAUDE.md`, `FULL_REQUIREMENTS_TO_IMPLEMENT.md` |
| 3. Working Brand and Persona Direction | `UX_SURFACES_PERSONA_ACTIONS.md`, legal gate register |
| 4. Users, Roles and Product Surfaces | `UX_SURFACES_PERSONA_ACTIONS.md`, authorization architecture |
| 5. Candidate Intelligence Profile and Onboarding | `DATA_MODEL_CONTRACTS.md`, Stage 1 work breakdown, F-001…F-012 |
| 6. OET Knowledge Brain | `AI_RAG_MEMORY_AND_ROUTING.md`, `CONTENT_OPS_INGESTION.md`, F-013…F-029 |
| 7. Content Intelligence, Search and Video Awareness | RAG/content/video architecture and tests |
| 8. Adaptive Study Plan and Next-Best-Action | learning-engine architecture and Stage 2 plan |
| 9. Memory, Error DNA and Learning Fingerprint | data model + memory architecture, F-041…F-047 |
| 10. Teaching Modes | tutor orchestration and prompt-behaviour contracts |
| 11. Writing AI | Writing tutor/assessment workstream and calibration gates |
| 12. Speaking AI | Speaking workstream, voice gate, metering and calibration |
| 13. Reading and Listening Coaches | tutor analytics/drill workstreams |
| 14. Grammar, Vocabulary and Micro-Learning | memory/tutor workstreams |
| 15. Mock, Exam Mode and Test-Day Intelligence | exam integrity, analytics and E2E tests |
| 16. Multimodal and File Intelligence | upload/multimodal data/security contracts |
| 17. Platform Navigator and Action-Taking AI | action registry/deep-link architecture, F-098…F-112 |
| 18. Context-Aware AI Inside Every Screen | client context envelope + entitlement enforcement |
| 19. Readiness, Mastery and Personal Analytics | analytics/mastery data model and UX |
| 20. Proactive Coaching, Calendar and Notifications | Stage 3/notification workstream, F-113…F-122 |
| 21. Human Tutor Collaboration, Admin AI and Content Ops | admin/tutor services and dashboards |
| 22. Trust, Accuracy, Safety and Privacy | `SECURITY_PRIVACY_SAFETY_LEGAL_GATES.md` |
| 23. Monetization Architecture | `MONETIZATION_CREDITS_BILLING.md` |
| 24. Recommended Launch Pricing and Limits | price/allowance configuration contracts |
| 25. Detailed AI Tier Feature Matrix | tier capability policy engine |
| 26. AI Credits and Top-Up Revenue | credit ledger and charge/reversal workflow |
| 27. Profit Protection and Internal Cost Ceilings | telemetry, cost budget and kill switches |
| 28. Illustrative Revenue Economics | marked non-authoritative planning reference only |
| 29. Course Bundles and AI Add-Ons | entitlement × AI subscription integration |
| 30. Upgrade UX, Paywalls and Conversion | contextual paywall/checkout continuation flow |
| 31. Growth, Lead Generation and SEO | analytics, consent, admin content queue, positive-moment referral rules |
| 32. Multi-Exam Expansion | Stage 4 modular exam-pack architecture |
| 33. B2B, White-Label and Enterprise | Stage 5 tenant architecture |
| 34. Business and AI Operations Dashboard | telemetry/data warehouse/dashboard requirements |
| 35. Complete Feature Inventory | `FEATURE_TRACEABILITY_MATRIX.md`, JSON/CSV |
| 36. Recommended Release Sequence | `RELEASE_PLAN_WORKBREAKDOWN.md` |
| 37. Final Acceptance Criteria | release gates and production verification prompt |
| 38. Unit Economics, Channel Fees and Regional Pricing | commercial configuration + validation gates |
| 39. Cohort Economics, Churn, LTV and CAC | analytics events/cohort model + decision register |
| 40. Technical Architecture Requirements | `ARCHITECTURE_AND_INTEGRATION.md`, RAG/model/observability design |
| 40A. Core Data Model and Schemas | `DATA_MODEL_CONTRACTS.md` |
| 40B. Operational Reliability and Incident Response | reliability/runbook and QA gates |
| 41. Evaluation and Quality Assurance | `QA_EVALUATION_AND_RELEASE_GATES.md` |
| 42. Content Ingestion and Knowledge Operations | `CONTENT_OPS_INGESTION.md` |
| 43. Arabic and Voice Feasibility Spike | Stage 3 gate + TO VERIFY register |
| 44. Legal, Privacy, Trademark and App-Store Readiness | legal/privacy/app-store gate register |
| 45. Risk Register | security/release planning + TO VERIFY register |
| 46. Explicit Non-Goals for the First 12 Months | release-plan deferrals and feature flags |
| 47. Support and Human-Cost Model | cost telemetry and operations dashboard |
| 48. Success Criteria and Target Register | analytics schema + KPI gate register |
| 49. Final Coverage Verdict / Remaining Gates | Stage 0 + beta exit criteria + change control |
| Appendix A. Implementation Traceability Matrix | human and machine feature matrices |
| Appendix B. Stage Plan | staged work breakdown + dependency gates |
| Appendix C. Verified External Facts and TO VERIFY Register | `TO_VERIFY_AND_DECISION_REGISTER.md` |

## Coverage rule for Claude Code

If implementation discovers a requirement in the source-derived documents that does not map to a feature ID, it still must be implemented or explicitly gated. The 184-feature matrix is the inventory backbone, not permission to ignore cross-cutting economics, security, reliability, legal or QA requirements that are stated outside the feature table.

