# Manual workflows (NOT registered)

Files here are deliberately outside .github/workflows/: the pipeline contract
(scripts/deploy/verify-pipeline-contract.mjs, owner directive 2026-10-06: CI never
runs automated QA) fails any QA-runner workflow in the workflows directory, which
blocks every Build images run on main.

Test-only workflows (writing-release-qa.yml) were deleted on 2026-10-08 with the test code
(tag last-commit-with-tests). What remains here is a parked manual measurement tool; it is not
registered in CI.
