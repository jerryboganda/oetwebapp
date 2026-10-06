using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fleet.Manager.Persistence;

/// <summary>
/// The manager's SQLite model. Schema policy (see README, "Database"): version 1 is created by
/// <c>EnsureCreated</c> from this model by <see cref="SchemaManager"/>; every later change ships as a
/// hand-written, numbered, forward-only SQL step in <see cref="SchemaManager.Upgrades"/> and bumps
/// <see cref="SchemaManager.CurrentVersion"/>. EF Core migrations are deliberately not used: the file
/// is tiny, single-writer and owned by one process.
/// </summary>
public sealed class FleetDbContext : DbContext
{
    private static readonly ValueConverter<DateTimeOffset, long> UnixMilliseconds = new(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    public FleetDbContext(DbContextOptions<FleetDbContext> options)
        : base(options)
    {
    }

    public DbSet<HostEntity> Hosts => Set<HostEntity>();

    public DbSet<OperationEntity> Operations => Set<OperationEntity>();

    public DbSet<OperationStepEntity> OperationSteps => Set<OperationStepEntity>();

    public DbSet<PolicyEntity> Policies => Set<PolicyEntity>();

    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();

    public DbSet<ReleaseEntity> Releases => Set<ReleaseEntity>();

    public DbSet<AuditEntity> Audit => Set<AuditEntity>();

    public DbSet<OwnerAccountEntity> OwnerAccounts => Set<OwnerAccountEntity>();

    public DbSet<SchemaInfoEntity> SchemaInfo => Set<SchemaInfoEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostEntity>(e =>
        {
            e.ToTable("hosts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.NodeRef).IsUnique();
            e.HasIndex(x => new { x.Address, x.SshPort }).IsUnique().HasFilter("lifecycle <> 'Removed'");
            e.Property(x => x.NodeRef).IsRequired();
            e.Property(x => x.Address).IsRequired();
        });

        modelBuilder.Entity<OperationEntity>(e =>
        {
            e.ToTable("operations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).ValueGeneratedNever();
            // Optimistic concurrency on the state: two actors (the runner and an owner action) can never both win a transition.
            e.Property(x => x.State).IsConcurrencyToken();
            e.HasIndex(x => x.Seq).IsUnique();
            e.HasIndex(x => new { x.HostId, x.Kind });
            e.HasIndex(x => x.HostId).IsUnique().HasFilter("kind = 'enroll'").HasDatabaseName("ux_operations_one_enroll_per_host");
            e.HasOne<HostEntity>().WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OperationStepEntity>(e =>
        {
            e.ToTable("operation_steps");
            e.HasKey(x => new { x.OperationId, x.Seq });
            e.Property(x => x.Seq).ValueGeneratedNever();
            e.HasOne<OperationEntity>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PolicyEntity>(e =>
        {
            e.ToTable("policies");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Scope, x.HostId }).IsUnique();
        });

        modelBuilder.Entity<CredentialEntity>(e =>
        {
            e.ToTable("credentials");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HostId, x.Purpose });
            e.HasOne<HostEntity>().WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReleaseEntity>(e =>
        {
            e.ToTable("releases");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Sha).IsUnique();
        });

        modelBuilder.Entity<AuditEntity>(e =>
        {
            e.ToTable("audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<OwnerAccountEntity>(e =>
        {
            e.ToTable("owner_account");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SchemaInfoEntity>(e =>
        {
            e.ToTable("schema_info");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(UnixMilliseconds);
                }
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && !char.IsUpper(name[i - 1]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Per-connection pragmas. <c>secure_delete</c> is per connection (deleted content, notably a
/// destroyed owner credential, is overwritten with zeros) and <c>busy_timeout</c> keeps the single
/// writer from failing under bursts. <c>journal_mode=WAL</c> is persistent and set by <see cref="SchemaManager"/>.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON; PRAGMA busy_timeout=10000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
