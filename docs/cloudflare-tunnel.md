# Cloudflare Tunnel Setup for MCP Server

This guide configures Cloudflare Tunnel to expose your K8s MCP Server securely to the internet for Hermes Agent access.

## Prerequisites

- Cloudflare account with a domain
- `cloudflared` installed locally
- Kubernetes cluster with `kubectl` access
- MCP Server deployed in cluster (see `deploy/k8s/`)

## 1. Install cloudflared

### Windows:
```powershell
# Via winget
winget install --id Cloudflare.cloudflared

# Or download from https://github.com/cloudflare/cloudflared/releases
```

### Linux/macOS:
```bash
# Debian/Ubuntu
curl -L --output cloudflared.deb https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64.deb
sudo dpkg -i cloudflared.deb

# macOS
brew install cloudflared
```

## 2. Authenticate with Cloudflare

```bash
cloudflared tunnel login
```

This opens a browser — select your domain and authorize.

## 3. Create a Tunnel

```bash
cloudflared tunnel create k8s-mcp-server
```

Output example:
```
Tunnel credentials written to /home/user/.cloudflared/abc123-def456.json
Tunnel ID: abc123-def456-ghi789
```

Save the **Tunnel ID** and **credentials file path**.

## 4. Configure Tunnel Routing

Create `~/.cloudflared/config.yml`:

```yaml
tunnel: abc123-def456-ghi789
credentials-file: /home/user/.cloudflared/abc123-def456.json

ingress:
  # MCP Server - HTTPS with mTLS
  - hostname: mcp.yourdomain.com
    service: https://k8s-mcp-server.monitoring.svc.cluster.local:80
    originRequest:
      noTLSVerify: true  # for self-signed certs in cluster
      connectTimeout: 30s
      tlsTimeout: 10s
      httpHostHeader: mcp.yourdomain.com

  # Optional: Prometheus (protect with auth!)
  - hostname: prometheus.yourdomain.com
    service: http://prometheus.monitoring.svc.cluster.local:9090
    originRequest:
      noTLSVerify: true

  # Optional: Grafana
  - hostname: grafana.yourdomain.com
    service: http://grafana.monitoring.svc.cluster.local:3000
    originRequest:
      noTLSVerify: true

  # Catch-all: return 404
  - service: http_status:404
```

## 5. Run Tunnel Locally (Development)

```bash
cloudflared tunnel run k8s-mcp-server
```

Test: `curl https://mcp.yourdomain.com/health`

## 6. Deploy Tunnel in Kubernetes (Production)

### Create Kubernetes Secret with Credentials

```bash
# Create secret from credentials file
kubectl create secret generic cloudflared-credentials \
  --from-file=credentials.json=/home/user/.cloudflared/abc123-def456.json \
  -n monitoring
```

### Cloudflared Deployment

```yaml
# deploy/k8s/cloudflared.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cloudflared
  namespace: monitoring
  labels:
    app: cloudflared
spec:
  replicas: 2
  selector:
    matchLabels:
      app: cloudflared
  template:
    metadata:
      labels:
        app: cloudflared
    spec:
      containers:
      - name: cloudflared
        image: cloudflare/cloudflared:2024.12.0
        args:
        - tunnel
        - run
        - --config
        - /etc/cloudflared/config.yaml
        - --credentials-file
        - /etc/cloudflared/credentials/credentials.json
        volumeMounts:
        - name: config
          mountPath: /etc/cloudflared
          readOnly: true
        - name: credentials
          mountPath: /etc/cloudflared/credentials
          readOnly: true
        resources:
          requests:
            cpu: "50m"
            memory: "64Mi"
          limits:
            cpu: "200m"
            memory: "128Mi"
      volumes:
      - name: config
        configMap:
          name: cloudflared-config
      - name: credentials
        secret:
          secretName: cloudflared-credentials
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: cloudflared-config
  namespace: monitoring
data:
  config.yaml: |
    tunnel: abc123-def456-ghi789
    credentials-file: /etc/cloudflared/credentials/credentials.json
    
    ingress:
      - hostname: mcp.yourdomain.com
        service: http://k8s-mcp-server.monitoring.svc.cluster.local:80
        originRequest:
          noTLSVerify: true
          connectTimeout: 30s
      - service: http_status:404
```

Apply:
```bash
kubectl apply -f deploy/k8s/cloudflared.yaml
```

## 7. DNS Configuration

### Option A: Cloudflare Dashboard
1. Go to DNS → Records
2. Add CNAME: `mcp` → `abc123-def456-ghi789.cfargotunnel.com`
3. Proxy status: **Proxied** (orange cloud)

