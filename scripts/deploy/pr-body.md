## Summary

Ships the EF migration that creates the three admin-permission tables (`AdminPermissionGrants`, `AdminUsers`, `PermissionTemplates`). These are referenced by `DbContext`, `AuthService`, `AdminService`, and `SeedData` but were never created by any prior migration.

## The bug this fixes

Every admin sign-in triggered this path in `AuthService.CreateSessionFromSubjectAsync`:

```csharp
if (string.Equals(account.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
{
    adminPerms = await db.AdminPermissionGrants
        .AsNoTracking()
        .Where(g => g.AdminUserId == account.Id)
        .Select(g => g.Permission)
        .ToArrayAsync(cancellationToken);
}
```

Against production PostgreSQL this threw `42P01 relation "AdminPermissionGrants" does not exist` on every admin login, which the UI surfaces as `An unexpected server error occurred` — the same message a new admin just saw during onboarding.

## Fix

New migration `20260418200000_AddAdminRoleBasedAccessControl` creates the three tables with schema matching their entity declarations:

- `AdminPermissionGrants` — PK `Id`, FK `AdminUserId` → `ApplicationUserAccounts.Id` (`ON DELETE CASCADE`), unique index on `(AdminUserId, Permission)`.
- `AdminUsers` — display-time admin-user record.
- `PermissionTemplates` — reusable role bundle.

Uses `IF NOT EXISTS` on every statement so it applies cleanly on production where I already hot-patched the same schema manually (via `scripts/deploy/create-admin-permissions-tables.sh`) to unblock the live admin login. Any fresh deploy will create these tables normally.

## Also included

12 reproducible production deploy + recovery scripts under `scripts/deploy/`, covering: pre-deploy DB snapshot, deploy runbook, post-deploy verification, admin-account census, email-pipeline inspection, forgot-password trigger, admin create/demote, admin-roster cleanup, and the manual hot-patch that repaired prod.

## Test / build evidence

- `dotnet build` → 0 errors
- No runtime code changes; migration-only plus scripts

## Not in scope

This PR does NOT auto-grant permissions to any admin. The prod hot-patch seeded 16 permissions for `manwara575@gmail.com`; any other admin accounts need their grants created via the admin UI or a follow-up seeder.
