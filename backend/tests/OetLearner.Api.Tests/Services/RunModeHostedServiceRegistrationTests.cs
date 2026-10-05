using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W4 — API mode registers zero cost-bearing hosted services; worker mode
/// registers <see cref="AiOperationWorker"/> plus the drained set.
/// </summary>
public sealed class RunModeHostedServiceRegistrationTests
{
    [Fact]
    public void ApiProduction_DoesNotRegisterCostBearingHostedServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "api",
            ["Ai:HostedWorkers:Enabled"] = "false",
            ["Listening:PartAAiScoring:Enabled"] = "true",
        }).Build();

        var services = new ServiceCollection();
        AiCostBearingHostedServiceRegistration.Add(services, configuration, enableCostBearing: false, isWorker: false);

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(AiOperationWorker));
        foreach (var type in AiCostBearingHostedServiceRegistration.CostBearingWorkerTypes)
        {
            Assert.DoesNotContain(services, d => d.ImplementationType == type);
        }
    }

    [Fact]
    public void WorkerMode_RegistersAiOperationWorkerAndCostBearingSet()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "worker",
            ["Listening:PartAAiScoring:Enabled"] = "true",
        }).Build();

        var services = new ServiceCollection();
        AiCostBearingHostedServiceRegistration.Add(services, configuration, enableCostBearing: true, isWorker: true);

        Assert.Contains(services, d => d.ImplementationType == typeof(AiOperationWorker));
        Assert.Contains(services, d => d.ImplementationType == typeof(ListeningTtsJobWorker));
        Assert.Contains(services, d => d.ImplementationType == typeof(ListeningPartAAiScoringWorker));
        Assert.Contains(services, d => d.ImplementationType == typeof(ContentTextExtractionWorker));
        Assert.Contains(services, d => d.ServiceType == typeof(IAiOperationLeaseClaimer));
    }

    [Fact]
    public void ContentTextExtractionWorker_IsInTheCostBearingSet_SoOnlyTheAiWorkerRunsItInProduction()
    {
        Assert.Contains(typeof(ContentTextExtractionWorker), AiCostBearingHostedServiceRegistration.CostBearingWorkerTypes);

        var production = new TestEnv(Environments.Production);
        var workerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "worker",
        }).Build();
        var apiConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "api",
        }).Build();

        var onWorker = new ServiceCollection();
        AiCostBearingHostedServiceRegistration.Add(
            onWorker, workerConfig, AiRunMode.EnableCostBearingHostedWorkers(workerConfig, production), AiRunMode.IsWorker(workerConfig));
        var onApiSlot = new ServiceCollection();
        AiCostBearingHostedServiceRegistration.Add(
            onApiSlot, apiConfig, AiRunMode.EnableCostBearingHostedWorkers(apiConfig, production), AiRunMode.IsWorker(apiConfig));

        Assert.Contains(onWorker, d => d.ImplementationType == typeof(ContentTextExtractionWorker));
        Assert.DoesNotContain(onApiSlot, d => d.ImplementationType == typeof(ContentTextExtractionWorker));
    }

    [Fact]
    public void ProgramCs_DoesNotRegisterTheExtractionWorkerOutsideTheCostBearingGate()
    {
        // The worker used to be an unconditional AddHostedService in Program.cs, which
        // ran it in the blue slot, the green slot and the ai-worker at once.
        var programPath = Path.Combine(
            FindRepositoryRoot(), "backend", "src", "OetLearner.Api", "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.DoesNotContain("AddHostedService<OetLearner.Api.Services.Content.ContentTextExtractionWorker>", program);
        Assert.DoesNotContain("AddHostedService<ContentTextExtractionWorker>", program);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetLearner.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [Fact]
    public void AiRunMode_WorkerIsDetected_AndProductionApiDefaultsDrainOff()
    {
        var worker = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "worker",
        }).Build();
        var api = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OET_RUN_MODE"] = "api",
        }).Build();

        Assert.True(AiRunMode.IsWorker(worker));
        Assert.False(AiRunMode.IsWorker(api));
        Assert.True(AiRunMode.EnableCostBearingHostedWorkers(worker, new TestEnv(Environments.Production)));
        Assert.False(AiRunMode.EnableCostBearingHostedWorkers(api, new TestEnv(Environments.Production)));
        Assert.True(AiRunMode.EnableCostBearingHostedWorkers(api, new TestEnv(Environments.Development)));
    }

    private sealed class TestEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
