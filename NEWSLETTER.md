---
title: "Weekly Dispatch: MCP Server for K8s Observability"
description: "What I built, what broke, and the pattern you can steal."
---

## 🎯 This Week: MCP Server for Kubernetes Observability

**Repo:** [github.com/yourorg/k8s-mcp-server](https://github.com/yourorg/k8s-mcp-server)

### What I Built

A .NET 10 Model Context Protocol server that exposes **observability checks** (not raw K8s API wrappers) for AI agents:

| Tool | What It Does |
|------|--------------|
| `cluster_health` | Nodes, pods, namespaces summary |
| `check_disk_pressure` | Nodes <15% disk |
| `check_oom_kills` | OOM kills last hour |
| `check_pod_restarts` | Crash loops (>3 restarts/hr) |
| `query_metrics` | Arbitrary PromQL |
| `query_error_logs` | Loki error search |

### The Operator Pattern

```
Hermes Cron (08:00) → MCP Server (K8s + Prometheus + Loki) → Telegram
```

The agent runs the checks I'd run manually, formats the answer, delivers it where I already look.

### Tuition Paid (What Broke)

1. **MCP sessions** — Stateless HTTP still needs session IDs for progress notifications
2. **AOT + `WithToolsFromAssembly()`** — Reflection kills native AOT; suppress warnings for now
3. **Cloudflare Tunnel timeouts** — Default 10s kills cold starts; bump to 30s
4. **Loki label cardinality** — `{namespace=~".+"}` chokes Loki; use `job` + line filters

### Deploy Stack

- **Local:** `dotnet run --project src/K8sMcpServer.Aspire` (spins Prometheus + Loki + MCP)
- **CI/CD:** GitHub Actions → GHCR → ArgoCD auto-sync from `main`
- **Ingress:** Cloudflare Tunnel + Zero Trust (service tokens for Hermes)

### Steal This Pattern

```yaml
# Hermes cron job
schedule: "0 8 * * *"
prompt: |
  You are my cluster operator. Call these 5 tools, format as Telegram.
skills: ["mcp-client"]
deliver: "telegram:<CHAT_ID>"
```

---

## 📚 Reading List

- [MCP 2025-06-18 spec](https://modelcontextprotocol.io/specification/2025-06-18) — stateless HTTP transport
- [Aspire 9.2 docs](https://learn.microsoft.com/dotnet/aspire) — container orchestration for local dev
- [Cloudflare Tunnel + K8s](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) — ingress without public IPs

---

## 🛠 Try It

```bash
# Local dev (needs Docker)
git clone https://github.com/yourorg/k8s-mcp-server
cd k8s-mcp-server
dotnet run --project src/K8sMcpServer.Aspire
# MCP at http://localhost:8080, Prometheus at :9090, Loki at :3100
```

---

*Reply to this email — I read every response. What observability checks would you add?*