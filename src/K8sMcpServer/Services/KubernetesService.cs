using k8s;
using k8s.Models;
using K8sMcpServer.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace K8sMcpServer.Services;

public sealed class KubernetesService : IKubernetesService
{
    private readonly Kubernetes _client;
    private readonly ILogger<KubernetesService> _logger;
    private readonly KubernetesOptions _options;

    public KubernetesService(IOptions<KubernetesOptions> options, ILogger<KubernetesService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.UseInClusterConfig)
        {
            _client = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        }
        else if (!string.IsNullOrEmpty(_options.KubeConfigPath))
        {
            _client = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(_options.KubeConfigPath));
        }
        else
        {
            _client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        }
    }

    public async Task<ClusterHealthResult> GetClusterHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var nodes = await _client.ListNodeAsync(cancellationToken: ct);
            var pods = await _client.ListPodForAllNamespacesAsync(cancellationToken: ct);

            var nodeStatuses = nodes.Items.Select(n => new NodeStatus
            {
                Name = n.Metadata.Name,
                Ready = n.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") ?? false,
                Version = n.Status?.NodeInfo?.KubeletVersion ?? "",
                OsImage = n.Status?.NodeInfo?.OsImage ?? "",
                Conditions = n.Status?.Conditions?
                    .ToDictionary(c => c.Type, c => c.Status ?? "") ?? new Dictionary<string, string>()
            }).ToList();

            return new ClusterHealthResult
            {
                NodesTotal = nodes.Items.Count,
                NodesReady = nodeStatuses.Count(n => n.Ready),
                PodsTotal = pods.Items.Count,
                PodsRunning = pods.Items.Count(p => p.Status?.Phase == "Running"),
                PodsFailed = pods.Items.Count(p => p.Status?.Phase == "Failed"),
                NamespacesCount = pods.Items.Select(p => p.Metadata.NamespaceProperty).Distinct().Count(),
                NodeStatuses = nodeStatuses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cluster health");
            throw;
        }
    }

    public async Task<NodeMetricsResult> GetNodeMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            // Metrics server API may not be available in all clusters
            // Return empty result with warning - metrics are better queried via Prometheus
            _logger.LogWarning("Node metrics via Kubernetes API not available; use Prometheus query_metrics tool instead");
            return new NodeMetricsResult { Nodes = [] };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get node metrics");
            return new NodeMetricsResult { Nodes = [] };
        }
    }

    public async Task<PodMetricsResult> GetPodMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            // Metrics server API may not be available in all clusters
            // Return empty result with warning - metrics are better queried via Prometheus
            _logger.LogWarning("Pod metrics via Kubernetes API not available; use Prometheus query_metrics tool instead");
            return new PodMetricsResult { Pods = [] };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get pod metrics");
            return new PodMetricsResult { Pods = [] };
        }
    }

    private static double ParseCpuQuantity(string? quantity)
    {
        if (string.IsNullOrEmpty(quantity)) return 0;
        
        // Kubernetes CPU: "500m" = 0.5 cores, "1" = 1 core
        if (quantity.EndsWith("m"))
        {
            return double.Parse(quantity[..^1]) / 1000.0;
        }
        return double.Parse(quantity);
    }

    private static double ParseMemoryQuantity(string? quantity)
    {
        if (string.IsNullOrEmpty(quantity)) return 0;
        
        // Kubernetes memory: "512Mi", "1Gi", "500M"
        var suffix = quantity.Length >= 2 ? quantity[^2..] : "";
        var value = double.Parse(quantity[..^suffix.Length]);
        
        return suffix.ToLower() switch
        {
            "ki" => value * 1024,
            "mi" => value * 1024 * 1024,
            "gi" => value * 1024 * 1024 * 1024,
            "ti" => value * 1024L * 1024 * 1024 * 1024,
            "k" => value * 1000,
            "m" => value * 1000 * 1000,
            "g" => value * 1000 * 1000 * 1000,
            _ => value
        };
    }
}