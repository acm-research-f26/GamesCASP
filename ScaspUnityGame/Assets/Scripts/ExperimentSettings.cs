using System;
using System.Globalization;

[Serializable]
public sealed class ExperimentSettings
{
    public int GameCount = 100;
    public int Seed = 55;
    public int NpcCount = 1;
    // Real seconds: a stalled simulation must not hold up the entire batch.
    public float MaxGameSeconds = 120f;
    public NpcDecisionBackend Backend = NpcDecisionBackend.BehaviorTree;
    public ScaspPolicy Policy = ScaspPolicy.Reasoned;
    public string OutputDirectory = "";

    public void Validate()
    {
        if (GameCount <= 0 || GameCount > int.MaxValue / 2)
            throw new ArgumentOutOfRangeException(nameof(GameCount));
        if (NpcCount <= 0) throw new ArgumentOutOfRangeException(nameof(NpcCount));
        if (float.IsNaN(MaxGameSeconds) || float.IsInfinity(MaxGameSeconds) || MaxGameSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxGameSeconds));
        if (!Enum.IsDefined(typeof(NpcDecisionBackend), Backend) || !Enum.IsDefined(typeof(ScaspPolicy), Policy))
            throw new ArgumentException("Unknown experiment backend or policy.");
    }

    public static ExperimentSettings FromArguments(string[] args)
    {
        var settings = new ExperimentSettings();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key != "-experimentGames" && key != "-experimentSeed" && key != "-experimentNpcs" &&
                key != "-experimentTimeout" && key != "-experimentOutput" && key != "-aiBackend" && key != "-scaspPolicy")
                continue;
            if (++i >= args.Length) throw new ArgumentException("Missing value for " + key);
            string value = args[i];
            switch (key)
            {
                case "-experimentGames": settings.GameCount = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "-experimentSeed": settings.Seed = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "-experimentNpcs": settings.NpcCount = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "-experimentTimeout": settings.MaxGameSeconds = float.Parse(value, CultureInfo.InvariantCulture); break;
                case "-experimentOutput": settings.OutputDirectory = value; break;
                case "-aiBackend": settings.Backend = (NpcDecisionBackend)Enum.Parse(typeof(NpcDecisionBackend), value, true); break;
                case "-scaspPolicy": settings.Policy = (ScaspPolicy)Enum.Parse(typeof(ScaspPolicy), value, true); break;
            }
        }
        settings.Validate();
        return settings;
    }
}
