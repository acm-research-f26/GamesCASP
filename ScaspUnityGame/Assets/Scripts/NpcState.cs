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
