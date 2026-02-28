using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Environment.Testcontainers.OptionC.YamlModules.Infrastructure;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

/// <summary>
/// Generic container module for arbitrary images (e.g. the application under test).
/// Supports environment variable template substitution via ${service.variable} syntax.
/// </summary>
public sealed class GenericModule : IContainerModule
{
    private ServiceDefinition _definition = null!;
    private bool _randomHostPorts;
    private IContainer _container = null!;
    private Dictionary<string, string> _allExposedVariables = new();

    public IContainer Container => _container;

    /// <summary>
    /// Inject all exposed variables from other modules for template substitution.
    /// </summary>
    public void SetExposedVariables(Dictionary<string, string> variables)
    {
        _allExposedVariables = variables;
    }

    public void Configure(ServiceDefinition definition, bool randomHostPorts)
    {
        _definition = definition;
        _randomHostPorts = randomHostPorts;
    }

    public IContainer BuildContainer(INetwork network)
    {
        var containerPort = _definition.Ports?.FirstOrDefault()?.Container ?? 8080;

        var builder = new ContainerBuilder(_definition.Image)
            .WithNetwork(network)
            .WithNetworkAliases(_definition.NetworkAlias)
            .WithExposedPort(containerPort);

        if (!_randomHostPorts && _definition.Ports?.Count > 0)
        {
            var port = _definition.Ports[0];
            builder = builder.WithPortBinding(port.Host, port.Container);
        }
        else
        {
            builder = builder.WithPortBinding(containerPort, true);
        }

        // Resolve environment variable templates
        if (_definition.Environment != null)
        {
            foreach (var (key, value) in _definition.Environment)
            {
                var resolved = ResolveTemplate(value);
                builder = builder.WithEnvironment(key, resolved);
            }
        }

        _container = builder.Build();
        return _container;
    }

    public Dictionary<string, string> GetExposedVariables()
    {
        var containerPort = _definition.Ports?.FirstOrDefault()?.Container ?? 8080;
        var hostPort = _container.GetMappedPublicPort(containerPort);

        return new Dictionary<string, string>
        {
            ["app.url"] = $"http://{_container.Hostname}:{hostPort}",
            ["app.port"] = hostPort.ToString()
        };
    }

    private string ResolveTemplate(string template)
    {
        return Regex.Replace(template, @"\$\{([^}]+)\}", match =>
        {
            var key = match.Groups[1].Value;
            return _allExposedVariables.TryGetValue(key, out var resolved) ? resolved : match.Value;
        });
    }
}


