using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Environment.Testcontainers.OptionC.YamlModules.Modules;

/// <summary>
/// Plugin interface for container modules. Each service type (mongodb, mssql, etc.)
/// implements this interface to encapsulate container creation and variable exposure.
/// </summary>
public interface IContainerModule
{
    /// <summary>
    /// Configure the module from the YAML service definition.
    /// </summary>
    void Configure(Infrastructure.ServiceDefinition definition, bool randomHostPorts);

    /// <summary>
    /// Build and return the container. The module applies its built-in WaitStrategy.
    /// </summary>
    IContainer BuildContainer(INetwork network);

    /// <summary>
    /// After the container has started, return exposed variables
    /// (connection strings, URLs, etc.) for template substitution.
    /// </summary>
    Dictionary<string, string> GetExposedVariables();

    /// <summary>
    /// The started container instance.
    /// </summary>
    IContainer Container { get; }
}

