using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Modules;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Environment.Testcontainers.OptionC.YamlModules.Infrastructure;

/// <summary>
/// Orchestrator that loads a YAML configuration, resolves container modules,
/// builds a dependency graph, and manages the container lifecycle.
/// </summary>
public sealed class TestEnvironmentOrchestrator : IAsyncLifetime
{
    private readonly string _yamlPath;
    private TestEnvironmentYaml _config = null!;
    private INetwork _network = null!;

    private readonly Dictionary<string, Type> _moduleRegistry = new()
    {
        ["mongodb"] = typeof(MongoDbModule),
        ["mssql"] = typeof(MsSqlModule),
        ["azurite"] = typeof(AzuriteModule),
        ["wiremock"] = typeof(WireMockModule),
        ["servicebus-emulator"] = typeof(ServiceBusEmulatorModule),
        ["generic"] = typeof(GenericModule)
    };

    private readonly Dictionary<string, IContainerModule> _modules = new();

    /// <summary>
    /// All exposed variables from started containers, keyed as "service.variable".
    /// </summary>
    public Dictionary<string, string> ExposedVariables { get; } = new();

    /// <summary>
    /// Access a started module by service name.
    /// </summary>
    public IContainerModule GetModule(string serviceName) => _modules[serviceName];

    public INetwork Network => _network;

    public TestEnvironmentOrchestrator() : this("test-environment.yaml")
    {
    }

    private TestEnvironmentOrchestrator(string yamlPath)
    {
        _yamlPath = yamlPath;
    }

    public async Task InitializeAsync()
    {
        // 1. Load and parse YAML
        _config = LoadYaml();

        // 2. Create Docker network
        var networkName = _config.Networks?.Keys.FirstOrDefault() ?? "functional-tests";
        _network = new NetworkBuilder()
            .WithName($"{networkName}-{Guid.NewGuid():N}")
            .Build();
        await _network.CreateAsync();

        var randomPorts = _config.Settings.RandomHostPorts;

        // 3. Create and configure all modules
        foreach (var (serviceName, definition) in _config.Services)
        {
            if (!_moduleRegistry.TryGetValue(definition.Type, out var moduleType))
                throw new InvalidOperationException(
                    $"Unknown service type '{definition.Type}' for service '{serviceName}'. " +
                    $"Registered types: {string.Join(", ", _moduleRegistry.Keys)}");

            var module = (IContainerModule)Activator.CreateInstance(moduleType)!;
            module.Configure(definition, randomPorts);
            _modules[serviceName] = module;
        }

        // 4. Resolve startup tiers via topological sort
        var tiers = GetStartupTiers();

        // 5. Start containers tier by tier (health-gated via WaitStrategy)
        foreach (var tier in tiers)
        {
            // Wire up dependencies before building containers in this tier
            foreach (var serviceName in tier)
                WireDependencies(serviceName);

            // Build containers for this tier
            foreach (var serviceName in tier)
                _modules[serviceName].BuildContainer(_network);

            // Start all containers in this tier in parallel
            var tasks = tier.Select(name => _modules[name].Container.StartAsync());
            await Task.WhenAll(tasks);

            // Collect exposed variables after all containers in tier are healthy
            foreach (var serviceName in tier)
            {
                var vars = _modules[serviceName].GetExposedVariables();
                foreach (var (key, value) in vars)
                    ExposedVariables[key] = value;
            }
        }
    }

    private void WireDependencies(string serviceName)
    {
        var module = _modules[serviceName];

        // Wire ServiceBus → MsSql dependency
        if (module is ServiceBusEmulatorModule sbModule && _modules.TryGetValue("sqlserver", out var msSqlModule))
            sbModule.SetMsSqlDependency((MsSqlModule)msSqlModule);

        // Wire Generic (app) → all exposed variables for template substitution
        if (module is GenericModule genericModule)
            genericModule.SetExposedVariables(ExposedVariables);
    }

    private TestEnvironmentYaml LoadYaml()
    {
        var yamlFullPath = Path.Combine(AppContext.BaseDirectory, _yamlPath);
        if (!File.Exists(yamlFullPath))
            throw new FileNotFoundException($"YAML configuration not found: {yamlFullPath}");

        var yaml = File.ReadAllText(yamlFullPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<TestEnvironmentYaml>(yaml);
    }

    /// <summary>
    /// Topological sort of services based on depends_on relationships.
    /// Returns tiers of services that can be started in parallel within each tier.
    /// </summary>
    private List<List<string>> GetStartupTiers()
    {
        var allServices = new Dictionary<string, HashSet<string>>();
        foreach (var (name, def) in _config.Services)
        {
            var deps = def.DependsOn?.Keys.ToHashSet() ?? new HashSet<string>();
            allServices[name] = deps;
        }

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

    public async Task DisposeAsync()
    {
        // Dispose modules in reverse order
        var allNames = _modules.Keys.ToList();
        allNames.Reverse();

        foreach (var name in allNames)
        {
            try
            {
                await _modules[name].Container.DisposeAsync();
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_network != null!)
            await _network.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection definition — all test classes in this collection share
/// the same orchestrator (single set of containers).
/// </summary>
[CollectionDefinition("FunctionalTests")]
public class FunctionalTestsCollection : ICollectionFixture<TestEnvironmentOrchestrator>;

