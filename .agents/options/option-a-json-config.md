# Option A: JSON Configuration + Factory Pattern ("Docker-Compose Lite")

## Overview

A `testcontainers.json` file loaded via `Microsoft.Extensions.Configuration` (just like `appsettings.json`). It defines `network`, `services` (mongo, mssql, azurite, wiremock, servicebus), and an `applicationUnderTest` block. Each service declares `image`, `ports`, `environmentVariables`, `networkAliases`, `dependsOn`, and optional `healthCheck`.

A `TestEnvironmentFactory` reads JSON → deserializes into strongly-typed POCOs → maps each service `type` to its Testcontainers module builder. A single xUnit `ICollectionFixture` implementing `IAsyncLifetime` creates the network, resolves `dependsOn` order, starts containers, and exposes connection strings.

The `applicationUnderTest` section uses template placeholders like `{{mongodb.connectionString}}`. After infra containers start, the factory resolves templates with real endpoints and passes them as env vars to a `GenericContainer`.

## Pros / Cons

| Pros | Cons |
|---|---|
| Closest to docker-compose feel | No compile-time safety; typos found at runtime |
| Easy to override per environment (CI vs local) via standard .NET config | Requires custom template resolver |
| Non-developers can read/edit | Service-type → builder mapping needs a registry |
| Supports env-var overrides out of the box | Health-check config limited to what JSON can express |

---

## File: `testcontainers.json`

```json
{
  "testEnvironment": {
    "network": {
      "name": "functional-tests-network"
    },
    "portMapping": {
      "randomHostPorts": true
    },
    "healthCheckDefaults": {
      "intervalSeconds": 5,
      "timeoutSeconds": 10,
      "retries": 10,
      "startPeriodSeconds": 15
    },
    "services": {
      "mongodb": {
        "type": "mongodb",
        "image": "mongo:7.0",
        "networkAlias": "mongo",
        "ports": {
          "container": 27017,
          "host": 27017
        },
        "environmentVariables": {
          "MONGO_INITDB_ROOT_USERNAME": "admin",
          "MONGO_INITDB_ROOT_PASSWORD": "password"
        }
      },
      "mssql": {
        "type": "mssql",
        "image": "mcr.microsoft.com/mssql/server:2022-latest",
        "networkAlias": "sqlserver",
        "ports": {
          "container": 1433,
          "host": 1433
        },
        "environmentVariables": {
          "ACCEPT_EULA": "Y",
          "SA_PASSWORD": "Strong!Passw0rd"
        }
      },
      "azurite": {
        "type": "azurite",
        "image": "mcr.microsoft.com/azure-storage/azurite:latest",
        "networkAlias": "azurite",
        "ports": {
          "blob": { "container": 10000, "host": 10000 },
          "queue": { "container": 10001, "host": 10001 },
          "table": { "container": 10002, "host": 10002 }
        }
      },
      "wiremock": {
        "type": "wiremock",
        "image": "wiremock/wiremock:3.12.1",
        "networkAlias": "wiremock",
        "ports": {
          "container": 8080,
          "host": 8080
        },
        "mappingsPath": "./wiremock/mappings"
      },
      "servicebus": {
        "type": "servicebus-emulator",
        "image": "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest",
        "networkAlias": "servicebus",
        "ports": {
          "container": 5672,
          "host": 5672
        },
        "dependsOn": ["mssql"],
        "configuration": {
          "configFilePath": "./servicebus/Config.json"
        },
        "healthCheck": {
          "test": "curl -f http://localhost:5672/ || exit 1",
          "intervalSeconds": 10,
          "timeoutSeconds": 15,
          "retries": 12,
          "startPeriodSeconds": 45
        }
      }
    },
    "applicationUnderTest": {
      "image": "mycompany/myapp:latest",
      "networkAlias": "app",
      "ports": {
        "container": 8080,
        "host": 8080
      },
      "dependsOn": ["mongodb", "mssql", "azurite", "wiremock", "servicebus"],
      "environmentVariables": {
        "ConnectionStrings__MongoDb": "{{mongodb.connectionString}}",
        "ConnectionStrings__SqlServer": "{{mssql.connectionString}}",
        "ConnectionStrings__AzureBlobStorage": "{{azurite.blobConnectionString}}",
        "ConnectionStrings__ServiceBus": "{{servicebus.connectionString}}",
        "ExternalApis__PaymentGateway": "http://wiremock:8080"
      },
      "waitStrategy": {
        "type": "httpGet",
        "path": "/health",
        "port": 8080,
        "timeoutSeconds": 60
      }
    }
  }
}
```

---

## Random Host Ports Flag

| `randomHostPorts` | Behavior |
|---|---|
| `true` | All `host` port values in the config are **ignored**. Docker assigns random ephemeral ports. Tests discover ports at runtime via module properties. |
| `false` (or omitted) | The `host` values are used as explicit port bindings (e.g., `27017:27017`). Useful for local debugging when you want predictable ports. |

Override in CI via environment variable:

```bash
export testEnvironment__portMapping__randomHostPorts=true
```

---

## Health-Aware Dependencies

### Where health checks come from (three layers, in priority order)

| Layer | Source | Example | When used |
|---|---|---|---|
| **1. Module built-in** | Testcontainers module's `WaitStrategy` | MsSql waits for `SELECT 1` | Always — every module has a default |
| **2. Config override** | `healthCheck` block in JSON | Custom curl command for Service Bus | When the built-in check isn't sufficient |
| **3. Global timeout** | Top-level `"healthCheckDefaults"` | Max timeout across all services | Safety net — prevents infinite hangs |

### What `dependsOn` actually means

> **Do not start this container until every dependency has:**
> 1. Started its container process
> 2. Passed its WaitStrategy (module built-in or config override)
> 3. Successfully responded to a health probe
>
> If a dependency fails its health check after all retries → **abort startup, fail the test with a clear error message.**

---

## Service Bus Emulator Dependency Chain

```
mssql  ──────►  servicebus  ──────►  app
                (requires mssql      (requires all)
                 as backing store)
```

The `dependsOn: ["mssql"]` on `servicebus` ensures MsSql is healthy before the emulator starts. The Service Bus Emulator module automatically configures itself to point to the MsSql container via the `sqlserver` network alias.

---

## Startup Flow with Health Gates

```
┌─────────────────────────────────────────────────────────────────────┐
│ Tier 1 (parallel): mongo, mssql, azurite, wiremock                 │
│                                                                     │
│   mongo   → start → WaitStrategy(port 27017 + ping)  → ✅ healthy  │
│   mssql   → start → WaitStrategy(SELECT 1)           → ✅ healthy  │
│   azurite → start → WaitStrategy(port ready)          → ✅ healthy  │
│   wiremock→ start → WaitStrategy(/__admin 200)        → ✅ healthy  │
│                                                                     │
│ ─── health gate: all tier 1 passed ───────────────────────────────  │
│                                                                     │
│ Tier 2: servicebus                                                  │
│   servicebus → start → healthCheck(curl localhost:5672) → ✅ healthy│
│                                                                     │
│ ─── health gate: servicebus passed ───────────────────────────────  │
│                                                                     │
│ Tier 3: app                                                         │
│   app → start → waitStrategy(GET /health 200)          → ✅ healthy │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Two-Perspective Model (Host vs Network)

```
From test code (host)  →  localhost:{random-or-fixed}   ← Testcontainers exposes this
From app container     →  mongo:27017                   ← via network alias, fixed container port
```

Templates like `{{mongodb.connectionString}}` resolve to the appropriate perspective based on context. For the app container's environment, internal (network alias) addresses are used. For test code, host-mapped ports are used.

