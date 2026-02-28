using YamlDotNet.Serialization;

namespace Environment.Testcontainers.OptionC.YamlModules.Infrastructure;

/// <summary>
/// Root model deserialized from test-environment.yaml.
/// </summary>
public sealed class TestEnvironmentYaml
{
    [YamlMember(Alias = "networks")]
    public Dictionary<string, NetworkDefinition>? Networks { get; set; }

    [YamlMember(Alias = "settings")]
    public SettingsDefinition Settings { get; set; } = new();

    [YamlMember(Alias = "services")]
    public Dictionary<string, ServiceDefinition> Services { get; set; } = new();
}

public sealed class NetworkDefinition
{
    [YamlMember(Alias = "driver")]
    public string Driver { get; set; } = "bridge";
}

public sealed class SettingsDefinition
{
    [YamlMember(Alias = "random_host_ports")]
    public bool RandomHostPorts { get; set; } = true;

    [YamlMember(Alias = "health_defaults")]
    public HealthDefaultsDefinition? HealthDefaults { get; set; }
}

public sealed class HealthDefaultsDefinition
{
    [YamlMember(Alias = "interval")]
    public string? Interval { get; set; }

    [YamlMember(Alias = "timeout")]
    public string? Timeout { get; set; }

    [YamlMember(Alias = "retries")]
    public int Retries { get; set; } = 10;

    [YamlMember(Alias = "start_period")]
    public string? StartPeriod { get; set; }
}

public sealed class ServiceDefinition
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; set; } = string.Empty;

    [YamlMember(Alias = "network_alias")]
    public string NetworkAlias { get; set; } = string.Empty;

    [YamlMember(Alias = "ports")]
    public List<PortMapping>? Ports { get; set; }

    [YamlMember(Alias = "environment")]
    public Dictionary<string, string>? Environment { get; set; }

    [YamlMember(Alias = "depends_on")]
    public Dictionary<string, DependencyCondition>? DependsOn { get; set; }

    [YamlMember(Alias = "healthcheck")]
    public HealthCheckDefinition? HealthCheck { get; set; }

    [YamlMember(Alias = "config")]
    public Dictionary<string, string>? Config { get; set; }
}

public sealed class PortMapping
{
    [YamlMember(Alias = "host")]
    public int Host { get; set; }

    [YamlMember(Alias = "container")]
    public int Container { get; set; }
}

public sealed class DependencyCondition
{
    [YamlMember(Alias = "condition")]
    public string Condition { get; set; } = "healthy";
}

public sealed class HealthCheckDefinition
{
    [YamlMember(Alias = "wait_strategy")]
    public string? WaitStrategy { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "port")]
    public int? Port { get; set; }

    [YamlMember(Alias = "interval")]
    public string? Interval { get; set; }

    [YamlMember(Alias = "timeout")]
    public string? Timeout { get; set; }

    [YamlMember(Alias = "retries")]
    public int? Retries { get; set; }

    [YamlMember(Alias = "start_period")]
    public string? StartPeriod { get; set; }

    [YamlMember(Alias = "test")]
    public string? Test { get; set; }
}

