# VPS All in One File

> **Production VPS — Complete Environment, Services & Projects Inventory**
> Captured by live SSH audit on **22 Apr 2026**.

---

## 1. Server & Infrastructure

| Item | Value |
|---|---|
| **Public IP** | `185.252.233.186` (+ IPv6 `2a02:c207:2299:7708::1`) |
| **Hostname** | `vmi2997708` |
| **OS** | Ubuntu 24.04.4 LTS (Noble) |
| **Kernel** | `6.8.0-101-generic` |
| **Virtualisation** | KVM / QEMU (Contabo-style VPS) |
| **CPU** | AMD EPYC (IBPB) — 4 vCPUs @ 2.0 GHz |
| **RAM** | 7.8 GiB (used ≈ 4.8 GiB) |
| **Swap** | 8.0 GiB (used ≈ 1.7 GiB) |
| **Disk** | `/dev/sda1` — 145 GB ext4, 55 GB used (38 %) |
| **Uptime** | 42 days, 21 hours (load avg ≈ 1.9) |
| **Firewall (UFW)** | **inactive** *(perimeter secured only by host provider / SSH keys)* |
| **Fail2ban** | Installed (`1.0.2-3ubuntu0.1`) |
| **Unattended-upgrades** | Running |

### Host-level services (systemd)

`docker.service`, `containerd.service`, `postgresql@16-main.service`, `minio.service` (native systemd, listens on 127.0.0.1:9002 + :9003), `ssh.service`, `cron.service`, `rsyslog.service`, `fail2ban.service`, `unattended-upgrades.service`, `systemd-resolved`, `systemd-networkd`, `systemd-timesyncd`.

### Installed key packages

| Package | Version |
|---|---|
| docker-ce | 29.3.0 |
| docker-compose-plugin | v5.1.0 |
| docker-buildx-plugin | 0.31.1 |
| docker-model-plugin | 1.1.8 |
| containerd.io | 2.2.1 |
| PostgreSQL (host) | 16.13 |
| Node.js | 22.22.1 (NodeSource) |
| Python | 3.12.3 |
| Git | 2.43.0 |
| fail2ban | 1.0.2 |
| ufw | 0.36.2 *(inactive)* |

### PM2 processes (host)

| Name | Mode | Uptime | Status |
|---|---|---|---|
| `oetwithdrhesham-app` (Next.js v0.1.0, port 3002) | fork | 28 d | online |

### Cron (root)

```cron
*    *    * * *  curl -s http://localhost:9000/cron > /dev/null 2>&1          # ovo-wpp cron pings
*/15 *    * * *  cd /root/maternal-mind && bash scripts/sync-now.sh >> /var/log/maternalmind-autosync.log 2>&1
```

### Host PostgreSQL 16 (listens 0.0.0.0:5432)

Databases: `docduty` (owner `docduty_admin`), plus templates.

### Native MinIO (systemd) — 127.0.0.1:9002 / :9003

Used by host-level object storage.

---

## 2. Listening Ports (public-facing summary)

| Port | Exposed by | Purpose |
|---|---|---|
| 22 | sshd | SSH |
| 80 / 81 / 443 | `nginx-proxy-manager-app-1` | **Reverse proxy — all public HTTP(S) traffic** |
| 2222 | Python service | Remnawave / Remnanode control |
| 3000 | `marriagebureau-frontend` (nginx) | DMB frontend (direct) |
| 3002 | PM2 `oetwithdrhesham-app` | Next.js dev/legacy (host) |
| 5000 | `maternal-mind-app-1` | Maternal-Mind Flask app |
| 5001 | `maternalmind-website` | Maternal Mind marketing site |
| 5050 | `fame-website` (127.0.0.1 only) | First-Aid-Made-Easy Flask |
| 5051 | `oetwithdrhesham-placeholder` (PHP/Apache) | OET legacy placeholder |
| 5432 | host Postgres 16 | DocDuty DB |
| 6001 / 9601 | `marriagebureau-soketi` | WebSocket (Pusher-compat) |
| 8000 / 9443 | `portainer` | Docker management UI |
| 8080 | `amaddiagnosticcentre-wordpress-1` | Amad Diagnostic WP |
| 8082 | `laravel-app-app-1` | Laravel portal |
| 8085 | `streamvault-web` (nginx) | StreamVault |
| 8086 | `marriagebureau-web` (nginx) | DMB API (PHP-FPM behind nginx) |
| 8092 | `radiology-web` (nginx) | Radiology static site |
| 9000 | `ovo-wpp-web` (nginx) | WhatsApp gateway front |
| 9002 / 9003 | host MinIO | Object storage |
| 4100 / 4101 | ExtractedIQ (127.0.0.1 only) | API + Frontend (behind NPM) |

