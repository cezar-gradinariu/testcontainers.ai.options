using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

/// <summary>
/// Service Bus Emulator module. Requires MsSql to be started first.
/// The orchestrator must pass the MsSql module via SetMsSqlDependency() before building.
/// </summary>
public sealed class ServiceBusEmulatorModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private ServiceBusContainer _container = null!;
    private MsSqlModule? _msSqlModule;

    public IContainer Container => _container;

    /// <summary>
    /// Called by the orchestrator to inject the MsSql dependency.
    /// </summary>
    public void SetMsSqlDependency(MsSqlModule msSqlModule)
    {
        _msSqlModule = msSqlModule;
    }

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;

        // Validate that depends_on includes sqlserver
        if (_definition.DependsOn == null || !_definition.DependsOn.ContainsKey("sqlserver"))
            throw new InvalidOperationException(
                "ServiceBus Emulator requires 'sqlserver' in depends_on. " +
                "The emulator uses MsSql as its backing store.");
    }

    public IContainer BuildContainer(INetwork network)
    {
        if (_msSqlModule == null)
            throw new InvalidOperationException(
                "MsSql dependency not set. Call SetMsSqlDependency() before BuildContainer().");

        // Do NOT call .WithNetwork() — WithMsSqlContainer() adds the network internally.
        var builder = new ServiceBusBuilder(_definition.Image)
            .WithNetworkAliases(_definition.NetworkAlias)
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(network, _msSqlModule.TypedContainer,
                _msSqlModule.NetworkAlias, _msSqlModule.Password);

        if (!_randomHostPorts && _definition.Ports?.Count > 0)
        {
            var port = _definition.Ports[0];
            builder = builder.WithPortBinding(port.Host, port.Container);
        }

        _container = builder.Build();
        return _container;
    }

    public Dictionary<string, string> GetExposedVariables()
    {
        return new Dictionary<string, string>
        {
            ["servicebus.connectionString"] = _container.GetConnectionString(),
            ["servicebus.internalConnectionString"] =
                $"Endpoint=sb://{_definition.NetworkAlias};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
        };
    }
}


