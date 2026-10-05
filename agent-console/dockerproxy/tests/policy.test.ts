// Policy table for CONTRACT.md §6 "Docker policy" — first match wins.
import { describe, expect, it } from 'vitest';
import {
  PROTECTED_DATA_VOLUMES,
  evaluate,
  isExecApprovalContainer,
  isFleetName,
  isOetImageRef,
  isVisibleContainerName,
  isVisibleNetworkName,
  isVisibleVolumeName,
  referencedContainers,
  type PolicyInput,
} from '../src/policy.js';

interface Row {
  name: string;
  method: string;
  path: string;
  body?: unknown;
  resolved?: PolicyInput['resolved'];
  execPreApproved?: boolean;
  decision: 'allow' | 'deny' | 'approval';
  rule: 1 | 2 | 3 | 4 | 5;
  transform?: string;
  grantKey?: string;
}

const EXEC = 'e'.repeat(64);

const rows: Row[] = [
  // ── Rule 1: console resources are untouchable ──
  { name: 'inspect console container', method: 'GET', path: '/containers/oet-agent-console/json', decision: 'deny', rule: 1 },
  { name: 'stop egress proxy', method: 'POST', path: '/v1.47/containers/oet-agent-egress/stop', decision: 'deny', rule: 1 },
  { name: 'exec into dockerproxy', method: 'POST', path: '/containers/oet-agent-dockerproxy/exec', body: { Cmd: ['sh'] }, decision: 'deny', rule: 1 },
  { name: 'remove dbproxy', method: 'DELETE', path: '/containers/oet-agent-dbproxy?force=1', decision: 'deny', rule: 1 },
  { name: 'logs of console', method: 'GET', path: '/containers/oet-agent-console/logs', decision: 'deny', rule: 1 },
  { name: 'id prefix resolving to the console', method: 'GET', path: '/containers/cccc/json', resolved: { containerName: '/oet-agent-console' }, decision: 'deny', rule: 1 },
  { name: 'percent-encoded console name', method: 'GET', path: '/containers/oet%2Dagent%2Dconsole/json', decision: 'deny', rule: 1 },
  { name: 'exec start whose container is the console', method: 'POST', path: `/exec/${EXEC}/start`, resolved: { containerName: 'oet-agent-console' }, execPreApproved: true, decision: 'deny', rule: 1 },
  { name: 'inspect console volume', method: 'GET', path: '/volumes/oet-agent-console_oet_agent_home', decision: 'deny', rule: 1 },
  { name: 'remove console volume', method: 'DELETE', path: '/volumes/oet-agent-console_oet_agent_sessions', decision: 'deny', rule: 1 },
  { name: 'remove protected postgres volume', method: 'DELETE', path: '/volumes/oetwebsite_oet_postgres_data', decision: 'deny', rule: 1 },
  { name: 'remove protected media volume', method: 'DELETE', path: '/volumes/oetwebsite_oet_learner_storage', decision: 'deny', rule: 1 },
  { name: 'connect a container to the console network', method: 'POST', path: '/networks/oet_agent_net/connect', body: { Container: 'oet-web-blue' }, decision: 'deny', rule: 1 },
  { name: 'remove the console control network', method: 'DELETE', path: '/networks/oet_agent_ctl', decision: 'deny', rule: 1 },
  { name: 'connect the console to another network', method: 'POST', path: '/networks/oetwebsite_internal/connect', body: { Container: 'oet-agent-console' }, decision: 'deny', rule: 1 },
  { name: 'create a network with a console name', method: 'POST', path: '/networks/create', body: { Name: 'oet_agent_egress' }, decision: 'deny', rule: 1 },
  { name: 'create impersonating a console name', method: 'POST', path: '/containers/create?name=oet-agent-console2', body: { Image: 'alpine' }, decision: 'deny', rule: 1 },
  { name: 'create binding the docker socket', method: 'POST', path: '/containers/create', body: { Image: 'docker:cli', HostConfig: { Binds: ['/var/run/docker.sock:/var/run/docker.sock'] } }, decision: 'deny', rule: 1 },
  { name: 'create binding /run (socket parent)', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['/run:/r'] } }, decision: 'deny', rule: 1 },
  { name: 'create binding the host root', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['/:/host'] } }, decision: 'deny', rule: 1 },
  { name: 'create binding docker data root', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Mounts: [{ Type: 'bind', Source: '/var/lib/docker/volumes', Target: '/v' }] } }, decision: 'deny', rule: 1 },
  { name: 'create binding the containerd socket', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['/run/containerd/containerd.sock:/c.sock'] } }, decision: 'deny', rule: 1 },
  { name: 'create binding containerd state', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Mounts: [{ Type: 'bind', Source: '/var/lib/containerd', Target: '/c' }] } }, decision: 'deny', rule: 1 },
  { name: 'create normalizing a traversal to the socket', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['/opt/oetwebapp/../../var/run:/r'] } }, decision: 'deny', rule: 1 },
  { name: 'create mounting a console volume', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Mounts: [{ Type: 'volume', Source: 'oet-agent-console_oet_agent_home', Target: '/h' }] } }, decision: 'deny', rule: 1 },
  { name: 'create with volumes-from the console', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { VolumesFrom: ['oet-agent-console:ro'] } }, decision: 'deny', rule: 1 },
  { name: 'create sharing the egress network namespace', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { NetworkMode: 'container:oet-agent-egress' } }, decision: 'deny', rule: 1 },
  { name: 'create by id resolving to the console', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { PidMode: 'container:cccc' } }, resolved: { names: { cccc: '/oet-agent-console' } }, decision: 'deny', rule: 1 },
  { name: 'create attached to the agent network', method: 'POST', path: '/containers/create', body: { Image: 'alpine', NetworkingConfig: { EndpointsConfig: { oet_agent_net: {} } } }, decision: 'deny', rule: 1 },

  // ── Rule 1: the Owner Fleet manager (oet-fleet*) is untouchable too. Without these rows an
  //    oet-fleet-* container would match OET_NAME and fall through to rules 2/3 (inspect, logs,
  //    stop/restart/kill allowed for free). ──
  { name: 'inspect the fleet manager', method: 'GET', path: '/containers/oet-fleet-manager/json', decision: 'deny', rule: 1 },
  { name: 'fleet manager logs', method: 'GET', path: '/containers/oet-fleet-manager/logs?tail=50', decision: 'deny', rule: 1 },
  { name: 'stop the fleet manager', method: 'POST', path: '/v1.47/containers/oet-fleet-manager/stop', decision: 'deny', rule: 1 },
  { name: 'restart the fleet manager', method: 'POST', path: '/containers/oet-fleet-manager/restart', decision: 'deny', rule: 1 },
  { name: 'kill the fleet manager', method: 'POST', path: '/containers/oet-fleet-manager/kill?signal=KILL', decision: 'deny', rule: 1 },
  { name: 'exec into the fleet manager', method: 'POST', path: '/containers/oet-fleet-manager/exec', body: { Cmd: ['sh'] }, decision: 'deny', rule: 1 },
  { name: 'remove the fleet manager', method: 'DELETE', path: '/containers/oet-fleet-manager?force=1', decision: 'deny', rule: 1 },
  { name: 'id prefix resolving to the fleet manager', method: 'GET', path: '/containers/ffff/json', resolved: { containerName: '/oet-fleet-manager' }, decision: 'deny', rule: 1 },
  { name: 'percent-encoded fleet name', method: 'GET', path: '/containers/oet%2Dfleet%2Dmanager/json', decision: 'deny', rule: 1 },
  { name: 'exec start whose container is the fleet manager', method: 'POST', path: `/exec/${EXEC}/start`, resolved: { containerName: 'oet-fleet-manager' }, execPreApproved: true, decision: 'deny', rule: 1 },
  { name: 'exec inspect whose container is the fleet manager', method: 'GET', path: `/exec/${EXEC}/json`, resolved: { containerName: 'oet-fleet-manager' }, decision: 'deny', rule: 1 },
  { name: 'inspect the fleet volume', method: 'GET', path: '/volumes/oet-fleet_fleet_data', decision: 'deny', rule: 1 },
  { name: 'remove the fleet volume', method: 'DELETE', path: '/volumes/oet-fleet_fleet_data', decision: 'deny', rule: 1 },
  { name: 'inspect the fleet network', method: 'GET', path: '/networks/oet-fleet_default', decision: 'deny', rule: 1 },
  { name: 'remove the fleet network', method: 'DELETE', path: '/networks/oet-fleet_default', decision: 'deny', rule: 1 },
  { name: 'connect a container to the fleet network', method: 'POST', path: '/networks/oet-fleet_default/connect', body: { Container: 'oet-web-blue' }, decision: 'deny', rule: 1 },
  { name: 'connect the fleet manager to another network', method: 'POST', path: '/networks/oetwebsite_internal/connect', body: { Container: 'oet-fleet-manager' }, decision: 'deny', rule: 1 },
  { name: 'create a network with a fleet name', method: 'POST', path: '/networks/create', body: { Name: 'oet-fleet_shadow' }, decision: 'deny', rule: 1 },
  { name: 'create impersonating the fleet manager', method: 'POST', path: '/containers/create?name=oet-fleet-manager2', body: { Image: 'alpine' }, decision: 'deny', rule: 1 },
  { name: 'create mounting the fleet volume', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Mounts: [{ Type: 'volume', Source: 'oet-fleet_fleet_data', Target: '/f' }] } }, decision: 'deny', rule: 1 },
  { name: 'create binding the fleet volume by name', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['oet-fleet_fleet_data:/f'] } }, decision: 'deny', rule: 1 },
  { name: 'create with volumes-from the fleet manager', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { VolumesFrom: ['oet-fleet-manager:ro'] } }, decision: 'deny', rule: 1 },
  { name: 'create sharing the fleet manager network namespace', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { NetworkMode: 'container:oet-fleet-manager' } }, decision: 'deny', rule: 1 },
  { name: 'create by id resolving to the fleet manager', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { PidMode: 'container:ffff' } }, resolved: { names: { ffff: '/oet-fleet-manager' } }, decision: 'deny', rule: 1 },
  { name: 'create attached to the fleet network', method: 'POST', path: '/containers/create', body: { Image: 'alpine', NetworkingConfig: { EndpointsConfig: { 'oet-fleet_default': {} } } }, decision: 'deny', rule: 1 },
  { name: 'create with the fleet network as network mode', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { NetworkMode: 'oet-fleet_default' } }, decision: 'deny', rule: 1 },

  // ── Rule 2: reads of OET-named objects ──
  { name: 'ping', method: 'GET', path: '/_ping', decision: 'allow', rule: 2 },
  { name: 'ping HEAD', method: 'HEAD', path: '/_ping', decision: 'allow', rule: 2 },
  { name: 'version', method: 'GET', path: '/v1.47/version', decision: 'allow', rule: 2 },
  { name: 'info', method: 'GET', path: '/info', decision: 'allow', rule: 2 },
  { name: 'events (filtered)', method: 'GET', path: '/events?filters=%7B%7D', decision: 'allow', rule: 2, transform: 'events' },
  { name: 'docker ps (filtered)', method: 'GET', path: '/v1.47/containers/json?all=1', decision: 'allow', rule: 2, transform: 'containers-list' },
  { name: 'inspect API slot (env redacted)', method: 'GET', path: '/containers/oet-api-blue/json', decision: 'allow', rule: 2, transform: 'container-inspect' },
  { name: 'inspect postgres (env redacted)', method: 'GET', path: '/containers/oet-postgres/json', decision: 'allow', rule: 2, transform: 'container-inspect' },
  { name: 'top oetwebsite container', method: 'GET', path: '/containers/oetwebsite-legacy/top', decision: 'allow', rule: 2 },
  { name: 'stats', method: 'GET', path: '/containers/oet-web-blue/stats?stream=false', decision: 'allow', rule: 2 },
  { name: 'images (filtered)', method: 'GET', path: '/images/json', decision: 'allow', rule: 2, transform: 'images-list' },
  { name: 'inspect project image', method: 'GET', path: '/images/ghcr.io/jerryboganda/oetwebapp-api:abc/json', decision: 'allow', rule: 2 },
  { name: 'inspect image by id with OET tags', method: 'GET', path: '/images/sha256:abcdef/json', resolved: { imageTags: ['ghcr.io/jerryboganda/oetwebapp-web:1'] }, decision: 'allow', rule: 2 },
  { name: 'networks (filtered)', method: 'GET', path: '/networks', decision: 'allow', rule: 2, transform: 'networks-list' },
  { name: 'inspect app network', method: 'GET', path: '/networks/oetwebsite_internal', decision: 'allow', rule: 2, transform: 'network-inspect' },
  { name: 'volumes (filtered)', method: 'GET', path: '/volumes', decision: 'allow', rule: 2, transform: 'volumes-list' },
  { name: 'inspect data volume', method: 'GET', path: '/volumes/oetwebsite_oet_postgres_data', decision: 'allow', rule: 2 },
  { name: 'exec inspect in OET container', method: 'GET', path: `/exec/${EXEC}/json`, resolved: { containerName: 'oet-web-blue' }, decision: 'allow', rule: 2 },

  // ── Rule 3: lifecycle + logs on oet-* except oet-postgres ──
  { name: 'restart web slot', method: 'POST', path: '/containers/oet-web-blue/restart', decision: 'allow', rule: 3 },
  { name: 'stop API slot', method: 'POST', path: '/v1.47/containers/oet-api-green/stop?t=30', decision: 'allow', rule: 3 },
  { name: 'start backup sidecar', method: 'POST', path: '/containers/oet-db-backup/start', decision: 'allow', rule: 3 },
  { name: 'wait', method: 'POST', path: '/containers/oet-ai-worker/wait?condition=next-exit', decision: 'allow', rule: 3 },
  { name: 'attach', method: 'POST', path: '/containers/oet-api-blue/attach?stream=1&stdout=1', decision: 'allow', rule: 3 },
  { name: 'kill', method: 'POST', path: '/containers/oet-clamav/kill?signal=HUP', decision: 'allow', rule: 3 },
  { name: 'logs', method: 'GET', path: '/containers/oet-api-blue/logs?follow=1&tail=100', decision: 'allow', rule: 3 },
  { name: 'postgres logs are not free', method: 'GET', path: '/containers/oet-postgres/logs', decision: 'deny', rule: 5 },
  { name: 'postgres restart is not free', method: 'POST', path: '/containers/oet-postgres/restart', decision: 'deny', rule: 5 },

  // ── Rule 4: owner approval ──
  { name: 'exec into postgres', method: 'POST', path: '/containers/oet-postgres/exec', body: { Cmd: ['psql', '-c', 'select 1'] }, decision: 'approval', rule: 4, transform: 'exec-create', grantKey: 'exec:oet-postgres' },
  { name: 'exec into API slot', method: 'POST', path: '/containers/oet-api-blue/exec', body: { Cmd: ['sh'] }, decision: 'approval', rule: 4, grantKey: 'exec:oet-api-blue' },
  { name: 'exec into AI worker', method: 'POST', path: '/containers/oet-ai-worker/exec', body: {}, decision: 'approval', rule: 4 },
  { name: 'exec into backup sidecar', method: 'POST', path: '/containers/oet-db-backup/exec', body: {}, decision: 'approval', rule: 4 },
  { name: 'exec into a co-tenant', method: 'POST', path: '/containers/ubag-vps-gateway-1/exec', body: {}, decision: 'approval', rule: 4, grantKey: 'exec:ubag-vps-gateway-1' },
  { name: 'exec start pre-approved at create', method: 'POST', path: `/exec/${EXEC}/start`, resolved: { containerName: 'oet-postgres' }, execPreApproved: true, decision: 'allow', rule: 4 },
  { name: 'exec inspect pre-approved (co-tenant)', method: 'GET', path: `/exec/${EXEC}/json`, resolved: { containerName: 'ubag-vps-gateway-1' }, execPreApproved: true, decision: 'allow', rule: 4 },
  { name: 'exec start not created through the proxy', method: 'POST', path: `/exec/${EXEC}/start`, resolved: { containerName: 'oet-postgres' }, decision: 'approval', rule: 4 },
  { name: 'create a container', method: 'POST', path: '/containers/create?name=oet-scratch', body: { Image: 'alpine', Cmd: ['true'] }, decision: 'approval', rule: 4 },
  { name: 'create with an allowed bind', method: 'POST', path: '/containers/create', body: { Image: 'alpine', HostConfig: { Binds: ['/opt/oetwebapp/scripts:/s:ro'] } }, decision: 'approval', rule: 4 },
  { name: 'remove web slot', method: 'DELETE', path: '/containers/oet-web-blue?force=1', decision: 'approval', rule: 4 },
  { name: 'remove image', method: 'DELETE', path: '/images/ghcr.io/jerryboganda/oetwebapp-web:old', decision: 'approval', rule: 4 },
  { name: 'remove app network', method: 'DELETE', path: '/networks/oetwebsite_internal', decision: 'approval', rule: 4 },
  { name: 'remove other volume', method: 'DELETE', path: '/volumes/scratch_data', decision: 'approval', rule: 4 },
  { name: 'volume prune', method: 'POST', path: '/volumes/prune', decision: 'approval', rule: 4 },
  { name: 'container prune', method: 'POST', path: '/containers/prune', decision: 'approval', rule: 4 },
  { name: 'image prune', method: 'POST', path: '/v1.47/images/prune?filters=%7B%7D', decision: 'approval', rule: 4 },
  { name: 'network prune', method: 'POST', path: '/networks/prune', decision: 'approval', rule: 4 },
  { name: 'build cache prune', method: 'POST', path: '/build/prune', decision: 'approval', rule: 4 },
  { name: 'network create', method: 'POST', path: '/networks/create', body: { Name: 'scratch' }, decision: 'approval', rule: 4 },
  { name: 'network connect', method: 'POST', path: '/networks/oetwebsite_internal/connect', body: { Container: 'oet-web-blue' }, decision: 'approval', rule: 4 },
  { name: 'inspect co-tenant', method: 'GET', path: '/containers/ubag-vps-gateway-1/json', decision: 'approval', rule: 4, transform: 'container-inspect', grantKey: 'read:ubag-vps-gateway-1' },
  { name: 'co-tenant logs', method: 'GET', path: '/containers/ubag-vps-gateway-1/logs', decision: 'approval', rule: 4, grantKey: 'read:ubag-vps-gateway-1' },
  { name: 'restart co-tenant', method: 'POST', path: '/containers/ubag-vps-gateway-1/restart', decision: 'approval', rule: 4, grantKey: 'lifecycle:ubag-vps-gateway-1' },
  { name: 'inspect non-OET image', method: 'GET', path: '/images/pgvector/pgvector:pg17/json', decision: 'approval', rule: 4 },
  { name: 'inspect non-OET network', method: 'GET', path: '/networks/bridge', decision: 'approval', rule: 4 },
  { name: 'inspect console network (read, non-OET name)', method: 'GET', path: '/networks/oet_agent_net', decision: 'approval', rule: 4 },
  { name: 'inspect non-OET volume', method: 'GET', path: '/volumes/cotenant_data', decision: 'approval', rule: 4 },

  // ── Rule 5: everything else ──
  { name: 'exec into web slot (not in the approval list)', method: 'POST', path: '/containers/oet-web-blue/exec', body: { Cmd: ['sh'] }, decision: 'deny', rule: 5 },
  { name: 'exec start in web slot', method: 'POST', path: `/exec/${EXEC}/start`, resolved: { containerName: 'oet-web-blue' }, decision: 'deny', rule: 5 },
  { name: 'exec with unknown container', method: 'POST', path: `/exec/${EXEC}/start`, decision: 'deny', rule: 5 },
  { name: 'image pull', method: 'POST', path: '/images/create?fromImage=alpine&tag=latest', decision: 'deny', rule: 5 },
  { name: 'image push (exfiltration)', method: 'POST', path: '/images/ghcr.io/x/y:tag/push', decision: 'deny', rule: 5 },
  { name: 'image load', method: 'POST', path: '/images/load', decision: 'deny', rule: 5 },
  { name: 'image export', method: 'GET', path: '/images/get?names=x', decision: 'deny', rule: 5 },
  { name: 'build', method: 'POST', path: '/build', decision: 'deny', rule: 5 },
  { name: 'commit', method: 'POST', path: '/commit?container=oet-web-blue', decision: 'deny', rule: 5 },
  { name: 'copy files out of API slot', method: 'GET', path: '/containers/oet-api-blue/archive?path=/app', decision: 'deny', rule: 5 },
  { name: 'copy files into API slot', method: 'PUT', path: '/containers/oet-api-blue/archive?path=/app', decision: 'deny', rule: 5 },
  { name: 'archive HEAD', method: 'HEAD', path: '/containers/oet-api-blue/archive?path=/app', decision: 'deny', rule: 5 },
  { name: 'export container filesystem', method: 'GET', path: '/containers/oet-api-blue/export', decision: 'deny', rule: 5 },
  { name: 'copy files out of co-tenant', method: 'GET', path: '/containers/ubag-vps-gateway-1/archive?path=/', decision: 'deny', rule: 5 },
  { name: 'update resources', method: 'POST', path: '/containers/oet-api-blue/update', body: { Memory: 1 }, decision: 'deny', rule: 5 },
  { name: 'rename', method: 'POST', path: '/containers/oet-web-blue/rename?name=x', decision: 'deny', rule: 5 },
  { name: 'attach websocket', method: 'GET', path: '/containers/oet-api-blue/attach/ws', decision: 'deny', rule: 5 },
  { name: 'volume create', method: 'POST', path: '/volumes/create', body: { Name: 'x' }, decision: 'deny', rule: 5 },
  { name: 'system df', method: 'GET', path: '/system/df', decision: 'deny', rule: 5 },
  { name: 'swarm', method: 'GET', path: '/swarm', decision: 'deny', rule: 5 },
  { name: 'secrets', method: 'GET', path: '/secrets', decision: 'deny', rule: 5 },
  { name: 'plugins install', method: 'POST', path: '/plugins/pull?remote=x', decision: 'deny', rule: 5 },
  { name: 'registry auth', method: 'POST', path: '/auth', decision: 'deny', rule: 5 },
  { name: 'encoded traversal', method: 'GET', path: '/containers/..%2F..%2Fetc/json', decision: 'deny', rule: 5 },
  { name: 'empty segment', method: 'GET', path: '/containers//json', decision: 'deny', rule: 5 },
];

