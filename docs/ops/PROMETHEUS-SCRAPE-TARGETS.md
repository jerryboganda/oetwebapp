# Prometheus scrape targets and helper-node metrics

Status: design and contract, 2026-10-06. Nothing described under "Proposed" exists in the repository yet.
Read `ops/README.md` first: no Prometheus, Grafana, Alertmanager, node-exporter or cAdvisor runs from any
compose file here, and the API emits no Prometheus metrics. This page says what to scrape, where it must run,
and, for enrolled helper VPSs, **which transport carries their metrics**.

## 1. Decision: helper metrics are agent-pushed, not scraped

The owner-approved fleet design (the remote-worker protocol OET-RWP/1, decisions D1 to D3 and sections 1.2, 1.3
and 10.1) fixes three facts about a helper:

- it accepts **no inbound connection** except SSH from the manager on the primary, and it makes **one outbound
  connection destination**, the API host over HTTPS (H7); there is no overlay network and no WireGuard (D3);
- its container is read-only, runs as a non-root user, and has **no docker socket, no host mounts, no extra
  capabilities** (H6);
- no job content, file name or learner identifier may appear in any log, exception message or **metric label**
  (H3).

A scraped exporter on the helper breaks every one of those: node-exporter needs host mounts and a listening
port, cAdvisor needs the docker socket and host mounts, and a scrape from the primary needs an inbound port
reachable over the public internet (or a tunnel the design forbids). So:

> **Helpers run no node-exporter and no cAdvisor. Their metrics travel inside the agent's node heartbeat to the
> API, and Prometheus reads them from the primary.**

The path:

```
agent --POST /v1/internal/remote-worker/workers/heartbeat (every 15 s, HTTPS, node token)--> API
API   stores capacity / load / leases / state / agent on the RemoteWorkers row
manager (oet-fleet, loopback only) --GET /v1/internal/fleet/nodes and /stats (service credential)--> API
Prometheus (primary) --scrape loopback or internal network--> a /metrics rendering of that node list
```

Everything the heartbeat carries (protocol section 4.7): `state`, `agent.{version, imageDigest, protocol,
clockSkewMs}`, `capacity.{cpuCoresTotal, cpuBudgetMilli, cpuBudgetFreeMilli, memTotalMiB, memAvailableMiB,
memBudgetMiB, memBudgetFreeMiB, tmpBudgetMiB, tmpFreeMiB, diskFreeMiB, heavySlotsTotal, heavySlotsFree,
configuredConcurrency, effectiveConcurrency}`, `load.{cpuPct, cpuPct15s, memFreePct, load1, pressure}` and the
list of held leases. That is already the node-exporter-and-cAdvisor subset that capacity planning needs, at a
15 second resolution, host-wide.

### Proposed series (contract for the exposition; not emitted today)

The exposition is a deliverable of the fleet manager track (a loopback `/metrics` on `oet-fleet`, rendering
only data the manager already reads from the API, so no new data leaves the helper). Names, labels and units
below are the contract; verify them against that deliverable at integration.

| Series | Type | Source field | Labels (allowed) |
| --- | --- | --- | --- |
| `oetfleet_node_up` | gauge 0/1 | health is `Online` (heartbeat inside 45 s) | `node` (the `nodeRef`), `region`, `provider` |
| `oetfleet_node_status` | gauge, one-hot | node `status` (Pending, Probation, Active, Draining, Disabled, Quarantined, Revoked: the seven statuses of OET-RWP/1 section 3.9, one-hot so a revoked node stays visible; check the list against the protocol at integration) | `node`, `status` |
| `oetfleet_node_last_heartbeat_age_seconds` | gauge | now minus `lastHeartbeatAt` | `node` |
| `oetfleet_node_cpu_percent` | gauge | `load.cpuPct` (host-wide) | `node` |
| `oetfleet_node_mem_free_percent` | gauge | `load.memFreePct` | `node` |
| `oetfleet_node_load1` | gauge | `load.load1` | `node` |
| `oetfleet_node_pressure_reduced` | gauge 0/1 | `load.pressure == reduced` | `node` |
| `oetfleet_node_effective_concurrency` | gauge | `capacity.effectiveConcurrency` | `node` |
| `oetfleet_node_heavy_slots_free` | gauge | `capacity.heavySlotsFree` | `node` |
| `oetfleet_node_leases` | gauge | `leases.count` | `node` |
| `oetfleet_node_tmp_free_mib` | gauge | `capacity.tmpFreeMiB` | `node` |
| `oetfleet_node_disk_free_mib` | gauge | `capacity.diskFreeMiB` | `node` |
| `oetfleet_node_clock_skew_ms` | gauge | `agent.clockSkewMs` | `node` |
| `oetfleet_node_agent_info` | gauge 1 | `agent.version`, `agent.protocol`, `agent.imageDigest` | `node`, `version`, `protocol`, `digest` |
| `oetfleet_jobs` | gauge | `GET /v1/internal/fleet/stats` queue depth by state | `kind`, `state` |
| `oetfleet_oldest_queued_age_seconds` | gauge | stats oldest `Queued` age | `kind` |
| `oetfleet_node_canary_ok` | gauge 0/1 | `lastCanary.ok` | `node` |

Label rules (H3, A2): only the node reference, region, provider, job kind and state. Never a job id, resource
id, asset id, file name, learner id or any text taken from a job. Cardinality is bounded by the number of nodes
times a handful of states.

