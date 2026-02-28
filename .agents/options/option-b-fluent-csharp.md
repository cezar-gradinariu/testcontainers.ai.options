# Option B: Strongly-Typed C# Fluent DSL ("Fluent Compose")

## Overview

No external config file required (optional overrides-only JSON). Everything is defined in a C# `TestEnvironmentBuilder` with a fluent API: `.AddMongoDb(...)`, `.AddMsSql(...)`, `.AddAzurite()`, `.AddWireMock()`, `.AddServiceBusEmulator()`, `.AddApplicationUnderTest(...)`, `.Build()`.

The builder produces a `TestEnvironment` (implements `IAsyncLifetime`) holding a dependency graph. One shared `INetwork` is created and injected into every container. Services are accessed via typed accessors: `environment.MongoDb.ConnectionString`. Used as `ICollectionFixture<TestEnvironment>`.

Connection string wiring for the app container happens via a type-safe lambda with full IntelliSense — no string-based template engine needed.

## Pros / Cons

| Pros | Cons |
|---|---|
| Full type safety and IntelliSense | Config is code — requires recompile to change |
| Easy to debug; step through setup | Less accessible to non-C# developers |
| Complex health-check logic as lambdas | Harder to share config across solutions |
| No template engine needed | Slightly more verbose for simple setups |
| Refactoring-safe: rename a service → compiler finds all references | |

---

## `TestEnvironmentSetup.cs` (conceptual)

```csharp
public class FunctionalTestEnvironment : IAsyncLifetime
{
    private readonly TestEnvironment _environment;

    public FunctionalTestEnvironment()
    {
        _environment = new TestEnvironmentBuilder()
            .WithNetwork("functional-tests")

            // The flag: random vs fixed host ports
            .WithRandomHostPorts(true)  // false = use explicit HostPort values below

            // Global health-check defaults (safety net for all containers)
            .WithHealthCheckDefaults(health =>
            {
                health.Interval = TimeSpan.FromSeconds(5);
                health.Timeout = TimeSpan.FromSeconds(10);
                health.Retries = 10;
                health.StartPeriod = TimeSpan.FromSeconds(15);
            })

            .AddMongoDb(mongo =>
            {
                mongo.Alias = "mongo";
                mongo.Image = "mongo:7.0";
                mongo.Username = "admin";
                mongo.Password = "password";
                mongo.HostPort = 27017;   // only used when randomHostPorts = false
                // Built-in WaitStrategy: port 27017 accepting connections
                // + successful mongosh ping. No config needed.
            })

            .AddMsSql(sql =>
            {
                sql.Alias = "sqlserver";
                sql.Image = "mcr.microsoft.com/mssql/server:2022-latest";
                sql.Password = "Strong!Passw0rd";
                sql.HostPort = 1433;
                // Built-in WaitStrategy: SELECT 1 succeeds.
                // Optionally override:
                // sql.WaitStrategy = Wait.ForUnixContainer()
                //     .UntilCommandIsCompleted(
                //         "/opt/mssql-tools/bin/sqlcmd",
                //         "-S", "localhost", "-U", "sa", "-P", "Strong!Passw0rd",
                //         "-Q", "SELECT 1");
            })

            .AddAzurite(azurite =>
            {
                azurite.Alias = "azurite";
                azurite.BlobHostPort = 10000;
                azurite.QueueHostPort = 10001;
                azurite.TableHostPort = 10002;
                // Built-in WaitStrategy: blob port ready.
            })

            .AddWireMock(wiremock =>
            {
                wiremock.Alias = "wiremock";
                wiremock.MappingsPath = "./wiremock/mappings";
                wiremock.HostPort = 8080;
                // Built-in WaitStrategy: /__admin/mappings returns 200.
            })

            // Service Bus Emulator — depends on MsSql
            .AddServiceBusEmulator(servicebus =>
            {
                servicebus.Alias = "servicebus";
                servicebus.HostPort = 5672;
                servicebus.ConfigFilePath = "./servicebus/Config.json";
                // DependsOn("sqlserver") is implicit — the module enforces it.
                // The module automatically wires itself to use the MsSql
                // container via the "sqlserver" network alias.

                // Override WaitStrategy if built-in isn't sufficient:
                servicebus.WaitStrategy = Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPath("/")
                        .ForPort(5672)
                        .ForStatusCode(HttpStatusCode.OK))
                    .WithTimeout(TimeSpan.FromSeconds(120));
            })

            .AddApplicationUnderTest(app =>
            {
                app.Image = "mycompany/myapp:latest";
                app.Alias = "app";
                app.HostPort = 8080;
                app.DependsOn("mongo", "sqlserver", "azurite", "wiremock", "servicebus");

                // Wire connection strings — fully type-safe, IntelliSense-enabled
                app.WithEnvironment(ctx => new Dictionary<string, string>
                {
                    // Internal connection strings: alias + fixed container port
                    ["ConnectionStrings__MongoDb"]          = ctx.MongoDb.InternalConnectionString,
                    ["ConnectionStrings__SqlServer"]        = ctx.MsSql.InternalConnectionString,
                    ["ConnectionStrings__AzureBlobStorage"] = ctx.Azurite.InternalBlobConnectionString,
                    ["ConnectionStrings__ServiceBus"]       = ctx.ServiceBus.InternalConnectionString,
                    ["ExternalApis__PaymentGateway"]        = "http://wiremock:8080"
                });

                // App's own health gate
                app.WaitStrategy = Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPath("/health")
                        .ForPort(8080))
                    .WithTimeout(TimeSpan.FromSeconds(60));
            })

            .Build();
    }

    // Typed accessors — no casting, no string keys
    public MongoDbTestContainer      MongoDb    => _environment.MongoDb;
    public MsSqlTestContainer        MsSql      => _environment.MsSql;
    public AzuriteTestContainer      Azurite    => _environment.Azurite;
    public WireMockTestContainer     WireMock   => _environment.WireMock;
    public ServiceBusTestContainer   ServiceBus => _environment.ServiceBus;
    public AppTestContainer          App        => _environment.App;

    public Task InitializeAsync() => _environment.StartAsync();
    public Task DisposeAsync()    => _environment.StopAsync();
}
```

