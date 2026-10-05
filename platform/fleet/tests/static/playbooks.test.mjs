// Static contract of the fleet Ansible content and the agent image definition (protocol 7.4, 8.2, 8.6, 10.1). Pure file reads:
// no Ansible, no Docker. Runs on GitHub Actions only (node --test).
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const fleet = resolve(here, '../..');
const ansible = join(fleet, 'ansible');
const roles = join(ansible, 'roles');

function walk(dir, filter = () => true, out = []) {
  for (const entry of readdirSync(dir)) {
    if (['bin', 'obj', 'publish', 'node_modules'].includes(entry)) continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) walk(path, filter, out);
    else if (filter(path)) out.push(path);
  }
  return out;
}

const read = (path) => readFileSync(path, 'utf8');
const rel = (path) => relative(fleet, path).replaceAll('\\', '/');
const taskFiles = readdirSync(roles).map((role) => join(roles, role, 'tasks', 'main.yml')).filter(existsSync);
const playbooks = ['preflight', 'bootstrap', 'agent', 'drain', 'repair', 'remove'].map((name) => join(ansible, `${name}.yml`));
const allYaml = walk(ansible, (path) => /\.ya?ml$/.test(path));

/** Splits a tasks file into task chunks (a chunk starts at a "- name:" line at the same indentation as the first). */
function tasksOf(text) {
  const lines = text.split('\n');
  const chunks = [];
  let current = null;
  for (const line of lines) {
    if (/^\s*- name: /.test(line)) {
      if (current) chunks.push(current);
      current = [line];
    } else if (current) {
      current.push(line);
    }
  }
  if (current) chunks.push(current);
  return chunks.map((chunk) => chunk.join('\n'));
}

test('every playbook and every role the playbooks use exists', () => {
  for (const playbook of playbooks) assert.ok(existsSync(playbook), `${rel(playbook)} is missing`);
  for (const playbook of playbooks) {
    for (const match of read(playbook).matchAll(/^\s+- role: (\w+)/gm)) {
      assert.ok(existsSync(join(roles, match[1], 'tasks', 'main.yml')), `role ${match[1]} has no tasks/main.yml`);
    }
  }
  for (const file of ['ansible.cfg', 'requirements.yml', 'README.md', 'group_vars/all.yml']) {
    assert.ok(existsSync(join(ansible, file)), `${file} is missing`);
  }
});

test('collections are pinned to an exact version (no latest, no ranges, no floating Galaxy installs)', () => {
  const text = read(join(ansible, 'requirements.yml')).split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');
  const entries = [...text.matchAll(/- name: ([\w.]+)\s+version: (\S+)/g)];
  assert.ok(entries.length >= 1);
  for (const [, name, version] of entries) {
    assert.match(version, /^\d+\.\d+\.\d+$/, `${name} must be pinned exactly, got ${version}`);
  }
  assert.doesNotMatch(text, /latest|[<>~^]=?\s*\d/);
});

test('no fleet file ever uses accept-new or disables host key checking', () => {
  const files = walk(fleet, (path) => /\.(yml|yaml|cs|csproj|sh|cfg|md|j2)$/.test(path) || /oet-fleet-(gate|ctl)$|Dockerfile/.test(path))
    .filter((path) => !rel(path).startsWith('tests/static/') && !rel(path).startsWith('scripts/'));
  for (const file of files) {
    const text = read(file);
    assert.doesNotMatch(text, /accept-new/, rel(file));
    assert.doesNotMatch(text, /StrictHostKeyChecking\s*=\s*no/i, rel(file));
    assert.doesNotMatch(text, /UserKnownHostsFile\s*=\s*\/dev\/null/, rel(file));
  }
});

test('no playbook or role contains a destructive or unbounded command', () => {
  const forbidden = [
    /docker\s+system\s+prune/, /docker\s+volume\b/, /docker\s+network\s+(rm|prune)/, /docker\s+image\s+prune/, /docker\s+container\s+prune/,
    /docker\s+compose\s+down/, /\bmkfs\b/, /\bwipefs\b/, /\bdd\s+if=/, /rm\s+-rf\s+\/(?!run\b|etc\/oet-fleet|var\/lib\/oet-fleet)/,
    /--force-yes/, /curl[^|\n]*\|\s*(sudo\s+)?(ba)?sh/, /:latest\b/, /@latest\b/,
  ];
  for (const file of [...allYaml, ...walk(join(roles, 'fleet_user', 'files'))]) {
    const text = read(file).split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');
    for (const pattern of forbidden) assert.doesNotMatch(text, pattern, `${rel(file)} matches ${pattern}`);
  }
});

