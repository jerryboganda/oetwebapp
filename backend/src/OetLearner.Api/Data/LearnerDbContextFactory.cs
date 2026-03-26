using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OetLearner.Api.Data;

public sealed class LearnerDbContextFactory : IDesignTimeDbContextFactory<LearnerDbContext>
{
    public LearnerDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<LearnerDbContext>();
        if (connectionString.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseInMemoryDatabase(connectionString["InMemory:".Length..]);
        }
        else
        {
            optionsBuilder.UseNpgsql(connectionString);
        }

        return new LearnerDbContext(optionsBuilder.Options);
    }
}