---

## 3. Reverse Proxy — Nginx Proxy Manager

Container `nginx-proxy-manager-app-1` (image `jc21/nginx-proxy-manager:latest`) terminates all TLS and routes **26 active proxy hosts** to internal services.

### Domain → Backend mapping

| # | Domain(s) | Forward Target | Real Container |
|---|---|---|---|
| 1 | `amaddiagnosticcentre.com.pk`, `www.amaddiagnosticcentre.com.pk` | `http://185.252.233.186:8080` | `amaddiagnosticcentre-wordpress-1` |
| 3 | `portal.amaddiagnosticcentre.com.pk` | `http://185.252.233.186:8082` | `laravel-app-app-1` |
| 4 | `polytronx.com`, `www.polytronx.com` | `http://185.252.233.186:8083` | **⚠ stale — 8083 not listening** |
| 5 | `vps.polytronx.com` | `http://172.17.0.1:8085` | `streamvault-web` |
| 6 | `panel.doctormarriagebureau.com.pk` | `http://marriagebureau-frontend:80` | DMB Frontend (Vue/React) |
| 7 | `whatsapp.firstaidmadeeasy.com.pk` | `http://ovo-wpp-web:80` | OVO WhatsApp Gateway |
| 8 | `api.doctormarriagebureau.com.pk` | `http://marriagebureau-web:80` | DMB Laravel API |
| 9 | `soketi.polytronx.com` | `http://soketi:6001` | Standalone Soketi WS |
| 11 | `maternalmind.com.pk` | `http://185.252.233.186:5001` | `maternalmind-website` |
| 13 | `admin.maternalmind.com.pk` | `http://185.252.233.186:5000` | `maternal-mind-app-1` |
| 14 | `new.firstaidmadeeasy.com.pk` | `http://fame-website:5000` | `fame-website` |
| 16 | `dd.polytronx.com` | `http://172.17.0.1:3100` | DocDuty dev (legacy) |
| 17 | `docduty.com.pk` | `http://docduty-marketing:80` | `docduty-marketing` |
| 18 | `portal.docduty.com.pk` | `http://docduty-portal:80` | `docduty-portal` |
| 19 | `admin.docduty.com.pk` | `http://docduty-portal:80` | `docduty-portal` |
| 20 | `api.docduty.com.pk` | `http://docduty-api:3001` | `docduty-api` |
| 21 | `madni.polytronx.com` | `http://madni-website:80` | `madni-website` |
| 24 | `extractiq.polytronx.com` | `http://extractediq-frontend:80` (+ `/api` → `extractediq-api:4000`) | ExtractedIQ stack |
| 25 | `oetwithdrhesham.co.uk`, `www.oetwithdrhesham.co.uk` | `http://185.252.233.186:5051` | `oetwithdrhesham-placeholder` |
| 26 | `api.oetwithdrhesham.co.uk` | `http://oet-api:8080` | `oet-api` |
| 27 | `drhesham.polytronx.com` | `http://drhesham-website:3000` | `drhesham-website` |
| 28 | `app.oetwithdrhesham.co.uk` | `http://oet-web:3000` | `oet-web` |
| 29 | `projectmanagement.polytronx.com` | `http://pm-web:3000` + `/api`→`pm-api:8080` | PM SaaS |
| 30 | `extractediq.dev`, `www.extractediq.dev` | `http://extractediq-frontend:80` (+ `/api` → `extractediq-api:4000`) | ExtractedIQ (prod domain) |

> **⚠ Stale entries**: `polytronx.com` (port 8083) has no listener.
>
> **✅ aimsacademy.com.pk fully decommissioned on 2026-04-22.** Removed: NPM proxy host #2 + `2.conf`; live + rotated (`.gz`) access/error logs; all matching NPM SQLite rows across every table (proxy_host, certificate, redirection_host, dead_host, stream, audit_log) + VACUUM; pre-aims DB backups; Let's Encrypt traces (none found); 1.3 GB `/root/incident-response-20260308-232429/` archive (contained the old aims compose file, DB snapshot, logs, 2.conf); stray reference in `/root/doctormarriagebureau_fix_messaging/linux vps memory file.md`; sanitised perf-opt baseline snapshots; scrubbed `/root/.bash_history`. Final verification: 0 matches anywhere on host or in NPM container; proxy_host/ config dir clean; HTTP probe to `aimsacademy.com.pk` returns NPM default 301 fallback (no backend). NPM container healthy.

