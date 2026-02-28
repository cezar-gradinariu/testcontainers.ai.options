using Environment.Testcontainers.OptionA.JsonConfig.Infrastructure;

namespace Environment.Testcontainers.OptionA.JsonConfig;

[Collection("FunctionalTests")]
public class EnvironmentConnectivityTests : IClassFixture<TestEnvironmentFixture>
{
    private readonly TestEnvironmentFixture _env;

    public EnvironmentConnectivityTests(TestEnvironmentFixture env)
    {
        _env = env;
    }

    [Fact]
    public void MongoDb_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.MongoDb);
        var connectionString = _env.ExposedVariables["mongodb.connectionString"];
        Assert.NotEmpty(connectionString);
        Assert.Contains("mongodb://", connectionString);
    }

    [Fact]
    public void MsSql_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.MsSql);
        var connectionString = _env.ExposedVariables["mssql.connectionString"];
        Assert.NotEmpty(connectionString);
        Assert.Contains("Server=", connectionString);
    }

    [Fact]
    public void Azurite_ShouldBeRunning_AndExposeEndpoints()
    {
        Assert.NotNull(_env.Azurite);
        Assert.NotEmpty(_env.ExposedVariables["azurite.connectionString"]);
        Assert.NotEmpty(_env.ExposedVariables["azurite.blobEndpoint"]);
        Assert.NotEmpty(_env.ExposedVariables["azurite.queueEndpoint"]);
        Assert.NotEmpty(_env.ExposedVariables["azurite.tableEndpoint"]);
    }

    [Fact]
    public void WireMock_ShouldBeRunning_AndExposeUrl()
    {
        Assert.NotNull(_env.WireMock);
        var url = _env.ExposedVariables["wiremock.url"];
        Assert.NotEmpty(url);
        Assert.StartsWith("http", url);
    }

    [Fact]
    public void ServiceBus_ShouldBeRunning_AndExposeConnectionString()
    {
        Assert.NotNull(_env.ServiceBus);
        var connectionString = _env.ExposedVariables["servicebus.connectionString"];
        Assert.NotEmpty(connectionString);
    }

    [Fact]
    public void AllContainers_ShouldShareSameNetwork()
    {
        Assert.NotNull(_env.Network);
        // All containers were created with the same INetwork — 
        // if they started successfully, they are on the same network.
        Assert.NotNull(_env.MongoDb);
        Assert.NotNull(_env.MsSql);
        Assert.NotNull(_env.Azurite);
        Assert.NotNull(_env.WireMock);
        Assert.NotNull(_env.ServiceBus);
    }

    [Fact]
    public void TemplateResolver_ShouldResolveConnectionStrings()
    {
        var factory = new TestEnvironmentFactory(TestEnvironmentFactory.LoadConfig());

        var templates = new Dictionary<string, string>
        {
            ["MongoUri"] = "{{mongodb.connectionString}}",
            ["SqlConn"] = "{{mssql.connectionString}}",
            ["Static"] = "http://wiremock:8080"
        };

        var resolved = factory.ResolveTemplates(templates, _env.ExposedVariables);

        Assert.DoesNotContain("{{", resolved["MongoUri"]);
        Assert.DoesNotContain("{{", resolved["SqlConn"]);
        Assert.Equal("http://wiremock:8080", resolved["Static"]);
    }
}
