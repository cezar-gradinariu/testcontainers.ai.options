using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using Testcontainers.MongoDb;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

public sealed class MongoDbModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private MongoDbContainer _container = null!;

    public IContainer Container => _container;

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;
    }

    public IContainer BuildContainer(INetwork network)
    {
        var builder = new MongoDbBuilder(_definition.Image)
            .WithNetwork(network)
            .WithNetworkAliases(_definition.NetworkAlias);

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
            ["mongodb.connectionString"] = _container.GetConnectionString(),
            ["mongodb.host"] = _container.Hostname,
            ["mongodb.port"] = _container.GetMappedPublicPort(27017).ToString(),
            ["mongodb.internalConnectionString"] =
                $"mongodb://{_definition.NetworkAlias}:27017"
        };
    }
}


