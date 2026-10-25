using UnityEngine;

// super simple for now, each fact name will connect to a boolean in the behavior tree blackboard
// every fact will either be reset or not based on its type
// to-do: how will it work for scasp?
public class Fact
{
    public string name;
    public FactType type;
    public string target; // UID of object?
    public bool value;

    public Fact (string name, FactType type, string target = "", bool value = true)
    {
        this.name = name;
        this.type = type;
        this.target = target;
        this.value = value;
    }
}
