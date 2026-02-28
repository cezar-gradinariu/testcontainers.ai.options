namespace Environment.Testcontainers.OptionB.FluentDsl.Infrastructure;
public sealed class FunctionalTestEnvironment : IAsyncLifetime
{
    private readonly TestEnvironment _environment;
    public FunctionalTestEnvironment()
    {
        _environment = new TestEnvironmentBuilder()
            .WithNetwork("functional-tests")
            .WithRandomHostPorts(true)
            .AddMongoDb(mongo =>
            {
                mongo.Alias = "mongo";
                mongo.Image = "mongo:7.0";
                mongo.HostPort = 27017;
            })
            .AddMsSql(sql =>
            {
                sql.Alias = "sqlserver";
                sql.Image = "mcr.microsoft.com/mssql/server:2022-latest";
                sql.Password = "Strong!Passw0rd";
                sql.HostPort = 1433;
            })
            .AddAzurite(azurite =>
            {
                azurite.Alias = "azurite";
                azurite.BlobHostPort = 10000;
                azurite.QueueHostPort = 10001;
                azurite.TableHostPort = 10002;
            })
            .AddWireMock(wiremock =>
            {
                wiremock.Alias = "wiremock";
                wiremock.HostPort = 8080;
            })
            .AddServiceBusEmulator(servicebus =>
            {
                servicebus.Alias = "servicebus";
                servicebus.HostPort = 5672;
            })
            .Build();
    }
    public TestEnvironment Environment => _environment;
    public Task InitializeAsync() => _environment.InitializeAsync();
    public Task DisposeAsync() => _environment.DisposeAsync();
}
[CollectionDefinition("FunctionalTests")]
public class FunctionalTestsCollection : ICollectionFixture<FunctionalTestEnvironment>;
