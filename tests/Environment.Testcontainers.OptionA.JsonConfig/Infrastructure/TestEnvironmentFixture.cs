using DotNet.Testcontainers.Networks;
using Testcontainers.Azurite;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;
using WireMock.Net.Testcontainers;

namespace Environment.Testcontainers.OptionA.JsonConfig.Infrastructure;

/// <summary>
/// xUnit collection fixture that manages the complete test environment lifecycle.
/// Reads configuration from testcontainers.json, creates a Docker network,
/// starts all containers in dependency order, and tears them down after tests.
/// </summary>
public sealed class TestEnvironmentFixture : IAsyncLifetime
{
    private readonly TestEnvironmentFactory _factory;

    public INetwork Network { get; private set; } = null!;
    public MongoDbContainer MongoDb { get; private set; } = null!;
    public MsSqlContainer MsSql { get; private set; } = null!;
    public AzuriteContainer Azurite { get; private set; } = null!;
    public WireMockContainer WireMock { get; private set; } = null!;
    public ServiceBusContainer ServiceBus { get; private set; } = null!;

    /// <summary>
    /// Exposes resolved variables (connection strings, URLs) for template resolution.
    /// </summary>
    public Dictionary<string, string> ExposedVariables { get; } = new();

    public TestEnvironmentFixture()
    {
        var config = TestEnvironmentFactory.LoadConfig();
        _factory = new TestEnvironmentFactory(config);
    }

    public async Task InitializeAsync()
    {
        // 1. Create the Docker network
        Network = _factory.CreateNetwork();
        await Network.CreateAsync();

        // 2. Resolve startup tiers from dependency graph
        var tiers = _factory.GetStartupTiers();

        // 3. Pre-build all containers (but don't start yet)
        //    We need MsSql reference for ServiceBus, so we build in order.
        MongoDb = _factory.CreateMongoDb(Network);
        MsSql = _factory.CreateMsSql(Network);
        Azurite = _factory.CreateAzurite(Network);
        WireMock = _factory.CreateWireMock(Network);

        // 4. Start containers tier by tier (health-gated via WaitStrategy in StartAsync)
        foreach (var tier in tiers)
        {
            var tasks = new List<Task>();
            foreach (var serviceName in tier)
            {
                tasks.Add(serviceName switch
                {
                    "mongodb" => MongoDb.StartAsync(),
                    "mssql" => MsSql.StartAsync(),
                    "azurite" => Azurite.StartAsync(),
                    "wiremock" => WireMock.StartAsync(),
                    "servicebus" => StartServiceBusAsync(),
                    _ => Task.CompletedTask
                });
            }
            await Task.WhenAll(tasks);

            // After each tier, populate exposed variables for services in this tier
            foreach (var serviceName in tier)
                PopulateVariables(serviceName);
        }
    }

    private async Task StartServiceBusAsync()
    {
        // ServiceBus is built here because it requires the MsSql container reference
        // and MsSql must be started first (ensured by tiered startup).
        ServiceBus = _factory.CreateServiceBus(Network, MsSql);
        await ServiceBus.StartAsync();
    }

    private void PopulateVariables(string serviceName)
    {
        switch (serviceName)
        {
            case "mongodb":
                ExposedVariables["mongodb.connectionString"] = MongoDb.GetConnectionString();
                ExposedVariables["mongodb.host"] = MongoDb.Hostname;
                ExposedVariables["mongodb.port"] = MongoDb.GetMappedPublicPort(27017).ToString();
                break;
            case "mssql":
                ExposedVariables["mssql.connectionString"] = MsSql.GetConnectionString();
                ExposedVariables["mssql.host"] = MsSql.Hostname;
                ExposedVariables["mssql.port"] = MsSql.GetMappedPublicPort(1433).ToString();
                break;
            case "azurite":
                ExposedVariables["azurite.connectionString"] = Azurite.GetConnectionString();
                ExposedVariables["azurite.blobEndpoint"] = Azurite.GetBlobEndpoint();
                ExposedVariables["azurite.queueEndpoint"] = Azurite.GetQueueEndpoint();
                ExposedVariables["azurite.tableEndpoint"] = Azurite.GetTableEndpoint();
                break;
            case "wiremock":
                ExposedVariables["wiremock.url"] = WireMock.GetPublicUrl();
                break;
            case "servicebus":
                ExposedVariables["servicebus.connectionString"] = ServiceBus.GetConnectionString();
                break;
        }
    }

    public async Task DisposeAsync()
    {
        // Stop in reverse order
        if (ServiceBus != null!) await ServiceBus.DisposeAsync();
        if (WireMock != null!) await WireMock.DisposeAsync();
        if (Azurite != null!) await Azurite.DisposeAsync();
        if (MsSql != null!) await MsSql.DisposeAsync();
        if (MongoDb != null!) await MongoDb.DisposeAsync();
        if (Network != null!) await Network.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection definition — all test classes in this collection share
/// the same TestEnvironmentFixture (single set of containers).
/// </summary>
[CollectionDefinition("FunctionalTests")]
public class FunctionalTestsCollection : ICollectionFixture<TestEnvironmentFixture>;