describe('docker policy table (CONTRACT.md §6)', () => {
  it.each(rows)('$name → $decision (rule $rule)', (row) => {
    const result = evaluate({
      method: row.method,
      path: row.path,
      body: row.body,
      resolved: row.resolved,
      execPreApproved: row.execPreApproved,
    });
    expect({ decision: result.decision, rule: result.rule }).toEqual({ decision: row.decision, rule: row.rule });
    if (row.transform !== undefined) expect(result.transform).toBe(row.transform);
    if (row.grantKey !== undefined) expect(result.grantKey).toBe(row.grantKey);
    if (result.decision !== 'allow' || result.rule === 4) expect(result.reasons.length).toBeGreaterThan(0);
  });
});

describe('approval details', () => {
  it('lists create risks on the approval card', () => {
    const result = evaluate({
      method: 'POST',
      path: '/containers/create?name=oet-scratch',
      body: {
        Image: 'alpine',
        HostConfig: {
          Privileged: true,
          CapAdd: ['SYS_ADMIN'],
          Binds: ['/etc:/host-etc:ro'],
          PidMode: 'host',
          Mounts: [{ Type: 'volume', Source: 'oetwebsite_oet_postgres_data', Target: '/pg' }],
          Devices: [{ PathOnHost: '/dev/sda' }],
          SecurityOpt: ['seccomp=unconfined'],
          PortBindings: { '80/tcp': [{ HostPort: '8080' }] },
        },
      },
    });
    expect(result.decision).toBe('approval');
    expect(result.grantKey).toBeUndefined();
    expect(result.reasons).toEqual(
      expect.arrayContaining([
        'creates a new container',
        'privileged container',
        'adds capabilities: SYS_ADMIN',
        'bind mount outside /opt/oetwebapp and /var/opt/oet-learner/releases: /etc',
        'host PID namespace (PidMode=host)',
        'mounts protected production volume oetwebsite_oet_postgres_data',
        'maps host devices',
        'weakened security options: seccomp=unconfined',
        'publishes host ports',
      ]),
    );
    expect(result.summary).toBe('docker create oet-scratch (alpine)');
  });

  it('shows exec commands but only env KEYS', () => {
    const result = evaluate({
      method: 'POST',
      path: '/containers/oet-postgres/exec',
      body: { Cmd: ['psql', '-c', 'delete from scratch where true'], Env: ['PGPASSWORD=super-secret-value'], User: 'postgres' },
    });
    expect(result.summary).toBe('docker exec oet-postgres psql -c delete from scratch where true');
    expect(result.details).toMatchObject({ cmd: ['psql', '-c', 'delete from scratch where true'], envKeys: ['PGPASSWORD'], user: 'postgres' });
    expect(JSON.stringify(result.details)).not.toContain('super-secret-value');
  });

  it('never makes create/delete/prune session-grantable', () => {
    for (const [method, path] of [
      ['POST', '/containers/create'],
      ['DELETE', '/containers/oet-web-blue'],
      ['POST', '/volumes/prune'],
      ['POST', '/networks/create'],
    ] as const) {
      expect(evaluate({ method, path, body: { Image: 'alpine', Name: 'n' } }).grantKey).toBeUndefined();
    }
  });
});

