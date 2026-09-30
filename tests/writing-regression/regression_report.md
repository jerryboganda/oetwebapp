# Writing grader regression — before/after (Addendum Five, 29 Sep 2026)

Model: claude-opus-5-5, effort high, via Claude Code CLI (Max subscription). House style owner-clarifications-5-2026-09-29.

## Original 9 missed defects

| # | Defect type | Previous Opus result | New Opus result | Expected severity | Assigned | New FP? |
|---|---|---|---|---|---|---|
| R1 | Missing explicit request to recipient (employer equipment / phased return) | Missed | Detected | major | major | none |
| R2 | Omitted device/treatment + planned date (spica cast + removal date) | Missed | Detected | major | major | none |
| R3 | Omitted operative outcome (uncomplicated / no complications) | Missed | Detected | major | major | none |
| R4 | Incomplete omitted-medication detection (regular medicines) | Partly missed | Detected | major | major | none |
| R5 | Excessive irrelevant inpatient/nursing detail not excluded | Missed | Detected | major | major | none |
| R6 | Related info scattered across paragraphs (dry eye) | Missed | Detected | minor | major | none |
| R7 | Outdated/irrelevant functional detail retained (old mobility) | Missed | Detected | minor | minor | none |
| R8 | Recipient-useless technical detail (imaging/compression) | Missed | Detected | minor | minor | none |
| R9 | Presenting symptom placed in wrong section (among imaging findings) | Missed | Detected | minor | minor | none |

## Unseen-equivalent generalisation (3 per defect type, different clinical contexts)

| # | Unseen cases detected | Severities assigned |
|---|---|---|
| R1 | 3/3 | major |
| R2 | 3/3 | major |
| R3 | 3/3 | major |
| R4 | 3/3 | major |
| R5 | 3/3 | major |
| R6 | 3/3 | major, minor |
| R7 | 3/3 | minor |
| R8 | 3/3 | minor |
| R9 | 3/3 | major, minor |

Clean-control material (critical/major) false positives across the clean letters: **0**
