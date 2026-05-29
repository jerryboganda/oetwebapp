using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Test double for <see cref="ILaunchReadinessService"/>. <see cref="Approved"/>
/// returns a snapshot with every realtime launch-readiness gate satisfied so the
/// Conversation realtime ASR provider/selector can resolve a real provider once
/// the <c>ConversationOptions</c> gates also pass. Negative-path tests fail on the
/// <c>ConversationOptions</c> gates first, so an approved readiness leaves their
/// assertions intact while letting the single "all gates pass" test succeed.
/// </summary>
internal sealed class FakeLaunchReadinessService(AdminLaunchReadinessSettingsResponse settings)
    : ILaunchReadinessService
{
    public static FakeLaunchReadinessService Approved() => new(ApprovedSettings());

    public Task<AdminLaunchReadinessSettingsResponse> GetSettingsAsync(CancellationToken ct)
        => Task.FromResult(settings);

    public Task<AdminLaunchReadinessSettingsResponse> UpdateSettingsAsync(
        string? adminId,
        string? adminName,
        AdminLaunchReadinessSettingsRequest request,
        CancellationToken ct)
        => Task.FromResult(settings);

    public Task<PublicAppReleaseSettingsResponse> GetPublicReleasePolicyAsync(string? platform, CancellationToken ct)
        => throw new NotSupportedException();

    private static AdminLaunchReadinessSettingsResponse ApprovedSettings() => new(
        MobileMinSupportedVersion: "1.0.0",
        MobileLatestVersion: "1.0.0",
        MobileForceUpdate: false,
        IosAppStoreUrl: null,
        AndroidPlayStoreUrl: null,
        IosBundleId: null,
        AppleTeamId: null,
        AppleAssociatedDomainStatus: null,
        AppleUniversalLinksStatus: null,
        IosSigningProfileReference: null,
        IosIapStatus: null,
        IosPushStatus: null,
        AndroidPackageName: null,
        AndroidSha256Fingerprints: null,
        AndroidSigningKeyReference: null,
        AndroidAssetLinksStatus: null,
        AndroidIapStatus: null,
        AndroidPushStatus: null,
        DesktopMinSupportedVersion: "1.0.0",
        DesktopLatestVersion: "1.0.0",
        DesktopForceUpdate: false,
        DesktopUpdateFeedUrl: null,
        DesktopUpdateChannel: null,
        WindowsSigningStatus: null,
        MacSigningStatus: null,
        LinuxSigningStatus: null,
        DeviceValidationEvidenceUrl: null,
        DeviceValidationNotes: null,
        RealtimeLegalApprovalStatus: "approved",
        RealtimePrivacyApprovalStatus: "approved",
        RealtimeProtectedSmokeStatus: "approved",
        RealtimeEvidenceUrl: "https://evidence.example.test/realtime",
        RealtimeSpendCapApproved: true,
        RealtimeTopologyApproved: true,
        ReleaseOwnerApprovalStatus: "approved",
        LaunchNotes: null,
        UpdatedAt: default,
        UpdatedByAdminId: null,
        UpdatedByAdminName: null);
}
