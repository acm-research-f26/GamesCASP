using System;
using System.Collections.Generic;

// Which backend was controlling an NPC while it had a role
public enum ExperimentStrategy { Scasp, BehaviorTree }

// The two roles we keep track of for the experiment results
public enum ExperimentRole { Innocent, HalfZombie }

// Collects the real latency samples and role populations for ONE configuration run,
// then fills in the ExperimentMetrics placeholders when the run is over
// This measures how well each role survived, not which team won the whole match
// There is no UnityEngine in here so Verify-Experiments.ps1 can test it without Unity
public sealed class ExperimentMeasurements
{
    // How an NPC looked when the run ended, the gameplay side fills this in
    public struct NpcEndState
    {
        public string AgentId;
        public int Health;
        public bool IsHalfZombie;
        public bool BiteWindowExpired;

        public NpcEndState(string agentId, int health, bool isHalfZombie, bool biteWindowExpired)
        {
            AgentId = agentId;
            Health = health;
            IsHalfZombie = isHalfZombie;
            BiteWindowExpired = biteWindowExpired;
        }
    }

    // Every latency sample for this run in ms, kept separate per strategy and category
    private readonly List<double> scaspFactMs = new List<double>();
    private readonly List<double> scaspRoundTripMs = new List<double>();
    private readonly List<double> scaspQueryMs = new List<double>();
    private readonly List<double> behaviorTreeFactMs = new List<double>();
    private readonly List<double> behaviorTreeActionMs = new List<double>();

    // The NPC ids that were ever in each strategy and role
    // We use sets so the same NPC never gets counted twice
    private readonly Dictionary<(ExperimentStrategy, ExperimentRole), HashSet<string>> members =
        new Dictionary<(ExperimentStrategy, ExperimentRole), HashSet<string>>();

    // True once FillMetrics ran, after that nothing else can be added to this run
    // This is what stops a slow reply from an old game from changing its results
    public bool IsFinished { get; private set; }

    // How many samples each category got, mostly so the tests can check what was counted
    public int ScaspFactSampleCount => scaspFactMs.Count;
    public int ScaspRoundTripSampleCount => scaspRoundTripMs.Count;
    public int ScaspQuerySampleCount => scaspQueryMs.Count;
    public int BehaviorTreeFactSampleCount => behaviorTreeFactMs.Count;
    public int BehaviorTreeActionSampleCount => behaviorTreeActionMs.Count;

    // Each of these returns false when the sample was thrown away
    public bool AddScaspFactLatency(double ms) => AddSample(scaspFactMs, ms);
    public bool AddScaspRoundTripLatency(double ms) => AddSample(scaspRoundTripMs, ms);
    public bool AddScaspQueryLatency(double ms) => AddSample(scaspQueryMs, ms);
    public bool AddBehaviorTreeFactLatency(double ms) => AddSample(behaviorTreeFactMs, ms);
    public bool AddBehaviorTreeActionLatency(double ms) => AddSample(behaviorTreeActionMs, ms);

    // Only keep real measurements, a bad number means something went wrong so we skip it
    // Dont count this if the game already ended
    private bool AddSample(List<double> samples, double ms)
    {
        if (IsFinished || double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0)
            return false;

        samples.Add(ms);
        return true;
    }

    // Remember that this NPC was controlled by this strategy while it had this role
    // Calling it again for the same NPC does nothing, so repeated events are safe
    // Returns false if the run already finished
    public bool RecordRole(string agentId, ExperimentStrategy strategy, ExperimentRole role)
    {
        if (string.IsNullOrEmpty(agentId))
            throw new ArgumentException("Every NPC needs a stable agent id.", nameof(agentId));
        if (IsFinished)
            return false;

        Members(strategy, role).Add(agentId);
        return true;
    }

    // This is T(i) for one strategy and role
    public int TotalFor(ExperimentStrategy strategy, ExperimentRole role) => Members(strategy, role).Count;

    // Get the set for a strategy and role, and make it the first time we need it
    private HashSet<string> Members(ExperimentStrategy strategy, ExperimentRole role)
    {
        if (!members.TryGetValue((strategy, role), out HashSet<string> set))
            members[(strategy, role)] = set = new HashSet<string>();
        return set;
    }

    // Now we can store the results for this run in the existing metrics object
    public void FillMetrics(ExperimentMetrics metrics, IEnumerable<NpcEndState> endStates)
    {
        if (metrics == null) throw new ArgumentNullException(nameof(metrics));
        if (endStates == null) throw new ArgumentNullException(nameof(endStates));

        // Look up the final state of each NPC by its id
        var finalStates = new Dictionary<string, NpcEndState>();
        foreach (NpcEndState state in endStates)
        {
            if (string.IsNullOrEmpty(state.AgentId) || finalStates.ContainsKey(state.AgentId))
                throw new ArgumentException("End states need unique, nonempty agent ids.", nameof(endStates));
            finalStates[state.AgentId] = state;
        }

        // Save the average latency for this run, missing ones stay null
        metrics.ScaspFactLatencyMs = Average(scaspFactMs);
        metrics.ScaspActionRoundTripLatencyMs = Average(scaspRoundTripMs);
        metrics.ScaspActionQueryLatencyMs = Average(scaspQueryMs);
        metrics.BehaviorTreeFactLatencyMs = Average(behaviorTreeFactMs);
        metrics.BehaviorTreeActionLatencyMs = Average(behaviorTreeActionMs);

        // Save T(i) and A(i) for every strategy and role
        // A count of 0 here is real, it just means nobody had that role this run
        metrics.ScaspInnocents = Population(ExperimentStrategy.Scasp, ExperimentRole.Innocent, finalStates);
        metrics.ScaspHalfZombies = Population(ExperimentStrategy.Scasp, ExperimentRole.HalfZombie, finalStates);
        metrics.BehaviorTreeInnocents = Population(ExperimentStrategy.BehaviorTree, ExperimentRole.Innocent, finalStates);
        metrics.BehaviorTreeHalfZombies = Population(ExperimentStrategy.BehaviorTree, ExperimentRole.HalfZombie, finalStates);

        // The run is over, so lock it so late results cant change anything
        IsFinished = true;
    }

    // Count the total and the survivors for one strategy and role
    private ExperimentPopulation Population(ExperimentStrategy strategy, ExperimentRole role,
        Dictionary<string, NpcEndState> finalStates)
    {
        HashSet<string> set = Members(strategy, role);
        int alive = 0;
        foreach (string agentId in set)
        {
            // If an NPC is gone at the end we cant say it survived, so it just counts toward the total
            if (finalStates.TryGetValue(agentId, out NpcEndState state) && SurvivedAs(role, state))
                alive++;
        }
        return new ExperimentPopulation(set.Count, alive);
    }

    // Check if this NPC counts as a survivor for the role
    public static bool SurvivedAs(ExperimentRole role, NpcEndState state)
    {
        // Nobody survives with no health left
        if (state.Health <= 0)
            return false;

        // Innocents only survive if they never got infected
        // IsHalfZombie never goes back to false during a run so this means never infected
        if (role == ExperimentRole.Innocent)
            return !state.IsHalfZombie;

        // Half zombies lose when the bite window runs out, even if they are just frozen there
        return state.IsHalfZombie && !state.BiteWindowExpired;
    }

    // Average of the samples, or null if we never got one so it shows up as missing and not as 0
    public static double? Average(List<double> samples)
    {
        if (samples == null || samples.Count == 0)
            return null;

        double sum = 0;
        foreach (double sample in samples)
            sum += sample;
        return sum / samples.Count;
    }
}
