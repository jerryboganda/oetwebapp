using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Services.Ai;
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
        Assert.Contains(services, d => d.ServiceType == typeof(IAiOperationLeaseClaimer));
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
