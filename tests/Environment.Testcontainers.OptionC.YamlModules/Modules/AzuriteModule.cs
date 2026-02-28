using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using Testcontainers.Azurite;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

public sealed class AzuriteModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private AzuriteContainer _container = null!;

    public IContainer Container => _container;

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;
    }

    public IContainer BuildContainer(INetwork network)
    {
        var builder = new AzuriteBuilder(_definition.Image)
            .WithNetwork(network)
            .WithNetworkAliases(_definition.NetworkAlias);

        if (!_randomHostPorts && _definition.Ports != null)
        {
            foreach (var port in _definition.Ports)
                builder = builder.WithPortBinding(port.Host, port.Container);
        }

        _container = builder.Build();
        return _container;
    }

    public Dictionary<string, string> GetExposedVariables()
    {
        return new Dictionary<string, string>
        {
            ["azurite.connectionString"] = _container.GetConnectionString(),
            ["azurite.blobEndpoint"] = _container.GetBlobEndpoint(),
            ["azurite.queueEndpoint"] = _container.GetQueueEndpoint(),
            ["azurite.tableEndpoint"] = _container.GetTableEndpoint(),
            ["azurite.internalBlobConnectionString"] =
                $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://{_definition.NetworkAlias}:10000/devstoreaccount1"
        };
    }
}


