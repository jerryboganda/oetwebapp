import { spawnSync } from 'node:child_process';

const mode = process.argv[2] === 'full' ? 'full' : 'smoke';
const playwrightBin = process.platform === 'win32' ? 'npx.cmd' : 'npx';

const learnerDesktopProjects = ['chromium-learner', 'firefox-learner', 'webkit-learner', 'sydney-learner'];
const learnerProjects = [...learnerDesktopProjects, 'mobile-chromium-learner', 'mobile-webkit-learner'];
const expertProjects = ['chromium-expert', 'firefox-expert', 'webkit-expert'];
const adminProjects = ['chromium-admin', 'firefox-admin', 'webkit-admin'];

function quoteCmdArg(value) {
  if (!/[ \t"&|<>^()]/.test(value)) {
    return value;
  }

  return `"${value.replaceAll('"', '\\"')}"`;
}

function projectArgs(projects) {
  return projects.flatMap((project) => ['--project', project]);
}

function learnerProjectSmokeRuns(project) {
  return [
    {
      label: `learner: deep-link smoke on ${project}`,
      args: playwrightArgs({
        files: ['tests/e2e/learner/deep-link-smoke.spec.ts'],
        projects: [project],
        workers: 1,
      }),
    },
    {
      label: `learner: workspace smoke on ${project}`,
      args: playwrightArgs({
        files: ['tests/e2e/learner/learner-smoke.spec.ts'],
        projects: [project],
        workers: 1,
      }),
    },
  ];
}

function playwrightArgs({ files, projects, grep, workers }) {
  const args = ['playwright', 'test'];
  args.push(...files);
  if (grep) {
    args.push('--grep', grep);
  }
  if (workers !== undefined) {
    args.push('--workers', String(workers));
  }
  args.push(...projectArgs(projects));
  return args;
}

const continueOnFailure = process.env.PLAYWRIGHT_CONTINUE_ON_FAILURE === '1';
const failedRuns = [];

function run(label, args) {
  console.log(`\n==> ${label}`);
  const result = process.platform === 'win32'
    ? spawnSync('cmd.exe', ['/d', '/s', '/c', [playwrightBin, ...args].map(quoteCmdArg).join(' ')], {
        cwd: process.cwd(),
        stdio: 'inherit',
        env: process.env,
      })
    : spawnSync(playwrightBin, args, {
        cwd: process.cwd(),
        stdio: 'inherit',
        env: process.env,
      });

  if (result.status !== 0) {
    const exitCode = typeof result.status === 'number' ? result.status : 1;
    if (continueOnFailure) {
      failedRuns.push({ label, exitCode });
      console.log(`==> [continue-on-failure] ${label} failed with exit ${exitCode}; continuing.`);
      return;
    }
    process.exit(exitCode);
  }
}

function verifyReadiness() {
  const result = spawnSync(process.execPath, ['scripts/qa/assert-local-stack.mjs'], {
    cwd: process.cwd(),
    stdio: 'inherit',
    env: process.env,
  });

  if (result.status !== 0) {
    process.exit(typeof result.status === 'number' ? result.status : 1);
  }
}

const smokeRuns = [
  {
    label: 'auth: unauthenticated redirects',
    args: playwrightArgs({
      files: ['tests/e2e/auth/auth.spec.ts'],
      projects: ['chromium-unauth'],
    }),
  },
  ...learnerProjectSmokeRuns('chromium-learner'),
  ...learnerProjectSmokeRuns('firefox-learner'),
  ...learnerProjectSmokeRuns('webkit-learner'),
  ...learnerProjectSmokeRuns('sydney-learner'),
  ...learnerProjectSmokeRuns('mobile-chromium-learner'),
  ...learnerProjectSmokeRuns('mobile-webkit-learner'),
  {
    label: 'learner: billing smoke on chromium',
    args: playwrightArgs({
      files: ['tests/e2e/billing.smoke.spec.ts'],
      projects: ['chromium-learner'],
      workers: 1,
    }),
  },
  {
    label: 'learner: chromium-only immersive flows',
    args: playwrightArgs({
      files: [
        'tests/e2e/learner/player-workflows.spec.ts',
        'tests/e2e/learner/immersive-completion.spec.ts',
      ],
      projects: ['chromium-learner'],
      workers: 1,
    }),
  },
  {
    label: 'expert: smoke details across desktop projects',
    args: playwrightArgs({
      files: ['tests/e2e/expert/detail-smoke.spec.ts'],
      projects: expertProjects,
    }),
  },
  {
    label: 'expert: privileged smoke on expert sessions',
    args: playwrightArgs({
      files: ['tests/e2e/expert/privileged-smoke.spec.ts'],
      projects: expertProjects,
      grep: 'expert',
      workers: 1,
    }),
  },
  {
    label: 'admin: privileged smoke on admin sessions',
    args: playwrightArgs({
      files: ['tests/e2e/expert/privileged-smoke.spec.ts'],
      projects: adminProjects,
      grep: 'admin',
      workers: 1,
    }),
  },
  {
    label: 'expert: chromium-only review flows',
    args: playwrightArgs({
      files: [
        'tests/e2e/expert/review-workflows.spec.ts',
        'tests/e2e/expert/review-completion.spec.ts',
      ],
      projects: ['chromium-expert'],
    }),
  },
  {
    label: 'admin: smoke details across desktop projects',
    args: playwrightArgs({
      files: ['tests/e2e/admin/detail-smoke.spec.ts'],
      projects: adminProjects,
    }),
  },
  {
    label: 'admin: chromium-only workflows and mutations',
    args: playwrightArgs({
      files: [
        'tests/e2e/admin/admin-workflows.spec.ts',
        'tests/e2e/admin/admin-deep-mutations.spec.ts',
      ],
      projects: ['chromium-admin'],
    }),
  },
  {
    label: 'role guards: smoke protection checks',
    args: playwrightArgs({
      files: ['tests/e2e/shared/role-guards.spec.ts'],
      projects: ['chromium-learner'],
    }),
  },
  {
    label: 'role guards: expert protection checks',
    args: playwrightArgs({
      files: ['tests/e2e/shared/role-guards.spec.ts'],
      projects: ['chromium-expert'],
    }),
  },
  {
    label: 'role guards: admin protection checks',
    args: playwrightArgs({
      files: ['tests/e2e/shared/role-guards.spec.ts'],
      projects: ['chromium-admin'],
    }),
  },
];

const fullRuns = [
  ...smokeRuns,
  {
    label: 'accessibility: unauthenticated sign-in screen',
    args: playwrightArgs({
      files: ['tests/e2e/shared/accessibility.spec.ts'],
      projects: ['chromium-unauth'],
      grep: 'sign in screen',
    }),
  },
  {
    label: 'accessibility: learner desktop surfaces',
    args: playwrightArgs({
      files: ['tests/e2e/shared/accessibility.spec.ts'],
      projects: ['chromium-learner'],
      grep: 'learner dashboard|settings profile',
    }),
  },
  {
    label: 'accessibility: expert queue',
    args: playwrightArgs({
      files: ['tests/e2e/shared/accessibility.spec.ts'],
      projects: ['chromium-expert'],
      grep: 'expert queue',
    }),
  },
  {
    label: 'accessibility: admin content library',
    args: playwrightArgs({
      files: ['tests/e2e/shared/accessibility.spec.ts'],
      projects: ['chromium-admin'],
      grep: 'admin content library',
    }),
  },
  {
    label: 'notifications: learner desktop',
    args: playwrightArgs({
      files: ['tests/e2e/shared/notifications.spec.ts'],
      projects: learnerDesktopProjects,
      grep: 'learner desktop bell',
    }),
  },
  {
    label: 'notifications: learner mobile',
    args: playwrightArgs({
      files: ['tests/e2e/shared/notifications.spec.ts'],
      projects: ['mobile-chromium-learner', 'mobile-webkit-learner'],
      grep: 'learner mobile bell',
    }),
  },
  {
    label: 'notifications: expert desktop',
    args: playwrightArgs({
      files: ['tests/e2e/shared/notifications.spec.ts'],
      projects: expertProjects,
      grep: 'expert desktop receives',
    }),
  },
  {
    label: 'notifications: admin desktop',
    args: playwrightArgs({
      files: ['tests/e2e/shared/notifications.spec.ts'],
      projects: adminProjects,
      grep: 'admin notification operations',
    }),
  },
];

verifyReadiness();

const runs = mode === 'full' ? fullRuns : smokeRuns;

for (const { label, args } of runs) {
  run(label, args);
}

if (continueOnFailure && failedRuns.length > 0) {
  console.log(`\nPlaywright ${mode} matrix completed with ${failedRuns.length} failed bucket(s):`);
  for (const failure of failedRuns) {
    console.log(`  - ${failure.label} (exit ${failure.exitCode})`);
  }
  process.exit(1);
}

console.log(`\nPlaywright ${mode} matrix completed without skipped-role routing.`);