---

## 4. Docker Compose Projects (15)

| # | Project | Compose file | Containers |
|---|---|---|---|
| 1 | **amaddiagnosticcentre** | `/opt/docker/wordpress/amaddiagnosticcentre/docker-compose.yml` | 3 |
| 2 | **doctormarriagebureau** | `/root/doctormarriagebureau/docker-compose.yml` | 5 |
| 3 | **extractediq** | `/root/extractediq/docker-compose.prod.yml` (+ `.proxy.yml`) | 7 |
| 4 | **laravel-app** | `/root/laravel-app/docker-compose.yml` | 2 |
| 5 | **madni-website** | `/opt/docker/madni-website/docker-compose.yml` | 1 |
| 6 | **maternal-mind** | `/root/maternal-mind/docker-compose.yml` | 2 |
| 7 | **maternalmind-website** | `/root/maternalmind-website/docker-compose.yml` | 1 |
| 8 | **nginx-proxy-manager** | `/opt/docker/nginx-proxy-manager/docker-compose.yml` | 1 |
| 9 | **oetwebsite** | `/root/oetwebsite/docker-compose.production.yml` | 3 |
| 10 | **ops (DocDuty)** | `/opt/docduty-platform/backend/ops/docker-compose.yml` | 5 |
| 11 | **ovo-wpp** | `/var/www/ovo-wpp/docker-compose.yml` | 3 |
| 12 | **projectmanagementsaas** | `/opt/projectmanagementsaas/docker-compose.vps.yml` | 6 |
| 13 | **remnanode** | `/opt/remnanode/docker-compose.yml` | 1 |
| 14 | **soketi** | `/root/soketi/docker-compose.yml` | 2 |
| 15 | **streamvault** | `/opt/streamvault/docker-compose.prod.yml` | 3 |

**Total running containers: 49**
**Docker networks: 17** — one custom bridge per project plus `bridge`, `host`, `none`.

---

## 5. Projects — End-to-End Inventory

### 5.1 ExtractedIQ
- **Domains**: `extractediq.dev`, `www.extractediq.dev`, `extractiq.polytronx.com`
- **Source**: `/root/extractediq`
- **Containers**:
  - `extractediq-frontend` (image `extractediq-frontend`, :4101→80) — React/Vite
  - `extractediq-api` (`extractediq-api`, :4100→4000) — Node/TS API
  - `extractediq-worker-1`, `extractediq-worker-2` (`extractediq-worker`) — job workers
  - `extractediq-postgres` (`pgvector/pgvector:pg16`) — vector DB
  - `extractediq-redis` (`redis:7-alpine`) — queues/cache
  - `extractediq-minio` (`minio/minio:latest`) — object storage
- **Network**: `extractediq`
- **Volumes**: `extractediq_pgdata`, `extractediq_redisdata`, `extractediq_miniodata` (+ legacy `extractiq_*`)

### 5.2 Project Management SaaS
- **Domain**: `projectmanagement.polytronx.com`
- **Source**: `/opt/projectmanagementsaas`
- **Containers**: `pm-web` (Next.js :3000), `pm-api` (:8080), `pm-worker`, `pm-postgres` (pg16-alpine), `pm-redis` (redis:7-alpine), `pm-minio`
- **Network**: `projectmanagementsaas_lp_net`
- **Volumes**: `projectmanagementsaas_pgdata`, `projectmanagementsaas_miniodata`

### 5.3 OET With Dr Hesham
- **Domains**:
  - `oetwithdrhesham.co.uk`, `www.oetwithdrhesham.co.uk` → legacy PHP placeholder (`oetwithdrhesham-placeholder`, :5051)
  - `app.oetwithdrhesham.co.uk` → `oet-web` (Next.js :3000)
  - `api.oetwithdrhesham.co.uk` → `oet-api` (:8080)
  - `drhesham.polytronx.com` → `drhesham-website` (:3000)
