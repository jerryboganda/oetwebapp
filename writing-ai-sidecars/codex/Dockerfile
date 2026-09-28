# Codex writing sidecar.
#
# Base: the SAME image as the owner agent-console (ships the verified `codex`
# CLI + node22). Codex credentials are NOT shared with the agent console —
# the console's /etc/codex/requirements.toml forces approval_policy
# UnlessTrusted (which hangs headless `codex exec`). This sidecar gets its own
# credential volume (oet_writing_codex_home) and a one-time device-code login.

ARG AGENT_CONSOLE_IMAGE=ghcr.io/jerryboganda/oetwebapp-agent-console:latest
FROM ${AGENT_CONSOLE_IMAGE}

USER 0
WORKDIR /srv/writing-ai
COPY shared/ /srv/writing-ai/shared/
COPY codex/server.mjs /srv/writing-ai/codex/server.mjs
RUN chown -R 10002:10002 /srv/writing-ai

USER 10002

# The base image's ENTRYPOINT (oet-agent-entrypoint) is the console's control
# plane: it demands uid 0 and would exec the CONSOLE's server. The facade runs
# its own node server directly, so the inherited entrypoint is cleared.
ENTRYPOINT []

ENV NODE_ENV=production \
    PORT=8080 \
    CODEX_HOME=/home/agent/.codex

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||8080)+'/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "/srv/writing-ai/codex/server.mjs"]
