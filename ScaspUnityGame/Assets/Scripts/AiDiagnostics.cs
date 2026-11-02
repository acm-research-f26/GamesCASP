using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum AiTraceLevel { Off, Summary, Verbose }

// What each NPC's strategy asked for, received and executed, for both backends.
// Summary logs selection, connections and failures; Verbose also logs every
// request, response and executed action. Counters are kept at every level.
public static class AiDiagnostics
{
    public static AiTraceLevel Level = AiTraceLevel.Summary;

    public sealed class AgentStats
    {
        public string Agent;
        public string Backend;
        public string Policy = "";
        public int Requests, Responses, Executed, Stale, Errors, Timeouts, Skipped, Failures;
        public readonly List<float> RoundTripMs = new List<float>();
        public readonly List<float> SolverMs = new List<float>();
        public readonly List<float> DecisionMs = new List<float>();
        public readonly Dictionary<string, int> Actions = new Dictionary<string, int>();
        public string LastAction = "";

        public void CountAction(string action)
        {
            Actions.TryGetValue(action, out int count);
            Actions[action] = count + 1;
            LastAction = action;
        }
    }

    private static readonly Dictionary<string, AgentStats> stats = new Dictionary<string, AgentStats>();

    public static IReadOnlyCollection<AgentStats> All => stats.Values;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() => stats.Clear();

    public static AgentStats For(string agent)
    {
        if (!stats.TryGetValue(agent, out var entry))
            stats[agent] = entry = new AgentStats { Agent = agent };
        return entry;
    }

    public static void Log(string message, Object context = null)
    {
        if (Level >= AiTraceLevel.Summary)
            Debug.Log("[AI] " + message, context);
    }

    public static void Warn(string message, Object context = null)
    {
        if (Level >= AiTraceLevel.Summary)
            Debug.LogWarning("[AI] " + message, context);
    }

    public static void Trace(string message, Object context = null)
    {
        if (Level >= AiTraceLevel.Verbose)
            Debug.Log("[AI] " + message, context);
    }

    public static float Percentile(List<float> values, float p)
    {
        if (values.Count == 0)
            return 0f;
        var sorted = new List<float>(values);
        sorted.Sort();
        return sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    public static string Summary()
    {
        var text = new StringBuilder();
        foreach (AgentStats s in stats.Values)
        {
            text.Append($"{s.Agent} [{s.Backend}{(s.Policy.Length > 0 ? "/" + s.Policy : "")}] executed {s.Executed}");
            if (s.Backend == "scasp")
                text.Append($", requests {s.Requests}, responses {s.Responses}, skipped {s.Skipped}, stale {s.Stale}, errors {s.Errors}, timeouts {s.Timeouts}, " +
                            $"round trip p50 {Percentile(s.RoundTripMs, 0.5f):F0} ms / p95 {Percentile(s.RoundTripMs, 0.95f):F0} ms");
            else
                text.Append($", decision p50 {Percentile(s.DecisionMs, 0.5f):F3} ms / p95 {Percentile(s.DecisionMs, 0.95f):F3} ms");
            text.Append($", last {s.LastAction}\n");
        }
        return text.ToString();
    }
}
