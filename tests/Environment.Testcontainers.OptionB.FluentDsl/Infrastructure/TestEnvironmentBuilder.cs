using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Azurite;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;
using WireMock.Net.Testcontainers;

namespace Environment.Testcontainers.OptionB.FluentDsl.Infrastructure;

/// <summary>
/// Fluent builder for constructing a complete test environment with
/// type-safe container configuration and dependency management.
/// </summary>
public sealed class TestEnvironmentBuilder
{
    private string _networkName = "functional-tests";
    private bool _randomHostPorts = true;

    // Container configurations
    private Action<MongoDbOptions>? _mongoDbConfig;
    private Action<MsSqlOptions>? _msSqlConfig;
    private Action<AzuriteOptions>? _azuriteConfig;
    private Action<WireMockOptions>? _wireMockConfig;
    private Action<ServiceBusOptions>? _serviceBusConfig;
    private Action<AppUnderTestOptions>? _appConfig;

    public TestEnvironmentBuilder WithNetwork(string name)
    {
        _networkName = name;
        return this;
    }

    public TestEnvironmentBuilder WithRandomHostPorts(bool enabled)
    {
        _randomHostPorts = enabled;
        return this;
    }

    public TestEnvironmentBuilder AddMongoDb(Action<MongoDbOptions> configure)
    {
        _mongoDbConfig = configure;
        return this;
    }

    public TestEnvironmentBuilder AddMsSql(Action<MsSqlOptions> configure)
    {
        _msSqlConfig = configure;
        return this;
    }

    public TestEnvironmentBuilder AddAzurite(Action<AzuriteOptions> configure)
    {
        _azuriteConfig = configure;
        return this;
    }

    public TestEnvironmentBuilder AddWireMock(Action<WireMockOptions> configure)
    {
        _wireMockConfig = configure;
        return this;
    }

    public TestEnvironmentBuilder AddServiceBusEmulator(Action<ServiceBusOptions> configure)
    {
        _serviceBusConfig = configure;
        return this;
    }

    public TestEnvironmentBuilder AddApplicationUnderTest(Action<AppUnderTestOptions> configure)
    {
        _appConfig = configure;
        return this;
    }

    public TestEnvironment Build()
    {
        if (_msSqlConfig == null && _serviceBusConfig != null)
            throw new InvalidOperationException(
                "ServiceBus Emulator requires MsSql. Call AddMsSql() before AddServiceBusEmulator().");

        return new TestEnvironment(
            _networkName, _randomHostPorts,
            _mongoDbConfig, _msSqlConfig, _azuriteConfig,
            _wireMockConfig, _serviceBusConfig, _appConfig);
    }
}

#region Option classes

public sealed class MongoDbOptions
{
    public string Alias { get; set; } = "mongo";
    public string Image { get; set; } = "mongo:7.0";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int HostPort { get; set; } = 27017;
}

public sealed class MsSqlOptions
{
    public string Alias { get; set; } = "sqlserver";
    public string Image { get; set; } = "mcr.microsoft.com/mssql/server:2022-latest";
    public string Password { get; set; } = "Strong!Passw0rd";
    public int HostPort { get; set; } = 1433;
}

public sealed class AzuriteOptions
{
    public string Alias { get; set; } = "azurite";
    public string Image { get; set; } = "mcr.microsoft.com/azure-storage/azurite:latest";
    public int BlobHostPort { get; set; } = 10000;
    public int QueueHostPort { get; set; } = 10001;
    public int TableHostPort { get; set; } = 10002;
}

public sealed class WireMockOptions
{
    public string Alias { get; set; } = "wiremock";
    public string Image { get; set; } = "wiremock/wiremock:3.12.1";
    public int HostPort { get; set; } = 8080;
}

public sealed class ServiceBusOptions
{
    public string Alias { get; set; } = "servicebus";
    public string Image { get; set; } = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";
    public int HostPort { get; set; } = 5672;
}

public sealed class AppUnderTestOptions
{
    public string Image { get; set; } = string.Empty;
    public string Alias { get; set; } = "app";
    public int ContainerPort { get; set; } = 8080;
    public int HostPort { get; set; } = 5000;
    public List<string> DependsOn { get; set; } = new();

