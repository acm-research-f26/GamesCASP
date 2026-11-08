using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// This same verifier runs from Unity or the standalone .NET check script.
public static class ExperimentMetricsVerification
{
    private static int checks;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Experiments/Verify CSV Calculations")]
#endif
    public static void Run()
    {
        checks = 0;
        Check(ExperimentCsv.Mean(Array.Empty<double?>()) == 0, "Empty averages are safe");
        Check(ExperimentCsv.Mean(new double?[] { null, 0, 10 }) == 5, "Missing values excluded; real zeros included");
        Check(ExperimentCsv.WinPercentage(0, 0) == 0, "Zero denominator is safe");
        Throws(() => new ExperimentPopulation(1, 2), "Alive cannot exceed total");
        Throws(() => new ExperimentPopulation(-1, 0), "Negative counts rejected");
        Throws(() => new ExperimentPopulation { Total = 1 }.Validate(), "Incomplete counts rejected");
        Throws(() => new ExperimentMetrics { ScaspFactLatencyMs = double.NaN }.Validate(), "NaN rejected");
        Throws(() => new ExperimentMetrics { BehaviorTreeFactLatencyMs = double.PositiveInfinity }.Validate(), "Infinity rejected");
        Throws(() => new ExperimentMetrics { ScaspActionQueryLatencyMs = -1 }.Validate(), "Negative latency rejected");

        string root = Path.Combine(Path.GetTempPath(), "gamescasp_csv_" + Guid.NewGuid().ToString("N"));
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using (var csv = new ExperimentCsv(Path.Combine(root, "measured"), "test,\"batch\"", 2, "test", "test"))
            {
                var first = Metrics(10, 2, 1);
                csv.Append(1, 1, 55, "Won", 1.25, first);
                first.ScaspFactLatencyMs = 999; // A saved record must be a snapshot.
                first.ScaspInnocents = new ExperimentPopulation(100, 0);
                csv.Append(1, 2, 55, "TimedOut", 2.5, new ExperimentMetrics());
                csv.Append(2, 1, 56, "NoSurvivors", 3.5, Metrics(30, 8, 2));
                csv.Append(2, 2, 56, "Won", 4.5, Metrics(0, 0, 0));
                Throws(() => csv.Append(2, 2, 56, "Won", 1, Metrics(0, 0, 0)), "Duplicate/out-of-order runs rejected");
                csv.WriteSummary("completed");
                var games = Read(csv.GamesPath);
                var summaries = Read(csv.SummaryPath);
                Check(games.Count == 4 && summaries.Count == 3, "Every run and all three summary groups written");
                Check(games[0]["batch_id"] == "test,\"batch\"", "CSV quotes and commas escaped");
                Check(games[0]["duration_seconds"] == "1.25", "Invariant decimal formatting");
                Check(games[1]["scasp_fact_latency_ms"] == "" && games[1]["metrics_status"] == "placeholder", "Unmeasured run is visibly a placeholder");
                Check(games[3]["metrics_status"] == "measured", "Measured zeros are not placeholders");
                var all = summaries.Single(row => row["configuration"] == "all");
                var config1 = summaries.Single(row => row["configuration"] == "1");
                var config2 = summaries.Single(row => row["configuration"] == "2");
                foreach (string latency in new[] { "scasp_fact_latency_ms", "scasp_action_round_trip_latency_ms",
                    "scasp_action_query_latency_ms", "behavior_tree_fact_latency_ms", "behavior_tree_action_latency_ms" })
                {
                    Check(Math.Abs(Number(all[latency]) - 40.0 / 3) < 0.000001, latency + " averages per-game means");
                    Check(all[latency + "_measured_runs"] == "3", latency + " counts measured runs");
                    Check(Number(config1[latency]) == 20 && Number(config2[latency]) == 0, latency + " splits configurations");
                }
                foreach (string population in new[] { "scasp_innocents", "scasp_half_zombies", "behavior_tree_innocents", "behavior_tree_half_zombies" })
                {
                    Check(all[population + "_total"] == "10" && all[population + "_alive"] == "3", population + " sums populations");
                    Check(Number(all[population + "_win_percentage"]) == 30, population + " uses ratio of sums, not mean of rates");
                    Check(all[population + "_measured_runs"] == "3", population + " counts measured runs");
                }
            }
            using (var csv = new ExperimentCsv(Path.Combine(root, "empty"), "empty", 1, "test", "test"))
            {
                csv.WriteSummary("cancelled");
                Check(Read(csv.GamesPath).Count == 0, "Empty batch has header only");
                foreach (var row in Read(csv.SummaryPath))
                {
                    Check(row["completed_runs"] == "0" && row["metrics_status"] == "placeholder", "Empty summary is explicit");
                    Check(row["scasp_fact_latency_ms"] == "0" && row["scasp_innocents_win_percentage"] == "0", "Empty summary has safe zeros");
                    Check(row["batch_status"] == "cancelled", "Interrupted batch status exported");
                }
            }
            using (var csv = new ExperimentCsv(Path.Combine(root, "partial"), "partial", 1, "test", "test"))
            {
                csv.Append(1, 1, 55, "Won", 1, new ExperimentMetrics { ScaspFactLatencyMs = 5 });
                csv.WriteSummary("interrupted");
                var row = Read(csv.SummaryPath).Single(value => value["configuration"] == "all");
                Check(row["metrics_status"] == "partial" && row["completed_runs"] == "1", "Partial metrics and incomplete pair supported");
                Check(row["behavior_tree_fact_latency_ms_measured_runs"] == "0", "Missing categories tracked independently");
            }
            Throws(() => new ExperimentCsv(Path.Combine(root, "measured"), "duplicate", 2, "test", "test"), "Existing game CSV cannot be overwritten");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
#if UNITY_EDITOR
        UnityEngine.Debug.Log($"EXPERIMENT CSV VERIFIED: {checks} checks passed.");
#else
        Console.WriteLine($"EXPERIMENT CSV VERIFIED: {checks} checks passed.");
#endif
    }

