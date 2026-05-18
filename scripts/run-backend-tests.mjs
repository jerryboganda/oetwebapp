import { spawnSync } from 'node:child_process';

const solution = 'backend/OetLearner.sln';

const batches = [
  ['Admin endpoints', 'FullyQualifiedName~OetLearner.Api.Tests.Admin'],
  ['AI services', 'FullyQualifiedName~OetLearner.Api.Tests.Ai'],
  ['Analytics API auth', [
    'FullyQualifiedName~OetLearner.Api.Tests.Analytics',
    'FullyQualifiedName~OetLearner.Api.Tests.Api',
    'FullyQualifiedName~OetLearner.Api.Tests.Auth',
  ].join('|')],
  ['B-C', [
    'FullyQualifiedName~OetLearner.Api.Tests.B',
    'FullyQualifiedName~OetLearner.Api.Tests.C',
  ].join('|')],
  ['D-F', [
    'FullyQualifiedName~OetLearner.Api.Tests.D',
    'FullyQualifiedName~OetLearner.Api.Tests.E',
    'FullyQualifiedName~OetLearner.Api.Tests.F',
  ].join('|')],
  ['G-M', [
    'FullyQualifiedName~OetLearner.Api.Tests.G',
    'FullyQualifiedName~OetLearner.Api.Tests.H',
    'FullyQualifiedName~OetLearner.Api.Tests.I',
    'FullyQualifiedName~OetLearner.Api.Tests.J',
    'FullyQualifiedName~OetLearner.Api.Tests.K',
    'FullyQualifiedName~OetLearner.Api.Tests.L',
    'FullyQualifiedName~OetLearner.Api.Tests.M',
  ].join('|')],
  ['Notification proof trigger', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.ProofTrigger_CreatesInboxItems_AndSupportsReadFlows'],
  ['Notification SQLite feed', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.FeedEndpoint_RemainsQueryable_WhenSqliteBacksDesktopRuntime'],
  ['Notification policy override', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.AdminPolicyOverride_DisablesEmail_ButKeepsInAppDelivery'],
  ['Notification channel switches', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.AdminGlobalChannelSwitches_AreReturnedForEachAudience'],
  ['Notification frequency cap', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.FrequencyCap_SuppressesRepeatedNonProtectedEmailDelivery'],
  ['Notification policy caps clear', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.AdminPolicyFrequencyCaps_CanBeCleared_AndContinueInheritingGlobalCaps'],
  ['Notification digest cap', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.DailyDigestFrequencyCap_SuppressesOverflowWithinSameDigestBatch'],
  ['Notification catalog', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.AdminCatalog_CoversExpandedOetLifecycleEvents'],
  ['Notification protected policy', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.ProtectedCriticalPolicies_CannotBeDisabled_ByAdminOrPreferenceOptOut'],
  ['Notification digest proof', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.DailyDigestProofDispatch_SendsDigestEmail_AndRecordsDelivery'],
  ['Notification quiet hours', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.QuietHoursPreference_DefersPushIntoQueuedFanoutJob'],
  ['Notification expired push', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.ExpiredPushSubscription_IsDisabled_WhenDispatcherReturns410'],
  ['Notification SMS consent', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.SmsConsent_CanBeStoredByCategory_AndReturnedWithGlobalDefaults'],
  ['Notification suppression', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationFlowsTests.AdminSuppression_DisablesEmailFanout_AndCanBeReleased'],
  ['Notification SignalR', 'FullyQualifiedName=OetLearner.Api.Tests.NotificationSignalRTests.SignalRHub_RoutesNotifications_ToMatchingAuthAccountGroupOnly'],
  ['O-S', [
    'FullyQualifiedName~OetLearner.Api.Tests.O',
    'FullyQualifiedName~OetLearner.Api.Tests.P',
    'FullyQualifiedName~OetLearner.Api.Tests.Q',
    'FullyQualifiedName~OetLearner.Api.Tests.R',
    'FullyQualifiedName~OetLearner.Api.Tests.S',
  ].join('|')],
  ['T-Z', [
    'FullyQualifiedName~OetLearner.Api.Tests.T',
    'FullyQualifiedName~OetLearner.Api.Tests.U',
    'FullyQualifiedName~OetLearner.Api.Tests.V',
    'FullyQualifiedName~OetLearner.Api.Tests.W',
    'FullyQualifiedName~OetLearner.Api.Tests.X',
    'FullyQualifiedName~OetLearner.Api.Tests.Y',
    'FullyQualifiedName~OetLearner.Api.Tests.Z',
  ].join('|')],
];

function run(label, args) {
  console.log(`\n--- ${label} ---`);
  const result = spawnSync('dotnet', args, { stdio: 'inherit', shell: false });
  if (result.error) {
    console.error(result.error);
    process.exit(1);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

run('Backend build', ['build', solution, '--tl:off']);

for (const [label, filter] of batches) {
  run(`Backend tests: ${label}`, ['test', solution, '--no-build', '--tl:off', '--filter', filter]);
}
