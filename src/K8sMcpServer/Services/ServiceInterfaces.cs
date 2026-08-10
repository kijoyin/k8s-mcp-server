using k8s.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace K8sMcpServer.Services;

public interface IKubernetesService
{
    Task<ClusterHealthResult> GetClusterHealthAsync(CancellationToken ct = default);
    Task<NodeMetricsResult> GetNodeMetricsAsync(CancellationToken ct = default);
    Task<PodMetricsResult> GetPodMetricsAsync(CancellationToken ct = default);
}

public interface IPrometheusService
{
    Task<PrometheusQueryResult> QueryAsync(string query, CancellationToken ct = default);
    Task<PrometheusQueryResult> QueryRangeAsync(string query, string range, string step, CancellationToken ct = default);
}

public interface ILokiService
{
    Task<LokiQueryResult> QueryRangeAsync(string query, string range, int limit, CancellationToken ct = default);
}

// Result types
public sealed class ClusterHealthResult
{
    public int NodesTotal { get; set; }
    public int NodesReady { get; set; }
    public int PodsTotal { get; set; }
    public int PodsRunning { get; set; }
    public int PodsFailed { get; set; }
    public int NamespacesCount { get; set; }
    public IReadOnlyList<NodeStatus> NodeStatuses { get; set; } = [];
}

public sealed class NodeStatus
{
    public string Name { get; set; } = "";
    public bool Ready { get; set; }
    public string Version { get; set; } = "";
    public string OsImage { get; set; } = "";
    public IReadOnlyDictionary<string, string> Conditions { get; set; } = new Dictionary<string, string>();
}

public sealed class NodeMetricsResult
{
    public IReadOnlyList<NodeMetric> Nodes { get; set; } = [];
}

public sealed class NodeMetric
{
    public string Node { get; set; } = "";
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
}

public sealed class PodMetricsResult
{
    public IReadOnlyList<PodMetric> Pods { get; set; } = [];
}

public sealed class PodMetric
{
    public string Namespace { get; set; } = "";
    public string Pod { get; set; } = "";
    public int RestartCount { get; set; }
    public double CpuUsageCores { get; set; }
    public double MemoryUsageBytes { get; set; }
}

public sealed class PrometheusQueryResult
{
    public string Status { get; set; } = "";
    public PrometheusData Data { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class PrometheusData
{
    public string ResultType { get; set; } = "";
    public List<PrometheusResult> Result { get; set; } = [];
}

public sealed class PrometheusResult
{
    public Dictionary<string, string> Metric { get; set; } = [];
    public object? Value { get; set; }
    public List<object?> Values { get; set; } = [];
}

public sealed class LokiQueryResult
{
    public string Status { get; set; } = "";
    public LokiData Data { get; set; } = new();
}

public sealed class LokiData
{
    public string ResultType { get; set; } = "";
    public List<LokiStream> Result { get; set; } = [];
}

public sealed class LokiStream
{
    public Dictionary<string, string> Stream { get; set; } = [];
    public List<LokiValue> Values { get; set; } = [];
}

public sealed class LokiValue
{
    public string Timestamp { get; set; } = "";
    public string Line { get; set; } = "";
}