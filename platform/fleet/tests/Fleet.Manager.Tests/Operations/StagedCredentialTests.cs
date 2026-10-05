using System.Text;
using Fleet.Core.Validation;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Operations;

/// <summary>
/// The owner key saved BEFORE the host key is confirmed (so one form can add a helper), and keys that come with a passphrase. The key keeps every rule
/// of the one submitted later: encrypted in the vault, 60 minutes at most, destroyed at S8 / on cancel / on expiry, never audited, never logged, and
/// never USED before the owner has pinned the host key.
/// </summary>
public sealed class StagedCredentialTests : IAsyncLifetime
{
    private const string ProtectedKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nPASSPHRASE-PROTECTED\n-----END OPENSSH PRIVATE KEY-----\n";

    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private FleetWorld World => _host.World;

    private CredentialStore Credentials => _host.Get<CredentialStore>();

    private Task<OperationView> StageAsync(string operationId, string key, string? passphrase = null) =>
        _driver.Enrollment.StageOwnerCredentialAsync(operationId, "root", key, passphrase, "owner", CancellationToken.None);

    [Fact]
    public async Task A_key_saved_before_the_host_key_check_is_used_the_moment_the_host_key_is_confirmed_and_is_gone_at_the_end()
    {
        var (operation, helper) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;

        var staged = await StageAsync(operation.Id, helper.OwnerKeyText);

        Assert.Equal("Created", staged.State);
        Assert.True(await Credentials.ExistsActiveAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        var scanned = await _driver.RunAsync(operation.Id);
        Assert.Equal("HostKeyPending", scanned.State);
        Assert.Equal(0, helper.ApplyCounts.Values.Sum());

        // Nothing touches the helper until the owner pins the host key; pinning continues with the saved key.
        var confirmed = await _driver.ConfirmAsync(operation.Id, helper);
        Assert.Equal("Bootstrapping", confirmed.State);

        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        var finished = await _driver.RunAsync(operation.Id);

        Assert.Equal("Active", finished.State);
        Assert.False(await Credentials.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None), "the owner key is destroyed at S8");
        var audit = await _host.Get<IAuditService>().ListAsync(500);
        Assert.Contains(audit, row => row.Action == "enroll.owner_credential_staged");
        Assert.Contains(audit, row => row.Action == "enroll.saved_credential_used");
        var marker = helper.OwnerKeyText.Split('\n')[1];
        Assert.DoesNotContain(audit, row => row.DetailsJson.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(marker, _host.Logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_key_never_lets_the_enrollment_run_before_the_host_key_is_pinned()
    {
        var (operation, helper) = await _driver.AddAsync();
        await StageAsync(operation.Id, helper.OwnerKeyText);

        var scanned = await _driver.RunAsync(operation.Id);

        Assert.Equal("HostKeyPending", scanned.State);
        Assert.Null((await _driver.HostAsync()).HostKeySha256);
        Assert.Empty(helper.ApplyCounts);
        await Assert.ThrowsAsync<FleetOperationException>(() =>
            _driver.Enrollment.ContinueWithSavedCredentialAsync(operation.Id, "owner", CancellationToken.None));
    }

    [Fact]
    public async Task Saving_in_advance_is_refused_after_the_host_key_is_pinned_and_the_same_call_then_submits_the_key()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        Assert.Equal("HostKeyConfirmed", operation.State);
        var hostId = (await _driver.HostAsync()).Id;

        var refusal = await Assert.ThrowsAsync<FleetOperationException>(() => StageAsync(operation.Id, helper.OwnerKeyText));
        Assert.Equal("invalid_state", refusal.Code);
        Assert.False(await Credentials.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));

        // SetOwnerCredentialAsync picks the right mode for the state: here the operation is waiting for the key, so it is used.
        var submitted = await _driver.Enrollment.SetOwnerCredentialAsync(operation.Id, "root", helper.OwnerKeyText, null, "owner", CancellationToken.None);
        Assert.Equal("Bootstrapping", submitted.State);
    }

    [Fact]
    public async Task A_saved_key_that_expired_is_not_used_and_the_owner_is_asked_for_it_again()
    {
        var (operation, helper) = await _driver.AddAsync();
        await StageAsync(operation.Id, helper.OwnerKeyText);
        operation = await _driver.RunAsync(operation.Id);

        World.Time.Advance(TimeSpan.FromMinutes(61));
        operation = await _driver.ConfirmAsync(operation.Id, helper);

        Assert.Equal("HostKeyConfirmed", operation.State);
        Assert.True(operation.AwaitingOwnerCredential);
        var refusal = await Assert.ThrowsAsync<FleetOperationException>(() =>
            _driver.Enrollment.ContinueWithSavedCredentialAsync(operation.Id, "owner", CancellationToken.None));
        Assert.Equal("owner_credential_required", refusal.Code);

        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        Assert.Equal("Bootstrapping", operation.State);
    }

    [Fact]
    public async Task Cancelling_the_operation_erases_a_saved_key()
    {
        var (operation, helper) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;
        await StageAsync(operation.Id, helper.OwnerKeyText);

        await _driver.Enrollment.CancelAsync(operation.Id, "owner", CancellationToken.None);

        Assert.False(await Credentials.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
    }

    [Fact]
    public async Task A_key_that_cannot_be_read_is_not_saved()
    {
        var (operation, _) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;

        var unreadable = await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, ProtectedKey));

        Assert.Equal("owner_key_unusable", unreadable.Issues.Single().Code);
        Assert.False(await Credentials.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, "not a key at all"));
    }

    [Fact]
    public async Task A_protected_key_is_opened_with_its_passphrase_which_is_used_once_and_kept_nowhere()
    {
        var (operation, helper) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;

        await StageAsync(operation.Id, ProtectedKey, FakeSshKeyTool.CorrectPassphrase);

        Assert.Contains(FakeSshKeyTool.CorrectPassphrase, World.Keys.TriedPassphrases);
        using (var stored = await Credentials.OpenAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None))
        {
            Assert.NotNull(stored);
            var (user, key) = OwnerCredentialPayload.Unpack(stored!);
            using (key)
            {
                Assert.Equal("root", user);
                var text = Encoding.UTF8.GetString(key.AsSpan());
                Assert.Contains("UNPROTECTED", text);
                Assert.DoesNotContain("PASSPHRASE-PROTECTED", text);
            }
        }

        var audit = await _host.Get<IAuditService>().ListAsync(500);
        Assert.DoesNotContain(audit, row => row.DetailsJson.Contains(FakeSshKeyTool.CorrectPassphrase, StringComparison.Ordinal));
        Assert.DoesNotContain(FakeSshKeyTool.CorrectPassphrase, _host.Logs.All, StringComparison.Ordinal);
        Assert.NotNull(helper);
    }

    [Fact]
    public async Task A_wrong_passphrase_stores_nothing_and_says_the_key_could_not_be_opened()
    {
        var (operation, _) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;

        var refusal = await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, ProtectedKey, "not the passphrase"));

        Assert.Equal("owner_key_unusable", refusal.Issues.Single().Code);
        Assert.Contains("could not be opened", refusal.Issues.Single().Message);
        Assert.False(await Credentials.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
    }

    [Fact]
    public async Task A_legacy_pem_key_that_announces_itself_as_encrypted_is_accepted_only_together_with_a_passphrase()
    {
        const string legacy = "-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nPASSPHRASE-PROTECTED\n-----END RSA PRIVATE KEY-----\n";
        var (operation, _) = await _driver.AddAsync();

        var without = await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, legacy));
        Assert.Equal("owner_key_invalid", without.Issues.Single().Code);

        var staged = await StageAsync(operation.Id, legacy, FakeSshKeyTool.CorrectPassphrase);
        Assert.Equal("Created", staged.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("line one\nline two")]
    [InlineData("has a nul \0 inside")]
    public async Task A_passphrase_must_be_one_line_of_at_most_256_characters(string passphrase)
    {
        var (operation, helper) = await _driver.AddAsync();

        // An empty passphrase simply means "no passphrase": the key is judged on its own.
        if (passphrase.Length == 0)
        {
            Assert.Equal("Created", (await StageAsync(operation.Id, helper.OwnerKeyText, passphrase)).State);
            return;
        }

        var refusal = await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, helper.OwnerKeyText, passphrase));
        Assert.Equal("passphrase_invalid", refusal.Issues.Single().Code);
        var tooLong = await Assert.ThrowsAsync<FleetValidationException>(() => StageAsync(operation.Id, helper.OwnerKeyText, new string('x', 257)));
        Assert.Equal("passphrase_invalid", tooLong.Issues.Single().Code);
    }

    [Fact]
    public async Task The_acceptance_of_a_key_follows_the_state_of_the_operation()
    {
        var (operation, helper) = await _driver.AddAsync();
        var store = _host.Get<OperationStore>();

        async Task<OwnerKeyAcceptance> NowAsync() => EnrollmentService.AcceptanceFor((await store.GetAsync(operation.Id, CancellationToken.None))!);

        Assert.Equal(OwnerKeyAcceptance.Stage, await NowAsync());
        await _driver.RunAsync(operation.Id);
        Assert.Equal(OwnerKeyAcceptance.Stage, await NowAsync());
        await _driver.ConfirmAsync(operation.Id, helper);
        Assert.Equal(OwnerKeyAcceptance.Submit, await NowAsync());
        await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        Assert.Equal(OwnerKeyAcceptance.No, await NowAsync());
    }

    [Fact]
    public async Task The_json_api_saves_a_key_in_advance_and_accepts_a_passphrase_and_still_never_echoes_the_key()
    {
        await using var factory = new FleetWebFactory();
        var (client, secret, csrf) = await factory.SignedInAsync();
        using (client)
        {
            var helper = factory.World.Provisioner.AddHost("203.0.113.10");
            var driver = new EnrollmentDriver(factory.World, factory.Services);
            var (operation, _) = await driver.AddAsync();

            factory.Time.Advance(TimeSpan.FromSeconds(90));
            using var request = FleetWebFactory.ApiRequest(
                HttpMethod.Post,
                "/api/v1/operations/" + operation.Id + "/owner-credential",
                csrf,
                factory.Code(secret),
                new { user = "root", privateKey = helper.OwnerKeyText, passphrase = (string?)null });
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"state\":\"Created\"", text);
            Assert.DoesNotContain(helper.OwnerKeyText.Split('\n')[1], text, StringComparison.Ordinal);
            var hostId = (await driver.HostAsync()).Id;
            var store = factory.Services.GetRequiredService<CredentialStore>();
            Assert.True(await store.ExistsActiveAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        }
    }
}
