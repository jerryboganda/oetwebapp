# AGENTS

> **Scope:** This file applies only to work inside `.tools/autoskills/`, which is hardened with
> `fendo`. The whole OET repo uses **pnpm** with `pnpm-lock.yaml`; this subtree keeps its own pinned
> pnpm and supply-chain hardening. Do not apply these pnpm/fendo rules to the main app.

<!-- fendo:start -->
## Supply Chain Security

This project has been hardened against supply chain attacks using [fendo](https://github.com/midudev/fendo).

### Rules for AI assistants and contributors

- **Never use `^` or `~`** in dependency version specifiers. Always pin exact versions.
- **Always commit the lockfile** (`pnpm-lock.yaml`). Never delete it or add it to `.gitignore`.
- **Install scripts are disabled**. If a new dependency requires a build step, it must be explicitly approved.
- **New package versions must be at least 1 day old** before they can be installed (release age gating is enabled).
- When adding a dependency, verify it on [npmjs.com](https://www.npmjs.com) before installing.
- Prefer well-maintained packages with verified publishers and provenance.
- Run `pnpm install` with the lockfile present — never bypass it.
- Do not add git-based or tarball URL dependencies unless explicitly approved.
- **Do not run `npm update`**, `npx npm-check-updates`, or any blind upgrade command. Review each update individually.
- **Use deterministic installs**: prefer `pnpm install --frozen-lockfile` over `pnpm install` in CI and scripts.
- **Do not store secrets in plain text** in `.env` files committed to version control.
<!-- fendo:end -->
