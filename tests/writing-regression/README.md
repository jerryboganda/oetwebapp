# Writing grader regression suite

Permanent non-regression gate for the nine defect classes Opus 5.5 High missed in the
22-letter benchmark (29 Sep 2026 audit). The defects are generalised into rules R1–R9 in
`WritingRev8HouseStyle.CandidateGradingRules` (Addendum Five) — no rule references a
specific letter, so each guards a reusable defect *type*, not one letter.

## Layout

| File | Purpose |
|---|---|
| `prompt_builder.py` | Rebuilds the live `writing.score.v1` prompt from the repo's C#/rulebook sources (byte parity). |
| `corpus/<letterId>.json` | 11 letters: 7 carrying the original defects + 4 clean controls. |
| `regression_key.json` | Frozen key — the 9 defects, each tagged R1–R9 with expected severity. |
| `run_regression.py` | Grades the corpus with Opus 5.5 High via the Claude Code CLI (**owner Max subscription**, not the paid API). Resumable. |
| `check_regression.py` | Gates: detection + exact severity (major→minor = fail) + zero material clean-control false positives. |
| `selftest_checker.py` | Proves the gates (oracle passes; blind / downgrader / over-caller fail). Runs in CI for free. |
| `stamp_manifest.py` | Writes `manifest.json`: per-profession prompt SHA-256 + passing-run record. |
| `verify_manifest.py` | CI verifier — the manifest must match the current prompt and record a pass. |
| `build_report.py` | Emits the before/after table (`regression_report.md`). |

## Running it (owner's machine — uses the Claude Max subscription, no paid API key)

```powershell
cd tests\writing-regression
python run_regression.py            # dry run: builds prompts, no model call
python run_regression.py --execute  # grade all 11 letters (claude -p, subscription)
python check_regression.py          # enforce the gates (exit 1 on any failure)
python stamp_manifest.py --report-last-run
python build_report.py              # before/after table
```

## CI gate

`.github/workflows/writing-regression.yml` runs the checker self-test and
`verify_manifest.py` whenever the Writing prompt, house-style rules, rulebooks, AI route
or this corpus change. `build-images.yml` makes the same check a hard gate before deploy: a
Writing prompt/rules/model change without a fresh passing `manifest.json` **blocks the
deploy**. Because grading runs on the owner's Claude subscription (unreachable from CI
runners), the gate verifies a *recorded* passing run rather than re-running the model —
a prompt change without a re-run is caught by the prompt-hash comparison.

## The nine guarded defect classes

| Rule | Severity | Defect class |
|---|---|---|
| R1 | major | Missing explicit request for a required recipient action |
| R2 | major | Omitted device/treatment + its planned date |
| R3 | major | Omitted procedure/operative outcome |
| R4 | major | Incomplete omitted-medication detection |
| R5 | major | Excessive irrelevant inpatient/nursing detail |
| R6 | minor | Related information scattered across paragraphs |
| R7 | minor | Outdated/superseded functional detail retained |
| R8 | minor | Recipient-useless technical detail included |
| R9 | minor | Content placed in the wrong section |