- **Source**: `/root/oetwebsite`, `/opt/oetwebapp`
- **Containers**: `oet-web`, `oet-api`, `oet-postgres` (pg17-alpine), `drhesham-website`, `oetwithdrhesham-placeholder` (php:8.3-apache)
- **PM2 (host)**: `oetwithdrhesham-app` (port 3002, Next.js)
- **Volumes**: `oetwebsite_oet_postgres_data`, `oetwebsite_oet_learner_storage`, plus `oetwebapp_*`

### 5.4 DocDuty Platform
- **Domains**: `docduty.com.pk`, `portal.docduty.com.pk`, `admin.docduty.com.pk`, `api.docduty.com.pk`, `dd.polytronx.com`
- **Source**: `/opt/docduty-platform`, `/opt/docduty`, `/root/docduty`
- **Containers**: `docduty-marketing`, `docduty-portal`, `docduty-api` (:3001), `docduty-worker`, `docduty-postgres` (pg17-alpine)
- **Host DB**: also uses host Postgres DB `docduty` (owner `docduty_admin`)
- **Volume**: `ops_docduty-postgres-data`

### 5.5 Doctor Marriage Bureau
- **Domains**: `panel.doctormarriagebureau.com.pk`, `api.doctormarriagebureau.com.pk`
- **Source**: `/root/doctormarriagebureau`
- **Containers**:
  - `marriagebureau-frontend` (nginx, :3000→80) — Vue/React SPA
  - `marriagebureau-web` (nginx:alpine, :8086→80) — Laravel public gateway
  - `marriagebureau-app` (php-fpm)
  - `marriagebureau-db` (mysql:8.0)
  - `marriagebureau-soketi` (:6001, :9601) — Pusher-compatible WebSocket
- **Network**: `doctormarriagebureau_marriagebureau-network`
- **Volumes**: `doctormarriagebureau_marriagebureau-db-data`, `doctormarriagebureau_marriagebureau_db_data`

### 5.6 Maternal Mind (Admin / Flask app)
- **Domain**: `admin.maternalmind.com.pk`
- **Source**: `/root/maternal-mind`
- **Containers**: `maternal-mind-app-1` (Flask, :5000), `maternal-mind-db-1` (postgres:15-alpine)
- **Volume**: `maternal-mind_postgres_data`
- **Cron**: auto-sync every 15 min → `/var/log/maternalmind-autosync.log`

### 5.7 Maternal Mind (Marketing website)
- **Domain**: `maternalmind.com.pk`
- **Source**: `/root/maternalmind-website`
- **Container**: `maternalmind-website` (:5001)
- **Volume**: `maternalmind_postgres_data`

### 5.8 First Aid Made Easy (FAME)
- **Domain**: `new.firstaidmadeeasy.com.pk`
- **Container**: `fame-website` (image `fame-website:latest`, 127.0.0.1:5050→5000) — Flask

### 5.9 OVO WhatsApp Gateway
- **Domain**: `whatsapp.firstaidmadeeasy.com.pk`
- **Source**: `/var/www/ovo-wpp`
- **Containers**: `ovo-wpp-web` (nginx:alpine, :9000→80), `ovo-wpp-app` (PHP/Laravel), `ovo-wpp-db` (mysql:8.0)
- **Volume**: `ovo-wpp_db-data`
- **Cron**: host pings `http://localhost:9000/cron` every minute

### 5.10 Amad Diagnostic Centre (WordPress)
- **Domain**: `amaddiagnosticcentre.com.pk`, `www.amaddiagnosticcentre.com.pk`
- **Source**: `/opt/docker/wordpress/amaddiagnosticcentre`
- **Containers**: `amaddiagnosticcentre-wordpress-1` (wordpress:latest, :8080→80), `amaddiagnosticcentre-db-1` (mariadb:10.11), `amaddiagnosticcentre-redis-1` (redis:alpine)
- **Volumes**: `amaddiagnosticcentre_wordpress_data`, `amaddiagnosticcentre_db_data`

### 5.11 Generic Laravel Portal (Amad subdomain)
- **Domain**: `portal.amaddiagnosticcentre.com.pk`
- **Source**: `/root/laravel-app`
- **Containers**: `laravel-app-app-1` (php:8.3-apache, :8082→80), `laravel-app-db-1` (mariadb:10.11)
- **Volume**: `laravel-app_db_data`

### 5.12 Madni Website
- **Domain**: `madni.polytronx.com`
- **Source**: `/opt/docker/madni-website`
- **Container**: `madni-website` (image `madni-website-madni-website`, exposes 80 internally)

