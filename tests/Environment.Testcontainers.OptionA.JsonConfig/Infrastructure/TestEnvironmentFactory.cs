using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionA.JsonConfig.Configuration;
using Microsoft.Extensions.Configuration;
using Testcontainers.Azurite;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;
using WireMock.Net.Testcontainers;

namespace Environment.Testcontainers.OptionA.JsonConfig.Infrastructure;

/// <summary>
/// Factory that reads testcontainers.json configuration and builds
/// Testcontainers instances with a shared Docker network.
/// </summary>
public sealed class TestEnvironmentFactory
{
    private readonly TestEnvironmentConfig _config;

    public TestEnvironmentFactory(TestEnvironmentConfig config)
    {
        _config = config;
    }

    public static TestEnvironmentConfig LoadConfig(string jsonFileName = "testcontainers.json")
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(jsonFileName, optional: false)
            .AddEnvironmentVariables()
            .Build();

        var config = new TestEnvironmentConfig();
        configuration.GetSection("testEnvironment").Bind(config);
        return config;
    }

    public INetwork CreateNetwork()
    {
        var name = _config.Network.Name + "-" + Guid.NewGuid().ToString("N");
        return new NetworkBuilder()
            .WithName(name)
            .Build();
    }

    public MongoDbContainer CreateMongoDb(INetwork network)
    {
        var svc = _config.Services["mongodb"];
        var builder = new MongoDbBuilder(svc.Image)
            .WithNetwork(network)
            .WithNetworkAliases(new[] { svc.NetworkAlias });

        if (!_config.PortMapping.RandomHostPorts && svc.Ports.Host > 0)
            builder = builder.WithPortBinding(svc.Ports.Host, svc.Ports.Container);

        foreach (var env in svc.EnvironmentVariables)
            builder = builder.WithEnvironment(env.Key, env.Value);

        return builder.Build();
    }

    public MsSqlContainer CreateMsSql(INetwork network)
    {
        var svc = _config.Services["mssql"];
        var password = svc.EnvironmentVariables.GetValueOrDefault("SA_PASSWORD", "Strong!Passw0rd");

        var builder = new MsSqlBuilder(svc.Image)
            .WithNetwork(network)
            .WithNetworkAliases(new[] { svc.NetworkAlias })
            .WithPassword(password);

        if (!_config.PortMapping.RandomHostPorts && svc.Ports.Host > 0)
            builder = builder.WithPortBinding(svc.Ports.Host, svc.Ports.Container);

        return builder.Build();
    }

    public AzuriteContainer CreateAzurite(INetwork network)
    {
        var svc = _config.Services["azurite"];
        var builder = new AzuriteBuilder(svc.Image)
            .WithNetwork(network)
            .WithNetworkAliases(new[] { svc.NetworkAlias });

        if (!_config.PortMapping.RandomHostPorts && svc.Ports.Host > 0)
            builder = builder.WithPortBinding(svc.Ports.Host, svc.Ports.Container);

        return builder.Build();
    }

    public WireMockContainer CreateWireMock(INetwork network)
    {
        var svc = _config.Services["wiremock"];
        // WireMockContainerBuilder uses its own default image (sheyenrath/wiremock.net).
        // Do NOT override with the Java-based wiremock/wiremock image — it will crash.
        var builder = new WireMockContainerBuilder()
            .WithNetwork(network)
            .WithNetworkAliases(new[] { svc.NetworkAlias });

        if (!_config.PortMapping.RandomHostPorts && svc.Ports.Host > 0)
            builder = builder.WithPortBinding(svc.Ports.Host, svc.Ports.Container);

        return builder.Build();
    }

    public ServiceBusContainer CreateServiceBus(INetwork network, MsSqlContainer msSqlContainer)
    {
        var svc = _config.Services["servicebus"];
        var msSqlSvc = _config.Services["mssql"];
        var msSqlAlias = msSqlSvc.NetworkAlias;
        var msSqlPassword = msSqlSvc.EnvironmentVariables.GetValueOrDefault("SA_PASSWORD", "Strong!Passw0rd");

        // Do NOT call .WithNetwork() here — WithMsSqlContainer() adds the network internally.
        // Adding it twice causes a duplicate key error in ContainerConfigurationConverter.
        var builder = new ServiceBusBuilder(svc.Image)
            .WithNetworkAliases(new[] { svc.NetworkAlias })
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(network, msSqlContainer, msSqlAlias, msSqlPassword);

        if (!_config.PortMapping.RandomHostPorts && svc.Ports.Host > 0)
            builder = builder.WithPortBinding(svc.Ports.Host, svc.Ports.Container);

        return builder.Build();
    }

    public IContainer CreateApplicationUnderTest(
        INetwork network,
        Dictionary<string, string> resolvedEnvVars)
    {
        var appConfig = _config.ApplicationUnderTest;
        if (appConfig == null) throw new InvalidOperationException("No applicationUnderTest configured.");

        var builder = new ContainerBuilder(appConfig.Image)
            .WithNetwork(network)
            .WithNetworkAliases(new[] { appConfig.NetworkAlias })
            .WithExposedPort(appConfig.Ports.Container);

        if (!_config.PortMapping.RandomHostPorts && appConfig.Ports.Host > 0)
            builder = builder.WithPortBinding(appConfig.Ports.Host, appConfig.Ports.Container);
        else
            builder = builder.WithPortBinding(appConfig.Ports.Container, true);

        foreach (var env in resolvedEnvVars)
            builder = builder.WithEnvironment(env.Key, env.Value);

        return builder.Build();
    }

    /// <summary>
    /// Resolves template placeholders like {{mongodb.connectionString}} in environment
    /// variable values using the provided variable dictionary.
    /// </summary>
    public Dictionary<string, string> ResolveTemplates(
        Dictionary<string, string> templates,
        Dictionary<string, string> variables)
    {
        var result = new Dictionary<string, string>();
        foreach (var kvp in templates)
        {
            var value = Regex.Replace(kvp.Value, @"\{\{(\w+\.\w+)\}\}", match =>
            {
                var key = match.Groups[1].Value;
                return variables.TryGetValue(key, out var val) ? val : match.Value;
            });
            result[kvp.Key] = value;
        }
        return result;
    }

    /// <summary>
    /// Performs topological sort of services based on dependsOn relationships.
    /// Returns tiers of services that can be started in parallel within each tier.
    /// </summary>
    public List<List<string>> GetStartupTiers()
    {
        var allServices = new Dictionary<string, List<string>>();

        foreach (var (name, svc) in _config.Services)
            allServices[name] = svc.DependsOn;

        var started = new HashSet<string>();
        var tiers = new List<List<string>>();

        while (started.Count < allServices.Count)
        {
            var tier = allServices
                .Where(kvp => !started.Contains(kvp.Key))
                .Where(kvp => kvp.Value.All(dep => started.Contains(dep)))
                .Select(kvp => kvp.Key)
                .ToList();

            if (tier.Count == 0)
                throw new InvalidOperationException(
                    "Circular dependency detected among services: " +
                    string.Join(", ", allServices.Keys.Except(started)));

            tiers.Add(tier);
            foreach (var name in tier)
                started.Add(name);
        }

        return tiers;
    }
}

