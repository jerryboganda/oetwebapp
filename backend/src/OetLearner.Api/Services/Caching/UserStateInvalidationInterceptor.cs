using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Caching;

/// <summary>
/// Invalidates <see cref="UserStateCache"/> entries after a COMMITTED save of any entity the
/// cached values derive from. This is the single in-process invalidation hook: it covers
/// freeze requests / approvals / cancellations, session and refresh-token revocation (logout,
/// logout-all, device replacement, admin removal), password changes and resets, role changes,
/// suspension / deletion, learner access-expiry edits, subscription and add-on changes, per-user
/// module overrides and billing-plan or freeze-policy edits, wherever in the process they happen,
/// without each of those call sites having to remember to evict anything.
///
/// <para>The affected subjects are collected in <c>SavingChanges</c> (the change tracker is still
/// populated) and applied in <c>SavedChanges</c> (after the commit), so a concurrent reader cannot
/// re-fill the cache from pre-commit data after the eviction. Inside an outer database transaction
/// "saved" is not yet "committed"; the TTL bounds that case.</para>
///
/// <para>Not seen by an EF interceptor: bulk <c>ExecuteUpdate/ExecuteDelete/ExecuteSql</c> writes
/// (the admin user hard delete evicts explicitly; the billing plan / add-on hard deletes are refused
/// while any subscription or item references them) and writes made by another process. For those
/// the cache TTL is the bound. See docs/ops/user-state-cache.md.</para>
/// </summary>
public sealed class UserStateInvalidationInterceptor(UserStateCache cache) : SaveChangesInterceptor
{
    private sealed class Pending
    {
        public readonly HashSet<string> Subjects = new(StringComparer.Ordinal);
        public bool All;
    }

    private readonly ConditionalWeakTable<DbContext, Pending> pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Apply(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Discard(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var found = pending.GetValue(context, static _ => new Pending());
        found.Subjects.Clear();
        found.All = false;

        try
        {
            // ponytail: one Entries() pass = one DetectChanges plus one EntityEntry per tracked
            // entity, paid on every save. Deliberately NOT Entries<T>() per watched type: each of
            // those calls DetectChanges again (EF docs), so ~12 typed passes cost more than this
            // single one. If a bulk context (admin import, seeder) is ever measured as slow here,
            // call DetectChanges() once, set AutoDetectChangesEnabled = false in a try/finally and
            // then use the typed passes.
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                {
                    continue;
                }

                Collect(entry, found);
            }
        }
        catch (Exception)
        {
            // Cache bookkeeping must never fail a save. If a property lookup ever throws (a model
            // change that renamed a watched column), fail safe: evict everything after the commit.
            found.All = true;
        }
    }

    private static void Collect(EntityEntry entry, Pending found)
    {
        var modified = entry.State == EntityState.Modified;
        switch (entry.Entity)
        {
            case ApplicationUserAccount account:
                // Sign-in only touches LastLoginAt / failure counters: not worth an eviction.
                if (!modified || AnyModified(entry,
                        nameof(ApplicationUserAccount.Role),
                        nameof(ApplicationUserAccount.DeletedAt),
                        nameof(ApplicationUserAccount.PasswordHash)))
                {
                    AddAuthAccount(found, account.Id);
                }

                break;

            case RefreshTokenRecord token:
                // A new token (sign-in / rotation) cannot make a state worse; only a revocation,
                // an expiry change or a delete can.
                if (entry.State == EntityState.Deleted
                    || (modified && AnyModified(entry, nameof(RefreshTokenRecord.RevokedAt), nameof(RefreshTokenRecord.ExpiresAt))))
                {
                    AddAuthAccount(found, token.ApplicationUserAccountId);
                }

                break;

            case LearnerUser learner:
                // Streak / activity counters change on almost every practice write: skip those.
                if (!modified || AnyModified(entry,
                        nameof(LearnerUser.AccountStatus),
                        nameof(LearnerUser.AccessExpiresAt),
                        nameof(LearnerUser.AuthAccountId),
                        nameof(LearnerUser.ActiveProfessionId),
                        nameof(LearnerUser.CurrentPlanId)))
                {
                    AddLearner(found, learner.Id);
                    AddAuthAccount(found, learner.AuthAccountId);
                    if (modified
                        && entry.Property(nameof(LearnerUser.AuthAccountId)).IsModified
                        && entry.Property(nameof(LearnerUser.AuthAccountId)).OriginalValue is string previousAccountId)
                    {
                        AddAuthAccount(found, previousAccountId);
                    }
                }

                break;

            case ExpertUser expert:
                if (!modified || AnyModified(entry, nameof(ExpertUser.IsActive), nameof(ExpertUser.AuthAccountId)))
                {
                    AddAuthAccount(found, expert.AuthAccountId);
                }

                break;

            case Subscription subscription:
                AddLearner(found, subscription.UserId);
                break;

            case UserModuleOverride moduleOverride:
                AddLearner(found, moduleOverride.UserId);
                break;

            case AccountFreezeRecord freezeRecord:
                AddLearner(found, freezeRecord.UserId);
                break;

            case AccountFreezeEntitlement freezeEntitlement:
                AddLearner(found, freezeEntitlement.UserId);
                break;

            // Not keyed by user (an item hangs off a subscription; plans / policy are shared by
            // every learner): rare admin / purchase writes, so evict everything.
            case SubscriptionItem:
            case BillingPlan:
            case BillingPlanVersion:
            case BillingAddOn:
            case AccountFreezePolicy:
                found.All = true;
                break;
        }
    }

    private static bool AnyModified(EntityEntry entry, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (entry.Property(name).IsModified)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddAuthAccount(Pending found, string? authAccountId)
    {
        if (!string.IsNullOrEmpty(authAccountId))
        {
            found.Subjects.Add(UserStateCacheSubjects.AuthAccount(authAccountId));
        }
    }

    private static void AddLearner(Pending found, string? learnerUserId)
    {
        if (!string.IsNullOrEmpty(learnerUserId))
        {
            found.Subjects.Add(UserStateCacheSubjects.Learner(learnerUserId));
        }
    }

    private void Apply(DbContext? context)
    {
        if (context is null || !pending.TryGetValue(context, out var found))
        {
            return;
        }

        pending.Remove(context);
        if (found.All)
        {
            cache.InvalidateAll();
            return;
        }

        foreach (var subject in found.Subjects)
        {
            cache.Invalidate(subject);
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
        {
            pending.Remove(context);
        }
    }
}