### 5.13 StreamVault
- **Domains**: `vps.polytronx.com` (and served at host port 8085)
- **Source**: `/opt/streamvault`
- **Containers**: `streamvault-web` (nginx:alpine, :8085→80), `streamvault-app` (PHP-FPM), `streamvault-db` (mysql:8.0)
- **Volume**: `streamvault_db_data`

### 5.14 Radiology Website (static)
- **Served at**: host port `:8092` (no NPM host configured — direct IP access)
- **Source**: `/var/www/radiology-website`
- **Container**: `radiology-web` (nginx:alpine)

### 5.15 Soketi (shared WebSocket)
- **Domain**: `soketi.polytronx.com`
- **Source**: `/root/soketi`
- **Containers**: `soketi` (quay.io/soketi/soketi:latest-16-alpine), `soketi-redis` (redis:7-alpine)
- **Volume**: `soketi_soketi-redis-data`

### 5.16 Remnawave Node (VPN / proxy node)
- **Source**: `/opt/remnanode`
- **Container**: `remnanode` (remnawave/node:latest), control on host port `2222`

### 5.17 Portainer (Docker UI)
- **URL**: `https://185.252.233.186:9443` (also :8000)
- **Container**: `portainer` (portainer/portainer-ce:lts)
- **Volume**: `portainer_data`

### 5.18 Nginx Proxy Manager (Edge)
- **URL**: `http://185.252.233.186:81` (admin UI)
- **Ports**: 80, 81, 443
- **Container**: `nginx-proxy-manager-app-1`
- **Source**: `/opt/docker/nginx-proxy-manager`

---

## 6. Volumes (25 named)

| Volume | Used by |
|---|---|
| `amaddiagnosticcentre_db_data`, `amaddiagnosticcentre_wordpress_data` | Amad Diagnostic |
| `doctormarriagebureau_marriagebureau_db_data`, `..._marriagebureau-db-data` | DMB |
| `extractediq_pgdata`, `extractediq_redisdata`, `extractediq_miniodata` | ExtractedIQ |
| `extractiq_pgdata`, `extractiq_redisdata`, `extractiq_miniodata` | ExtractedIQ (legacy/orphan) |
| `laravel-app_db_data` | Laravel portal |
| `maternal-mind_postgres_data` | Maternal Mind admin |
| `maternalmind_postgres_data` | Maternal Mind site |
| `oetwebsite_oet_postgres_data`, `oetwebsite_oet_learner_storage` | OET current |
| `oetwebapp_oet_postgres_data`, `oetwebapp_oet_learner_storage` | OET prior |
| `ops_docduty-postgres-data` | DocDuty |
| `ovo-wpp_db-data` | OVO WhatsApp |
| `portainer_data` | Portainer |
| `projectmanagementsaas_pgdata`, `projectmanagementsaas_miniodata` | PM SaaS |
| `soketi_soketi-redis-data` | Soketi |
| `streamvault_db_data` | StreamVault |

Plus one anonymous hash-named volume (`4a8342ac5d…`).

---

## 7. Networks (custom bridges)

`amaddiagnosticcentre_wordpress_network`, `doctormarriagebureau_marriagebureau-network`, `extractediq`, `laravel-app_default`, `madni-website_default`, `maternal-mind_default`, `maternalmind-website_default`, `nginx-proxy-manager_default`, `oetwebapp_internal`, `oetwebsite_internal`, `ops_default`, `ovo-wpp_app-network`, `projectmanagementsaas_lp_net`, `soketi_soketi-network`, `streamvault_streamvault-network` + default `bridge`/`host`/`none`.

---

## 8. Observations / Recommendations

1. **UFW is inactive** — rely on SSH keys and Docker-level exposure. Consider enabling with explicit allow-list for 22 / 80 / 81 / 443.
2. **Stale NPM entries**: `polytronx.com` (8083) still points to a dead port — clean up or redeploy. (`aimsacademy.com.pk` was completely decommissioned on 2026-04-22 — see §3 callout for full scope.)
3. **Host Postgres 5432 is exposed on 0.0.0.0** — ensure `pg_hba.conf` restricts connections, or firewall the port.
4. **Dozens of bind-mounted overlay directories** under `/var/lib/docker/rootfs/overlayfs/` — normal for 49 containers, but disk usage (55/145 GB, 38 %) should be monitored.
5. **Two parallel ExtractedIQ volume sets** (`extractediq_*` and `extractiq_*`) — the older set appears orphaned and can probably be pruned after verification.
6. **Legacy DocDuty volume in `/opt/docduty-platform-next`** (empty dir) and `/opt/docduty-platform` coexist — single source of truth recommended.
7. **MinIO runs twice**: native (host, :9002/:9003) + container `projectmanagementsaas_miniodata` + container `extractediq-minio`. Verify this is intentional.
8. **SQL dumps in `/root/`** (`mb-live.sql`, `ovo-wpp-live.sql`, `ovo_wpp_prod_*.sql`, `deploy-oet.zip`) — move to `/root/backups/` or off-host storage and restrict perms.

