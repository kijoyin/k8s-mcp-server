# AGENTS.md — Instructions for AI Agents Working on This Repo

## Project Overview

**k8s-mcp-server**: A .NET 10 Model Context Protocol server exposing Kubernetes observability tools for AI agents.

**Stack**: .NET 10, MCP C# SDK 0.2.0-preview, KubernetesClient 13, Prometheus.Client, Aspire 9.

**Target Runtime**: Linux containers (distroless), Kubernetes (MicroK8s HA, AKS, EKS, GKE).

---

## Build & Test Commands

```bash
# Restore
dotnet restore k8s-mcp-server.slnx

# Build (Release, AOT)
dotnet build k8s-mcp-server.slnx -c Release --no-restore

# Test (with Testcontainers - requires Docker)
dotnet test k8s-mcp-server.slnx -c Release --no-build

# Run Aspire locally
dotnet run --project src/K8sMcpServer.Aspire/K8sMcpServer.Aspire.csproj

# Docker build
docker build -f deploy/Dockerfile -t k8s-mcp-server:local .
```

---

## Project Structure

```
src/
├── K8sMcpServer/              # Main MCP server (WebApplication)
│   ├── Tools/                 # MCP tool definitions (auto-discovered via [McpServerToolType])
│   ├── Services/              # K8s, Prometheus, Loki clients
│   ├── Middleware/            # Auth, request logging
│   ├── Program.cs             # DI, MCP server config, middleware pipeline
│   └── appsettings.json
├── K8sMcpServer.Tests/        # xUnit + Testcontainers (K8s, Prometheus, Loki)
└── K8sMcpServer.Aspire/       # Aspire AppHost for local dev

deploy/
├── k8s/                       # K8s manifests: Deployment, Service, RBAC, ArgoCD Application
├── docker/                    # Docker Compose (Prometheus + Loki + MCP)
└── Dockerfile                 # Multi-stage, distroless, AOT

docs/                          # Integration guides
.github/workflows/             # CI (build, test, docker, trivy) + CD (ArgoCD sync)
```

---

## Key Patterns

### Adding a New MCP Tool

1. Create `src/K8sMcpServer/Tools/YourTool.cs`
2. Add `[McpServerToolType]` class with `[McpServerTool]` methods
3. Inject required services via constructor
4. Return serializable result types (defined in `ServiceInterfaces.cs`)
5. Tool auto-registered via `WithToolsFromAssembly()`

```csharp
[McpServerToolType]
public sealed class YourTool
{
    private readonly IYourService _service;
    
    public YourTool(IYourService service) => _service = service;
    
    [McpServerTool(Name = "your_tool", Description = "What it does")]
    public async Task<YourResult> YourMethodAsync(
        [Description("Param description")] string param,
        CancellationToken ct = default)
    {
        return await _service.DoSomethingAsync(param, ct);
    }
}
```

### Service Registration

In `Program.cs`:
```csharp
builder.Services.AddSingleton<IYourService, YourService>();
builder.Services.AddHttpClient<IYourService, YourService>()
    .AddStandardResilienceHandler();
```

### Configuration

Add section to `appsettings.json`:
```json
"YourService": {
  "Url": "http://your-service:8080",
  "TimeoutSeconds": 30
}
```

Bind in `Program.cs`:
```csharp
builder.Services.Configure<YourOptions>(builder.Configuration.GetSection("YourService"));
```

---

## Testing Patterns

### Testcontainers Setup

Tests use real containers for K8s (kind), Prometheus, Loki:

```csharp
public class McpServerFixture : IAsyncLifetime
{
    private KubernetesContainer? _k8s;
    private PrometheusContainer? _prometheus;
    private LokiContainer? _loki;
    
    public async Task InitializeAsync()
    {
        _k8s = new KubernetesBuilder().Build();
        await _k8s.StartAsync();
        // ... same for Prometheus, Loki
    }
}
```

### MCP Protocol Test Helper

```csharp
public class McpTestClient
{
    private readonly HttpClient _http;
    
    public async Task<McpToolResponse> CallToolAsync(string toolName, object arguments)
    {
        // 1. Initialize session
        var init = await _http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "initialize", ... });
        var sessionId = init.Headers.GetValues("Mcp-Session-Id").FirstOrDefault();
        
        // 2. Call tool
        var response = await _http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/call", params = new { name = toolName, arguments } }, headersWith(sessionId));
        return parseResult(response);
    }
}
```

---

## MCP Protocol Details

- **Transport**: Stateless HTTP (MCP 2025-06-18 spec)
- **Endpoint**: `POST /mcp`
- **Session**: `Mcp-Session-Id` header (returned on initialize)
- **Auth**: Optional `X-MCP-API-Key` header
- **Tools**: Discovered via `tools/list`, invoked via `tools/call`

---

## Deployment Notes

### Kubernetes RBAC

ServiceAccount `k8s-mcp-server` in `monitoring` namespace needs:
- `nodes`, `pods`, `namespaces`, `events` — get/list/watch
- `nodes/metrics`, `pods/metrics` — get/list (metrics-server)
- `metrics.k8s.io` nodes/pods — get/list

### Prometheus Queries Used

| Tool | Query |
|------|-------|
| `check_disk_pressure` | `kubelet_volume_stats_available_bytes / kubelet_volume_stats_capacity_bytes < 0.15` |
| `check_oom_kills` | `increase(container_oom_events_total[1h]) > 0` |
| `check_pod_restarts` | `increase(kube_pod_container_status_restarts_total[1h]) > 3` |

### Loki Queries Used

| Tool | Query |
|------|-------|
| `query_error_logs` | `{job=~".+"} |= "ERROR" \|~ "(?i)(exception\|fail\|timeout\|refused\|denied\|unauthorized)"` |

---

## Common Issues

| Issue | Fix |
|-------|-----|
| AOT publish fails | Check `PublishAot=true`, `InvariantGlobalization=true`; avoid reflection-heavy APIs |
| Testcontainers timeout | Increase Docker resources; use `kubectl` context with kind |
| MCP session lost | Ensure `Mcp-Session-Id` header passed on subsequent calls |
| Prometheus connection refused | Verify service name/port; check network policies |
| Loki query returns empty | Check label selectors; verify Loki ingestion pipeline |

---

## Code Style

- **Nullable**: Enabled (`<Nullable>enable</Nullable>`)
- **ImplicitUsings**: Enabled
- **Records**: Prefer `record` for DTOs, `sealed class` for services
- **Async**: Always `CancellationToken` parameters
- **Logging**: Structured, use `ILogger<T>`, include correlation IDs
- **Errors**: Return `McpToolResult.Error()` not exceptions for tool failures

---

## PR Checklist

- [ ] Build passes (`dotnet build -c Release`)
- [ ] Tests pass (`dotnet test -c Release`)
- [ ] New tools have integration tests
- [ ] Configuration documented in `appsettings.json` example
- [ ] README updated if new tools added
- [ ] Docker image builds and runs