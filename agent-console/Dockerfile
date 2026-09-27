# syntax=docker/dockerfile:1
# oet-agent-console — control plane + Claude Code / Codex engines of the Owner Agent Console
# (agent-console/CONTRACT.md §2). Built only on GitHub Actions (.github/workflows/agent-console.yml,
# context ./agent-console) and pulled by the VPS; never built there or on a workstation.
#
# The container runs as uid 0 with capabilities reduced by compose (control plane). Engines and
# every tool they run are started as uid 10002 through /usr/local/bin/as-agent. PID 1 is
# docker's init (compose `init: true`), which reaps the engines' orphaned children.
ARG NODE_IMAGE=node:22-bookworm-slim@sha256:43ac6c60b8f89723f746e8a92ce91abd5017e627ce1ddfe4238355d3a30b772c

# ── build: dependencies (native node-pty / better-sqlite3), TypeScript ──────────────────────
FROM ${NODE_IMAGE} AS build
ENV npm_config_update_notifier=false \
    npm_config_fund=false
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 make g++ ca-certificates \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY package.json package-lock.json* .npmrc ./
# Optional dependencies must stay installed: they carry the SDK-bundled Claude Code binary
# (@anthropic-ai/claude-agent-sdk-linux-*) and the Codex native binary (@openai/codex-linux-*).
RUN if [ -f package-lock.json ]; then npm ci --no-audit; else npm install --no-audit; fi \
 && npm audit signatures
COPY tsconfig.json ./
COPY src ./src
RUN npx --no-install tsc -p tsconfig.json \
 && npm prune --omit=dev

# ── runtime ──────────────────────────────────────────────────────────────────────────────────
FROM ${NODE_IMAGE} AS runtime

ARG GITLEAKS_VERSION=8.30.1
ARG GITLEAKS_SHA256=551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb
# Primary-key fingerprints of the third-party apt repositories; a key file carrying any other
# primary key fails the build. GitHub CLI publishes two current signing keys.
ARG PGDG_KEY_FPRS="B97B0AFCAA1A47F044F244A07FCC7D46ACCC4CF8"
ARG DOCKER_KEY_FPRS="9DC858229FC7DD38854AE2D88D81803C0EBFCD88"
ARG GH_CLI_KEY_FPRS="2C6106201985B60E6C7AC87323F3D4EA75716059 7F38BBB59D064DBCB3D84D725612B36462313325"

LABEL org.opencontainers.image.title="oet-agent-console" \
      org.opencontainers.image.description="OET Owner Agent Console sidecar (control plane + Claude Code / Codex engines)" \
      org.opencontainers.image.source="https://github.com/jerryboganda/oetwebapp"

ENV DEBIAN_FRONTEND=noninteractive
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

