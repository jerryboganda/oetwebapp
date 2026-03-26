using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OetLearner.Api.Tests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-learner-tests-storage-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"InMemory:oet-learner-tests-{Guid.NewGuid():N}",
                ["Auth:UseDevelopmentAuth"] = "true",
                ["Platform:PublicApiBaseUrl"] = "http://localhost",
                ["Platform:FallbackEmailDomain"] = "example.test",
                ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
                ["Storage:LocalRootPath"] = _storageRoot
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only for test temp files.
        }
    }
}
