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

        var isDevelopment = !string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);
        var connectionString = DatabaseConfiguration.ResolveConnectionString(configuration, isDevelopment);

        var optionsBuilder = new DbContextOptionsBuilder<LearnerDbContext>();
        DatabaseConfiguration.ConfigureDbContext(optionsBuilder, connectionString);

        return new LearnerDbContext(optionsBuilder.Options);
    }
}
