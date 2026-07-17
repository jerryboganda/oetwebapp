# Agent State — CodeGraph + Serena Max-Potential Configuration

Last updated: 2026-07-17

## Goal
Configure CodeGraph and Serena to maximum potential in this OET Web App worktree.

## Implemented This Run

- CodeGraph 1.4.1 initialized and fully rebuilt:
  - 3,746 files, 69,828 nodes, 196,068 edges, 666 MB DB
  - `codegraph status` reports healthy and up to date
  - Sample query `codegraph query Program` returns symbols
- Serena 1.5.3 project tuned and re-indexed:
  - Renamed project to `OET Web App`
  - Added `csharp` language (backend now symbol-aware)
  - Added `ignored_paths` for build outputs, native artifacts, large binaries, vendored catalogs
  - Indexed `typescript=1960` files + `csharp=1615` files
  - Health-check passes under UTF-8 encoding
- Seeded 6 Serena memories with project invariants and Windows encoding workaround.
- Verified both MCP servers remain connected in Claude Code (`claude mcp list`).

## Files Touched

- `.serena/project.yml`
- `.serena/memories/*.md` (6 new memory files)
- `.github/agent-state.local.md`

## Notes

- `.serena/` and `.codegraph/` are gitignored; no tracked repo files were modified.
- CodeGraph MCP server was temporarily stopped to release the DB lock during rebuild; Claude Code automatically reconnected.

## Validation

- `codegraph status .` — healthy, up to date
- `codegraph query --limit 5 Program` — returns expected symbols
- `serena project health-check .` — exit 0 (with UTF-8 encoding workaround)
- `claude mcp list` — both `serena` and `codegraph` connected

## Blockers / Remaining Risk

- None.

## Next Step

Use the configured tools in future coding tasks; run Serena CLI commands with UTF-8 encoding to avoid Windows console emoji crashes.
