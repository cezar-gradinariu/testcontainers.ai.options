namespace Environment.Testcontainers.OptionA.JsonConfig.Configuration;

/// <summary>
/// Root configuration model bound from testcontainers.json.
/// </summary>
public sealed class TestEnvironmentConfig
{
    public NetworkConfig Network { get; set; } = new();
    public PortMappingConfig PortMapping { get; set; } = new();
    public Dictionary<string, ServiceConfig> Services { get; set; } = new();
    public ApplicationUnderTestConfig? ApplicationUnderTest { get; set; }
}

public sealed class NetworkConfig
{
    public string Name { get; set; } = "functional-tests";
}

public sealed class PortMappingConfig
{
    public bool RandomHostPorts { get; set; } = true;
}

public sealed class ServiceConfig
{
    public string Type { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string NetworkAlias { get; set; } = string.Empty;
    public PortConfig Ports { get; set; } = new();
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public List<string> DependsOn { get; set; } = new();
}

public sealed class PortConfig
{
    public int Container { get; set; }
    public int Host { get; set; }
}

public sealed class ApplicationUnderTestConfig
{
    public string Image { get; set; } = string.Empty;
    public string NetworkAlias { get; set; } = "app";
    public PortConfig Ports { get; set; } = new();
    public List<string> DependsOn { get; set; } = new();
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
}
