using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// Pure C#: export and aggregation can be verified without starting Unity or Python.
public sealed class ExperimentCsv : IDisposable
{
    private static readonly string[] LatencyNames = {
        "scasp_fact_latency_ms", "scasp_action_round_trip_latency_ms", "scasp_action_query_latency_ms",
        "behavior_tree_fact_latency_ms", "behavior_tree_action_latency_ms"
    };
    private static readonly Func<ExperimentMetrics, double?>[] Latencies = {
        m => m.ScaspFactLatencyMs, m => m.ScaspActionRoundTripLatencyMs, m => m.ScaspActionQueryLatencyMs,
        m => m.BehaviorTreeFactLatencyMs, m => m.BehaviorTreeActionLatencyMs
    };
    private static readonly string[] PopulationNames = {
        "scasp_innocents", "scasp_half_zombies", "behavior_tree_innocents", "behavior_tree_half_zombies"
    };
    private static readonly Func<ExperimentMetrics, ExperimentPopulation>[] Populations = {
        m => m.ScaspInnocents, m => m.ScaspHalfZombies, m => m.BehaviorTreeInnocents, m => m.BehaviorTreeHalfZombies
    };

    private readonly string batchId, backend, policy;
    private readonly int requestedGames;
    private readonly StreamWriter games;
    private readonly List<(int Configuration, ExperimentMetrics Metrics)> runs = new List<(int, ExperimentMetrics)>();
    public string GamesPath { get; }
    public string SummaryPath { get; }

    public ExperimentCsv(string directory, string batchId, int requestedGames, string backend, string policy)
    {
        if (requestedGames <= 0 || requestedGames > int.MaxValue / 2)
            throw new ArgumentOutOfRangeException(nameof(requestedGames));
        this.batchId = batchId;
        this.backend = backend;
        this.policy = policy;
        this.requestedGames = requestedGames;
        Directory.CreateDirectory(directory);
        GamesPath = Path.Combine(directory, "games.csv");
        SummaryPath = Path.Combine(directory, "summary.csv");
        // A batch must never overwrite an earlier result.
        games = new StreamWriter(new FileStream(GamesPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        WriteRow(games, new[] { "batch_id", "game", "configuration", "layout_seed", "backend", "scasp_policy",
            "role_mapping", "outcome", "duration_seconds", "metrics_status" }.Concat(MetricHeaders()).ToArray());
        games.Flush();
    }

    public void Append(int game, int configuration, int seed, string outcome, double seconds, ExperimentMetrics metrics)
    {
        if (game != runs.Count / 2 + 1 || configuration != runs.Count % 2 + 1 || game > requestedGames)
            throw new ArgumentException("Runs must be appended once in game/configuration order.");
        if (metrics == null) throw new ArgumentNullException(nameof(metrics));
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        metrics.Validate();
        var snapshot = metrics.Copy();
        var row = new List<object> { batchId, game, configuration, seed, backend, policy,
            "pending", outcome, seconds, Status(new[] { snapshot }) };
        foreach (var latency in Latencies) row.Add(latency(snapshot));
        foreach (var population in Populations)
        {
            var counts = population(snapshot);
            row.Add(counts.Total);
            row.Add(counts.Alive);
            row.Add(counts.Total.HasValue ? (object)WinPercentage(counts.Alive.Value, counts.Total.Value) : null);
        }
        WriteRow(games, row.ToArray());
        games.Flush(); // Completed games survive interruption of the batch.
        runs.Add((configuration, snapshot));
    }

    public void WriteSummary(string batchStatus)
    {
        using (var writer = new StreamWriter(SummaryPath, false, new UTF8Encoding(false)))
        {
            var header = new List<string> { "batch_id", "batch_status", "requested_games", "requested_runs",
                "configuration", "completed_runs", "backend", "scasp_policy", "role_mapping", "metrics_status" };
            header.AddRange(MetricHeaders());
            header.AddRange(LatencyNames.Select(name => name + "_measured_runs"));
            header.AddRange(PopulationNames.Select(name => name + "_measured_runs"));
            WriteRow(writer, header.ToArray());
            // Keep paired configurations independently available as well as the combined result.
            foreach (int configuration in new[] { 1, 2, 0 })
            {
                var data = runs.Where(run => configuration == 0 || run.Configuration == configuration).Select(run => run.Metrics).ToArray();
                var row = new List<object> { batchId, batchStatus, requestedGames, requestedGames * 2,
                    configuration == 0 ? "all" : configuration.ToString(CultureInfo.InvariantCulture),
                    data.Length, backend, policy, "pending", Status(data) };
                foreach (var latency in Latencies) row.Add(Mean(data.Select(latency)));
                foreach (var population in Populations)
                {
                    var values = data.Select(population).Where(value => value.Total.HasValue).ToArray();
                    long total = values.Sum(value => (long)value.Total.Value);
                    long alive = values.Sum(value => (long)value.Alive.Value);
                    row.Add(total);
                    row.Add(alive);
                    row.Add(WinPercentage(alive, total));
                }
                foreach (var latency in Latencies) row.Add(data.Count(value => latency(value).HasValue));
                foreach (var population in Populations) row.Add(data.Count(value => population(value).Total.HasValue));
                WriteRow(writer, row.ToArray());
            }
        }
    }

    public static double Mean(IEnumerable<double?> perGameAverages)
    {
        // Incremental mean avoids overflow from adding many large finite values.
        double mean = 0;
        long count = 0;
        foreach (double? value in perGameAverages)
            if (value.HasValue) mean += (value.Value - mean) / ++count;
        return mean;
    }

    public static double WinPercentage(long alive, long total) => total == 0 ? 0 : 100.0 * alive / total;

    private static IEnumerable<string> MetricHeaders()
    {
        foreach (string name in LatencyNames) yield return name;
        foreach (string name in PopulationNames)
        {
            yield return name + "_total";
            yield return name + "_alive";
            yield return name + "_win_percentage";
        }
    }

    private static string Status(ExperimentMetrics[] values)
    {
        int present = values.Sum(value => Latencies.Count(select => select(value).HasValue) +
            Populations.Count(select => select(value).Total.HasValue));
        return present == 0 ? "placeholder" : present == values.Length * 9 ? "measured" : "partial";
    }

    private static void WriteRow(TextWriter writer, params object[] values)
    {
        writer.WriteLine(string.Join(",", values.Select(value =>
        {
            string text = value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
        })));
    }

    public void Dispose() => games.Dispose();
}
