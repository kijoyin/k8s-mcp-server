# Hermes Agent Integration

This guide shows how to connect Hermes Agent to the K8s MCP Server for automated cluster monitoring.

## Prerequisites

- Hermes Agent installed and configured
- K8s MCP Server deployed and accessible via HTTP
- Telegram bot token and chat ID (for notifications)

## 1. MCP Client Skill for Hermes

Create a skill at `~/.hermes/skills/mcp-client/SKILL.md`:

```markdown
---
name: mcp-client
description: Call MCP servers over HTTP from Hermes agents
version: 1.0.0
---

## Tools

### `mcp_call(server_url, tool_name, arguments)`

Calls an MCP tool via HTTP transport (MCP 2025-06-18 spec).

**Parameters:**
- `server_url` — Base URL (e.g., `https://mcp.yourdomain.com` or `http://k8s-mcp-server.monitoring:80`)
- `tool_name` — Exact tool name (e.g., `cluster_health`, `check_oom_kills`)
- `arguments` — JSON object matching tool schema

**Returns:** Tool result as JSON.

## Configuration

Set environment variables or add to Hermes config:

```yaml
# ~/.hermes/config.yaml
mcp:
  k8s_server_url: "https://mcp.yourdomain.com"  # or internal K8s service URL
  api_key: "${MCP_API_KEY}"  # optional, if server requires auth
```
```

## 2. Python Script for Hermes

Save as `~/.hermes/skills/mcp-client/scripts/mcp_client.py`:

```python
#!/usr/bin/env python3
"""
MCP HTTP Client for Hermes Agent
Implements MCP 2025-06-18 stateless HTTP transport
"""

import asyncio
import json
import os
import httpx
from typing import Any, Dict, Optional


class McpHttpClient:
    """Minimal MCP client for HTTP transport."""
    
    def __init__(self, base_url: str, api_key: Optional[str] = None, timeout: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.timeout = timeout
        self._session_id: Optional[str] = None
        self._client: Optional[httpx.AsyncClient] = None
    
    async def __aenter__(self):
        self._client = httpx.AsyncClient(timeout=self.timeout)
        await self._initialize()
        return self
    
    async def __aexit__(self, *args):
        if self._client:
            await self._client.aclose()
    
    async def _initialize(self):
        """Initialize MCP session."""
        if not self._client:
            raise RuntimeError("Client not initialized")
        
        response = await self._client.post(
            f"{self.base_url}/mcp",
            json={
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {}
                }
            },
            headers=self._headers()
        )
        response.raise_for_status()
        
        # Extract session ID from response headers
        self._session_id = response.headers.get("mcp-session-id")
    
    def _headers(self) -> Dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if self.api_key:
            headers["X-MCP-API-Key"] = self.api_key
        if self._session_id:
            headers["Mcp-Session-Id"] = self._session_id
        return headers
    
    async def call_tool(self, tool_name: str, arguments: Dict[str, Any]) -> Any:
        """Call an MCP tool."""
        if not self._client:
            raise RuntimeError("Client not initialized. Use async context manager.")
        
        response = await self._client.post(
            f"{self.base_url}/mcp",
            json={
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {
                    "name": tool_name,
                    "arguments": arguments
                }
            },
            headers=self._headers()
        )
        response.raise_for_status()
        
        result = response.json()
        if "error" in result:
            raise RuntimeError(f"MCP error: {result['error']}")
        
        return result.get("result", {})


# Convenience function for Hermes skill system
async def mcp_call(server_url: str, tool_name: str, arguments: Dict[str, Any], api_key: Optional[str] = None) -> Any:
    """Call an MCP tool - simple one-shot interface."""
    async with McpHttpClient(server_url, api_key) as client:
        return await client.call_tool(tool_name, arguments)


# CLI for testing
if __name__ == "__main__":
    import sys
    
    if len(sys.argv) < 4:
        print("Usage: mcp_client.py <server_url> <tool_name> <arguments_json> [api_key]")
        sys.exit(1)
    
    server_url = sys.argv[1]
    tool_name = sys.argv[2]
    arguments = json.loads(sys.argv[3])
    api_key = sys.argv[4] if len(sys.argv) > 4 else None
    
    async def main():
        result = await mcp_call(server_url, tool_name, arguments, api_key)
        print(json.dumps(result, indent=2))
    
    asyncio.run(main())
