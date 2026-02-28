using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using Testcontainers.MsSql;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

public sealed class MsSqlModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private MsSqlContainer _container = null!;

    public IContainer Container => _container;
    public MsSqlContainer TypedContainer => _container;
    public string NetworkAlias => _definition.NetworkAlias;
    public string Password => _definition.Environment?.GetValueOrDefault("SA_PASSWORD", "Strong!Passw0rd")
                              ?? "Strong!Passw0rd";

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;
    }

    public IContainer BuildContainer(INetwork network)
    {
        var password = _definition.Environment?.GetValueOrDefault("SA_PASSWORD", "Strong!Passw0rd")
                       ?? "Strong!Passw0rd";

        var builder = new MsSqlBuilder(_definition.Image)
            .WithNetwork(network)
            .WithNetworkAliases(_definition.NetworkAlias)
            .WithPassword(password);

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
            ["mssql.connectionString"] = _container.GetConnectionString(),
            ["mssql.host"] = _container.Hostname,
            ["mssql.port"] = _container.GetMappedPublicPort(1433).ToString(),
            ["mssql.internalConnectionString"] =
                $"Server={_definition.NetworkAlias},1433;User Id=sa;Password={_definition.Environment?.GetValueOrDefault("SA_PASSWORD", "Strong!Passw0rd")};TrustServerCertificate=True"
        };
    }
}



