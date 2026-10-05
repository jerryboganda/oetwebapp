# OET fleet manager image. Build context: platform/fleet/ (built ONLY by GitHub Actions, never on the VPS).
#
# Runtime profile actually used (see docker-compose.fleet.yml and README "Container hardening"):
#   * non-root uid 10020, read-only root filesystem, all capabilities dropped, no-new-privileges;
#   * writable paths are exactly: /data (the external named volume with the SQLite file), /tmp and /home/fleet
#     (tmpfs). ansible-playbook and ssh need a writable HOME and temp directory; the manager points
#     HOME, ANSIBLE_HOME and ANSIBLE_LOCAL_TEMP at a per-run directory under /tmp/fleet and deletes it afterwards;
#   * packages added to the stock ASP.NET image: ansible-core, openssh-client, python3, ca-certificates. Nothing else
#     (no compiler, no docker CLI, no curl). Ansible uses ansible.builtin modules only, so no Galaxy download ever happens.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Fleet.Core/Fleet.Core.csproj src/Fleet.Core/
COPY src/Fleet.Manager/Fleet.Manager.csproj src/Fleet.Manager/
RUN dotnet restore src/Fleet.Manager/Fleet.Manager.csproj
COPY src/Fleet.Core/ src/Fleet.Core/
COPY src/Fleet.Manager/ src/Fleet.Manager/
RUN dotnet publish src/Fleet.Manager/Fleet.Manager.csproj -c Release -o /out --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ansible-core openssh-client python3 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10020 fleet \
    && useradd --uid 10020 --gid 10020 --home-dir /home/fleet --shell /usr/sbin/nologin fleet \
    && mkdir -p /opt/fleet /data /home/fleet \
    && chown -R 10020:10020 /data /home/fleet

COPY --from=build /out /app
COPY ansible /opt/fleet/ansible
RUN chmod -R a+rX /opt/fleet /app

ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=0 \
    HOME=/home/fleet \
    Fleet__Data__Directory=/data \
    Fleet__Secrets__Directory=/run/secrets \
    Fleet__Provisioning__AnsibleDirectory=/opt/fleet/ansible \
    Fleet__Provisioning__ScratchDirectory=/tmp/fleet

USER 10020:10020
WORKDIR /app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD ["dotnet", "/app/Fleet.Manager.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "/app/Fleet.Manager.dll"]
