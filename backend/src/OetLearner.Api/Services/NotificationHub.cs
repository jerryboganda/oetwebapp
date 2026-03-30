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
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var authAccountId = await ResolveAuthAccountIdAsync();
        if (!string.IsNullOrWhiteSpace(authAccountId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AccountGroup(authAccountId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string AccountGroup(string authAccountId)
        => $"account:{authAccountId}";

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
