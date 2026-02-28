# Option C: YAML Config + Module Plugin System ("Modular Compose")

## Overview

A `test-environment.yaml` file using `YamlDotNet`, mimicking docker-compose syntax almost 1:1 — `services`, `networks`, `depends_on`, `environment`, `healthcheck`. Each service has a `type` field (mongodb, mssql, azurite, wiremock, servicebus-emulator, generic) mapped to a C# module class implementing `IContainerModule`.

A `TestEnvironmentOrchestrator` loads YAML, resolves modules, builds a dependency graph from `depends_on`, and manages lifecycle as the xUnit `IAsyncLifetime` fixture. Adding a new container type = one new class, zero orchestrator changes.

Environment variable substitution uses `${service.variable}` syntax. Each module exposes a variable dictionary after startup; the orchestrator performs substitution before creating dependent containers.

## Pros / Cons

| Pros | Cons |
|---|---|
| Most docker-compose-like (YAML + comments) | Extra `YamlDotNet` dependency |
| Most extensible — new types are just a new module class | Two places to look (YAML + C# module) |
| Clean separation: *what* (YAML) vs *how* (C#) | Slightly higher initial complexity |
| Supports custom/generic containers without core changes | Variable substitution needs a small resolver |
| Plugin architecture — zero orchestrator changes for new types | |

---

## File: `test-environment.yaml`

```yaml
# Functional test environment definition
# Syntax intentionally mirrors docker-compose for familiarity

networks:
  functional-tests:
    driver: bridge

# Global settings
settings:
  random_host_ports: true    # false = use explicit host ports below
  # Global health-check defaults — applied to any service that doesn't
  # override them. Acts as a safety net.
  health_defaults:
    interval: 5s
    timeout: 10s
    retries: 10
    start_period: 15s

services:

  # ── Infrastructure Services ──────────────────────────────

  mongodb:
    type: mongodb
    image: mongo:7.0
    network_alias: mongo
    ports:
      - host: 27017          # only used when random_host_ports: false
        container: 27017
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: password
    # Health: module built-in WaitStrategy (port 27017 + ping command).
    # No override needed.
    #
    # Exposed variables after startup:
    #   mongodb.connectionString          → mongodb://admin:password@localhost:{port}
    #   mongodb.internalConnectionString  → mongodb://admin:password@mongo:27017
    #   mongodb.host                      → localhost
    #   mongodb.port                      → {port}

  sqlserver:
    type: mssql
    image: mcr.microsoft.com/mssql/server:2022-latest
    network_alias: sqlserver
    ports:
      - host: 1433
        container: 1433
    environment:
      ACCEPT_EULA: "Y"
      SA_PASSWORD: "Strong!Passw0rd"
    # Health: module built-in WaitStrategy (SELECT 1 succeeds).
    # Override if needed:
    # healthcheck:
    #   wait_strategy: command
    #   test: ["/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Strong!Passw0rd", "-Q", "SELECT 1", "-C"]
    #   interval: 5s
    #   timeout: 15s
    #   retries: 15
    #   start_period: 30s
    #
    # Exposed variables:
    #   mssql.connectionString            → Server=localhost,{port};...
    #   mssql.internalConnectionString    → Server=sqlserver,1433;...

  azurite:
    type: azurite
    image: mcr.microsoft.com/azure-storage/azurite:latest
    network_alias: azurite
    ports:
      - host: 10000
        container: 10000     # blob
      - host: 10001
        container: 10001     # queue
      - host: 10002
        container: 10002     # table
    # Health: module built-in WaitStrategy (ports ready).
    #
    # Exposed variables:
    #   azurite.blobConnectionString           → ...BlobEndpoint=http://localhost:{port}/...
    #   azurite.internalBlobConnectionString   → ...BlobEndpoint=http://azurite:10000/...
    #   (same pattern for queue, table)

  wiremock:
    type: wiremock
    image: wiremock/wiremock:3.12.1
    network_alias: wiremock
    ports:
      - host: 8080
        container: 8080
    volumes:
      - ./wiremock/mappings:/home/wiremock/mappings
    # Health: module built-in WaitStrategy (/__admin/mappings → 200).
    #
    # Exposed variables:
    #   wiremock.url          → http://localhost:{port}
    #   wiremock.internalUrl  → http://wiremock:8080

  # Service Bus Emulator — requires sqlserver as its backing store
  servicebus:
    type: servicebus-emulator
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    network_alias: servicebus
    ports:
      - host: 5672
        container: 5672
    depends_on:
      sqlserver:
        condition: healthy         # ← EXPLICIT: wait for mssql health check to pass
    config:
      config_file: ./servicebus/Config.json
    # Override health check — emulator needs extra startup time
    healthcheck:
      wait_strategy: http
      path: /
      port: 5672
      interval: 10s
      timeout: 15s
      retries: 12
      start_period: 45s            # emulator takes a while to bootstrap
    #
    # Exposed variables:
    #   servicebus.connectionString          → Endpoint=sb://localhost:{port};...
    #   servicebus.internalConnectionString  → Endpoint=sb://servicebus:5672;...

  # ── Application Under Test ──────────────────────────────

  app:
    type: generic
    image: mycompany/myapp:latest
    network_alias: app
    ports:
      - host: 5000
        container: 8080
    depends_on:
      mongodb:
        condition: healthy
      sqlserver:
        condition: healthy
      azurite:
        condition: healthy
      wiremock:
        condition: healthy
      servicebus:
        condition: healthy
    environment:
      # Use INTERNAL variables — app is inside the Docker network,
      # so it reaches other containers via alias + fixed container port.
      ConnectionStrings__MongoDb:          "${mongodb.internalConnectionString}"
      ConnectionStrings__SqlServer:        "${mssql.internalConnectionString}"
      ConnectionStrings__AzureBlobStorage: "${azurite.internalBlobConnectionString}"
      ConnectionStrings__ServiceBus:       "${servicebus.internalConnectionString}"
      ExternalApis__PaymentGateway:        "${wiremock.internalUrl}"
      ASPNETCORE_ENVIRONMENT:              "Testing"
    healthcheck:
      wait_strategy: http
      path: /health
      port: 8080
      timeout: 60s
    #
    # Exposed variables (for test code running on the host):
    #   app.url   → http://localhost:{port}
    #   app.port  → {port}
```

---

## Random Host Ports Flag

```yaml
settings:
  random_host_ports: true     # Docker assigns ephemeral host ports
  # random_host_ports: false  # Uses the explicit host: values in ports
```

| `random_host_ports` | Port behavior | Use case |
|---|---|---|
| `true` | All `host:` values ignored; Docker assigns random ports | CI, parallel runs |
| `false` | `host:` values used as-is (e.g., `27017:27017`) | Local dev, debugging |

Override via environment file:

```yaml
# test-environment.ci.yaml (merged on top)
settings:
  random_host_ports: true

services:
  app:
    image: mycompany/myapp:${BUILD_TAG}
```

---

## Health-Aware Dependencies

### `depends_on` syntax mirrors Docker Compose v3 long form

**Short form (ambiguous — avoided):**
```yaml
depends_on:
  - sqlserver          # just ordering? or health-gated?
```

**Long form (explicit — used):**
```yaml
depends_on:
  sqlserver:
    condition: healthy   # unambiguous: wait until sqlserver passes its healthcheck
```

### The `healthcheck` / `wait_strategy` field

Each service can declare how its health is verified. This maps to Testcontainers `IWaitForContainerOS` strategies:

| `wait_strategy` | What it does | Mapped to |
|---|---|---|
| `http` | HTTP GET on path+port, wait for 200 | `Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(...)` |
| `tcp` | Wait for TCP port to accept connections | `Wait.ForUnixContainer().UntilPortIsAvailable(...)` |
| `command` | Run a command inside the container, wait for exit 0 | `Wait.ForUnixContainer().UntilCommandIsCompleted(...)` |
| `log` | Wait for a specific log message | `Wait.ForUnixContainer().UntilMessageIsLogged(...)` |
| *(omitted)* | Use the module's built-in WaitStrategy | Module default |

---

## Dependency Graph & Startup Order

```
                ┌──────────┐
                │  mssql   │
                └────┬─────┘
                     │ depends_on (condition: healthy)
          ┌──────────▼──────────┐
          │  servicebus-emulator │
          │  (uses mssql as DB)  │
          └──────────┬──────────┘
                     │
  ┌────────┬─────────┼──────────┬──────────┐
  │ mongo  │ azurite │          │ wiremock  │
  └───┬────┘────┬────┘          └─────┬────┘
      │         │       depends_on    │
      └─────────┴─────────┬───────────┘
                    ┌─────▼─────┐
                    │    app    │
                    └───────────┘
```

Orchestrator resolves this into tiers:
1. **Tier 1** (parallel): `mongodb`, `mssql`, `azurite`, `wiremock`
2. **Tier 2** (after mssql healthy): `servicebus`
3. **Tier 3** (after all healthy): `app`

---

## Orchestrator Startup Flow

```
Orchestrator.StartAsync():

1. Parse YAML → build dependency graph
2. Topological sort → startup tiers
3. For each tier (sequential):
   a. Start all containers in the tier (parallel)
   b. For each container:
      - Call container.StartAsync()
      - Testcontainers internally applies the WaitStrategy
      - If WaitStrategy times out → throw, abort all
   c. Wait for ALL containers in tier to report healthy
4. Only then proceed to next tier

Tier 1 (no deps):     mongo ✅  mssql ✅  azurite ✅  wiremock ✅
                           │
                    all healthy?  YES
                           │
Tier 2 (deps: mssql):     servicebus ✅
                           │
                    healthy?  YES
                           │
Tier 3 (deps: all):       app ✅
```

---

## Module Interface

```
interface IContainerModule
{
    // Called by orchestrator — module configures the container
    void Configure(ServiceDefinition yaml);

    // Module builds the container with appropriate WaitStrategy
    IContainer BuildContainer(INetwork network, HealthCheckConfig? overrides);
    //                                          ▲
    //                          If YAML has a healthcheck block, it's passed here.
    //                          Module merges it with its built-in strategy.

    // After StartAsync() succeeds (i.e., container IS healthy)
    Dictionary<string, string> GetExposedVariables();
}
```

### Module implementations

```
IContainerModule
├── MongoDbModule         # wraps MongoDbBuilder, exposes connectionString
├── MsSqlModule           # wraps MsSqlBuilder, exposes connectionString
├── AzuriteModule         # wraps AzuriteBuilder, exposes blob/queue/table strings
├── WireMockModule        # wraps WireMockContainerBuilder, exposes url
├── ServiceBusModule      # wraps emulator builder, validates mssql dep, exposes connectionString
└── GenericModule         # wraps ContainerBuilder for arbitrary images (the app)
```

### Adding a new service type

To add, say, RabbitMQ:

1. Install `Testcontainers.RabbitMq` NuGet package.
2. Create `RabbitMqModule : IContainerModule` (≈30 lines).
3. Register it: `modules["rabbitmq"] = typeof(RabbitMqModule)`.
4. Add it to the YAML — **zero changes to the orchestrator**.

```yaml
  rabbitmq:
    type: rabbitmq
    image: rabbitmq:4.1-management
    network_alias: rabbitmq
    ports:
      - host: 5672
        container: 5672
      - host: 15672
        container: 15672
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
```

---

## Two-Perspective Model

```
┌─────────────────────────────────────────────────────────────┐
│  Docker Network: functional-tests                           │
│                                                             │
│   mongo:27017    sqlserver:1433    azurite:10000            │
│   wiremock:8080  servicebus:5672  app:8080                  │
│                                                             │
│   (containers talk to each other via alias + fixed port)    │
└──────────────────────────┬──────────────────────────────────┘
                           │ Docker port mapping
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Host (where tests run)                                     │
│                                                             │
│   localhost:55321  (mongo)         — random or fixed         │
│   localhost:49201  (sqlserver)     — random or fixed         │
│   localhost:61002  (azurite blob)  — random or fixed         │
│   localhost:62010  (wiremock)      — random or fixed         │
│   localhost:63100  (servicebus)    — random or fixed         │
│   localhost:58443  (app)           — random or fixed         │
│                                                             │
│   (tests use host ports via exposed variables)              │
└─────────────────────────────────────────────────────────────┘
```

---

## Failure Messages

```
TestEnvironmentStartupException:
  Service 'servicebus' dependency 'sqlserver' failed health check.

  WaitStrategy: command [sqlcmd -S localhost -U sa -P *** -Q "SELECT 1"]
  Retries: 15/15 exhausted
  Elapsed: 75s (timeout: 60s)
  Last error: Connection refused

  Container logs (last 20 lines):
  ...
```