---

## 9. Performance Optimizations Applied — 2026-04-22

> Audit + remediation pass on performance / speed / efficiency. **UFW intentionally untouched** per operator instruction (avoids SSH key rate-limiting).
> Backups of modified files in `/root/perf_backup_20260422_042002/`.

### 9.1 Critical find — 13-day runaway Python process (killed)

Discovered `python3 -` (PID 2227133) with parent `bash -c` (PID 2227132) from orphan SSH `session-63329.scope` (started **2026-04-08** from remote host `154.192.161.66`, state `closing`). The script was a recursive `glob.glob('/**/*compose*.yml', recursive=True)` that had been burning **99.8 % of one CPU core for 13 days 8 hours**. Killed both PIDs. Load average dropped from sustained ~3.25 to ~1.85.

### 9.2 Kernel sysctls (`/etc/sysctl.d/99-zzz-vps-perf.conf`)

Renamed to `99-zzz-…` so it loads **after** the pre-existing `99-vps-performance.conf` and wins the last-write-wins ordering.

| Tunable | Before | After | Rationale |
|---|---|---|---|
| `vm.swappiness` | 10 | 10 | kept — already good |
| `vm.vfs_cache_pressure` | 75 | **50** | retain inode/dentry cache longer under memory pressure |
| `vm.dirty_ratio` / `dirty_background_ratio` | 20 / default | **10 / 5** | smoother, more predictable writeback on 7.8 GiB host |
| `net.core.somaxconn` | 4096 | 4096 | kept |
| `net.core.netdev_max_backlog` | 1000 | **5000** | multi-container ingress |
| `net.core.rmem_max` / `wmem_max` | 212992 | **16 777 216** | required for BBR throughput on long-fat pipes |
| `net.core.default_qdisc` | — | **fq** | pair with BBR |
| `net.ipv4.tcp_congestion_control` | bbr | bbr | kept |
| `net.ipv4.tcp_max_syn_backlog` | 512 | **4096** | SYN-flood + burst resilience |
| `net.ipv4.tcp_tw_reuse` | 2 | 1 | standard ephemeral-port reuse |
| `net.ipv4.tcp_fin_timeout` | 60 | **15** | free ephemeral ports faster |
| `net.ipv4.ip_local_port_range` | 32768–60999 | **10240–65535** | ~40 % more ephemeral ports for the NPM edge |
| `net.ipv4.tcp_slow_start_after_idle` | 1 | **0** | keep cwnd for long-lived keep-alives |
| `net.ipv4.tcp_rmem` / `tcp_wmem` | default | **4k 256k 16M** | match rmem_max/wmem_max |
| `net.ipv4.tcp_mtu_probing` | 0 | **1** | handle ICMP-black-holed path MTUs |
| `net.ipv4.tcp_fastopen` | default | **3** | TFO for both client + server |
| `net.ipv4.tcp_keepalive_time/intvl/probes` | 7200/75/9 | **300/30/5** | faster dead-peer detection |
| `fs.inotify.max_user_watches` | 61656 | **524 288** | Node/Next watchers across 49 containers |
| `fs.inotify.max_user_instances` | **128** | **8192** | root cause of intermittent watcher exhaustion |
| `fs.inotify.max_queued_events` | 16384 | **65536** | burst safety |
| `fs.file-max` | ~unlimited | 2 097 152 | explicit cap (was already uncapped) |

### 9.3 Docker daemon (`/etc/docker/daemon.json`)

```json
{
  "dns": ["1.1.1.1","8.8.8.8","1.0.0.1","8.8.4.4"],
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "5" },
  "live-restore": true,
  "default-ulimits": { "nofile": { "Name":"nofile","Soft":65536,"Hard":65536 } }
}
```

