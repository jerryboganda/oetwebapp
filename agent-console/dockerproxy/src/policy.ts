// Docker policy (CONTRACT.md §6) — a PURE function over method + path + body
// (+ optional name resolution done by the server). First match wins:
//
//   1. deny     anything touching the console's own containers (oet-agent-*),
//               the console volumes, or (additive hardening, see below) the
//               console networks, the Docker socket / data root, and deletion
//               of the protected production data volumes.
//   2. allow    GET reads of OET-named objects (^/?(oet-|oetwebsite)); container
//               inspect has Config.Env values redacted; list/event responses are
//               filtered to OET names.
//   3. allow    lifecycle POST (start|stop|restart|kill|pause|unpause|wait|resize|attach)
//               and GET logs on oet-* except oet-postgres.
//   4. approval exec into oet-postgres / oet-api-* / oet-ai-worker / oet-db-backup,
//               container create, any DELETE, prunes, network changes, and the
//               rule-2/3/exec operations when they target a non-OET resource.
//   5. deny     everything else (image pull/push/build, archive/export, update,
//               rename, commit, swarm, plugins, secrets, …).
//
// Additive rule-1 hardening beyond the contract text (documented in the infra
// report): console networks cannot be modified; binds that expose the Docker
// / containerd sockets or their state dirs are denied (they would bypass this proxy); DELETE of
// the AGENTS.md protected production volumes is denied (the host-side docker
// wrapper from protect-production-data.sh does not see socket API calls).
import path from 'node:path';
import { parseDockerPath, type DockerRoute, type ParsedPath } from './routes.js';
import { asArray, asRecord, asStringArray, str, stripSlash, truncate } from './util.js';

export const OET_NAME = /^\/?(oet-|oetwebsite)/;
export const CONSOLE_CONTAINER = /^\/?oet-agent-/;
export const CONSOLE_VOLUMES: ReadonlySet<string> = new Set([
  'oet-agent-console_oet_agent_home',
  'oet-agent-console_oet_agent_workspace',
  'oet-agent-console_oet_agent_sessions',
]);
export const CONSOLE_NETWORKS: ReadonlySet<string> = new Set(['oet_agent_ctl', 'oet_agent_net', 'oet_agent_egress']);
export const PROTECTED_DATA_VOLUMES: ReadonlySet<string> = new Set([
  'oetwebsite_oet_postgres_data',
  'oetwebsite_oet_learner_storage',
  'oetwebsite_oet_db_backups',
  'oetwebsite_oet_clamav_data',
  'oetwebsite_oet_with_dr_hesham_storage',
]);
export const ALLOWED_BIND_ROOTS: readonly string[] = ['/opt/oetwebapp', '/var/opt/oet-learner/releases'];
// The containerd socket / state are equivalent to the Docker socket (a client
// on them can start privileged containers without going through this proxy).
const DOCKER_SOCKETS = [
  '/var/run/docker.sock',
  '/run/docker.sock',
  '/run/containerd/containerd.sock',
  '/var/run/containerd/containerd.sock',
];
const DOCKER_DATA_ROOTS = ['/var/lib/docker', '/var/lib/containerd', '/run/containerd', '/var/run/containerd'];

const LIFECYCLE_ACTIONS = new Set(['start', 'stop', 'restart', 'kill', 'pause', 'unpause', 'wait', 'resize', 'attach']);
const CONTAINER_READ_ACTIONS = new Set(['json', 'top', 'stats', 'changes']);
const IMAGE_READ_ACTIONS = new Set(['json', 'history']);
const PRUNE_KINDS = new Set<DockerRoute['kind']>([
  'containers-prune',
  'images-prune',
  'networks-prune',
  'volumes-prune',
  'system-prune',
  'build-prune',
]);

export function isOetName(name: string): boolean {
  return OET_NAME.test(name);
}

export function isConsoleContainer(name: string): boolean {
  return CONSOLE_CONTAINER.test(name);
}

/** Visible in lists/events: OET-named and not one of the console's own containers. */
export function isVisibleContainerName(name: string): boolean {
  const n = stripSlash(name);
  return isOetName(n) && !isConsoleContainer(n);
}

export function isVisibleVolumeName(name: string): boolean {
  return isOetName(name) && !CONSOLE_VOLUMES.has(name);
}

