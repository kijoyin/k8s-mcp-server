# CLAUDE.md — Instructions for Claude Code

## Project Context

**k8s-mcp-server**: .NET 10 MCP server for Kubernetes observability. First .NET-native MCP server for K8s (others are Go/TypeScript).

**Key differentiator**: Observability-first tools (`check_oom_kills`, `check_disk_pressure`) not raw API wrappers. Built for scheduled AI agents (Hermes), not chat.

## Quick Commands

```bash
# Build everything
dotnet build k8s-mcp-server.slnx -c Release

# Test (needs Docker for Testcontainers)
dotnet test k8s-mcp-server.slnx -c Release

# Run locally with Aspire (Prometheus + Loki + MCP)
dotnet run --project src/K8sMcpServer.Aspire

# Docker
docker build -f deploy/Dockerfile -t k8s-mcp-server .
```

## Architecture Notes

- **MCP Transport**: Stateless HTTP (2025-06-18 spec), endpoint `POST /mcp`
- **Tool Discovery**: `[McpServerToolType]` attribute + `WithToolsFromAssembly()`
- **DI**: All services singleton, HttpClient with resilience handlers
- **AOT**: Enabled in csproj — avoid reflection, use source generators
- **Distroless**: Runtime image is `mcr.microsoft.com/dotnet/runtime-deps:10.0-preview-distroless`

## Adding Tools (Claude: Do This)

1. Create `src/K8sMcpServer/Tools/NewTool.cs`
2. Follow pattern in `Tools/` — sealed class, `[McpServerToolType]`, `[McpServerTool]` methods
3. Inject services via constructor (IKubernetesService, IPrometheusService, ILokiService)
4. Return types from `Services/ServiceInterfaces.cs` (or add new ones there)
5. Add unit test in `K8sMcpServer.Tests`

Example tool signature:
```csharp
[McpServerTool(Name = "your_tool", Description = "One-line description")]
public async Task<YourResult> YourMethodAsync(
    [Description("Param description")] string param,
    CancellationToken ct = default)
```

## Configuration

- `appsettings.json` has all sections with comments
- Environment variables override: `Prometheus__Url`, `Loki__Url`, `Mcp__RequireAuth`, `McpAuth__ApiKey`
- In-cluster K8s config by default; `Kubernetes__KubeConfigPath` for local dev

## Testing Strategy

- **Testcontainers**: Real Kind cluster, Prometheus, Loki per test run
- **McpTestClient**: Handles session initialization + tool calls
- **Integration tests only** — no mocks for external dependencies
- Run: `dotnet test --filter FullyQualifiedName~McpServerTests`

## Deployment

- **K8s**: `deploy/k8s/` — Deployment, Service, ServiceAccount, ClusterRole, ArgoCD Application
- **RBAC**: Minimal — nodes/pods/namespaces/events + metrics.k8s.io
- **Cloudflare Tunnel**: `docs/cloudflare-tunnel.md` for public HTTPS + Zero Trust
- **ArgoCD**: Auto-sync from `main` branch, path `deploy/k8s`

## Common Fixes (Claude: Check These First)

| Error | Likely Cause |
|-------|--------------|
| AOT publish fails | Reflection in JSON serialization — use `JsonSerializerOptions` with `TypeInfoResolver` |
| MCP session lost | Missing `Mcp-Session-Id` header on subsequent calls |
| Prometheus 502 | Wrong service name/port in `Prometheus__Url` |
| Loki empty results | Label selector mismatch — check `{job=~".+"}` pattern |
| Testcontainers timeout | Docker Desktop resources too low (needs 4GB+ RAM) |

## Code Style Rules

- **Files**: One type per file, namespace matches folder
- **Nullability**: Enabled — no `!` unless proven non-null
- **Async**: Every external call takes `CancellationToken`
- **Logging**: `ILogger<T>`, structured, include correlation ID from middleware
- **Results**: Tools return data types, not `McpToolResult` — framework wraps

## When You're Stuck

1. Check `AGENTS.md` for detailed patterns
2. Look at existing tools in `Tools/` — copy/paste/adapt
3. Check `Services/ServiceInterfaces.cs` for result types
4. Run tests to verify: `dotnet test --filter "ClusterHealth"`

## Don't Do

- ❌ Add raw K8s API wrappers (`list_pods`, `get_deployment`) — use existing projects for that
- ❌ Use `HttpClient` directly — use `AddHttpClient<T>` with resilience
- ❌ Return exceptions from tools — return error data in result type
- ❌ Add NuGet packages without checking AOT compatibility
- ❌ Commit without running `dotnet build -c Release` and `dotnet test`