describe('Owner Fleet isolation', () => {
  it('denies with a fleet reason and never raises an approval card', () => {
    const result = evaluate({ method: 'POST', path: '/containers/oet-fleet-manager/stop' });
    expect(result.decision).toBe('deny');
    expect(result.rule).toBe(1);
    expect(result.reasons).toEqual(['oet-fleet-manager belongs to the Owner Fleet manager']);
    expect(result.grantKey).toBeUndefined();
  });

  it('explains which fleet resource a create touches', () => {
    const result = evaluate({
      method: 'POST',
      path: '/containers/create?name=oet-fleet-manager2',
      body: {
        Image: 'alpine',
        HostConfig: { Mounts: [{ Type: 'volume', Source: 'oet-fleet_fleet_data', Target: '/f' }], NetworkMode: 'oet-fleet_default' },
      },
    });
    expect(result.decision).toBe('deny');
    expect(result.reasons).toEqual(
      expect.arrayContaining([
        'container name oet-fleet-manager2 impersonates the Owner Fleet manager',
        'mounts Owner Fleet volume oet-fleet_fleet_data',
        'attaches to Owner Fleet network oet-fleet_default',
      ]),
    );
  });

  it('keeps the fleet volume in the protected production data set', () => {
    expect(PROTECTED_DATA_VOLUMES.has('oet-fleet_fleet_data')).toBe(true);
  });

  it.each([
    ['oet-fleet-manager', true],
    ['/oet-fleet-manager', true],
    ['oet-fleet_fleet_data', true],
    ['oet-fleet_default', true],
    ['oet-fleetx', true],
    ['oet-api-blue', false],
    ['oet-agent-console', false],
    ['myoet-fleet', false],
  ])('isFleetName(%s) === %s', (name, expected) => {
    expect(isFleetName(name)).toBe(expected);
  });

  it('never lists fleet containers, volumes or networks as visible', () => {
    expect(isVisibleContainerName('/oet-fleet-manager')).toBe(false);
    expect(isVisibleVolumeName('oet-fleet_fleet_data')).toBe(false);
    expect(isVisibleNetworkName('oet-fleet_default')).toBe(false);
    expect(isVisibleContainerName('/oet-api-blue')).toBe(true);
  });
});