export function isVisibleNetworkName(name: string): boolean {
  return isOetName(name) && !CONSOLE_NETWORKS.has(name);
}

export function isExecApprovalContainer(name: string): boolean {
  const n = stripSlash(name);
  return n === 'oet-postgres' || n === 'oet-ai-worker' || n === 'oet-db-backup' || n.startsWith('oet-api-');
}

/**
 * OET image reference: a repository whose path contains a component starting
 * with oet-, oetwebsite or oetwebapp (the project's GHCR images are
 * ghcr.io/<owner>/oetwebapp-*). Tag and digest are ignored.
 */
export function isOetImageRef(ref: string): boolean {
  let repo = ref.trim().toLowerCase();
  const at = repo.indexOf('@');
  if (at >= 0) repo = repo.slice(0, at);
  const lastSlash = repo.lastIndexOf('/');
  const colon = repo.lastIndexOf(':');
  if (colon > lastSlash) repo = repo.slice(0, colon);
  return /(^|\/)(oet-|oetwebsite|oetwebapp)/.test(repo);
}

function isWithin(child: string, root: string): boolean {
  if (root === '/') return child.startsWith('/');
  return child === root || child.startsWith(`${root}/`);
}

function exposesDockerDaemon(source: string): boolean {
  return (
    DOCKER_SOCKETS.some((socket) => isWithin(socket, source)) ||
    DOCKER_DATA_ROOTS.some((root) => isWithin(source, root) || isWithin(root, source))
  );
}

export type Decision = 'allow' | 'deny' | 'approval';
export type ResponseTransform =
  | 'containers-list'
  | 'container-inspect'
  | 'images-list'
  | 'networks-list'
  | 'network-inspect'
  | 'volumes-list'
  | 'events'
  | 'exec-create';

export interface PolicyDecision {
  decision: Decision;
  rule: 1 | 2 | 3 | 4 | 5;
  reasons: string[];
  /** One-line human summary for the approval card / log. */
  summary: string;
  /** Primary object the request touches (container/image/network/volume name). */
  target: string;
  transform?: ResponseTransform;
  /** Present when an owner "approve for session" may cover repeats of this operation. */
  grantKey?: string;
  details?: Record<string, unknown>;
}

export interface Resolution {
  /** Canonical name of the route's container, or of the exec's container for /exec routes. */
  containerName?: string;
  /** Canonical name of the route's network. */
  networkName?: string;
  /** RepoTags of the route's image. */
  imageTags?: string[];
  /** Container ids/names referenced from request bodies → canonical names. */
  names?: Record<string, string>;
}

export interface PolicyInput {
  method: string;
  /** Raw request target: "/v1.47/containers/json?all=1". */
  path: string;
  body?: unknown;
  resolved?: Resolution;
  /** The exec instance was created through an approved/allowed exec create. */
  execPreApproved?: boolean;
}

export interface CreateAnalysis {
  name: string;
  image: string;
  touchedContainers: string[];
  networks: string[];
  denyReasons: string[];
  approvalReasons: string[];
  binds: string[];
}

/** Container ids/names a request body refers to (the server resolves them to canonical names). */
export function referencedContainers(route: DockerRoute, body: unknown): string[] {
  const refs: string[] = [];
  const b = asRecord(body);
  if (route.kind === 'containers-create') {
    const hc = asRecord(b.HostConfig);
    for (const entry of asStringArray(hc.VolumesFrom)) refs.push(entry.split(':')[0] ?? '');
    for (const entry of asStringArray(hc.Links)) refs.push(stripSlash(entry.split(':')[0] ?? ''));
    for (const field of ['NetworkMode', 'PidMode', 'IpcMode']) {
      const mode = str(hc[field]);
      if (mode.startsWith('container:')) refs.push(mode.slice('container:'.length));
    }
  }
  if (route.kind === 'network' && (route.action === 'connect' || route.action === 'disconnect')) {
    refs.push(str(b.Container));
  }
  return refs.filter((ref) => ref !== '');
}