### Proposed alerts (write them as rules only when the series exist)

| Alert | Condition | Why |
| --- | --- | --- |
| Helper stale | `oetfleet_node_last_heartbeat_age_seconds > 45` for 1 m | the API stops assigning at 45 s (3 missed heartbeats) |
| Helper offline | age `> 600` | marked Offline by the protocol; leases are already requeued |
| Helper under pressure | `oetfleet_node_pressure_reduced == 1` for 5 m | the agent has cut its own concurrency |
| Helper has no capacity | `oetfleet_node_heavy_slots_free == 0` and `oetfleet_jobs{state="Queued"} > 0` for 10 m | work is waiting while the fleet is full |
| Queue backlog | `oetfleet_oldest_queued_age_seconds > 600` | the protocol falls back to local extraction at 10 minutes; this should page before that is the norm |
| Canary failing | `oetfleet_node_canary_ok == 0` | a node returned wrong output; it should already be quarantined |
| Unapproved image | `oetfleet_node_agent_info` digest not in the approved list | the agent stops claiming work |

No alert-rule file for these ships with this change: a rule over a metric nothing emits is exactly the defect
that removed the Speaking dashboards.

## 2. Targets on the primary VPS (proposed)

The primary is small and shared: about 6 CPU, about 11 GiB RAM and 60+ co-tenant containers
(`docs/ops/production-compute-offload.md`, snapshot of 2026-08-08; not re-verified). Monitoring must be lean and
must not hold learner data. The precedent for a side stack is `docker-compose.agent-console.yml`: its own
compose project, joined to the external network `oetwebsite_internal`, rolled out by its own dispatch workflow.
An `oet-monitoring` project would follow it. Anything that identifies "OET" containers must use the name prefix
`^/?(oet-|oetwebsite)` (`agent-console/CONTRACT.md`); co-tenants are everything else and must not be scraped.

| Job | Target | Auth | Exists today | Notes |
| --- | --- | --- | --- | --- |
| `node` | node-exporter on the host, bound to `127.0.0.1` | none (loopback) | no | host CPU, memory, disk, load, network. Needs `--path.rootfs` mounts, so it is a host-side service, never in a learner-facing container. |
| `cadvisor` | cAdvisor, bound to loopback | none (loopback) | no | per-container CPU / memory for the OET services (api blue/green, ai-worker, web, postgres, clamav, router). Run with `--docker_only` and most metric groups disabled; filter to `container_label_com_docker_compose_project="oetwebsite"` or the name prefix above. |
| `agent-gateway` | `oet-agent-gateway:8305/v1/metrics` over `oetwebsite_internal` | `x-oet-internal-token` header or `Authorization: Bearer` (from a file; never in the config) | yes | the only exporter in the repo (`oetgw_*`, rules in `ops/prometheus/alerts-oet-gateway.yml`). `/v1/healthz` is exempt from the token. |
| `postgres` | postgres-exporter, internal network only | a dedicated read-only role | no | `pg_stat_statements` is already enabled in the compose command; connection counts by `application_name` need the `Application Name` connection-string setting planned in the measurement work (not part of this change). |
| `fleet` | `oet-fleet` manager `/metrics`, loopback | none (loopback) | no | the series in section 1. The manager has no public ingress (decision D1), so Prometheus must run on the primary or reach it through an SSH tunnel. |
| `api-ops` | `GET /v1/admin/ops/snapshot` (admin JSON) | admin session | no (proposed by the measurement track) | JSON, not Prometheus. Use `json_exporter` or fold the gauges into a real `/metrics` before relying on it. |

If Prometheus itself is too heavy for the primary, run Prometheus and Grafana on a separate small host and
have it scrape over an SSH tunnel or the same loopback endpoints; do not expose any of the exporters publicly.
Remote-write to a hosted service is possible, but it moves operational metrics (not learner data) off the
host: that is an owner decision, not made here.

### Example scrape configuration (documentation only; nothing here is deployed)

```yaml
global:
  scrape_interval: 30s        # helper heartbeats are 15 s; 30 s is enough for capacity planning
  scrape_timeout: 10s
scrape_configs:
  - job_name: node
    static_configs: [{ targets: ['127.0.0.1:9100'] }]
  - job_name: cadvisor
    static_configs: [{ targets: ['127.0.0.1:8081'] }]
  - job_name: agent-gateway
    metrics_path: /v1/metrics
    authorization:
      type: Bearer
      credentials_file: /etc/prometheus/secrets/gateway-token   # mode 0400, not in the repository
    static_configs: [{ targets: ['oet-agent-gateway:8305'] }]
  - job_name: fleet
    static_configs: [{ targets: ['127.0.0.1:9201'] }]           # proposed oet-fleet loopback /metrics
rule_files:
  - /etc/prometheus/rules/alerts-oet-gateway.yml                # from ops/prometheus/
```

## 3. Load-test metrics

The k6 harness (`tests/load/fleet-1000.k6.js`) is a manual tool the owner runs from a self-provisioned load
generator (no CI runs it) and does not push to Prometheus. Each leg writes an `oet-load-summary/1` JSON through
`handleSummary`, and `tests/load/report/k6-load-report.mjs` merges the legs into the markdown report (see
`docs/ops/LOAD-TESTING.md`). To watch a long run live, k6's experimental Prometheus remote-write output can be
enabled on a leg (`-o experimental-prometheus-rw`, with the target Prometheus started with its remote-write
receiver); that is an operator convenience and is not wired into anything.
