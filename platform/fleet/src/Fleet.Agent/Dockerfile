# syntax=docker/dockerfile:1.19
#
# OET fleet helper agent (OET-RWP/1). Built ONLY by .github/workflows/fleet.yml from a publish directory produced on the
# Actions host (platform/fleet/publish/agent): the helper VPS pulls this image by DIGEST and never builds, tests or installs
# anything. Context is the repository root; Dockerfile.dockerignore keeps it to the publish output.
#
# Runtime contract (protocol 10.1, enforced again by `oet-fleet-ctl run` flags on the helper):
#   read-only root filesystem, /scratch and /tmp are tmpfs, non-root uid 10001, no capabilities, no docker socket,
#   no host mounts, no swap, core dumps off, only outbound HTTPS to the API host.
#
# Parity with the API image (protocol 6.1.4 item 4): linux/amd64 only, the same Debian-family dotnet base of the same major
# version, ICU present, invariant globalization and LANG / LC_ALL are never set in either image.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/runtime:10.0

# ffmpeg serves media.audio-extract and media.speaking-join only (decode and encode through pipes, never a network
# input). The agent reads the installed version at start and offers a media kind only when its engineVersion is known.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 fleet \
    && useradd --uid 10001 --gid 10001 --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin fleet \
    && mkdir -p /scratch \
    && chown 10001:10001 /scratch \
    && chmod 0700 /scratch

WORKDIR /app
COPY platform/fleet/publish/agent/ ./

# Build-time proof that the image is complete; nothing here changes the runtime.
RUN test -f /app/Fleet.Agent.dll \
    && test -f /app/Fleet.Agent.runtimeconfig.json \
    && ffmpeg -version | head -n 1

ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_NOLOGO=1

LABEL org.opencontainers.image.title="oet-fleet-agent" \
      org.opencontainers.image.description="OET fleet helper agent: stateless fenced-job executor (OET-RWP/1)" \
      oet.fleet.component="agent"

USER 10001:10001

# The agent exposes no port. It touches /tmp/oet-agent-health after every node heartbeat attempt; a stale file means the
# heartbeat loop is wedged.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD ["/bin/sh", "-c", "test $(( $(date +%s) - $(stat -c %Y /tmp/oet-agent-health) )) -lt 120"]

STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "Fleet.Agent.dll"]