function analyzeCreate(body: unknown, nameParam: string, nameOf: (ref: string) => string): CreateAnalysis {
  const b = asRecord(body);
  const hc = asRecord(b.HostConfig);
  const out: CreateAnalysis = {
    name: stripSlash(nameParam),
    image: str(b.Image),
    touchedContainers: [],
    networks: [],
    denyReasons: [],
    approvalReasons: [],
    binds: [],
  };

  if (out.name !== '' && isConsoleContainer(out.name)) {
    out.denyReasons.push(`container name ${out.name} impersonates the Owner Agent Console`);
  }

  const namedVolume = (volume: string): void => {
    if (CONSOLE_VOLUMES.has(volume)) out.denyReasons.push(`mounts console volume ${volume}`);
    else if (PROTECTED_DATA_VOLUMES.has(volume)) out.approvalReasons.push(`mounts protected production volume ${volume}`);
  };
  const bindSource = (raw: string): void => {
    out.binds.push(raw);
    if (!raw.startsWith('/')) {
      namedVolume(raw);
      return;
    }
    const source = path.posix.normalize(raw);
    if (exposesDockerDaemon(source)) {
      out.denyReasons.push(`bind mount ${source} exposes the Docker socket or data root (would bypass oet-agent-dockerproxy)`);
    } else if (!ALLOWED_BIND_ROOTS.some((root) => isWithin(source, root))) {
      out.approvalReasons.push(`bind mount outside /opt/oetwebapp and /var/opt/oet-learner/releases: ${source}`);
    }
  };

  for (const bind of asStringArray(hc.Binds)) bindSource(bind.split(':')[0] ?? '');
  for (const mount of asArray(hc.Mounts)) {
    const m = asRecord(mount);
    const type = str(m.Type) || 'volume';
    const source = str(m.Source);
    if (type === 'bind') {
      bindSource(source.startsWith('/') ? source : `/${source}`);
    } else if (type === 'volume') {
      if (source !== '') {
        out.binds.push(source);
        namedVolume(source);
      }
      const driverOptions = asRecord(asRecord(asRecord(m.VolumeOptions).DriverConfig).Options);
      if (Object.keys(driverOptions).length > 0) out.approvalReasons.push('volume mount with driver options (can bind host paths)');
    } else if (type !== 'tmpfs') {
      out.approvalReasons.push(`unusual mount type ${type}`);
    }
  }

  for (const ref of referencedContainers({ kind: 'containers-create' }, body)) {
    const name = nameOf(ref);
    out.touchedContainers.push(name);
    if (isConsoleContainer(name)) out.denyReasons.push(`references console container ${name}`);
  }

  const namespaces: Array<[string, string]> = [
    ['NetworkMode', 'network'],
    ['PidMode', 'PID'],
    ['IpcMode', 'IPC'],
    ['UTSMode', 'UTS'],
    ['UsernsMode', 'user'],
    ['CgroupnsMode', 'cgroup'],
  ];
  for (const [field, label] of namespaces) {
    const mode = str(hc[field]);
    if (mode === 'host') out.approvalReasons.push(`host ${label} namespace (${field}=host)`);
    else if (mode.startsWith('container:')) out.approvalReasons.push(`joins the ${label} namespace of ${nameOf(mode.slice('container:'.length))}`);
  }
  const networkMode = str(hc.NetworkMode);
  if (networkMode !== '' && !['default', 'bridge', 'none', 'host'].includes(networkMode) && !networkMode.startsWith('container:')) {
    out.networks.push(networkMode);
  }
  out.networks.push(...Object.keys(asRecord(asRecord(b.NetworkingConfig).EndpointsConfig)));
  for (const network of out.networks) {
    if (CONSOLE_NETWORKS.has(network)) out.denyReasons.push(`attaches to console network ${network}`);
  }

  if (hc.Privileged === true) out.approvalReasons.push('privileged container');
  const capAdd = asStringArray(hc.CapAdd);
  if (capAdd.length > 0) out.approvalReasons.push(`adds capabilities: ${capAdd.join(', ')}`);
  if (asArray(hc.Devices).length > 0) out.approvalReasons.push('maps host devices');
  if (asArray(hc.DeviceRequests).length > 0) out.approvalReasons.push('device requests (e.g. GPUs)');
  if (asArray(hc.DeviceCgroupRules).length > 0) out.approvalReasons.push('device cgroup rules');
  const securityOpt = asStringArray(hc.SecurityOpt);
  if (securityOpt.some((opt) => /unconfined|label[:=]disable/i.test(opt))) {
    out.approvalReasons.push(`weakened security options: ${securityOpt.join(', ')}`);
  }
  if (Object.keys(asRecord(hc.Sysctls)).length > 0) out.approvalReasons.push('kernel sysctls');
  if (str(hc.CgroupParent) !== '') out.approvalReasons.push(`custom cgroup parent ${str(hc.CgroupParent)}`);
  if (hc.PublishAllPorts === true || Object.keys(asRecord(hc.PortBindings)).length > 0) {
    out.approvalReasons.push('publishes host ports');
  }
  return out;
}