    private Func<EnvironmentContext, Dictionary<string, string>>? _envFactory;

    public void WithEnvironment(Func<EnvironmentContext, Dictionary<string, string>> factory)
        => _envFactory = factory;

    internal Func<EnvironmentContext, Dictionary<string, string>>? GetEnvironmentFactory()
        => _envFactory;
}

/// <summary>
/// Provides type-safe access to started containers for environment variable wiring.
/// </summary>
public sealed class EnvironmentContext
{
    public MongoDbContainer MongoDb { get; init; } = null!;
    public MsSqlContainer MsSql { get; init; } = null!;
    public AzuriteContainer Azurite { get; init; } = null!;
    public WireMockContainer WireMock { get; init; } = null!;
    public ServiceBusContainer ServiceBus { get; init; } = null!;
}

#endregion

/// <summary>
/// Manages the lifecycle of all containers in the test environment.
/// Implements IAsyncLifetime for xUnit fixture integration.
/// </summary>
public sealed class TestEnvironment : IAsyncLifetime
{
    private readonly string _networkName;
    private readonly bool _randomHostPorts;
    private readonly Action<MongoDbOptions>? _mongoDbConfig;
    private readonly Action<MsSqlOptions>? _msSqlConfig;
    private readonly Action<AzuriteOptions>? _azuriteConfig;
    private readonly Action<WireMockOptions>? _wireMockConfig;
    private readonly Action<ServiceBusOptions>? _serviceBusConfig;
    private readonly Action<AppUnderTestOptions>? _appConfig;

    // Store MsSql options for ServiceBus dependency
    private MsSqlOptions? _resolvedMsSqlOpts;

    public INetwork Network { get; private set; } = null!;
    public MongoDbContainer? MongoDb { get; private set; }
    public MsSqlContainer? MsSql { get; private set; }
    public AzuriteContainer? Azurite { get; private set; }
    public WireMockContainer? WireMock { get; private set; }
    public ServiceBusContainer? ServiceBus { get; private set; }
    public IContainer? App { get; private set; }

    internal TestEnvironment(
        string networkName, bool randomHostPorts,
        Action<MongoDbOptions>? mongoDbConfig,
        Action<MsSqlOptions>? msSqlConfig,
        Action<AzuriteOptions>? azuriteConfig,
        Action<WireMockOptions>? wireMockConfig,
        Action<ServiceBusOptions>? serviceBusConfig,
        Action<AppUnderTestOptions>? appConfig)
    {
        _networkName = networkName;
        _randomHostPorts = randomHostPorts;
        _mongoDbConfig = mongoDbConfig;
        _msSqlConfig = msSqlConfig;
        _azuriteConfig = azuriteConfig;
        _wireMockConfig = wireMockConfig;
        _serviceBusConfig = serviceBusConfig;
        _appConfig = appConfig;
    }

    public async Task InitializeAsync()
    {
        // 1. Create network
        Network = new NetworkBuilder()
            .WithName($"{_networkName}-{Guid.NewGuid():N}")
            .Build();
        await Network.CreateAsync();

        // 2. Tier 1: Start independent infrastructure containers in parallel
        var tier1Tasks = new List<Task>();

        if (_mongoDbConfig != null) tier1Tasks.Add(StartMongoDbAsync());
        if (_msSqlConfig != null) tier1Tasks.Add(StartMsSqlAsync());
        if (_azuriteConfig != null) tier1Tasks.Add(StartAzuriteAsync());
        if (_wireMockConfig != null) tier1Tasks.Add(StartWireMockAsync());

        await Task.WhenAll(tier1Tasks);

        // 3. Tier 2: Start ServiceBus (depends on MsSql being healthy)
        if (_serviceBusConfig != null)
            await StartServiceBusAsync();

        // 4. Tier 3: Start application under test (depends on all infra)
        if (_appConfig != null)
            await StartAppAsync();
    }