```

## 3. Hermes Cron Job for Daily Briefing

Create `~/.hermes/cron/k8s-daily-briefing.yaml`:

```yaml
schedule: "0 8 * * *"  # Daily at 08:00
name: "K8s Daily Briefing"
prompt: |
  You are my Kubernetes cluster operator. Use the K8s MCP server to gather a morning health report.
  
  Server URL: {{ env.MCP_K8S_SERVER_URL }}
  API Key: {{ env.MCP_API_KEY }}
  
  Execute these checks in order:
  
  1. **Cluster Overview** - Call `cluster_health`
     - Report: nodes total/ready, pods total/running/failed, namespaces count
     - Flag any nodes not ready
  
  2. **Disk Pressure** - Call `check_disk_pressure` (threshold: 0.15)
     - Flag any nodes with < 15% disk available
  
  3. **OOM Kills** - Call `check_oom_kills` (range: "1h")
     - Report any OOM kills in last hour
  
  4. **Pod Restarts** - Call `check_pod_restarts` (range: "1h", threshold: 3)
     - Flag pods with > 3 restarts in last hour
  
  5. **Error Logs** - Call `query_error_logs` (range: "6h", limit: 20)
     - Summarize top error patterns
  
  Format as a Telegram-ready message:
  - Use emoji status indicators: 🟢 Healthy, 🟡 Warning, 🔴 Critical
  - One-line summary per check
  - Actionable next steps where issues found
  - Keep under 4000 characters (Telegram limit)
  
  Example format:
  ```
  🟢 K8s Daily Briefing - 2026-01-15 08:00
  
  📊 Cluster: 3 nodes (3 ready), 47 pods (45 running, 0 failed), 8 namespaces
  💾 Disk: All nodes > 15% free
  💥 OOM: 0 kills in last hour
  🔄 Restarts: 0 pods > 3 restarts
  📝 Errors: 3 error patterns (top: "connection refused" in ingress-nginx)
  
  ✅ All systems nominal
  ```

skills: ["mcp-client"]
deliver: "telegram:<YOUR_CHAT_ID>"
```

## 4. Environment Variables

Add to your Hermes environment or `.env`:

```bash
# ~/.hermes/.env
MCP_K8S_SERVER_URL=https://mcp.yourdomain.com
MCP_API_KEY=your-api-key-if-required
TELEGRAM_BOT_TOKEN=your-bot-token
TELEGRAM_CHAT_ID=your-chat-id
```

## 5. Testing the Integration

Test the MCP client directly:

```bash
# Install dependencies
pip install httpx

# Test cluster health
python ~/.hermes/skills/mcp-client/scripts/mcp_client.py \
  "https://mcp.yourdomain.com" \
  "cluster_health" \
  "{}" \
  "your-api-key"
```

Test via Hermes cron:

```bash
# Run the cron job manually
hermes cron run k8s-daily-briefing
```

## 6. Available Tools Reference

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `cluster_health` | Overall cluster summary | None |
| `check_disk_pressure` | Nodes with low disk | `threshold` (default 0.15) |
| `check_oom_kills` | OOM kills in time range | `range` (default "1h") |
| `check_pod_restarts` | Frequently restarting pods | `range` (default "1h"), `threshold` (default 3) |
| `query_metrics` | Arbitrary PromQL query | `query`, `range?`, `step` (default "1m") |
| `query_error_logs` | Error logs from Loki | `query?`, `range` (default "1h"), `limit` (default 50) |

## 7. Troubleshooting

| Issue | Solution |
|-------|----------|
| `401 Unauthorized` | Check `MCP_API_KEY` matches server config |
| `Connection refused` | Verify server URL, check Cloudflare Tunnel / K8s Service |
| `Timeout` | Increase `timeout` in client, check Prometheus/Loki responsiveness |
| `No session ID` | Server may not support session affinity; try stateless calls |

## 8. Extending for Your Needs

Add custom tools to the MCP server (see `src/K8sMcpServer/Tools/`) and they'll automatically be available via `tools/list` and callable via this client.