function execDetails(body: unknown): Record<string, unknown> {
  const b = asRecord(body);
  const cmd = asStringArray(b.Cmd).slice(0, 64).map((part) => truncate(part, 2000));
  return {
    cmd,
    user: str(b.User) || undefined,
    workingDir: str(b.WorkingDir) || undefined,
    privileged: b.Privileged === true,
    tty: b.Tty === true,
    // Keys only: values may be secrets the agent typed.
    envKeys: asStringArray(b.Env).map((entry) => entry.split('=')[0] ?? ''),
  };
}

function commandLine(body: unknown): string {
  return truncate(asStringArray(asRecord(body).Cmd).join(' '), 500);
}

function decision(
  kind: Decision,
  rule: PolicyDecision['rule'],
  reasons: string[],
  summary: string,
  target: string,
  extra: Partial<Pick<PolicyDecision, 'transform' | 'grantKey' | 'details'>> = {},
): PolicyDecision {
  return { decision: kind, rule, reasons, summary, target, ...extra };
}

/** Parses the path, then evaluates. Convenience entry point used by the policy-table tests. */
export function evaluate(input: PolicyInput): PolicyDecision {
  const method = input.method.toUpperCase();
  const parsed = parseDockerPath(method, input.path);
  if (!parsed.ok) {
    return decision('deny', 5, [`malformed Docker API path (${parsed.error})`], `${method} ${truncate(input.path, 200)}`, '');
  }
  return evaluateParsed(method, parsed.value, input);
}