    public static void WriteExample(string directory)
    {
        using (var csv = new ExperimentCsv(directory, "example_placeholders_only", 1, "BehaviorTree", "Reasoned"))
        {
            csv.Append(1, 1, 55, "ExampleOnly", 0, new ExperimentMetrics());
            csv.Append(1, 2, 55, "ExampleOnly", 0, new ExperimentMetrics());
            csv.WriteSummary("example_only");
        }
    }

    private static ExperimentMetrics Metrics(double latency, int total, int alive) => new ExperimentMetrics {
        ScaspFactLatencyMs = latency, ScaspActionRoundTripLatencyMs = latency, ScaspActionQueryLatencyMs = latency,
        BehaviorTreeFactLatencyMs = latency, BehaviorTreeActionLatencyMs = latency,
        ScaspInnocents = new ExperimentPopulation(total, alive), ScaspHalfZombies = new ExperimentPopulation(total, alive),
        BehaviorTreeInnocents = new ExperimentPopulation(total, alive), BehaviorTreeHalfZombies = new ExperimentPopulation(total, alive)
    };

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }
    private static void Throws(Action action, string description)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        catch (IOException) { checks++; return; }
        throw new Exception(description);
    }

    public static List<Dictionary<string, string>> Read(string path)
    {
        // Parse the CSV independently from the exporter, including escaped quotes.
        string[] lines;
        using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string[] header = ParseLine(lines[0]);
        return lines.Skip(1).Select(line => {
            string[] values = ParseLine(line);
            Check(values.Length == header.Length, "Every CSV row matches its header");
            return header.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => values[item.index]);
        }).ToList();
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (line[i] == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(line[i]);
        }
        fields.Add(field.ToString());
        return fields.ToArray();
    }

#if !UNITY_EDITOR
    public static void Main(string[] args)
    {
        Run();
        if (args.Length == 2 && args[0] == "--example-output") WriteExample(args[1]);
    }
#endif
}
