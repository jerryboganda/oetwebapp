using Fleet.Core.Ssh;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>Plays the owner and the CI sync job against the real services so a test can say "enroll this helper" in one line.</summary>
public sealed class EnrollmentDriver
{
    public const string DefaultNodeRef = "helper-eu-01";
    public const string DefaultAddress = "203.0.113.10";

    private readonly FleetWorld _world;
    private readonly Func<IServiceProvider> _services;

    /// <summary>Drives a <see cref="FleetTestHost"/>; it follows the host across restarts because the provider is read on every use.</summary>
    public EnrollmentDriver(FleetTestHost host)
    {
        _world = host.World;
        _services = () => host.Services;
    }

    /// <summary>Drives any application built over the fake world (for example the web factory).</summary>
    public EnrollmentDriver(FleetWorld world, IServiceProvider services)
    {
        _world = world;
        _services = () => services;
    }

    private T Get<T>()
        where T : notnull => _services().GetRequiredService<T>();

    public EnrollmentService Enrollment => Get<EnrollmentService>();

    public HostService Hosts => Get<HostService>();

    public ReleaseService Releases => Get<ReleaseService>();

    public OperationRunner Runner => Get<OperationRunner>();

    public async Task<(OperationView Operation, FakeHost Helper)> AddAsync(
        string nodeRef = DefaultNodeRef,
        string address = DefaultAddress,
        int port = 22)
    {
        var helper = _world.Provisioner.Find(address, port) ?? _world.Provisioner.AddHost(address, port);
        var result = await Enrollment.AddHostAsync(
            new AddHostRequest(nodeRef, "Helper EU 01", address, port, "eu-central", "ExampleHost"),
            "owner",
            "request-1",
            CancellationToken.None);
        return (result.Operation, helper);
    }

    public async Task<OperationView> RunAsync(string operationId, CancellationToken cancellationToken = default)
    {
        await Runner.RunAsync(operationId, cancellationToken);
        return await Hosts.GetOperationAsync(operationId, CancellationToken.None);
    }

    /// <summary>Types the first 8 characters of the fingerprint, exactly as the owner does after comparing it with the provider console.</summary>
    public Task<OperationView> ConfirmAsync(string operationId, FakeHost helper) =>
        Enrollment.ConfirmHostKeyAsync(operationId, helper.Fingerprint["SHA256:".Length..][..8], "owner", CancellationToken.None);

    public Task<OperationView> SubmitOwnerKeyAsync(string operationId, FakeHost helper, string user = "root") =>
        Enrollment.SubmitOwnerCredentialAsync(operationId, user, helper.OwnerKeyText, "owner", CancellationToken.None);

    public static string Sha(char c) => new(c, 40);

    public ReleaseRecordInput Record(char shaChar = '1', string? digest = null, string? imageId = null) =>
        new(Sha(shaChar), "123456789", FleetCtlVerbs.AgentRepository, digest ?? FleetWorld.AgentDigest, imageId ?? FleetWorld.AgentImageId, null);

    public async Task<ReleaseView> ApproveReleaseAsync(char shaChar = '1', string? digest = null, string? imageId = null)
    {
        var release = await Releases.IngestAsync(Record(shaChar, digest, imageId), "ci", CancellationToken.None);
        return await Releases.ApproveAsync(release.Id, "owner", CancellationToken.None);
    }

    /// <summary>The CI sync job: the release record plus a job-scoped registry token (held in memory only).</summary>
    public Task<ReleaseView> SyncAsync(string token = "ghs_TESTONLYTOKEN0123456789abcdef") =>
        Releases.SyncAsync(new SyncPayload(Record(), "ci-user", token), "ci-sync", CancellationToken.None);

    /// <summary>The complete happy path up to and including activation.</summary>
    public async Task<(OperationView Operation, FakeHost Helper)> EnrollToActiveAsync(
        string nodeRef = DefaultNodeRef,
        string address = DefaultAddress)
    {
        var (operation, helper) = await AddAsync(nodeRef, address);
        operation = await RunAsync(operation.Id);
        operation = await ConfirmAsync(operation.Id, helper);
        operation = await SubmitOwnerKeyAsync(operation.Id, helper);
        await ApproveReleaseAsync();
        await SyncAsync();
        operation = await RunAsync(operation.Id);
        return (operation, helper);
    }

    public async Task<HostEntity> HostAsync(string nodeRef = DefaultNodeRef) =>
        await Get<HostStore>().FindByNodeRefAsync(nodeRef, CancellationToken.None) ?? throw new InvalidOperationException("no such host");
}
