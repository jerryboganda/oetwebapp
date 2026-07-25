using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services;

[Authorize]
public sealed class NotificationHub(LearnerDbContext dbContext) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authAccountId = await ResolveAuthAccountIdAsync();
        if (string.IsNullOrWhiteSpace(authAccountId))
        {
            throw new HubException("auth_account_id_required");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, AccountGroup(authAccountId));

        // Security spec §3.1: join the refresh-token-family group too, so a
        // sign-in elsewhere can push an immediate `session_revoked` to THIS
        // specific session (not just the account as a whole — other sessions
        // on the same account must keep working). Old tokens minted before
        // the "sfam" claim existed simply don't join a family group; they
        // still fall back to the OnTokenValidated liveness check on their
        // next API call.
        var sessionFamilyId = Context.User?.FindFirstValue(AuthTokenService.SessionFamilyClaimType);
        if (Guid.TryParse(sessionFamilyId, out var familyId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionFamilyGroup(familyId));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var authAccountId = await ResolveAuthAccountIdAsync();
        if (!string.IsNullOrWhiteSpace(authAccountId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AccountGroup(authAccountId));
        }

        var sessionFamilyId = Context.User?.FindFirstValue(AuthTokenService.SessionFamilyClaimType);
        if (Guid.TryParse(sessionFamilyId, out var familyId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionFamilyGroup(familyId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string AccountGroup(string authAccountId)
        => $"account:{authAccountId}";

    public static string SessionFamilyGroup(Guid sessionFamilyId)
        => $"session-family:{sessionFamilyId}";

    private async Task<string?> ResolveAuthAccountIdAsync()
    {
        var authAccountId = Context.User?.FindFirstValue(AuthTokenService.AuthAccountIdClaimType);
        if (!string.IsNullOrWhiteSpace(authAccountId))
        {
            return authAccountId;
        }

        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        return await dbContext.ApplicationUserAccounts
            .AsNoTracking()
            .Where(account => account.NormalizedEmail == normalizedEmail && account.DeletedAt == null)
            .Select(account => account.Id)
            .FirstOrDefaultAsync();
    }
}
