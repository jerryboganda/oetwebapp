# ACTION REQUIRED — hung ship process is blocking the release path

**Status at 2026-10-09 16:40Z:** the shared release lock is held by a **hung** ship
process. Nothing can be pushed to `main` by any session until it is cleared. This is a
repo-wide outage of the release path, not a problem with one agent's work.

## The diagnosis (measured, not inferred)

| Signal | Value | What it means |
| --- | --- | --- |
| Process | `pid 22100` (node) | The ship wrapper holding `<git-common-dir>/ax-ship/lock.json` |
| Started | 2026-10-09 16:16:29 | |
| **CPU consumed** | **0.2 s** over ~24 min wall | Not working. A live ship accumulates CPU. |
| Lock file mtime | 16:16:29, unchanged | Never updated after the first write |
| Runs in flight | **none** | `Build images c6e1c4a0e` and `Deploy production c6e1c4a0e` both **success** |
| Lock recorded expiry | 2026-10-09T11:46:29Z | Already ~5 h past, and the file was written *after* it |

A ship that had work to do would show CPU growth and progress. This one completed its
release successfully and then neither released the lock nor exited.

## Why this was not auto-cleared

`AGENTS.md` is explicit: *"Recover only a verified inactive lock, never force-release a
live owner or drop another holder."* The OS still reports the process as running, so an
agent must not unilaterally terminate another session's release process — the owner of
that session needs to decide. The ship wrapper refuses to override a live owner anyway.

## Clear it (either command frees the path)

```powershell
Stop-Process -Id 22100 -Force
```

Then push the waiting work with the normal wrapper:

```powershell
cd "D:\Projects\OET with Dr Hesham\OET Project Web App"
node scripts/ship/ship.mjs
```

A retry loop has also been polling (`pwsh-18`); it will push automatically the moment the
lock frees, so running the second command is only needed if that loop has stopped.

## Work waiting to be pushed

| Commit | What it is |
| --- | --- |
| `114766728` | **Fix:** `official_result` entries were invisible to `companion_why_score_change`, so a learner who had just recorded an official result and asked why their score moved was told they had *no confirmed scores* — the tool denying data it held. Also adds **F-083 personal bests**, with a `basis` flag so a single result is never presented as a best to beat. |
| `c81a33eac` | Documents the break-the-neighbour defect class and this lock behaviour. |

Both are committed and safe in local history. Nothing is half-written and nothing is lost;
they simply have not reached `origin/main` or a build yet.

`09ed154c8` (a sibling's writing-audit commit) is also sitting unpushed for the same reason.

## What to check after the push

1. `Build images` green for the new SHA — this is the first compile of commits `114766728`
   and `c81a33eac`, so it is the real check on both.
2. `Deploy production` green, then confirm `X-Oet-Release` on
   `https://api.oetwithdrhesham.co.uk/health/live` carries it.
3. Repo visibility returns to **PRIVATE** once no lease holder and no run is in flight —
   `node scripts/ship/ship.mjs --may-flip-private` decides. It was left **PUBLIC** by the
   hung process's lease.
