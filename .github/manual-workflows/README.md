# Manual workflows (NOT registered)

Files here are deliberately outside .github/workflows/: the pipeline contract
(scripts/deploy/verify-pipeline-contract.mjs, owner directive 2026-10-06: CI never
runs automated QA) fails any QA-runner workflow in the workflows directory, which
blocks every Build images run on main.

To run writing-release-qa.yml by hand: move it back into .github/workflows/,
push, dispatch it from the Actions tab, then move it back here.