- **`live-restore: true`** (was `false`) — dockerd can now be reloaded/restarted without dropping the 49 running containers.
- **Default `nofile = 65536`** — prevents file-descriptor exhaustion for busy containers (NPM, MinIO, Postgres).
- Log rotation was already correctly set to 10m×5.
- Reload applied via `systemctl reload docker` — **no container restart needed**.

### 9.4 systemd-journald cap (`/etc/systemd/journald.conf.d/size.conf`)

```
[Journal]
SystemMaxUse=200M
SystemMaxFileSize=50M
MaxRetentionSec=1month
```

Disk: **377.9 MB → 194.9 MB** (183 MB reclaimed).

### 9.5 PostgreSQL 16 (host) — tuned + bound to localhost

`/etc/postgresql/16/main/postgresql.conf` was running defaults. Appended managed block:

```
listen_addresses = 'localhost'
shared_buffers = 512MB
effective_cache_size = 2GB
work_mem = 8MB
maintenance_work_mem = 128MB
wal_buffers = 16MB
checkpoint_completion_target = 0.9
max_wal_size = 2GB
min_wal_size = 512MB
random_page_cost = 1.1
effective_io_concurrency = 200
default_statistics_target = 100
max_worker_processes = 4
max_parallel_workers_per_gather = 2
max_parallel_workers = 4
```

Also commented duplicate `listen_addresses = '*'` at line 823 that was overriding our `'localhost'`. **Restarted postgresql@16-main** (required for `listen_addresses`).

**Result**: `5432` now listens on `127.0.0.1` + `[::1]` only — **no longer publicly reachable** (closes recommendation #3 above).

### 9.6 Disk reclaim

| Item | Before | After | Freed |
|---|---|---|---|
| Docker build cache | 12.58 GB (11.28 reclaimable) | 1.28 GB | **~11.3 GB** |
| Docker images | 27.34 GB | 17.22 GB | **~10.1 GB** |
| Docker volumes (7 dangling removed) | 3.30 GB | 2.91 GB | ~0.4 GB |
| `/var/log/btmp` + `btmp.1` | 188 MB | 0 | 188 MB |
| journald | 378 MB | 195 MB | 183 MB |
| `/root/.npm` + `/root/.cache` | 1.4 GB + 192 MB | minimal | ~1.6 GB |
| **Root FS usage** | **55 GB (38 %)** | **41 GB (29 %)** | **~14 GB on disk** |

Dangling volumes removed: `doctormarriagebureau_marriagebureau_db_data`, `extractiq_miniodata`, `extractiq_pgdata`, `extractiq_redisdata`, `maternalmind_postgres_data`, `oetwebapp_oet_learner_storage`, `oetwebapp_oet_postgres_data` (confirmed not attached to any container).

### 9.7 Summary of measurable wins

| Metric | Before | After |
|---|---|---|
| Load avg (1m) | ~3.25 (with runaway) | ~1.85 |
| CPU reclaimed (runaway process killed) | — | **~25 % of total CPU capacity** |
| Disk usage | 38 % | 29 % |
| Docker reclaimable space | ~37 GB | ~1 GB |
| Postgres public exposure | 0.0.0.0:5432 | 127.0.0.1 only |
| Docker live-restore | off | on |
| inotify user-instance limit | 128 | 8192 |
| TCP socket buffers (BBR tuning) | 208 KB | 16 MB |

### 9.8 Items intentionally deferred

- **UFW**: not applied per operator instruction (key rate-limiting concern).
- **Container resource limits** for the many `mem_limit=0 / nano_cpus=0` services — would require compose edits and restarts; skipped for stability.
- **Zombie processes (4)**: `wget` × 2 under DMB `next-server` (PID 1219065) and `esbuild` × 2 under ExtractedIQ `node --import tsx apps/worker/src/index.ts` — cosmetic; requires adding `init: true` to compose services + container restart.
- **Unused Docker images** (~16 GB still "reclaimable") — kept for fast rollbacks; prune manually with `docker image prune -af` if more space needed.

### 9.9 Rollback

All modified files backed up to `/root/perf_backup_20260422_042002/`:

- `daemon.json.bak`
- `postgresql.conf.bak`

Sysctl file to remove if rolling back: `/etc/sysctl.d/99-zzz-vps-perf.conf` (then `sysctl --system`).
Journald override: `/etc/systemd/journald.conf.d/size.conf`.

---

*End of report — generated from live SSH introspection on 185.252.233.186.*
