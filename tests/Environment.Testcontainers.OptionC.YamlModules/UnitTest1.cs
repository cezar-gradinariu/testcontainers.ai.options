using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using Environment.Testcontainers.OptionC.YamlModules.Modules;

namespace Environment.Testcontainers.OptionC.YamlModules;

[Collection("FunctionalTests")]
public class EnvironmentConnectivityTests
{
    private readonly TestEnvironmentOrchestrator _orchestrator;

    public EnvironmentConnectivityTests(TestEnvironmentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [Fact]
    public void MongoDb_ShouldBeRunning_AndExposeConnectionString()
    {
        var connectionString = _orchestrator.ExposedVariables["mongodb.connectionString"];
        Assert.NotEmpty(connectionString);
        Assert.Contains("mongodb://", connectionString);
    }

    [Fact]
    public void MsSql_ShouldBeRunning_AndExposeConnectionString()
    {
        var connectionString = _orchestrator.ExposedVariables["mssql.connectionString"];
        Assert.NotEmpty(connectionString);
        Assert.Contains("Server=", connectionString);
    }

    [Fact]
    public void Azurite_ShouldBeRunning_AndExposeEndpoints()
    {
        Assert.NotEmpty(_orchestrator.ExposedVariables["azurite.connectionString"]);
        Assert.NotEmpty(_orchestrator.ExposedVariables["azurite.blobEndpoint"]);
        Assert.NotEmpty(_orchestrator.ExposedVariables["azurite.queueEndpoint"]);
        Assert.NotEmpty(_orchestrator.ExposedVariables["azurite.tableEndpoint"]);
    }

    [Fact]
    public void WireMock_ShouldBeRunning_AndExposeUrl()
    {
        var url = _orchestrator.ExposedVariables["wiremock.url"];
        Assert.NotEmpty(url);
        Assert.StartsWith("http", url);
    }

    [Fact]
    public void ServiceBus_ShouldBeRunning_AndExposeConnectionString()
    {
        var connectionString = _orchestrator.ExposedVariables["servicebus.connectionString"];
        Assert.NotEmpty(connectionString);
    }

    [Fact]
    public void AllContainers_ShouldShareSameNetwork()
    {
        Assert.NotNull(_orchestrator.Network);
        Assert.NotNull(_orchestrator.GetModule("mongodb").Container);
        Assert.NotNull(_orchestrator.GetModule("sqlserver").Container);
        Assert.NotNull(_orchestrator.GetModule("azurite").Container);
        Assert.NotNull(_orchestrator.GetModule("wiremock").Container);
        Assert.NotNull(_orchestrator.GetModule("servicebus").Container);
    }

    [Fact]
    public void ModuleRegistry_ShouldResolveCorrectTypes()
    {
        Assert.IsType<MongoDbModule>(_orchestrator.GetModule("mongodb"));
        Assert.IsType<MsSqlModule>(_orchestrator.GetModule("sqlserver"));
        Assert.IsType<AzuriteModule>(_orchestrator.GetModule("azurite"));
        Assert.IsType<WireMockModule>(_orchestrator.GetModule("wiremock"));
        Assert.IsType<ServiceBusEmulatorModule>(_orchestrator.GetModule("servicebus"));
    }

    [Fact]
    public void InternalConnectionStrings_ShouldUseNetworkAliases()
    {
        var mongoInternal = _orchestrator.ExposedVariables["mongodb.internalConnectionString"];
        var mssqlInternal = _orchestrator.ExposedVariables["mssql.internalConnectionString"];

        Assert.Contains("mongo:", mongoInternal);
        Assert.Contains("sqlserver", mssqlInternal);
    }
}