### Option B: cloudflared CLI
```bash
cloudflared tunnel route dns k8s-mcp-server mcp.yourdomain.com
```

## 8. Access Control (Zero Trust)

### Add Application in Cloudflare Zero Trust

1. Go to **Zero Trust** → **Applications** → **Add Application**
2. Select **Self-hosted**
3. Configure:
   - **Application Domain**: `mcp.yourdomain.com`
   - **Identity Providers**: Choose (GitHub, Google, OIDC, etc.)
   - **Policies**: 
     - Require valid email from your domain
     - Or: Service tokens for machine-to-machine

### Service Tokens for Hermes

1. In Zero Trust: **Service Tokens** → **Create**
2. Name: `hermes-mcp-client`
3. Copy **Token ID** and **Token Secret**
4. Use in Hermes MCP client as headers:
   ```python
   headers = {
       "CF-Access-Client-Id": "TOKEN_ID",
       "CF-Access-Client-Secret": "TOKEN_SECRET"
   }
   ```

## 9. mTLS for MCP Server (Optional but Recommended)

### Generate Certificates

```bash
# Using cloudflared's built-in cert generation
cloudflared tunnel cert mcp.yourdomain.com --out /tmp/certs
```

### Mount in MCP Server Deployment

```yaml
# Add to deployment.yaml
spec:
  template:
    spec:
      volumes:
      - name: tls-certs
        secret:
          secretName: mcp-tls-certs
      containers:
      - name: k8s-mcp-server
        volumeMounts:
        - name: tls-certs
          mountPath: /etc/tls
          readOnly: true
        env:
        - name: ASPNETCORE_Kestrel__Certificates__Default__Path
          value: "/etc/tls/cert.pem"
        - name: ASPNETCORE_Kestrel__Certificates__Default__KeyPath
          value: "/etc/tls/key.pem"
```

## 10. Testing End-to-End

```bash
# Test health endpoint
curl -H "CF-Access-Client-Id: $TOKEN_ID" \
     -H "CF-Access-Client-Secret: $TOKEN_SECRET" \
     https://mcp.yourdomain.com/health

# Test MCP initialize
curl -X POST https://mcp.yourdomain.com/mcp \
  -H "Content-Type: application/json" \
  -H "CF-Access-Client-Id: $TOKEN_ID" \
  -H "CF-Access-Client-Secret: $TOKEN_SECRET" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{}}}'

# Test tool call
curl -X POST https://mcp.yourdomain.com/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id-from-above>" \
  -H "CF-Access-Client-Id: $TOKEN_ID" \
  -H "CF-Access-Client-Secret: $TOKEN_SECRET" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"cluster_health","arguments":{}}}'
```

## 11. Monitoring the Tunnel

### Cloudflare Dashboard
- **Zero Trust** → **Networks** → **Tunnels** → Your tunnel
- Shows: connections, latency, errors, bandwidth

### Prometheus Metrics (if enabled)
```yaml
# Add to cloudflared config
metrics: 0.0.0.0:2000
```

Then scrape from Prometheus.

## 12. Troubleshooting

| Issue | Solution |
|-------|----------|
| `502 Bad Gateway` | Check service name/port in config.yml; verify K8s Service exists |
| `403 Forbidden` | Zero Trust policy blocking; check service token headers |
| `Connection timeout` | Increase `connectTimeout` in originRequest; check network policies |
| `Certificate verify failed` | Set `noTLSVerify: true` for self-signed certs |
| Tunnel not starting | Check credentials file path; verify tunnel ID matches |

## 13. Alternative: Cloudflare Tunnel Operator

For GitOps-native management, use the [Cloudflare Tunnel Operator](https://github.com/cloudflare/cloudflare-tunnel-operator):

```yaml
apiVersion: cloudflare.com/v1alpha1
kind: Tunnel
metadata:
  name: k8s-mcp-server
  namespace: monitoring
spec:
  tunnelId: abc123-def456-ghi789
  credentialsSecret: cloudflared-credentials
  config:
    ingress:
      - hostname: mcp.yourdomain.com
        service: http://k8s-mcp-server.monitoring.svc.cluster.local:80
        originRequest:
          noTLSVerify: true
      - service: http_status:404
```

## 14. Cost Considerations

- **Cloudflare Tunnel**: Free for all plans
- **Zero Trust Access**: Free for up to 50 users
- **Argo Smart Routing**: Paid (Pro+) — optional, improves latency
- **Bandwidth**: Included in Cloudflare plan limits

---

**Next Steps**: 
1. Deploy tunnel in cluster
2. Configure Zero Trust policies
3. Update Hermes MCP client with service token headers
4. Test daily briefing cron job