# Verification Ledger

Append-only. Every row is machine-written by `pnpm run ax:record` from GitHub Actions
(`gh run list` / `gh run view`). Never hand-edit a Result — re-check it with
`pnpm run ax:verify`. A claim with no run id is not evidence.

| Date (UTC) | Claim / gate | Workflow | Run | Job(s) | Conclusion | SHA |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-02 01:26 | AX Ledger Tools | AX Ledger Tools | 36950958768 | self-test (windows, node 22)=success self-test (windows, node 24)=success self-test (linux, node 22)=success | SUCCESS | e27b55dbf |