describe('helpers', () => {
  it.each([
    ['oet-postgres', true],
    ['/oet-api-blue', true],
    ['oet-api-green', true],
    ['oet-ai-worker', true],
    ['oet-db-backup', true],
    ['oet-api', false],
    ['oet-web-blue', false],
  ])('isExecApprovalContainer(%s) === %s', (name, expected) => {
    expect(isExecApprovalContainer(name)).toBe(expected);
  });

  it.each([
    ['ghcr.io/jerryboganda/oetwebapp-api:abc', true],
    ['ghcr.io/jerryboganda/oetwebapp-agent-console@sha256:0123', true],
    ['oetwebsite-learner-api:local', true],
    ['localhost:5000/oet-tool:1', true],
    ['pgvector/pgvector:pg17', false],
    ['nginx:1.27-alpine', false],
    ['myoet-x:1', false],
  ])('isOetImageRef(%s) === %s', (ref, expected) => {
    expect(isOetImageRef(ref)).toBe(expected);
  });

  it('collects body references for resolution', () => {
    expect(
      referencedContainers(
        { kind: 'containers-create' },
        { HostConfig: { VolumesFrom: ['abc:ro'], Links: ['/db:db'], NetworkMode: 'container:def', PidMode: 'container:ghi', IpcMode: 'shareable' } },
      ),
    ).toEqual(['abc', 'db', 'def', 'ghi']);
    expect(referencedContainers({ kind: 'network', id: 'n', action: 'connect' }, { Container: 'xyz' })).toEqual(['xyz']);
  });
});