# OS tools the agent uses: git, gh, psql 17 (PGDG), docker CLI + compose (Docker repo), rg, jq,
# curl, ssh client; setpriv (util-linux) for as-agent; procps for the kill switch.
# VERIFY-ON-PIN: apt packages float within their pinned major/series; bump the base digest to
# refresh them.
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl gnupg; \
    install -d -m 0755 /etc/apt/keyrings; \
    GNUPGHOME="$(mktemp -d)"; export GNUPGHOME; \
    primary_fprs() { gpg --show-keys --with-colons "$1" | awk -F: '$1=="pub"{p=1;next} p&&$1=="fpr"{print $10;p=0}'; }; \
    check_fprs() { \
      local file="$1" allowed=" $2 " fpr found=""; \
      for fpr in $(primary_fprs "$file"); do \
        [[ "$allowed" == *" $fpr "* ]] || { echo "unexpected signing key $fpr in $file" >&2; exit 1; }; \
        found=1; \
      done; \
      [ -n "$found" ] || { echo "no signing key in $file" >&2; exit 1; }; \
    }; \
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /etc/apt/keyrings/pgdg.gpg; \
    check_fprs /etc/apt/keyrings/pgdg.gpg "$PGDG_KEY_FPRS"; \
    curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg; \
    check_fprs /etc/apt/keyrings/docker.gpg "$DOCKER_KEY_FPRS"; \
    curl -fsSLo /etc/apt/keyrings/githubcli.gpg https://cli.github.com/packages/githubcli-archive-keyring.gpg; \
    check_fprs /etc/apt/keyrings/githubcli.gpg "$GH_CLI_KEY_FPRS"; \
    chmod 0644 /etc/apt/keyrings/*.gpg; \
    arch="$(dpkg --print-architecture)"; \
    echo "deb [arch=${arch} signed-by=/etc/apt/keyrings/pgdg.gpg] https://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" > /etc/apt/sources.list.d/pgdg.list; \
    echo "deb [arch=${arch} signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian bookworm stable" > /etc/apt/sources.list.d/docker.list; \
    echo "deb [arch=${arch} signed-by=/etc/apt/keyrings/githubcli.gpg] https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
      git openssh-client postgresql-client-17 docker-ce-cli docker-compose-plugin \
      ripgrep jq gh util-linux procps less; \
    apt-get purge -y --auto-remove gnupg; \
    rm -rf /var/lib/apt/lists/* "$GNUPGHOME"; \
    setpriv --version; psql --version; docker --version; gh --version

# gitleaks (Ship executor's secret scan), pinned release + checksum.
RUN set -eux; \
    [ "$(dpkg --print-architecture)" = amd64 ] || { echo "gitleaks is pinned for linux/amd64 only" >&2; exit 1; }; \
    curl -fsSLo /tmp/gitleaks.tgz "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz"; \
    echo "${GITLEAKS_SHA256}  /tmp/gitleaks.tgz" | sha256sum -c -; \
    tar -xzf /tmp/gitleaks.tgz -C /usr/local/bin gitleaks; \
    chown root:root /usr/local/bin/gitleaks; \
    chmod 0755 /usr/local/bin/gitleaks; \
    rm -f /tmp/gitleaks.tgz; \
    gitleaks version

# The agent identity (CONTRACT.md §2). The control plane is uid 0.
RUN groupadd --gid 10002 agent \
 && useradd --uid 10002 --gid 10002 --home-dir /home/agent --no-create-home --shell /bin/bash agent

ENV NODE_ENV=production \
    NODE_OPTIONS=--max-old-space-size=512 \
    DISABLE_AUTOUPDATER=1 \
    DISABLE_UPDATES=1 \
    CLAUDE_CONFIG_DIR=/home/agent/.claude \
    CODEX_HOME=/home/agent/.codex

WORKDIR /app
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY etc ./etc
COPY bin/as-agent bin/claude-as-agent bin/oet-console-erase /usr/local/bin/
COPY bin/entrypoint.sh /usr/local/bin/oet-agent-entrypoint
COPY scripts/oet-env-edit /usr/local/bin/oet-env-edit

# Wrappers + policy, and stable paths for the SDK-bundled engine binaries:
#   /usr/local/lib/oet-agent/claude   the Agent SDK's bundled Claude Code (unmodified)
#   /usr/local/lib/oet-agent/codex    the pinned @openai/codex native binary
#   /usr/local/bin/{claude,codex}     same, for SSH break-glass `docker exec -u agent … claude auth login`
RUN set -eux; \
    for f in as-agent claude-as-agent oet-agent-entrypoint oet-env-edit oet-console-erase; do \
      sed -i 's/\r$//' "/usr/local/bin/$f"; \
      chown root:root "/usr/local/bin/$f"; \
      chmod 0755 "/usr/local/bin/$f"; \
    done; \
    find /app/etc -type f -exec sed -i 's/\r$//' {} +; \
    chown -R root:root /app; \
    chmod -R go-w /app; \
    arch="$(node -p process.arch)"; \
    claude_pkg="/app/node_modules/@anthropic-ai/claude-agent-sdk-linux-${arch}"; \
    claude_bin="$claude_pkg/claude"; \
    # VERIFY-ON-PIN: binary file name inside the platform package; fall back to a search.
    [ -x "$claude_bin" ] || claude_bin="$(find "$claude_pkg" -type f -name claude -perm -u+x 2>/dev/null | head -n 1)"; \
    [ -n "$claude_bin" ] && [ -x "$claude_bin" ] || { echo "SDK-bundled Claude Code binary not found under $claude_pkg" >&2; exit 1; }; \
    codex_bin="$(find /app/node_modules/@openai -path '*linux*' -type f -name codex -perm -u+x | head -n 1)"; \
    [ -n "$codex_bin" ] || { echo "Codex native binary not found under node_modules/@openai" >&2; exit 1; }; \
    install -d -m 0755 /usr/local/lib/oet-agent; \
    ln -sf "$claude_bin" /usr/local/lib/oet-agent/claude; \
    ln -sf "$codex_bin" /usr/local/lib/oet-agent/codex; \
    ln -sf /usr/local/lib/oet-agent/claude /usr/local/bin/claude; \
    ln -sf /usr/local/lib/oet-agent/codex /usr/local/bin/codex; \
    install -d -m 0755 /etc/claude-code /etc/codex; \
    install -o root -g root -m 0644 /app/etc/managed-settings.json /etc/claude-code/managed-settings.json; \
    # Smoke-run both binaries with throwaway config dirs: nothing may be created under
    # /home/agent at build time (a named volume is seeded from the image on first mount, and
    # root-owned files there would be unwritable for the agent).
    HOME=/tmp CLAUDE_CONFIG_DIR=/tmp/claude-version-check /usr/local/lib/oet-agent/claude --version; \
    HOME=/tmp CODEX_HOME=/tmp/codex-version-check /usr/local/lib/oet-agent/codex --version; \
    rm -rf /tmp/.claude* /tmp/claude-version-check /tmp/codex-version-check; \
    [ ! -e /home/agent ] || { echo "/home/agent must not exist in the image" >&2; exit 1; }

EXPOSE 8410
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=5 \
  CMD curl -fsS -m 5 http://127.0.0.1:8410/healthz >/dev/null || exit 1
ENTRYPOINT ["/usr/local/bin/oet-agent-entrypoint"]