export function evaluateParsed(methodInput: string, parsed: ParsedPath, input: PolicyInput): PolicyDecision {
  const method = methodInput.toUpperCase();
  const { route, query } = parsed;
  const resolved = input.resolved ?? {};
  const nameOf = (ref: string): string => stripSlash(resolved.names?.[ref] ?? ref);
  const isRead = method === 'GET' || method === 'HEAD';
  const body = input.body;
  const request = `${method} ${parsed.versionPrefix}/${parsed.segments.join('/')}`;

  let containerName: string | undefined;
  if (route.kind === 'container') containerName = stripSlash(resolved.containerName ?? route.id);
  if (route.kind === 'exec' && resolved.containerName) containerName = stripSlash(resolved.containerName);
  const networkName = route.kind === 'network' ? stripSlash(resolved.networkName ?? route.id) : undefined;

  // ── Rule 1: the console's own resources ─────────────────────────────────
  if (containerName !== undefined && isConsoleContainer(containerName)) {
    return decision('deny', 1, [`${containerName} belongs to the Owner Agent Console`], request, containerName);
  }
  if (route.kind === 'volume' && CONSOLE_VOLUMES.has(route.name)) {
    return decision('deny', 1, [`${route.name} is an Owner Agent Console volume`], request, route.name);
  }
  if (route.kind === 'volume' && method === 'DELETE' && PROTECTED_DATA_VOLUMES.has(route.name)) {
    return decision('deny', 1, [`${route.name} is a protected production data volume (AGENTS.md storage law)`], request, route.name);
  }
  if (route.kind === 'network' && networkName !== undefined && CONSOLE_NETWORKS.has(networkName) && !isRead) {
    return decision('deny', 1, [`${networkName} is an Owner Agent Console network`], request, networkName);
  }
  if (route.kind === 'networks-create' && CONSOLE_NETWORKS.has(str(asRecord(body).Name))) {
    return decision('deny', 1, [`network name ${str(asRecord(body).Name)} is reserved for the Owner Agent Console`], request, str(asRecord(body).Name));
  }
  if (route.kind === 'network' && (route.action === 'connect' || route.action === 'disconnect')) {
    const container = nameOf(str(asRecord(body).Container));
    if (container !== '' && isConsoleContainer(container)) {
      return decision('deny', 1, [`${container} belongs to the Owner Agent Console`], request, container);
    }
  }
  let create: CreateAnalysis | undefined;
  if (route.kind === 'containers-create') {
    create = analyzeCreate(body, query.get('name') ?? '', nameOf);
    if (create.denyReasons.length > 0) {
      return decision('deny', 1, create.denyReasons, `docker create ${create.name || '<unnamed>'} (${create.image || 'no image'})`, create.name);
    }
  }

  // ── Rule 2: reads of OET-named objects ──────────────────────────────────
  if (isRead) {
    switch (route.kind) {
      case 'ping':
      case 'version':
      case 'info':
        return decision('allow', 2, [], request, '');
      case 'events':
        return decision('allow', 2, [], request, '', { transform: 'events' });
      case 'containers-list':
        return decision('allow', 2, [], request, '', { transform: 'containers-list' });
      case 'images-list':
        return decision('allow', 2, [], request, '', { transform: 'images-list' });
      case 'networks-list':
        return decision('allow', 2, [], request, '', { transform: 'networks-list' });
      case 'volumes-list':
        return decision('allow', 2, [], request, '', { transform: 'volumes-list' });
      case 'container':
        if (containerName !== undefined && route.action !== null && CONTAINER_READ_ACTIONS.has(route.action) && isOetName(containerName)) {
          return decision('allow', 2, [], request, containerName, route.action === 'json' ? { transform: 'container-inspect' } : {});
        }
        break;
      case 'image': {
        const tags = resolved.imageTags ?? [route.name];
        if (route.action !== null && IMAGE_READ_ACTIONS.has(route.action) && (isOetImageRef(route.name) || tags.some(isOetImageRef))) {
          return decision('allow', 2, [], request, route.name);
        }
        break;
      }
      case 'network':
        if (route.action === null && networkName !== undefined && isVisibleNetworkName(networkName)) {
          return decision('allow', 2, [], request, networkName, { transform: 'network-inspect' });
        }
        break;
      case 'volume':
        if (isVisibleVolumeName(route.name)) return decision('allow', 2, [], request, route.name);
        break;
      case 'exec':
        if (route.action === 'json' && containerName !== undefined && isOetName(containerName)) {
          return decision('allow', 2, [], request, containerName);
        }
        break;
      default:
        break;
    }
  }

  // ── Rule 3: lifecycle + logs on oet-* except oet-postgres ───────────────
  if (route.kind === 'container' && containerName !== undefined && isOetName(containerName) && containerName !== 'oet-postgres') {
    if (method === 'POST' && route.action !== null && LIFECYCLE_ACTIONS.has(route.action)) {
      return decision('allow', 3, [], `docker ${route.action} ${containerName}`, containerName);
    }
    if (isRead && route.action === 'logs') {
      return decision('allow', 3, [], `docker logs ${containerName}`, containerName);
    }
  }

  // ── Rule 4: owner approval ───────────────────────────────────────────────
  if (route.kind === 'container' && method === 'POST' && route.action === 'exec' && containerName !== undefined) {
    const sensitive = isExecApprovalContainer(containerName);
    if (sensitive || !isOetName(containerName)) {
      return decision(
        'approval',
        4,
        [sensitive ? `exec into sensitive container ${containerName}` : `exec into non-OET container ${containerName}`],
        truncate(`docker exec ${containerName} ${commandLine(body)}`, 600),
        containerName,
        { transform: 'exec-create', grantKey: `exec:${containerName}`, details: execDetails(body) },
      );
    }
  }
  if (route.kind === 'exec' && containerName !== undefined && (isExecApprovalContainer(containerName) || !isOetName(containerName))) {
    const permitted = (method === 'POST' && (route.action === 'start' || route.action === 'resize')) || (isRead && route.action === 'json');
    if (permitted) {
      if (input.execPreApproved === true) {
        return decision('allow', 4, ['exec instance was approved when it was created'], request, containerName);
      }
      return decision('approval', 4, [`exec ${route.action} in ${containerName} for an exec this proxy did not approve`], request, containerName, {
        grantKey: `exec:${containerName}`,
      });
    }
  }
  if (route.kind === 'containers-create' && create !== undefined) {
    return decision(
      'approval',
      4,
      ['creates a new container', ...create.approvalReasons],
      `docker create ${create.name || '<unnamed>'} (${create.image || 'no image'})`,
      create.name,
      {
        details: {
          name: create.name || undefined,
          image: create.image,
          cmd: asStringArray(asRecord(body).Cmd).slice(0, 64),
          mounts: create.binds,
          networks: create.networks,
          touchedContainers: create.touchedContainers,
        },
      },
    );
  }
  if (method === 'DELETE' && (route.kind === 'container' || route.kind === 'image' || route.kind === 'network' || route.kind === 'volume')) {
    const target =
      route.kind === 'container' ? (containerName ?? route.id) : route.kind === 'image' ? route.name : route.kind === 'network' ? (networkName ?? route.id) : route.name;
    const verb = { container: 'docker rm', image: 'docker rmi', network: 'docker network rm', volume: 'docker volume rm' }[route.kind];
    return decision('approval', 4, [`deletes ${route.kind} ${target}`], `${verb} ${target}`, target, {
      details: { force: query.get('force') ?? undefined, volumes: query.get('v') ?? undefined },
    });
  }
  if (method === 'POST' && PRUNE_KINDS.has(route.kind)) {
    return decision('approval', 4, [`${route.kind.replace('-', ' ')} (bulk delete)`], `docker ${route.kind.replace('-', ' ')}`, route.kind, {
      details: { filters: query.get('filters') ?? undefined },
    });
  }
  if (method === 'POST' && route.kind === 'networks-create') {
    const name = str(asRecord(body).Name);
    return decision('approval', 4, [`creates network ${name}`], `docker network create ${name}`, name, {
      details: { driver: str(asRecord(body).Driver) || undefined, internal: asRecord(body).Internal === true },
    });
  }
  if (method === 'POST' && route.kind === 'network' && networkName !== undefined && (route.action === 'connect' || route.action === 'disconnect')) {
    const container = nameOf(str(asRecord(body).Container));
    return decision('approval', 4, [`${route.action}s ${container} ${route.action === 'connect' ? 'to' : 'from'} network ${networkName}`], `docker network ${route.action} ${networkName} ${container}`, networkName);
  }
  // Non-OET resources: operations that rules 2/3 would allow on OET objects.
  if (route.kind === 'container' && containerName !== undefined && !isOetName(containerName) && route.action !== null) {
    if (isRead && (CONTAINER_READ_ACTIONS.has(route.action) || route.action === 'logs')) {
      return decision('approval', 4, [`reads non-OET container ${containerName}`], `docker ${route.action === 'logs' ? 'logs' : 'inspect'} ${containerName}`, containerName, {
        grantKey: `read:${containerName}`,
        ...(route.action === 'json' ? { transform: 'container-inspect' as const } : {}),
      });
    }
    if (method === 'POST' && LIFECYCLE_ACTIONS.has(route.action)) {
      return decision('approval', 4, [`${route.action} on non-OET container ${containerName}`], `docker ${route.action} ${containerName}`, containerName, {
        grantKey: `lifecycle:${containerName}`,
      });
    }
  }
  if (isRead && route.kind === 'image' && route.action !== null && IMAGE_READ_ACTIONS.has(route.action)) {
    return decision('approval', 4, [`reads non-OET image ${route.name}`], `docker image inspect ${route.name}`, route.name, {
      grantKey: `read-image:${route.name}`,
    });
  }
  if (isRead && route.kind === 'network' && route.action === null && networkName !== undefined && !isOetName(networkName)) {
    return decision('approval', 4, [`reads non-OET network ${networkName}`], `docker network inspect ${networkName}`, networkName, {
      grantKey: `read-network:${networkName}`,
      transform: 'network-inspect',
    });
  }
  if (isRead && route.kind === 'volume' && !isOetName(route.name)) {
    return decision('approval', 4, [`reads non-OET volume ${route.name}`], `docker volume inspect ${route.name}`, route.name, {
      grantKey: `read-volume:${route.name}`,
    });
  }

  // ── Rule 5: everything else ──────────────────────────────────────────────
  return decision('deny', 5, ['operation not permitted by the oet-agent-dockerproxy policy'], request, containerName ?? networkName ?? '');
}
