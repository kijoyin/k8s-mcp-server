---
title: "I Taught My AI Assistant to Operate My Kubernetes Cluster"
description: "Building a .NET 10 MCP server that exposes observability tools (not raw API wrappers), deploying it to MicroK8s via ArgoCD, and hooking it to a Hermes cron agent that briefs me on Telegram every morning."
date: 2026-08-02
tags: [dotnet, mcp, kubernetes, observability, ai-agents, home-lab, argocd]
---

> **TL;DR** — Most MCP servers for Kubernetes expose raw API wrappers (`list_pods`, `get_deployment`). I built one that exposes *observability checks* (`check_oom_kills`, `check_disk_pressure`, `check_pod_restarts`), deployed it to my 3-node MicroK8s HA cluster via Aspire + ArgoCD, and hooked it to a Hermes Agent cron job that sends me a Telegram briefing every 08:00. The repo is at [github.com/yourorg/k8s-mcp-server](https://github.com/yourorg/k8s-mcp-server).

---

## Why Another Kubernetes MCP Server?

Three existing projects dominate the space:

| Project | Language | Approach |
|---------|----------|----------|
| `containers/kubernetes-mcp-server` | Go | Full K8s API (100+ tools), CNCF-backed |
| `Flux159/mcp-server-kubernetes` | TypeScript | `kubectl`-style commands, NPM package |
| `prometheus/prometheus-mcp` | Go | PromQL queries only |

**All three miss what I needed:**

1. **Observability-first tools** — Not `list_pods` but `check_oom_kills`. The agent shouldn't explore; it should *alert*.
2. **.NET implementation** — Zero .NET MCP servers for K8s existed. I work in .NET/Azure daily; this is my stack.
3. **Agent-as-operator pattern** — Most docs show interactive chat. I wanted a *scheduled operator* (Hermes cron → MCP → Telegram).
4. **Home lab reality** — MicroK8s HA on ThinkCentre M710q's, Cloudflare Tunnel, ArgoCD GitOps. Not EKS/GKE/AKS.

---

## The Tools: Designed for Alerting, Not Exploration

```csharp
[McpServerToolType]
public sealed class CheckOomKillsTool
{
    [McpServerTool(Name = "check_oom_kills", 
        Description = "Check for OOM kills in the last hour")]
    public async Task<PrometheusQueryResult> CheckOomKillsAsync(
        string range = "1h", CancellationToken ct = default)
    {
        var query = $"increase(container_oom_events_total[{range}]) > 0";
        return await _prom.QueryAsync(query, ct);
    }
}
```

| Tool | Purpose | PromQL / LogQL |
|------|---------|----------------|
| `cluster_health` | Daily briefing header | K8s API: nodes, pods, namespaces |
| `check_disk_pressure` | Nodes <15% disk | `kubelet_volume_stats_available_bytes / capacity < 0.15` |
| `check_oom_kills` | OOM kills last hour | `increase(container_oom_events_total[1h]) > 0` |
| `check_pod_restarts` | Crash loops | `increase(kube_pod_container_status_restarts_total[1h]) > 3` |
| `query_metrics` | Arbitrary PromQL | Pass-through |
| `query_error_logs` | Loki error search | `{job=~".+"} \|= "ERROR" \|~ "(?i)(exception\|fail\|timeout\|refused\|denied\|unauthorized)"` |

**Key design choice:** Tools return *structured results* (not raw JSON). The agent gets typed data it can reason about.

---

## Architecture: .NET 10 + MCP 2025-06-18 + Aspire + ArgoCD

```
┌─────────────────┐     MCP/HTTP      ┌──────────────────┐
│  Hermes Agent   │ ◄────────────────► │ K8s MCP Server   │
│  (cron 08:00)   │  tools/call       │  (.NET 10, AOT)  │
└────────┬────────┘                   └────────┬─────────┘
         │                                     │
         ▼                                     ▼
┌─────────────────┐                   ┌──────────────────┐
│  Telegram Bot   │                   │  3-node MicroK8s │
│  (my chat)      │                   │  + Prometheus    │
└─────────────────┘                   │  + Loki          │
                                      └──────────────────┘
```

**Stack decisions:**

| Layer | Choice | Why |
|-------|--------|-----|
| Language | .NET 10 (preview) | AOT, `WithToolsFromAssembly()`, my daily driver |
| MCP SDK | `ModelContextProtocol.AspNetCore` 0.2.0-preview | Stateless HTTP transport, v2025-06-18 spec |
| K8s client | `KubernetesClient` 13.x | Official, supports in-cluster + kubeconfig |
| Metrics | `Prometheus.Client` + HTTP API | No metrics-server dependency |
| Logs | Loki HTTP API | No Fluent Bit / agent needed |
| Local dev | Aspire 9.2 | Spins Prometheus + Loki + MCP in one command |
| Deploy | ArgoCD + Kustomize | GitOps, auto-sync from `main` |
| Ingress | Cloudflare Tunnel + Zero Trust | HTTPS, mTLS, service tokens for Hermes |

---

## The "Tuition Paid" Moments

### 1. MCP Session Handling (Stateless HTTP ≠ Stateless Client)

```csharp
// Initialize once per session
var init = await _http.PostAsJsonAsync("/mcp", new { 
    jsonrpc = "2.0", id = 1, method = "initialize", ... });
var sessionId = init.Headers.GetValues("Mcp-Session-Id").FirstOrDefault();

// Pass on every subsequent call
headers["Mcp-Session-Id"] = sessionId;
```

**Lesson:** The spec says "stateless HTTP" but sessions exist for progress notifications. Cache the session ID per agent run.

### 2. AOT + `WithToolsFromAssembly()` = Reflection Hell

```csharp
// Program.cs - this breaks AOT
builder.Services.AddMcpServer().WithToolsFromAssembly();

// Fix: use source-generated tools (TODO) or accept trimming warnings
#pragma warning disable IL2026, IL3050
builder.Services.AddMcpServer().WithToolsFromAssembly();
#pragma warning restore IL2026, IL3050
```

**Lesson:** Native AOT and dynamic tool discovery fight each other. For now, suppress warnings; long-term, write a source generator.

### 3. Cloudflare Tunnel WebSocket Timeouts

```yaml
# cloudflared config.yml
ingress:
  - hostname: mcp.yourdomain.com
    service: http://k8s-mcp-server.monitoring.svc.cluster.local:80
    originRequest:
      noTLSVerify: true
      connectTimeout: 30s      # default 10s too short for cold start
      tlsTimeout: 10s
```

**Lesson:** MCP over HTTP keeps connections alive. Tunnel defaults kill idle connections. Bump timeouts.

### 4. Loki Label Cardinality

```logql
# Bad: high cardinality
{namespace=~".+", pod=~".+"} |= "ERROR"

# Good: bounded
{job=~".+"} |= "ERROR" |~ "(?i)(exception|fail|timeout)"
```

**Lesson:** Loki chokes on unbounded label matchers. Use `job` + line filters.

---

## Hermes Integration: The Operator Pattern

### MCP Client Skill (`~/.hermes/skills/mcp-client/scripts/mcp_client.py`)

```python
async def mcp_call(server_url: str, tool_name: str, arguments: dict, api_key: str = None):
    async with httpx.AsyncClient(timeout=30) as client:
        # 1. Initialize
        init = await client.post(f"{server_url}/mcp", json={...})
        session_id = init.headers.get("mcp-session-id")
        
        # 2. Call tool
        headers = {"Mcp-Session-Id": session_id} if session_id else {}
        if api_key: headers["X-MCP-API-Key"] = api_key
        
        resp = await client.post(f"{server_url}/mcp", json={
            "jsonrpc": "2.0", "id": 2, "method": "tools/call",
            "params": {"name": tool_name, "arguments": arguments}
        }, headers=headers)
        return resp.json()["result"]
```

### Daily Briefing Cron (`~/.hermes/cron/k8s-daily-briefing.yaml`)

```yaml
schedule: "0 8 * * *"
name: "K8s Daily Briefing"
prompt: |
  You are my cluster operator. Use the K8s MCP server ({{ env.MCP_K8S_SERVER_URL }}) to:
  
  1. Call `cluster_health` — nodes total/ready, pods running/failed, namespaces
  2. Call `check_disk_pressure` (threshold: 0.15)
  3. Call `check_oom_kills` (range: "1h")
  4. Call `check_pod_restarts` (range: "1h", threshold: 3)
  5. Call `query_error_logs` (range: "6h", limit: 20)
  
  Format as Telegram MarkdownV2:
  - 🟢/🟡/🔴 per check
  - One-line summary + actionable next step
  - Under 4000 chars

skills: ["mcp-client"]
deliver: "telegram:<YOUR_CHAT_ID>"
```

### Sample Output

```
🟢 K8s Daily Briefing — 2026-08-02 08:00

📊 Cluster: 3 nodes (3 ready), 47 pods (45 running, 0 failed), 8 namespaces
💾 Disk: All nodes >15% free (lowest: node-2 at 23%)
💥 OOM: 0 kills in last hour
🔄 Restarts: 0 pods >3 restarts
📝 Errors: 2 patterns — "connection refused" (ingress-nginx, 3×), "context deadline exceeded" (cert-manager, 1×)

✅ All systems nominal
```

---

## Deploy to Your Cluster

```bash
# 1. Build & push (GitHub Actions does this on push to main)
docker build -f deploy/Dockerfile -t ghcr.io/yourorg/k8s-mcp-server:latest .
docker push ghcr.io/yourorg/k8s-mcp-server:latest

# 2. Apply manifests (or let ArgoCD auto-sync)
kubectl apply -f deploy/k8s/

# 3. Verify
kubectl -n monitoring port-forward svc/k8s-mcp-server 8080:80
curl http://localhost:8080/health
# {"status":"healthy","timestamp":"2026-08-02T08:00:00Z"}
```

**ArgoCD Application** auto-syncs from `deploy/k8s/` on every push to `main`. Zero manual steps.

---

## What's Next

| Area | Plan |
|------|------|
| **Source generator** | Replace `WithToolsFromAssembly()` for true AOT |
| **Tool expansion** | `check_cert_expiry`, `check_hpa_saturation`, `get_recent_events` |
| **Multi-cluster** | Single MCP server → multiple kubeconfigs via header |
| **Eval harness** | Testcontainers-based contract tests for each tool |
| **Dashboard** | Aspire + Grafana template in repo |

---

## Repo & Resources

| Artifact | Link |
|----------|------|
| **Source** | [github.com/yourorg/k8s-mcp-server](https://github.com/yourorg/k8s-mcp-server) |
| **Docker** | `ghcr.io/yourorg/k8s-mcp-server:latest` |
| **Hermes MCP Skill** | `docs/hermes-integration.md` |
| **Telegram Setup** | `docs/telegram-bot.md` |
| **Cloudflare Tunnel** | `docs/cloudflare-tunnel.md` |

---

## Closing Thought

> **The best monitoring dashboard is the one that messages you.**

Dashboards are write-only. This stack — MCP server + scheduled agent + Telegram — turns observability into *operations*. The agent runs the checks I'd run manually at 08:00, formats the answer, and delivers it where I already look.

**Tuition principle:** I paid the tuition (WebSocket timeouts, AOT trimming, label cardinality) in my home lab. You get the pattern catalog.

---

*Questions? Issues? PRs welcome at [github.com/yourorg/k8s-mcp-server](https://github.com/yourorg/k8s-mcp-server).*