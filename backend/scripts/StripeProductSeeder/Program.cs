using System.Text.Json;
using OetLearner.Scripts.StripeProductSeeder;
using Stripe;

// ── CLI ───────────────────────────────────────────────────────────────────────
//  --dry-run     (default) print what would be done, do not call Stripe.
//  --test        use STRIPE_TEST_SECRET_KEY env var.
//  --live        use STRIPE_LIVE_SECRET_KEY env var.
//
//  Optional:
//  --catalog <path>     override catalog.json location (default: next to the
//                       binary as a copy-to-output content file).
// ──────────────────────────────────────────────────────────────────────────────

var mode = SeedMode.DryRun;
string? catalogPathOverride = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dry-run":
            mode = SeedMode.DryRun;
            break;
        case "--test":
            mode = SeedMode.Test;
            break;
        case "--live":
            mode = SeedMode.Live;
            break;
        case "--catalog":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("--catalog requires a path argument.");
                return 2;
            }
            catalogPathOverride = args[++i];
            break;
        case "--help":
        case "-h":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintHelp();
            return 2;
    }
}

var catalogPath = catalogPathOverride
    ?? Path.Combine(AppContext.BaseDirectory, "catalog.json");

if (!System.IO.File.Exists(catalogPath))
{
    Console.Error.WriteLine($"Catalog file not found at: {catalogPath}");
    return 3;
}

CatalogManifest manifest;
await using (var stream = System.IO.File.OpenRead(catalogPath))
{
    var parsed = await JsonSerializer.DeserializeAsync<CatalogManifest>(stream, CatalogManifest.JsonOptions);
    if (parsed is null)
    {
        Console.Error.WriteLine("Catalog manifest deserialized to null.");
        return 4;
    }
    manifest = parsed;
}

Console.WriteLine($"Loaded {manifest.Products.Count} products from {catalogPath}.");
Console.WriteLine($"Mode: {mode}");

IStripeCatalogGateway gateway;
switch (mode)
{
    case SeedMode.DryRun:
        gateway = new DryRunStripeCatalogGateway();
        break;
    case SeedMode.Test:
        {
            var key = Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                Console.Error.WriteLine("STRIPE_TEST_SECRET_KEY is not set.");
                return 5;
            }
            StripeConfiguration.ApiKey = key;
            gateway = new StripeCatalogGateway();
            break;
        }
    case SeedMode.Live:
        {
            var key = Environment.GetEnvironmentVariable("STRIPE_LIVE_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                Console.Error.WriteLine("STRIPE_LIVE_SECRET_KEY is not set.");
                return 5;
            }
            StripeConfiguration.ApiKey = key;
            gateway = new StripeCatalogGateway();
            Console.WriteLine("!!! LIVE MODE — this will mutate the live Stripe account.");
            break;
        }
    default:
        throw new InvalidOperationException($"Unsupported mode {mode}");
}

var seeder = new StripeCatalogSeeder(gateway);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var result = await seeder.SeedAsync(manifest, cts.Token);
    Console.WriteLine();
    Console.WriteLine("Summary:");
    Console.WriteLine($"  products: created={result.ProductsCreated} updated={result.ProductsUpdated} unchanged={result.ProductsUnchanged}");
    Console.WriteLine($"  prices:   created={result.PricesCreated} reused={result.PricesReused}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Seeder failed: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("OET Stripe Product Seeder");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -- [--dry-run|--test|--live] [--catalog <path>]");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  --dry-run   (default) Use an in-memory fake; print what would be done.");
    Console.WriteLine("  --test      Talk to Stripe via STRIPE_TEST_SECRET_KEY.");
    Console.WriteLine("  --live      Talk to Stripe via STRIPE_LIVE_SECRET_KEY (writes the live account).");
    Console.WriteLine();
    Console.WriteLine("Idempotent. Re-running with no source changes is a no-op.");
}

internal enum SeedMode
{
    DryRun,
    Test,
    Live
}
