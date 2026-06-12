using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Admin;

/// <summary>
/// Irreversibly purges a user and EVERY row that references it across the whole
/// schema — the literal "leave nothing behind" hard delete, including financial and
/// audit records (explicit admin choice; not GDPR-minimal). Model-driven so it
/// adapts to new tables: for each entity type, any non-key <see cref="string"/>
/// property whose name looks like a user reference is matched against this user's
/// ids (learner id, auth-account id, expert id). Those ids are globally-unique
/// 32-char tokens, so id-equality cannot false-match an unrelated column — broad
/// name matching is therefore safe and only ever deletes rows that truly reference
/// this user. Deletes run dependents-first (topological over the FK graph) inside a
/// single transaction; any FK conflict rolls the whole purge back rather than
/// leaving partial state. Every table touched is returned for the audit trail.
/// </summary>
public sealed class UserHardDeleteService(LearnerDbContext db, ILogger<UserHardDeleteService> logger)
{
    // Column-name suffixes (lower-cased) that denote a reference to a user/account.
    private static readonly string[] UserRefSuffixes =
    {
        "userid", "authaccountid", "accountid", "actorid",
        "tutorid", "interlocutorid", "authorid", "ownerid",
    };

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == "Set" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

    private static readonly MethodInfo WhereMethod = typeof(Queryable).GetMethods()
        .Single(m => m.Name == "Where"
            && m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2);

    private static readonly MethodInfo ExecuteDeleteAsyncMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
        .Single(m => m.Name == "ExecuteDeleteAsync" && m.GetParameters().Length == 2);

    private static readonly MethodInfo EfPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(string));

    public async Task<IReadOnlyDictionary<string, int>> PurgeAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var authAccountId = user.AuthAccountId;
        string? expertId = null;
        if (!string.IsNullOrEmpty(authAccountId))
        {
            expertId = await db.ExpertUsers.AsNoTracking()
                .Where(e => e.AuthAccountId == authAccountId)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(ct);
        }

        var ids = new List<string> { userId };
        if (!string.IsNullOrEmpty(authAccountId)) ids.Add(authAccountId);
        if (!string.IsNullOrEmpty(expertId)) ids.Add(expertId);

        var report = new Dictionary<string, int>();
        var ordered = DependentsFirstOrder();

        var supportsTx = db.Database.IsRelational();
        await using var tx = supportsTx ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            foreach (var et in ordered)
            {
                var clr = et.ClrType;
                // Identity rows are removed explicitly, last (they are the principals
                // that the NoAction LearnerUser/ExpertUser -> account FKs block on).
                if (clr == typeof(LearnerUser) || clr == typeof(ApplicationUserAccount) || clr == typeof(ExpertUser))
                    continue;

                foreach (var prop in et.GetProperties())
                {
                    if (prop.ClrType != typeof(string) || prop.IsPrimaryKey()) continue;
                    var lname = prop.Name.ToLowerInvariant();
                    if (!UserRefSuffixes.Any(lname.EndsWith)) continue;

                    var deleted = await DynamicDeleteWhereInAsync(clr, prop.Name, ids, ct);
                    if (deleted > 0) report[$"{clr.Name}.{prop.Name}"] = deleted;
                }
            }

            // Identity rows, dependents-first: expert + learner reference the account
            // (NoAction), so they must go before the ApplicationUserAccount.
            if (!string.IsNullOrEmpty(expertId))
            {
                var n = await db.ExpertUsers.Where(e => e.Id == expertId).ExecuteDeleteAsync(ct);
                if (n > 0) report["ExpertUser"] = n;
            }
            report["LearnerUser"] = await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
            if (!string.IsNullOrEmpty(authAccountId))
            {
                var n = await db.ApplicationUserAccounts.Where(a => a.Id == authAccountId).ExecuteDeleteAsync(ct);
                if (n > 0) report["ApplicationUserAccount"] = n;
            }

            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        logger.LogWarning(
            "Hard-deleted user {UserId} (auth {AuthAccountId}): purged {Rows} rows across {Tables} tables.",
            userId, authAccountId, report.Values.Sum(), report.Count);
        return report;
    }

    private async Task<int> DynamicDeleteWhereInAsync(Type clr, string propName, List<string> ids, CancellationToken ct)
    {
        var set = SetMethod.MakeGenericMethod(clr).Invoke(db, null)!;
        var param = Expression.Parameter(clr, "e");
        var efProp = Expression.Call(EfPropertyMethod, param, Expression.Constant(propName));
        var contains = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(string) },
            Expression.Constant(ids), efProp);
        var lambda = Expression.Lambda(contains, param);
        var whered = WhereMethod.MakeGenericMethod(clr).Invoke(null, new[] { set, lambda })!;
        return await (Task<int>)ExecuteDeleteAsyncMethod.MakeGenericMethod(clr).Invoke(null, new[] { whered, ct })!;
    }

    /// <summary>Entity types ordered so a dependent is deleted before its principal
    /// (post-order DFS over FK edges, then reversed). One CLR set per TPH hierarchy
    /// (roots only). Cycles are tolerated via a recursion guard.</summary>
    private List<IEntityType> DependentsFirstOrder()
    {
        var types = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.BaseType is null && e.FindPrimaryKey() is not null)
            .ToList();
        var typeSet = new HashSet<IEntityType>(types);
        var visited = new HashSet<IEntityType>();
        var principalsFirst = new List<IEntityType>();

        void Visit(IEntityType n, HashSet<IEntityType> stack)
        {
            if (!visited.Add(n)) return;
            stack.Add(n);
            foreach (var fk in n.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType;
                if (principal.BaseType is not null) principal = principal.GetRootType();
                if (!ReferenceEquals(principal, n) && typeSet.Contains(principal) && !stack.Contains(principal))
                    Visit(principal, stack);
            }
            stack.Remove(n);
            principalsFirst.Add(n);
        }

        foreach (var t in types) Visit(t, new HashSet<IEntityType>());
        principalsFirst.Reverse(); // dependents first
        return principalsFirst;
    }
}