---

## Random Host Ports Flag

```csharp
// Toggle via code:
.WithRandomHostPorts(true)

// Or load from config/environment:
.WithRandomHostPorts(configuration.GetValue<bool>("RandomHostPorts"))
```

| `WithRandomHostPorts` | Behavior |
|---|---|
| `true` | All `.HostPort` values are ignored. `env.MongoDb.Port` returns the random port. |
| `false` | `.HostPort` values are applied as explicit bindings. `env.MongoDb.Port` returns `27017`. |

### Optional overrides file: `testcontainers.overrides.json`

```json
{
  "overrides": {
    "randomHostPorts": true,
    "mongodb": { "image": "mongo:8.0" },
    "mssql":   { "image": "mcr.microsoft.com/mssql/server:2025-latest" }
  }
}
```

The builder can optionally load this file to override image tags and the port flag without recompilation — but the *structure* is always defined in C#.

---

## Health-Aware Dependencies

### How `DependsOn` enforces health

Inside the builder, `DependsOn` is implemented via tiered startup:

```csharp
// Pseudocode — what StartAsync() does internally
async Task StartAsync()
{
    var tiers = TopologicalSort(dependencyGraph);

    foreach (var tier in tiers)
    {
        // Start all containers in this tier in parallel
        var tasks = tier.Select(service => StartAndWaitUntilHealthy(service));
        await Task.WhenAll(tasks);
        // ▲ Only proceeds to next tier when ALL containers in this tier
        //   have passed their WaitStrategy (i.e., are healthy).
    }
}
```

**Key insight:** Testcontainers `StartAsync()` already blocks until the container's `WaitStrategy` passes. So tiered startup naturally gives health-gated dependencies.

### Service Bus Emulator → MsSql relationship

The `.AddServiceBusEmulator()` module **implicitly requires MsSql**:
- If no `.AddMsSql()` was called → **compile-time error** (the `ctx.MsSql` reference won't resolve).
- The module internally configures the emulator to connect to `sqlserver:1433` (the MsSql network alias).
- Startup order is enforced: MsSql starts and passes health check → Service Bus Emulator starts.

```
Startup order (automatic):
  1. mongo, azurite, wiremock     (parallel — no dependencies)
  2. mssql                        (parallel with tier 1, no deps)
  3. servicebus                   (after mssql is healthy)
  4. app                          (after all infra is healthy)
```

---

## How Tests Use It

```csharp
[Collection("FunctionalTests")]
public class OrderApiTests(FunctionalTestEnvironment env)
{
    [Fact]
    public async Task CreateOrder_PublishesToServiceBus()
    {
        // Port is random or fixed depending on the flag — code doesn't care
        var mongoClient = new MongoClient(env.MongoDb.ConnectionString);
        var sbClient    = new ServiceBusClient(env.ServiceBus.ConnectionString);
        var appUrl      = env.App.BaseUrl;

        // ... test logic
    }
}
```

### Key distinction: Internal vs External connection strings

| Property | Value | Used by |
|---|---|---|
| `ctx.MongoDb.ConnectionString` | `mongodb://admin:password@localhost:55321` | Test code (from host) |
| `ctx.MongoDb.InternalConnectionString` | `mongodb://admin:password@mongo:27017` | App container (inside network) |

---

## Failure Modes

| Scenario | What happens |
|---|---|
| MsSql takes too long to start | `StartAsync()` throws `TimeoutException` after configured timeout → test fails with clear error |
| MsSql starts but Service Bus can't connect to it | Service Bus WaitStrategy fails → `StartAsync()` throws → test fails |
| All infra healthy but app crashes on startup | App's WaitStrategy (GET /health) times out → test fails |
| Circular dependency declared | `.Build()` throws `InvalidOperationException("Circular dependency detected: A → B → A")` at fixture construction time |

