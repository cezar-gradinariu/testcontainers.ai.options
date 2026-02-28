using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;
using WireMock.Net.Testcontainers;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

public sealed class WireMockModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private WireMockContainer _container = null!;

    public IContainer Container => _container;

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;
    }

    public IContainer BuildContainer(INetwork network)
    {
        // WireMockContainerBuilder uses its own default image (sheyenrath/wiremock.net).
        // Do NOT override with the Java-based wiremock/wiremock image.
        var builder = new WireMockContainerBuilder()
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
            ["wiremock.url"] = _container.GetPublicUrl(),
            ["wiremock.internalUrl"] = $"http://{_definition.NetworkAlias}:8080"
        };
    }
}

