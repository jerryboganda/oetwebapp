# Claude writing sidecar.
#
# Base: the SAME image as the owner agent-console. It already ships the
# verified `claude` CLI (/usr/local/lib/oet-agent/claude) plus node22, and —
# critically — is the image whose Claude Code credentials the backend shares
# via the oet_agent_home volume, so the dedicated Max 5x login works here
# with no re-auth. We layer ONLY the tiny facade on top and never touch the
# CLI binaries.

ARG AGENT_CONSOLE_IMAGE=ghcr.io/jerryboganda/oetwebapp-agent-console:latest
FROM ${AGENT_CONSOLE_IMAGE}

USER 0
WORKDIR /srv/writing-ai
COPY shared/ /srv/writing-ai/shared/
COPY claude/server.mjs /srv/writing-ai/claude/server.mjs
RUN chown -R 10002:10002 /srv/writing-ai

# Run as the unprivileged engine uid (same uid the agent-console uses for its
# engines) so the facade can read the shared credential volume.
USER 10002

# The base image's ENTRYPOINT (oet-agent-entrypoint) is the console's control
# plane: it demands uid 0 and would exec the CONSOLE's server. The facade runs
# its own node server directly, so the inherited entrypoint is cleared.
ENTRYPOINT []

ENV NODE_ENV=production \
    PORT=8080 \
    CLAUDE_CONFIG_DIR=/home/agent/.claude

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||8080)+'/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "/srv/writing-ai/claude/server.mjs"]
