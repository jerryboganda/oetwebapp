# Private CI on a self-hosted runner

**Decision (owner, 20 Sep 2026):** `jerryboganda/GEPA` and `jerryboganda/oetwebapp` stay
**private at all times** — never flipped public, even temporarily — to run GitHub Actions.

## Why a self-hosted runner

Private-repo GitHub-hosted runners are refused for this account:

> The job was not started because recent account payments have failed or your spending limit
> needs to be increased. Please check the 'Billing & plans' section in your settings.

Symptom: every job ends `failure` with `steps=0` and no runner ever assigned, in seconds.
Confirm it (do not assume) with the check-run annotation:
`gh api repos/<owner>/<repo>/check-runs/<job_id>/annotations`.

Until billing is fixed, CI and deploy run on a private runner the owner controls.

## How workflows pick the runner

The CI and deploy jobs use:

```yaml
runs-on: ${{ vars.CI_RUNS_ON || 'ubuntu-latest' }}
```

| Repository variable `CI_RUNS_ON` | Jobs run on |
|---|---|
| `oet-private` | the self-hosted runner below |
| unset / empty | GitHub-hosted `ubuntu-latest` (needs billing fixed) |

Switch back to GitHub-hosted, once billing is fixed:
`gh variable delete CI_RUNS_ON --repo jerryboganda/<repo>`.
Switch to the private runner: `gh variable set CI_RUNS_ON --body oet-private --repo jerryboganda/<repo>`.

## The runner host

* A dedicated WSL2 distro named **`oet-ci`** (Ubuntu 24.04) stored at `D:\wsl\oet-ci`. It is
  separate from any other WSL distro on the workstation.
* Hardening: Windows interop and Windows drive mounts are **disabled** in `/etc/wsl.conf`, so job
  code cannot read the Windows filesystem or start Windows programs. There are no personal
  credentials in the distro. Repo secrets reach a job only through the normal Actions mechanism.
* Docker Engine, `gh`, `ffmpeg`, `jq`, `python3`, build tools and the Playwright system libraries
  are installed. Node, .NET and Rust toolchains come from the workflows' own setup actions.
* One runner process per repository, label `oet-private`. A runner runs one job at a time, so
  fixed-port service containers (Postgres on 5432) never collide *within* a repo. **Run only one
  repo's jobs at a time** — the two repos share one Docker daemon and both bind port 5432.

Provisioning scripts: `D:\wsl\oet-ci-setup\01-base.sh` (host) and `02-runner.sh` (register a runner).

## Operating it

Start / stop (nothing starts automatically with Windows):

```powershell
wsl.exe -d oet-ci -u root -- systemctl start docker        # first start after boot
wsl.exe -d oet-ci -- bash -lc 'cd ~/runners/oet-ci-gepa && sudo ./svc.sh start'
wsl.exe -d oet-ci -- bash -lc 'cd ~/runners/oet-ci-gepa && sudo ./svc.sh status'
wsl.exe --terminate oet-ci                                  # stop everything
```

While the distro is stopped, jobs that target `oet-private` **queue and wait** — they do not fail.
If a deploy seems hung, check that the runner is online:
`gh api repos/jerryboganda/<repo>/actions/runners --jq '.runners[] | "\(.name) \(.status)"'`.

Disk hygiene (Docker layers grow): `wsl.exe -d oet-ci -- docker system prune -af --volumes`
between runs. The distro disk lives on `D:`.

## Rules

1. **Never register a self-hosted runner on a public repository.** Public forks could then run code
   on the workstation. If a repo is ever made public, deregister its runner first.
2. Deregister a runner: get a removal token with
   `gh api -X POST repos/jerryboganda/<repo>/actions/runners/remove-token -q .token` and run
   `./config.sh remove --token <token>` in the runner directory; or delete it in
   Settings → Actions → Runners.
3. Remove the whole host: `wsl.exe --unregister oet-ci` (deletes the distro and its disk).
4. This runner is a stop-gap for the billing block, not a security boundary for untrusted code.