test('only fleet-owned containers are ever removed by name', () => {
  for (const file of allYaml) {
    for (const line of read(file).split('\n')) {
      if (!/docker\s+rm\b/.test(line) || /^\s*#/.test(line)) continue;
      assert.match(line, /fleet_container_name|fleet_cadvisor_container_name|"\$name"/, `${rel(file)}: ${line.trim()}`);
    }
  }
});

test('secrets use no_log and travel on stdin, never in an argument vector', () => {
  for (const file of taskFiles) {
    for (const chunk of tasksOf(read(file))) {
      if (!/fleet_(node|ghcr)_token|fleet_env_text/.test(chunk)) continue;
      if (/ansible\.builtin\.assert/.test(chunk)) continue;
      assert.match(chunk, /no_log:\s*true/, `${rel(file)}: a task handling a secret lacks no_log\n${chunk.split('\n')[0]}`);
    }
  }
  for (const file of allYaml) {
    for (const line of read(file).split('\n')) {
      if (!/fleet_ghcr_token/.test(line) || /^\s*#/.test(line)) continue;
      assert.match(line, /stdin:|is defined|fail_msg:/, `${rel(file)}: ${line.trim()}`);
    }
  }
  const agent = read(join(roles, 'agent', 'tasks', 'main.yml'));
  assert.match(agent, /stdin: .*fleet_ghcr_token/);
  assert.doesNotMatch(agent, /docker\s+login/);
  assert.doesNotMatch(agent, /\bdocker\s+run\b/, 'the agent role must start the container through oet-fleet-ctl so the flags stay fixed');
});

test('the agent is installed by digest through the control script, with login/logout around the pull', () => {
  const agent = read(join(roles, 'agent', 'tasks', 'main.yml'));
  for (const verb of ['login', 'pull', 'verify', 'logout', 'put-env', 'unit-sync', 'run']) {
    assert.match(agent, new RegExp(`\\b${verb}\\b`), `verb ${verb} missing`);
  }
  assert.match(agent, /always:/, 'logout must run in an always section');
  assert.match(agent, /fleet_agent_repository \}\}@\{\{ fleet_agent_digest/);
  assert.match(agent, /sha256:\[0-9a-f\]\{64\}/);
});

test('budgets scale with the hardware exactly like the protocol default (4 vCPU / 8 GiB gives 3000 / 5120 / 3072)', () => {
  const agent = read(join(roles, 'agent', 'tasks', 'main.yml'));
  const cpu = /\(\(ansible_processor_vcpus \| int\) \* 750 \/\/ 250\) \* 250/.test(agent);
  const mem = /\(\(\(ansible_memtotal_mb \| int\) \* 65 \/\/ 100\) \/\/ 256\) \* 256/.test(agent);
  const tmp = /\* 60 \/\/ 100\) \/\/ 256 \* 256/.test(agent);
  assert.ok(cpu && mem && tmp, 'budget formulas changed');
  // The same arithmetic, evaluated.
  const cores = 4;
  const memTotal = 7900;
  const cpuMilli = Math.floor((cores * 750) / 250) * 250;
  const memMib = Math.floor(Math.floor((memTotal * 65) / 100) / 256) * 256;
  const tmpMib = Math.floor(Math.floor((memMib * 60) / 100) / 256) * 256;
  assert.deepEqual([cpuMilli, memMib, tmpMib], [3000, 5120, 3072]);
});

test('preflight knows every supported OS and refuses everything else with precise reasons', () => {
  const vars = read(join(ansible, 'group_vars', 'all.yml'));
  assert.match(vars, /Ubuntu: \["22\.04", "24\.04", "26\.04"\]/);
  assert.match(vars, /Debian: \["12", "13"\]/);
  assert.match(vars, /fleet_supported_arch: x86_64/);
  assert.match(vars, /185\.252\.233\.186/);
  const preflight = read(join(roles, 'preflight', 'tasks', 'main.yml'));
  for (const reason of ['os_unsupported', 'arch_unsupported', 'cpu_below_min', 'mem_below_min', 'disk_below_min', 'time_skew', 'no_systemd', 'existing_oet_workload', 'foreign_workloads', 'forbidden_host']) {
    assert.match(preflight, new RegExp(`- name: ${reason}`), reason);
  }
  assert.doesNotMatch(preflight, /ansible\.builtin\.(apt|package|file|user|service|systemd|lineinfile|template):/, 'preflight must be read-only');
  // The one copy writes the report on the CONTROLLER, never on the helper.
  const reportTask = tasksOf(preflight).find((chunk) => /ansible\.builtin\.copy/.test(chunk));
  assert.match(reportTask, /delegate_to: localhost/);
});

test('Docker is reused when present and installed only from a pinned key and a held series', () => {
  const docker = read(join(roles, 'docker_engine', 'tasks', 'main.yml'));
  assert.match(docker, /fleet_docker_probe\.rc != 0 and fleet_docker_cli\.rc != 0/);
  assert.match(docker, /fleet_docker_key_fingerprint/);
  assert.match(read(join(ansible, 'group_vars', 'all.yml')), /fleet_docker_key_fingerprint: 9DC858229FC7DD38854AE2D88D81803C0EBFCD88/);
  assert.match(docker, /dpkg_selections/);
  assert.match(docker, /rescue:/);
  assert.doesNotMatch(docker, /apt-key|add-apt-repository|get\.docker\.com/);
});

test('the firewall keeps SSH reachable before it is enabled and leaves hosts with other services alone', () => {
  const firewall = read(join(roles, 'firewall', 'tasks', 'main.yml'));
  const enable = firewall.indexOf('state: enabled');
  assert.ok(enable > 0);
  for (const marker of ['comment: oet-fleet-manager', 'comment: oet-fleet-session', 'default: "{{ item.policy }}"']) {
    const index = firewall.indexOf(marker);
    assert.ok(index > 0 && index < enable, `${marker} must come before the firewall is enabled`);
  }
  assert.match(firewall, /foreign_workloads/);
  assert.match(firewall, /fleet_force_firewall/);
});

test('SSH hardening verifies a second connection and restores the previous configuration on any doubt', () => {
  const ssh = read(join(roles, 'harden_ssh', 'tasks', 'main.yml'));
  assert.match(ssh, /lockout_risk/);
  assert.match(ssh, /rescue:/);
  assert.match(ssh, /StrictHostKeyChecking=yes/);
  assert.match(ssh, /UserKnownHostsFile=/);
  assert.match(ssh, /delegate_to: localhost/);
  assert.match(ssh, /sshd -t/);
  assert.match(ssh, /oet-fleet-ctl\s+- status/);
  const dropin = read(join(roles, 'harden_ssh', 'templates', 'sshd-hardening.conf.j2'));
  for (const line of ['PasswordAuthentication no', 'KbdInteractiveAuthentication no', 'X11Forwarding no', 'MaxAuthTries 3', 'AllowAgentForwarding no']) {
    assert.match(dropin, new RegExp(`^${line}$`, 'm'));
  }
  assert.doesNotMatch(dropin, /PermitRootLogin|AllowUsers|DenyUsers/, 'root login policy must not be modified');
});

test('the fleet user can run exactly one command as root and cannot edit its own key', () => {
  const sudoers = read(join(roles, 'fleet_user', 'templates', 'sudoers.j2'));
  assert.equal([...sudoers.matchAll(/^\{\{ fleet_user \}\} ALL=/gm)].length, 1);
  assert.match(sudoers, /NOPASSWD: \{\{ fleet_ctl_path \}\}$/m);
  assert.doesNotMatch(sudoers, /ALL=\(ALL\)|NOPASSWD:\s*ALL/);
  const tasks = read(join(roles, 'fleet_user', 'tasks', 'main.yml'));
  assert.match(tasks, /restrict,command="\{\{ fleet_gate_path \}\}",no-pty/);
  assert.match(tasks, /dest: "\{\{ fleet_authorized_keys_path \}\}"\s+owner: root/);
  assert.match(tasks, /validate: \/usr\/sbin\/visudo -cf %s/);
  assert.match(tasks, /ssh-ed25519/);
});

test('remove is confirmed, fleet-scoped and never uninstalls Docker or touches the firewall by default', () => {
  const remove = read(join(roles, 'remove', 'tasks', 'main.yml'));
  assert.match(remove, /fleet_confirm_remove \| default\(''\) == fleet_remove_confirmation_phrase/);
  assert.match(remove, /fleet_remove_docker \| default\(false\)/);
  assert.match(remove, /fleet_remove_firewall_rules \| default\(false\)/);
  assert.match(remove, /installed_docker/);
  assert.doesNotMatch(remove, /docker\s+(system|volume|network)/);
});

test('exporters listen on loopback, pin cAdvisor by digest and never mount the root filesystem, docker state or the socket', () => {
  const vars = read(join(ansible, 'group_vars', 'all.yml'));
  assert.match(vars, /fleet_node_exporter_listen: 127\.0\.0\.1:9100/);
  assert.match(vars, /fleet_cadvisor_listen: 127\.0\.0\.1:8081/);
  assert.match(vars, /fleet_cadvisor_image: "[a-z0-9./:_-]+@sha256:[0-9a-f]{64}"/);
  const exporters = read(join(roles, 'exporters', 'tasks', 'main.yml')).split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');
  assert.match(exporters, /--publish 127\.0\.0\.1:/);
  assert.match(exporters, /--read-only/);
  assert.match(exporters, /--cap-drop ALL/);
  assert.doesNotMatch(exporters, /docker\.sock|\/var\/lib\/docker|--volume \/:|-v \/:|--privileged/);
});

test('the agent Dockerfile and dockerignore keep the context to the publish output and the runtime non-root', () => {
  const dockerfile = read(join(fleet, 'src', 'Fleet.Agent', 'Dockerfile'));
  assert.match(dockerfile, /^FROM --platform=linux\/amd64 mcr\.microsoft\.com\/dotnet\/runtime:10\.0$/m);
  assert.match(dockerfile, /^COPY platform\/fleet\/publish\/agent\/ \.\/$/m);
  assert.match(dockerfile, /^USER 10001:10001$/m);
  assert.doesNotMatch(dockerfile, /^(ENV|ARG)\s.*\b(LANG|LC_ALL|DOTNET_SYSTEM_GLOBALIZATION_INVARIANT)\b/m);
  assert.doesNotMatch(dockerfile, /\b(tesseract|openssh|sudo|docker\.io)\b/i);
  const ignore = read(join(fleet, 'src', 'Fleet.Agent', 'Dockerfile.dockerignore'));
  assert.match(ignore, /^\*\*$/m);
  assert.match(ignore, /^!platform\/fleet\/publish\/agent\/\*\*$/m);
});

test('the agent runs as the documented child-process worker and the host config is stateless', () => {
  const program = read(join(fleet, 'src', 'Fleet.Agent', 'Program.cs'));
  assert.match(program, /"--child"/);
  assert.match(program, /return 2;/);
  const options = read(join(fleet, 'src', 'Fleet.Agent', 'AgentOptions.cs'));
  for (const key of ['OET_API_BASE', 'OET_NODE_ID', 'OET_NODE_TOKEN', 'OET_AGENT_IMAGE_DIGEST', 'OET_BUDGET_CPU_MILLI', 'OET_BUDGET_MEM_MIB', 'OET_BUDGET_TMP_MIB', 'OET_LOG_LEVEL']) {
    assert.match(options, new RegExp(key), key);
  }
  // Every key the agent reads is on the put-env allow-list of oet-fleet-ctl, and vice versa.
  const ctl = read(join(roles, 'fleet_user', 'files', 'oet-fleet-ctl'));
  const allowList = [...ctl.matchAll(/^\s+(OET_[A-Z_]+)\)/gm)].map((match) => match[1]).sort();
  const agentKeys = [...new Set([...options.matchAll(/"(OET_[A-Z_]+)"/g)].map((match) => match[1]))].sort();
  assert.deepEqual(allowList, agentKeys);
});
