# K8s MCP Server

> **A .NET 10 Model Context Protocol server that exposes Kubernetes observability tools for AI agents.**

[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-2025--06--18-green)](https://modelcontextprotocol.io/)
[![Kubernetes](https://img.shields.io/badge/Kubernetes-1.30+-blue)](https://kubernetes.io/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

## Why This Exists

Most MCP servers for Kubernetes expose raw API wrappers (`list_pods`, `get_deployment`, etc.). This server is different:

- **Observability-first tools** — Pre-canned checks like `check_oom_kills`, `check_disk_pressure`, `check_pod_restarts`
- **Built for agents** — Designed for scheduled AI operators (Hermes, custom agents) not interactive chat
- **.NET 10 native** — First .NET MCP server for Kubernetes (others are Go/TypeScript)
- **Home lab ready** — Runs on MicroK8s HA, deploys via ArgoCD, exposed via Cloudflare Tunnel

## Tools Provided

| Tool | Purpose | Example Use |
|------|---------|-------------|
| `cluster_health` | Overall cluster summary | Daily briefing header |
| `check_disk_pressure` | Nodes with <15% disk | Alert before OOM |
| `check_oom_kills` | OOM kills in last hour | Detect memory issues |
| `check_pod_restarts` | Pods restarting >3x/hour | Catch crash loops early |
| `query_metrics` | Arbitrary PromQL | Custom dashboards |
| `query_error_logs` | Loki error log search | Incident investigation |

## Quick Start

### Local Development (Aspire)

```bash
cd src/K8sMcpServer.Aspire
dotnet run
```

This starts:
- MCP Server at `http://localhost:8080`
- Prometheus at `http://localhost:9090`
- Loki at `http://localhost:3100`
- Aspire Dashboard at `http://localhost:15888`

### Docker Compose

```bash
cd deploy/docker
docker-compose up -d
```

### Kubernetes (Production)

```bash
# Apply manifests
kubectl apply -f deploy/k8s/

# Or use ArgoCD (recommended)
kubectl apply -f deploy/k8s/argocd-app.yaml
```

## Architecture

```
┌─────────────────┐     MCP/HTTP      ┌──────────────────┐
│  AI Agent       │ ◄────────────────► │ K8s MCP Server   │
│  (Hermes, etc.) │  tools/call       │  (.NET 10)       │
└─────────────────┘                   └────────┬─────────┘
                                                │
                    ┌───────────────────────────┼───────────────────────────┐
                    ▼                           ▼                           ▼
            ┌───────────────┐           ┌───────────────┐           ┌───────────────┐
            │ Kubernetes    │           │ Prometheus    │           │ Loki          │
            │ API Server    │           │ (metrics)     │           │ (logs)        │
            └───────────────┘           └───────────────┘           └───────────────┘
```

## Configuration

| Environment Variable | Description | Default |
|---------------------|-------------|---------|
| `Kubernetes__UseInClusterConfig` | Use in-cluster auth | `true` |
| `Prometheus__Url` | Prometheus endpoint | `http://prometheus:9090` |
| `Loki__Url` | Loki endpoint | `http://loki:3100` |
| `Mcp__RequireAuth` | Require API key | `false` |
| `McpAuth__ApiKey` | API key for auth | (none) |

## Hermes Agent Integration

See [docs/hermes-integration.md](docs/hermes-integration.md) for:
- MCP client skill for Hermes
- Daily briefing cron job (Telegram delivery)
- Environment configuration

## Deployment

| Component | Guide |
|-----------|-------|
| Docker | `deploy/Dockerfile` |
| Kubernetes | `deploy/k8s/` |
| ArgoCD | `deploy/k8s/argocd-app.yaml` |
| Cloudflare Tunnel | `docs/cloudflare-tunnel.md` |
| Telegram Bot | `docs/telegram-bot.md` |

## Testing

```bash
# Run tests with Testcontainers
dotnet test src/K8sMcpServer.Tests/K8sMcpServer.Tests.csproj
```

## Project Structure

```
k8s-mcp-server/
├── src/
│   ├── K8sMcpServer/           # Main MCP server
│   │   ├── Tools/              # 6 MCP tools
│   │   ├── Services/           # K8s, Prometheus, Loki clients
│   │   ├── Middleware/         # Auth, logging
│   │   └── Program.cs
│   ├── K8sMcpServer.Tests/     # xUnit + Testcontainers
│   └── K8sMcpServer.Aspire/    # Aspire AppHost
├── deploy/
│   ├── k8s/                    # K8s manifests + ArgoCD
│   └── docker/                 # Docker Compose for local
├── docs/
│   ├── hermes-integration.md
│   ├── telegram-bot.md
│   └── cloudflare-tunnel.md
└── .github/workflows/          # CI/CD
```

## Contributing

1. Fork the repo
2. Create a feature branch
3. Add tests for new tools
4. Submit PR

## License

MIT — see [LICENSE](LICENSE)

## Related Projects

- [containers/kubernetes-mcp-server](https://github.com/containers/kubernetes-mcp-server) — Go, full K8s API
- [Flux159/mcp-server-kubernetes](https://github.com/Flux159/mcp-server-kubernetes) — TypeScript, kubectl-style
- [prometheus/prometheus-mcp](https://github.com/prometheus/prometheus-mcp) — Official Prometheus MCP
- [rhobs/obs-mcp](https://github.com/rhobs/obs-mcp) — Go, observability stack
- [microsoft/mcp-gateway](https://github.com/microsoft/mcp-gateway) — C#, MCP gateway/proxy

---

**Built for the "tuition principle" — pay tuition in spikes, not production.** 🎓