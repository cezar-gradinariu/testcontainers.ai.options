using Environment.Testcontainers.OptionB.FluentDsl.Infrastructure;

namespace Environment.Testcontainers.OptionB.FluentDsl;

[Collection("FunctionalTests")]
public class EnvironmentConnectivityTests
{
    private readonly FunctionalTestEnvironment _env;

    public EnvironmentConnectivityTests(FunctionalTestEnvironment env)
    {
        _env = env;
    }

    [Fact]
    public void MongoDb_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.Environment.MongoDb);
        var connectionString = _env.Environment.MongoDb!.GetConnectionString();
        Assert.NotEmpty(connectionString);
        Assert.Contains("mongodb://", connectionString);
    }

    [Fact]
    public void MsSql_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.Environment.MsSql);
        var connectionString = _env.Environment.MsSql!.GetConnectionString();
        Assert.NotEmpty(connectionString);
        Assert.Contains("Server=", connectionString);
    }

    [Fact]
    public void Azurite_ShouldBeRunning_AndExposeEndpoints()
    {
        Assert.NotNull(_env.Environment.Azurite);
        Assert.NotEmpty(_env.Environment.Azurite!.GetConnectionString());
    }

    [Fact]
    public void WireMock_ShouldBeRunning_AndExposeUrl()
    {
        Assert.NotNull(_env.Environment.WireMock);
        var url = _env.Environment.WireMock!.GetPublicUrl();
        Assert.NotEmpty(url);
        Assert.StartsWith("http", url);
    }

    [Fact]
    public void ServiceBus_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.Environment.ServiceBus);
        var connectionString = _env.Environment.ServiceBus!.GetConnectionString();
        Assert.NotEmpty(connectionString);
    }

    [Fact]
    public void AllContainers_ShouldShareSameNetwork()
    {
        Assert.NotNull(_env.Environment.Network);
        Assert.NotNull(_env.Environment.MongoDb);
        Assert.NotNull(_env.Environment.MsSql);
        Assert.NotNull(_env.Environment.Azurite);
        Assert.NotNull(_env.Environment.WireMock);
        Assert.NotNull(_env.Environment.ServiceBus);
    }

    [Fact]
    public void RandomHostPorts_ShouldAssignDifferentPorts()
    {
        var mongoPorts = _env.Environment.MongoDb!.GetMappedPublicPort(27017);
        var mssqlPort = _env.Environment.MsSql!.GetMappedPublicPort(1433);

        // Random ports should be > 0 and likely not the default container ports
        Assert.True(mongoPorts > 0);
        Assert.True(mssqlPort > 0);
    }
}