    private async Task StartMongoDbAsync()
    {
        var opts = new MongoDbOptions();
        _mongoDbConfig!(opts);

        var builder = new MongoDbBuilder(opts.Image)
            .WithNetwork(Network)
            .WithNetworkAliases(opts.Alias);

        if (!_randomHostPorts)
            builder = builder.WithPortBinding(opts.HostPort, 27017);

        MongoDb = builder.Build();
        await MongoDb.StartAsync();
    }

    private async Task StartMsSqlAsync()
    {
        var opts = new MsSqlOptions();
        _msSqlConfig!(opts);
        _resolvedMsSqlOpts = opts;

        var builder = new MsSqlBuilder(opts.Image)
            .WithNetwork(Network)
            .WithNetworkAliases(opts.Alias)
            .WithPassword(opts.Password);

        if (!_randomHostPorts)
            builder = builder.WithPortBinding(opts.HostPort, 1433);

        MsSql = builder.Build();
        await MsSql.StartAsync();
    }

    private async Task StartAzuriteAsync()
    {
        var opts = new AzuriteOptions();
        _azuriteConfig!(opts);

        var builder = new AzuriteBuilder(opts.Image)
            .WithNetwork(Network)
            .WithNetworkAliases(opts.Alias);

        if (!_randomHostPorts)
        {
            builder = builder
                .WithPortBinding(opts.BlobHostPort, 10000)
                .WithPortBinding(opts.QueueHostPort, 10001)
                .WithPortBinding(opts.TableHostPort, 10002);
        }

        Azurite = builder.Build();
        await Azurite.StartAsync();
    }

    private async Task StartWireMockAsync()
    {
        var opts = new WireMockOptions();
        _wireMockConfig!(opts);

        // WireMockContainerBuilder uses its own default image (sheyenrath/wiremock.net).
        var builder = new WireMockContainerBuilder()
            .WithNetwork(Network)
            .WithNetworkAliases(opts.Alias);

        if (!_randomHostPorts)
            builder = builder.WithPortBinding(opts.HostPort, 8080);

        WireMock = builder.Build();
        await WireMock.StartAsync();
    }

    private async Task StartServiceBusAsync()
    {
        var opts = new ServiceBusOptions();
        _serviceBusConfig!(opts);

        // Do NOT call .WithNetwork() — WithMsSqlContainer() adds the network internally.
        var builder = new ServiceBusBuilder(opts.Image)
            .WithNetworkAliases(opts.Alias)
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(Network, MsSql!, _resolvedMsSqlOpts!.Alias, _resolvedMsSqlOpts.Password);

        if (!_randomHostPorts)
            builder = builder.WithPortBinding(opts.HostPort, 5672);

        ServiceBus = builder.Build();
        await ServiceBus.StartAsync();
    }

    private async Task StartAppAsync()
    {
        var opts = new AppUnderTestOptions();
        _appConfig!(opts);

        var ctx = new EnvironmentContext
        {
            MongoDb = MongoDb!,
            MsSql = MsSql!,
            Azurite = Azurite!,
            WireMock = WireMock!,
            ServiceBus = ServiceBus!
        };

        var envFactory = opts.GetEnvironmentFactory();
        var envVars = envFactory?.Invoke(ctx) ?? new Dictionary<string, string>();

        var builder = new ContainerBuilder(opts.Image)
            .WithNetwork(Network)
            .WithNetworkAliases(opts.Alias)
            .WithExposedPort(opts.ContainerPort);

        if (!_randomHostPorts)
            builder = builder.WithPortBinding(opts.HostPort, opts.ContainerPort);
        else
            builder = builder.WithPortBinding(opts.ContainerPort, true);

        foreach (var env in envVars)
            builder = builder.WithEnvironment(env.Key, env.Value);

        App = builder.Build();
        await App.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (App != null) await App.DisposeAsync();
        if (ServiceBus != null) await ServiceBus.DisposeAsync();
        if (WireMock != null) await WireMock.DisposeAsync();
        if (Azurite != null) await Azurite.DisposeAsync();
        if (MsSql != null) await MsSql.DisposeAsync();
        if (MongoDb != null) await MongoDb.DisposeAsync();
        if (Network != null!) await Network.DisposeAsync();
    }
}









