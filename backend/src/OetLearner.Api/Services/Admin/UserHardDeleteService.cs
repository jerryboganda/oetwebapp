using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

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
public sealed class UserHardDeleteService(
    LearnerDbContext db,
    ILogger<UserHardDeleteService> logger,
    IFileStorage? fileStorage = null)
{
    // Column-name suffixes (lower-cased) that denote a reference to a user/account.
    private static readonly string[] UserRefSuffixes =
    {
        "userid", "authaccountid", "accountid", "actorid",
        "tutorid", "interlocutorid", "authorid", "ownerid",
        // Legacy and provider-facing rows use these names instead of a
        // conventional UserId FK. User ids are globally unique, so matching
        // the exact value remains scoped to the account being purged.
        "learnerid", "expertid", "adminid", "resourceid", "identity",
        "useraid", "userbid",
        // User-owned media is collected before deletion and its id is added to
        // the match set, so link rows are deleted before the MediaAsset row.
        "mediaassetid", "uploadedby",
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
        var learner = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        var expert = await db.ExpertUsers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == userId, ct);

        if (learner is null && expert is null)
        {
            var isAdmin = await db.ApplicationUserAccounts.AsNoTracking()
                .AnyAsync(a => a.Id == userId && a.Role == ApplicationUserRoles.Admin, ct);
            if (isAdmin)
            {
                throw OetLearner.Api.Services.ApiException.Validation(
                    "admin_lifecycle_immutable",
                    "Admin account deletion is not supported by the current account model.");
            }

            throw OetLearner.Api.Services.ApiException.NotFound("user_not_found", "User not found.");
        }

        var authAccountId = learner?.AuthAccountId ?? expert?.AuthAccountId;
        var learnerIds = new List<string>();
        var expertIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(authAccountId))
        {
            learnerIds = await db.Users.AsNoTracking()
                .Where(u => u.AuthAccountId == authAccountId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            expertIds = await db.ExpertUsers.AsNoTracking()
                .Where(e => e.AuthAccountId == authAccountId)
                .Select(e => e.Id)
                .ToListAsync(ct);
        }

        if (learner is not null && !learnerIds.Contains(learner.Id, StringComparer.Ordinal))
            learnerIds.Add(learner.Id);
        if (expert is not null && !expertIds.Contains(expert.Id, StringComparer.Ordinal))
            expertIds.Add(expert.Id);

        var ids = learnerIds
            .Concat(expertIds)
            .Append(userId)
            .Append(authAccountId ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var userReferenceIds = ids.ToList();
        var ownedMedia = await db.MediaAssets.AsNoTracking()
            .Where(asset => asset.UploadedBy != null && userReferenceIds.Contains(asset.UploadedBy))
            .Select(asset => new UserMediaStorageObject(
                asset.Id,
                asset.StoragePath,
                asset.ThumbnailPath,
                asset.CaptionPath,
                asset.TranscriptPath))
            .ToListAsync(ct);
        ids.AddRange(ownedMedia.Select(asset => asset.Id));

        var report = new Dictionary<string, int>();
        if (fileStorage is not null)
        {
            var storageKeys = await CollectStorageKeysAsync(userReferenceIds, ownedMedia, ct);
            var deletedStorageObjects = await DeleteUnsharedStorageAsync(
                userReferenceIds,
                ownedMedia,
                storageKeys,
                ct);
            if (deletedStorageObjects > 0)
                report["StorageObject"] = deletedStorageObjects;
        }

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
            // (NoAction), so they must go before the ApplicationUserAccount. A shared
            // auth account can legitimately have both profile rows, so remove every
            // linked profile rather than just the profile used to start the purge.
            if (expertIds.Count > 0)
            {
                var n = await DeleteEntitiesAsync(db.ExpertUsers.Where(e => expertIds.Contains(e.Id)), ct);
                if (n > 0) report["ExpertUser"] = n;
            }
            if (learnerIds.Count > 0)
            {
                var n = await DeleteEntitiesAsync(db.Users.Where(u => learnerIds.Contains(u.Id)), ct);
                if (n > 0) report["LearnerUser"] = n;
            }
            if (!string.IsNullOrWhiteSpace(authAccountId))
            {
                var n = await DeleteEntitiesAsync(db.ApplicationUserAccounts.Where(a => a.Id == authAccountId), ct);
                if (n > 0) report["ApplicationUserAccount"] = n;
            }

            if (db.Database.IsInMemory()) await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        logger.LogWarning(
            "Hard-deleted a {ProfileType} account: purged {Rows} rows across {Tables} tables.",
            learner is not null ? ApplicationUserRoles.Learner : ApplicationUserRoles.Expert,
            report.Values.Sum(), report.Count);
        return report;
    }

    private async Task<HashSet<string>> CollectStorageKeysAsync(
        IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<UserMediaStorageObject> ownedMedia,
        CancellationToken ct)
    {
        var userIdList = userIds.ToList();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var media in ownedMedia)
        {
            AddStorageKey(keys, media.StoragePath);
            AddStorageKey(keys, media.ThumbnailPath);
            AddStorageKey(keys, media.CaptionPath);
            AddStorageKey(keys, media.TranscriptPath);
        }

        var attemptKeys = await db.Attempts.AsNoTracking()
            .Where(attempt => userIdList.Contains(attempt.UserId))
            .Select(attempt => attempt.AudioObjectKey)
            .ToListAsync(ct);
        foreach (var key in attemptKeys) AddStorageKey(keys, key);

        var pronunciationKeys = await db.PronunciationAttempts.AsNoTracking()
            .Where(attempt => userIdList.Contains(attempt.UserId))
            .Select(attempt => attempt.AudioStorageKey)
            .ToListAsync(ct);
        foreach (var key in pronunciationKeys) AddStorageKey(keys, key);

        return keys;
    }

    private async Task<int> DeleteUnsharedStorageAsync(
        IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<UserMediaStorageObject> ownedMedia,
        IReadOnlySet<string> candidateKeys,
        CancellationToken ct)
    {
        if (candidateKeys.Count == 0 || fileStorage is null) return 0;

        var ownedMediaIds = ownedMedia.Select(media => media.Id).ToList();
        var remainingMedia = await db.MediaAssets.AsNoTracking()
            .Where(media => !ownedMediaIds.Contains(media.Id))
            .Select(media => new UserMediaStorageObject(
                media.Id,
                media.StoragePath,
                media.ThumbnailPath,
                media.CaptionPath,
                media.TranscriptPath))
            .ToListAsync(ct);

        var protectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var media in remainingMedia)
        {
            AddStorageKey(protectedKeys, media.StoragePath);
            AddStorageKey(protectedKeys, media.ThumbnailPath);
            AddStorageKey(protectedKeys, media.CaptionPath);
            AddStorageKey(protectedKeys, media.TranscriptPath);
        }

        var userIdList = userIds.ToList();
        var otherAttemptKeys = await db.Attempts.AsNoTracking()
            .Where(attempt => !userIdList.Contains(attempt.UserId))
            .Select(attempt => attempt.AudioObjectKey)
            .ToListAsync(ct);
        foreach (var key in otherAttemptKeys) AddStorageKey(protectedKeys, key);

        var otherPronunciationKeys = await db.PronunciationAttempts.AsNoTracking()
            .Where(attempt => !userIdList.Contains(attempt.UserId))
            .Select(attempt => attempt.AudioStorageKey)
            .ToListAsync(ct);
        foreach (var key in otherPronunciationKeys) AddStorageKey(protectedKeys, key);

        var deleted = 0;
        foreach (var key in candidateKeys)
        {
            if (protectedKeys.Contains(key)) continue;
            if (await fileStorage.DeleteAsync(key, ct)) deleted++;
        }

        return deleted;
    }

    private static void AddStorageKey(HashSet<string> keys, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.StartsWith("seed://", StringComparison.OrdinalIgnoreCase)) return;
        keys.Add(key);
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

        // The production database is relational, but the admin integration
        // harness intentionally uses EF InMemory. Keep the same purge contract
        // there by tracking matching entities and committing once at the end.
        if (db.Database.IsInMemory())
        {
            var rows = ((IEnumerable)whered).Cast<object>().ToList();
            db.RemoveRange(rows);
            return rows.Count;
        }

        return await (Task<int>)ExecuteDeleteAsyncMethod.MakeGenericMethod(clr).Invoke(null, new[] { whered, ct })!;
    }

    private async Task<int> DeleteEntitiesAsync<TEntity>(IQueryable<TEntity> query, CancellationToken ct)
        where TEntity : class
    {
        if (!db.Database.IsInMemory())
            return await query.ExecuteDeleteAsync(ct);

        var rows = await query.ToListAsync(ct);
        db.RemoveRange(rows);
        return rows.Count;
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

    private sealed record UserMediaStorageObject(
        string Id,
        string? StoragePath,
        string? ThumbnailPath,
        string? CaptionPath,
        string? TranscriptPath);
}
