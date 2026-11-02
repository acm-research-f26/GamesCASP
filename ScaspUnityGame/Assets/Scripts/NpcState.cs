using System;

[Serializable]
public class NpcState
{
    // NPC health and game objectives
    public int health;
    public int levers_to_win;
    public int levers_pulled_count;

    // World state
    public NpcLeverState[] levers = Array.Empty<NpcLeverState>();
    public NpcZombieState[] zombies = Array.Empty<NpcZombieState>();
    public NpcBarricadeState[] barricades = Array.Empty<NpcBarricadeState>();

    // Half-zombie status, timer, and nearby human targets.
    // Times are game-time seconds (Time.time), so they pause with Time.timeScale.
    // bite_window_seconds lets Python reject a timer that disagrees with s(CASP).
    public bool half_zombie;
    public float current_time;
    public float bite_window_started_at;
    public float bite_window_seconds;
    public NpcHumanState[] humans = Array.Empty<NpcHumanState>();
}

[Serializable]
public class NpcLeverState
{
    public string id;
    public string state;
    public bool visible;
    public float distance;
    public float nearest_zombie_distance;
}

[Serializable]
public class NpcZombieState
{
    public string id;
    public float distance;
}

[Serializable]
public class NpcBarricadeState
{
    public string id;
    public bool visible;
    public float reinforcement;
    public float distance;
}

// Only what an observer can see. Infection status is deliberately absent.
[Serializable]
public class NpcHumanState
{
    public string id;
    public int health;
    public bool visible;
    public float distance;
}
