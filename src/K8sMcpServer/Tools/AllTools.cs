using K8sMcpServer.Services;
using ModelContextProtocol.Server;

namespace K8sMcpServer.Tools;

[McpServerToolType]
public sealed class ClusterHealthTool
{
    private readonly IKubernetesService _k8s;
    private readonly ILogger<ClusterHealthTool> _logger;

    public ClusterHealthTool(IKubernetesService k8s, ILogger<ClusterHealthTool> logger)
    {
        _k8s = k8s;
        _logger = logger;
    }

    [McpServerTool(Name = "cluster_health")]
    public async Task<ClusterHealthResult> GetClusterHealthAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Getting cluster health");
        var result = await _k8s.GetClusterHealthAsync(ct);

        _logger.LogInformation("Cluster health: {NodesTotal} nodes ({NodesReady} ready), {PodsTotal} pods ({PodsRunning} running, {PodsFailed} failed), {NamespacesCount} namespaces",
            result.NodesTotal, result.NodesReady, result.PodsTotal, result.PodsRunning, result.PodsFailed, result.NamespacesCount);

        return result;
    }
}

[McpServerToolType]
public sealed class CheckDiskPressureTool
{
    private readonly IPrometheusService _prom;
    private readonly ILogger<CheckDiskPressureTool> _logger;

    public CheckDiskPressureTool(IPrometheusService prom, ILogger<CheckDiskPressureTool> logger)
    {
        _prom = prom;
        _logger = logger;
    }

    [McpServerTool(Name = "check_disk_pressure")]
    public async Task<PrometheusQueryResult> CheckDiskPressureAsync(
        double threshold = 0.15,
        CancellationToken ct = default)
    {
        var query = $"kubelet_volume_stats_available_bytes / kubelet_volume_stats_capacity_bytes < {threshold}";
        _logger.LogInformation("Checking disk pressure with query: {Query}", query);

        return await _prom.QueryAsync(query, ct);
    }
}

[McpServerToolType]
public sealed class CheckOomKillsTool
{
    private readonly IPrometheusService _prom;
    private readonly ILogger<CheckOomKillsTool> _logger;

    public CheckOomKillsTool(IPrometheusService prom, ILogger<CheckOomKillsTool> logger)
    {
        _prom = prom;
        _logger = logger;
    }

    [McpServerTool(Name = "check_oom_kills")]
    public async Task<PrometheusQueryResult> CheckOomKillsAsync(
        string range = "1h",
        CancellationToken ct = default)
    {
        var query = $"increase(container_oom_events_total[{range}]) > 0";
        _logger.LogInformation("Checking OOM kills with query: {Query}", query);

        return await _prom.QueryAsync(query, ct);
    }
}

[McpServerToolType]
public sealed class CheckPodRestartsTool
{
    private readonly IPrometheusService _prom;
    private readonly ILogger<CheckPodRestartsTool> _logger;

    public CheckPodRestartsTool(IPrometheusService prom, ILogger<CheckPodRestartsTool> logger)
    {
        _prom = prom;
        _logger = logger;
    }

    [McpServerTool(Name = "check_pod_restarts")]
    public async Task<PrometheusQueryResult> CheckPodRestartsAsync(
        string range = "1h",
        int threshold = 3,
        CancellationToken ct = default)
    {
        var query = $"increase(kube_pod_container_status_restarts_total[{range}]) > {threshold}";
        _logger.LogInformation("Checking pod restarts with query: {Query}", query);

        return await _prom.QueryAsync(query, ct);
    }
}

[McpServerToolType]
public sealed class QueryMetricsTool
{
    private readonly IPrometheusService _prom;
    private readonly ILogger<QueryMetricsTool> _logger;

    public QueryMetricsTool(IPrometheusService prom, ILogger<QueryMetricsTool> logger)
    {
        _prom = prom;
        _logger = logger;
    }

    [McpServerTool(Name = "query_metrics")]
    public async Task<PrometheusQueryResult> QueryMetricsAsync(
        string query,
        string? range = null,
        string step = "1m",
        CancellationToken ct = default)
    {
        _logger.LogInformation("Executing PromQL query: {Query} (range: {Range}, step: {Step})", query, range, step);

        if (!string.IsNullOrEmpty(range))
        {
            return await _prom.QueryRangeAsync(query, range, step, ct);
        }

        return await _prom.QueryAsync(query, ct);
    }
}

[McpServerToolType]
public sealed class QueryErrorLogsTool
{
    private readonly ILokiService _loki;
    private readonly ILogger<QueryErrorLogsTool> _logger;

    public QueryErrorLogsTool(ILokiService loki, ILogger<QueryErrorLogsTool> logger)
    {
        _loki = loki;
        _logger = logger;
    }

    [McpServerTool(Name = "query_error_logs")]
    public async Task<LokiQueryResult> QueryErrorLogsAsync(
        string? query = null,
        string range = "1h",
        int limit = 50,
        CancellationToken ct = default)
    {
        var defaultQuery = "{job=~\".+\"} |= \"ERROR\" |~ \"(?i)(exception|fail|timeout|refused|denied|unauthorized)\"";
        var finalQuery = query ?? defaultQuery;

        _logger.LogInformation("Querying Loki for error logs: {Query} (range: {Range}, limit: {Limit})", finalQuery, range, limit);

        return await _loki.QueryRangeAsync(finalQuery, range, limit, ct);
    }
}